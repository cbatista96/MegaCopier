using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MegaCopier.Engine;

/// <summary>
/// Archivos cifrados de MegaCopier (.mcenc), para llevar copias en un USB que nadie más pueda leer.
/// AES-256-GCM por bloques de 1 MB: cada bloque lleva su número y si es el último en los datos autenticados, así que no
/// se puede cambiar, reordenar ni recortar nada sin que se note. La clave sale de la contraseña con PBKDF2-SHA256
/// (600.000 iteraciones) y una sal aleatoria; la contraseña nunca se guarda.
/// Formato: "MCENC1\0\0" | sal (16) | iteraciones (4) | prefijo del nonce (8) | tamaño original (8) | bloques.
/// </summary>
public static class EncryptedFile
{
    public const string Extension = ".mcenc";
    private const int Block = 1024 * 1024;
    private const int TagSize = 16;
    private const int HeaderSize = 8 + 16 + 4 + 8 + 8;
    private const int Iterations = 600_000;
    private static readonly byte[] Magic = "MCENC1\0\0"u8.ToArray();

    // Derivar la clave cuesta a propósito (≈0,3 s): se hace una vez por contraseña y sal.
    private static readonly ConcurrentDictionary<string, byte[]> Keys = new();

    /// <summary>Sal de un trabajo: todos sus archivos la comparten para derivar la clave una sola vez.</summary>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(16);

    /// <summary>Tamaño que tendrá cifrado un archivo de <paramref name="length"/> bytes.</summary>
    public static long EncryptedLength(long length) => HeaderSize + length + (Math.Max(1, (length + Block - 1) / Block) * TagSize);

    public static void Encrypt(Stream input, Stream output, long length, string password, byte[] salt, Action<byte[], int>? progress = null, CancellationToken cancellationToken = default)
    {
        byte[] key = Key(password, salt, Iterations);
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        salt.CopyTo(header, 8);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), Iterations);
        RandomNumberGenerator.Fill(header.AsSpan(28, 8));
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(36), length);
        output.Write(header);

        using var aes = new AesGcm(key, TagSize);
        byte[] plain = new byte[Block];
        byte[] cipher = new byte[Block];
        byte[] tag = new byte[TagSize];
        long blocks = Math.Max(1, (length + Block - 1) / Block);
        for (long i = 0; i < blocks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(Block, length - (i * Block));
            input.ReadExactly(plain, 0, count);
            aes.Encrypt(Nonce(header, i), plain.AsSpan(0, count), cipher.AsSpan(0, count), tag, Associated(header, i, i == blocks - 1));
            output.Write(cipher, 0, count);
            output.Write(tag);
            progress?.Invoke(plain, count);
        }
    }

    /// <summary>
    /// Descifra comprobando cada bloque. Lanza CryptographicException si la contraseña no es la buena o el archivo se
    /// dañó o se manipuló (también si le falta el final).
    /// </summary>
    public static long Decrypt(Stream input, Stream output, string password, Action<byte[], int>? progress = null, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[HeaderSize];
        input.ReadExactly(header);
        if (!header.AsSpan(0, 8).SequenceEqual(Magic))
        {
            throw new CryptographicException(Texts.Get("E_NotEncrypted"));
        }

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(24));
        long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(36));
        if (iterations is < 100_000 or > 10_000_000 || length < 0)
        {
            throw new CryptographicException(Texts.Get("E_BadEncryptedHeader"));
        }

        byte[] key = Key(password, header.AsSpan(8, 16).ToArray(), iterations);
        using var aes = new AesGcm(key, TagSize);
        byte[] plain = new byte[Block];
        byte[] cipher = new byte[Block];
        byte[] tag = new byte[TagSize];
        long blocks = Math.Max(1, (length + Block - 1) / Block);
        for (long i = 0; i < blocks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = (int)Math.Min(Block, length - (i * Block));
            input.ReadExactly(cipher, 0, count);
            input.ReadExactly(tag);
            aes.Decrypt(Nonce(header, i), cipher.AsSpan(0, count), tag, plain.AsSpan(0, count), Associated(header, i, i == blocks - 1));
            output.Write(plain, 0, count);
            progress?.Invoke(plain, count);
        }

        if (input.ReadByte() != -1)
        {
            throw new CryptographicException(Texts.Get("E_EncryptedExtra"));
        }

        return length;
    }

    /// <summary>Tamaño original de un .mcenc (sin descifrarlo), o -1.</summary>
    public static long OriginalLength(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            byte[] header = new byte[HeaderSize];
            stream.ReadExactly(header);
            return header.AsSpan(0, 8).SequenceEqual(Magic) ? BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(36)) : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static byte[] Key(string password, byte[] salt, int iterations) =>
        Keys.GetOrAdd($"{iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)))}",
            _ => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32));

    private static byte[] Nonce(byte[] header, long block)
    {
        byte[] nonce = new byte[12];
        header.AsSpan(28, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), (uint)block);
        return nonce;
    }

    private static byte[] Associated(byte[] header, long block, bool last)
    {
        byte[] data = new byte[HeaderSize + 9];
        header.CopyTo(data, 0);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(HeaderSize), block);
        data[^1] = last ? (byte)1 : (byte)0;
        return data;
    }
}
