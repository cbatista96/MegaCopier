using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

internal sealed partial class FileTransfer
{
    /// <summary>
    /// Copia cifrada: el destino (.mcenc) se escribe en un .mcpart y se renombra al terminar. Con "verificar", se descifra
    /// lo escrito (comprobando cada bloque) y se compara con el original. No se copian flujos alternativos ni permisos,
    /// que irían sin cifrar; sí la fecha.
    /// </summary>
    private FileTransferResult CopyEncrypted(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + PartialSuffix;
        var result = new FileTransferResult();
        _current = result;
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            using (var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                long length = source.Length;
                var hasher = FileHasher.Create(_options.HashAlgorithm);
                EncryptedFile.Encrypt(source, destination, length, _options.EncryptionPassword, _options.EncryptionSalt, (block, count) =>
                {
                    _controller.WaitIfPaused(cancellationToken);
                    hasher?.Append(block.AsSpan(0, count));
                    if (_options.MaxBytesPerSecond > 0)
                    {
                        _throttle?.WaitAsync(count, cancellationToken).GetAwaiter().GetResult();
                    }

                    _progress.AddTransferred(count);
                }, cancellationToken);
                destination.Flush(flushToDisk: true);
                result.BytesTransferred = length;
                result.SourceHash = hasher?.Finish();
            }

            if (_options.Verify && result.SourceHash is { } expected)
            {
                _progress.SetPhase(CopyPhase.Verifying);
                var check = FileHasher.Create(_options.HashAlgorithm)!;
                using (var written = new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                {
                    EncryptedFile.Decrypt(written, Stream.Null, _options.EncryptionPassword, (block, count) => check.Append(block.AsSpan(0, count)), cancellationToken);
                }

                result.DestinationHash = check.Finish();
                _progress.SetPhase(CopyPhase.Copying);
            }

            File.SetLastWriteTimeUtc(partialPath, File.GetLastWriteTimeUtc(sourcePath));
            if (File.Exists(destinationPath))
            {
                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }

            File.Move(partialPath, destinationPath, overwrite: true);
            return result;
        }
        catch
        {
            IoHelpers.DeleteIfExists(partialPath);
            throw;
        }
    }
}
