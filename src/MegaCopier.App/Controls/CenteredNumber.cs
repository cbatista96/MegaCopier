using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Controls;

/// <summary>
/// Centra el número dentro de un NumberBox (el control no tiene una propiedad para ello: se ajusta el cuadro de texto
/// de su plantilla). Se aplica a todos con un estilo implícito en Styles.xaml.
/// </summary>
public static class CenteredNumber
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(CenteredNumber), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is NumberBox box && e.NewValue is true)
        {
            box.Loaded += (_, _) => Center(box);
            Center(box);
        }
    }

    private static void Center(NumberBox box)
    {
        if (FindInput(box) is { } input)
        {
            input.TextAlignment = TextAlignment.Center;
        }
    }

    private static TextBox? FindInput(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { Name: "InputBox" } input)
            {
                return input;
            }

            if (FindInput(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
