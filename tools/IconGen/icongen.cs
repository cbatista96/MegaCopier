// Genera el icono de MegaCopier (ICO con varios tamaños) sin dependencias externas.
// Uso: dotnet run tools/IconGen/icongen.cs -- <salida.ico> [preview.png-no-soportado]
using System.Buffers.Binary;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
string output = args.Length > 0 ? args[0] : "MegaCopier.ico";

var images = sizes.Select(s => (Size: s, Data: EncodeBmp(Render(s), s))).ToList();

using var file = File.Create(output);
using var w = new BinaryWriter(file);
w.Write((ushort)0);            // reservado
w.Write((ushort)1);            // tipo: icono
w.Write((ushort)images.Count);
int offset = 6 + (16 * images.Count);
foreach (var (size, data) in images)
{
    w.Write((byte)(size >= 256 ? 0 : size));
    w.Write((byte)(size >= 256 ? 0 : size));
    w.Write((byte)0);          // colores de paleta
    w.Write((byte)0);
    w.Write((ushort)1);        // planos
    w.Write((ushort)32);       // bits por píxel
    w.Write(data.Length);
    w.Write(offset);
    offset += data.Length;
}

foreach (var (_, data) in images)
{
    w.Write(data);
}

Console.WriteLine($"Icono generado: {Path.GetFullPath(output)} ({string.Join(", ", sizes)} px)");

// RGBA en orden de filas de arriba a abajo, alfa sin premultiplicar.
static float[] Render(int n)
{
    var px = new float[n * n * 4];
    const int ss = 4; // supermuestreo para bordes suaves
    for (int y = 0; y < n; y++)
    {
        for (int x = 0; x < n; x++)
        {
            float r = 0, g = 0, b = 0, a = 0;
            for (int sy = 0; sy < ss; sy++)
            {
                for (int sx = 0; sx < ss; sx++)
                {
                    float u = (x + (sx + 0.5f) / ss) / n;
                    float v = (y + (sy + 0.5f) / ss) / n;
                    var c = Sample(u, v, n);
                    // composición "sobre" acumulando premultiplicado
                    r += c.R * c.A; g += c.G * c.A; b += c.B * c.A; a += c.A;
                }
            }

            int i = ((y * n) + x) * 4;
            float count = ss * ss;
            px[i + 3] = a / count;
            if (a > 0)
            {
                px[i] = r / a; px[i + 1] = g / a; px[i + 2] = b / a;
            }
        }
    }

    return px;
}

static (float R, float G, float B, float A) Sample(float u, float v, int n)
{
    // Fondo: cuadrado redondeado con degradado diagonal azul → violeta.
    float bg = RoundRect(u, v, 0.5f, 0.5f, 0.47f, 0.47f, 0.2f);
    if (bg > 0)
    {
        return (0, 0, 0, 0);
    }

    float t = Math.Clamp((u + v) / 2f, 0, 1);
    (float R, float G, float B) color = (Lerp(0.22f, 0.45f, t), Lerp(0.47f, 0.25f, t), Lerp(1.00f, 0.96f, t));

    // Dos documentos superpuestos: el de atrás translúcido, el de delante blanco.
    float back = RoundRect(u, v, 0.43f, 0.43f, 0.19f, 0.24f, 0.05f);
    float front = RoundRect(u, v, 0.57f, 0.57f, 0.19f, 0.24f, 0.05f);

    if (front <= 0)
    {
        // Líneas de "texto" en el documento delantero (solo si hay resolución para verlas).
        if (n >= 32)
        {
            foreach (float ly in (float[])[0.50f, 0.58f, 0.66f])
            {
                float len = ly > 0.6f ? 0.20f : 0.26f;
                if (RoundRect(u, v, 0.57f - ((0.26f - len) / 2f), ly, len / 2f, 0.018f, 0.018f) <= 0)
                {
                    return (color.R, color.G, color.B, 1);
                }
            }
        }

        return (1, 1, 1, 1);
    }

    if (back <= 0)
    {
        return (Lerp(color.R, 1, 0.55f), Lerp(color.G, 1, 0.55f), Lerp(color.B, 1, 0.55f), 1);
    }

    return (color.R, color.G, color.B, 1);
}

// Distancia con signo a un rectángulo redondeado (negativa dentro).
static float RoundRect(float x, float y, float cx, float cy, float hw, float hh, float radius)
{
    float qx = Math.Abs(x - cx) - hw + radius;
    float qy = Math.Abs(y - cy) - hh + radius;
    float outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0));
    return outside + MathF.Min(MathF.Max(qx, qy), 0) - radius;
}

static float Lerp(float a, float b, float t) => a + ((b - a) * t);

static byte[] EncodeBmp(float[] px, int n)
{
    int maskStride = ((n + 31) / 32) * 4;
    var data = new byte[40 + (n * n * 4) + (maskStride * n)];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, 40);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], n);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], n * 2); // XOR + máscara AND
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], 32);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], (n * n * 4) + (maskStride * n));

    int o = 40;
    for (int y = n - 1; y >= 0; y--) // BMP: de abajo a arriba
    {
        for (int x = 0; x < n; x++)
        {
            int i = ((y * n) + x) * 4;
            data[o++] = ToByte(px[i + 2]);
            data[o++] = ToByte(px[i + 1]);
            data[o++] = ToByte(px[i]);
            data[o++] = ToByte(px[i + 3]);
        }
    }

    return data; // la máscara AND queda a cero: la transparencia la da el canal alfa
}

static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0, 255);
