namespace MegaCopier.Engine;

/// <summary>
/// Escudo contra ransomware: antes de sobrescribir una copia de seguridad con lo del origen, se mira una muestra de los
/// archivos que van a reemplazar a otros. Si muchos que en el destino son "normales" (texto, documentos, imágenes sin
/// comprimir...) en el origen parecen datos aleatorios, es casi seguro que alguien los cifró: la copia se detiene antes
/// de estropear también la copia buena. Los formatos ya comprimidos (ZIP, JPG, vídeo...) no cuentan, porque también
/// parecen aleatorios sin que pase nada.
/// </summary>
public static class RansomwareShield
{
    /// <summary>Cuántos archivos parecidos a cifrados hacen falta para parar (y qué parte de la muestra).</summary>
    public const int MinimumSuspicious = 8;

    private const int SampleBytes = 64 * 1024;
    private const int MaxPairs = 300;
    private const double RandomEntropy = 7.9;   // bits por byte: datos cifrados o comprimidos
    private const double NormalEntropy = 7.0;   // por debajo, contenido "normal"

    /// <summary>
    /// Mira (como mucho) 300 parejas origen → destino ya existente. Devuelve los orígenes sospechosos si son bastantes
    /// para parar (al menos <see cref="MinimumSuspicious"/> y una cuarta parte de lo mirado), o una lista vacía.
    /// </summary>
    public static List<string> Check(IEnumerable<(string Source, string Target)> replacements, CancellationToken cancellationToken = default)
    {
        var suspicious = new List<string>();
        int looked = 0;
        byte[] buffer = new byte[SampleBytes];
        foreach (var (source, target) in replacements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (looked >= MaxPairs)
            {
                break;
            }

            double? before = Entropy(target, buffer);
            if (before is null or >= NormalEntropy)
            {
                continue; // ya era aleatorio (comprimido) o no se puede leer: no dice nada
            }

            double? after = Entropy(source, buffer);
            if (after is null)
            {
                continue;
            }

            looked++;
            if (after >= RandomEntropy)
            {
                suspicious.Add(source);
            }
        }

        return suspicious.Count >= MinimumSuspicious && suspicious.Count * 4 >= looked ? suspicious : [];
    }

    /// <summary>Entropía de Shannon (bits por byte) del principio del archivo; null si es muy pequeño o no se puede leer.</summary>
    public static double? Entropy(string path, byte[] buffer)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read < 4096 ? null : Entropy(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static double Entropy(ReadOnlySpan<byte> data)
    {
        Span<int> counts = stackalloc int[256];
        foreach (byte value in data)
        {
            counts[value]++;
        }

        double entropy = 0;
        foreach (int count in counts)
        {
            if (count > 0)
            {
                double p = (double)count / data.Length;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }
}
