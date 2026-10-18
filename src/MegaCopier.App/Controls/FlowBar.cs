using System.Numerics;
using MegaCopier.App.Theming;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MegaCopier.App.Controls;

/// <summary>
/// Barra de progreso animada con estilos (Fluent, Aero, ola, rayas, neón, bloques, LED...). El avance se desliza (suave,
/// elástico o a saltos), los efectos se mueven mientras copia, en pausa salen rayas ámbar, con error se pone roja y da
/// una sacudida, y al terminar da un destello. El estilo, los colores, el grosor y el redondeo salen de
/// <see cref="BarAppearance"/> según su <see cref="Role"/>, o de <see cref="Appearance"/> (vistas previas).
/// </summary>
public sealed partial class FlowBar : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(FlowBar), new PropertyMetadata(0.0, (d, _) => ((FlowBar)d).OnValueChanged()));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(FlowBar), new PropertyMetadata(null, (d, _) => ((FlowBar)d).Rebuild()));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateState()));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateState()));

    public static readonly DependencyProperty IsErrorProperty =
        DependencyProperty.Register(nameof(IsError), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, e) => ((FlowBar)d).OnErrorChanged((bool)e.NewValue)));

    public static readonly DependencyProperty IsWarningProperty =
        DependencyProperty.Register(nameof(IsWarning), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).Rebuild()));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateIndeterminate()));

    public static readonly DependencyProperty AppearanceProperty =
        DependencyProperty.Register(nameof(Appearance), typeof(BarSettings), typeof(FlowBar), new PropertyMetadata(null, (d, _) => ((FlowBar)d).ApplyLayout()));

    private readonly Grid _outer = new();
    private readonly Canvas _glowHost = new() { IsHitTestVisible = false };
    private readonly Grid _root = new();
    private readonly Border _track = new();
    private readonly Border _ghost = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Visibility = Visibility.Collapsed };
    private readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Grid _decor = new() { IsHitTestVisible = false };
    private readonly Rectangle _pauseStripes = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Rectangle _flash = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _text = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TranslateTransform _indeterminateOffset = new();
    private readonly TranslateTransform _shake = new();
    private readonly List<Storyboard> _loops = [];
    private readonly CompositionRoundedRectangleGeometry _rootClip;
    private readonly CompositionRoundedRectangleGeometry _fillClip;
    private readonly SpriteVisual _glow;
    private readonly DropShadow _glowShadow;
    private Storyboard? _widthAnimation;
    private Storyboard? _ghostAnimation;
    private Storyboard? _indeterminateAnimation;
    private Storyboard? _oneShot;
    private BarSettings _settings = new();
    private double _shownValue;
    private double _height = 8;
    private double _radius = 4;
    private double _builtWidth = -1;
    private bool _loopsRunning;
    private bool _rebuildPending;
    private bool _glowPulsing;

    public FlowBar()
    {
        IsTabStop = false;

        var compositor = ElementCompositionPreview.GetElementVisual(_root).Compositor;
        _rootClip = compositor.CreateRoundedRectangleGeometry();
        ElementCompositionPreview.GetElementVisual(_root).Clip = compositor.CreateGeometricClip(_rootClip);
        _fillClip = compositor.CreateRoundedRectangleGeometry();
        ElementCompositionPreview.GetElementVisual(_fill).Clip = compositor.CreateGeometricClip(_fillClip);

        // Halo (neón, aero): una sombra de color bajo la parte llena, fuera del recorte de la barra.
        _glow = compositor.CreateSpriteVisual();
        _glow.Brush = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        _glowShadow = compositor.CreateDropShadow();
        _glowShadow.Opacity = 0;
        _glow.Shadow = _glowShadow;
        ElementCompositionPreview.SetElementChildVisual(_glowHost, _glow);

        _root.SizeChanged += (_, e) =>
        {
            _rootClip.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            if (Math.Abs(e.NewSize.Width - _builtWidth) > 0.5 && !_rebuildPending)
            {
                // Al cambiar el tamaño de la ventana llegan muchos cambios seguidos: se redibuja una vez.
                _rebuildPending = true;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _rebuildPending = false;
                    Rebuild();
                });
            }

            SetFillWidth(_shownValue, animate: false);
            if (IsIndeterminate)
            {
                UpdateIndeterminate(); // el recorrido depende del ancho real
            }
        };
        _fill.SizeChanged += (_, e) =>
        {
            _fillClip.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            _glow.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            FillWidthChanged(e.NewSize.Width);
        };

        _fill.RenderTransform = _indeterminateOffset;
        _fill.Children.Add(_decor);
        _fill.Children.Add(_pauseStripes);
        _fill.Children.Add(_flash);
        _root.Children.Add(_track);
        _root.Children.Add(_ghost);
        _root.Children.Add(_fill);
        _outer.Children.Add(_glowHost);
        _outer.Children.Add(_root);
        _outer.Children.Add(_overlay);
        _outer.Children.Add(_text);
        _outer.RenderTransform = _shake;
        Content = _outer;

        ThemeManager.Changed += OnThemeChanged;
        BarAppearance.Changed += ApplyLayout;
        Loaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            ThemeManager.Changed += OnThemeChanged;
            BarAppearance.Changed -= ApplyLayout;
            BarAppearance.Changed += ApplyLayout;
            ApplyLayout();
        };
        Unloaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            BarAppearance.Changed -= ApplyLayout;
            StopLoops();
        };
        ApplyLayout();
    }

    /// <summary>Avance de 0 a 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Copiando ahora mismo: los efectos se mueven.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public bool IsError
    {
        get => (bool)GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    /// <summary>Terminó con avisos: color de aviso (ámbar).</summary>
    public bool IsWarning
    {
        get => (bool)GetValue(IsWarningProperty);
        set => SetValue(IsWarningProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>Qué barra es: total, archivo o mini-barra de la lista (cada una con su aspecto).</summary>
    public BarRole Role { get; set; } = BarRole.Total;

    /// <summary>Barra sencilla (uso de disco...): siempre el estilo Fluent con los colores del tema.</summary>
    public bool Plain { get; set; }

    /// <summary>Aspecto propio en lugar del de <see cref="Role"/> (vistas previas de los ajustes).</summary>
    public BarSettings? Appearance
    {
        get => (BarSettings?)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>Alto fijo en lugar del que marca el tema (para las mini-barras de la lista de trabajos).</summary>
    public double FixedHeight { get; set; } = double.NaN;

    private BarStyle BarKind => _settings.Style;

    private double Speed => Math.Clamp(_settings.Speed, 0.25, 3);

    private double GlowAmount => Math.Clamp(_settings.Glow, 0, 1);

    /// <summary>Los efectos se mueven: copiando, sin pausa, sin error y con animaciones permitidas.</summary>
    private bool Running => IsActive && !IsPaused && !IsError && !IsWarning && !BarAppearance.ReduceMotion;

    private void OnThemeChanged(ThemeDefinition theme) => ApplyLayout();

    /// <summary>Alto, redondeo, borde y texto según el tema y el aspecto elegido; después se redibuja.</summary>
    private void ApplyLayout()
    {
        _settings = Plain ? new BarSettings() : (Appearance ?? BarAppearance.For(Role)).Clone();
        var theme = ThemeManager.Current;
        double height = !double.IsNaN(FixedHeight) ? FixedHeight : theme.BarHeight;
        if (_settings.Thickness > 0 && (double.IsNaN(FixedHeight) || Role == BarRole.Mini))
        {
            height = _settings.Thickness;
        }

        if (double.IsNaN(FixedHeight) && (_settings.Text == BarText.Inside || BarKind == BarStyle.Terminal))
        {
            height = Math.Max(height, 18);
        }

        _height = height;
        Height = height;
        _radius = Math.Min(_settings.Radius >= 0 ? _settings.Radius : theme.CornerRadius, height / 2);
        _rootClip.CornerRadius = new Vector2((float)_radius, (float)_radius);
        _fillClip.CornerRadius = new Vector2((float)_radius, (float)_radius);
        _track.CornerRadius = new CornerRadius(_radius);
        _ghost.CornerRadius = new CornerRadius(_radius);
        _builtWidth = -1;
        Rebuild();
    }

    private void OnValueChanged()
    {
        if (IsIndeterminate)
        {
            return;
        }

        double value = Math.Clamp(Value, 0, 1);
        bool finished = value >= 0.999 && _shownValue < 0.999 && _settings.StateEffects && Role != BarRole.File && !IsError && IsActive;
        SetFillWidth(value, animate: true);
        UpdateValueDependent();
        if (finished)
        {
            Flash();
        }
    }

    private void UpdateState()
    {
        _fill.Opacity = IsPaused ? (_settings.StateEffects ? 0.75 : 0.5) : 1;
        _pauseStripes.Opacity = IsPaused && _settings.StateEffects && !IsIndeterminate ? 0.55 : 0;
        if (Running && !_loopsRunning)
        {
            StartLoops();
        }
        else if (!Running && _loopsRunning)
        {
            StopLoops();
        }

        // Neón: el halo respira mientras copia; en reposo se queda quieto.
        float level = Running || BarKind is BarStyle.Neon ? (float)GlowLevel() : 0;
        if (BarKind == BarStyle.Neon && Running)
        {
            if (!_glowPulsing)
            {
                var pulse = _glow.Compositor.CreateScalarKeyFrameAnimation();
                pulse.InsertKeyFrame(0, level * 0.55f);
                pulse.InsertKeyFrame(0.5f, level);
                pulse.InsertKeyFrame(1, level * 0.55f);
                pulse.Duration = TimeSpan.FromSeconds(1.4 / Speed);
                pulse.IterationBehavior = AnimationIterationBehavior.Forever;
                _glowShadow.StartAnimation("Opacity", pulse);
                _glowPulsing = true;
            }
        }
        else
        {
            if (_glowPulsing)
            {
                _glowShadow.StopAnimation("Opacity");
                _glowPulsing = false;
            }

            _glowShadow.Opacity = level;
        }
    }

    private void OnErrorChanged(bool error)
    {
        Rebuild();
        if (error && _settings.StateEffects && !BarAppearance.ReduceMotion)
        {
            Shake();
        }
    }

    private void UpdateIndeterminate()
    {
        _indeterminateAnimation?.Stop();
        _indeterminateAnimation = null;
        if (IsIndeterminate && ActualWidth > 0)
        {
            SetFillWidth(0.3, animate: false);
            var animation = new DoubleAnimation
            {
                From = -0.3 * ActualWidth,
                To = ActualWidth,
                Duration = Seconds(1.3),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };
            _indeterminateAnimation = Animate(_indeterminateOffset, "X", animation);
            _indeterminateAnimation.Begin();
        }
        else if (!IsIndeterminate)
        {
            _indeterminateOffset.X = 0;
            SetFillWidth(Math.Clamp(Value, 0, 1), animate: false);
        }

        UpdateValueDependent();
        UpdateState();
    }

    private void SetFillWidth(double value, bool animate)
    {
        // Hacia atrás (empieza otro archivo) no se anima: la barra vuelve al principio de golpe.
        animate &= value >= _shownValue - 0.0001;
        _shownValue = value;
        double width = _root.ActualWidth > 0 ? _root.ActualWidth : ActualWidth;
        double target = Math.Max(0, width * Snap(value));
        if (BarKind == BarStyle.Dual)
        {
            AnimateGhost(Math.Min(width, target + (IsActive && value < 1 ? width * 0.03 : 0)), animate);
        }

        if (!animate || width <= 0 || double.IsNaN(_fill.Width) || _settings.Motion == BarMotion.Steps)
        {
            _widthAnimation?.Stop();
            _fill.Width = target;
            return;
        }

        // Al parar una animación WinUI vuelve al valor base: se fija antes el ancho actual para no dar saltos.
        double current = _fill.ActualWidth;
        _widthAnimation?.Stop();
        _fill.Width = current;

        EasingFunctionBase easing = _settings.Motion == BarMotion.Elastic
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(BarKind == BarStyle.Dual ? 1100 : _settings.Motion == BarMotion.Elastic ? 650 : 320)),
            EasingFunction = easing,
            EnableDependentAnimation = true,
        };
        _widthAnimation = Animate(_fill, "Width", animation);
        _widthAnimation.Begin();
    }

    /// <summary>Doble capa: la capa clara llega enseguida y la sólida la sigue despacio.</summary>
    private void AnimateGhost(double target, bool animate)
    {
        double current = _ghost.ActualWidth;
        _ghostAnimation?.Stop();
        _ghost.Width = animate ? current : target;
        if (!animate)
        {
            return;
        }

        _ghostAnimation = Animate(_ghost, "Width", new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        });
        _ghostAnimation.Begin();
    }

    /// <summary>A saltos: múltiplos del 5 %; bloques, LED, píxeles y segmentos: piezas enteras.</summary>
    private double Snap(double value)
    {
        if (_settings.Motion == BarMotion.Steps)
        {
            value = Math.Floor(value * 20) / 20;
        }

        double width = _root.ActualWidth;
        if (width > 0 && _cell > 0 && BarKind is BarStyle.Blocks or BarStyle.Led or BarStyle.Pixels or BarStyle.Segments && value < 1)
        {
            value = Math.Ceiling(value * width / _cell) * _cell / width;
        }

        return Math.Clamp(value, 0, 1);
    }

    /// <summary>Destello del color de «correcto» al terminar.</summary>
    private void Flash()
    {
        if (BarAppearance.ReduceMotion)
        {
            return;
        }

        _flash.Fill = ThemeManager.SuccessBrush;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(900)) };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(180), Value = 0.85, EasingFunction = new CubicEase() });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(900), Value = 0, EasingFunction = new CubicEase() });
        _oneShot?.Stop();
        _oneShot = Animate(_flash, "Opacity", animation);
        _oneShot.Begin();
    }

    /// <summary>Sacudida corta cuando la copia falla.</summary>
    private void Shake()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(420)) };
        double[] steps = [0, -5, 5, -4, 4, -2, 2, 0];
        for (int i = 0; i < steps.Length; i++)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(60 * i), Value = steps[i] });
        }

        _oneShot?.Stop();
        _oneShot = Animate(_shake, "X", animation);
        _oneShot.Begin();
    }

    private void StartLoops()
    {
        foreach (var loop in _loops)
        {
            loop.Begin();
        }

        _loopsRunning = true;
    }

    private void StopLoops()
    {
        foreach (var loop in _loops)
        {
            loop.Stop();
        }

        _loopsRunning = false;
    }

    /// <summary>Repite una animación sin fin mientras la barra está copiando.</summary>
    private void Loop(DependencyObject target, string property, Timeline animation)
    {
        animation.RepeatBehavior = RepeatBehavior.Forever;
        _loops.Add(Animate(target, property, animation));
    }

    private static Storyboard Animate(DependencyObject target, string property, Timeline animation)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }

    /// <summary>Duración ajustada a la velocidad de los efectos elegida.</summary>
    private Duration Seconds(double seconds) => new(TimeSpan.FromSeconds(seconds / Speed));
}
