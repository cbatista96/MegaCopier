using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MegaCopier.App.Services;

/// <summary>
/// El texto de una imagen con el OCR que trae Windows (en el idioma del usuario, sin internet; usa la tarjeta gráfica
/// si puede). Para el filtro "fotos con el texto…": capturas de pantalla, tiques, documentos escaneados, carteles...
/// </summary>
internal static class WindowsOcr
{
    private static readonly Lazy<OcrEngine?> Engine = new(() =>
        OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")));

    public static bool Available => Engine.Value is not null;

    /// <summary>El texto de la imagen, o null si no se pudo leer (no es una imagen, está dañada, no hay OCR...).</summary>
    public static string? Read(string path)
    {
        if (Engine.Value is not { } engine)
        {
            return null;
        }

        try
        {
            return ReadAsync(engine, path).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static async Task<string> ReadAsync(OcrEngine engine, string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            double scale = (double)max / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Text;
    }
}
