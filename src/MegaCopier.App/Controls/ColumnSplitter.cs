using MegaCopier.App.Theming;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace MegaCopier.App.Controls;

/// <summary>
/// Línea divisoria entre dos columnas de una cabecera: se arrastra para cambiar el ancho de la columna de la
/// izquierda (doble clic, ancho de siempre). Una zona de 12 px que se puede agarrar con una línea fina en medio, que
/// se resalta con el color de acento al pasar por encima o al arrastrar.
/// </summary>
public sealed partial class ColumnSplitter : Grid
{
    private readonly Rectangle _line = new() { Width = 1, Height = 16, RadiusX = 0.5, RadiusY = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private double _startX;
    private bool _dragging;

    public ColumnSplitter()
    {
        Width = 12;
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(_line);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Highlight(false);
        PointerEntered += (_, _) => Highlight(true);
        PointerExited += (_, _) => Highlight(_dragging);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => Finish();
        DoubleTapped += (_, e) =>
        {
            Reset?.Invoke();
            e.Handled = true;
        };
    }

    /// <summary>Al arrastrar: cuántos píxeles se movió desde que se pulsó.</summary>
    public event Action<double>? Dragged;

    /// <summary>Al empezar a arrastrar (para recordar el ancho de partida).</summary>
    public event Action? Started;

    /// <summary>Al soltar (para guardar el ancho).</summary>
    public event Action? Finished;

    /// <summary>Doble clic: volver al ancho de siempre.</summary>
    public event Action? Reset;

    private void Highlight(bool on)
    {
        _line.Fill = on ? ThemeManager.AccentBrush : ThemeManager.TextSecondaryBrush;
        _line.Opacity = on ? 1 : 0.45;
        _line.Width = on ? 2 : 1;
        _line.Height = on ? 20 : 16;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _startX = e.GetCurrentPoint(null).Position.X;
        _dragging = CapturePointer(e.Pointer);
        Highlight(true);
        Started?.Invoke();
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Dragged?.Invoke(e.GetCurrentPoint(null).Position.X - _startX);
            e.Handled = true;
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleasePointerCapture(e.Pointer);
        Finish();
        e.Handled = true;
    }

    private void Finish()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Highlight(false);
        Finished?.Invoke();
    }
}
