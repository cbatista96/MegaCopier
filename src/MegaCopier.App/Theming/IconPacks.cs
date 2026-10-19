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
/// <see cref="IconPacks.Tones"/>; sin ella, la de MegaCopier) y el acabado de cada sistema en el que se inspira.
/// </summary>
public sealed record IconPack(string Id, IconStyle Style, string FontFamily)
{
    public IconShape Shape { get; init; } = IconShape.Rounded;

    public Color[]? Palette { get; init; }

    /// <summary>Brillo de cristal en la mitad de arriba del fondo (Windows XP, Vista y 7, macOS, elementary).</summary>
    public bool Gloss { get; init; }

    /// <summary>Un solo color para todos los iconos, sin tonos por significado (GNOME, Fedora, Arch, holográfico).</summary>
    public Color? Tint { get; init; }

    /// <summary>Los colores salen del acento del tema elegido (Material You: «color dinámico»).</summary>
    public bool FromTheme { get; init; }

    /// <summary>Fondo plano, sin degradado (Android, One UI, Linux Mint, Arch, Windows 8).</summary>
    public bool Flat { get; init; }

    /// <summary>Fondo de un color fijo para todos (Steam, Nothing, Windows 95); el icono lleva el color de la paleta.</summary>
    public Color? TileColor { get; init; }

    /// <summary>Relieve en 3D, claro arriba a la izquierda y oscuro abajo a la derecha, como los botones de Windows 95.</summary>
    public bool Bevel { get; init; }
}

/// <summary>
/// Conjuntos de iconos, independientes del tema: cualquier tema con cualquier conjunto. Todos tienen un icono para cada
/// función del copiador: usan los iconos de Windows 11 (Segoe Fluent Icons), salvo «Windows 10», que usa los de Segoe
/// MDL2 Assets y, para los pocos que esa fuente no tiene, los de Windows 11 (ver <see cref="FontFor"/>). Cada conjunto
/// inspirado en un sistema imita su forma, su paleta y su acabado, para que ninguno se parezca a otro.
/// </summary>
internal static class IconPacks
{
    public const string FluentFont = "Segoe Fluent Icons";
    public const string Mdl2Font = "Segoe MDL2 Assets";

    public static IReadOnlyList<IconPack> All { get; } =
    [
        new("fluent", IconStyle.Mono, FluentFont),
        new("color", IconStyle.Color, FluentFont),
        new("mosaico", IconStyle.Tile, FluentFont),
        new("suave", IconStyle.Soft, FluentFont),
        new("degradado", IconStyle.Gradient, FluentFont),
        new("neon", IconStyle.Neon, FluentFont),
        new("windows10", IconStyle.Mono, Mdl2Font),

        // Android: AOSP con los círculos planos de Material; Material You con el color dinámico del tema; One UI con
        // sus «squircles» planos y vivos; Nothing en blanco y negro con su rojo.
        new("android", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Flat = true, Palette = Hex("0F9D58 F4B400 DB4437 607D8B 4285F4 FBBC04 009688 FF7043 03A9F4 9C27B0 E91E63 AB47BC 3F51B5") },
        new("android-tonal", IconStyle.Soft, FluentFont) { Shape = IconShape.Squircle, FromTheme = true },
        new("oneui", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Flat = true, Palette = Hex("34C97B FFB300 FF5A5F 8A94A6 3E91FF FFC93C 1FC8C0 FF8A3D 4FB4FF A36BFF FF6FA8 C46BF2 5B6CFF") },
        new("nothing", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Flat = true, TileColor = Rgb(0x1C1C1C), Palette = Hex("F2F2F2 F2F2F2 D71921 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2 F2F2F2") },

        // Apple.
        new("macos", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Gloss = true, Palette = Hex("34C759 FF9500 FF3B30 8E8E93 007AFF FFCC00 30B0C7 FF9500 5AC8FA AF52DE FF2D55 BF5AF2 5856D6") },
        new("macos-color", IconStyle.Color, FluentFont) { Palette = Hex("28CD41 FF9F0A FF453A 98989D 0A84FF FFD60A 40C8E0 FF9F0A 64D2FF BF5AF2 FF375F DA8FFF 5E5CE6") },

        // Linux: cada escritorio con lo que lo distingue. Ubuntu, naranja y berenjena; KDE, iconos de color sin fondo;
        // GNOME, botones redondos con su azul (como los ajustes rápidos); elementary, círculos con brillo; Mint, todo en
        // verdes; Fedora, aros en su azul; Pop!_OS, sus tonos cálidos; Arch, mosaicos planos en su azul.
        new("ubuntu", IconStyle.Tile, FluentFont) { Palette = Hex("0E8420 F99B11 C7162B 77216F E95420 F99B11 19B6EE E95420 19B6EE 77216F 924D8B 5E2750 772953") },
        new("kde", IconStyle.Color, FluentFont) { Palette = Hex("27AE60 F67400 DA4453 7F8C8D 3DAEE9 FDBC4B 1ABC9C F47750 1D99F3 8E44AD E93A9A 9B59B6 2980B9") },
        new("gnome", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Flat = true, Tint = Rgb(0x3584E4) },
        new("elementary", IconStyle.Tile, FluentFont) { Shape = IconShape.Circle, Gloss = true, Palette = Hex("68B723 F9C440 C6262E 667885 3689E6 F9C440 28BCA3 F37329 64BAFF A56DE2 DE3E80 7A36B1 0D52BF") },
        new("mint", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Flat = true, Palette = Hex("5AAA45 8DB255 3F8F3A 6E8F68 4E9A6F 9BBF5B 2F9A7E 87A84A 64B98A 4E8A5C 7DAF6B 5E9D4F 3B7D5B") },
        new("fedora", IconStyle.Outline, FluentFont) { Shape = IconShape.Circle, Tint = Rgb(0x3C6EB4) },
        new("popos", IconStyle.Tile, FluentFont) { Palette = Hex("73C48F FAA41A F15D22 574F4A 48B9C7 FBB86C 48B9C7 FAA41A 94EBEB A86F9F E36F7A 9E6FA8 4C6A92") },
        new("arch", IconStyle.Tile, FluentFont) { Shape = IconShape.Square, Flat = true, Tint = Rgb(0x1793D1) },

        // Windows de otras épocas.
        new("win95", IconStyle.Tile, FluentFont) { Shape = IconShape.Square, Flat = true, Bevel = true, TileColor = Rgb(0xC0C0C0), Palette = Hex("008000 808000 C00000 404040 000080 808000 008080 A04000 0000C0 800080 A00060 800080 000080") },
        new("winxp", IconStyle.Tile, FluentFont) { Gloss = true, Palette = Hex("3C8D2E F3A10C E0432B 6D7F99 245EDC FFD34E 2E9E9A F07D21 4A9BF0 7B5AC2 D9478A A64DBF 2F49C7") },
        new("vista", IconStyle.Tile, FluentFont) { Gloss = true, TileColor = Rgb(0x1E2B3A), Palette = Hex("7BD84A FFC23D FF5A4E B5C3D3 4CC2FF FFE066 4EE0D0 FF9A3D 7FD3FF C49BFF FF7AB8 E08CFF 8C9BFF") },
        new("win7", IconStyle.Tile, FluentFont) { Shape = IconShape.Squircle, Gloss = true, Palette = Hex("2F9E3F E8A317 D23A2F 5B6F86 1E73C9 F2C230 1BA39C E47E26 3BA0E8 7F4FC9 D1458E A045B5 3653C4") },
        new("metro", IconStyle.Tile, FluentFont) { Shape = IconShape.Square, Flat = true, Palette = Hex("00A300 F09609 E51400 647687 1BA1E2 E3C800 00ABA9 FA6800 1BA1E2 A200FF FF0097 D80073 6A00FF") },

        // Juegos y futuristas.
        new("steam", IconStyle.Tile, FluentFont) { Flat = true, TileColor = Rgb(0x1B2838), Palette = Hex("A4D007 E8A317 E04B4B 8F98A0 66C0F4 FFC82C 66C0F4 E77A2F 66C0F4 B28FE0 E36F9E C27CE0 66C0F4") },
        new("cyber", IconStyle.Neon, FluentFont) { Palette = Hex("39FF14 FFE600 FF2E63 9AA5B1 00F0FF FFE600 00FFC6 FF8A00 00F0FF B026FF FF2BD6 FF2BD6 7A5CFF") },
        new("hud", IconStyle.Outline, FluentFont) { Shape = IconShape.Square, Tint = Rgb(0x00E5FF) },
    ];

    /// <summary>Los 13 tonos por significado, en el orden de las paletas de cada conjunto.</summary>
    /// (Propiedad y no campo: los campos estáticos se inician en orden y los colores están declarados más abajo.)
    internal static Color[] Tones => s_tones ??= [Green, Amber, Red, Slate, Blue, Yellow, Teal, Orange, Sky, Purple, Pink, Magenta, Indigo];

    private static Color[]? s_tones;

    /// <summary>
    /// Color de un icono en un conjunto: el tono por su significado, tomado de la paleta del conjunto (o el único color
    /// del conjunto, o el acento del tema).
    /// </summary>
    public static Color ColorFor(string glyph, IconPack pack)
    {
        if (pack.FromTheme)
        {
            return ThemeManager.Parse(ThemeManager.Current.Accent);
        }

        if (pack.Tint is { } tint)
        {
            return tint;
        }

        var color = DefaultColorFor(glyph);
        if (pack.Palette is not { Length: 13 } palette)
        {
            return color;
        }

        int tone = Array.IndexOf(Tones, color);
        return palette[tone < 0 ? 4 : tone];
    }

    /// <summary>
    /// La fuente con la que se dibuja un icono en el conjunto: la suya o, si no tiene ese icono (Segoe MDL2 Assets no
    /// tiene los más nuevos), la de Windows 11. Así ningún conjunto deja un hueco en lugar de un icono.
    /// </summary>
    public static string FontFor(string glyph, IconPack pack) =>
        pack.FontFamily == FluentFont || glyph.Length == 0 || FontGlyphs.Has(pack.FontFamily, glyph[0]) ? pack.FontFamily : FluentFont;

    private static Color Rgb(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

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
