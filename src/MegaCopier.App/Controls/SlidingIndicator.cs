using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MegaCopier.App.Controls;

/// <summary>
/// La barrita de color junto al elemento elegido de una lista (las secciones de Ajustes, las copias): al elegir otro,
/// se desliza desde el anterior en lugar de aparecer de golpe. Va en una capa encima de la lista; la lista tiene que
/// tener desactivada la suya (recurso ListViewItemSelectionIndicatorVisualEnabled = False).
/// </summary>
internal sealed class SlidingIndicator
{
    private readonly ListViewBase _list;
    private readonly Canvas _layer;
    private readonly Rectangle _bar = new() { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly CompositeTransform _move = new();
    private Storyboard? _story;
    private Point? _at;

    public SlidingIndicator(ListViewBase list, Canvas layer)
    {
        _list = list;
        _layer = layer;
        _bar.Fill = ThemeManager.AccentBrush;
        _bar.RenderTransform = _move;
        _bar.RenderTransformOrigin = new Point(0.5, 0.5);
        layer.Children.Add(_bar);
        list.SelectionChanged += (_, _) => Update(animate: true);
        list.LayoutUpdated += (_, _) => Update(animate: false); // desplazamiento, cambio de tamaño, filas nuevas
    }

    private void Update(bool animate)
    {
        if (_list.SelectedItem is not { } selected || _list.ContainerFromItem(selected) is not FrameworkElement container
            || container.ActualHeight == 0 || _layer.XamlRoot is null)
        {
            _bar.Visibility = Visibility.Collapsed;
            _at = null;
            return;
        }

        Point corner;
        try
        {
            corner = container.TransformToVisual(_layer).TransformPoint(default);
        }
        catch (ArgumentException)
        {
            return; // la fila se está reciclando o aún no está en la ventana: en el siguiente repintado
        }

        var target = new Point(Math.Round(corner.X + 4), Math.Round(corner.Y + ((container.ActualHeight - _bar.Height) / 2)));
        if (_at == target && _bar.Visibility == Visibility.Visible)
        {
            return;
        }

        var previous = _at;
        _at = target;
        Canvas.SetLeft(_bar, target.X);
        Canvas.SetTop(_bar, target.Y);
        _bar.Visibility = Visibility.Visible;
        if (!animate || previous is not { } from || BarAppearance.ReduceMotion)
        {
            return;
        }

        // Sale de donde estaba y llega al nuevo sitio, estirándose un poco a medio camino.
        _move.TranslateX = from.X - target.X;
        _move.TranslateY = from.Y - target.Y;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var story = new Storyboard();
        Add(story, "TranslateX", 0, ease);
        Add(story, "TranslateY", 0, ease);
        var stretch = new DoubleAnimationUsingKeyFrames();
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)), Value = 1.6, EasingFunction = ease });
        stretch.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), Value = 1, EasingFunction = ease });
        Storyboard.SetTarget(stretch, _move);
        Storyboard.SetTargetProperty(stretch, "ScaleY");
        story.Children.Add(stretch);
        story.Completed += (_, _) =>
        {
            story.Stop();
            _move.TranslateX = _move.TranslateY = 0;
            _move.ScaleY = 1;
        };
        _story?.Stop();
        _story = story;
        story.Begin();
    }

    private void Add(Storyboard story, string property, double to, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = ease };
        Storyboard.SetTarget(animation, _move);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
