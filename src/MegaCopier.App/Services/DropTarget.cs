using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MegaCopier.App.Services;

/// <summary>
/// Dónde caería lo que se arrastra sobre una lista: el índice de inserción (antes de qué elemento), la línea que lo
/// indica y el elemento que hay bajo el puntero. Desplaza la lista sola cerca de los bordes.
/// </summary>
internal static class DropTarget
{
    public readonly record struct Spot(int Index, double LineY, int OverIndex, Rect OverBounds);

    /// <param name="minIndex">Primera posición permitida (lo ya empezado no se puede adelantar).</param>
    public static Spot Find(ListViewBase list, Point pointer, UIElement layer, int minIndex = 0)
    {
        int count = list.Items.Count;
        int index = count;
        int over = -1;
        Rect overBounds = default;
        double lineY = 0;
        double lastBottom = 0;

        for (int i = 0; i < count; i++)
        {
            if (list.ContainerFromIndex(i) is not FrameworkElement container || container.ActualHeight <= 0)
            {
                continue; // no visible (virtualizado)
            }

            var bounds = container.TransformToVisual(list).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            lastBottom = bounds.Bottom;
            if (pointer.Y >= bounds.Top && pointer.Y < bounds.Bottom)
            {
                over = i;
                overBounds = container.TransformToVisual(layer).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            }

            if (index == count && pointer.Y < bounds.Top + (bounds.Height / 2))
            {
                index = i;
            }
        }

        index = Math.Max(index, Math.Min(minIndex, count));
        if (index < count && list.ContainerFromIndex(index) is FrameworkElement target)
        {
            lineY = target.TransformToVisual(layer).TransformPoint(new Point(0, 0)).Y;
        }
        else
        {
            lineY = list.TransformToVisual(layer).TransformPoint(new Point(0, lastBottom)).Y;
        }

        AutoScroll(list, pointer);
        return new Spot(index, lineY, over, overBounds);
    }

    private static void AutoScroll(ListViewBase list, Point pointer)
    {
        if (Responsive.Descendants<ScrollViewer>(list).FirstOrDefault() is not { } scroller)
        {
            return;
        }

        const double edge = 36;
        if (pointer.Y < edge)
        {
            scroller.ChangeView(null, Math.Max(0, scroller.VerticalOffset - 24), null, disableAnimation: true);
        }
        else if (pointer.Y > list.ActualHeight - edge)
        {
            scroller.ChangeView(null, scroller.VerticalOffset + 24, null, disableAnimation: true);
        }
    }

    /// <summary>Coloca la línea de inserción (con un punto al inicio, como en el Explorador) en la capa.</summary>
    public static void ShowLine(FrameworkElement line, double y, double left, double width)
    {
        line.Visibility = Visibility.Visible;
        line.Width = Math.Max(0, width);
        Canvas.SetLeft(line, left);
        Canvas.SetTop(line, y - (line.Height / 2));
    }

    public static void ShowBox(FrameworkElement box, Rect bounds)
    {
        box.Visibility = Visibility.Visible;
        box.Width = bounds.Width;
        box.Height = bounds.Height;
        Canvas.SetLeft(box, bounds.Left);
        Canvas.SetTop(box, bounds.Top);
    }
}
