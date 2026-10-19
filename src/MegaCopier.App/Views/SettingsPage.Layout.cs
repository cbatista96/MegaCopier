using MegaCopier.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Orden visual de las tarjetas de ajustes, aplicado a todas a la vez (las del XAML y las que se crean en código):
///  - el control de la derecha (desplegable, interruptor, número, botón) queda centrado en vertical en la tarjeta;
///  - el botón «i» pasa de la línea del título a su propia columna, justo antes del control y centrado con él;
///  - lo que va debajo del título (una lista, una casilla secundaria...) entra en la columna del texto, para que el
///    control quede centrado respecto a toda la tarjeta y no solo a la primera línea.
/// Así ningún control se ve "subido" ni descolocado, tenga la tarjeta una línea de texto o cinco.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>Sangría con la que el XAML alinea lo de debajo con el texto (icono de 18 + separación de 12).</summary>
    private const double TextIndent = 24;

    private void PolishCards()
    {
        var cardStyle = (Style)Application.Current.Resources["McSettingCard"];
        foreach (var card in LogicalDescendants<Border>(SectionsHost).Where(b => b.Style == cardStyle).ToList())
        {
            if (card.Child is Grid row && IsSettingRow(row))
            {
                PolishRow(row);
            }
            else if (card.Child is StackPanel { Children: [Grid first, ..] } stack && IsSettingRow(first))
            {
                PolishRow(first);
                // Más filas en la misma tarjeta (p. ej. «Conexiones a la vez» debajo de los servidores): igual de
                // centradas que la primera.
                foreach (var other in stack.Children.Skip(1).OfType<Grid>().Where(IsSettingRow).ToList())
                {
                    PolishRow(other);
                }

                FoldUnderText(stack, first);
                CenterLoneInfo(card, stack, first);
            }
        }
    }

    /// <summary>Fila "icono · textos · controles": la primera columna fija, la segunda se estira.</summary>
    private static bool IsSettingRow(Grid grid) =>
        grid.ColumnDefinitions.Count >= 3 && grid.ColumnDefinitions[0].Width.IsAuto && grid.ColumnDefinitions[1].Width.IsStar;

    private static void PolishRow(Grid row)
    {
        foreach (var child in row.Children.OfType<FrameworkElement>())
        {
            if (Grid.GetColumn(child) >= 1)
            {
                child.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        if (row.Children.OfType<StackPanel>().FirstOrDefault(s => Grid.GetColumn(s) == 1) is not { } texts
            || texts.Children.OfType<Grid>().FirstOrDefault(g => g.Children.OfType<InfoTip>().Any()) is not { } titleLine)
        {
            return;
        }

        // El «i» sale de la línea del título a una columna propia, entre el texto y los controles.
        var info = titleLine.Children.OfType<InfoTip>().First();
        info.Label ??= titleLine.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
        titleLine.Children.Remove(info);
        foreach (var child in row.Children.OfType<FrameworkElement>())
        {
            int column = Grid.GetColumn(child);
            if (column >= 2)
            {
                Grid.SetColumn(child, column + 1);
            }
            else if (column + Grid.GetColumnSpan(child) > 2)
            {
                Grid.SetColumnSpan(child, Grid.GetColumnSpan(child) + 1); // lo que ocupaba hasta el control sigue llegando
            }
        }

        row.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = GridLength.Auto });
        info.Margin = new Thickness(0);
        info.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(info, 2);
        Grid.SetRow(info, 0);
        row.Children.Add(info);
    }

    /// <summary>
    /// Lo que la tarjeta muestra debajo del título (sangrado para alinearse con el texto) pasa a la columna del texto:
    /// el control de la derecha queda centrado respecto a toda la tarjeta. Lo que ocupa todo el ancho se queda donde está.
    /// </summary>
    private static void FoldUnderText(StackPanel stack, Grid row)
    {
        var below = stack.Children.Skip(1).OfType<FrameworkElement>().ToList();
        bool hasControl = row.Children.OfType<FrameworkElement>().Any(c => Grid.GetColumn(c) >= 2 && c is not InfoTip);
        // Los campos para escribir (horas, números, rutas) necesitan su ancho: con ellos la tarjeta se queda como está.
        static bool HasInput(object element) => element is TextBox or NumberBox or ComboBox or Slider or TimePicker or PasswordBox
            || LogicalDescendants<Control>(element).Any(c => c is TextBox or NumberBox or ComboBox or Slider or TimePicker or PasswordBox);
        if (!hasControl || below.Count == 0 || below.Any(e => e.Margin.Left < TextIndent || e is VariableSizedWrapGrid || HasInput(e))
            || row.Children.OfType<StackPanel>().FirstOrDefault(s => Grid.GetColumn(s) == 1) is not { } texts)
        {
            return;
        }

        foreach (var element in below)
        {
            stack.Children.Remove(element);
            element.Margin = new Thickness(0, Math.Max(element.Margin.Top, 6), element.Margin.Right, element.Margin.Bottom);
            texts.Children.Add(element);
        }
    }

    /// <summary>
    /// Tarjeta sin control a la derecha (solo la «i») y con algo debajo que ocupa todo el ancho (casillas en fila):
    /// la «i» pasa a una columna propia a la derecha de toda la tarjeta, centrada en su alto, como en las demás.
    /// </summary>
    private static void CenterLoneInfo(Border card, StackPanel stack, Grid row)
    {
        bool onlyInfo = !row.Children.OfType<FrameworkElement>().Any(c => Grid.GetColumn(c) >= 2 && c is not InfoTip);
        if (!onlyInfo || stack.Children.Count < 2 || row.Children.OfType<InfoTip>().FirstOrDefault() is not { } info)
        {
            return;
        }

        row.Children.Remove(info);
        card.Child = null;
        var holder = new Grid { ColumnSpacing = 14 };
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        holder.Children.Add(stack);
        info.VerticalAlignment = VerticalAlignment.Center;
        info.Margin = new Thickness(0);
        Grid.SetColumn(info, 1);
        Grid.SetRow(info, 0);
        holder.Children.Add(info);
        card.Child = holder;
    }

    /// <summary>Descendientes por el árbol lógico (también los de secciones ocultas, que aún no tienen árbol visual).</summary>
    private static IEnumerable<T> LogicalDescendants<T>(object root)
        where T : DependencyObject
    {
        IEnumerable<object> children = root switch
        {
            Panel panel => panel.Children,
            Border border when border.Child is not null => [border.Child],
            ContentControl { Content: UIElement content } => [content],
            UserControl { Content: { } content } => [content],
            _ => [],
        };

        foreach (var child in children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in LogicalDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
