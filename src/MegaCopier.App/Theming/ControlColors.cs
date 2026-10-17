using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MegaCopier.App.Theming;

/// <summary>
/// Colorea los controles estándar de WinUI (botones, desplegables, cuadros de texto, interruptores, casillas, listas,
/// menú lateral, menús, diálogos y barras de desplazamiento) con la paleta del tema. Se hace sustituyendo en los recursos
/// de la app las claves que usan las plantillas de WinUI; los temas Fluent (StyleControls = false) las quitan para verse
/// exactamente como Windows.
/// </summary>
internal static class ControlColors
{
    /// <summary>Todas las claves que se sustituyen, para poder quitarlas al volver a un tema Fluent.</summary>
    private static readonly string[] Keys =
    [
        "ScrollBarThumbFill", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed", "ScrollBarTrackFill",
        "ScrollBarTrackFillPointerOver", "ScrollBarButtonArrowForeground", "ScrollBarButtonArrowForegroundPointerOver",
        "ScrollBarPanningThumbBackground",
        "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonForeground",
        "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ButtonBorderBrush", "ButtonBorderBrushPointerOver",
        "ButtonBorderBrushPressed",
        "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush",
        "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed", "ComboBoxBorderBrush",
        "ComboBoxBorderBrushPointerOver", "ComboBoxForeground", "ComboBoxDropDownBackground", "ComboBoxDropDownBorderBrush",
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlForeground",
        "TextControlForegroundPointerOver", "TextControlForegroundFocused", "TextControlBorderBrush",
        "TextControlBorderBrushPointerOver", "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
        "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver", "ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver",
        "ToggleSwitchKnobFillOff", "ToggleSwitchKnobFillOffPointerOver", "ToggleSwitchContentForeground",
        "ToggleSwitchHeaderForeground",
        "CheckBoxCheckBackgroundFillUnchecked", "CheckBoxCheckBackgroundFillUncheckedPointerOver",
        "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver",
        "CheckBoxForegroundUnchecked", "CheckBoxForegroundChecked", "CheckBoxForegroundUncheckedPointerOver",
        "CheckBoxForegroundCheckedPointerOver",
        "RadioButtonForeground", "RadioButtonForegroundPointerOver",
        "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver",
        "ListViewItemForeground", "ListViewItemForegroundPointerOver", "ListViewItemForegroundSelected",
        "NavigationViewContentBackground", "NavigationViewContentGridBorderBrush", "NavigationViewExpandedPaneBackground",
        "NavigationViewDefaultPaneBackground", "NavigationViewItemBackgroundPointerOver", "NavigationViewItemBackgroundSelected",
        "NavigationViewItemBackgroundSelectedPointerOver", "NavigationViewItemForeground", "NavigationViewItemForegroundPointerOver",
        "NavigationViewItemForegroundSelected", "NavigationViewItemForegroundSelectedPointerOver",
        "NavigationViewSelectionIndicatorForeground",
        "MenuFlyoutPresenterBackground", "MenuFlyoutPresenterBorderBrush", "MenuFlyoutItemForeground",
        "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutSubItemForeground",
        "FlyoutPresenterBackground", "FlyoutBorderThemeBrush",
        "ContentDialogBackground", "ContentDialogForeground", "ContentDialogBorderBrush", "ContentDialogTopOverlay",
        "ToolTipBackground", "ToolTipForeground", "ToolTipBorderBrush",
        "HyperlinkButtonForeground", "HyperlinkButtonForegroundPointerOver",
        "SliderTrackFill", "SliderTrackFillPointerOver",
        "SelectorBarItemForeground",
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
    ];

    /// <summary>
    /// Aplica (o quita) los colores. Devuelve una firma de los colores puestos: si cambia, los controles tienen que
    /// volver a leer sus recursos.
    /// </summary>
    public static string Apply(ThemeDefinition theme, ResourceDictionary resources)
    {
        if (!theme.StyleControls)
        {
            foreach (string key in Keys)
            {
                resources.Remove(key);
            }

            return "";
        }

        // Base opaca: el fondo del tema sobre el gris de Windows (para menús y diálogos, que no pueden ser transparentes).
        Color windowBase = theme.Dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        Color background = Over(ThemeManager.Parse(theme.Background), windowBase);
        if (theme.BackgroundEnd.Length > 0)
        {
            background = Mix(background, Over(ThemeManager.Parse(theme.BackgroundEnd), windowBase), 0.5);
        }

        Color surface = Over(ThemeManager.Parse(theme.Surface), background);
        Color strong = Over(ThemeManager.Parse(theme.SurfaceStrong), background);
        Color border = Over(ThemeManager.Parse(theme.Border), background);
        Color text = Over(ThemeManager.Parse(theme.Text), background);
        Color secondary = Over(ThemeManager.Parse(theme.TextSecondary), background);
        Color accent = Opaque(ThemeManager.Parse(theme.Accent));
        Color track = Over(ThemeManager.Parse(theme.Track), background);
        Color hover = Mix(strong, text, 0.08);
        Color pressed = Mix(strong, text, 0.14);
        Color popup = Mix(strong, background, 0.35);
        Color selected = Mix(surface, accent, 0.28);
        Color input = theme.Dark ? Mix(background, Color.FromArgb(255, 0, 0, 0), 0.25) : Mix(surface, Color.FromArgb(255, 255, 255, 255), 0.6);

        var set = new Dictionary<string, Color>
        {
            ["ScrollBarThumbFill"] = WithAlpha(secondary, 170),
            ["ScrollBarThumbFillPointerOver"] = accent,
            ["ScrollBarThumbFillPressed"] = Mix(accent, text, 0.2),
            ["ScrollBarTrackFill"] = WithAlpha(track, 120),
            ["ScrollBarTrackFillPointerOver"] = WithAlpha(track, 170),
            ["ScrollBarButtonArrowForeground"] = secondary,
            ["ScrollBarButtonArrowForegroundPointerOver"] = text,
            ["ScrollBarPanningThumbBackground"] = WithAlpha(secondary, 170),

            ["ButtonBackground"] = strong,
            ["ButtonBackgroundPointerOver"] = hover,
            ["ButtonBackgroundPressed"] = pressed,
            ["ButtonForeground"] = text,
            ["ButtonForegroundPointerOver"] = text,
            ["ButtonForegroundPressed"] = secondary,
            ["ButtonBorderBrush"] = border,
            ["ButtonBorderBrushPointerOver"] = Mix(border, accent, 0.35),
            ["ButtonBorderBrushPressed"] = border,
            ["SubtleFillColorSecondaryBrush"] = WithAlpha(text, 24),
            ["SubtleFillColorTertiaryBrush"] = WithAlpha(text, 14),

            ["ComboBoxBackground"] = strong,
            ["ComboBoxBackgroundPointerOver"] = hover,
            ["ComboBoxBackgroundPressed"] = pressed,
            ["ComboBoxBorderBrush"] = border,
            ["ComboBoxBorderBrushPointerOver"] = Mix(border, accent, 0.35),
            ["ComboBoxForeground"] = text,
            ["ComboBoxDropDownBackground"] = popup,
            ["ComboBoxDropDownBorderBrush"] = border,

            ["TextControlBackground"] = input,
            ["TextControlBackgroundPointerOver"] = Mix(input, text, 0.05),
            ["TextControlBackgroundFocused"] = input,
            ["TextControlForeground"] = text,
            ["TextControlForegroundPointerOver"] = text,
            ["TextControlForegroundFocused"] = text,
            ["TextControlBorderBrush"] = border,
            ["TextControlBorderBrushPointerOver"] = Mix(border, accent, 0.35),
            ["TextControlPlaceholderForeground"] = secondary,
            ["TextControlPlaceholderForegroundPointerOver"] = secondary,

            ["ToggleSwitchFillOff"] = input,
            ["ToggleSwitchFillOffPointerOver"] = Mix(input, text, 0.06),
            ["ToggleSwitchStrokeOff"] = secondary,
            ["ToggleSwitchStrokeOffPointerOver"] = text,
            ["ToggleSwitchKnobFillOff"] = secondary,
            ["ToggleSwitchKnobFillOffPointerOver"] = text,
            ["ToggleSwitchContentForeground"] = text,
            ["ToggleSwitchHeaderForeground"] = text,

            ["CheckBoxCheckBackgroundFillUnchecked"] = input,
            ["CheckBoxCheckBackgroundFillUncheckedPointerOver"] = Mix(input, text, 0.06),
            ["CheckBoxCheckBackgroundStrokeUnchecked"] = secondary,
            ["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = text,
            ["CheckBoxForegroundUnchecked"] = text,
            ["CheckBoxForegroundChecked"] = text,
            ["CheckBoxForegroundUncheckedPointerOver"] = text,
            ["CheckBoxForegroundCheckedPointerOver"] = text,
            ["RadioButtonForeground"] = text,
            ["RadioButtonForegroundPointerOver"] = text,

            ["ListViewItemBackgroundPointerOver"] = WithAlpha(text, 18),
            ["ListViewItemBackgroundSelected"] = selected,
            ["ListViewItemBackgroundSelectedPointerOver"] = Mix(selected, text, 0.06),
            ["ListViewItemForeground"] = text,
            ["ListViewItemForegroundPointerOver"] = text,
            ["ListViewItemForegroundSelected"] = text,

            ["NavigationViewContentBackground"] = Color.FromArgb(0, 0, 0, 0),
            ["NavigationViewContentGridBorderBrush"] = Color.FromArgb(0, 0, 0, 0),
            ["NavigationViewExpandedPaneBackground"] = Color.FromArgb(0, 0, 0, 0),
            ["NavigationViewDefaultPaneBackground"] = Color.FromArgb(0, 0, 0, 0),
            ["NavigationViewItemBackgroundPointerOver"] = WithAlpha(text, 18),
            ["NavigationViewItemBackgroundSelected"] = WithAlpha(text, 26),
            ["NavigationViewItemBackgroundSelectedPointerOver"] = WithAlpha(text, 34),
            ["NavigationViewItemForeground"] = text,
            ["NavigationViewItemForegroundPointerOver"] = text,
            ["NavigationViewItemForegroundSelected"] = text,
            ["NavigationViewItemForegroundSelectedPointerOver"] = text,
            ["NavigationViewSelectionIndicatorForeground"] = accent,

            ["MenuFlyoutPresenterBackground"] = popup,
            ["MenuFlyoutPresenterBorderBrush"] = border,
            ["MenuFlyoutItemForeground"] = text,
            ["MenuFlyoutItemBackgroundPointerOver"] = Mix(popup, text, 0.08),
            ["MenuFlyoutSubItemForeground"] = text,
            ["FlyoutPresenterBackground"] = popup,
            ["FlyoutBorderThemeBrush"] = border,
            ["ContentDialogBackground"] = popup,
            ["ContentDialogForeground"] = text,
            ["ContentDialogBorderBrush"] = border,
            ["ContentDialogTopOverlay"] = Mix(popup, background, 0.5),
            ["ToolTipBackground"] = popup,
            ["ToolTipForeground"] = text,
            ["ToolTipBorderBrush"] = border,

            ["HyperlinkButtonForeground"] = accent,
            ["HyperlinkButtonForegroundPointerOver"] = Mix(accent, text, 0.25),
            ["SliderTrackFill"] = track,
            ["SliderTrackFillPointerOver"] = Mix(track, text, 0.1),
            ["SelectorBarItemForeground"] = text,
            ["TextFillColorPrimaryBrush"] = text,
            ["TextFillColorSecondaryBrush"] = secondary,
        };

        var signature = new System.Text.StringBuilder();
        foreach (var (key, color) in set)
        {
            resources[key] = new SolidColorBrush(color);
            signature.Append(ThemeManager.ToHex(color));
        }

        return signature.ToString();
    }

    /// <summary>Un color con transparencia pintado sobre otro opaco.</summary>
    private static Color Over(Color top, Color bottom)
    {
        double a = top.A / 255.0;
        return Color.FromArgb(
            255,
            (byte)((top.R * a) + (bottom.R * (1 - a))),
            (byte)((top.G * a) + (bottom.G * (1 - a))),
            (byte)((top.B * a) + (bottom.B * (1 - a))));
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
        (byte)(a.A + ((b.A - a.A) * amount)),
        (byte)(a.R + ((b.R - a.R) * amount)),
        (byte)(a.G + ((b.G - a.G) * amount)),
        (byte)(a.B + ((b.B - a.B) * amount)));

    private static Color Opaque(Color c) => Color.FromArgb(255, c.R, c.G, c.B);

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
