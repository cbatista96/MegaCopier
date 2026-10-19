using System.Runtime.InteropServices;
using Windows.UI;

namespace MegaCopier.App.Services;

/// <summary>Cómo va la copia que dibuja un icono de progreso de la bandeja.</summary>
internal enum TrayProgressState
{
    Running,
    Paused,
    Error,
}

/// <summary>
/// Dibuja el icono de progreso de una copia para la bandeja: un recuadro redondeado que se va llenando de abajo arriba
/// con el degradado de las barras del tema (ámbar en pausa, rojo con errores) y el porcentaje encima, con un contorno
/// para que se lea sobre la barra y sobre la barra de tareas, clara u oscura. Se dibuja píxel a píxel (con suavizado);
/// solo el número usa GDI, como máscara.
/// </summary>
internal static unsafe partial class TrayProgressPainter
{
    /// <summary>Un HICON nuevo (quien lo recibe lo destruye con DestroyIcon).</summary>
    /// <param name="done">Terminada: barra llena y una marca en lugar del número.</param>
    public static nint Create(int size, double fraction, bool done, TrayProgressState state, Color barStart, Color barEnd, bool lightTaskbar) =>
        ToIcon(Render(size, fraction, done, state, barStart, barEnd, lightTaskbar), size);

    /// <summary>Los píxeles del icono (ARGB sin premultiplicar, de arriba abajo).</summary>
    internal static uint[] Render(int size, double fraction, bool done, TrayProgressState state, Color barStart, Color barEnd, bool lightTaskbar)
    {
        fraction = done ? 1 : Math.Clamp(fraction, 0, 1);
        var pixels = new Rgba[size * size];
        float stroke = size >= 24 ? 1.5f : 1f;
        float radius = size * 0.24f;
        float inset = 0.5f; // el borde cae entero dentro del icono

        // Colores según la barra de tareas: marco y fondo claros sobre una oscura, oscuros sobre una clara.
        var frame = lightTaskbar ? new Rgba(0x20, 0x20, 0x20, 0.78f) : new Rgba(0xFF, 0xFF, 0xFF, 0.82f);
        var back = lightTaskbar ? new Rgba(0x00, 0x00, 0x00, 0.07f) : new Rgba(0xFF, 0xFF, 0xFF, 0.12f);
        (barStart, barEnd) = state switch
        {
            TrayProgressState.Paused => (Color.FromArgb(255, 0xF5, 0xB8, 0x3D), Color.FromArgb(255, 0xE0, 0x8E, 0x0B)),
            TrayProgressState.Error => (Color.FromArgb(255, 0xF2, 0x6B, 0x70), Color.FromArgb(255, 0xD9, 0x34, 0x3C)),
            _ => (barStart, barEnd),
        };

        float innerTop = inset + stroke, innerBottom = size - inset - stroke;
        float level = innerBottom - ((innerBottom - innerTop) * (float)fraction); // donde empieza la barra (de abajo arriba)
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float distance = RoundedRect(px, py, inset, inset, size - inset, size - inset, radius);
                float outer = Coverage(distance);
                float inner = Coverage(distance + stroke);
                if (outer <= 0)
                {
                    continue;
                }

                var pixel = back.With(back.A * inner);

                // La barra: de abajo hasta el nivel, con el borde superior suavizado.
                float bar = inner * Math.Clamp(py - level + 0.5f, 0, 1);
                if (bar > 0)
                {
                    float t = Math.Clamp((py - innerTop) / Math.Max(1, innerBottom - innerTop), 0, 1);
                    var color = Mix(barStart, barEnd, t);
                    pixel = Over(pixel, new Rgba(color.R, color.G, color.B, 0.95f * bar));
                }

                pixel = Over(pixel, frame.With(frame.A * (outer - inner)));
                pixels[(y * size) + x] = pixel;
            }
        }

        // El porcentaje (hasta 99: al llegar a 100 la copia termina y el icono se va), con contorno.
        string text = done ? "✓" :((int)Math.Min(99, Math.Floor(fraction * 100))).ToString(System.Globalization.CultureInfo.InvariantCulture);
        float[] mask = TextMask(text, size);
        float[] halo = Dilate(mask, size);
        var ink = lightTaskbar ? new Rgba(0x14, 0x14, 0x14, 1) : new Rgba(0xFF, 0xFF, 0xFF, 1);
        var outline = lightTaskbar ? new Rgba(0xFF, 0xFF, 0xFF, 0.9f) : new Rgba(0x00, 0x00, 0x00, 0.72f);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Over(Over(pixels[i], outline.With(outline.A * halo[i])), ink.With(mask[i]));
        }

        var argb = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            uint alpha = (uint)Math.Round(Math.Clamp(p.A, 0, 1) * 255);
            argb[i] = (alpha << 24) | ((uint)p.R << 16) | ((uint)p.G << 8) | p.B; // alfa sin premultiplicar
        }

        return argb;
    }

    /// <summary>Distancia con signo a un rectángulo redondeado (negativa dentro).</summary>
    private static float RoundedRect(float x, float y, float left, float top, float right, float bottom, float radius)
    {
        float cx = (left + right) / 2, cy = (top + bottom) / 2;
        float hx = ((right - left) / 2) - radius, hy = ((bottom - top) / 2) - radius;
        float dx = Math.Abs(x - cx) - hx, dy = Math.Abs(y - cy) - hy;
        float ox = Math.Max(dx, 0), oy = Math.Max(dy, 0);
        return MathF.Sqrt((ox * ox) + (oy * oy)) + Math.Min(Math.Max(dx, dy), 0) - radius;
    }

    /// <summary>Cuánto de un píxel cubre la forma, según su distancia al borde (suavizado de un píxel).</summary>
    private static float Coverage(float distance) => Math.Clamp(0.5f - distance, 0, 1);

    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        255, (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    /// <summary>Pinta <paramref name="top"/> encima de <paramref name="bottom"/> (alfa normal).</summary>
    private static Rgba Over(Rgba bottom, Rgba top)
    {
        float a = top.A + (bottom.A * (1 - top.A));
        if (a <= 0.0001f)
        {
            return default;
        }

        float Blend(byte t, byte b) => ((t * top.A) + (b * bottom.A * (1 - top.A))) / a;
        return new Rgba((byte)Math.Round(Blend(top.R, bottom.R)), (byte)Math.Round(Blend(top.G, bottom.G)), (byte)Math.Round(Blend(top.B, bottom.B)), a);
    }

    /// <summary>
    /// El texto como máscara (0 a 1), centrado en el icono por lo que de verdad ocupa (las cifras no tienen la altura
    /// de la línea entera): se dibuja con GDI en grande y se recoloca.
    /// </summary>
    private static float[] TextMask(string text, int size)
    {
        int canvas = size * 2;
        var result = new float[size * size];
        nint dc = CreateCompatibleDC(0);
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = canvas, Height = -canvas, Planes = 1, BitCount = 32 };
        nint bitmap = CreateDIBSection(dc, &header, 0, out nint bits, 0, 0);
        int height = (int)Math.Round(size * (text.Length > 1 ? 0.54 : 0.6));
        nint font = CreateFontW(-height, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, "Segoe UI");
        try
        {
            if (dc == 0 || bitmap == 0 || bits == 0)
            {
                return result;
            }

            nint oldBitmap = SelectObject(dc, bitmap);
            nint oldFont = SelectObject(dc, font);
            SetTextColor(dc, 0x00FFFFFF);
            SetBkMode(dc, 1 /* TRANSPARENT */);
            var rect = new Rect { Right = canvas, Bottom = canvas };
            DrawTextW(dc, text, -1, &rect, 0x1 | 0x4 | 0x20 | 0x800 /* CENTER | VCENTER | SINGLELINE | NOPREFIX */);
            GdiFlush();

            // Lo que ocupa de verdad, para centrarlo.
            uint* data = (uint*)bits;
            int minX = canvas, minY = canvas, maxX = -1, maxY = -1;
            for (int y = 0; y < canvas; y++)
            {
                for (int x = 0; x < canvas; x++)
                {
                    if ((data[(y * canvas) + x] & 0xFF) > 24)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            if (maxX >= 0)
            {
                int offsetX = minX - ((size - (maxX - minX + 1)) / 2);
                int offsetY = minY - ((size - (maxY - minY + 1)) / 2);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int sx = x + offsetX, sy = y + offsetY;
                        if (sx >= 0 && sy >= 0 && sx < canvas && sy < canvas)
                        {
                            result[(y * size) + x] = (data[(sy * canvas) + sx] & 0xFF) / 255f;
                        }
                    }
                }
            }

            SelectObject(dc, oldFont);
            SelectObject(dc, oldBitmap);
            return result;
        }
        finally
        {
            DeleteObject(font);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    /// <summary>El contorno del texto: la máscara engordada un píxel.</summary>
    private static float[] Dilate(float[] mask, int size)
    {
        var result = new float[mask.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float max = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < size && ny < size)
                        {
                            max = Math.Max(max, mask[(ny * size) + nx] * (dx != 0 && dy != 0 ? 0.7f : 1f));
                        }
                    }
                }

                result[(y * size) + x] = max;
            }
        }

        return result;
    }

    private static nint ToIcon(uint[] pixels, int size)
    {
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = size, Height = -size, Planes = 1, BitCount = 32 };
        nint color = CreateDIBSection(0, &header, 0, out nint bits, 0, 0);
        nint mask = CreateBitmap(size, size, 1, 1, null);
        try
        {
            if (color == 0 || bits == 0 || mask == 0)
            {
                return 0;
            }

            pixels.CopyTo(new Span<uint>((uint*)bits, pixels.Length));
            var info = new IconInfo { IsIcon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(&info);
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    /// <summary>Un color con opacidad de 0 a 1.</summary>
    private readonly record struct Rgba(byte R, byte G, byte B, float A)
    {
        public Rgba With(float alpha) => this with { A = Math.Clamp(alpha, 0, 1) };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint dc, uint color);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawTextW(nint dc, string text, int length, Rect* rect, uint format);

    [LibraryImport("user32.dll")]
    private static partial nint CreateIconIndirect(IconInfo* info);
}
