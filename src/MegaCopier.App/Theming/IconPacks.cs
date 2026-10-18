using Windows.UI;

namespace MegaCopier.App.Theming;

/// <summary>Cómo se dibujan los iconos de un conjunto.</summary>
public enum IconStyle
{
    /// <summary>Del color del texto (como Windows).</summary>
    Mono,

    /// <summary>Cada icono con su color.</summary>
    Color,

    /// <summary>Icono blanco sobre un cuadrado redondeado de su color (como la Configuración de Windows 11).</summary>
    Tile,

    /// <summary>Icono de su color sobre un fondo suave del mismo color.</summary>
    Soft,

    /// <summary>Relleno con el degradado de las barras del tema.</summary>
    Gradient,

    /// <summary>Color vivo con un halo de luz alrededor.</summary>
    Neon,

    /// <summary>Icono de su color dentro de un aro fino del mismo color.</summary>
    Outline,
}

/// <summary>Forma del fondo en los conjuntos con fondo (mosaico, suave, contorno).</summary>
public enum IconShape
{
    /// <summary>Cuadrado con las esquinas redondeadas (Windows 11).</summary>
    Rounded,

    /// <summary>Círculo (Android, elementary).</summary>
    Circle,

    /// <summary>Cuadrado de esquinas muy suaves (macOS).</summary>
    Squircle,

    /// <summary>Cuadrado sin redondear (Windows 8).</summary>
    Square,
}

/// <summary>
/// Un conjunto de iconos: estilo, tipo de letra, forma del fondo, paleta de colores (13 tonos, en el orden de
/// <see cref="IconPacks.Tones"/>; sin ella, la de MegaCopier) y brillo de cristal en la mitad de arriba.
/// </summary>
public sealed record IconPack(string Id, IconStyle Style, string FontFamily)
{
    public IconShape Shape { get; init; } = IconShape.Rounded;

    public Color[]? Palette { get; init; }

    public bool Gloss { get; init; }
}

/// <summary>
/// Conjuntos de iconos, independientes del tema: cualquier tema con cualquier conjunto. Todos usan los iconos de Windows
/// 11 (Segoe Fluent Icons), salvo «Windows 10», que usa los de Segoe MDL2 Assets.
/// </summary>
internal static class IconPacks
{
    public const string FluentFont = "Segoe Fluent Icons";

    public static IReadOnlyList<IconPack> All { get; } =
    [
        new("fluent", IconStyle.Mono, FluentFont),
        new("color", IconStyle.Color, FluentFont),
        new("mosaico", IconStyle.Tile, FluentFont),
        new("suave", IconStyle.Soft, FluentFont),
        new("degradado", IconStyle.Gradient, FluentFont),
        new("neon", IconStyle.Neon, FluentFont),
        new("windows10", IconStyle.Mono, "Segoe MDL2 Assets"),

        // Inspirados en otros sistemas: misma colección de iconos, con la forma, la paleta y el acabado de cada uno.
        new("android", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Palette = Hex("0F9D58 F4B400 DB4437 607D8B 4285F4 FBBC04 009688 FF7043 03A9F4 9C27B0 E91E63 AB47BC 3F51B5") },
        new("android-tonal", IconStyle.Soft, FluentFont) { Shape = IconShape.Circle, Palette = Hex("1E8E3E E37400 D93025 5F6368 1A73E8 F9AB00 12B5CB FA7B17 4ECDE6 A142F4 E52592 C5221F 3F51B5") },
        new("macos", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Gloss = true, Palette = Hex("34C759 FF9500 FF3B30 8E8E93 007AFF FFCC00 30B0C7 FF9500 5AC8FA AF52DE FF2D55 BF5AF2 5856D6") },
        new("macos-color", IconStyle.Color, FluentFont) { Palette = Hex("28CD41 FF9F0A FF453A 98989D 0A84FF FFD60A 40C8E0 FF9F0A 64D2FF BF5AF2 FF375F DA8FFF 5E5CE6") },
        new("ubuntu", IconStyle.Tile, FluentFont) { Palette = Hex("0E8420 F99B11 C7162B 77216F E95420 F99B11 19B6EE E95420 19B6EE 77216F 924D8B 5E2750 772953") },
        new("kde", IconStyle.Color, FluentFont) { Palette = Hex("27AE60 F67400 DA4453 7F8C8D 3DAEE9 FDBC4B 1ABC9C F47750 1D99F3 8E44AD E93A9A 9B59B6 2980B9") },
        new("gnome", IconStyle.Soft, FluentFont) { Palette = Hex("2EC27E E5A50A E01B24 77767B 3584E4 F6D32D 2190A4 FF7800 62A0EA 9141AC DC8ADD C061CB 1A5FB4") },
        new("elementary", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Gloss = true, Palette = Hex("68B723 F9C440 C6262E 667885 3689E6 F9C440 28BCA3 F37329 64BAFF A56DE2 DE3E80 7A36B1 0D52BF") },
        new("mint", IconStyle.Soft, FluentFont) { Shape = IconShape.Squircle, Palette = Hex("87CF3E E8A92C D9534F 70806C 3E9BB8 D6C13A 4AB49A E2873A 5DB7C9 8E6BB8 D36E9A A066B0 4C7A9E") },
        new("fedora", IconStyle.Outline, FluentFont) { Shape = IconShape.Circle, Palette = Hex("3C9B3C E8A317 D0332C 6E7E8E 51A2DA E5C62A 2DA6A0 EB7A2F 51A2DA 7B5EB5 D1478C A04DB8 294172") },
        new("popos", IconStyle.Tile, FluentFont) { Palette = Hex("73C48F FAA41A F15D22 574F4A 48B9C7 FBB86C 48B9C7 FAA41A 94EBEB A86F9F E36F7A 9E6FA8 4C6A92") },
        new("arch", IconStyle.Outline, FluentFont) { Shape = IconShape.Rounded, Palette = Hex("3FA34D E8A317 D9453B 7A8590 1793D1 E5C62A 1BA39C E47E26 1793D1 7D5BC4 D1458E A045B5 1793D1") },
        new("winxp", IconStyle.Tile, FluentFont) { Gloss = true, Palette = Hex("3C8D2E F3A10C E0432B 6D7F99 245EDC FFD34E 2E9E9A F07D21 4A9BF0 7B5AC2 D9478A A64DBF 2F49C7") },
        new("win7", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Gloss = true, Palette = Hex("2F9E3F E8A317 D23A2F 5B6F86 1E73C9 F2C230 1BA39C E47E26 3BA0E8 7F4FC9 D1458E A045B5 3653C4") },
        new("metro", IconStyle.Tile, FluentFont) { Shape = IconShape.Square, Palette = Hex("00A300 F09609 E51400 647687 1BA1E2 E3C800 00ABA9 FA6800 1BA1E2 A200FF FF0097 D80073 6A00FF") },
        new("steam", IconStyle.Soft, FluentFont) { Palette = Hex("5BA32B E4A11B C94F3C 8F98A0 1A9FFF FFC82C 66C0F4 E77A2F 66C0F4 8E5EC8 D9558F A866C0 417A9B") },
    ];

    /// <summary>Los 13 tonos por significado, en el orden de las paletas de cada conjunto.</summary>
    /// (Propiedad y no campo: los campos estáticos se inician en orden y los colores están declarados más abajo.)
    internal static Color[] Tones => s_tones ??= [Green, Amber, Red, Slate, Blue, Yellow, Teal, Orange, Sky, Purple, Pink, Magenta, Indigo];

    private static Color[]? s_tones;

    /// <summary>Color de un icono en un conjunto: el tono por su significado, tomado de la paleta del conjunto.</summary>
    public static Color ColorFor(string glyph, IconPack pack)
    {
        var color = DefaultColorFor(glyph);
        if (pack.Palette is not { Length: 13 } palette)
        {
            return color;
        }

        int tone = Array.IndexOf(Tones, color);
        return palette[tone < 0 ? 4 : tone];
    }

    private static Color[] Hex(string colors) =>
        [.. colors.Split(' ').Select(h => Color.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..], 16)))];

    public static IconPack Current { get; private set; } = All[0];

    /// <summary>Cambió el conjunto (los iconos visibles se vuelven a dibujar).</summary>
    public static event Action? Changed;

    public static IconPack Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    public static void Apply(string id)
    {
        var pack = Find(id);
        if (pack == Current)
        {
            return;
        }

        Current = pack;
        Changed?.Invoke();
    }

    /// <summary>
    /// Color propio de cada icono (para los conjuntos de color): por su significado (reproducir verde, pausa ámbar,
    /// detener rojo...) y, si no se conoce, uno fijo de la paleta según el icono.
    /// </summary>
    private static Color DefaultColorFor(string glyph)
    {
        if (glyph.Length == 0)
        {
            return Palette[0];
        }

        return glyph[0] switch
        {
            '' or '' or '' or '' or '' or '' or '' => Green,       // reproducir, aceptar, escudo
            '' or '' or '' or '' or '' or '' => Amber,                   // pausa, cronómetro, historial
            '' or '' or '' or '' or '' or '' or '' => Red,          // detener, borrar, cerrar, error
            '' or '' or '' or '' or '' => Slate,                                // ajustes, herramientas
            '' or '' or '' or '' or '' or '' => Blue,                     // copiar, documentos, listas
            '' or '' or '' or '' or '' or '' or '' => Yellow,       // carpetas
            '' or '' or '' or '' or '' => Teal,                                 // sincronizar, actualizar
            '' or '' or '' => Orange,                                                       // candado, permisos
            '' or '' or '' or '' or '' or '' => Sky,                      // nube, red, mundo
            '' or '' or '' or '' or '' => Purple,                               // móvil, dispositivos
            '' or '' or '' or '' => Pink,                                             // sonido
            '' or '' or '' => Magenta,                                                      // personalizar, colores
            '' or '' or '' => Blue,                                                         // información, ayuda
            '' or '' or '' or '' => Indigo,                                           // discos, unidades
            _ => Palette[glyph[0] % Palette.Length],
        };
    }

    private static readonly Color Green = Color.FromArgb(255, 34, 177, 76);
    private static readonly Color Amber = Color.FromArgb(255, 240, 160, 20);
    private static readonly Color Red = Color.FromArgb(255, 226, 60, 64);
    private static readonly Color Slate = Color.FromArgb(255, 110, 128, 150);
    private static readonly Color Blue = Color.FromArgb(255, 30, 120, 230);
    private static readonly Color Yellow = Color.FromArgb(255, 230, 175, 30);
    private static readonly Color Teal = Color.FromArgb(255, 0, 170, 160);
    private static readonly Color Orange = Color.FromArgb(255, 240, 110, 30);
    private static readonly Color Sky = Color.FromArgb(255, 40, 170, 240);
    private static readonly Color Purple = Color.FromArgb(255, 140, 80, 220);
    private static readonly Color Pink = Color.FromArgb(255, 230, 70, 150);
    private static readonly Color Magenta = Color.FromArgb(255, 200, 60, 200);
    private static readonly Color Indigo = Color.FromArgb(255, 80, 90, 210);

    private static readonly Color[] Palette = [Blue, Teal, Purple, Orange, Green, Pink, Sky, Indigo, Amber, Magenta];
}
