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
    private readonly TextBlock _glyph = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        IsTextScaleFactorEnabled = false,
    };

    private ContainerVisual? _glow;
    private bool _subscribed;

    public McIcon()
    {
        IsTabStop = false;
        FontSize = 16;
        _root.Children.Add(_tile);
        _root.Children.Add(_glyph);
        Content = _root;
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                IconPacks.Changed += Render;
                ThemeManager.Changed += OnThemeChanged;
                _subscribed = true;
            }

            Render();
        };
        Unloaded += (_, _) =>
        {
            IconPacks.Changed -= Render;
            ThemeManager.Changed -= OnThemeChanged;
            _subscribed = false;
        };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.4;
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Render());
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Render());
        _glyph.SizeChanged += (_, _) => UpdateGlowSize();
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

    private void OnThemeChanged(ThemeDefinition theme) => Render();

    private void Render()
    {
        var pack = PackOverride.Length > 0 ? IconPacks.Find(PackOverride) : IconPacks.Current;
        _glyph.Text = Glyph;
        _glyph.FontFamily = new FontFamily(pack.FontFamily);

        // Un Foreground puesto a propósito (estado: error, aviso, éxito) manda sobre el color del conjunto.
        bool ownColor = ReadLocalValue(ForegroundProperty) != DependencyProperty.UnsetValue;
        Color? fixedColor = ownColor && Foreground is SolidColorBrush own ? own.Color : null;
        Color color = AdjustForTheme(fixedColor ?? IconPacks.ColorFor(Glyph));
        double size = FontSize;

        _tile.Visibility = Visibility.Collapsed;
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
            {
                // El cuadrado ocupa lo mismo que ocupaba el icono (un poco más), para no mover nada de sitio.
                double box = Math.Round(size * 1.45);
                Width = box;
                Height = box;
                _tile.Width = box;
                _tile.Height = box;
                _tile.CornerRadius = new CornerRadius(box * 0.28);
                _tile.Visibility = Visibility.Visible;
                _glyph.FontSize = Math.Round(size * 0.82);
                if (pack.Style == IconStyle.Tile)
                {
                    var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
                    gradient.GradientStops.Add(new GradientStop { Color = Lighten(color, 0.18), Offset = 0 });
                    gradient.GradientStops.Add(new GradientStop { Color = Darken(color, 0.18), Offset = 1 });
                    _tile.Background = gradient;
                    _glyph.Foreground = new SolidColorBrush(Colors.White);
                }
                else
                {
                    _tile.Background = new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));
                    _glyph.Foreground = new SolidColorBrush(color);
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
