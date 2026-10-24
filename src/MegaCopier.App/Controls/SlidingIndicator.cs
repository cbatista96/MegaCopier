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
    private bool _selectionPending;
    private object? _selected;

    public SlidingIndicator(ListViewBase list, Canvas layer, double height = 16)
    {
        _bar.Height = height;
        _list = list;
        _layer = layer;
        _bar.Fill = ThemeManager.AccentBrush;
        _bar.RenderTransform = _move;
        _bar.RenderTransformOrigin = new Point(0.5, 0.5);
        layer.Children.Add(_bar);
        list.SelectionChanged += (_, _) =>
        {
            _selectionPending = true;
            // Una fila visitada por primera vez puede no estar medida durante SelectionChanged.
            list.DispatcherQueue.TryEnqueue(() => Update());
        };
        list.LayoutUpdated += (_, _) => Update(); // desplazamiento, cambio de tamaño, filas nuevas
    }

    private void Update()
    {
        if (_list.SelectedItem is not { } selected)
        {
            Stop();
            _bar.Visibility = Visibility.Collapsed;
            _at = null;
            _selected = null;
            _selectionPending = false;
            return;
        }
        // LayoutUpdated puede preceder a nuestro SelectionChanged si otro controlador
        // fuerza una disposición. Detectar también aquí el cambio evita consumirlo sin animar.
        if (!ReferenceEquals(_selected, selected)) _selectionPending = true;
        // Conservar el origen y la petición de animación hasta que la nueva fila esté colocada.
        if (_list.ContainerFromItem(selected) is not FrameworkElement container
            || container.ActualHeight == 0 || _layer.XamlRoot is null) return;

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
        _selected = selected;
        if (_at == target && _bar.Visibility == Visibility.Visible)
        {
            _selectionPending = false;
            return;
        }

        // Si se pulsa otra opción durante el recorrido, continuar desde la posición visible,
        // no desde el destino de la animación anterior.
        Point? previous = _at is { } position ? new Point(position.X + _move.TranslateX, position.Y + _move.TranslateY) : null;
        bool animate = _selectionPending;
        _selectionPending = false;
        Stop();
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
            if (_story == story) Stop();
        };
        _story = story;
        story.Begin();
    }

    private void Stop()
    {
        _story?.Stop();
        _story = null;
        _move.TranslateX = _move.TranslateY = 0;
        _move.ScaleY = 1;
    }

    private void Add(Storyboard story, string property, double to, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = ease };
        Storyboard.SetTarget(animation, _move);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
