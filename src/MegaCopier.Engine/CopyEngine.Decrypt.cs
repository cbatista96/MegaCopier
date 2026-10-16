using System.Diagnostics;
using System.Security.Cryptography;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Descifrar: los .mcenc de los orígenes vuelven a su forma original en el destino (con su estructura de carpetas y
    /// su fecha). Si la contraseña no es la buena o un archivo se dañó o se manipuló, ese archivo falla y no se escribe.
    /// </summary>
    private CopyJobResult RunDecrypt(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        _destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        var plan = CopyPlanner.Build(request.Sources, request.Destination, _options.Filter, LinkHandling.FollowTarget);
        foreach (var file in plan.Files.Where(f => f.Source.EndsWith(EncryptedFile.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            long length = EncryptedFile.OriginalLength(file.Source);
            Files.Add(file.Source, file.Destination[..^EncryptedFile.Extension.Length], Math.Max(0, length), file.LastWriteUtc);
        }

        if (Files.Count == 0)
        {
            throw new CopyPlanException("No hay archivos cifrados por MegaCopier (.mcenc) en lo elegido.");
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        Log(LogLevel.Info, $"Descifrar {totalFiles} archivos ({FormatBytes(totalBytes)}) en {request.Destination}.");

        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Destination, item.Length);
            string partial = item.Destination + FileTransfer.PartialSuffix;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                var hasher = FileHasher.Create(_options.HashAlgorithm);
                using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    EncryptedFile.Decrypt(input, output, _options.EncryptionPassword, (block, count) =>
                    {
                        hasher?.Append(block.AsSpan(0, count));
                        progress.AddTransferred(count);
                    }, cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                item.SourceHash = hasher?.Finish();
                File.SetLastWriteTimeUtc(partial, File.GetLastWriteTimeUtc(item.Source));
                File.Move(partial, item.Destination, overwrite: _options.Conflict is ConflictPolicy.Overwrite);
                result.FilesCopied++;
                result.BytesTransferred += item.Length;
                Files.Update(item, FileState.Verified); // cada bloque se comprobó al descifrarlo
            }
            catch (CryptographicException)
            {
                IoHelpers.DeleteIfExists(partial);
                Fail(item, result, "Contraseña incorrecta, o el archivo cifrado está dañado o se manipuló.", FileErrorKind.ChecksumMismatch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                IoHelpers.DeleteIfExists(partial);
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        return result;
    }
}
