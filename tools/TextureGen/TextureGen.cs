#:package System.Drawing.Common@10.0.0
#:property TargetFramework=net10.0-windows
#:property PublishAot=false
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

// Genera las texturas de fondo de los temas (src/MegaCopier.App/Assets/Textures/*.jpg). Todo se dibuja con matemáticas
// (ruido, ondas, partículas): no hay imágenes de terceros. Uso:  dotnet run tools/TextureGen/TextureGen.cs [carpeta]
string output = args.Length > 0 ? args[0] : Path.Combine("src", "MegaCopier.App", "Assets", "Textures");
Directory.CreateDirectory(output);
const int W = 1600, H = 1000;

Save("brasas", Embers());
Save("madera", Wood());
Save("papel", Paper());
Save("lluvia", DigitalRain());
Save("carbono", Carbon());
Save("aurora", Aurora());
Save("galaxia", Galaxy());
Save("metal", BrushedMetal());
Save("ondas", Waves());
Save("rejilla", NeonGrid());

void Save(string name, Bitmap bitmap)
{
    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    using var parameters = new EncoderParameters(1);
    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 84L);
    string path = Path.Combine(output, name + ".jpg");
    bitmap.Save(path, codec, parameters);
    Console.WriteLine($"{path} ({new FileInfo(path).Length / 1024} KB)");
    bitmap.Dispose();
}

// ---------------------------------------------------------------------------------------------------------------
// Texturas

Bitmap Embers()
{
    var noise = new Noise(11);
    var bmp = Paint((x, y) =>
    {
        double fy = y / (double)H;
        double n = noise.Fbm(x / 260.0, y / 180.0 - (fy * 0.8), 5);
        double heat = Math.Clamp(Math.Pow(fy, 1.6) * 0.9 + ((n - 0.5) * 0.7), 0, 1);
        return Lerp3(Color.FromArgb(10, 5, 4), Color.FromArgb(120, 28, 6), Color.FromArgb(240, 110, 20), heat * 0.85);
    });
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    var random = new Random(5);
    for (int i = 0; i < 420; i++)
    {
        float x = random.Next(W), y = (float)(H * Math.Pow(random.NextDouble(), 0.55));
        float r = (float)(1 + (random.NextDouble() * 3.5));
        Glow(g, x, y, r * 6, Color.FromArgb(255, 160 + random.Next(80), 40 + random.Next(60), 10), 70);
        using var core = new SolidBrush(Color.FromArgb(230, 255, 220, 140));
        g.FillEllipse(core, x - (r / 2), y - (r / 2), r, r);
    }

    return bmp;
}

Bitmap Wood()
{
    var noise = new Noise(3);
    const int plank = 200;
    return Paint((x, y) =>
    {
        int index = x / plank;
        double offset = index * 37.7;
        double grain = noise.Fbm((x % plank) / 90.0 + offset, y / 900.0, 3) * 6;
        double rings = Math.Sin(((x % plank) / 7.0) + (grain * 3.2) + offset);
        double fine = noise.Fbm(x / 3.0, y / 120.0, 2);
        double t = 0.5 + (rings * 0.22) + ((fine - 0.5) * 0.25) + ((index % 3) * 0.05);
        var c = Lerp(Color.FromArgb(150, 104, 66), Color.FromArgb(214, 170, 120), Math.Clamp(t, 0, 1));
        return x % plank < 2 ? Darken(c, 0.55) : c; // junta entre tablas
    });
}

Bitmap Paper()
{
    var noise = new Noise(8);
    var bmp = Paint((x, y) =>
    {
        double fiber = noise.Fbm(x / 2.5, y / 9.0, 2) * 0.5 + noise.Fbm(x / 60.0, y / 60.0, 3) * 0.5;
        return Lerp(Color.FromArgb(236, 228, 205), Color.FromArgb(250, 246, 232), fiber);
    });
    using var g = Graphics.FromImage(bmp);
    using var line = new Pen(Color.FromArgb(55, 90, 140, 200), 1.2f);
    for (int y = 90; y < H; y += 32)
    {
        g.DrawLine(line, 0, y, W, y);
    }

    using var margin = new Pen(Color.FromArgb(90, 220, 80, 80), 1.5f);
    g.DrawLine(margin, 120, 0, 120, H);
    return bmp;
}

Bitmap DigitalRain()
{
    var bmp = Paint((_, _) => Color.FromArgb(0, 6, 2));
    using var g = Graphics.FromImage(bmp);
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    var random = new Random(42);
    string glyphs = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789";
    using var font = new Font("MS Gothic", 20, FontStyle.Bold, GraphicsUnit.Pixel);
    for (int column = 0; column < W / 22; column++)
    {
        int x = column * 22;
        int trails = 1 + random.Next(2);
        for (int t = 0; t < trails; t++)
        {
            int head = random.Next(H + 300);
            int length = 8 + random.Next(26);
            for (int k = 0; k < length; k++)
            {
                int y = head - (k * 24);
                if (y < -24 || y > H)
                {
                    continue;
                }

                int alpha = k == 0 ? 255 : (int)(220 * (1 - (k / (double)length)));
                var color = k == 0 ? Color.FromArgb(alpha, 210, 255, 210) : Color.FromArgb(alpha, 30, 200 + random.Next(55), 70);
                using var brush = new SolidBrush(color);
                g.DrawString(glyphs[random.Next(glyphs.Length)].ToString(), font, brush, x, y);
            }
        }
    }

    return bmp;
}

Bitmap Carbon()
{
    const int cell = 16;
    return Paint((x, y) =>
    {
        int cx = x / cell, cy = y / cell;
        bool horizontal = ((cx + cy) / 2 % 2) == 0; // tejido en sarga 2x2
        double u = (x % cell) / (double)cell, v = (y % cell) / (double)cell;
        double shade = horizontal ? Math.Sin(v * Math.PI) : Math.Sin(u * Math.PI);
        double sheen = horizontal ? 0.6 + (0.4 * Math.Sin(u * Math.PI)) : 0.6 + (0.4 * Math.Sin(v * Math.PI));
        int value = (int)(14 + (shade * sheen * 34));
        return Color.FromArgb(value, value, value + 3);
    });
}

Bitmap Aurora()
{
    var noise = new Noise(21);
    var bmp = Paint((x, y) =>
    {
        double fy = y / (double)H;
        return Lerp(Color.FromArgb(6, 10, 28), Color.FromArgb(14, 22, 48), fy);
    });
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    (Color Color, float Y)[] bands =
    [
        (Color.FromArgb(255, 40, 230, 160), 0.30f),
        (Color.FromArgb(255, 60, 160, 255), 0.42f),
        (Color.FromArgb(255, 170, 90, 255), 0.55f),
    ];
    foreach (var (color, baseY) in bands)
    {
        for (int x = -100; x < W + 100; x += 6)
        {
            double wave = Math.Sin(x / 230.0 + (baseY * 9)) * 70 + ((noise.Fbm(x / 300.0, baseY * 5, 3) - 0.5) * 160);
            float y = (float)((baseY * H) + wave);
            float height = (float)(180 + (noise.Fbm(x / 140.0, baseY * 3, 2) * 200));
            using var brush = new LinearGradientBrush(new RectangleF(x, y - height, 8, height + 2), Color.FromArgb(0, color), Color.FromArgb(26, color), 90f);
            g.FillRectangle(brush, x, y - height, 8, height);
        }
    }

    Stars(g, 260, new Random(7), 150);
    return bmp;
}

Bitmap Galaxy()
{
    var noise = new Noise(31);
    var bmp = Paint((x, y) =>
    {
        double n = noise.Fbm(x / 340.0, y / 340.0, 5);
        double band = Math.Exp(-Math.Pow(((y - (x * 0.35) - 250) / 260.0), 2)); // franja diagonal
        double t = Math.Clamp((n - 0.35) * 1.6 * band, 0, 1);
        var nebula = Lerp3(Color.FromArgb(4, 4, 14), Color.FromArgb(70, 30, 120), Color.FromArgb(40, 120, 200), t);
        return nebula;
    });
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    Stars(g, 1400, new Random(9), 255);
    return bmp;
}

Bitmap BrushedMetal()
{
    var random = new Random(4);
    double[] rows = new double[H];
    for (int y = 0; y < H; y++)
    {
        rows[y] = random.NextDouble();
    }

    var noise = new Noise(5);
    return Paint((x, y) =>
    {
        double streak = (rows[y] * 0.55) + (noise.Fbm(x / 400.0, y / 1.5, 2) * 0.45);
        double light = 0.5 + (0.5 * Math.Cos((x / (double)W * Math.PI * 1.2) - 0.6)); // reflejo suave
        int v = (int)(176 + (streak * 34) + (light * 26));
        return Color.FromArgb(Math.Min(255, v), Math.Min(255, v + 2), Math.Min(255, v + 6));
    });
}

Bitmap Waves()
{
    var bmp = Paint((x, y) => Lerp(Color.FromArgb(60, 170, 220), Color.FromArgb(20, 120, 190), y / (double)H));
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    for (int band = 0; band < 7; band++)
    {
        using var path = new GraphicsPath();
        double phase = band * 1.3, amplitude = 26 + (band * 6), baseY = 260 + (band * 110);
        var points = new List<PointF>();
        for (int x = 0; x <= W; x += 10)
        {
            points.Add(new PointF(x, (float)(baseY + (Math.Sin((x / 260.0) + phase) * amplitude) + (Math.Sin((x / 97.0) + (phase * 2)) * amplitude * 0.3))));
        }

        points.Add(new PointF(W, H));
        points.Add(new PointF(0, H));
        path.AddPolygon(points.ToArray());
        using var brush = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
        g.FillPath(brush, path);
    }

    return bmp;
}

Bitmap NeonGrid()
{
    var bmp = Paint((x, y) =>
    {
        double fy = y / (double)H;
        return fy < 0.55 ? Lerp(Color.FromArgb(10, 4, 30), Color.FromArgb(60, 10, 80), fy / 0.55) : Lerp(Color.FromArgb(30, 6, 50), Color.FromArgb(8, 2, 20), (fy - 0.55) / 0.45);
    });
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    float horizon = H * 0.55f;
    Glow(g, W / 2f, horizon, 380, Color.FromArgb(255, 255, 60, 170), 60); // sol
    for (int i = 1; i < 24; i++)
    {
        float y = horizon + (float)(Math.Pow(i / 24.0, 2.2) * (H - horizon));
        using var pen = new Pen(Color.FromArgb(Math.Min(255, 40 + (i * 9)), 255, 40, 200), 1.6f);
        g.DrawLine(pen, 0, y, W, y);
    }

    for (int i = -20; i <= 20; i++)
    {
        using var pen = new Pen(Color.FromArgb(150, 40, 220, 255), 1.4f);
        g.DrawLine(pen, (W / 2f) + (i * 18), horizon, (W / 2f) + (i * 150), H);
    }

    return bmp;
}

// ---------------------------------------------------------------------------------------------------------------
// Utilidades

Bitmap Paint(Func<int, int, Color> pixel)
{
    var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
    var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    int[] row = new int[W];
    for (int y = 0; y < H; y++)
    {
        for (int x = 0; x < W; x++)
        {
            row[x] = pixel(x, y).ToArgb();
        }

        Marshal.Copy(row, 0, data.Scan0 + (y * data.Stride), W);
    }

    bmp.UnlockBits(data);
    return bmp;
}

void Glow(Graphics g, float x, float y, float radius, Color color, int alpha)
{
    using var path = new GraphicsPath();
    path.AddEllipse(x - radius, y - radius, radius * 2, radius * 2);
    using var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, color), SurroundColors = [Color.FromArgb(0, color)] };
    g.FillPath(brush, path);
}

void Stars(Graphics g, int count, Random random, int maxAlpha)
{
    for (int i = 0; i < count; i++)
    {
        float x = random.Next(W), y = random.Next(H);
        float size = (float)(0.6 + (Math.Pow(random.NextDouble(), 6) * 3));
        using var brush = new SolidBrush(Color.FromArgb(random.Next(maxAlpha / 3, maxAlpha), 255, 255, 255));
        g.FillEllipse(brush, x, y, size, size);
        if (size > 2.4)
        {
            Glow(g, x + (size / 2), y + (size / 2), size * 5, Color.FromArgb(255, 180, 200, 255), 70);
        }
    }
}

static Color Lerp(Color a, Color b, double t)
{
    t = Math.Clamp(t, 0, 1);
    return Color.FromArgb((int)(a.R + ((b.R - a.R) * t)), (int)(a.G + ((b.G - a.G) * t)), (int)(a.B + ((b.B - a.B) * t)));
}

static Color Lerp3(Color a, Color b, Color c, double t) => t < 0.5 ? Lerp(a, b, t * 2) : Lerp(b, c, (t - 0.5) * 2);

static Color Darken(Color c, double amount) => Color.FromArgb((int)(c.R * amount), (int)(c.G * amount), (int)(c.B * amount));

/// <summary>Ruido de valor suavizado con varias octavas (fBm), determinista por semilla.</summary>
sealed class Noise(int seed)
{
    private readonly int[] _p = Enumerable.Range(0, 512).Select(i => new Random(seed + (i % 256)).Next(256)).ToArray();

    public double Fbm(double x, double y, int octaves)
    {
        double sum = 0, amplitude = 0.5, frequency = 1, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value(x * frequency, y * frequency) * amplitude;
            norm += amplitude;
            amplitude *= 0.5;
            frequency *= 2;
        }

        return sum / norm;
    }

    private double Value(double x, double y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi;
        double u = xf * xf * (3 - (2 * xf)), v = yf * yf * (3 - (2 * yf));
        double a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return (a + ((b - a) * u)) + (((c + ((d - c) * u)) - (a + ((b - a) * u))) * v);
    }

    private double Hash(int x, int y) => _p[(_p[x & 255] + y) & 511] / 255.0;
}
