using MegaCopier.App.Theming;
using Windows.UI;
using static MegaCopier.App.Services.TrayProgressPainter;

namespace MegaCopier.App.Services;

/// <summary>
/// Dibuja un icono como lo dibuja <see cref="Controls.McIcon"/> con cada conjunto (fondo con su forma, color plano o
/// degradado, brillo de cristal, relieve, contorno, halo de neón...), pero en píxeles: para los sitios que no aceptan
/// un control, como los menús de WinUI (que solo admiten un icono) y los iconos de la bandeja del sistema. Así todos
/// los iconos de MegaCopier siguen el conjunto elegido.
/// </summary>
internal static class IconPainter
{
    /// <summary>Los píxeles del icono (ARGB sin premultiplicar, de arriba abajo), de <paramref name="size"/> × <paramref name="size"/>.</summary>
    /// <param name="text">Color del texto (el de los conjuntos monocromos que no van en color de acento).</param>
    /// <param name="accent">En los conjuntos monocromos, del color de acento del tema.</param>
    public static uint[] Render(string glyph, IconPack pack, int size, Color text, bool accent = false)
    {
        bool dark = ThemeManager.Current.Dark;
        Color color = Adjust(IconPacks.ColorFor(glyph, pack), dark);
        string font = IconPacks.FontFor(glyph, pack);
        var pixels = new Rgba[size * size];
        bool tiled = pack.Style is IconStyle.Tile or IconStyle.Soft or IconStyle.Outline;

        // El icono en un fondo ocupa lo que ocuparía sin él (como McIcon: la caja mide 1,45 veces la letra).
        double letter = tiled ? size / 1.45 : pack.Style == IconStyle.Neon ? size * 0.72 : size;
        double em = tiled ? letter * (pack.Shape == IconShape.Circle ? 0.76 : 0.82) : letter;
        float[] mask = TextMask(glyph, size, font, 400, em);

        Rgba ink;
        if (tiled)
        {
            ink = PaintTile(pixels, size, pack, color, IconPacks.ColorFor(glyph, pack), letter);
        }
        else
        {
            ink = pack.Style switch
            {
                IconStyle.Color => Solid(color),
                IconStyle.Neon => Solid(Lighten(color, 0.25)),
                IconStyle.Mono when accent => Solid(ThemeManager.Parse(ThemeManager.Current.Accent)),
                _ => Solid(text),
            };

            if (pack.Style == IconStyle.Neon)
            {
                // Halo de neón: una capa ceñida e intensa y otra amplia y suave, del color del icono.
                var tight = Blur(mask, size, Math.Max(1, (int)Math.Round(size * 0.06)));
                var wide = Blur(mask, size, Math.Max(2, (int)Math.Round(size * 0.16)));
                for (int i = 0; i < pixels.Length; i++)
                {
                    float glow = Math.Clamp((tight[i] * 1.4f) + (wide[i] * 0.9f), 0, 1);
                    pixels[i] = Solid(color).With(glow * 0.85f);
                }
            }
        }

        // La letra: degradado del tema o el color que toca.
        var theme = ThemeManager.Current;
        Color start = ThemeManager.Parse(theme.TotalStart), end = ThemeManager.Parse(theme.TotalEnd);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                if (mask[i] <= 0)
                {
                    continue;
                }

                var paint = pack.Style == IconStyle.Gradient ? Solid(Mix(start, end, (x + y) / (2f * size))) : ink;
                pixels[i] = Over(pixels[i], paint.With(paint.A * mask[i]));
            }
        }

        return ToArgb(pixels);
    }

    /// <summary>El fondo (forma, color, brillo, relieve o contorno). Devuelve el color de la letra.</summary>
    private static Rgba PaintTile(Rgba[] pixels, int size, IconPack pack, Color color, Color raw, double letter)
    {
        float radius = pack.Shape switch
        {
            IconShape.Circle => size / 2f,
            IconShape.Squircle => size * 0.36f,
            IconShape.Square => 0,
            _ => size * 0.28f,
        };
        float border = pack.Style == IconStyle.Outline ? Math.Max(1, (float)Math.Round(letter / 14)) : 0;
        float bevel = pack.Bevel ? Math.Max(1, (float)Math.Round(letter / 12)) : 0;
        bool vertical = pack.Shape == IconShape.Squircle;
        Color light = Lighten(color, 0.18), deep = Darken(color, 0.18);

        Rgba ink;
        if (pack.Style == IconStyle.Tile && pack.TileColor is not null)
        {
            ink = Solid(raw); // fondo fijo (Steam, Nothing, Windows 95): la letra lleva el color de su significado
        }
        else
        {
            ink = pack.Style == IconStyle.Tile ? Solid(Color.FromArgb(255, 255, 255, 255)) : Solid(color);
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float distance = RoundedRect(px, py, 0, 0, size, size, radius);
                float cover = Coverage(distance);
                if (cover <= 0)
                {
                    continue;
                }

                Rgba back = pack.Style switch
                {
                    IconStyle.Tile when pack.TileColor is { } tile => Solid(tile),
                    IconStyle.Tile when pack.Flat => Solid(color),
                    IconStyle.Tile => Solid(Mix(light, deep, vertical ? py / size : (px + py) / (2f * size))),
                    IconStyle.Soft => Solid(color).With(48 / 255f),
                    _ => Solid(color).With(18 / 255f),
                };
                var pixel = back.With(back.A * cover);

                if (border > 0)
                {
                    float ring = cover - Coverage(distance + border);
                    pixel = Over(pixel, Solid(color).With(ring));
                }

                if (pack.Gloss && py < size * 0.5f)
                {
                    // Reflejo de cristal en la mitad de arriba: blanco que se desvanece hacia abajo.
                    float inner = Coverage(distance + 1);
                    float alpha = ((150 - (125 * (py / (size * 0.5f)))) / 255f) * inner;
                    pixel = Over(pixel, Solid(Color.FromArgb(255, 255, 255, 255)).With(alpha));
                }

                if (bevel > 0)
                {
                    // Relieve de Windows 95: oscuro abajo y a la derecha, claro arriba y a la izquierda.
                    if (px > size - bevel || py > size - bevel)
                    {
                        pixel = Over(pixel, Solid(Color.FromArgb(255, 64, 64, 64)).With(cover));
                    }

                    if (px < bevel || py < bevel)
                    {
                        pixel = Over(pixel, Solid(Color.FromArgb(255, 255, 255, 255)).With(cover));
                    }
                }

                pixels[(y * size) + x] = pixel;
            }
        }

        return ink;
    }

    /// <summary>Los mismos píxeles en el formato de un WriteableBitmap: BGRA premultiplicado.</summary>
    public static byte[] ToPremultipliedBgra(uint[] argb)
    {
        var bytes = new byte[argb.Length * 4];
        for (int i = 0; i < argb.Length; i++)
        {
            uint p = argb[i];
            uint a = p >> 24;
            bytes[(i * 4) + 0] = (byte)((p & 0xFF) * a / 255);
            bytes[(i * 4) + 1] = (byte)(((p >> 8) & 0xFF) * a / 255);
            bytes[(i * 4) + 2] = (byte)(((p >> 16) & 0xFF) * a / 255);
            bytes[(i * 4) + 3] = (byte)a;
        }

        return bytes;
    }

    private static uint[] ToArgb(Rgba[] pixels)
    {
        var argb = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            uint alpha = (uint)Math.Round(Math.Clamp(p.A, 0, 1) * 255);
            argb[i] = (alpha << 24) | ((uint)p.R << 16) | ((uint)p.G << 8) | p.B;
        }

        return argb;
    }

    /// <summary>Desenfoque de caja (dos pasadas, horizontal y vertical).</summary>
    private static float[] Blur(float[] mask, int size, int radius)
    {
        var temp = new float[mask.Length];
        var result = new float[mask.Length];
        float scale = 1f / ((radius * 2) + 1);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = x + k;
                    if (sx >= 0 && sx < size)
                    {
                        sum += mask[(y * size) + sx];
                    }
                }

                temp[(y * size) + x] = sum * scale;
            }
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = y + k;
                    if (sy >= 0 && sy < size)
                    {
                        sum += temp[(sy * size) + x];
                    }
                }

                result[(y * size) + x] = sum * scale;
            }
        }

        return result;
    }

    private static Rgba Solid(Color c) => new(c.R, c.G, c.B, c.A / 255f);

    /// <summary>En temas oscuros los colores se aclaran un poco para que se lean bien (como en McIcon).</summary>
    private static Color Adjust(Color color, bool dark) => dark ? Lighten(color, 0.12) : color;

    private static Color Lighten(Color c, double amount) => Color.FromArgb(
        c.A, (byte)(c.R + ((255 - c.R) * amount)), (byte)(c.G + ((255 - c.G) * amount)), (byte)(c.B + ((255 - c.B) * amount)));

    private static Color Darken(Color c, double amount) => Color.FromArgb(
        c.A, (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
}
