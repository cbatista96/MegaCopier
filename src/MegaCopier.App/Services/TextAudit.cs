using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Services;

/// <summary>
/// Diagnóstico (MEGACOPIER_AUDIT=1): busca textos que no se ven completos (cortados con "…", recortados por su
/// contenedor o con líneas que no caben) en la ventana y en los menús o diálogos abiertos, y los anota en
/// %LOCALAPPDATA%\MegaCopier\audit.log.
/// </summary>
internal static class TextAudit
{
    private static readonly HashSet<string> Reported = [];

    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("MEGACOPIER_AUDIT") == "1";

    private static string LogPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "audit.log");

    public static void Run(UIElement root, string where)
    {
        if (!Enabled || root.XamlRoot is null)
        {
            return;
        }

        var found = new List<string>();
        Walk(root, found);
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            if (popup.Child is { } child)
            {
                Walk(child, found);
            }
        }

        var fresh = found.Where(f => Reported.Add(where + "|" + f)).ToList();
        if (fresh.Count > 0)
        {
            File.AppendAllLines(LogPath, fresh.Select(f => $"[{where}] {f}"));
        }
    }

    private static void Walk(DependencyObject element, List<string> found)
    {
        if (element is UIElement { Visibility: Visibility.Collapsed })
        {
            return;
        }

        if (element is TextBlock { ActualWidth: > 0 } text && !string.IsNullOrWhiteSpace(text.Text) && Problem(text) is { } problem)
        {
            found.Add($"{problem}: \"{text.Text}\" (ancho {text.ActualWidth:0}, en {Describe(text)})");
        }

        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(element, i), found);
        }
    }

    private static string? Problem(TextBlock text)
    {
        if (text.IsTextTrimmed)
        {
            return "CORTADO con …";
        }

        // Se mide una copia para no tocar el diseño real.
        var probe = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontFamily = text.FontFamily,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            CharacterSpacing = text.CharacterSpacing,
            TextWrapping = text.TextWrapping,
        };

        if (text.TextWrapping == TextWrapping.NoWrap)
        {
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (probe.DesiredSize.Width > text.ActualWidth + 1.5)
            {
                return $"RECORTADO (necesita {probe.DesiredSize.Width:0})";
            }
        }
        else
        {
            probe.Measure(new Windows.Foundation.Size(text.ActualWidth + 2, double.PositiveInfinity));
            if (probe.DesiredSize.Height > text.ActualHeight + 1.5)
            {
                return $"LÍNEAS QUE NO CABEN (necesita alto {probe.DesiredSize.Height:0}, tiene {text.ActualHeight:0})";
            }
        }

        // Visible en parte: el texto mide lo que necesita, pero un contenedor más pequeño lo recorta.
        return ClippedByParent(text);
    }

    private static string? ClippedByParent(FrameworkElement text)
    {
        Windows.Foundation.Rect bounds;
        try
        {
            bounds = text.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, text.ActualWidth, text.ActualHeight));
        }
        catch (ArgumentException)
        {
            return null;
        }

        DependencyObject? current = VisualTreeHelper.GetParent(text);
        for (int depth = 0; current is FrameworkElement parent && depth < 12; depth++)
        {
            // Dentro de una zona con desplazamiento es normal que el contenido se salga: ahí se deja de mirar.
            if (parent is ScrollContentPresenter or ScrollViewer or Microsoft.UI.Xaml.Controls.Primitives.ScrollPresenter or ItemsRepeater)
            {
                return null;
            }

            if (parent.ActualWidth > 0 && parent.ActualHeight > 0)
            {
                var box = parent.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, parent.ActualWidth, parent.ActualHeight));
                double cutRight = bounds.Right - box.Right;
                double cutBottom = bounds.Bottom - box.Bottom;
                if (cutRight > 1.5 || cutBottom > 1.5)
                {
                    return $"RECORTADO por {parent.GetType().Name}{(parent.Name.Length > 0 ? " " + parent.Name : "")} (sobran {Math.Max(cutRight, cutBottom):0} px)";
                }
            }

            current = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static string Describe(DependencyObject element)
    {
        var path = new List<string>();
        for (var current = element; current is not null && path.Count < 6; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name: { Length: > 0 } name })
            {
                path.Add(name);
            }
            else if (current is Control or UserControl)
            {
                path.Add(current.GetType().Name);
            }
        }

        return string.Join(" < ", path);
    }
}
