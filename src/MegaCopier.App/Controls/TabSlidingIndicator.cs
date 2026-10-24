using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace MegaCopier.App.Controls;

// Una sola línea fuera de las plantillas: puede cruzar las pestañas sin que sus bordes la recorten.
internal sealed class TabSlidingIndicator
{
    private readonly SelectorBar _tabs;
    private readonly Canvas _layer;
    private readonly Rectangle _bar = new() { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Visibility = Visibility.Collapsed };
    private readonly CompositeTransform _move = new();
    private Storyboard? _story;
    private SelectorBarItem? _selected;
    private double _x, _width;

    public TabSlidingIndicator(SelectorBar tabs, Canvas layer)
    {
        _tabs = tabs;
        _layer = layer;
        _bar.Fill = ThemeManager.AccentBrush;
        _bar.RenderTransform = _move;
        layer.Children.Add(_bar);
        tabs.LayoutUpdated += (_, _) => Update();
        tabs.SelectionChanged += (_, _) => tabs.DispatcherQueue.TryEnqueue(Update);
    }

    private void Update()
    {
        foreach (var item in _tabs.Items)
            foreach (var marker in Responsive.Descendants<Rectangle>(item).Where(r => r.Name == "PART_SelectionVisual"))
                marker.Visibility = Visibility.Collapsed;

        if (_tabs.Visibility != Visibility.Visible || _tabs.SelectedItem is not { } selected)
        {
            _story?.Stop();
            _story = null;
            _selected = null;
            _bar.Visibility = Visibility.Collapsed;
            return;
        }
        if (selected.ActualWidth <= 0 || _layer.XamlRoot is null) return;
        var point = selected.TransformToVisual(_layer).TransformPoint(default);
        double width = Math.Max(24, selected.ActualWidth - 24);
        double x = point.X + (selected.ActualWidth - width) / 2;
        double y = point.Y + selected.ActualHeight - 3;
        Canvas.SetTop(_bar, y);
        if (_selected == selected && Math.Abs(_x - x) < .1 && Math.Abs(_width - width) < .1) return;

        // Capturar la posición visible antes de detener un recorrido interrumpido por otro clic.
        double previousX = _x + _move.TranslateX;
        double previousWidth = double.IsNaN(_bar.Width) ? width : _bar.Width;
        bool animate = _selected != null && _selected != selected && !BarAppearance.ReduceMotion;
        _story?.Stop();
        _selected = selected;
        _x = x;
        _width = width;
        Canvas.SetLeft(_bar, x);
        _move.TranslateX = 0;
        _bar.Width = width;
        _bar.Visibility = Visibility.Visible;
        if (!animate) return;

        var story = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Add(story, _move, "TranslateX", previousX - x, 0, ease);
        Add(story, _bar, "Width", previousWidth, width, ease);
        _story = story;
        story.Begin();
    }

    private static void Add(Storyboard story, DependencyObject target, string property, double from, double to, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(320), EasingFunction = ease, EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
