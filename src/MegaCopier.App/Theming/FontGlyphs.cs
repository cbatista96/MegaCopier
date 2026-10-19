using System.Runtime.InteropServices;

namespace MegaCopier.App.Theming;

/// <summary>
/// ¿Tiene una fuente instalada este carácter? Se pregunta a GDI una vez por fuente y carácter (sin sustituir por otra
/// fuente) y se recuerda. Sirve para que un conjunto de iconos con su propia fuente nunca muestre un hueco.
/// </summary>
internal static unsafe partial class FontGlyphs
{
    private static readonly Dictionary<(string Font, char Glyph), bool> Known = [];
    private static readonly Lock Gate = new();

    public static bool Has(string font, char glyph)
    {
        lock (Gate)
        {
            if (Known.TryGetValue((font, glyph), out bool has))
            {
                return has;
            }

            has = Check(font, glyph);
            Known[(font, glyph)] = has;
            return has;
        }
    }

    private static bool Check(string font, char glyph)
    {
        nint dc = CreateCompatibleDC(0);
        nint handle = CreateFontW(-32, 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 0, 0, font);
        try
        {
            if (dc == 0 || handle == 0)
            {
                return true; // ante la duda, la del conjunto
            }

            nint old = SelectObject(dc, handle);
            ushort index = 0;
            uint result = GetGlyphIndicesW(dc, &glyph, 1, &index, 1 /* GGI_MARK_NONEXISTING_GLYPHS */);
            SelectObject(dc, old);
            return result == uint.MaxValue || index != 0xFFFF;
        }
        finally
        {
            DeleteObject(handle);
            DeleteDC(dc);
        }
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    private static partial uint GetGlyphIndicesW(nint dc, char* text, int count, ushort* indices, uint flags);
}
