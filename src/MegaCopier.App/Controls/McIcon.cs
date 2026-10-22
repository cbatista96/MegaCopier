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

        UpdateShape();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Pausa ⇄ reproducir: la figura se transforma (las dos barras se juntan en el triángulo y al revés)

    /// <summary>Las dos piezas de cada figura, en proporción a la caja del icono (arriba izquierda, arriba derecha, abajo derecha, abajo izquierda).</summary>
    private static readonly double[][] PausePieces =
    [
        [0.20, 0.14, 0.40, 0.14, 0.40, 0.86, 0.20, 0.86],
        [0.60, 0.14, 0.80, 0.14, 0.80, 0.86, 0.60, 0.86],
    ];

    private static readonly double[][] PlayPieces =
    [
        [0.25, 0.10, 0.59, 0.30, 0.59, 0.70, 0.25, 0.90],
        [0.585, 0.29, 0.92, 0.50, 0.92, 0.50, 0.585, 0.71],
    ];

    private Microsoft.UI.Xaml.Shapes.Path? _shape;
    private double _shapeAt, _shapeFrom, _shapeTo;
    private long _shapeStart;

    /// <summary>
    /// Dibuja el icono de pausa o el de reproducir con una figura (en vez de la letra de la fuente) para poder
    /// transformar una en otra: las barras de la pausa se deslizan y se inclinan hasta formar el triángulo. Conserva el
    /// color y el fondo del conjunto de iconos.
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
                IsHitTestVisible = false,
            };
            _root.Children.Add(_shape);
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

    /// <summary>La figura toma el color y el tamaño de la letra del icono, que se queda oculta debajo.</summary>
    private void UpdateShape()
    {
        if (_shape is null)
        {
            return;
        }

        _glyph.Opacity = 0;
        _shape.Fill = _glyph.Foreground;
        _shape.Stroke = _glyph.Foreground;
        _shape.StrokeThickness = Math.Max(0.6, _glyph.FontSize / 18); // redondea las esquinas y tapa la junta
        DrawShape();
    }

    private void DrawShape()
    {
        if (_shape is null)
        {
            return;
        }

        double box = _glyph.FontSize;
        _shape.Width = box;
        _shape.Height = box;
        var geometry = new PathGeometry();
        for (int piece = 0; piece < 2; piece++)
        {
            var from = PausePieces[piece];
            var to = PlayPieces[piece];
            Windows.Foundation.Point At(int i) => new(
                (from[i * 2] + ((to[i * 2] - from[i * 2]) * _shapeAt)) * box,
                (from[(i * 2) + 1] + ((to[(i * 2) + 1] - from[(i * 2) + 1]) * _shapeAt)) * box);

            var figure = new PathFigure { StartPoint = At(0), IsClosed = true, IsFilled = true };
            for (int i = 1; i < 4; i++)
            {
                figure.Segments.Add(new LineSegment { Point = At(i) });
            }

            geometry.Figures.Add(figure);
        }

        _shape.Data = geometry;
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
