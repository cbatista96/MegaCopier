using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace MegaCopier.App.Controls;

/// <summary>Cómo se dibuja cada estilo de barra.</summary>
public sealed partial class FlowBar
{
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color[] RainbowColors =
    [
        Color.FromArgb(255, 239, 68, 68), Color.FromArgb(255, 249, 115, 22), Color.FromArgb(255, 234, 179, 8),
        Color.FromArgb(255, 34, 197, 94), Color.FromArgb(255, 6, 182, 212), Color.FromArgb(255, 59, 130, 246),
        Color.FromArgb(255, 168, 85, 247),
    ];

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private Rectangle? _base;
    private Ellipse? _blink;
    private Grid? _marker;
    private Rectangle? _trail;
    private Rectangle? _head;
    private Canvas? _sparks;
    private TextBlock? _terminalBody;
    private StackPanel? _terminal;
    private double _cell;
    private double _trailWidth;
    private double _headWidth;
    private Color[] _stops = [];

    /// <summary>Vuelve a montar las capas del estilo (al cambiar el estilo, los colores, el tamaño o el estado).</summary>
    private void Rebuild()
    {
        bool wasRunning = _loopsRunning;
        StopLoops();
        if (_glowPulsing)
        {
            _glowShadow.StopAnimation("Opacity");
            _glowPulsing = false;
        }

        _loops.Clear();
        _decor.Children.Clear();
        _canvas.Children.Clear();
        _overlay.Children.Clear();
        _decor.Children.Add(_canvas);
        _base = null;
        _blink = null;
        _marker = null;
        _trail = null;
        _head = null;
        _sparks = null;
        _terminalBody = null;
        _cell = 0;
        if (_terminal is not null)
        {
            _outer.Children.Remove(_terminal);
            _terminal = null;
        }

        double width = _root.ActualWidth;
        _builtWidth = width;
        double h = _height;

        // Lo común a todos: la pista con el color del tema, el relleno a todo el alto.
        _track.Child = null;
        _track.Background = ThemeManager.TrackBrush;
        _track.BorderBrush = ThemeManager.BorderBrush;
        _track.BorderThickness = new Thickness(_settings.Border ? 1 : 0);
        _track.VerticalAlignment = VerticalAlignment.Stretch;
        _track.Height = double.NaN;
        _fill.VerticalAlignment = VerticalAlignment.Stretch;
        _fill.Height = double.NaN;
        _fill.Visibility = Visibility.Visible;
        _ghost.Visibility = Visibility.Collapsed;
        _stops = Stops();
        _canvas.Height = h;
        _glowShadow.Color = MainColor();
        _glowShadow.BlurRadius = (float)(6 + (14 * GlowAmount));

        switch (BarKind)
        {
            case BarStyle.Aero: BuildAero(width, h); break;
            case BarStyle.Wave: BuildWave(width, h); break;
            case BarStyle.Stripes: Base(); Stripes(width, h, Color.FromArgb((byte)(50 + (70 * GlowAmount)), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0.9); break;
            case BarStyle.Neon: BuildNeon(h); break;
            case BarStyle.Blocks: BuildCells(width, h, Math.Max(h * 0.85, 6), Math.Max(2, h * 0.2), gloss: true); break;
            case BarStyle.Segments: BuildCells(width, h, Math.Max(width / 20, 4), 3, gloss: false); break;
            case BarStyle.Led: BuildLed(width, h); break;
            case BarStyle.Rainbow: Flowing(width, h, RainbowColors, 4.5); break;
            case BarStyle.Pulse: Base(); Pulse(); break;
            case BarStyle.Comet: BuildComet(h); break;
            case BarStyle.Glass: BuildGlass(width, h); break;
            case BarStyle.Fire: BuildFire(width, h); break;
            case BarStyle.Terminal: BuildTerminal(h); break;
            case BarStyle.Line: BuildLine(h); break;
            case BarStyle.Dual: Base(); _ghost.Visibility = Visibility.Visible; _ghost.Background = MainBrush(); _ghost.Opacity = 0.35; break;
            case BarStyle.Pixels: BuildPixels(width, h); break;
            case BarStyle.Metal: BuildMetal(width, h); break;
            case BarStyle.Outline: BuildOutline(width, h); break;
            case BarStyle.Flow: Flowing(width, h, [_stops[0], _stops[^1]], 3.2); break;
            case BarStyle.Bubbles: Base(); Bubbles(width, h); break;
            default: Base(); Shimmer(width, h, 140, 1.6, 0.18 + (0.3 * GlowAmount)); break; // Fluent y Semáforo
        }

        var warning = ThemeManager.WarningBrush.Color;
        _pauseStripesBrush = StripeBrush(h, Color.FromArgb((byte)(warning.A * 0.55), warning.R, warning.G, warning.B), Color.FromArgb(0, 0, 0, 0), width + h); // transparencia en el color, no en el elemento
        _text.FontSize = Math.Clamp(h * 0.62, 10, 13);
        _text.Foreground = ThemeManager.TextBrush;
        UpdateValueDependent();
        SetFillWidth(_shownValue, animate: false);
        _loopsRunning = false;
        UpdateState();
        if (wasRunning && !_loopsRunning && Running)
        {
            StartLoops();
        }
    }

    /// <summary>Lo que cambia con el avance: color del semáforo, LED que parpadea, texto de terminal y porcentaje.</summary>
    private void UpdateValueDependent()
    {
        double value = Math.Clamp(Value, 0, 1);
        if (_base is not null && TrafficColors && !IsError && !IsWarning)
        {
            _base.Fill = new SolidColorBrush(TrafficColor(value));
            _glowShadow.Color = TrafficColor(value);
        }

        if (_blink is not null && _cell > 0 && _root.ActualWidth > 0)
        {
            int lit = (int)Math.Ceiling(value * _root.ActualWidth / _cell);
            _blink.Visibility = lit > 0 && value < 1 ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_blink, Math.Max(0, (lit - 1) * _cell));
        }

        if (_terminalBody is not null)
        {
            double charWidth = _terminalBody.FontSize * 0.6;
            int cells = Math.Max(4, (int)((_root.ActualWidth - (charWidth * 9)) / charWidth));
            int done = (int)Math.Round(value * cells);
            _terminalBody.Text = IsIndeterminate
                ? "[" + new string('·', cells) + "]"
                : $"[{new string('#', done)}{new string('·', cells - done)}] {value * 100,5:0.0}%";
        }

        bool inside = _settings.Text == BarText.Inside && BarKind != BarStyle.Terminal && double.IsNaN(FixedHeight) && !IsIndeterminate;
        _text.Visibility = inside ? Visibility.Visible : Visibility.Collapsed;
        if (inside)
        {
            _text.Text = (value * 100).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " %";
        }
    }

    /// <summary>Lo que va pegado al final de la parte llena: cola del cometa, marcador de la línea, chispas.</summary>
    private void FillWidthChanged(double width)
    {
        if (_trail is not null && _head is not null)
        {
            _trail.Width = Math.Min(_trailWidth, width);
            _head.Width = Math.Min(_headWidth, width);
        }

        if (_sparks is not null)
        {
            Canvas.SetLeft(_sparks, width);
        }

        if (_marker is not null)
        {
            Canvas.SetLeft(_marker, Math.Clamp(width - (_height / 2), 0, Math.Max(0, _root.ActualWidth - _height)));
        }
    }

    private double GlowLevel() => BarKind switch
    {
        BarStyle.Neon => 0.45 + (0.5 * GlowAmount),
        BarStyle.Aero or BarStyle.Glass or BarStyle.Comet => 0.3 * GlowAmount,
        _ => 0,
    };

    // ---------------------------------------------------------------------------------------------------------
    // Colores

    private bool TrafficColors => BarKind == BarStyle.Traffic || (_settings.ColorMode == BarColorMode.Traffic && BarKind != BarStyle.Rainbow);

    /// <summary>Colores del relleno, de izquierda a derecha.</summary>
    private Color[] Stops()
    {
        if (IsError)
        {
            return [ThemeManager.ErrorBrush.Color];
        }

        if (IsWarning)
        {
            return [ThemeManager.WarningBrush.Color];
        }

        if (TrafficColors)
        {
            return [TrafficColor(Math.Clamp(Value, 0, 1))];
        }

        if (BarKind == BarStyle.Rainbow || _settings.ColorMode == BarColorMode.Rainbow)
        {
            return RainbowColors;
        }

        if (_settings.ColorMode == BarColorMode.Custom && !Plain)
        {
            return [ThemeManager.Parse(_settings.ColorStart), ThemeManager.Parse(_settings.ColorEnd)];
        }

        if (BarAppearance.NaturalColors(BarKind) is { } natural)
        {
            return [natural.Start, natural.End];
        }

        return Fill switch
        {
            LinearGradientBrush gradient when gradient.GradientStops.Count > 0 => [.. gradient.GradientStops.OrderBy(s => s.Offset).Select(s => s.Color)],
            SolidColorBrush solid => [solid.Color],
            _ => [ThemeManager.AccentBrush.Color],
        };
    }

    private Color MainColor() => _stops.Length == 0 ? ThemeManager.AccentBrush.Color : _stops[^1];

    private Color ColorAt(double t)
    {
        if (_stops.Length == 1)
        {
            return _stops[0];
        }

        t = Math.Clamp(t, 0, 1) * (_stops.Length - 1);
        int i = Math.Min((int)t, _stops.Length - 2);
        return Mix(_stops[i], _stops[i + 1], t - i);
    }

    private Brush MainBrush()
    {
        if (_stops.Length == 1)
        {
            return new SolidColorBrush(_stops[0]);
        }

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < _stops.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop { Color = _stops[i], Offset = (double)i / (_stops.Length - 1) });
        }

        return brush;
    }

    private static Color TrafficColor(double value)
    {
        var red = Color.FromArgb(255, 229, 72, 77);
        var amber = Color.FromArgb(255, 245, 165, 36);
        var green = Color.FromArgb(255, 48, 164, 108);
        return value < 0.5 ? Mix(red, amber, value / 0.5) : Mix(amber, green, (value - 0.5) / 0.5);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + ((b.A - a.A) * t)), (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    private static Color Lighter(Color c, double amount) => Mix(c, Color.FromArgb(c.A, 255, 255, 255), amount);

    private static Color Darker(Color c, double amount) => Mix(c, Color.FromArgb(c.A, 0, 0, 0), amount);

    private static Color Alpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    private static LinearGradientBrush Gradient(Point start, Point end, params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop { Color = color, Offset = offset });
        }

        return brush;
    }

    /// <summary>Rayas diagonales (absolutas: no se estiran con la barra) para cubrir <paramref name="length"/> píxeles.</summary>
    private static LinearGradientBrush StripeBrush(double size, Color on, Color off, double length) =>
        Repeated(Gradient(new Point(0, 0), new Point(size, size), (on, 0), (on, 0.5), (off, 0.5), (off, 1)), length);

    /// <summary>
    /// Repite a mano un degradado (de su punto inicial al final es un periodo, en píxeles) hasta cubrir
    /// <paramref name="length"/> píxeles en esa dirección. No se usa GradientSpreadMethod.Repeat: WinUI no lo dibuja por
    /// sí mismo y, con él en pantalla, otros textos, iconos y botones de la ventana se quedaban dibujados apagados.
    /// </summary>
    private static LinearGradientBrush Repeated(LinearGradientBrush period, double length)
    {
        var start = period.StartPoint;
        double dx = period.EndPoint.X - start.X, dy = period.EndPoint.Y - start.Y;
        double step = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
        int count = Math.Clamp((int)Math.Ceiling(Math.Max(1, length) / step) + 1, 1, 1200);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = start,
            EndPoint = new Point(start.X + (dx * count), start.Y + (dy * count)),
        };
        var stops = period.GradientStops.OrderBy(s => s.Offset).ToList();
        for (int k = 0; k < count; k++)
        {
            foreach (var stop in stops)
            {
                brush.GradientStops.Add(new GradientStop { Color = stop.Color, Offset = (k + stop.Offset) / count });
            }
        }

        return brush;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Piezas

    private Rectangle Base(double opacity = 1)
    {
        _base = new Rectangle { Fill = MainBrush(), Opacity = opacity };
        _decor.Children.Insert(0, _base);
        return _base;
    }

    /// <summary>Un elemento en el lienzo de la parte llena, a la izquierda y a todo el alto.</summary>
    private T Add<T>(T element, double width, double height, double left = 0, double top = 0)
        where T : FrameworkElement
    {
        element.Width = width;
        element.Height = height;
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _canvas.Children.Add(element);
        return element;
    }

    /// <summary>Un brillo suave que recorre la parte llena.</summary>
    private void Shimmer(double width, double h, double size, double seconds, double alpha)
    {
        var shine = Add(new Rectangle
        {
            Fill = Gradient(new Point(0, 0.5), new Point(1, 0.5), (Alpha(White, 0), 0), (Alpha(White, alpha), 0.5), (Alpha(White, 0), 1)),
        }, size, h);
        var offset = new TranslateTransform { X = -size };
        shine.RenderTransform = offset;
        Loop(offset, "X", new DoubleAnimation
        {
            From = -size,
            To = Math.Max(width, 200),
            Duration = Seconds(seconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Destello de lado a lado y una pausa (el reflejo de las barras de Windows 7).</summary>
    private void Glare(double width, double h, double size, double passSeconds, double totalSeconds, double alpha)
    {
        var glare = Add(new Rectangle
        {
            Fill = Gradient(new Point(0, 0.5), new Point(1, 0.5), (Alpha(White, 0), 0), (Alpha(White, alpha * 0.6), 0.35), (Alpha(White, alpha), 0.5), (Alpha(White, alpha * 0.6), 0.65), (Alpha(White, 0), 1)),
        }, size, h);
        var offset = new TranslateTransform { X = -size };
        glare.RenderTransform = offset;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = Seconds(totalSeconds) };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = -size });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(passSeconds / Speed), Value = Math.Max(width, 200), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(totalSeconds / Speed), Value = Math.Max(width, 200) });
        Loop(offset, "X", animation);
    }

    /// <summary>Brillo de cristal en la mitad de arriba.</summary>
    private void Gloss(double h, double alpha) => _decor.Children.Add(new Rectangle
    {
        VerticalAlignment = VerticalAlignment.Top,
        Height = Math.Max(1, h * 0.5),
        Fill = Gradient(new Point(0.5, 0), new Point(0.5, 1), (Alpha(White, alpha), 0), (Alpha(White, alpha * 0.25), 1)),
    });

    private void Stripes(double width, double h, Color on, Color off, double seconds)
    {
        double size = Math.Max(6, h);
        var stripes = Add(new Rectangle { Fill = StripeBrush(size, on, off, width + (size * 3) + h) }, width + (size * 2) + 2, h, 0);
        var offset = new TranslateTransform { X = -size * 2 };
        stripes.RenderTransform = offset;
        Loop(offset, "X", new DoubleAnimation { From = -size * 2, To = 0, Duration = Seconds(seconds) });
    }

    private void Pulse()
    {
        var light = new Rectangle { Fill = new SolidColorBrush(White), Opacity = 0 };
        _decor.Children.Add(light);
        Loop(light, "Opacity", new DoubleAnimation
        {
            From = 0,
            To = 0.08 + (0.32 * GlowAmount),
            Duration = Seconds(0.6),
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Colores que corren de derecha a izquierda sin cortes (arcoíris, degradado fluido).</summary>
    private void Flowing(double width, double h, Color[] colors, double seconds)
    {
        var cycle = colors.Concat(colors).Append(colors[0]).ToArray();
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < cycle.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop { Color = cycle[i], Offset = (double)i / (cycle.Length - 1) });
        }

        double span = Math.Max(width, 200);
        var rect = Add(new Rectangle { Fill = brush }, span * 2, h);
        var offset = new TranslateTransform();
        rect.RenderTransform = offset;
        Loop(offset, "X", new DoubleAnimation { From = 0, To = -span, Duration = Seconds(seconds) });
    }

    // ---------------------------------------------------------------------------------------------------------
    // Estilos

    private void BuildAero(double width, double h)
    {
        _track.Child = new Rectangle
        {
            VerticalAlignment = VerticalAlignment.Top,
            Height = Math.Max(1, h * 0.5),
            Fill = Gradient(new Point(0.5, 0), new Point(0.5, 1), (Alpha(White, 0.22), 0), (Alpha(White, 0.04), 1)),
        };
        Base();
        Gloss(h, 0.25 + (0.4 * GlowAmount));
        Glare(width, h, Math.Max(60, h * 8), 1.7, 2.8, 0.45 + (0.45 * GlowAmount));
    }

    private void BuildWave(double width, double h)
    {
        double length = Math.Max(28, h * 4.5);
        double amplitude = Math.Max(1, h * 0.18);
        var brush = MainBrush();
        Path Wave(double phase, double opacity) => Add(new Path { Fill = brush, Opacity = opacity, Data = WaveGeometry(width + length, h, length, amplitude, phase) }, width + length, h);
        var back = Wave(Math.PI, 0.45);
        var front = Wave(0, 1);
        var backOffset = new TranslateTransform { X = -length };
        var frontOffset = new TranslateTransform();
        back.RenderTransform = backOffset;
        front.RenderTransform = frontOffset;
        Loop(frontOffset, "X", new DoubleAnimation { From = 0, To = -length, Duration = Seconds(1.3) });
        Loop(backOffset, "X", new DoubleAnimation { From = -length, To = 0, Duration = Seconds(1.9) });
    }

    /// <summary>Superficie de un líquido: el borde de arriba ondula y por debajo todo está lleno.</summary>
    private static Geometry WaveGeometry(double width, double h, double length, double amplitude, double phase)
    {
        var figure = new PathFigure { StartPoint = new Point(0, h), IsClosed = true, IsFilled = true };
        var line = new PolyLineSegment();
        for (double x = 0; x <= width + 2; x += 2)
        {
            line.Points.Add(new Point(x, amplitude + (amplitude * Math.Sin((2 * Math.PI * x / length) + phase))));
        }

        line.Points.Add(new Point(width + 2, h));
        figure.Segments.Add(line);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void BuildNeon(double h)
    {
        _stops = [.. _stops.Select(c => Lighter(c, 0.15))];
        Base();
        _decor.Children.Add(new Rectangle
        {
            VerticalAlignment = VerticalAlignment.Center,
            Height = Math.Max(1, h * 0.28),
            RadiusX = h * 0.14,
            RadiusY = h * 0.14,
            Fill = new SolidColorBrush(Alpha(White, 0.25 + (0.45 * GlowAmount))),
        });
        _glowShadow.Color = MainColor();
    }

    /// <summary>Bloques separados (cada uno con su color del degradado y brillo arriba) o segmentos.</summary>
    private void BuildCells(double width, double h, double cell, double gap, bool gloss)
    {
        _cell = cell;
        if (width <= 0)
        {
            return;
        }

        for (double x = 0; x < width; x += cell)
        {
            var color = ColorAt((x + (cell / 2)) / width);
            Brush fill = gloss
                ? Gradient(new Point(0.5, 0), new Point(0.5, 1), (Lighter(color, 0.45), 0), (color, 0.55), (Darker(color, 0.12), 1))
                : new SolidColorBrush(color);
            Add(new Rectangle { Fill = fill, RadiusX = Math.Min(2, _radius), RadiusY = Math.Min(2, _radius) }, Math.Max(1, cell - gap), h, x);
        }
    }

    /// <summary>Puntos de luz: los encendidos con el color de la barra, el último parpadea.</summary>
    private void BuildLed(double width, double h)
    {
        double gap = Math.Max(2, h * 0.45);
        _cell = h + gap;
        _track.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _track.BorderThickness = new Thickness(0);
        var dim = new Canvas();
        _track.Child = dim;
        if (width <= 0)
        {
            return;
        }

        for (double x = 0; x + h <= width + 0.5; x += _cell)
        {
            var off = new Ellipse { Width = h, Height = h, Fill = ThemeManager.TrackBrush };
            Canvas.SetLeft(off, x);
            dim.Children.Add(off);
            var color = ColorAt(x / width);
            Add(new Ellipse { Fill = Gradient(new Point(0.3, 0.2), new Point(0.7, 1), (Lighter(color, 0.5), 0), (color, 0.6)) }, h, h, x);
        }

        _blink = new Ellipse { Width = h, Height = h, Fill = new SolidColorBrush(Alpha(White, 0.75)), Opacity = 0 };
        _overlay.Children.Add(_blink);
        Loop(_blink, "Opacity", new DoubleAnimation { From = 0, To = 1, Duration = Seconds(0.45), AutoReverse = true });
    }

    private void BuildComet(double h)
    {
        Base(0.45);
        var color = MainColor();
        _trailWidth = 180;
        _headWidth = Math.Max(10, h * 2);
        _trail = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Fill = Gradient(new Point(0, 0.5), new Point(1, 0.5), (Alpha(color, 0), 0), (Lighter(color, 0.25), 1)),
        };
        _head = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Fill = Gradient(new Point(0, 0.5), new Point(1, 0.5), (Alpha(White, 0), 0), (Alpha(White, 0.35 + (0.6 * GlowAmount)), 1)),
        };
        _decor.Children.Add(_trail);
        _decor.Children.Add(_head);
        Loop(_head, "Opacity", new DoubleAnimation { From = 0.6, To = 1, Duration = Seconds(0.12), AutoReverse = true });

        // Chispas que saltan hacia atrás desde la cabeza.
        _sparks = new Canvas();
        _overlay.Children.Add(_sparks);
        var random = new Random(7);
        for (int i = 0; i < 4; i++)
        {
            double size = Math.Max(2, h * 0.3);
            var spark = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Lighter(color, 0.6)), Opacity = 0 };
            Canvas.SetTop(spark, random.NextDouble() * Math.Max(0, h - size));
            var move = new TranslateTransform();
            spark.RenderTransform = move;
            _sparks.Children.Add(spark);
            var begin = TimeSpan.FromSeconds(i * 0.18 / Speed);
            var fly = new DoubleAnimation { From = -size, To = -size - 26 - (random.NextDouble() * 18), Duration = Seconds(0.7), BeginTime = begin };
            var fade = new DoubleAnimation { From = 1, To = 0, Duration = Seconds(0.7), BeginTime = begin };
            Loop(move, "X", fly);
            Loop(spark, "Opacity", fade);
        }
    }

    private void BuildGlass(double width, double h)
    {
        _track.Background = new SolidColorBrush(Alpha(ThemeManager.TrackBrush.Color, ThemeManager.TrackBrush.Color.A / 255.0 * 0.7));
        _track.BorderBrush = new SolidColorBrush(Alpha(White, 0.22));
        _track.BorderThickness = new Thickness(1);
        Base(0.5);
        Gloss(h, 0.2 + (0.35 * GlowAmount));
        _decor.Children.Add(new Border { BorderBrush = new SolidColorBrush(Alpha(White, 0.55)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(_radius) });
        Shimmer(width, h, 180, 3.2, 0.12 + (0.2 * GlowAmount));
    }

    private void BuildFire(double width, double h)
    {
        var start = _stops[0];
        var end = _stops[^1];
        _decor.Children.Insert(0, _base = new Rectangle
        {
            Fill = Gradient(new Point(0, 0.5), new Point(1, 0.5), (Darker(start, 0.35), 0), (start, 0.45), (Mix(start, end, 0.6), 0.8), (Lighter(end, 0.2), 1)),
        });

        // Dos capas de llamas que corren a distinta velocidad y un parpadeo.
        void Flames(double length, double seconds, Color color, double alpha)
        {
            var brush = Repeated(Gradient(new Point(0, 0), new Point(length, 0), (Alpha(color, 0), 0), (Alpha(color, alpha), 0.3), (Alpha(color, 0), 0.6), (Alpha(color, alpha * 0.6), 0.8), (Alpha(color, 0), 1)), width + (length * 2) + 2);
            var rect = Add(new Rectangle { Fill = brush }, width + length + 2, h);
            var offset = new TranslateTransform { X = -length };
            rect.RenderTransform = offset;
            Loop(offset, "X", new DoubleAnimation { From = -length, To = 0, Duration = Seconds(seconds) });
        }

        Flames(44, 0.9, Lighter(end, 0.3), 0.45);
        Flames(27, 0.55, Color.FromArgb(255, 255, 120, 0), 0.4);
        _decor.Children.Add(new Rectangle { Fill = Gradient(new Point(0.5, 0), new Point(0.5, 1), (Alpha(Color.FromArgb(255, 0, 0, 0), 0.25), 0), (Alpha(Color.FromArgb(255, 0, 0, 0), 0), 0.6)) });
        var flicker = new Rectangle { Fill = new SolidColorBrush(Alpha(Color.FromArgb(255, 255, 230, 160), 0.18)), Opacity = 0 };
        _decor.Children.Add(flicker);
        Loop(flicker, "Opacity", new DoubleAnimation { From = 0, To = 1, Duration = Seconds(0.14), AutoReverse = true });
    }

    /// <summary>[#####·····] 45 %: una barra de consola con su cursor parpadeando.</summary>
    private void BuildTerminal(double h)
    {
        _track.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _track.BorderThickness = new Thickness(0);
        _fill.Visibility = Visibility.Collapsed;
        var font = new FontFamily("Cascadia Mono, Consolas");
        var color = new SolidColorBrush(MainColor());
        double size = Math.Clamp(h * 0.72, 11, 15);
        _terminalBody = new TextBlock { FontFamily = font, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        var cursor = new TextBlock { Text = "▌", FontFamily = font, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        _terminal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsHitTestVisible = false, Children = { _terminalBody, cursor } };
        _outer.Children.Add(_terminal);
        Loop(cursor, "Opacity", new DoubleAnimationUsingKeyFrames
        {
            Duration = Seconds(1),
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 },
                new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(0.5 / Speed), Value = 0 },
            },
        });
    }

    /// <summary>Línea fina con un marcador redondo en el punto de avance que late.</summary>
    private void BuildLine(double h)
    {
        double thin = Math.Max(2, Math.Round(h * 0.25));
        _track.VerticalAlignment = VerticalAlignment.Center;
        _track.Height = thin;
        _track.CornerRadius = new CornerRadius(thin / 2);
        _fill.VerticalAlignment = VerticalAlignment.Center;
        _fill.Height = thin;
        Base();
        var color = MainColor();
        var ripple = new Ellipse { Width = h, Height = h, Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(1.5, h * 0.12), Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5) };
        var scale = new ScaleTransform();
        ripple.RenderTransform = scale;
        var dot = new Ellipse { Width = h, Height = h, Fill = new SolidColorBrush(Lighter(color, 0.85)), Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(1.5, h * 0.2) };
        _marker = new Grid { Width = h, Height = h, Children = { ripple, dot } };
        _overlay.Children.Add(_marker);
        Loop(ripple, "Opacity", new DoubleAnimation { From = 0.8, To = 0, Duration = Seconds(1.2) });
        Loop(scale, "ScaleX", new DoubleAnimation { From = 1, To = 2.2, Duration = Seconds(1.2) });
        Loop(scale, "ScaleY", new DoubleAnimation { From = 1, To = 2.2, Duration = Seconds(1.2) });
    }

    /// <summary>Píxeles cuadrados en filas, con algunos destellos.</summary>
    private void BuildPixels(double width, double h)
    {
        double size = Math.Max(3, Math.Floor(h / 2));
        int rows = Math.Max(1, (int)(h / size));
        _cell = size;
        _track.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _track.BorderThickness = new Thickness(0);
        var dim = new Grid();
        _track.Child = dim;
        LinearGradientBrush Row(Color color)
        {
            return Repeated(Gradient(new Point(0, 0), new Point(size, 0), (color, 0), (color, (size - 1) / size), (Alpha(color, 0), (size - 1) / size), (Alpha(color, 0), 1)), width + size);
        }

        for (int row = 0; row < rows; row++)
        {
            double top = row * size;
            dim.Children.Add(new Rectangle { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, top, 0, 0), Height = size - 1, Fill = Row(ThemeManager.TrackBrush.Color) });
            var color = row == 0 ? Lighter(MainColor(), 0.25) : row == rows - 1 && rows > 2 ? Darker(MainColor(), 0.15) : MainColor();
            Add(new Rectangle { Fill = Row(color) }, Math.Max(width, 1), size - 1, 0, top);
        }

        var random = new Random(3);
        for (int i = 0; i < 6 && width > 0; i++)
        {
            var twinkle = Add(new Rectangle { Fill = new SolidColorBrush(White), Opacity = 0 }, size - 1, size - 1, Math.Floor(random.NextDouble() * width / size) * size, random.Next(rows) * size);
            Loop(twinkle, "Opacity", new DoubleAnimation { From = 0, To = 0.85, Duration = Seconds(0.35 + (random.NextDouble() * 0.5)), AutoReverse = true, BeginTime = TimeSpan.FromSeconds(random.NextDouble()) });
        }
    }

    private void BuildMetal(double width, double h)
    {
        var c = MainColor();
        _decor.Children.Insert(0, _base = new Rectangle
        {
            Fill = Gradient(new Point(0.5, 0), new Point(0.5, 1), (Darker(c, 0.3), 0), (Lighter(c, 0.5), 0.3), (c, 0.55), (Darker(c, 0.35), 1)),
        });
        var brushed = Repeated(Gradient(new Point(0, 0), new Point(0, 2), (Alpha(White, 0.08), 0), (Alpha(White, 0.08), 0.5), (Alpha(White, 0), 0.5), (Alpha(White, 0), 1)), h + 2);
        _decor.Children.Add(new Rectangle { Fill = brushed });
        Glare(width, h, 30, 1.4, 3.4, 0.5 + (0.45 * GlowAmount));
    }

    private void BuildOutline(double width, double h)
    {
        var color = MainColor();
        _track.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _track.BorderBrush = new SolidColorBrush(color);
        _track.BorderThickness = new Thickness(Math.Max(1, Math.Min(1.5, h / 5)));
        Stripes(width, h, Alpha(color, 0.95), Alpha(color, 0.3), 2.6);
    }

    private void Bubbles(double width, double h)
    {
        var random = new Random(11);
        int count = (int)Math.Clamp(width / 45, 4, 16);
        for (int i = 0; i < count; i++)
        {
            double size = Math.Max(2, h * (0.25 + (random.NextDouble() * 0.4)));
            var bubble = Add(new Ellipse
            {
                Fill = new SolidColorBrush(Alpha(White, 0.2 + (0.3 * GlowAmount))),
                Stroke = new SolidColorBrush(Alpha(White, 0.5)),
                StrokeThickness = size > 5 ? 1 : 0,
            }, size, size, 0, random.NextDouble() * Math.Max(0, h - size));
            var move = new CompositeTransform { TranslateX = -size };
            bubble.RenderTransform = move;
            double seconds = 2.5 + (random.NextDouble() * 2.5);
            Loop(move, "TranslateX", new DoubleAnimation { From = -size, To = Math.Max(width, 200) + size, Duration = Seconds(seconds), BeginTime = TimeSpan.FromSeconds(random.NextDouble() * seconds / Speed) });
            Loop(move, "TranslateY", new DoubleAnimation { From = -h * 0.12, To = h * 0.12, Duration = Seconds(0.6 + (random.NextDouble() * 0.4)), AutoReverse = true });
        }
    }
}
