using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MegaCopier.App.Services;

/// <summary>Color de acento y fondo de la ventana.</summary>
internal static class Appearance
{
    /// <summary>Colores de acento ofrecidos (además del de Windows).</summary>
    public static readonly (string Key, string Hex)[] AccentPresets =
    [
        ("Blue", "#3B6FF5"),
        ("Violet", "#7C4DFF"),
        ("Teal", "#0F9D8F"),
        ("Green", "#2E9E4F"),
        ("Orange", "#E8710A"),
        ("Pink", "#D6338A"),
        ("Red", "#D93636"),
    ];

    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    /// <summary>Sustituye el color de acento de Windows por uno propio ("system" deja el de Windows).</summary>
    public static void ApplyAccent(string accent)
    {
        var resources = Application.Current.Resources;
        if (!TryParse(accent, out var color))
        {
            foreach (string key in AccentKeys)
            {
                resources.Remove(key);
            }

            return;
        }

        // Las variantes claras y oscuras que usan los controles (botones, barras de progreso, enlaces...).
        resources["SystemAccentColor"] = color;
        resources["SystemAccentColorLight1"] = Blend(color, Colors.White, 0.25);
        resources["SystemAccentColorLight2"] = Blend(color, Colors.White, 0.45);
        resources["SystemAccentColorLight3"] = Blend(color, Colors.White, 0.65);
        resources["SystemAccentColorDark1"] = Blend(color, Colors.Black, 0.2);
        resources["SystemAccentColorDark2"] = Blend(color, Colors.Black, 0.4);
        resources["SystemAccentColorDark3"] = Blend(color, Colors.Black, 0.6);
    }

    public static SystemBackdrop? CreateBackdrop(AppBackdrop backdrop) => backdrop switch
    {
        AppBackdrop.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        AppBackdrop.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        AppBackdrop.Acrylic => new AcrylicBackdrop(DesktopAcrylicKind.Base),
        AppBackdrop.AcrylicThin => new AcrylicBackdrop(DesktopAcrylicKind.Thin),
        _ => null,
    };

    /// <summary>"#AARRGGBB" o "#RRGGBB" → "#RRGGBB" (el acento de los controles no lleva transparencia).</summary>
    public static string ToRgbHex(string color)
    {
        var c = Theming.ThemeManager.Parse(color);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static bool TryParse(string text, out Color color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            return false;
        }

        color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static Color Blend(Color color, Color with, double amount) => Color.FromArgb(
        255,
        (byte)(color.R + ((with.R - color.R) * amount)),
        (byte)(color.G + ((with.G - color.G) * amount)),
        (byte)(color.B + ((with.B - color.B) * amount)));

    private static class Colors
    {
        public static readonly Color White = Color.FromArgb(255, 255, 255, 255);
        public static readonly Color Black = Color.FromArgb(255, 0, 0, 0);
    }
}

/// <summary>
/// Acrílico de escritorio de la clase pedida (normal o delgado): el DesktopAcrylicBackdrop de WinUI solo da el normal.
/// </summary>
internal sealed partial class AcrylicBackdrop(DesktopAcrylicKind kind) : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _controller = new DesktopAcrylicController { Kind = kind };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
    }

    protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }
}
