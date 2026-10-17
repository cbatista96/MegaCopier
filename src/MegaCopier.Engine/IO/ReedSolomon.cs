namespace MegaCopier.Engine.IO;

/// <summary>
/// Código Reed-Solomon sistemático sobre GF(256) con matriz de Cauchy (como PAR2 y los RAID 6): con k bloques de datos
/// se calculan m bloques de paridad, y con cualesquiera k de los k+m se recuperan los datos (hasta m bloques perdidos).
/// </summary>
internal sealed class ReedSolomon
{
    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    private readonly int _data;
    private readonly int _parity;
    private readonly byte[,] _cauchy; // [paridad, dato]

    static ReedSolomon()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= 0x11D; // x^8 + x^4 + x^3 + x^2 + 1
            }
        }

        for (int i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    public ReedSolomon(int data, int parity)
    {
        if (data < 1 || parity < 1 || data + parity > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(parity));
        }

        _data = data;
        _parity = parity;
        _cauchy = new byte[parity, data];
        for (int j = 0; j < parity; j++)
        {
            for (int i = 0; i < data; i++)
            {
                _cauchy[j, i] = Inverse((byte)((data + j) ^ i)); // 1 / (x_j + y_i), con x_j = k + j e y_i = i distintos
            }
        }
    }

    public static byte Multiply(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    public static byte Inverse(byte a) => a == 0 ? throw new DivideByZeroException() : Exp[255 - Log[a]];

    /// <summary>Calcula la paridad: <paramref name="parity"/>[j] = Σ C[j,i]·<paramref name="data"/>[i].</summary>
    public void Encode(IReadOnlyList<byte[]> data, IReadOnlyList<byte[]> parity, int length)
    {
        for (int j = 0; j < _parity; j++)
        {
            Array.Clear(parity[j], 0, length);
            for (int i = 0; i < _data; i++)
            {
                MultiplyAdd(_cauchy[j, i], data[i], parity[j], length);
            }
        }
    }

    /// <summary>
    /// Reconstruye los bloques de datos perdidos. <paramref name="data"/> y <paramref name="parity"/> son todos los
    /// bloques (null = perdido o dañado); los de datos perdidos se rellenan. False si faltan más de los que se pueden
    /// recuperar.
    /// </summary>
    public bool Reconstruct(byte[]?[] data, byte[]?[] parity, int length)
    {
        var missing = Enumerable.Range(0, _data).Where(i => data[i] is null).ToList();
        if (missing.Count == 0)
        {
            return true;
        }

        var spare = Enumerable.Range(0, _parity).Where(j => parity[j] is not null).ToList();
        if (spare.Count < missing.Count)
        {
            return false;
        }

        // Ecuaciones: cada paridad usada da Σ C[j,i]·d_i = p_j. Se pasan al otro lado los datos que sí se tienen y queda
        // un sistema de "perdidos × perdidos" que se resuelve invirtiendo su matriz.
        int n = missing.Count;
        var rows = spare.Take(n).ToList();
        var matrix = new byte[n, n];
        var known = new byte[n][];
        for (int r = 0; r < n; r++)
        {
            int j = rows[r];
            known[r] = (byte[])parity[j]!.Clone();
            for (int i = 0; i < _data; i++)
            {
                if (data[i] is { } present)
                {
                    MultiplyAdd(_cauchy[j, i], present, known[r], length);
                }
            }

            for (int c = 0; c < n; c++)
            {
                matrix[r, c] = _cauchy[j, missing[c]];
            }
        }

        var inverse = Invert(matrix, n);
        for (int c = 0; c < n; c++)
        {
            byte[] block = new byte[known[0].Length];
            for (int r = 0; r < n; r++)
            {
                MultiplyAdd(inverse[c, r], known[r], block, length);
            }

            data[missing[c]] = block;
        }

        return true;
    }

    private static byte[,] Invert(byte[,] source, int n)
    {
        var a = (byte[,])source.Clone();
        var inverse = new byte[n, n];
        for (int i = 0; i < n; i++)
        {
            inverse[i, i] = 1;
        }

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            while (a[pivot, col] == 0)
            {
                pivot++; // una submatriz de Cauchy siempre es invertible: siempre hay pivote
            }

            if (pivot != col)
            {
                for (int k = 0; k < n; k++)
                {
                    (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                    (inverse[col, k], inverse[pivot, k]) = (inverse[pivot, k], inverse[col, k]);
                }
            }

            byte scale = Inverse(a[col, col]);
            for (int k = 0; k < n; k++)
            {
                a[col, k] = Multiply(a[col, k], scale);
                inverse[col, k] = Multiply(inverse[col, k], scale);
            }

            for (int row = 0; row < n; row++)
            {
                if (row != col && a[row, col] != 0)
                {
                    byte factor = a[row, col];
                    for (int k = 0; k < n; k++)
                    {
                        a[row, k] ^= Multiply(factor, a[col, k]);
                        inverse[row, k] ^= Multiply(factor, inverse[col, k]);
                    }
                }
            }
        }

        return inverse;
    }

    /// <summary>destino ^= coeficiente · origen (byte a byte, con una tabla de 256 productos).</summary>
    private static void MultiplyAdd(byte coefficient, byte[] source, byte[] target, int length)
    {
        if (coefficient == 0)
        {
            return;
        }

        Span<byte> table = stackalloc byte[256];
        for (int v = 0; v < 256; v++)
        {
            table[v] = Multiply(coefficient, (byte)v);
        }

        for (int i = 0; i < length; i++)
        {
            target[i] ^= table[source[i]];
        }
    }
}
