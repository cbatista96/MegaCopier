using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

internal sealed partial class FileTransfer
{
    /// <summary>
    /// Archivo cifrado con EFS y <see cref="CopyOptions.EfsRaw"/>: se copia sin descifrar (formato de copia de seguridad
    /// de EFS), así que el destino queda cifrado con las mismas claves. No se puede reanudar a medias. Con "verificar",
    /// se vuelve a exportar el destino y se compara con lo exportado del origen.
    /// </summary>
    private FileTransferResult CopyEfsRaw(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + PartialSuffix;
        var result = new FileTransferResult();
        _current = result;
        long length = new FileInfo(sourcePath).Length, counted = 0;
        var hasher = FileHasher.Create(_options.HashAlgorithm);
        try
        {
            IoHelpers.DeleteIfExists(partialPath);
            EfsRaw.Copy(sourcePath, partialPath, block =>
            {
                _controller.WaitIfPaused(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                hasher?.Append(block);
                if (_options.MaxBytesPerSecond > 0)
                {
                    _throttle?.WaitAsync(block.Length, cancellationToken).GetAwaiter().GetResult();
                }

                // El formato exportado lleva algo más que los datos (cabeceras y claves): la barra no pasa del tamaño.
                long add = Math.Min(block.Length, length - counted);
                counted += add;
                _progress.AddTransferred(add);
            });
            result.BytesTransferred = length;
            result.SourceHash = hasher?.Finish();

            if (_options.Verify && result.SourceHash is { } expected)
            {
                _progress.SetPhase(CopyPhase.Verifying);
                var check = FileHasher.Create(_options.HashAlgorithm)!;
                EfsRaw.Export(partialPath, block => check.Append(block));
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
