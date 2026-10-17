using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.IO;

/// <summary>Una entrada del directorio central de un ZIP.</summary>
internal sealed record ZipEntryInfo(string Name, ushort Method, ushort ActualMethod, bool Encrypted, uint Crc, long Size, long CompressedSize, long Offset)
{
    public bool IsDirectory => Name.EndsWith('/');
}

/// <summary>
/// Lee lo que escribe <see cref="ZipArchiveWriter"/> (y cualquier ZIP sin cifrar o con AES de WinZip): para verificar la
/// copia releyendo el ZIP entero, descifrando y descomprimiendo cada archivo.
/// </summary>
internal static class ZipArchiveReader
{
    public static List<ZipEntryInfo> Entries(Stream zip)
    {
        // Fin del directorio central: en los últimos 64 KB (más el comentario, que puede llegar a 64 KB).
        int tail = (int)Math.Min(zip.Length, 22 + 65535);
        byte[] end = new byte[tail];
        zip.Position = zip.Length - tail;
        zip.ReadExactly(end);
        int eocd = -1;
        for (int i = tail - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(i)) == 0x06054b50)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
        }

        long count = BinaryPrimitives.ReadUInt16LittleEndian(end.AsSpan(eocd + 10));
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(eocd + 16));
        if (eocd >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(eocd - 20)) == 0x07064b50)
        {
            long record = BinaryPrimitives.ReadInt64LittleEndian(end.AsSpan(eocd - 20 + 8));
            byte[] zip64 = new byte[56];
            zip.Position = record;
            zip.ReadExactly(zip64);
            count = BinaryPrimitives.ReadInt64LittleEndian(zip64.AsSpan(32));
            directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(zip64.AsSpan(48));
        }

        zip.Position = directoryOffset;
        using var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);
        var entries = new List<ZipEntryInfo>();
        for (long n = 0; n < count; n++)
        {
            if (reader.ReadUInt32() != 0x02014b50)
            {
                throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
            }

            reader.ReadUInt16();
            reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();
            ushort method = reader.ReadUInt16();
            reader.ReadUInt32(); // fecha y hora
            uint crc = reader.ReadUInt32();
            long compressed = reader.ReadUInt32();
            long size = reader.ReadUInt32();
            int nameLength = reader.ReadUInt16(), extraLength = reader.ReadUInt16(), commentLength = reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            long offset = reader.ReadUInt32();
            byte[] nameBytes = reader.ReadBytes(nameLength);
            string name = (flags & (1 << 11)) != 0 ? Encoding.UTF8.GetString(nameBytes) : Encoding.Latin1.GetString(nameBytes);
            byte[] extra = reader.ReadBytes(extraLength);
            reader.ReadBytes(commentLength);
            ushort actual = method;
            for (int i = 0; i + 4 <= extra.Length;)
            {
                ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(i));
                int length = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(i + 2));
                var data = extra.AsSpan(i + 4, Math.Min(length, extra.Length - i - 4));
                if (id == 0x0001)
                {
                    int p = 0;
                    if (size == uint.MaxValue) { size = BinaryPrimitives.ReadInt64LittleEndian(data[p..]); p += 8; }
                    if (compressed == uint.MaxValue) { compressed = BinaryPrimitives.ReadInt64LittleEndian(data[p..]); p += 8; }
                    if (offset == uint.MaxValue) { offset = BinaryPrimitives.ReadInt64LittleEndian(data[p..]); }
                }
                else if (id == 0x9901 && data.Length >= 7)
                {
                    actual = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
                }

                i += 4 + length;
            }

            entries.Add(new ZipEntryInfo(name, method, actual, (flags & 1) != 0, crc, size, compressed, offset));
        }

        return entries;
    }

    /// <summary>
    /// Pasa a <paramref name="onData"/> el contenido original de la entrada. Lanza <see cref="CryptographicException"/>
    /// si la contraseña no es la buena o el contenido cifrado se alteró, e <see cref="InvalidDataException"/> si no cuadra.
    /// </summary>
    public static void Extract(Stream zip, ZipEntryInfo entry, string? password, Action<ReadOnlySpan<byte>> onData, CancellationToken cancellationToken = default)
    {
        zip.Position = entry.Offset;
        byte[] header = new byte[30];
        zip.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x04034b50)
        {
            throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
        }

        zip.Position = entry.Offset + 30 + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        long dataLength = entry.CompressedSize;
        long dataStart = zip.Position;
        Stream raw = new SubStream(zip, zip.Position, dataLength);
        AesCtrHmacStream? aes = null;
        if (entry.Encrypted)
        {
            if (entry.Method != ZipArchiveWriter.MethodAes || string.IsNullOrEmpty(password))
            {
                throw new CryptographicException(Texts.Get("E_WrongPassword"));
            }

            byte[] salt = new byte[ZipArchiveWriter.AesSaltLength];
            byte[] verifier = new byte[2];
            zip.ReadExactly(salt);
            zip.ReadExactly(verifier);
            var (key, hmacKey, expected) = ZipArchiveWriter.DeriveKeys(password, salt);
            if (!expected.AsSpan().SequenceEqual(verifier))
            {
                throw new CryptographicException(Texts.Get("E_WrongPassword"));
            }

            long cipherLength = dataLength - salt.Length - verifier.Length - ZipArchiveWriter.AesAuthLength;
            aes = new AesCtrHmacStream(new SubStream(zip, zip.Position, cipherLength), key, hmacKey, decrypt: true);
            raw = aes;
        }

        using (raw)
        {
            Stream content = entry.ActualMethod switch
            {
                ZipArchiveWriter.MethodDeflate => new DeflateStream(raw, CompressionMode.Decompress, leaveOpen: true),
                ZipArchiveWriter.MethodStored => raw,
                _ => throw new InvalidDataException(Texts.Get("E_ZipDamaged")),
            };
            byte[] buffer = new byte[1 << 20];
            long total = 0;
            var crc = new System.IO.Hashing.Crc32();
            int read;
            while ((read = content.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, read));
                onData(buffer.AsSpan(0, read));
                total += read;
            }

            if (content != raw)
            {
                content.Dispose();
            }

            if (aes is not null)
            {
                // Lo que queda del cifrado (si el descompresor no lo leyó todo) también cuenta para el HMAC.
                while (aes.Read(buffer) > 0)
                {
                }

                byte[] stored = new byte[ZipArchiveWriter.AesAuthLength];
                zip.Position = dataStart + dataLength - ZipArchiveWriter.AesAuthLength; // justo tras lo cifrado
                zip.ReadExactly(stored);
                if (!CryptographicOperations.FixedTimeEquals(stored, aes.AuthenticationCode()))
                {
                    throw new CryptographicException(Texts.Get("E_WrongPassword"));
                }
            }
            else if (crc.GetCurrentHashAsUInt32() != entry.Crc)
            {
                throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
            }

            if (total != entry.Size)
            {
                throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
            }
        }
    }

    /// <summary>Un tramo de otro flujo (solo lectura), para no leer más allá de los datos de una entrada.</summary>
    internal sealed class SubStream(Stream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, length - _position);
            if (count <= 0)
            {
                return 0;
            }

            inner.Position = start + _position;
            int read = inner.Read(buffer[..count]);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
