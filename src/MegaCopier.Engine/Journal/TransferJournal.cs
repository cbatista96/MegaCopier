using System.Buffers.Binary;
using System.IO.Hashing;
using MegaCopier.Engine.Native;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Journal;

/// <summary>Un bloque ya escrito, vaciado al disco y confirmado.</summary>
internal readonly record struct ChunkRecord(long Index, int Length, UInt128 Hash);

/// <summary>
/// Registro persistente de una transferencia de un archivo grande.
///
/// Formato (little-endian):
///   Cabecera de 512 bytes: "MCJ1", versión, tamaño de bloque, identidad del origen, CRC32 en el byte 508.
///   Registros de 32 bytes: índice (8) + longitud (4) + hash XxHash128 (16) + CRC32 (4).
///
/// Garantías:
///   - Un registro solo se añade después de que sus datos se vaciaron al disco.
///   - Cada registro lleva su propio CRC: si el apagón deja un registro a medio escribir, se descarta.
///   - Los registros de 32 bytes nunca cruzan un sector de 512, así que una escritura no puede romper dos.
/// </summary>
internal sealed class TransferJournal : IDisposable
{
    public const int HeaderSize = 512;
    public const int RecordSize = 32;
    private const ushort FormatVersion = 1;

    private static ReadOnlySpan<byte> Magic => "MCJ1"u8;

    private readonly SafeFileHandle _handle;
    private int _count;

    private TransferJournal(string path, SafeFileHandle handle, long chunkSize, SourceIdentity source, List<ChunkRecord> chunks)
    {
        Path = path;
        _handle = handle;
        ChunkSize = chunkSize;
        Source = source;
        LoadedChunks = chunks;
        _count = chunks.Count;
    }

    public string Path { get; }

    public long ChunkSize { get; }

    public SourceIdentity Source { get; }

    /// <summary>Bloques confirmados que se encontraron al abrir el journal.</summary>
    public IReadOnlyList<ChunkRecord> LoadedChunks { get; }

    public int Count => _count;

    public static TransferJournal Create(string path, long chunkSize, SourceIdentity source)
    {
        var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, FileOptions.WriteThrough);
        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            header.Clear();
            Magic.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FormatVersion);
            BinaryPrimitives.WriteInt64LittleEndian(header[8..], chunkSize);
            BinaryPrimitives.WriteInt64LittleEndian(header[16..], source.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header[24..], source.LastWriteFileTime);
            BinaryPrimitives.WriteUInt32LittleEndian(header[32..], source.VolumeSerial);
            BinaryPrimitives.WriteUInt64LittleEndian(header[40..], source.FileIndex);
            BinaryPrimitives.WriteInt64LittleEndian(header[48..], DateTime.UtcNow.ToFileTimeUtc());
            BinaryPrimitives.WriteUInt32LittleEndian(header[508..], Crc32.HashToUInt32(header[..508]));

            RandomAccess.Write(handle, header, 0);
            Win32.Flush(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return new TransferJournal(path, handle, chunkSize, source, []);
    }

    /// <summary>Abre un journal existente. Devuelve null si no existe o su cabecera está dañada.</summary>
    public static TransferJournal? TryOpen(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, FileOptions.WriteThrough);
        try
        {
            long length = RandomAccess.GetLength(handle);
            if (length < HeaderSize)
            {
                handle.Dispose();
                return null;
            }

            Span<byte> header = stackalloc byte[HeaderSize];
            if (ReadExactly(handle, header, 0) != HeaderSize || !TryParseHeader(header, out long chunkSize, out var source))
            {
                handle.Dispose();
                return null;
            }

            long recordCount = (length - HeaderSize) / RecordSize;
            var data = new byte[checked((int)(recordCount * RecordSize))];
            int read = ReadExactly(handle, data, HeaderSize);
            recordCount = read / RecordSize;

            var chunks = new List<ChunkRecord>((int)recordCount);
            for (int i = 0; i < recordCount; i++)
            {
                if (!TryParseRecord(data.AsSpan(i * RecordSize, RecordSize), out var record) || record.Index != i)
                {
                    break;
                }

                chunks.Add(record);
            }

            // Recorta cualquier resto inválido (registro roto por el apagón) para seguir añadiendo limpio.
            long validLength = HeaderSize + ((long)chunks.Count * RecordSize);
            if (length != validLength)
            {
                RandomAccess.SetLength(handle, validLength);
                Win32.Flush(handle);
            }

            return new TransferJournal(path, handle, chunkSize, source, chunks);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Append(ChunkRecord record)
    {
        if (record.Index != _count)
        {
            throw new InvalidOperationException($"Se esperaba el bloque {_count} y llegó el {record.Index}.");
        }

        Span<byte> buffer = stackalloc byte[RecordSize];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, record.Index);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], record.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[12..], (ulong)record.Hash);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[20..], (ulong)(record.Hash >> 64));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], Crc32.HashToUInt32(buffer[..28]));

        RandomAccess.Write(_handle, buffer, HeaderSize + (record.Index * RecordSize));
        Win32.Flush(_handle);
        _count++;
    }

    /// <summary>Descarta los bloques a partir de <paramref name="count"/> (por ejemplo, si no pasaron la verificación).</summary>
    public void TruncateTo(int count)
    {
        RandomAccess.SetLength(_handle, HeaderSize + ((long)count * RecordSize));
        Win32.Flush(_handle);
        _count = count;
    }

    public void Delete()
    {
        Dispose();
        File.Delete(Path);
    }

    public void Dispose() => _handle.Dispose();

    private static bool TryParseHeader(ReadOnlySpan<byte> header, out long chunkSize, out SourceIdentity source)
    {
        chunkSize = 0;
        source = default;

        if (!header[..4].SequenceEqual(Magic)
            || BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != FormatVersion
            || BinaryPrimitives.ReadUInt32LittleEndian(header[508..]) != Crc32.HashToUInt32(header[..508]))
        {
            return false;
        }

        chunkSize = BinaryPrimitives.ReadInt64LittleEndian(header[8..]);
        source = new SourceIdentity(
            BinaryPrimitives.ReadInt64LittleEndian(header[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(header[24..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[32..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[40..]));

        return chunkSize > 0;
    }

    private static bool TryParseRecord(ReadOnlySpan<byte> data, out ChunkRecord record)
    {
        record = default;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[28..]) != Crc32.HashToUInt32(data[..28]))
        {
            return false;
        }

        ulong low = BinaryPrimitives.ReadUInt64LittleEndian(data[12..]);
        ulong high = BinaryPrimitives.ReadUInt64LittleEndian(data[20..]);
        record = new ChunkRecord(
            BinaryPrimitives.ReadInt64LittleEndian(data),
            BinaryPrimitives.ReadInt32LittleEndian(data[8..]),
            new UInt128(high, low));
        return true;
    }

    private static int ReadExactly(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer[total..], offset + total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
