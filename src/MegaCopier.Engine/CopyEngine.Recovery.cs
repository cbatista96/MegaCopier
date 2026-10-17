using System.Diagnostics;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Al terminar una copia con <see cref="CopyOptions.RecoveryPercent"/>: se crean los datos de recuperación de lo
    /// copiado (en &lt;destino&gt;\.megacopier-recuperacion), leyendo cada archivo ya escrito.
    /// </summary>
    private void CreateRecoveryData(ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (_options.RecoveryPercent <= 0 || _options.DryRun || _options.Zip)
        {
            return;
        }

        var done = Files.Snapshot().Where(i => i.State is FileState.Copied or FileState.Verified && i.Length > 0 && File.Exists(i.Destination)).ToList();
        if (done.Count == 0)
        {
            return;
        }

        progress.SetPhase(CopyPhase.Verifying);
        int created = 0;
        foreach (var item in done)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RecoveryData.Create(item.Destination, RecoveryData.PathFor(_destinationRoot, item.Destination), _options.RecoveryPercent, null, cancellationToken);
                created++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log(LogLevel.Warning, Texts.F("E_RecoveryFailed", ex.Message), item.Destination);
            }
        }

        var folder = new DirectoryInfo(Path.Combine(_destinationRoot, RecoveryData.Folder));
        if (folder.Exists)
        {
            folder.Attributes |= FileAttributes.Hidden;
        }

        progress.SetPhase(CopyPhase.Copying);
        Log(LogLevel.Info, Texts.F("E_RecoveryCreated", created, _options.RecoveryPercent));
    }

    /// <summary>
    /// Dónde más está cada archivo del origen (<see cref="CopyOptions.AlternateSources"/>): cada sitio alternativo es el
    /// equivalente del origen, así que "\\nas1\fotos\2026\a.jpg" con la alternativa "\\nas2\fotos" es "\\nas2\fotos\2026\a.jpg".
    /// Con varios orígenes, cada alternativa contiene una carpeta con el nombre de cada uno.
    /// </summary>
    private Func<string, IReadOnlyList<string>>? AlternatesFor(CopyJobRequest request)
    {
        if (_options.AlternateSources.Count == 0 || request.Sources.Count == 0)
        {
            return null;
        }

        var roots = request.Sources.Select(s => Path.TrimEndingDirectorySeparator(Path.GetFullPath(s))).ToList();
        var alternates = _options.AlternateSources.Select(a => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a))).ToList();
        bool single = roots.Count == 1;
        return path =>
        {
            foreach (string root in roots)
            {
                string? relative = string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ? ""
                    : path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path[(root.Length + 1)..]
                    : null;
                if (relative is not null)
                {
                    return [.. alternates.Select(alternate => single ? Path.Combine(alternate, relative) : Path.Combine(alternate, Path.GetFileName(root), relative))
                        .Select(Path.TrimEndingDirectorySeparator)];
                }
            }

            return [];
        };
    }

    /// <summary>El manifiesto firmado de la última copia en modo forense.</summary>
    public string? LastManifest { get; private set; }

    /// <summary>Modo forense: al terminar, el manifiesto firmado con la SHA-256 de cada archivo copiado y comprobado.</summary>
    private void WriteForensicManifest(CopyJobRequest request, DateTime startedUtc)
    {
        if (!_options.Forensic || _options.DryRun)
        {
            return;
        }

        var manifest = new Forensics.Manifest
        {
            Machine = Environment.MachineName,
            User = Environment.UserDomainName + "\\" + Environment.UserName,
            StartedUtc = startedUtc,
            FinishedUtc = DateTime.UtcNow,
            Sources = [.. request.Sources.Select(Path.GetFullPath)],
            Destination = _destinationRoot,
        };
        foreach (var item in Files.Snapshot().Where(i => i.State == FileState.Verified && i.SourceHash is not null && i.DestinationHash is not null))
        {
            manifest.Files.Add(new Forensics.ManifestEntry
            {
                Path = Path.GetRelativePath(_destinationRoot, item.Destination),
                Source = item.Source,
                Size = item.Length,
                ModifiedUtc = item.LastWriteUtc,
                Sha256Source = item.SourceHash!.ToLowerInvariant(),
                Sha256Copy = item.DestinationHash!.ToLowerInvariant(),
            });
        }

        string path = Path.Combine(_destinationRoot, $"{Forensics.ForensicManifest.FilePrefix}{DateTime.Now:yyyy-MM-dd HHmmss}.json");
        LastManifest = Forensics.ForensicManifest.Write(path, manifest);
        Log(LogLevel.Info, Texts.F("E_ManifestWritten", path, manifest.Files.Count, manifest.KeyFingerprint));
    }

    /// <summary>
    /// Reparar: revisa los archivos que tienen datos de recuperación y reconstruye los bloques dañados o ilegibles.
    /// </summary>
    private CopyJobResult RunRepair(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        var recoveries = new Dictionary<int, string>();
        foreach (string source in request.Sources)
        {
            string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            string root = RecoveryData.FindRoot(selected) ?? throw new CopyPlanException(Texts.F("E_NoRecoveryData", selected));
            string folder = Path.Combine(root, RecoveryData.Folder);
            foreach (string recovery in Directory.EnumerateFiles(folder, "*" + RecoveryData.Extension, SearchOption.AllDirectories))
            {
                string file = Path.Combine(root, Path.GetRelativePath(folder, recovery)[..^RecoveryData.Extension.Length]);
                bool inside = string.Equals(file, selected, StringComparison.OrdinalIgnoreCase)
                    || file.StartsWith(selected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(selected, root, StringComparison.OrdinalIgnoreCase);
                if (inside && File.Exists(file))
                {
                    var info = new FileInfo(file);
                    recoveries[Files.Add(file, file, info.Length, info.LastWriteTimeUtc).Id] = recovery;
                }
            }
        }

        if (recoveries.Count == 0)
        {
            throw new CopyPlanException(Texts.F("E_NoRecoveryData", string.Join(", ", request.Sources)));
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        progress.SetPhase(CopyPhase.Verifying);
        _progress = progress;
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, item.Length);
            try
            {
                var (outcome, blocks) = RecoveryData.Repair(item.Source, recoveries[item.Id], read => progress.AddTransferred(read), cancellationToken);
                switch (outcome)
                {
                    case RepairOutcome.Intact:
                        result.FilesSkipped++;
                        Files.Update(item, FileState.Verified, Texts.Get("E_RepairIntact"));
                        break;
                    case RepairOutcome.Repaired:
                        result.FilesCopied++;
                        result.BytesTransferred += item.Length;
                        Files.Update(item, FileState.Copied, Texts.F("E_RepairDone", blocks));
                        Log(LogLevel.Warning, Texts.F("E_RepairDone", blocks), item.Source);
                        break;
                    case RepairOutcome.Changed:
                        result.FilesSkipped++;
                        Files.Update(item, FileState.Skipped, Texts.Get("E_RepairChanged"));
                        break;
                    default:
                        Fail(item, result, Texts.Get("E_RepairImpossible"), FileErrorKind.ChecksumMismatch);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(result.Success ? LogLevel.Info : LogLevel.Warning, Texts.F("E_RepairSummary", result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;
    }
}
