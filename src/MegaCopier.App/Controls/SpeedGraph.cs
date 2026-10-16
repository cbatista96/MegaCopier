using MegaCopier.App.Theming;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MegaCopier.App.Controls;

/// <summary>Gráfica de velocidad (una muestra por segundo) como un área rellena con el degradado del tema.</summary>
public sealed partial class SpeedGraph : UserControl
{
    private readonly Canvas _canvas = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path _area = new() { Opacity = 0.55 };
    private readonly Microsoft.UI.Xaml.Shapes.Path _line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly TextBlock _peak = new() { FontSize = 11 };
    private float[] _samples = [];

    public SpeedGraph()
    {
        var border = new Border
        {
            Child = _canvas,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(ThemeManager.Current.CornerRadius),
        };
        border.SetValue(Border.BackgroundProperty, ThemeManager.SurfaceBrush);
        border.SetValue(Border.BorderBrushProperty, ThemeManager.BorderBrush);
        border.BorderThickness = new Thickness(1);

        _area.SetValue(Shape.FillProperty, ThemeManager.TotalBrush);
        _line.SetValue(Shape.StrokeProperty, ThemeManager.AccentBrush);
        _peak.SetValue(TextBlock.ForegroundProperty, ThemeManager.TextSecondaryBrush);

        for (int i = 1; i < 4; i++)
        {
            var grid = new Line { StrokeThickness = 1, Opacity = 0.35, Tag = i };
            grid.SetValue(Shape.StrokeProperty, ThemeManager.BorderBrush);
            _canvas.Children.Add(grid);
        }

        _canvas.Children.Add(_area);
        _canvas.Children.Add(_line);
        _canvas.Children.Add(_peak);
        Content = border;
        SizeChanged += (_, _) => Redraw();
    }

    public void Update(float[] samples)
    {
        _samples = samples;
        Redraw();
    }

    private void Redraw()
    {
        double width = _canvas.ActualWidth, height = _canvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var line in _canvas.Children.OfType<Line>())
        {
            double y = height * (int)line.Tag / 4;
            (line.X1, line.Y1, line.X2, line.Y2) = (0, y, width, y);
        }

        var samples = _samples;
        if (samples.Length < 2)
        {
            _area.Data = null;
            _line.Data = null;
            _peak.Text = "";
            return;
        }

        float max = Math.Max(samples.Max(), 1);
        double top = 18; // espacio para la etiqueta del pico
        double step = width / (samples.Length - 1);
        Point At(int i) => new(i * step, height - ((samples[i] / max) * (height - top)));

        var line2 = new PathFigure { StartPoint = At(0) };
        var area = new PathFigure { StartPoint = new Point(0, height), IsClosed = true };
        area.Segments.Add(new LineSegment { Point = At(0) });
        for (int i = 1; i < samples.Length; i++)
        {
            line2.Segments.Add(new LineSegment { Point = At(i) });
            area.Segments.Add(new LineSegment { Point = At(i) });
        }

        area.Segments.Add(new LineSegment { Point = new Point(width, height) });
        _line.Data = new PathGeometry { Figures = { line2 } };
        _area.Data = new PathGeometry { Figures = { area } };
        _peak.Text = $"▲ {Format.Speed(max)}";
        Canvas.SetLeft(_peak, 8);
        Canvas.SetTop(_peak, 2);
    }
}
