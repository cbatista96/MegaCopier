using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MegaCopier.App.Controls;

/// <summary>
/// Círculo de progreso del archivo en curso: un anillo tenue y, encima, un arco que se va cerrando con lo copiado.
/// Dibujado solo con formas de XAML: el ProgressRing de WinUI es una animación Lottie con su propio visual de
/// composición, y al crearse y ocultarse en las filas de la lista (al abrir y cerrar «Más») WinUI se cerraba de golpe.
/// </summary>
public sealed partial class ProgressArc : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressArc), new PropertyMetadata(0.0, (d, _) => ((ProgressArc)d).Draw()));

    private const double Thickness = 2.5;

    private readonly Ellipse _track = new() { StrokeThickness = Thickness };
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public ProgressArc()
    {
        IsTabStop = false;
        _track.Stroke = ThemeManager.TrackBrush;
        _arc.Stroke = ThemeManager.AccentBrush;
        Content = new Grid { Children = { _track, _arc } };
        SizeChanged += (_, _) => Draw();
    }

    /// <summary>De 0 a 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private void Draw()
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2)
        {
            return;
        }

        double fraction = Math.Clamp(Value / 100, 0, 1);
        double radius = (size - Thickness) / 2;
        var center = new Point(size / 2, size / 2);
        if (fraction <= 0.001)
        {
            _arc.Data = null;
            return;
        }

        if (fraction >= 0.999)
        {
            _arc.Data = new EllipseGeometry { Center = center, RadiusX = radius, RadiusY = radius };
            return;
        }

        // Desde arriba, en el sentido de las agujas del reloj.
        double angle = fraction * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle)));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }
}
