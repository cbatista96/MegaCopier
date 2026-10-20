using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Services;

/// <summary>
/// Material de las superficies que se abren encima, separado del de la ventana, con el tinte que pone
/// Theming/ControlColors.cs:
/// <list type="bullet">
/// <item>Menús y menú de la bandeja: acrílico de escritorio, <see cref="PopupBackdrop"/> (McPopupBackdrop en
/// Themes/Styles.xaml).</item>
/// <item>Listas desplegables: acrílico de escritorio con <see cref="AcrylicProperty"/>, solo mientras están
/// abiertas.</item>
/// <item>Diálogos: acrílico de la app (ContentDialogBackground), que difumina la parte de la ventana que tapan. El de
/// escritorio, con un SystemBackdropElement dentro del diálogo, hacía caer la app (Microsoft.UI.Input.dll) al cambiar
/// de tema después de usarlo, con cualquier forma de ponerlo y quitarlo.</item>
/// </list>
/// Si Windows no admite el acrílico de escritorio, los tintes pasan a ser opacos.
/// </summary>
public static class Materials
{
    /// <summary>El acrílico de escritorio funciona en este equipo (si no, nada se dibuja detrás del tinte).</summary>
    public static bool DesktopAcrylic { get; } = DesktopAcrylicController.IsSupported();

    /// <summary>Windows está en alto contraste: se dejan los colores del sistema y el material pasa a color liso.</summary>
    public static bool HighContrast => new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;

    /// <summary>
    /// Windows está en modo oscuro. El acrílico de las listas desplegables toma el modo de Windows y no el del tema:
    /// si no coinciden, ControlColors les pone un tinte casi opaco del tema para que se lean bien.
    /// </summary>
    public static bool SystemDark => new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128;

    /// <summary>
    /// Acrílico de escritorio en la lista de un <see cref="ComboBox"/> (se pone en su estilo implícito, en
    /// Themes/Styles.xaml). La plantilla de WinUI no lo admite y Popup.SystemBackdrop no dibuja nada en ella: al abrir la
    /// lista se pone un SystemBackdropElement dentro de su PopupBorder, detrás de la lista, y se quita al cerrarla
    /// (dejarlo puesto con la lista cerrada hacía caer la app al cambiar de tema en marcha). Lleva el
    /// DesktopAcrylicBackdrop de WinUI, que toma el modo claro u oscuro de Windows (ver <see cref="SystemDark"/>).
    /// </summary>
    public static readonly DependencyProperty AcrylicProperty = DependencyProperty.RegisterAttached(
        "Acrylic", typeof(bool), typeof(Materials), new PropertyMetadata(false, OnAcrylicChanged));

    public static bool GetAcrylic(ComboBox element) => (bool)element.GetValue(AcrylicProperty);

    public static void SetAcrylic(ComboBox element, bool value) => element.SetValue(AcrylicProperty, value);

    private static void OnAcrylicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox box)
        {
            box.DropDownOpened -= ComboBox_DropDownOpened;
            box.DropDownClosed -= ComboBox_DropDownClosed;
            if ((bool)e.NewValue)
            {
                box.DropDownOpened += ComboBox_DropDownOpened;
                box.DropDownClosed += ComboBox_DropDownClosed;
            }
        }
    }

    private static void ComboBox_DropDownOpened(object? sender, object e)
    {
        var box = (ComboBox)sender!;
        if (HighContrast || FindPopupBorder(box) is not { } border)
        {
            return;
        }

        if (border.Child is ScrollViewer list)
        {
            // El tinte (ComboBoxDropDownBackground, el pincel compartido que recolorea ControlColors) pasa de
            // PopupBorder a una capa encima del acrílico: si no, el acrílico lo taparía.
            var tint = new Border { Background = border.Background, CornerRadius = border.CornerRadius, IsHitTestVisible = false };
            border.Background = null;
            border.Child = null;
            border.Child = new Grid { Children = { tint, list } };
        }

        if (border.Child is Grid { Children: [not SystemBackdropElement, ..] } layers)
        {
            layers.Children.Insert(0, new SystemBackdropElement
            {
                SystemBackdrop = new DesktopAcrylicBackdrop(), CornerRadius = border.CornerRadius, IsHitTestVisible = false,
            });
        }
    }

    private static void ComboBox_DropDownClosed(object? sender, object e)
    {
        if (FindPopupBorder((ComboBox)sender!) is { Child: Grid { Children: [SystemBackdropElement element, ..] } layers })
        {
            element.SystemBackdrop = null;
            layers.Children.RemoveAt(0);
        }
    }

    /// <summary>
    /// Popup &gt; PopupBorder &gt; ScrollViewer (DefaultComboBoxStyle); al abrirse, la ComboBox mete PopupBorder en un
    /// Canvas junto a otra capa. Solo mira los hijos directos de la raíz de la plantilla.
    /// </summary>
    private static Border? FindPopupBorder(ComboBox box)
    {
        if (VisualTreeHelper.GetChildrenCount(box) == 0 || VisualTreeHelper.GetChild(box, 0) is not Panel root)
        {
            return null;
        }

        foreach (var child in root.Children)
        {
            if (child is Popup { Name: "Popup" } popup)
            {
                var host = popup.Child is Canvas canvas
                    ? canvas.Children.FirstOrDefault(c => c is Border { Name: "PopupBorder" })
                    : popup.Child;
                return host is Border { Name: "PopupBorder" } found ? found : null;
            }
        }

        return null;
    }
}
