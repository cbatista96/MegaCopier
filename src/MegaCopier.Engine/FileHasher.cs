using System.IO.Hashing;
using System.Security.Cryptography;

namespace MegaCopier.Engine;

/// <summary>Algoritmo del hash completo de cada archivo (el que se muestra en la lista y se guarda en el archivo de sumas).</summary>
public enum FileHashAlgorithm
{
    None,
    Crc32,
    XxHash3,
    XxHash64,
    XxHash128,
    Md5,
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>Hash incremental de un archivo completo con el algoritmo elegido.</summary>
internal abstract class FileHasher
{
    public abstract void Append(ReadOnlySpan<byte> data);

    public abstract string Finish();

    public static FileHasher? Create(FileHashAlgorithm algorithm) => algorithm switch
    {
        FileHashAlgorithm.None => null,
        FileHashAlgorithm.Crc32 => new NonCryptographic(new Crc32(), h => ((Crc32)h).GetCurrentHashAsUInt32().ToString("x8")),
        FileHashAlgorithm.XxHash3 => new NonCryptographic(new XxHash3(), h => ((XxHash3)h).GetCurrentHashAsUInt64().ToString("x16")),
        FileHashAlgorithm.XxHash64 => new NonCryptographic(new XxHash64(), h => ((XxHash64)h).GetCurrentHashAsUInt64().ToString("x16")),
        FileHashAlgorithm.XxHash128 => new NonCryptographic(new XxHash128(), h => ((XxHash128)h).GetCurrentHashAsUInt128().ToString("x32")),
        FileHashAlgorithm.Md5 => new Cryptographic(HashAlgorithmName.MD5),
        FileHashAlgorithm.Sha1 => new Cryptographic(HashAlgorithmName.SHA1),
        FileHashAlgorithm.Sha256 => new Cryptographic(HashAlgorithmName.SHA256),
        FileHashAlgorithm.Sha512 => new Cryptographic(HashAlgorithmName.SHA512),
        _ => null,
    };

    /// <summary>Extensión habitual del archivo de sumas para cada algoritmo.</summary>
    public static string Extension(FileHashAlgorithm algorithm) => algorithm switch
    {
        FileHashAlgorithm.Crc32 => "sfv",
        FileHashAlgorithm.XxHash3 => "xxh3",
        FileHashAlgorithm.XxHash64 => "xxh64",
        FileHashAlgorithm.XxHash128 => "xxh128",
        FileHashAlgorithm.Md5 => "md5",
        FileHashAlgorithm.Sha1 => "sha1",
        FileHashAlgorithm.Sha256 => "sha256",
        FileHashAlgorithm.Sha512 => "sha512",
        _ => "txt",
    };

    /// <summary>Se muestra como número en hexadecimal, la forma habitual de estos algoritmos (y de los archivos .sfv/.xxh).</summary>
    private sealed class NonCryptographic(NonCryptographicHashAlgorithm hash, Func<NonCryptographicHashAlgorithm, string> format) : FileHasher
    {
        public override void Append(ReadOnlySpan<byte> data) => hash.Append(data);

        public override string Finish() => format(hash);
    }

    private sealed class Cryptographic(HashAlgorithmName name) : FileHasher
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(name);

        public override void Append(ReadOnlySpan<byte> data) => _hash.AppendData(data);

        public override string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());
    }
}
