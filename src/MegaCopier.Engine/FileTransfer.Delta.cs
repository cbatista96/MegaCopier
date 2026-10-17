using System.Buffers;
using System.Buffers.Binary;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Copia diferencial: al reemplazar un archivo grande que ya está en el destino, solo se escriben los bloques que
/// cambiaron (como rsync --inplace). Es transaccional: antes de sobrescribir un bloque, el antiguo se guarda en un
/// registro de deshacer (.mcundo); si se cancela, se salta, falla o se va la luz, el archivo vuelve a su versión anterior.
/// </summary>
internal sealed partial class FileTransfer
{
    public const string UndoSuffix = ".mcundo";

    /// <summary>Por debajo de este tamaño no compensa: se copia entero.</summary>
    public const long DeltaThreshold = 16L * 1024 * 1024;

    private const int DeltaBlock = 1024 * 1024;

    /// <summary>
    /// Actualiza <paramref name="destinationPath"/> con el contenido de <paramref name="sourcePath"/> escribiendo solo lo
    /// distinto. Null si no se puede (el destino no existe): entonces se copia de la forma normal.
    /// </summary>
    private FileTransferResult? CopyDelta(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + PartialSuffix;
        string undoPath = destinationPath + UndoSuffix;

        // Restos de una actualización interrumpida (apagón): primero se vuelve a la versión anterior, luego se empieza.
        RestoreDelta(destinationPath);
        if (!File.Exists(destinationPath))
        {
            return null;
        }

        using var source = OpenFile(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var info = Win32.GetSourceInfo(source);
        long length = info.Identity.Length;
        IoHelpers.DeleteIfExists(destinationPath + JournalSuffix);
        IoHelpers.DeleteIfExists(partialPath);

        // El archivo antiguo pasa a ser el .mcpart (renombrado atómico): el nombre final solo vuelve completo.
        File.Move(destinationPath, partialPath);
        var result = new FileTransferResult { Delta = true };
        _current = result;
        byte[] fresh = ArrayPool<byte>.Shared.Rent(DeltaBlock);
        byte[] old = ArrayPool<byte>.Shared.Rent(DeltaBlock);
        try
        {
            var hasher = FileHasher.Create(_options.HashAlgorithm);
            using (var destination = OpenFile(partialPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, FileOptions.RandomAccess))
            using (var undo = new FileStream(undoPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough))
            {
                long oldLength = RandomAccess.GetLength(destination);
                Span<byte> header = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(header, oldLength);
                undo.Write(header); // para devolverle su tamaño al deshacer

                Span<byte> record = stackalloc byte[12];
                for (long offset = 0; offset < length; offset += DeltaBlock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _controller.WaitIfPaused(cancellationToken);
                    int count = (int)Math.Min(DeltaBlock, length - offset);
                    ReadExactly(source, fresh.AsSpan(0, count), offset);
                    hasher?.Append(fresh.AsSpan(0, count));

                    int existing = offset < oldLength ? (int)Math.Min(count, oldLength - offset) : 0;
                    if (existing > 0)
                    {
                        ReadExactly(destination, old.AsSpan(0, existing), offset);
                    }

                    if (existing == count && fresh.AsSpan(0, count).SequenceEqual(old.AsSpan(0, count)))
                    {
                        result.BytesUnchanged += count;
                    }
                    else
                    {
                        // Primero el bloque antiguo al registro de deshacer (escrito ya en el disco), después el nuevo.
                        if (existing > 0)
                        {
                            BinaryPrimitives.WriteInt64LittleEndian(record, offset);
                            BinaryPrimitives.WriteInt32LittleEndian(record[8..], existing);
                            undo.Write(record);
                            undo.Write(old, 0, existing);
                            undo.Flush(flushToDisk: true);
                        }

                        if (_options.MaxBytesPerSecond > 0)
                        {
                            _throttle?.WaitAsync(count, cancellationToken).GetAwaiter().GetResult();
                        }

                        RandomAccess.Write(destination, fresh.AsSpan(0, count), offset);
                        result.BytesTransferred += count;
                    }

                    _progress.AddTransferred(count);
                }

                if (oldLength != length)
                {
                    RandomAccess.SetLength(destination, length);
                }

                Win32.Flush(destination);
            }

            result.SourceHash = hasher?.Finish();
            if (_options.Verify && result.SourceHash is not null)
            {
                _progress.SetPhase(CopyPhase.Verifying);
                var check = FileHasher.Create(_options.HashAlgorithm)!;
                using (var copy = OpenFile(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan))
                {
                    for (long offset = 0; offset < length; offset += DeltaBlock)
                    {
                        int count = (int)Math.Min(DeltaBlock, length - offset);
                        ReadExactly(copy, fresh.AsSpan(0, count), offset);
                        check.Append(fresh.AsSpan(0, count));
                    }
                }

                result.DestinationHash = check.Finish();
                _progress.SetPhase(CopyPhase.Copying);
            }

            Finalize(sourcePath, partialPath, destinationPath, info, result);
            IoHelpers.DeleteIfExists(undoPath);
            return result;
        }
        catch
        {
            RestoreDelta(destinationPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(fresh);
            ArrayPool<byte>.Shared.Return(old);
        }
    }

    /// <summary>
    /// Deshace una actualización diferencial a medias: devuelve cada bloque guardado en el .mcundo (del último al
    /// primero), el tamaño original y el nombre final. Sin .mcundo no hace nada. Devuelve si había algo que deshacer.
    /// </summary>
    public static bool RestoreDelta(string destinationPath)
    {
        string partialPath = destinationPath + PartialSuffix;
        string undoPath = destinationPath + UndoSuffix;
        if (!File.Exists(undoPath))
        {
            return false;
        }

        if (File.Exists(partialPath))
        {
            byte[] data = File.ReadAllBytes(undoPath);
            var records = new List<(long Offset, int Start, int Length)>();
            long oldLength = data.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(data) : -1;
            for (int position = 8; position + 12 <= data.Length;)
            {
                long offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(position));
                int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position + 8));
                if (count < 0 || position + 12 + count > data.Length)
                {
                    break; // registro cortado por el apagón: ese bloque aún no se había sobrescrito
                }

                records.Add((offset, position + 12, count));
                position += 12 + count;
            }

            using (var file = File.OpenHandle(partialPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    RandomAccess.Write(file, data.AsSpan(records[i].Start, records[i].Length), records[i].Offset);
                }

                if (oldLength >= 0)
                {
                    RandomAccess.SetLength(file, oldLength);
                }

                Win32.Flush(file);
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }

        IoHelpers.DeleteIfExists(undoPath);
        return true;
    }

    private static void ReadExactly(Microsoft.Win32.SafeHandles.SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer[total..], offset + total);
            if (read == 0)
            {
                throw SourceChanged();
            }

            total += read;
        }
    }
}
