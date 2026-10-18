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
}

/// <summary>Un conjunto de iconos: estilo y tipo de letra de los iconos.</summary>
public sealed record IconPack(string Id, IconStyle Style, string FontFamily);

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
    ];

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
    public static Color ColorFor(string glyph)
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
