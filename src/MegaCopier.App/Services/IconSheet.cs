using MegaCopier.App.Theming;
using Windows.UI;

namespace MegaCopier.App.Services;

/// <summary>
/// Solo pruebas: una hoja con los iconos de cada conjunto dibujados por <see cref="IconPainter"/> (los de los menús) y
/// los iconos de progreso de la bandeja (en marcha, en pausa, con errores y terminado), para revisarlos de un vistazo.
/// </summary>
internal static class IconSheet
{
    private static readonly string[] Glyphs = ["", "", "", "", "", "", "", "", "", ""];

    public static async Task WriteAsync(string path, int size)
    {
        var packs = IconPacks.All;
        int cell = size + 8;
        int columns = Glyphs.Length + 5;
        int width = columns * cell, height = packs.Count * cell;
        var sheet = new byte[width * height * 4];
        var background = ThemeManager.Current.Dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        for (int i = 0; i < width * height; i++)
        {
            sheet[(i * 4) + 0] = background.B;
            sheet[(i * 4) + 1] = background.G;
            sheet[(i * 4) + 2] = background.R;
            sheet[(i * 4) + 3] = 255;
        }

        var text = ThemeManager.Current.Dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 26, 26, 26);
        var theme = ThemeManager.Current;
        for (int row = 0; row < packs.Count; row++)
        {
            var pack = packs[row];
            for (int column = 0; column < Glyphs.Length; column++)
            {
                Blit(sheet, width, IconPainter.Render(Glyphs[column], pack, size, text), size, (column * cell) + 4, (row * cell) + 4, background);
            }

            var cases = new (double Fraction, bool Done, TrayProgressState State)[]
            {
                (0.37, false, TrayProgressState.Running), (0.62, false, TrayProgressState.Paused), (0.5, false, TrayProgressState.Error), (1, true, TrayProgressState.Running),
            };
            for (int i = 0; i < cases.Length; i++)
            {
                var (fraction, done, state) = cases[i];
                var tray = TrayProgressPainter.Render(size, fraction, done, state, ThemeManager.Parse(theme.TotalStart), ThemeManager.Parse(theme.TotalEnd), !theme.Dark, pack);
                Blit(sheet, width, tray, size, ((Glyphs.Length + 1 + i) * cell) + 4, (row * cell) + 4, background);
            }
        }

        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, sheet);
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.AsStreamForRead().ReadExactlyAsync(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }

    /// <summary>Pinta un icono (ARGB sin premultiplicar) sobre la hoja, mezclado con el fondo.</summary>
    private static void Blit(byte[] sheet, int width, uint[] icon, int size, int left, int top, Color background)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                uint p = icon[(y * size) + x];
                double a = (p >> 24) / 255.0;
                int i = (((top + y) * width) + left + x) * 4;
                sheet[i + 0] = (byte)Math.Round(((p & 0xFF) * a) + (background.B * (1 - a)));
                sheet[i + 1] = (byte)Math.Round((((p >> 8) & 0xFF) * a) + (background.G * (1 - a)));
                sheet[i + 2] = (byte)Math.Round((((p >> 16) & 0xFF) * a) + (background.R * (1 - a)));
            }
        }
    }
}
