namespace MegaCopier.Engine.Backup;

/// <summary>
/// Corta un archivo en trozos según su contenido (FastCDC, con "gear hash"): los cortes dependen de los bytes y no de
/// la posición, así que si se inserta algo al principio de un archivo grande solo cambian los trozos de alrededor y el
/// resto sigue siendo igual (y no se vuelve a guardar). Trozos de 256 KB a 4 MB, 1 MB de media.
/// </summary>
internal static class ContentChunker
{
    public const int MinSize = 256 * 1024;
    public const int AverageSize = 1024 * 1024;
    public const int MaxSize = 4 * 1024 * 1024;

    // Máscaras de FastCDC: más bits (más difícil cortar) antes de la media y menos después: los tamaños se concentran.
    private const ulong MaskSmall = 0x0000_D903_0353_0000UL; // 22 bits
    private const ulong MaskLarge = 0x0000_D900_0353_0000UL; // 18 bits

    private static readonly ulong[] Gear = BuildGear();

    private static ulong[] BuildGear()
    {
        // Tabla fija (pseudoaleatoria, siempre la misma): los cortes tienen que salir igual en todas las versiones.
        var table = new ulong[256];
        ulong state = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 256; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            table[i] = state;
        }

        return table;
    }

    /// <summary>Longitud del siguiente trozo de <paramref name="data"/> (que empieza en un corte).</summary>
    public static int Cut(ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        if (length <= MinSize)
        {
            return length;
        }

        int limit = Math.Min(length, MaxSize);
        int normal = Math.Min(limit, AverageSize);
        ulong hash = 0;
        int i = MinSize;
        for (; i < normal; i++)
        {
            hash = (hash << 1) + Gear[data[i]];
            if ((hash & MaskSmall) == 0)
            {
                return i + 1;
            }
        }

        for (; i < limit; i++)
        {
            hash = (hash << 1) + Gear[data[i]];
            if ((hash & MaskLarge) == 0)
            {
                return i + 1;
            }
        }

        return limit;
    }

    /// <summary>Recorre un flujo devolviendo cada trozo (el búfer se reutiliza: hay que usarlo antes del siguiente).</summary>
    public static IEnumerable<ArraySegment<byte>> Split(Stream stream)
    {
        byte[] buffer = new byte[MaxSize * 2];
        int filled = 0;
        bool end = false;
        while (true)
        {
            if (!end && filled < MaxSize)
            {
                int read;
                while (filled < buffer.Length && (read = stream.Read(buffer, filled, buffer.Length - filled)) > 0)
                {
                    filled += read;
                }

                end = filled < buffer.Length;
            }

            if (filled == 0)
            {
                yield break;
            }

            // Sin llegar al final, el búfer siempre tiene al menos un trozo máximo: el corte no depende de cuánto se leyó.
            int cut = Cut(buffer.AsSpan(0, filled));
            yield return new ArraySegment<byte>(buffer, 0, cut);
            Buffer.BlockCopy(buffer, cut, buffer, 0, filled - cut);
            filled -= cut;
        }
    }
}
