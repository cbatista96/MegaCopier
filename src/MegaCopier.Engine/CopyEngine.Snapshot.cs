using System.Diagnostics;
using MegaCopier.Engine.Backup;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>La última instantánea guardada (para mostrarla al terminar).</summary>
    public Snapshot? LastSnapshot { get; private set; }

    /// <summary>
    /// Copia de seguridad sin duplicados: guarda los orígenes como una instantánea nueva en el repositorio del destino.
    /// Lo que no cambió desde la anterior (mismo tamaño y fecha) ni se lee; de lo demás solo se guardan los trozos nuevos.
    /// </summary>
    private CopyJobResult RunSnapshot(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        var repository = new BackupRepository(request.Destination);
        _destinationRoot = repository.Root;
        var previous = repository.Snapshots().LastOrDefault()?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, SnapshotFile>(StringComparer.OrdinalIgnoreCase);
        string fakeRoot = Path.Combine(Path.GetTempPath(), "megacopier-snapshot-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, fakeRoot, _options.Filter, LinkHandling.FollowTarget);
        var relatives = new Dictionary<int, string>();
        var excluded = new HashSet<string>(request.Excluded, StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files.Where(f => !excluded.Contains(f.Source)))
        {
            string relative = Path.GetRelativePath(fakeRoot, file.Destination);
            relatives[Files.Add(file.Source, Path.Combine(repository.Root, relative), file.Length, file.LastWriteUtc).Id] = relative;
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, Texts.F("E_SnapshotStart", totalFiles, FormatBytes(totalBytes), repository.Root));
        if (_options.DryRun)
        {
            progress.Finish();
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        repository.Initialize();
        var snapshot = new Snapshot
        {
            Id = repository.NewId(DateTime.Now),
            CreatedUtc = DateTime.UtcNow,
            Sources = [.. request.Sources.Select(Path.GetFullPath)],
            Folders = [.. plan.Directories.Select(d => Path.GetRelativePath(fakeRoot, d.Destination))],
        };
        long stored = 0;
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, item.Length);
            string relative = relatives[item.Id];
            try
            {
                var info = new FileInfo(item.Source);
                if (previous.TryGetValue(relative, out var before) && before.Size == info.Length
                    && (before.ModifiedUtc - info.LastWriteTimeUtc).Duration() < TimeSpan.FromSeconds(1) && before.Chunks.All(repository.HasChunk))
                {
                    // Igual que en la copia anterior: se reutiliza sin leerlo.
                    snapshot.Files.Add(new SnapshotFile { Path = relative, Size = before.Size, ModifiedUtc = before.ModifiedUtc, Attributes = info.Attributes, Chunks = before.Chunks });
                    result.FilesSkipped++;
                    Files.Update(item, FileState.Skipped, Texts.Get("E_SnapshotUnchanged"));
                    progress.AddTransferred(item.Length);
                }
                else
                {
                    var entry = new SnapshotFile { Path = relative, Size = info.Length, ModifiedUtc = info.LastWriteTimeUtc, Attributes = info.Attributes };
                    using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                    {
                        foreach (var chunk in ContentChunker.Split(input))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            Controller.WaitIfPaused(cancellationToken);
                            var (hash, isNew) = repository.Store(chunk);
                            entry.Chunks.Add(hash);
                            if (isNew)
                            {
                                stored += chunk.Count;
                            }

                            progress.AddTransferred(chunk.Count);
                        }
                    }

                    snapshot.Files.Add(entry);
                    result.FilesCopied++;
                    result.BytesTransferred += item.Length;
                    Files.Update(item, FileState.Copied);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        repository.Save(snapshot);
        LastSnapshot = snapshot;
        var (snapshots, chunks) = repository.Prune(_options.VersionsDaily, _options.VersionsWeekly, _options.VersionsMonthly);
        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(LogLevel.Info, Texts.F("E_SnapshotDone", snapshot.Id, FormatBytes(snapshot.TotalBytes), FormatBytes(stored)));
        if (snapshots > 0)
        {
            Log(LogLevel.Info, Texts.F("E_SnapshotPruned", snapshots, chunks));
        }

        return result;
    }

    /// <summary>Restaurar una instantánea (<see cref="CopyOptions.SnapshotId"/>, o la última) en el destino.</summary>
    private CopyJobResult RunRestore(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        var repository = new BackupRepository(request.Sources[0]);
        var snapshot = repository.Find(_options.SnapshotId)
            ?? throw new CopyPlanException(Texts.F("E_SnapshotMissing", _options.SnapshotId.Length == 0 ? "latest" : _options.SnapshotId, repository.Root));
        _destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        var entries = new Dictionary<int, SnapshotFile>();
        foreach (var file in snapshot.Files)
        {
            entries[Files.Add(Path.Combine(repository.Root, snapshot.Id, file.Path), Path.Combine(_destinationRoot, file.Path), file.Size, file.ModifiedUtc).Id] = file;
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, Texts.F("E_RestoreStart", snapshot.Id, totalFiles, FormatBytes(totalBytes), _destinationRoot));
        foreach (string folder in snapshot.Folders)
        {
            Directory.CreateDirectory(Path.Combine(_destinationRoot, folder));
        }

        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Destination, item.Length);
            var entry = entries[item.Id];
            string partial = item.Destination + FileTransfer.PartialSuffix;
            try
            {
                var existing = new FileInfo(item.Destination);
                if (existing.Exists && existing.Length == entry.Size && (existing.LastWriteTimeUtc - entry.ModifiedUtc).Duration() < TimeSpan.FromSeconds(2))
                {
                    result.FilesSkipped++;
                    Files.Update(item, FileState.Skipped, Texts.Get("E_SkipIdentical"));
                    progress.AddTransferred(item.Length);
                    progress.EndFile();
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                var hasher = FileHasher.Create(_options.HashAlgorithm);
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    foreach (string hash in entry.Chunks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        byte[] data = repository.Load(hash); // comprueba el SHA-256 de cada trozo
                        hasher?.Append(data);
                        output.Write(data);
                        progress.AddTransferred(data.Length);
                    }

                    output.Flush(flushToDisk: true);
                }

                item.SourceHash = hasher?.Finish();
                File.SetLastWriteTimeUtc(partial, entry.ModifiedUtc);
                if (existing.Exists)
                {
                    File.SetAttributes(item.Destination, FileAttributes.Normal);
                }

                File.Move(partial, item.Destination, overwrite: true);
                File.SetAttributes(item.Destination, entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive));
                result.FilesCopied++;
                result.BytesTransferred += item.Length;
                Files.Update(item, FileState.Verified); // cada trozo se comprobó al leerlo
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                IO.IoHelpers.DeleteIfExists(partial);
                Fail(item, result, ex.Message, ex is InvalidDataException ? FileErrorKind.ChecksumMismatch : FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(result.Success ? LogLevel.Info : LogLevel.Warning,
            Texts.F("E_DoneCopy", result.Elapsed.ToString(@"hh\:mm\:ss"), result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;
    }
}
