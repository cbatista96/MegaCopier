using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MegaCopier.App.Theming;

/// <summary>Un tema completo: colores de toda la interfaz, de las dos barras de progreso, fondo y redondeo.</summary>
public sealed class ThemeDefinition
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Claro u oscuro: decide el aspecto de los controles estándar (botones, listas, menús).</summary>
    public bool Dark { get; set; } = true;

    public AppBackdrop Backdrop { get; set; } = AppBackdrop.Mica;

    /// <summary>Color que tiñe el fondo de la ventana (con transparencia sobre Mica/Acrílico).</summary>
    public string Background { get; set; } = "#00000000";

    /// <summary>Segundo color del fondo para un degradado ("" = fondo de un solo color).</summary>
    public string BackgroundEnd { get; set; } = "";

    /// <summary>Dirección del degradado del fondo en grados (0 = de izquierda a derecha, 90 = de arriba abajo).</summary>
    public double GradientAngle { get; set; } = 90;

    /// <summary>Textura de fondo incluida en MegaCopier (Assets/Textures/&lt;nombre&gt;.jpg) o "" para ninguna.</summary>
    public string Texture { get; set; } = "";

    /// <summary>Opacidad de la textura sobre el fondo (0 a 1).</summary>
    public double TextureOpacity { get; set; } = 0.35;

    /// <summary>
    /// Colorea también los controles estándar (botones, listas, menús, cuadros de texto, barras de desplazamiento) con
    /// los colores del tema. Los temas Fluent lo dejan desactivado para verse exactamente como Windows.
    /// </summary>
    public bool StyleControls { get; set; } = true;

    public string Surface { get; set; } = "#14FFFFFF";

    public string SurfaceStrong { get; set; } = "#22FFFFFF";

    public string Border { get; set; } = "#1AFFFFFF";

    public string Text { get; set; } = "#FFFFFFFF";

    public string TextSecondary { get; set; } = "#B3FFFFFF";

    public string Accent { get; set; } = "#4C8DFF";

    public string AccentText { get; set; } = "#FFFFFFFF";

    /// <summary>Barra superior: progreso total (degradado de inicio a fin).</summary>
    public string TotalStart { get; set; } = "#4C8DFF";

    public string TotalEnd { get; set; } = "#8A5CFF";

    /// <summary>Barra inferior: archivo actual.</summary>
    public string FileStart { get; set; } = "#2EC4B6";

    public string FileEnd { get; set; } = "#4C8DFF";

    public string Track { get; set; } = "#26FFFFFF";

    public string Success { get; set; } = "#3CCB7F";

    public string Warning { get; set; } = "#F2B33D";

    public string Error { get; set; } = "#F2545B";

    public double CornerRadius { get; set; } = 8;

    /// <summary>Aspecto de las barras guardado con el tema (solo en los temas propios; null = no cambiarlas).</summary>
    public BarSettings? TotalBar { get; set; }

    public BarSettings? FileBar { get; set; }

    /// <summary>Altura de las barras de progreso en píxeles.</summary>
    public double BarHeight { get; set; } = 8;

    public bool BuiltIn { get; set; }

    public ThemeDefinition Clone() => (ThemeDefinition)MemberwiseClone();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ThemeDefinition))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;

/// <summary>
/// Temas de la app. Los pinceles se crean una vez y se insertan en los recursos antes de cargar la interfaz;
/// cambiar de tema solo recolorea esos pinceles, así toda la interfaz cambia al instante sin reiniciar.
/// </summary>
internal static class ThemeManager
{
    public static readonly LinearGradientBrush BackgroundBrush = Gradient();
    public static readonly SolidColorBrush SurfaceBrush = new();
    public static readonly SolidColorBrush SurfaceStrongBrush = new();
    public static readonly SolidColorBrush BorderBrush = new();
    public static readonly SolidColorBrush TextBrush = new();
    public static readonly SolidColorBrush TextSecondaryBrush = new();
    public static readonly SolidColorBrush AccentBrush = new();
    public static readonly SolidColorBrush AccentTextBrush = new();
    public static readonly SolidColorBrush TrackBrush = new();
    public static readonly SolidColorBrush SuccessBrush = new();
    public static readonly SolidColorBrush WarningBrush = new();
    public static readonly SolidColorBrush ErrorBrush = new();
    public static readonly LinearGradientBrush TotalBrush = Gradient();
    public static readonly LinearGradientBrush FileBrush = Gradient();

    public static string Folder { get; } = Path.Combine(
        Engine.DataFolder.Path, "themes");

    /// <summary>Cambió el tema (para lo que no se pinta con los pinceles compartidos: fondo, radio, altura).</summary>
    public static event Action<ThemeDefinition>? Changed;

    public static IReadOnlyList<ThemeDefinition> BuiltIn { get; } =
    [
        new() { Id = "fluent-dark", Name = "Fluent oscuro", Dark = true, BuiltIn = true, StyleControls = false },
        new()
        {
            Id = "fluent-light", Name = "Fluent claro", Dark = false, BuiltIn = true, StyleControls = false,
            Surface = "#B3FFFFFF", SurfaceStrong = "#E6FFFFFF", Border = "#14000000",
            Text = "#E4000000", TextSecondary = "#99000000", Track = "#1A000000", Accent = "#2563EB",
            TotalStart = "#2563EB", TotalEnd = "#7C3AED", FileStart = "#0EA5A4", FileEnd = "#2563EB",
        },
        new()
        {
            Id = "midnight", Name = "Medianoche", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.Solid,
            Background = "#FF0B1020", Surface = "#FF131A2E", SurfaceStrong = "#FF1B2440", Border = "#FF26314F",
            Accent = "#7AA2FF", TotalStart = "#5B8CFF", TotalEnd = "#B06BFF", FileStart = "#3DDBD9", FileEnd = "#5B8CFF",
            Track = "#FF222B45",
        },
        new()
        {
            Id = "graphite", Name = "Grafito", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.Solid, CornerRadius = 4,
            Background = "#FF1E1E1E", Surface = "#FF262626", SurfaceStrong = "#FF303030", Border = "#FF3A3A3A",
            Accent = "#3B8EEA", TotalStart = "#1F6FD1", TotalEnd = "#3B8EEA", FileStart = "#1F6FD1", FileEnd = "#3B8EEA",
            Track = "#FF3A3A3A", BarHeight = 6,
        },
        new()
        {
            Id = "ocean", Name = "Océano", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.Acrylic,
            Background = "#990A2A3D", Surface = "#330E4A66", SurfaceStrong = "#4D11607F", Border = "#3326B5D9",
            Accent = "#2ED3F2", TotalStart = "#00B4D8", TotalEnd = "#48CAE4", FileStart = "#90E0EF", FileEnd = "#00B4D8",
            Track = "#33FFFFFF", CornerRadius = 12, BarHeight = 10,
        },
        new()
        {
            Id = "forest", Name = "Bosque", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.Solid,
            Background = "#FF0F1A12", Surface = "#FF16241A", SurfaceStrong = "#FF1E3124", Border = "#FF27402E",
            Accent = "#4ADE80", AccentText = "#FF06210F", TotalStart = "#22C55E", TotalEnd = "#A3E635", FileStart = "#16A34A", FileEnd = "#4ADE80",
            Track = "#FF223627",
        },
        new()
        {
            Id = "sunset", Name = "Atardecer", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.MicaAlt,
            Background = "#661E0B1A", Surface = "#26FF7A59", SurfaceStrong = "#40FF7A59", Border = "#33FF9E7A",
            Accent = "#FF7A59", TotalStart = "#FF5F6D", TotalEnd = "#FFC371", FileStart = "#F857A6", FileEnd = "#FF5858",
            Track = "#33FFFFFF", CornerRadius = 14, BarHeight = 10,
        },
        new()
        {
            Id = "supercopier", Name = "Azul clásico", Dark = false, BuiltIn = true, Backdrop = AppBackdrop.Solid, CornerRadius = 2,
            Background = "#FFE9EEF6", Surface = "#FFFFFFFF", SurfaceStrong = "#FFDCE6F5", Border = "#FFB9C8E0",
            Text = "#FF102A43", TextSecondary = "#FF486581", Accent = "#1F6FD1",
            TotalStart = "#1C5FC4", TotalEnd = "#5AA2F0", FileStart = "#3D8BEB", FileEnd = "#8CC4FF", Track = "#FFC9D6EA", BarHeight = 14,
        },
        new()
        {
            Id = "ultracopier", Name = "Gris clásico", Dark = false, BuiltIn = true, Backdrop = AppBackdrop.Solid, CornerRadius = 3,
            Background = "#FFF2F2F2", Surface = "#FFFFFFFF", SurfaceStrong = "#FFE6E6E6", Border = "#FFC8C8C8",
            Text = "#FF1A1A1A", TextSecondary = "#FF5C5C5C", Accent = "#2E8B2E",
            TotalStart = "#2EA043", TotalEnd = "#7BD88F", FileStart = "#2EA043", FileEnd = "#7BD88F", Track = "#FFD5D5D5", BarHeight = 16,
        },
        new()
        {
            Id = "contrast", Name = "Alto contraste", Dark = true, BuiltIn = true, Backdrop = AppBackdrop.Solid, CornerRadius = 0,
            Background = "#FF000000", Surface = "#FF000000", SurfaceStrong = "#FF1A1A1A", Border = "#FFFFFFFF",
            Text = "#FFFFFFFF", TextSecondary = "#FFFFFF00", Accent = "#FF00FFFF", AccentText = "#FF000000",
            TotalStart = "#FF00FFFF", TotalEnd = "#FF00FFFF", FileStart = "#FFFFFF00", FileEnd = "#FFFFFF00", Track = "#FF404040", BarHeight = 12,
        },
        .. ThemeCatalog.Extra(),
    ];

    // Después de BuiltIn: los inicializadores estáticos se ejecutan en orden.
    public static ThemeDefinition Current { get; private set; } = BuiltIn[0];

    /// <summary>Inserta los pinceles en los recursos de la app. Debe llamarse antes de crear la ventana.</summary>
    public static void Register(ResourceDictionary resources)
    {
        resources["McBackgroundBrush"] = BackgroundBrush;
        resources["McSurfaceBrush"] = SurfaceBrush;
        resources["McSurfaceStrongBrush"] = SurfaceStrongBrush;
        resources["McBorderBrush"] = BorderBrush;
        resources["McTextBrush"] = TextBrush;
        resources["McTextSecondaryBrush"] = TextSecondaryBrush;
        resources["McAccentBrush"] = AccentBrush;
        resources["McAccentTextBrush"] = AccentTextBrush;
        resources["McTrackBrush"] = TrackBrush;
        resources["McSuccessBrush"] = SuccessBrush;
        resources["McWarningBrush"] = WarningBrush;
        resources["McErrorBrush"] = ErrorBrush;
        resources["McTotalBrush"] = TotalBrush;
        resources["McFileBrush"] = FileBrush;
    }

    public static IReadOnlyList<ThemeDefinition> All()
    {
        var themes = new List<ThemeDefinition>(BuiltIn);
        if (Directory.Exists(Folder))
        {
            foreach (string file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                if (Load(file) is { } custom)
                {
                    custom.BuiltIn = false;
                    themes.Add(custom);
                }
            }
        }

        return themes;
    }

    /// <summary>Tema virtual que sigue el modo claro/oscuro de Windows (Fluent oscuro o Fluent claro).</summary>
    public const string AutoId = "fluent-auto";

    public static ThemeDefinition Find(string id)
    {
        var theme = id == AutoId ? (SystemIsDark() ? BuiltIn[0] : BuiltIn[1]) : All().FirstOrDefault(t => t.Id == id) ?? BuiltIn[0];
        return theme.Id.StartsWith("fluent", StringComparison.Ordinal) ? WithSystemAccent(theme) : theme;
    }

    /// <summary>Los temas Fluent usan el color de acento de Windows.</summary>
    private static ThemeDefinition WithSystemAccent(ThemeDefinition theme)
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var accent = settings.GetColorValue(theme.Dark ? Windows.UI.ViewManagement.UIColorType.AccentLight2 : Windows.UI.ViewManagement.UIColorType.AccentDark1);
            var clone = theme.Clone();
            clone.Accent = ToHex(accent);
            clone.AccentText = theme.Dark ? "#FF000000" : "#FFFFFFFF";
            return clone;
        }
        catch (Exception)
        {
            return theme;
        }
    }

    /// <summary>Modo de las apps de Windows: el fondo del sistema es oscuro.</summary>
    public static bool SystemIsDark()
    {
        try
        {
            var background = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R + background.G + background.B < 382;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void Apply(ThemeDefinition theme)
    {
        Current = theme;
        SetGradient(BackgroundBrush, theme.Background, theme.BackgroundEnd.Length > 0 ? theme.BackgroundEnd : theme.Background);
        double angle = theme.GradientAngle * Math.PI / 180;
        BackgroundBrush.StartPoint = new Windows.Foundation.Point(0.5 - (Math.Cos(angle) / 2), 0.5 - (Math.Sin(angle) / 2));
        BackgroundBrush.EndPoint = new Windows.Foundation.Point(0.5 + (Math.Cos(angle) / 2), 0.5 + (Math.Sin(angle) / 2));
        Set(SurfaceBrush, theme.Surface);
        Set(SurfaceStrongBrush, theme.SurfaceStrong);
        Set(BorderBrush, theme.Border);
        Set(TextBrush, theme.Text);
        Set(TextSecondaryBrush, theme.TextSecondary);
        Set(AccentBrush, theme.Accent);
        Set(AccentTextBrush, theme.AccentText);
        Set(TrackBrush, theme.Track);
        Set(SuccessBrush, theme.Success);
        Set(WarningBrush, theme.Warning);
        Set(ErrorBrush, theme.Error);
        SetGradient(TotalBrush, theme.TotalStart, theme.TotalEnd);
        SetGradient(FileBrush, theme.FileStart, theme.FileEnd);
        Changed?.Invoke(theme);
    }

    /// <summary>Guarda un tema personalizado (crea o sustituye).</summary>
    public static void Save(ThemeDefinition theme)
    {
        Directory.CreateDirectory(Folder);
        theme.BuiltIn = false;
        File.WriteAllText(Path.Combine(Folder, theme.Id + ".json"), JsonSerializer.Serialize(theme, ThemeJsonContext.Default.ThemeDefinition));
    }

    public static void Delete(ThemeDefinition theme)
    {
        string path = Path.Combine(Folder, theme.Id + ".json");
        if (!theme.BuiltIn && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void Export(ThemeDefinition theme, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(theme, ThemeJsonContext.Default.ThemeDefinition));

    /// <summary>Importa un .json de tema y lo guarda como personalizado (con un id nuevo si choca).</summary>
    public static ThemeDefinition? Import(string path)
    {
        if (Load(path) is not { } theme)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(theme.Id) || BuiltIn.Any(b => b.Id == theme.Id))
        {
            theme.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        }

        Save(theme);
        return theme;
    }

    public static Color Parse(string text)
    {
        // #RRGGBB o #AARRGGBB
        string hex = text.TrimStart('#');
        uint value = uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint parsed) ? parsed : 0xFF808080;
        return hex.Length <= 6
            ? Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static ThemeDefinition? Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ThemeJsonContext.Default.ThemeDefinition);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Set(SolidColorBrush brush, string color) => brush.Color = Parse(color);

    private static void SetGradient(LinearGradientBrush brush, string start, string end)
    {
        brush.GradientStops[0].Color = Parse(start);
        brush.GradientStops[1].Color = Parse(end);
    }

    private static LinearGradientBrush Gradient()
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop { Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Offset = 1 });
        return brush;
    }
}
