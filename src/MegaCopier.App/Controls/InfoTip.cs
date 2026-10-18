using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MegaCopier.App.Controls;

/// <summary>
/// Botón «i» junto a una opción: al pulsarlo explica para qué sirve, con ejemplos de cuándo conviene usarla. Los textos
/// salen de <c>Info_{Topic}_Title</c> e <c>Info_{Topic}_Text</c>; en el texto, un párrafo por línea y las que empiezan
/// por «• » se muestran como lista.
/// </summary>
public sealed partial class InfoTip : Button
{
    public InfoTip()
    {
        Style = (Style)Application.Current.Resources["McToolButton"];
        Width = 28;
        Height = 28;
        VerticalAlignment = VerticalAlignment.Center;
        Content = new McIcon { Glyph = "", FontSize = 14, Accent = true };
        Click += (_, _) => Open();
        Loaded += (_, _) =>
        {
            string title = Title();
            ToolTipService.SetToolTip(this, L.F("Info_More", title));
            AutomationProperties.SetName(this, L.F("Info_More", title));
        };
    }

    /// <summary>Título de la ayuda: el suyo o, si no tiene, el texto de la opción que está al lado.</summary>
    private string Title()
    {
        string key = $"Info_{Topic}_Title";
        string title = L.Get(key);
        if (title != key)
        {
            return title;
        }

        return Label ?? (Parent as Panel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
    }

    /// <summary>Nombre de la opción que explica (si no tiene título propio ni un texto al lado).</summary>
    public string? Label { get; set; }

    /// <summary>Tema de la ayuda: la clave de sus textos.</summary>
    public string Topic { get; set; } = "";

    private void Open()
    {
        var body = new StackPanel { Spacing = 8, MaxWidth = 380 };
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new McIcon { Glyph = "", FontSize = 16, Accent = true, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = Title(), Style = (Style)Application.Current.Resources["McStrong"], TextWrapping = TextWrapping.Wrap },
            },
        });

        StackPanel? list = null;
        foreach (string line in L.Get($"Info_{Topic}_Text").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                if (list is null)
                {
                    list = new StackPanel { Spacing = 4, Margin = new Thickness(4, 0, 0, 0) };
                    body.Children.Add(list);
                }

                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new McIcon { Glyph = "", FontSize = 11, Accent = true, Margin = new Thickness(0, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top });
                var text = new TextBlock { Text = line[2..], TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["McBody"] };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                list.Children.Add(row);
                continue;
            }

            list = null;
            body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["McBody"] });
        }

        var flyout = new Flyout { Content = body, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        flyout.ShowAt(this);
    }
}
