using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine.IO;

/// <summary>
/// Escribe un ZIP (con ZIP64, sin límite de 4 GB ni de 65 535 archivos). Con contraseña, cada archivo va cifrado con
/// AES-256 en el formato de WinZip (AE-2), el que abren 7-Zip, WinZip, WinRAR y PeaZip; los nombres de archivo no se
/// cifran (el formato no lo permite). Los archivos que ya vienen comprimidos (fotos, vídeos, ZIP...) se guardan sin
/// volver a comprimir.
/// </summary>
internal sealed class ZipArchiveWriter : IArchiveWriter
{
    internal const ushort MethodStored = 0;
    internal const ushort MethodDeflate = 8;
    internal const ushort MethodAes = 99;
    internal const int AesSaltLength = 16;
    internal const int AesAuthLength = 10;
    internal const int AesIterations = 1000;

    private static readonly HashSet<string> Compressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".xz", ".bz2", ".zst", ".cab", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".avif",
        ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".flac", ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".docx", ".xlsx",
        ".pptx", ".odt", ".ods", ".epub", ".pdf", ".apk", ".jar", ".msi", ".iso", ".mcenc",
    };

    /// <summary>Ya viene comprimido (fotos, vídeos, ZIP...): se guarda tal cual, comprimirlo otra vez solo gasta tiempo.</summary>
    internal static bool IsCompressed(string name) => Compressed.Contains(Path.GetExtension(name));

    private readonly Stream _output;
    private readonly string? _password;
    private readonly CompressionLevel _level;
    private readonly List<Entry> _entries = [];

    public ZipArchiveWriter(Stream output, string? password, CompressionLevel level = CompressionLevel.Fastest)
    {
        _output = output;
        _password = string.IsNullOrEmpty(password) ? null : password;
        _level = level;
    }

    public int Count => _entries.Count;

    public void AddDirectory(string name, DateTime lastWriteUtc)
    {
        name = name.Replace('\\', '/').TrimEnd('/') + "/";
        var entry = new Entry(name, lastWriteUtc, _output.Position, IsDirectory: true, Method: MethodStored, Encrypted: false);
        WriteLocalHeader(entry);
        _entries.Add(entry);
    }

    /// <summary>
    /// Añade un archivo leyendo <paramref name="source"/> hasta el final. <paramref name="onRead"/> recibe lo leído (para
    /// el progreso y el hash). Si algo falla a medias, el ZIP vuelve a quedar como antes de este archivo.
    /// </summary>
    public void AddFile(string name, Stream source, DateTime lastWriteUtc, Action<ReadOnlySpan<byte>> onRead, CancellationToken cancellationToken)
    {
        name = name.Replace('\\', '/');
        ushort method = IsCompressed(name) ? MethodStored : MethodDeflate;
        long start = _output.Position;
        var entry = new Entry(name, lastWriteUtc, start, IsDirectory: false, Method: method, Encrypted: _password is not null);
        try
        {
            WriteLocalHeader(entry);
            long dataStart = _output.Position;
            var crc = new Crc32();
            AesCtrHmacStream? aes = null;
            Stream sink = _output;
            if (_password is not null)
            {
                byte[] salt = RandomNumberGenerator.GetBytes(AesSaltLength);
                var (key, hmacKey, verifier) = DeriveKeys(_password, salt);
                _output.Write(salt);
                _output.Write(verifier);
                aes = new AesCtrHmacStream(_output, key, hmacKey);
                sink = aes;
            }

            using (var compressor = method == MethodDeflate ? new DeflateStream(sink, _level, leaveOpen: true) : null)
            {
                Stream target = compressor ?? sink;
                byte[] buffer = new byte[1 << 20];
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

            if (aes is not null)
            {
                _output.Write(aes.AuthenticationCode());
            }

            long end = _output.Position;
            entry.CompressedSize = end - dataStart;
            // AE-2: el CRC no se guarda (lo sustituye el código de autenticación); sin cifrar, sí.
            entry.Crc = entry.Encrypted ? 0 : crc.GetCurrentHashAsUInt32();

            // Se vuelve a la cabecera local a poner el CRC y los tamaños, que ahora ya se saben.
            _output.Position = start + 14;
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crcBytes, entry.Crc);
            _output.Write(crcBytes);
            _output.Position = start + 30 + Encoding.UTF8.GetByteCount(entry.Name) + 4;
            Span<byte> sizes = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(sizes, entry.Size);
            BinaryPrimitives.WriteInt64LittleEndian(sizes[8..], entry.CompressedSize);
            _output.Write(sizes);
            _output.Position = end;
            _entries.Add(entry);
        }
        catch
        {
            _output.SetLength(start);
            _output.Position = start;
            throw;
        }
    }

    private Dictionary<string, ZipEntryInfo>? _written;

    /// <summary>Relee un archivo ya escrito leyendo el ZIP como cualquier programa (después de <see cref="Finish"/>).</summary>
    public void ReadBack(string name, Action<ReadOnlySpan<byte>> onData, CancellationToken cancellationToken)
    {
        _written ??= ZipArchiveReader.Entries(_output).ToDictionary(e => e.Name, StringComparer.Ordinal);
        if (!_written.TryGetValue(name.Replace('\\', '/'), out var entry))
        {
            throw new InvalidDataException(Texts.Get("E_ZipDamaged"));
        }

        ZipArchiveReader.Extract(_output, entry, _password, onData, cancellationToken);
    }

    /// <summary>Escribe el directorio central: sin él no se puede abrir el ZIP.</summary>
    public void Finish()
    {
        long directoryStart = _output.Position;
        using (var writer = new BinaryWriter(_output, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var entry in _entries)
            {
                byte[] name = Encoding.UTF8.GetBytes(entry.Name);
                bool bigSize = entry.Size >= uint.MaxValue || entry.CompressedSize >= uint.MaxValue;
                bool bigOffset = entry.Offset >= uint.MaxValue;
                var zip64 = new MemoryStream();
                using (var extra = new BinaryWriter(zip64))
                {
                    if (bigSize)
                    {
                        extra.Write(entry.Size);
                        extra.Write(entry.CompressedSize);
                    }

                    if (bigOffset)
                    {
                        extra.Write(entry.Offset);
                    }
                }

                byte[] zip64Data = zip64.ToArray();
                int extraLength = (zip64Data.Length > 0 ? 4 + zip64Data.Length : 0) + (entry.Encrypted ? 11 : 0);
                writer.Write(0x02014b50u);
                writer.Write((ushort)(63 | (0 << 8))); // hecho por: especificación 6.3, MS-DOS (atributos de Windows)
                writer.Write(VersionNeeded(entry));
                writer.Write(Flags(entry));
                writer.Write(entry.Encrypted ? MethodAes : entry.Method);
                WriteDosTime(writer, entry.LastWriteUtc);
                writer.Write(entry.Crc);
                writer.Write(bigSize ? uint.MaxValue : (uint)entry.CompressedSize);
                writer.Write(bigSize ? uint.MaxValue : (uint)entry.Size);
                writer.Write((ushort)name.Length);
                writer.Write((ushort)extraLength);
                writer.Write((ushort)0); // comentario
                writer.Write((ushort)0); // disco
                writer.Write((ushort)0); // atributos internos
                writer.Write(entry.IsDirectory ? 0x10u : 0x20u);
                writer.Write(bigOffset ? uint.MaxValue : (uint)entry.Offset);
                writer.Write(name);
                if (zip64Data.Length > 0)
                {
                    writer.Write((ushort)0x0001);
                    writer.Write((ushort)zip64Data.Length);
                    writer.Write(zip64Data);
                }

                if (entry.Encrypted)
                {
                    WriteAesExtra(writer, entry.Method);
                }
            }

            long directoryEnd = _output.Position;
            long directorySize = directoryEnd - directoryStart;
            bool needs64 = _entries.Count >= ushort.MaxValue || directoryStart >= uint.MaxValue || directorySize >= uint.MaxValue;
            if (needs64)
            {
                writer.Write(0x06064b50u);
                writer.Write(44L);              // tamaño del resto de este registro
                writer.Write((ushort)45);
                writer.Write((ushort)45);
                writer.Write(0u);               // este disco
                writer.Write(0u);               // disco del directorio central
                writer.Write((long)_entries.Count);
                writer.Write((long)_entries.Count);
                writer.Write(directorySize);
                writer.Write(directoryStart);
                writer.Write(0x07064b50u);      // localizador
                writer.Write(0u);
                writer.Write(directoryEnd);
                writer.Write(1u);
            }

            writer.Write(0x06054b50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(needs64 ? ushort.MaxValue : (ushort)_entries.Count);
            writer.Write(needs64 ? ushort.MaxValue : (ushort)_entries.Count);
            writer.Write(needs64 ? uint.MaxValue : (uint)directorySize);
            writer.Write(needs64 ? uint.MaxValue : (uint)directoryStart);
            writer.Write((ushort)0);
        }
    }

    internal static (byte[] Key, byte[] HmacKey, byte[] Verifier) DeriveKeys(string password, byte[] salt)
    {
        byte[] material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, AesIterations, HashAlgorithmName.SHA1, 32 + 32 + 2);
        return (material[..32], material[32..64], material[64..]);
    }

    private void WriteLocalHeader(Entry entry)
    {
        byte[] name = Encoding.UTF8.GetBytes(entry.Name);
        using var writer = new BinaryWriter(_output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x04034b50u);
        writer.Write(VersionNeeded(entry));
        writer.Write(Flags(entry));
        writer.Write(entry.Encrypted ? MethodAes : entry.Method);
        WriteDosTime(writer, entry.LastWriteUtc);
        writer.Write(0u);                // CRC: se pone al terminar
        writer.Write(uint.MaxValue);     // tamaños: en el campo ZIP64 (aún no se saben)
        writer.Write(uint.MaxValue);
        writer.Write((ushort)name.Length);
        writer.Write((ushort)(20 + (entry.Encrypted ? 11 : 0)));
        writer.Write(name);
        writer.Write((ushort)0x0001);
        writer.Write((ushort)16);
        writer.Write(0L);
        writer.Write(0L);
        if (entry.Encrypted)
        {
            WriteAesExtra(writer, entry.Method);
        }
    }

    private static void WriteAesExtra(BinaryWriter writer, ushort actualMethod)
    {
        writer.Write((ushort)0x9901);
        writer.Write((ushort)7);
        writer.Write((ushort)2);        // AE-2
        writer.Write((byte)'A');
        writer.Write((byte)'E');
        writer.Write((byte)3);          // AES-256
        writer.Write(actualMethod);
    }

    private static ushort VersionNeeded(Entry entry) => entry.Encrypted ? (ushort)51 : (ushort)45;

    // Bit 0: cifrado. Bit 11: nombres en UTF-8.
    private static ushort Flags(Entry entry) => (ushort)((entry.Encrypted ? 1 : 0) | (1 << 11));

    private static void WriteDosTime(BinaryWriter writer, DateTime utc)
    {
        var local = utc.ToLocalTime();
        if (local.Year < 1980)
        {
            local = new DateTime(1980, 1, 1);
        }

        writer.Write((ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2)));
        writer.Write((ushort)(((local.Year - 1980) << 9) | (local.Month << 5) | local.Day));
    }

    private sealed record Entry(string Name, DateTime LastWriteUtc, long Offset, bool IsDirectory, ushort Method, bool Encrypted)
    {
        public long Size { get; set; }

        public long CompressedSize { get; set; }

        public uint Crc { get; set; }
    }
}

/// <summary>
/// AES en modo contador (como lo usa WinZip: contador de 128 bits en little-endian empezando en 1) más HMAC-SHA1 de lo
/// cifrado. Sirve para cifrar (al escribir) y para descifrar (al leer), porque en modo contador es la misma operación.
/// </summary>
internal sealed class AesCtrHmacStream : Stream
{
    private readonly Stream _inner;
    private readonly Aes _aes;
    private readonly IncrementalHash _hmac;
    private readonly bool _decrypt;
    private readonly byte[] _counters = new byte[16 * 4096];
    private readonly byte[] _keystream = new byte[16 * 4096];
    private int _keystreamUsed;
    private ulong _counter;

    public AesCtrHmacStream(Stream inner, byte[] key, byte[] hmacKey, bool decrypt = false)
    {
        _inner = inner;
        _aes = Aes.Create();
        _aes.Key = key;
        _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, hmacKey);
        _decrypt = decrypt;
        _keystreamUsed = _keystream.Length;
    }

    public override bool CanRead => _decrypt;

    public override bool CanSeek => false;

    public override bool CanWrite => !_decrypt;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>Los 10 primeros bytes del HMAC-SHA1 de todo lo cifrado.</summary>
    public byte[] AuthenticationCode() => _hmac.GetHashAndReset()[..ZipArchiveWriter.AesAuthLength];

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        byte[] work = new byte[Math.Min(buffer.Length, 1 << 20)];
        while (buffer.Length > 0)
        {
            int count = Math.Min(work.Length, buffer.Length);
            buffer[..count].CopyTo(work);
            Transform(work.AsSpan(0, count));
            _hmac.AppendData(work, 0, count);
            _inner.Write(work, 0, count);
            buffer = buffer[count..];
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        _hmac.AppendData(buffer[..read]);
        Transform(buffer[..read]);
        return read;
    }

    private void Transform(Span<byte> data)
    {
        while (data.Length > 0)
        {
            if (_keystreamUsed == _keystream.Length)
            {
                for (int block = 0; block < _counters.Length / 16; block++)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(_counters.AsSpan(block * 16), ++_counter);
                }

                _aes.EncryptEcb(_counters, _keystream, PaddingMode.None);
                _keystreamUsed = 0;
            }

            int run = Math.Min(data.Length, _keystream.Length - _keystreamUsed);
            Xor(data[..run], _keystream.AsSpan(_keystreamUsed, run));
            _keystreamUsed += run;
            data = data[run..];
        }
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> key)
    {
        int i = 0;
        for (; i <= target.Length - Vector<byte>.Count; i += Vector<byte>.Count)
        {
            (new Vector<byte>(target[i..]) ^ new Vector<byte>(key[i..])).CopyTo(target[i..]);
        }

        for (; i < target.Length; i++)
        {
            target[i] ^= key[i];
        }
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aes.Dispose();
            _hmac.Dispose();
        }

        base.Dispose(disposing);
    }
}
