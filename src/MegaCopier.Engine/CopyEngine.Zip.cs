using System.Diagnostics;
using System.Security.Cryptography;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// El ZIP en el que acaba la copia con <see cref="CopyOptions.Zip"/>: el destino si ya termina en .zip; si no, dentro
    /// de esa carpeta, con el nombre del origen (o "MegaCopier" si hay varios), sin pisar uno que ya exista salvo con
    /// "reemplazar".
    /// </summary>
    internal string ZipTarget(CopyJobRequest request)
    {
        string destination = Path.GetFullPath(request.Destination);
        if (destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        string extension = _options.ArchiveFormat == ArchiveFormat.SevenZip ? ".7z" : ".zip";

        string name = request.Sources.Count == 1
            ? (Directory.Exists(request.Sources[0]) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(request.Sources[0])) : Path.GetFileNameWithoutExtension(request.Sources[0]))
            : "MegaCopier";
        if (string.IsNullOrEmpty(name))
        {
            name = "MegaCopier"; // la raíz de una unidad
        }

        string path = Path.Combine(destination, name + extension);
        for (int n = 2; File.Exists(path) && _options.Conflict != ConflictPolicy.Overwrite; n++)
        {
            path = Path.Combine(destination, $"{name} ({n}){extension}");
        }

        return path;
    }

    /// <summary>
    /// Copia a un ZIP (con <see cref="CopyOptions.Encrypt"/>, cifrado con AES-256 y la contraseña de la copia). Se escribe
    /// en un .mcpart y solo se renombra al terminar, así que nunca queda un ZIP a medias con el nombre bueno. Con
    /// "verificar" se relee el ZIP entero (descifrando y descomprimiendo) y se compara con lo leído de cada original.
    /// Un corte obliga a empezar el ZIP de nuevo: no se puede seguir escribiendo uno sin terminar.
    /// </summary>
    private CopyJobResult RunZip(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        if (_options.Encrypt && _options.EncryptionPassword.Length == 0)
        {
            throw new CopyPlanException(Texts.Get("E_NeedPassword"));
        }

        var result = new CopyJobResult();
        string zipPath = ZipTarget(request);
        string root = Path.GetDirectoryName(zipPath)!;
        _destinationRoot = root;
        var plan = CopyPlanner.Build(request.Sources, root, _options.Filter, LinkHandling.FollowTarget);
        foreach (string warning in plan.Warnings)
        {
            Log(LogLevel.Warning, warning);
        }

        var excluded = new HashSet<string>(request.Excluded, StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files.Where(f => !excluded.Contains(f.Source)))
        {
            Files.Add(file.Source, Path.Combine(zipPath, Path.GetRelativePath(root, file.Destination)), file.Length, file.LastWriteUtc);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(_options.Verify ? totalBytes * 2 : totalBytes, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, Texts.F("E_ZipStart", totalFiles, FormatBytes(totalBytes), zipPath));
        if (_options.DryRun)
        {
            progress.Finish();
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        Directory.CreateDirectory(root);
        string partial = zipPath + FileTransfer.PartialSuffix;
        var added = new List<(TransferItem Item, string Name, string? Hash)>();
        try
        {
            using (var output = new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20))
            {
                string? password = _options.Encrypt ? _options.EncryptionPassword : null;
                IArchiveWriter writer = zipPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                    ? new SevenZipWriter(output, password)
                    : new ZipArchiveWriter(output, password);
                foreach (var directory in plan.Directories.OrderBy(d => d.Depth))
                {
                    writer.AddDirectory(Path.GetRelativePath(root, directory.Destination), Directory.GetLastWriteTimeUtc(directory.Source));
                }

                while (Files.TakeNext() is { } item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Controller.WaitIfPaused(cancellationToken);
                    progress.BeginFile(item.Source, item.Length);
                    string name = Path.GetRelativePath(zipPath, item.Destination);
                    try
                    {
                        var hasher = FileHasher.Create(_options.HashAlgorithm) ?? FileHasher.Create(FileHashAlgorithm.XxHash128);
                        using (var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                        {
                            writer.AddFile(name, source, File.GetLastWriteTimeUtc(item.Source), block =>
                            {
                                Controller.WaitIfPaused(cancellationToken);
                                hasher?.Append(block);
                                progress.AddTransferred(block.Length);
                            }, cancellationToken);
                        }

                        item.SourceHash = hasher?.Finish();
                        added.Add((item, name, item.SourceHash));
                        result.FilesCopied++;
                        result.BytesTransferred += item.Length;
                        Files.Update(item, FileState.Copied);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Fail(item, result, ex.Message, FileErrors.Classify(ex));
                    }

                    progress.EndFile();
                }

                writer.Finish();
                output.Flush(flushToDisk: true);

                if (_options.Verify)
                {
                    VerifyArchive(writer, added, progress, result, cancellationToken);
                }
            }

            if (File.Exists(zipPath))
            {
                File.SetAttributes(zipPath, FileAttributes.Normal);
            }

            File.Move(partial, zipPath, overwrite: true);
        }
        catch
        {
            IoHelpers.DeleteIfExists(partial);
            throw;
        }

        // Mover: solo se borra lo que quedó dentro del ZIP (y comprobado, si se verificó).
        if (_options.Move)
        {
            foreach (var (item, _, _) in added.Where(a => !result.Failures.Any(f => string.Equals(f.Source, a.Item.Source, StringComparison.OrdinalIgnoreCase))))
            {
                TryDeleteSource(item, result);
            }

            foreach (var directory in plan.Directories.OrderByDescending(d => d.Depth))
            {
                TryDeleteEmptyDirectory(directory.Source);
            }
        }

        ZipFile = zipPath;
        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(result.Success ? LogLevel.Info : LogLevel.Warning, Texts.F("E_ZipDone", zipPath, result.FilesCopied, result.Failures.Count));
        return result;
    }

    /// <summary>El ZIP creado por la última copia con <see cref="CopyOptions.Zip"/>.</summary>
    public string? ZipFile { get; private set; }

    private void VerifyArchive(IArchiveWriter archive, List<(TransferItem Item, string Name, string? Hash)> added, ProgressTracker progress, CopyJobResult result, CancellationToken cancellationToken)
    {
        progress.SetPhase(CopyPhase.Verifying);
        foreach (var (item, name, hash) in added)
        {
            progress.BeginFile(item.Destination, item.Length);
            try
            {
                var check = FileHasher.Create(_options.HashAlgorithm) ?? FileHasher.Create(FileHashAlgorithm.XxHash128)!;
                archive.ReadBack(name, block =>
                {
                    check.Append(block);
                    progress.AddTransferred(block.Length);
                }, cancellationToken);
                item.DestinationHash = check.Finish();
                if (item.DestinationHash != hash)
                {
                    throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
                }

                Files.Update(item, FileState.Verified);
            }
            catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException)
            {
                result.FilesCopied--;
                result.BytesTransferred -= item.Length;
                Fail(item, result, ex.Message, FileErrorKind.ChecksumMismatch);
            }

            progress.EndFile();
        }

        progress.SetPhase(CopyPhase.Copying);
    }
}
