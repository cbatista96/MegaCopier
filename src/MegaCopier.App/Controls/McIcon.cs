using System.Numerics;
using MegaCopier.App.Theming;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MegaCopier.App.Controls;

/// <summary>
/// Icono de MegaCopier: se dibuja según el conjunto de iconos elegido (monocromo, de color, en mosaico, suave, en
/// degradado, neón o Windows 10), independiente del tema. Se usa como un FontIcon: Glyph, FontSize y, si se le pone un
/// Foreground propio (por ejemplo, rojo de error), ese color manda sobre el del conjunto.
/// </summary>
public sealed partial class McIcon : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(McIcon), new PropertyMetadata("", (d, _) => ((McIcon)d).Render()));

    /// <summary>Dibujarlo con este conjunto en vez del elegido (para las muestras de la galería).</summary>
    public static readonly DependencyProperty PackOverrideProperty =
        DependencyProperty.Register(nameof(PackOverride), typeof(string), typeof(McIcon), new PropertyMetadata("", (d, _) => ((McIcon)d).Render()));

    /// <summary>En el conjunto monocromo, del color de acento del tema (en los de color, cada icono lleva el suyo).</summary>
    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(bool), typeof(McIcon), new PropertyMetadata(false, (d, _) => ((McIcon)d).Render()));

    private readonly Grid _root = new();
    private readonly Border _tile = new() { Visibility = Visibility.Collapsed };

    /// <summary>Brillo de cristal sobre la mitad de arriba del fondo (Windows XP y 7, macOS, elementary).</summary>
    private readonly Border _gloss = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly TextBlock _glyph = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextLineBounds = TextLineBounds.Tight,
        IsTextScaleFactorEnabled = false,
    };

    private ContainerVisual? _glow;
    private bool _spinning;
    private Visual? _spinVisual;

    /// <summary>Solo gira el símbolo; el fondo propio del conjunto permanece quieto.</summary>
    internal void SetSpinning(bool value)
    {
        if (_spinning == value) return;
        _spinning = value;
        ApplySpin();
    }

    private void ApplySpin()
    {
        if (!_spinning && _spinVisual is null) return;
        var visual = _spinVisual ??= ElementCompositionPreview.GetElementVisual(_glyph);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
        visual.CenterPoint = new Vector3((float)_glyph.ActualWidth / 2, (float)_glyph.ActualHeight / 2, 0);
        if (!_spinning || !IsLoaded || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(1, 360, visual.Compositor.CreateLinearEasingFunction());
        animation.Duration = TimeSpan.FromSeconds(1.8);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("RotationAngleInDegrees", animation);
    }

    /// <summary>
    /// Todos los iconos que existen, para redibujarlos al cambiar de conjunto o de tema. No se usa Loaded/Unloaded para
    /// apuntarse y borrarse: cuando una lista reutiliza un elemento, WinUI puede avisar Loaded antes que Unloaded y el
    /// icono se quedaba sin enterarse de los cambios (con los iconos del conjunto anterior).
    /// </summary>
    private static readonly List<WeakReference<McIcon>> Live = [];

    static McIcon()
    {
        IconPacks.Changed += RenderAll;
        ThemeManager.Changed += _ => RenderAll();
    }

    public McIcon()
    {
        IsTabStop = false;
        FontSize = 16;
        _root.Children.Add(_tile);
        _root.Children.Add(_gloss);
        _root.Children.Add(_glyph);
        Content = _root;
        Live.Add(new WeakReference<McIcon>(this));
        Loaded += (_, _) => { Render(); ApplySpin(); };
        Unloaded += (_, _) => _spinVisual?.StopAnimation("RotationAngleInDegrees");
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Render());
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Render());
        _glyph.SizeChanged += (_, _) => { UpdateGlowSize(); ApplySpin(); };
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string PackOverride
    {
        get => (string)GetValue(PackOverrideProperty);
        set => SetValue(PackOverrideProperty, value);
    }

    public bool Accent
    {
        get => (bool)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private static void RenderAll()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (Live[i].TryGetTarget(out var icon))
            {
                if (icon.IsLoaded)
                {
                    icon.Render(); // los que no están a la vista se redibujan al volver (Loaded)
                }
            }
            else
            {
                Live.RemoveAt(i);
            }
        }
    }

    private void Render()
    {
        var pack = PackOverride.Length > 0 ? IconPacks.Find(PackOverride) : IconPacks.Current;
        _glyph.Text = Glyph;
        _glyph.FontFamily = new FontFamily(IconPacks.FontFor(Glyph, pack));

        // Un Foreground puesto a propósito (estado: error, aviso, éxito) manda sobre el color del conjunto.
        bool ownColor = ReadLocalValue(ForegroundProperty) != DependencyProperty.UnsetValue;
        Color? fixedColor = ownColor && Foreground is SolidColorBrush own ? own.Color : null;
        Color color = AdjustForTheme(fixedColor ?? IconPacks.ColorFor(Glyph, pack));
        double size = FontSize;

        _tile.Visibility = Visibility.Collapsed;
        _gloss.Visibility = Visibility.Collapsed;
        _tile.ClearValue(Border.BorderBrushProperty);
        _tile.BorderThickness = new Thickness(0);
        _glyph.FontSize = size;
        _glyph.ClearValue(TextBlock.ForegroundProperty);
        RemoveGlow();
        Width = double.NaN;
        Height = double.NaN;

        switch (pack.Style)
        {
            case IconStyle.Mono:
                if (fixedColor is { } c)
                {
                    _glyph.Foreground = new SolidColorBrush(c);
                }
                else if (Accent)
                {
                    _glyph.Foreground = ThemeManager.AccentBrush;
                }

                break;

            case IconStyle.Color:
                _glyph.Foreground = new SolidColorBrush(color);
                break;

            case IconStyle.Tile:
            case IconStyle.Soft:
            case IconStyle.Outline:
            {
                // El fondo ocupa lo mismo que ocupaba el icono (un poco más), para no mover nada de sitio.
                double box = Math.Round(size * 1.45);
                Width = box;
                Height = box;
                _tile.Width = box;
                _tile.Height = box;
                _tile.CornerRadius = new CornerRadius(pack.Shape switch
                {
                    IconShape.Circle => box / 2,
                    IconShape.Squircle => box * 0.36,
                    IconShape.Square => 0,
                    _ => box * 0.28,
                });
                _tile.Visibility = Visibility.Visible;
                _glyph.FontSize = Math.Round(size * (pack.Shape == IconShape.Circle ? 0.76 : 0.82));
                if (pack.Style == IconStyle.Tile && pack.TileColor is { } tileColor)
                {
                    // Fondo fijo (Steam, Nothing, Windows 95): el icono lleva el color de su significado.
                    _tile.Background = new SolidColorBrush(tileColor);
                    _glyph.Foreground = new SolidColorBrush(fixedColor ?? IconPacks.ColorFor(Glyph, pack));
                }
                else if (pack.Style == IconStyle.Tile && pack.Flat)
                {
                    _tile.Background = new SolidColorBrush(color);
                    _glyph.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (pack.Style == IconStyle.Tile)
                {
                    // macOS y Windows 7: degradado vertical, de claro arriba a oscuro abajo; el resto, en diagonal.
                    bool vertical = pack.Shape == IconShape.Squircle;
                    var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = vertical ? new(0, 1) : new(1, 1) };
                    gradient.GradientStops.Add(new GradientStop { Color = Lighten(color, 0.18), Offset = 0 });
                    gradient.GradientStops.Add(new GradientStop { Color = Darken(color, 0.18), Offset = 1 });
                    _tile.Background = gradient;
                    _glyph.Foreground = new SolidColorBrush(Colors.White);
                }
                else if (pack.Style == IconStyle.Soft)
                {
                    _tile.Background = new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));
                    _glyph.Foreground = new SolidColorBrush(color);
                }
                else
                {
                    _tile.Background = new SolidColorBrush(Color.FromArgb(18, color.R, color.G, color.B));
                    _tile.BorderBrush = new SolidColorBrush(color);
                    _tile.BorderThickness = new Thickness(Math.Max(1, Math.Round(size / 14)));
                    _glyph.Foreground = new SolidColorBrush(color);
                }

                if (pack.Bevel)
                {
                    // Relieve de Windows 95: borde oscuro abajo a la derecha y claro arriba a la izquierda.
                    double edge = Math.Max(1, Math.Round(size / 12));
                    _tile.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 64, 64, 64));
                    _tile.BorderThickness = new Thickness(0, 0, edge, edge);
                    _gloss.Width = box;
                    _gloss.Height = box;
                    _gloss.Margin = new Thickness(0);
                    _gloss.CornerRadius = new CornerRadius(0);
                    _gloss.Background = null;
                    _gloss.BorderBrush = new SolidColorBrush(Colors.White);
                    _gloss.BorderThickness = new Thickness(edge, edge, 0, 0);
                    _gloss.Visibility = Visibility.Visible;
                }
                else if (pack.Gloss)
                {
                    _gloss.ClearValue(Border.BorderBrushProperty);
                    _gloss.BorderThickness = new Thickness(0);
                    // Reflejo de cristal: blanco que se desvanece en la mitad de arriba, con las mismas esquinas.
                    var shine = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
                    shine.GradientStops.Add(new GradientStop { Color = Color.FromArgb(150, 255, 255, 255), Offset = 0 });
                    shine.GradientStops.Add(new GradientStop { Color = Color.FromArgb(25, 255, 255, 255), Offset = 1 });
                    var radius = _tile.CornerRadius;
                    _gloss.Width = box - 2;
                    _gloss.Height = box * 0.5;
                    _gloss.Margin = new Thickness(0, 1, 0, 0);
                    _gloss.CornerRadius = new CornerRadius(Math.Max(0, radius.TopLeft - 1), Math.Max(0, radius.TopRight - 1), radius.TopLeft * 0.25, radius.TopRight * 0.25);
                    _gloss.Background = shine;
                    _gloss.Visibility = Visibility.Visible;
                }

                break;
            }

            case IconStyle.Gradient:
            {
                var theme = ThemeManager.Current;
                var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
                gradient.GradientStops.Add(new GradientStop { Color = fixedColor ?? ThemeManager.Parse(theme.TotalStart), Offset = 0 });
                gradient.GradientStops.Add(new GradientStop { Color = fixedColor ?? ThemeManager.Parse(theme.TotalEnd), Offset = 1 });
                _glyph.Foreground = gradient;
                break;
            }

            case IconStyle.Neon:
                _glyph.Foreground = new SolidColorBrush(Lighten(color, 0.25));
                if (IsLoaded)
                {
                    AddGlow(color); // al cargarse se vuelve a dibujar: el halo necesita el icono ya en pantalla
                }

                break;
        }

        _morph = IconPacks.FontFor(Glyph, pack) == IconPacks.Mdl2Font ? Mdl2Morph : FluentMorph;
        var ink = InkScale(Glyph, IconPacks.FontFor(Glyph, pack));
        _glyph.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _glyph.RenderTransform = ink.Scale == 1 ? null : new CompositeTransform { ScaleX = ink.Scale, ScaleY = ink.Scale, TranslateY = -ink.Up * size };
        UpdateShape();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Pausa ⇄ reproducir: la figura se transforma (las dos barras se juntan en el triángulo y al revés)

    /// <summary>
    /// Pausa y reproducir de una fuente de iconos, medidos sobre su letra por el centro de la línea, en proporción a la
    /// caja del icono. Cada figura son dos piezas de cuatro esquinas (arriba izquierda, arriba derecha, abajo derecha,
    /// abajo izquierda); el triángulo sale exacto de las dos, para que al juntarse no quede ninguna línea por medio. Los
    /// radios son lo que se recorta de cada lado para redondear cada esquina.
    /// </summary>
    private sealed record MorphShape(double[][] Pause, double[][] Play, double[][] PauseRadius, double[][] PlayRadius, bool Rounded, double Scale);

    /// <summary>Windows 11 (Segoe Fluent Icons): barras y triángulo con las esquinas redondeadas y la punta muy suave.</summary>
    private static readonly MorphShape FluentMorph = new(
        Pause:
        [
            [0.0975, 0.0975, 0.3425, 0.0975, 0.3425, 0.9025, 0.0975, 0.9025],
            [0.6575, 0.0975, 0.9025, 0.0975, 0.9025, 0.9025, 0.6575, 0.9025],
        ],
        Play:
        [
            [0.155, 0.05, 0.53, 0.2565, 0.53, 0.7435, 0.155, 0.95],
            [0.53, 0.2565, 0.973, 0.5, 0.973, 0.5, 0.53, 0.7435],
        ],
        PauseRadius: [[0.07, 0.07, 0.07, 0.07], [0.07, 0.07, 0.07, 0.07]],
        PlayRadius: [[0.1, 0, 0, 0.1], [0, 0.17, 0.17, 0]],
        Rounded: true,
        Scale: 1);

    /// <summary>
    /// Windows 10 (Segoe MDL2 Assets): la pausa son dos líneas sueltas y el triángulo tiene las esquinas vivas. Algo más
    /// grande que la letra, para que se vea del tamaño de los demás iconos de la barra.
    /// </summary>
    private static readonly MorphShape Mdl2Morph = new(
        Pause:
        [
            [0.345, 0.125, 0.345, 0.125, 0.345, 0.875, 0.345, 0.875],
            [0.655, 0.125, 0.655, 0.125, 0.655, 0.875, 0.655, 0.875],
        ],
        Play:
        [
            [0.28, 0.121, 0.53, 0.296, 0.53, 0.704, 0.28, 0.879],
            [0.53, 0.296, 0.821, 0.5, 0.821, 0.5, 0.53, 0.704],
        ],
        PauseRadius: [[0, 0, 0, 0], [0, 0, 0, 0]],
        PlayRadius: [[0, 0, 0, 0], [0, 0, 0, 0]],
        Rounded: false,
        Scale: 1.1);

    /// <summary>
    /// Segoe MDL2 Assets dibuja algunos iconos redondos más pequeños que el resto: se agrandan para que en una fila todos
    /// se vean del mismo tamaño (el de la papelera) y a la misma altura. Medido sobre la tinta de cada letra: cuánto
    /// se agranda y cuánto sube (en proporción al tamaño).
    /// </summary>
    private static (double Scale, double Up) InkScale(string glyph, string font) =>
        font != IconPacks.Mdl2Font || glyph.Length == 0 ? (1, 0) : glyph[0] switch
        {
            (char)0xE121 => (1.15, 0.075), // reloj (línea de tiempo): su dibujo va más abajo que el de los demás
            (char)0xE946 => (1.07, 0), // información
            _ => (1, 0),
        };

    private Microsoft.UI.Xaml.Shapes.Path? _shape;

    /// <summary>Los lados por donde se juntan las dos piezas: se desvanecen al formar el triángulo.</summary>
    private Microsoft.UI.Xaml.Shapes.Path? _seam;

    /// <summary>La figura sigue la fuente del conjunto: la de Windows 11 o la de Windows 10.</summary>
    private MorphShape _morph = FluentMorph;
    private double _shapeAt, _shapeFrom, _shapeTo;
    private long _shapeStart;

    /// <summary>
    /// Dibuja el icono de pausa o el de reproducir con una figura (en vez de la letra de la fuente) para poder
    /// transformar una en otra: las barras de la pausa se deslizan y se inclinan hasta formar el triángulo. Es la misma
    /// forma de la letra del conjunto elegido, solo contorno, con su color y su fondo.
    /// </summary>
    public void MorphTo(bool play, bool animate)
    {
        Glyph = play ? "" : ""; // para el color y el fondo de cada conjunto
        if (_shape is null)
        {
            _shape = new Microsoft.UI.Xaml.Shapes.Path
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            _seam = new Microsoft.UI.Xaml.Shapes.Path
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            _root.Children.Add(_shape);
            _root.Children.Add(_seam);
            animate = false;
            UpdateShape();
        }

        _shapeFrom = _shapeAt;
        _shapeTo = play ? 1 : 0;
        CompositionTarget.Rendering -= AnimateShape;
        if (!animate || Theming.BarAppearance.ReduceMotion)
        {
            _shapeAt = _shapeTo;
            DrawShape();
            return;
        }

        _shapeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += AnimateShape;
    }

    private void AnimateShape(object? sender, object e)
    {
        double progress = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(_shapeStart).TotalMilliseconds / 260);
        double eased = progress < 0.5 ? 4 * progress * progress * progress : 1 - (Math.Pow(-2 * progress + 2, 3) / 2);
        _shapeAt = _shapeFrom + ((_shapeTo - _shapeFrom) * eased);
        DrawShape();
        if (progress >= 1)
        {
            CompositionTarget.Rendering -= AnimateShape;
        }
    }

    /// <summary>La figura toma el color y el grosor de línea de la letra del icono, que se queda oculta debajo.</summary>
    private void UpdateShape()
    {
        if (_shape is null || _seam is null)
        {
            return;
        }

        _glyph.Opacity = 0;
        var cap = _morph.Rounded ? PenLineCap.Round : PenLineCap.Flat;
        foreach (var path in new[] { _shape, _seam })
        {
            path.Fill = null;
            path.Stroke = _glyph.Foreground;
            path.StrokeThickness = Math.Max(1, _glyph.FontSize * 0.065); // el grosor de línea de los iconos de Windows
            path.StrokeLineJoin = _morph.Rounded ? PenLineJoin.Round : PenLineJoin.Miter;
            path.StrokeStartLineCap = cap;
            path.StrokeEndLineCap = cap;
        }

        DrawShape();
    }

    private void DrawShape()
    {
        if (_shape is null || _seam is null)
        {
            return;
        }

        // Un poco más de sitio que el icono para lo que sobresale al agrandarla, sin cambiar lo que ocupa en la fila.
        double box = _glyph.FontSize;
        double room = box * 1.4;
        foreach (var path in new[] { _shape, _seam })
        {
            path.Width = room;
            path.Height = room;
            path.Margin = new Thickness(-(room - box) / 2);
        }

        double scale = box * _morph.Scale;
        var geometry = new PathGeometry();
        var seams = new PathGeometry();
        for (int piece = 0; piece < 2; piece++)
        {
            var corners = new Windows.Foundation.Point[4];
            var radii = new double[4];
            for (int i = 0; i < 4; i++)
            {
                double Mix(double[] from, double[] to, int at) => from[at] + ((to[at] - from[at]) * _shapeAt);
                corners[i] = new(
                    (room / 2) + ((Mix(_morph.Pause[piece], _morph.Play[piece], i * 2) - 0.5) * scale),
                    (room / 2) + ((Mix(_morph.Pause[piece], _morph.Play[piece], (i * 2) + 1) - 0.5) * scale));
                radii[i] = Mix(_morph.PauseRadius[piece], _morph.PlayRadius[piece], i) * scale;
            }

            // El lado por el que se junta con la otra pieza (el derecho de la primera, el izquierdo de la segunda) va
            // aparte, con sus dos esquinas: «first» es la esquina en la que empieza el contorno de fuera.
            int first = piece == 0 ? 2 : 0;
            int a = first, b = (first + 1) % 4, c = (first + 2) % 4, d = (first + 3) % 4;
            var outer = new Figure(Toward(corners[a], corners[b], radii[a], corners[d]));
            if (Distance(corners[b], corners[c]) < radii[b] + radii[c])
            {
                // La punta del triángulo: las dos esquinas se juntan en una sola, redondeada.
                var tip = new Windows.Foundation.Point((corners[b].X + corners[c].X) / 2, (corners[b].Y + corners[c].Y) / 2);
                outer.Corner(corners[a], tip, corners[d], Math.Max(radii[b], radii[c]));
            }
            else
            {
                outer.Corner(corners[a], corners[b], corners[c], radii[b]);
                outer.Corner(corners[b], corners[c], corners[d], radii[c]);
            }

            var join = Toward(corners[d], corners[c], radii[d], corners[a]);
            outer.LineTo(join);
            geometry.Figures.Add(outer.Done);

            var seam = new Figure(join);
            seam.Corner(corners[c], corners[d], corners[a], radii[d]);
            seam.Corner(corners[d], corners[a], corners[b], radii[a]);
            seams.Figures.Add(seam.Done);
        }

        _shape.Data = geometry;
        _seam.Data = seams;
        _seam.Opacity = 1 - _shapeAt;
    }

    private static double Distance(Windows.Foundation.Point p, Windows.Foundation.Point q) => Math.Sqrt(((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)));

    /// <summary>
    /// Donde empieza el redondeo de la esquina <paramref name="corner"/> hacia <paramref name="next"/>: a
    /// <paramref name="radius"/> de ella, sin pasar de la mitad de ninguno de sus dos lados.
    /// </summary>
    private static Windows.Foundation.Point Toward(Windows.Foundation.Point corner, Windows.Foundation.Point next, double radius, Windows.Foundation.Point previous)
    {
        double length = Distance(corner, next);
        double cut = Math.Min(radius, Math.Min(length, Distance(corner, previous)) / 2);
        return length < 1e-6 ? corner : new(corner.X + ((next.X - corner.X) * cut / length), corner.Y + ((next.Y - corner.Y) * cut / length));
    }

    /// <summary>Un contorno abierto hecho de lados rectos y esquinas redondeadas (curva con la esquina como control).</summary>
    private sealed class Figure(Windows.Foundation.Point start)
    {
        public PathFigure Done { get; } = new() { StartPoint = start, IsClosed = false, IsFilled = false };

        public Windows.Foundation.Point End { get; private set; } = start;

        public void LineTo(Windows.Foundation.Point point)
        {
            if (Distance(End, point) > 1e-6)
            {
                Done.Segments.Add(new LineSegment { Point = point });
                End = point;
            }
        }

        /// <summary>Lado hasta la esquina y su redondeo; sin radio, la esquina viva.</summary>
        public void Corner(Windows.Foundation.Point previous, Windows.Foundation.Point corner, Windows.Foundation.Point next, double radius)
        {
            var into = Toward(corner, previous, radius, next);
            var outOf = Toward(corner, next, radius, previous);
            LineTo(into);
            if (Distance(into, outOf) > 1e-6)
            {
                Done.Segments.Add(new QuadraticBezierSegment { Point1 = corner, Point2 = outOf });
                End = outOf;
            }
            else
            {
                LineTo(corner);
            }
        }
    }

    /// <summary>Halo de luz del color del icono, con la forma exacta del icono (sombra de composición).</summary>
    private void AddGlow(Color color)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_glyph);
        var compositor = visual.Compositor;
        // Dos capas, como un tubo de neón: una ceñida e intensa y otra amplia y suave.
        var mask = _glyph.GetAlphaMask();
        _glow = compositor.CreateContainerVisual();
        foreach (double blur in new[] { 0.25, 0.9 })
        {
            var shadow = compositor.CreateDropShadow();
            shadow.Color = color;
            shadow.BlurRadius = (float)(FontSize * blur);
            shadow.Offset = Vector3.Zero;
            shadow.Mask = mask;
            var layer = compositor.CreateSpriteVisual();
            layer.Shadow = shadow;
            _glow.Children.InsertAtTop(layer);
        }
        ElementCompositionPreview.SetElementChildVisual(_tile, _glow);
        _tile.Visibility = Visibility.Visible;
        _tile.Background = null;
        _tile.Width = double.NaN;
        _tile.Height = double.NaN;
        UpdateGlowSize();
    }

    private void UpdateGlowSize()
    {
        if (_glow is not null && _glyph.IsLoaded && _tile.IsLoaded)
        {
            var size = new Vector2((float)_glyph.ActualWidth, (float)_glyph.ActualHeight);
            _glow.Size = size;
            foreach (var layer in _glow.Children)
            {
                layer.Size = size;
            }

            var offset = _glyph.TransformToVisual(_tile).TransformPoint(new Windows.Foundation.Point(0, 0));
            _glow.Offset = new Vector3((float)offset.X, (float)offset.Y, 0);
        }
    }

    private void RemoveGlow()
    {
        if (_glow is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(_tile, null);
            _glow.Dispose();
            _glow = null;
        }
    }

    /// <summary>En temas oscuros los colores se aclaran un poco para que se lean bien.</summary>
    private static Color AdjustForTheme(Color color) => ThemeManager.Current.Dark ? Lighten(color, 0.12) : color;

    private static Color Lighten(Color c, double amount) => Color.FromArgb(
        c.A, (byte)(c.R + ((255 - c.R) * amount)), (byte)(c.G + ((255 - c.G) * amount)), (byte)(c.B + ((255 - c.B) * amount)));

    private static Color Darken(Color c, double amount) => Color.FromArgb(
        c.A, (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
}
