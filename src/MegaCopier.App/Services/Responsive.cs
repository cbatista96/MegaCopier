using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Services;

/// <summary>Ayudas para que la interfaz se adapte al ancho en lugar de cortar textos.</summary>
internal static class Responsive
{
    /// <summary>
    /// Botones con icono y texto: con poco sitio muestran solo el icono y el texto pasa a ser su tooltip (y su nombre
    /// accesible), así nunca queda un rótulo cortado.
    /// </summary>
    public static void IconOnly(Panel buttons, bool iconOnly)
    {
        foreach (var button in buttons.Children.OfType<ButtonBase>())
        {
            if (button.Content is not Panel content || content.Children.OfType<TextBlock>().FirstOrDefault() is not { } label)
            {
                continue;
            }

            if (ToolTipService.GetToolTip(button) is null && !string.IsNullOrEmpty(label.Text))
            {
                ToolTipService.SetToolTip(button, label.Text);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label.Text);
            }

            label.Visibility = iconOnly ? Visibility.Collapsed : Visibility.Visible;

            // Sin texto, el botón se ajusta al icono (el estilo pide un ancho mínimo pensado para el rótulo).
            if (iconOnly)
            {
                button.MinWidth = 40;
                button.Padding = new Thickness(11, 6, 11, 6);
            }
            else
            {
                button.ClearValue(FrameworkElement.MinWidthProperty);
                button.ClearValue(Control.PaddingProperty);
            }
        }
    }

    /// <summary>
    /// Fila "icono · textos · control" de los ajustes: si no cabe, el control baja debajo del texto en lugar de
    /// apretarlo.
    /// </summary>
    public static void StackSettingRow(Grid row, bool stacked)
    {
        if (row.ColumnDefinitions.Count < 3)
        {
            return;
        }

        if (row.RowDefinitions.Count < 2)
        {
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // Los controles (columnas 2 en adelante) se recuerdan la primera vez, con su columna original.
        if (!Controls.TryGetValue(row, out var controls))
        {
            controls = [.. row.Children.OfType<FrameworkElement>()
                .Where(c => (int)c.GetValue(Grid.RowProperty) == 0 && (int)c.GetValue(Grid.ColumnProperty) >= 2)
                .Select(c => (c, (int)c.GetValue(Grid.ColumnProperty)))];
            Controls.Add(row, controls);
        }

        if (controls.Count == 0)
        {
            return;
        }

        row.RowSpacing = stacked ? 8 : 0;
        if (controls.Count == 1)
        {
            var (control, original) = controls[0];
            Grid.SetRow(control, stacked ? 1 : 0);
            Grid.SetColumn(control, stacked ? 1 : original);
            Grid.SetColumnSpan(control, stacked ? row.ColumnDefinitions.Count - 1 : 1);
            return;
        }

        // Varios controles: debajo van juntos en una fila propia (si se quedaran en sus columnas seguirían apretando el texto).
        if (!Holders.TryGetValue(row, out var holder))
        {
            holder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            Grid.SetRow(holder, 1);
            Grid.SetColumn(holder, 1);
            Grid.SetColumnSpan(holder, row.ColumnDefinitions.Count - 1);
            Holders.Add(row, holder);
        }

        bool inHolder = holder.Parent is not null;
        if (stacked && !inHolder)
        {
            foreach (var (control, _) in controls)
            {
                row.Children.Remove(control);
                holder.Children.Add(control);
            }

            row.Children.Add(holder);
        }
        else if (!stacked && inHolder)
        {
            holder.Children.Clear();
            row.Children.Remove(holder);
            foreach (var (control, original) in controls)
            {
                Grid.SetRow(control, 0);
                Grid.SetColumn(control, original);
                row.Children.Add(control);
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Grid, List<(FrameworkElement Control, int Column)>> Controls = [];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Grid, StackPanel> Holders = [];

    /// <summary>Todos los descendientes de un tipo.</summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
