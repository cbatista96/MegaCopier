using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.IO;

/// <summary>
/// Escribe un archivo .7z (el formato de 7-Zip). Cada archivo va en su propio bloque, comprimido con Deflate (o sin
/// comprimir si ya lo está), así que un archivo dañado no arrastra a los demás. Con contraseña, cada bloque va cifrado
/// con AES-256 (como 7-Zip) y también la lista de archivos: sin la contraseña ni siquiera se ven los nombres, cosa que
/// el ZIP no permite.
/// </summary>
internal sealed class SevenZipWriter : IArchiveWriter
{
    private const int SignatureHeaderSize = 32;
    private const int CyclesPower = 19; // como 7-Zip: 2^19 rondas de SHA-256 para obtener la clave

    private static readonly byte[] Signature = [(byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] DeflateId = [0x04, 0x01, 0x08];
    private static readonly byte[] CopyId = [0x00];
    private static readonly byte[] AesId = [0x06, 0xF1, 0x07, 0x01];

    private readonly Stream _output;
    private readonly byte[]? _key;
    private readonly CompressionLevel _level;
    private readonly List<Entry> _entries = [];

    public SevenZipWriter(Stream output, string? password, CompressionLevel level = CompressionLevel.Fastest)
    {
        _output = output;
        _level = level;
        _key = string.IsNullOrEmpty(password) ? null : DeriveKey(password);
        _output.Write(new byte[SignatureHeaderSize]); // se rellena al terminar
    }

    public int Count => _entries.Count;

    /// <summary>La clave de 7-Zip: SHA-256 de 2^19 repeticiones de (contraseña en UTF-16 + contador de 64 bits).</summary>
    internal static byte[] DeriveKey(string password)
    {
        byte[] secret = Encoding.Unicode.GetBytes(password);
        byte[] block = new byte[secret.Length + 8];
        secret.CopyTo(block, 0);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long round = 0; round < 1L << CyclesPower; round++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(block.AsSpan(secret.Length), round);
            sha.AppendData(block);
        }

        return sha.GetHashAndReset();
    }

    public void AddDirectory(string name, DateTime lastWriteUtc) =>
        _entries.Add(new Entry(name.Replace('/', '\\').TrimEnd('\\'), lastWriteUtc, IsDirectory: true));

    /// <summary>Añade un archivo; si algo falla a medias, el .7z vuelve a quedar como antes de él.</summary>
    public void AddFile(string name, Stream source, DateTime lastWriteUtc, Action<ReadOnlySpan<byte>> onRead, CancellationToken cancellationToken)
    {
        var entry = new Entry(name.Replace('/', '\\'), lastWriteUtc, IsDirectory: false);
        long start = _output.Position;
        try
        {
            bool store = ZipArchiveWriter.IsCompressed(name);
            var crc = new Crc32();
            byte[] buffer = new byte[1 << 20];
            long compressedSize = 0;
            byte[]? iv = _key is null ? null : RandomNumberGenerator.GetBytes(16);
            Stream sink = _output;
            CountingStream? counting = null;
            CryptoStream? encryptor = null;
            Aes? aes = null;
            if (_key is not null)
            {
                aes = Aes.Create();
                aes.Key = _key;
                aes.IV = iv!;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.Zeros; // 7-Zip rellena con ceros hasta 16 bytes
                encryptor = new CryptoStream(_output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                sink = encryptor;
            }

            counting = new CountingStream(sink);
            using (var compressor = store ? null : new DeflateStream(counting, _level, leaveOpen: true))
            {
                Stream target = compressor ?? (Stream)counting;
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var block = buffer.AsSpan(0, read);
                    crc.Append(block);
                    onRead(block);
                    target.Write(block);
                    entry.Size += read;
                }
            }

            compressedSize = counting.Written;
            encryptor?.FlushFinalBlock();
            encryptor?.Dispose();
            aes?.Dispose();

            entry.Crc = crc.GetCurrentHashAsUInt32();
            if (entry.Size == 0)
            {
                // Vacío: en 7z no lleva datos (solo el nombre).
                _output.SetLength(start);
                _output.Position = start;
            }
            else
            {
                entry.Stored = store;
                entry.CodedSize = compressedSize;
                entry.Offset = start;
                entry.PackSize = _output.Position - start;
                entry.Iv = iv;
            }

            _entries.Add(entry);
        }
        catch
        {
            _output.SetLength(start);
            _output.Position = start;
            throw;
        }
    }

    /// <summary>Relee del disco el bloque de un archivo, lo descifra y lo descomprime, y comprueba su CRC.</summary>
    public void ReadBack(string name, Action<ReadOnlySpan<byte>> onData, CancellationToken cancellationToken)
    {
        var entry = _entries.FirstOrDefault(e => !e.IsDirectory && e.Name == name.Replace('/', '\\'))
            ?? throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
        if (entry.Size == 0)
        {
            return;
        }

        Stream data = new ZipArchiveReader.SubStream(_output, entry.Offset, entry.PackSize);
        Aes? aes = null;
        if (_key is not null)
        {
            aes = Aes.Create();
            aes.Key = _key;
            aes.IV = entry.Iv!;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None; // el relleno de ceros se quita por tamaño
            data = new LimitedStream(new CryptoStream(data, aes.CreateDecryptor(), CryptoStreamMode.Read), entry.CodedSize);
        }

        using (aes)
        using (data)
        using (var content = entry.Stored ? data : new DeflateStream(data, CompressionMode.Decompress, leaveOpen: true))
        {
            var crc = new Crc32();
            byte[] buffer = new byte[1 << 20];
            long total = 0;
            int read;
            while ((read = content.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                crc.Append(buffer.AsSpan(0, read));
                onData(buffer.AsSpan(0, read));
                total += read;
            }

            if (total != entry.Size || crc.GetCurrentHashAsUInt32() != entry.Crc)
            {
                throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
            }
        }
    }

    private sealed class LimitedStream(Stream inner, long length) : Stream
    {
        private readonly long _length = length;
        private long _left = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position { get => _length - _left; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0)
            {
                return 0;
            }

            int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            _left -= read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Escribe la lista de archivos y la cabecera de inicio: sin esto el .7z no se puede abrir.</summary>
    public void Finish()
    {
        byte[] header = BuildHeader();
        long headerOffset = _output.Position - SignatureHeaderSize;
        byte[] next;
        if (_key is null)
        {
            _output.Write(header);
            next = header;
        }
        else
        {
            // Cabecera cifrada: lo que se escribe al final es un "EncodedHeader" que dice dónde está y cómo descifrarla.
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            byte[] encrypted = Encrypt(header, iv);
            _output.Write(encrypted);
            var w = new HeaderWriter();
            w.Byte(0x17); // kEncodedHeader
            w.Byte(0x06); // kPackInfo
            w.Number((ulong)headerOffset);
            w.Number(1);
            w.Byte(0x09);
            w.Number((ulong)encrypted.Length);
            w.Byte(0x00);
            w.Byte(0x07); // kUnPackInfo
            w.Byte(0x0B);
            w.Number(1);
            w.Byte(0);
            WriteAesCoderOnly(w, iv);
            w.Byte(0x0C);
            w.Number((ulong)header.Length);
            w.Byte(0x0A); // CRC de la cabecera sin cifrar
            w.Byte(1);
            w.UInt32(Crc32.HashToUInt32(header));
            w.Byte(0x00);
            w.Byte(0x00);
            next = w.ToArray();
            headerOffset = _output.Position - SignatureHeaderSize;
            _output.Write(next);
        }

        Span<byte> start = stackalloc byte[SignatureHeaderSize];
        Signature.CopyTo(start);
        start[6] = 0;
        start[7] = 4;
        BinaryPrimitives.WriteInt64LittleEndian(start[12..], headerOffset);
        BinaryPrimitives.WriteInt64LittleEndian(start[20..], next.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(start[28..], Crc32.HashToUInt32(next));
        BinaryPrimitives.WriteUInt32LittleEndian(start[8..], Crc32.HashToUInt32(start[12..]));
        long end = _output.Position;
        _output.Position = 0;
        _output.Write(start);
        _output.Position = end;
    }

    private byte[] Encrypt(byte[] data, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = _key!;
        return aes.EncryptCbc(data, iv, PaddingMode.Zeros);
    }

    private byte[] BuildHeader()
    {
        var streams = _entries.Where(e => !e.IsDirectory && e.Size > 0).ToList();
        var w = new HeaderWriter();
        w.Byte(0x01); // kHeader
        if (streams.Count > 0)
        {
            w.Byte(0x04); // kMainStreamsInfo
            w.Byte(0x06); // kPackInfo
            w.Number(0);
            w.Number((ulong)streams.Count);
            w.Byte(0x09);
            foreach (var e in streams)
            {
                w.Number((ulong)e.PackSize);
            }

            w.Byte(0x00);
            w.Byte(0x07); // kUnPackInfo
            w.Byte(0x0B);
            w.Number((ulong)streams.Count);
            w.Byte(0);
            foreach (var e in streams)
            {
                WriteFolder(w, e);
            }

            w.Byte(0x0C); // tamaños de salida de cada codificador
            foreach (var e in streams)
            {
                w.Number((ulong)e.Size);
                if (_key is not null)
                {
                    w.Number((ulong)e.CodedSize);
                }
            }

            w.Byte(0x00);
            w.Byte(0x08); // kSubStreamsInfo: un archivo por bloque, con su CRC
            w.Byte(0x0A);
            w.Byte(1);
            foreach (var e in streams)
            {
                w.UInt32(e.Crc);
            }

            w.Byte(0x00);
            w.Byte(0x00);
        }

        w.Byte(0x05); // kFilesInfo
        w.Number((ulong)_entries.Count);
        var empty = _entries.Select(e => e.IsDirectory || e.Size == 0).ToList();
        if (empty.Contains(true))
        {
            w.Property(0x0E, Bits(empty));
            var emptyFiles = _entries.Where(e => e.IsDirectory || e.Size == 0).Select(e => !e.IsDirectory).ToList();
            if (emptyFiles.Contains(true))
            {
                w.Property(0x0F, Bits(emptyFiles));
            }
        }

        var names = new MemoryStream();
        names.WriteByte(0); // no externos
        foreach (var e in _entries)
        {
            names.Write(Encoding.Unicode.GetBytes(e.Name));
            names.Write([0, 0]);
        }

        w.Property(0x11, names.ToArray());
        var times = new byte[2 + (8 * _entries.Count)];
        times[0] = 1; // todas definidas
        times[1] = 0; // no externas
        for (int i = 0; i < _entries.Count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(times.AsSpan(2 + (8 * i)), _entries[i].LastWriteUtc.ToFileTimeUtc());
        }

        w.Property(0x14, times);
        var attributes = new byte[2 + (4 * _entries.Count)];
        attributes[0] = 1;
        for (int i = 0; i < _entries.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(2 + (4 * i)), _entries[i].IsDirectory ? 0x10u : 0x20u);
        }

        w.Property(0x15, attributes);
        w.Byte(0x00);
        w.Byte(0x00);
        return w.ToArray();
    }

    /// <summary>Un bloque: Deflate (o copia) y, con contraseña, AES detrás (el AES descifra y le pasa lo suyo al Deflate).</summary>
    private void WriteFolder(HeaderWriter w, Entry e)
    {
        byte[] id = e.Stored ? CopyId : DeflateId;
        if (_key is null)
        {
            w.Number(1);
            w.Byte((byte)id.Length);
            w.Bytes(id);
            return;
        }

        w.Number(2);
        w.Byte((byte)id.Length);
        w.Bytes(id);
        WriteAesCoder(w, e.Iv!);
        w.Number(0); // InIndex: la entrada del primer codificador...
        w.Number(1); // ...viene de la salida del AES
    }

    private static void WriteAesCoderOnly(HeaderWriter w, byte[] iv)
    {
        w.Number(1);
        WriteAesCoder(w, iv);
    }

    private static void WriteAesCoder(HeaderWriter w, byte[] iv)
    {
        w.Byte((byte)(0x20 | AesId.Length)); // con propiedades
        w.Bytes(AesId);
        byte[] properties = new byte[2 + iv.Length];
        properties[0] = (byte)(CyclesPower | 0x40); // sin sal, con IV
        properties[1] = (byte)(iv.Length - 1);
        iv.CopyTo(properties, 2);
        w.Number((ulong)properties.Length);
        w.Bytes(properties);
    }

    private static byte[] Bits(List<bool> values)
    {
        byte[] bits = new byte[(values.Count + 7) / 8];
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                bits[i / 8] |= (byte)(0x80 >> (i % 8));
            }
        }

        return bits;
    }

    private sealed record Entry(string Name, DateTime LastWriteUtc, bool IsDirectory)
    {
        public long Size { get; set; }

        public uint Crc { get; set; }

        public bool Stored { get; set; }

        /// <summary>Tamaño a la salida del Deflate (antes de cifrar).</summary>
        public long CodedSize { get; set; }

        /// <summary>Lo que ocupa en el .7z.</summary>
        public long PackSize { get; set; }

        public byte[]? Iv { get; set; }

        public long Offset { get; set; }
    }

    private sealed class HeaderWriter
    {
        private readonly MemoryStream _data = new();

        public void Byte(byte value) => _data.WriteByte(value);

        public void Bytes(ReadOnlySpan<byte> value) => _data.Write(value);

        public void UInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            _data.Write(bytes);
        }

        /// <summary>Número de 7z: el primer byte dice cuántos bytes más le siguen.</summary>
        public void Number(ulong value)
        {
            byte first = 0;
            byte mask = 0x80;
            int extra;
            for (extra = 0; extra < 8; extra++)
            {
                if (value < (1UL << (7 * (extra + 1))))
                {
                    first |= (byte)(value >> (8 * extra));
                    break;
                }

                first |= mask;
                mask >>= 1;
            }

            _data.WriteByte(first);
            for (; extra > 0; extra--)
            {
                _data.WriteByte((byte)value);
                value >>= 8;
            }
        }

        public void Property(byte id, byte[] data)
        {
            Byte(id);
            Number((ulong)data.Length);
            Bytes(data);
        }

        public byte[] ToArray() => _data.ToArray();
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written;

        public override long Position { get => Written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Written += buffer.Length;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
