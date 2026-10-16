using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MegaCopier.App.Controls;

/// <summary>
/// Barra de progreso animada: el avance se desliza suavemente, un brillo recorre la parte llena mientras está activa,
/// se atenúa en pausa y cambia al color de error si algo falla. Colores, alto y redondeo los pone el tema.
/// </summary>
public sealed partial class FlowBar : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(FlowBar), new PropertyMetadata(0.0, (d, _) => ((FlowBar)d).AnimateTo()));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(FlowBar), new PropertyMetadata(null, (d, _) => ((FlowBar)d).UpdateLook()));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateShimmer()));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateLook()));

    public static readonly DependencyProperty IsErrorProperty =
        DependencyProperty.Register(nameof(IsError), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateLook()));

    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(FlowBar), new PropertyMetadata(false, (d, _) => ((FlowBar)d).UpdateIndeterminate()));

    private readonly Border _track = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Rectangle _shimmer = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
    private readonly TranslateTransform _shimmerOffset = new();
    private readonly TranslateTransform _indeterminateOffset = new();
    private readonly RectangleGeometry _fillClip = new();
    private Storyboard? _widthAnimation;
    private Storyboard? _shimmerAnimation;
    private Storyboard? _indeterminateAnimation;
    private double _shownValue;

    public FlowBar()
    {
        IsTabStop = false;
        var shimmerBrush = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
        shimmerBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 0 });
        shimmerBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(90, 255, 255, 255), Offset = 0.5 });
        shimmerBrush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 1 });
        _shimmer.Fill = shimmerBrush;
        _shimmer.RenderTransform = _shimmerOffset;
        _shimmer.Opacity = 0;

        _fill.Child = _shimmer;
        _fill.Clip = _fillClip;
        _fill.RenderTransform = _indeterminateOffset;
        _fill.SizeChanged += (_, e) => _fillClip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);

        var root = new Grid();
        root.Children.Add(_track);
        root.Children.Add(_fill);
        Content = root;

        _track.SetValue(Border.BackgroundProperty, Theming.ThemeManager.TrackBrush);
        ApplyTheme(Theming.ThemeManager.Current);
        Theming.ThemeManager.Changed += ApplyTheme;
        Unloaded += (_, _) => Theming.ThemeManager.Changed -= ApplyTheme;
        Loaded += (_, _) =>
        {
            Theming.ThemeManager.Changed -= ApplyTheme;
            Theming.ThemeManager.Changed += ApplyTheme;
            ApplyTheme(Theming.ThemeManager.Current);
        };
        SizeChanged += (_, _) => SetFillWidth(_shownValue, animate: false);
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

    /// <summary>Copiando ahora mismo: muestra el brillo en movimiento.</summary>
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

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>Alto fijo en lugar del que marca el tema (para las mini-barras de la lista de trabajos).</summary>
    public double FixedHeight { get; set; } = double.NaN;

    private void ApplyTheme(Theming.ThemeDefinition theme)
    {
        double height = double.IsNaN(FixedHeight) ? theme.BarHeight : FixedHeight;
        Height = height;
        var radius = new CornerRadius(Math.Min(theme.CornerRadius, height / 2));
        _track.CornerRadius = radius;
        _fill.CornerRadius = radius;
        _shimmer.Height = height;
        UpdateLook();
    }

    private void UpdateLook()
    {
        if (IsError)
        {
            _fill.SetValue(Border.BackgroundProperty, Theming.ThemeManager.ErrorBrush);
        }
        else if (Fill is { } fill)
        {
            _fill.Background = fill;
        }

        _fill.Opacity = IsPaused ? 0.5 : 1;
        UpdateShimmer();
    }

    private void UpdateShimmer()
    {
        bool run = IsActive && !IsPaused && !IsError && !IsIndeterminate;
        if (run && _shimmerAnimation is null)
        {
            var animation = new DoubleAnimation
            {
                From = -140,
                To = Math.Max(ActualWidth, 400),
                Duration = new Duration(TimeSpan.FromMilliseconds(1600)),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, _shimmerOffset);
            Storyboard.SetTargetProperty(animation, "X");
            _shimmerAnimation = new Storyboard();
            _shimmerAnimation.Children.Add(animation);
            _shimmer.Opacity = 1;
            _shimmerAnimation.Begin();
        }
        else if (!run && _shimmerAnimation is not null)
        {
            _shimmerAnimation.Stop();
            _shimmerAnimation = null;
            _shimmer.Opacity = 0;
        }
    }

    private void UpdateIndeterminate()
    {
        if (IsIndeterminate)
        {
            SetFillWidth(0.3, animate: false);
            var animation = new DoubleAnimation
            {
                From = -0.3 * Math.Max(ActualWidth, 100),
                To = Math.Max(ActualWidth, 100),
                Duration = new Duration(TimeSpan.FromMilliseconds(1300)),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, _indeterminateOffset);
            Storyboard.SetTargetProperty(animation, "X");
            _indeterminateAnimation = new Storyboard();
            _indeterminateAnimation.Children.Add(animation);
            _indeterminateAnimation.Begin();
        }
        else
        {
            _indeterminateAnimation?.Stop();
            _indeterminateAnimation = null;
            _indeterminateOffset.X = 0;
            SetFillWidth(Value, animate: false);
        }

        UpdateShimmer();
    }

    private void AnimateTo()
    {
        if (!IsIndeterminate)
        {
            SetFillWidth(Math.Clamp(Value, 0, 1), animate: true);
        }
    }

    private void SetFillWidth(double value, bool animate)
    {
        _shownValue = value;
        double target = Math.Max(0, ActualWidth * value);
        if (!animate || ActualWidth <= 0 || double.IsNaN(_fill.Width))
        {
            _widthAnimation?.Stop();
            _fill.Width = target;
            return;
        }

        // Al parar una animación WinUI vuelve al valor base: se fija antes el ancho actual para no dar saltos.
        double current = _fill.ActualWidth;
        _widthAnimation?.Stop();
        _fill.Width = current;

        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(320)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, _fill);
        Storyboard.SetTargetProperty(animation, "Width");
        _widthAnimation = new Storyboard();
        _widthAnimation.Children.Add(animation);
        _widthAnimation.Begin();
    }
}
