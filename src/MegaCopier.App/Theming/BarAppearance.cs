using Windows.UI;

namespace MegaCopier.App.Theming;

/// <summary>Estilo de dibujo de una barra de progreso.</summary>
public enum BarStyle
{
    Fluent,
    Aero,
    Wave,
    Stripes,
    Neon,
    Blocks,
    Led,
    Rainbow,
    Pulse,
    Comet,
    Glass,
    Fire,
    Terminal,
    Line,
    Dual,
    Pixels,
    Metal,
    Outline,
    Flow,
    Bubbles,
    Traffic,
    Segments,
}

/// <summary>De dónde salen los colores de la barra.</summary>
public enum BarColorMode
{
    /// <summary>Los del tema (barra total y barra de archivo).</summary>
    Theme,

    /// <summary>Un degradado de dos colores elegidos.</summary>
    Custom,

    /// <summary>Rojo, ámbar y verde según avanza.</summary>
    Traffic,

    /// <summary>Todos los colores del arcoíris.</summary>
    Rainbow,
}

/// <summary>Dónde se ve el porcentaje.</summary>
public enum BarText
{
    /// <summary>A la derecha, fuera de la barra (como siempre).</summary>
    Outside,

    /// <summary>Dentro de la barra (la barra se hace un poco más alta).</summary>
    Inside,

    /// <summary>En ninguna parte.</summary>
    Hidden,
}

/// <summary>Cómo avanza el relleno.</summary>
public enum BarMotion
{
    Smooth,

    /// <summary>Se pasa un poco y vuelve, como un muelle.</summary>
    Elastic,

    /// <summary>A saltos, como las barras de antes.</summary>
    Steps,
}

/// <summary>Qué barra: la del total, la del archivo actual o las pequeñas de la lista de copias.</summary>
public enum BarRole
{
    Total,
    File,
    Mini,
}

/// <summary>Aspecto de una barra de progreso: estilo, colores, grosor, redondeo, velocidad de los efectos y texto.</summary>
public sealed class BarSettings
{
    public BarStyle Style { get; set; } = BarStyle.Fluent;

    public BarColorMode ColorMode { get; set; } = BarColorMode.Theme;

    public string ColorStart { get; set; } = "#FF3CB043";

    public string ColorEnd { get; set; } = "#FF7EE070";

    /// <summary>Grosor en píxeles; 0 = el del tema.</summary>
    public double Thickness { get; set; }

    /// <summary>Redondeo en píxeles; -1 = el del tema.</summary>
    public double Radius { get; set; } = -1;

    /// <summary>Velocidad de las animaciones (0,25 a 3).</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Intensidad del brillo y del halo (0 a 1).</summary>
    public double Glow { get; set; } = 0.6;

    /// <summary>Un borde fino alrededor de la barra.</summary>
    public bool Border { get; set; }

    public BarText Text { get; set; } = BarText.Outside;

    public BarMotion Motion { get; set; } = BarMotion.Smooth;

    /// <summary>Efectos según el estado: rayas en pausa, sacudida con error y destello al terminar.</summary>
    public bool StateEffects { get; set; } = true;

    public BarSettings Clone() => (BarSettings)MemberwiseClone();
}

/// <summary>
/// Aspecto de las barras de toda la app: uno para la barra total, otro para la de archivo y otro para las mini-barras
/// de la lista de copias. Las barras escuchan <see cref="Changed"/> y se redibujan al momento.
/// </summary>
public static class BarAppearance
{
    private static BarSettings _total = new();
    private static BarSettings _file = new();
    private static BarSettings _mini = new();
    private static bool _reduceMotion;

    public static event Action? Changed;

    public static BarSettings For(BarRole role) => role switch
    {
        BarRole.File => _file,
        BarRole.Mini => _mini,
        _ => _total,
    };

    /// <summary>Sin animaciones continuas (ajuste propio o «Efectos de animación» desactivado en Windows).</summary>
    public static bool ReduceMotion => _reduceMotion || !SystemAnimations();

    public static void Apply(BarSettings total, BarSettings file, BarSettings mini, bool reduceMotion)
    {
        _total = total.Clone();
        _file = file.Clone();
        _mini = mini.Clone();
        _reduceMotion = reduceMotion;
        Changed?.Invoke();
    }

    /// <summary>Colores de cada estilo cuando no hay colores propios ni del tema (fuego, terminal...).</summary>
    public static (Color Start, Color End)? NaturalColors(BarStyle style) => style switch
    {
        BarStyle.Fire => (Color.FromArgb(255, 214, 40, 20), Color.FromArgb(255, 255, 196, 40)),
        BarStyle.Metal => (Color.FromArgb(255, 120, 128, 140), Color.FromArgb(255, 200, 206, 214)),
        _ => null,
    };

    private static bool SystemAnimations()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
