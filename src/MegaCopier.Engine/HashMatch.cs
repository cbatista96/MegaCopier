using System.Text.RegularExpressions;

namespace MegaCopier.Engine;

/// <summary>
/// Comprobar un archivo contra una suma escrita a mano o copiada de una web (la de una ISO, por ejemplo): por su
/// longitud se sabe qué algoritmos pueden ser, se calculan todos leyendo el archivo una sola vez y se dice si coincide.
/// También lee el CRC32 que algunas descargas llevan en el nombre («video [A1B2C3D4].mkv»).
/// </summary>
public static partial class HashMatch
{
    /// <summary>Resultado: si coincide y con qué algoritmo (o el primero probado, si no coincide ninguno).</summary>
    public readonly record struct Result(bool Matches, FileHashAlgorithm Algorithm, string Actual);

    /// <summary>
    /// La suma que hay en un texto: el primer número hexadecimal con una longitud de suma conocida. Admite «0x…»,
    /// mayúsculas, espacios y líneas como las de md5sum («abc…  archivo.iso») o «SHA256: abc…». Null si no hay ninguna.
    /// </summary>
    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in HexRun().Matches(text))
        {
            string hex = match.Groups[1].Value.ToLowerInvariant();
            if (Candidates(hex).Count > 0)
            {
                return hex;
            }
        }

        return null;
    }

    /// <summary>Algoritmos que dan sumas de esta longitud (en el orden en que se informan).</summary>
    public static IReadOnlyList<FileHashAlgorithm> Candidates(string hex) => hex.Length switch
    {
        8 => [FileHashAlgorithm.Crc32],
        16 => [FileHashAlgorithm.XxHash3, FileHashAlgorithm.XxHash64],
        32 => [FileHashAlgorithm.Md5, FileHashAlgorithm.XxHash128],
        40 => [FileHashAlgorithm.Sha1],
        64 => [FileHashAlgorithm.Sha256, FileHashAlgorithm.Blake3, FileHashAlgorithm.Sha3_256],
        128 => [FileHashAlgorithm.Sha512, FileHashAlgorithm.Sha3_512],
        _ => [],
    };

    /// <summary>Calcula a la vez todos los algoritmos posibles para esa suma y dice si alguno coincide.</summary>
    public static Result Check(string path, string expected, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string hex = Extract(expected) ?? throw new ArgumentException("No es una suma conocida", nameof(expected));
        var hashers = Candidates(hex)
            .Select(a => (Algorithm: a, Hasher: FileHasher.Create(a)))
            .Where(h => h.Hasher is not null)
            .ToList();
        if (hashers.Count == 0)
        {
            throw new NotSupportedException("Ningún algoritmo disponible para esa suma");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        long length = stream.Length;
        long done = 0;
        byte[] buffer = new byte[4 * 1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = buffer.AsSpan(0, read);
            foreach (var (_, hasher) in hashers)
            {
                hasher!.Append(data);
            }

            done += read;
            progress?.Report(length > 0 ? (double)done / length : 1);
        }

        Result? first = null;
        foreach (var (algorithm, hasher) in hashers)
        {
            string actual = hasher!.Finish();
            if (string.Equals(actual, hex, StringComparison.OrdinalIgnoreCase))
            {
                return new Result(true, algorithm, actual);
            }

            first ??= new Result(false, algorithm, actual);
        }

        return first!.Value;
    }

    /// <summary>El CRC32 que lleva el nombre entre corchetes o paréntesis («[A1B2C3D4]»), o null.</summary>
    public static string? CrcInName(string fileName)
    {
        var match = CrcTag().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success ? match.Groups["crc"].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"(?<![0-9a-fA-F])(?:0x)?([0-9a-fA-F]{8,128})(?![0-9a-fA-F])")]
    private static partial Regex HexRun();

    [GeneratedRegex(@"[\[(](?<crc>[0-9a-fA-F]{8})[\])]")]
    private static partial Regex CrcTag();
}
