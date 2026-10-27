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

    /// <summary>
    /// El material de una ventana. <paramref name="altWhenInactive"/>: el de serie de MegaCopier, Mica que al perder el
    /// foco pasa a Mica Alt en lugar de apagarse.
    /// </summary>
    public static SystemBackdrop? CreateBackdrop(AppBackdrop backdrop, bool dark, bool altWhenInactive = false, bool active = true) =>
        backdrop == AppBackdrop.Solid ? null : new MaterialBackdrop(backdrop, dark, altWhenInactive, active);

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
/// Material de Windows 11 detrás de una ventana o de un menú: Mica, Mica Alt, acrílico normal o delgado. Lleva el modo
/// claro u oscuro del tema de MegaCopier y no el de Windows (los de WinUI siguen al sistema: «Fluent claro» con Windows
/// en oscuro quedaba con una Mica oscura). Al perder el foco, Windows apaga el material y deja un color liso; con
/// <c>altWhenInactive</c> no se apaga: pasa a su pareja (Mica ⇄ Mica Alt, acrílico ⇄ acrílico delgado) y vuelve al
/// elegido al recuperarlo.
/// </summary>
internal sealed partial class MaterialBackdrop(AppBackdrop kind, bool dark, bool altWhenInactive = false, bool active = true) : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _configuration = new()
    {
        Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
        IsInputActive = altWhenInactive || active,
    };

    private readonly Dictionary<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop, ISystemBackdropControllerWithTargets> _controllers = [];
    private bool _active = active;

    public AppBackdrop Kind => kind;

    /// <summary>La ventana ganó (true) o perdió el foco.</summary>
    public void SetActive(bool value)
    {
        if (_active == value)
        {
            return;
        }

        _active = value;
        if (altWhenInactive)
        {
            var shown = Shown;
            foreach (var controller in _controllers.Values)
            {
                switch (controller)
                {
                    case MicaController mica:
                        mica.Kind = shown == AppBackdrop.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base;
                        break;
                    case DesktopAcrylicController acrylic:
                        acrylic.Kind = shown == AppBackdrop.AcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base;
                        break;
                }
            }
        }
        else
        {
            _configuration.IsInputActive = value;
        }
    }

    /// <summary>La pareja de cada material sin el foco: Mica ⇄ Mica Alt y acrílico ⇄ acrílico delgado (translúcida).</summary>
    internal static AppBackdrop Partner(AppBackdrop backdrop) => backdrop switch
    {
        AppBackdrop.Mica => AppBackdrop.MicaAlt,
        AppBackdrop.MicaAlt => AppBackdrop.Mica,
        AppBackdrop.Acrylic => AppBackdrop.AcrylicThin,
        AppBackdrop.AcrylicThin => AppBackdrop.Acrylic,
        _ => backdrop,
    };

    /// <summary>El material que se ve ahora: el elegido con el foco y, sin él, su pareja.</summary>
    private AppBackdrop Shown => altWhenInactive && !_active ? Partner(kind) : kind;

    protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        ISystemBackdropControllerWithTargets controller = Shown switch
        {
            AppBackdrop.Acrylic => new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base },
            AppBackdrop.AcrylicThin => new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin },
            AppBackdrop.MicaAlt => new MicaController { Kind = MicaKind.BaseAlt },
            _ => new MicaController { Kind = MicaKind.Base },
        };
        controller.AddSystemBackdropTarget(connectedTarget);
        controller.SetSystemBackdropConfiguration(_configuration);
        _controllers[connectedTarget] = controller;
    }

    protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controllers.Remove(disconnectedTarget, out var controller))
        {
            controller.RemoveSystemBackdropTarget(disconnectedTarget);
            controller.Dispose();
        }
    }
}

/// <summary>
/// Acrílico de escritorio de los menús, flyouts y del menú de la bandeja de MegaCopier (se pone en Themes/Styles.xaml;
/// ver <see cref="Materials"/>). Sigue el modo claro u oscuro del tema y no se apaga cuando la ventana no tiene el foco
/// (el de WinUI, sí: el menú se veía opaco). Con transparencia desactivada, ahorro de batería o alto contraste, Windows
/// lo cambia por un color liso.
/// </summary>
public sealed partial class PopupBackdrop : SystemBackdrop
{
    /// <summary>Configuración de todas las superficies abiertas, para seguir el cambio de tema sin cerrarlas.</summary>
    private static readonly List<SystemBackdropConfiguration> Open = [];

    private readonly Dictionary<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop, (DesktopAcrylicController Controller, SystemBackdropConfiguration Configuration)> _targets = [];

    /// <summary>Cambió el tema o el alto contraste: lo aplica a las superficies abiertas.</summary>
    public static void Refresh()
    {
        foreach (var configuration in Open)
        {
            Configure(configuration);
        }
    }

    private static void Configure(SystemBackdropConfiguration configuration)
    {
        configuration.Theme = Theming.ThemeManager.Current.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        configuration.IsHighContrast = Materials.HighContrast;
    }

    protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var configuration = new SystemBackdropConfiguration { IsInputActive = true };
        Configure(configuration);
        Open.Add(configuration);
        var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        controller.AddSystemBackdropTarget(connectedTarget);
        controller.SetSystemBackdropConfiguration(configuration);
        _targets[connectedTarget] = (controller, configuration);
    }

    protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_targets.Remove(disconnectedTarget, out var target))
        {
            Open.Remove(target.Configuration);
            target.Controller.RemoveSystemBackdropTarget(disconnectedTarget);
            target.Controller.Dispose();
        }
    }
}
