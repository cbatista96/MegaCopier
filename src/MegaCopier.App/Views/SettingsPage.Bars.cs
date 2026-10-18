using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Views;

/// <summary>
/// Ajustes > Interfaz > Barras de progreso: qué barra se edita (total, archivo o lista de copias), una vista previa en
/// movimiento con los estados (copiando, en pausa, error, terminada), la galería de estilos animados y todas las
/// opciones (colores, grosor, redondeo, velocidad, brillo, texto, avance y efectos). Todo se aplica al momento.
/// </summary>
public sealed partial class SettingsPage
{
    private static readonly BarStyle[] AllBarStyles = Enum.GetValues<BarStyle>();

    /// <summary>Colores listos para elegir de un toque (inicio y fin del degradado).</summary>
    private static readonly (string Key, string Start, string End)[] BarSwatches =
    [
        ("Green", "#FF2E9E3E", "#FF8BE07A"),
        ("Violet", "#FF3B82F6", "#FFA855F7"),
        ("Teal", "#FF0EA5A4", "#FF5EEAD4"),
        ("Orange", "#FFEA580C", "#FFFBBF24"),
        ("Pink", "#FFDB2777", "#FFF9A8D4"),
        ("Silver", "#FF8D96A3", "#FFE5E9EF"),
    ];

    private readonly List<FlowBar> _barPreviews = [];
    private BarRole _barRole = BarRole.Total;
    private BarSettings _bar = new();
    private bool _barLoading;
    private DispatcherTimer? _barTimer;
    private double _barClock;
    private FlowBar? _barPreview;
    private GridView? _barGallery;
    private GalleryMore? _barMore;
    private ComboBox? _barColorBox;
    private StackPanel? _barCustomColors;
    private Button? _barStartButton;
    private Button? _barEndButton;
    private Slider? _barThickness;
    private Slider? _barRadius;
    private Slider? _barSpeed;
    private Slider? _barGlow;
    private ComboBox? _barTextBox;
    private ComboBox? _barMotionBox;
    private ToggleSwitch? _barEffects;
    private ToggleSwitch? _barBorder;
    private ToggleSwitch? _barReduce;
    private FrameworkElement? _barTextCard;
    private string _barPreviewState = "Running";

    /// <summary>Monta la sección la primera vez que se muestra la Interfaz.</summary>
    private void BuildBarsSection()
    {
        if (BarsHost.Children.Count > 0)
        {
            return;
        }

        var header = new Grid { ColumnSpacing = 6 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = L.Get("Bars_Title"), Style = Res<Style>("McSection") };
        header.Children.Add(title);
        var info = new InfoTip { Topic = "Bars", Margin = new Thickness(0, 14, 0, 0) };
        Grid.SetColumn(info, 1);
        header.Children.Add(info);
        BarsHost.Children.Add(header);
        BarsHost.Children.Add(new TextBlock { Text = L.Get("Bars_Description"), Style = Res<Style>("McSecondary"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, -2, 0, 4) });

        // Qué barra se edita.
        var roles = new SelectorBar();
        foreach (var role in new[] { BarRole.Total, BarRole.File, BarRole.Mini })
        {
            roles.Items.Add(new SelectorBarItem { Text = L.Get("Bars_Role" + role), Tag = role, IsSelected = role == _barRole });
        }

        roles.SelectionChanged += (s, _) =>
        {
            if (s.SelectedItem?.Tag is BarRole role)
            {
                _barRole = role;
                LoadBar();
            }
        };
        AutomationProperties.SetName(roles, L.Get("Bars_Which"));
        BarsHost.Children.Add(roles);

        // Vista previa grande con los estados.
        _barPreview = new FlowBar { IsActive = true, Value = PreviewValue, Margin = new Thickness(0, 6, 0, 0) };
        _barPreviews.Add(_barPreview);
        var states = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (string state in new[] { "Running", "Paused", "Error", "Done" })
        {
            var button = new ToggleButton { Content = L.Get("Bars_State" + state), Tag = state, IsChecked = state == _barPreviewState, Padding = new Thickness(10, 4, 10, 5), CornerRadius = new CornerRadius(14) };
            button.Click += (s, _) =>
            {
                _barPreviewState = (string)((FrameworkElement)s).Tag;
                foreach (var other in states.Children.OfType<ToggleButton>())
                {
                    other.IsChecked = (string)other.Tag == _barPreviewState;
                }

                ShowPreviewState();
            };
            states.Children.Add(button);
        }

        var previewCard = new Border
        {
            Style = Res<Style>("McSettingCard"),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = L.Get("Bars_Preview"), Style = Res<Style>("McStrong") },
                    _barPreview,
                    states,
                },
            },
        };
        BarsHost.Children.Add(previewCard);

        // Galería de estilos, cada uno moviéndose.
        BarsHost.Children.Add(new TextBlock { Text = L.Get("Bars_Styles"), Style = Res<Style>("McStrong"), Margin = new Thickness(2, 8, 0, 0) });
        _barGallery = new GridView { SelectionMode = ListViewSelectionMode.Single, Margin = new Thickness(-8, -4, 0, 0), Padding = new Thickness(4, 4, 0, 0) };
        AutomationProperties.SetName(_barGallery, L.Get("Bars_Styles"));
        var barItems = new List<object>();
        foreach (var style in AllBarStyles)
        {
            var bar = new FlowBar { IsActive = true, Margin = new Thickness(0, 4, 0, 4) };
            _barPreviews.Add(bar);
            var tile = new StackPanel { Width = 196, Spacing = 6, Padding = new Thickness(10, 8, 10, 10) };
            tile.Children.Add(new Border { Height = 34, Padding = new Thickness(2, 0, 2, 0), Child = bar });
            tile.Children.Add(new TextBlock { Text = L.Get("BarStyle_" + style), Style = Res<Style>("McBody"), TextTrimming = TextTrimming.CharacterEllipsis });
            tile.Children.Add(new TextBlock { Text = L.Get("BarStyle_" + style + "_Hint"), Style = Res<Style>("McSecondary"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Height = 34 });
            var item = new GridViewItem { Content = tile, Tag = style };
            AutomationProperties.SetName(item, L.Get("BarStyle_" + style));
            ToolTipService.SetToolTip(item, L.Get("BarStyle_" + style + "_Hint"));
            barItems.Add(item);
        }

        _barGallery.SelectionChanged += (_, _) =>
        {
            if (!_barLoading && _barGallery.SelectedItem is GridViewItem { Tag: BarStyle style })
            {
                _bar.Style = style;
                SaveBar();
            }
        };
        BarsHost.Children.Add(_barGallery);
        _barMore = new GalleryMore(
            _barGallery,
            () => barItems.OfType<GridViewItem>().FirstOrDefault(i => (BarStyle)i.Tag == _bar.Style),
            item =>
            {
                bool was = _barLoading;
                _barLoading = true;
                _barGallery.SelectedItem = item;
                _barLoading = was;
            });
        _barMore.SetItems(barItems);

        // Opciones.
        _barColorBox = Combo("Bars_ColorTheme", "Bars_ColorCustom", "Bars_ColorTraffic", "Bars_ColorRainbow");
        _barColorBox.SelectionChanged += (_, _) =>
        {
            if (!_barLoading)
            {
                _bar.ColorMode = (BarColorMode)Math.Max(0, _barColorBox.SelectedIndex);
                SaveBar();
            }
        };
        _barStartButton = ColorButton(start: true);
        _barEndButton = ColorButton(start: false);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (key, start, end) in BarSwatches)
        {
            var swatch = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops = { new GradientStop { Color = ThemeManager.Parse(start), Offset = 0 }, new GradientStop { Color = ThemeManager.Parse(end), Offset = 1 } },
                },
            };
            ToolTipService.SetToolTip(swatch, L.Get("Bars_Swatch" + key));
            AutomationProperties.SetName(swatch, L.Get("Bars_Swatch" + key));
            swatch.Click += (_, _) =>
            {
                _bar.ColorMode = BarColorMode.Custom;
                _bar.ColorStart = start;
                _bar.ColorEnd = end;
                SaveBar();
                LoadBar();
            };
            swatches.Children.Add(swatch);
        }

        _barCustomColors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _barStartButton, _barEndButton } };
        _barThickness = SliderFor(0, 32, 1);
        _barRadius = SliderFor(-1, 16, 1);
        _barSpeed = SliderFor(0.25, 3, 0.25);
        _barGlow = SliderFor(0, 100, 5);
        _barTextBox = Combo("Bars_TextOutside", "Bars_TextInside", "Bars_TextHidden");
        _barTextBox.SelectionChanged += (_, _) =>
        {
            if (!_barLoading)
            {
                _bar.Text = (BarText)Math.Max(0, _barTextBox.SelectedIndex);
                SaveBar();
            }
        };
        _barMotionBox = Combo("Bars_MotionSmooth", "Bars_MotionElastic", "Bars_MotionSteps");
        _barMotionBox.SelectionChanged += (_, _) =>
        {
            if (!_barLoading)
            {
                _bar.Motion = (BarMotion)Math.Max(0, _barMotionBox.SelectedIndex);
                SaveBar();
            }
        };
        _barEffects = Toggle((on) => _bar.StateEffects = on);
        _barBorder = Toggle((on) => _bar.Border = on);
        _barReduce = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        _barReduce.Toggled += (_, _) =>
        {
            if (!_barLoading)
            {
                _app.Settings.ReduceBarMotion = _barReduce.IsOn;
                SaveBar();
            }
        };

        BarsHost.Children.Add(BarCard("", "Colors", _barColorBox, new StackPanel { Spacing = 8, Children = { swatches, _barCustomColors } }));
        BarsHost.Children.Add(BarCard("", "Thickness", _barThickness));
        BarsHost.Children.Add(BarCard("", "Radius", _barRadius));
        BarsHost.Children.Add(BarCard("", "Speed", _barSpeed));
        BarsHost.Children.Add(BarCard("", "Glow", _barGlow));
        _barTextCard = BarCard("", "Text", _barTextBox);
        BarsHost.Children.Add(_barTextCard);
        BarsHost.Children.Add(BarCard("", "Motion", _barMotionBox));
        BarsHost.Children.Add(BarCard("", "Effects", _barEffects));
        BarsHost.Children.Add(BarCard("", "Border", _barBorder));
        BarsHost.Children.Add(BarCard("", "Reduce", _barReduce));

        var copyAll = new Button { Style = Res<Style>("McButton"), Content = L.Get("Bars_ApplyAll") };
        copyAll.Click += (_, _) =>
        {
            _app.Settings.TotalBar = _bar.Clone();
            _app.Settings.FileBar = _bar.Clone();
            var mini = _bar.Clone();
            mini.Thickness = 0;
            mini.Text = BarText.Outside;
            _app.Settings.MiniBar = mini;
            SaveBar(store: false);
        };
        var reset = new Button { Style = Res<Style>("McButton"), Content = L.Get("Bars_Reset") };
        reset.Click += (_, _) =>
        {
            _bar = new BarSettings();
            SaveBar();
            LoadBar();
        };
        BarsHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { copyAll, reset } });

        _barTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _barTimer.Tick += (_, _) => AnimatePreviews();
        BarsHost.Loaded += (_, _) => _barTimer.Start();
        BarsHost.Unloaded += (_, _) => _barTimer.Stop();
        _barTimer.Start();
        LoadBar();
    }

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];

    /// <summary>Una tarjeta de opción con icono, título, explicación, botón «i» y su control.</summary>
    private static FrameworkElement BarCard(string glyph, string topic, FrameworkElement control, FrameworkElement? below = null)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new McIcon { Glyph = glyph, FontSize = 18, Accent = true, VerticalAlignment = VerticalAlignment.Center });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = L.Get($"Info_Bar{topic}_Title"), Style = Res<Style>("McBody"), TextWrapping = TextWrapping.Wrap };
        texts.Children.Add(label);
        texts.Children.Add(new TextBlock { Text = L.Get($"Bars_{topic}Description"), Style = Res<Style>("McSecondary"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var info = new InfoTip { Topic = "Bar" + topic };
        Grid.SetColumn(info, 2);
        grid.Children.Add(info);
        AutomationProperties.SetName(control, label.Text);
        if (control is Slider)
        {
            // Los deslizadores, debajo y a todo el ancho.
            Grid.SetRow(control, 1);
            Grid.SetColumn(control, 1);
            Grid.SetColumnSpan(control, 3);
        }
        else
        {
            Grid.SetColumn(control, 3);
            control.VerticalAlignment = VerticalAlignment.Center;
        }

        grid.Children.Add(control);
        if (below is not null)
        {
            Grid.SetRow(below, 1);
            Grid.SetColumn(below, 1);
            Grid.SetColumnSpan(below, 3);
            grid.Children.Add(below);
        }

        grid.Tag = "SettingRow";
        return new Border { Style = Res<Style>("McSettingCard"), Child = grid };
    }

    private static ComboBox Combo(params string[] keys)
    {
        var box = new ComboBox { MinWidth = 220 };
        foreach (string key in keys)
        {
            box.Items.Add(new ComboBoxItem { Content = L.Get(key) });
        }

        return box;
    }

    private Slider SliderFor(double minimum, double maximum, double step)
    {
        var slider = new Slider { Minimum = minimum, Maximum = maximum, StepFrequency = step, MinWidth = 220 };
        slider.ThumbToolTipValueConverter = new BarSliderText(this, slider);
        slider.ValueChanged += (_, _) =>
        {
            if (_barLoading)
            {
                return;
            }

            if (slider == _barThickness)
            {
                _bar.Thickness = slider.Value;
            }
            else if (slider == _barRadius)
            {
                _bar.Radius = slider.Value;
            }
            else if (slider == _barSpeed)
            {
                _bar.Speed = slider.Value;
            }
            else if (slider == _barGlow)
            {
                _bar.Glow = slider.Value / 100;
            }

            SaveBar();
        };
        return slider;
    }

    /// <summary>Texto del deslizador: «Del tema», «12 px», «1,5×», «60 %».</summary>
    internal string SliderText(Slider slider, double value) =>
        slider == _barThickness ? (value <= 0 ? L.Get("Bars_FromTheme") : $"{value:0} px")
        : slider == _barRadius ? (value < 0 ? L.Get("Bars_FromTheme") : $"{value:0} px")
        : slider == _barSpeed ? $"{value:0.##}×"
        : $"{value:0} %";

    private ToggleSwitch Toggle(Action<bool> set)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.Toggled += (_, _) =>
        {
            if (!_barLoading)
            {
                set(toggle.IsOn);
                SaveBar();
            }
        };
        return toggle;
    }

    /// <summary>Botón con la muestra del color; al pulsarlo se abre el selector de color.</summary>
    private Button ColorButton(bool start)
    {
        var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = ThemeManager.BorderBrush };
        var label = new TextBlock { Text = L.Get(start ? "Bars_ColorStart" : "Bars_ColorEnd"), VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { Style = Res<Style>("McButton"), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, label } }, Tag = swatch };
        AutomationProperties.SetName(button, label.Text);
        button.Click += (_, _) =>
        {
            var picker = new ColorPicker
            {
                Color = ThemeManager.Parse(start ? _bar.ColorStart : _bar.ColorEnd),
                IsAlphaEnabled = false,
                IsMoreButtonVisible = false,
                IsHexInputVisible = true,
                ColorSpectrumShape = ColorSpectrumShape.Ring,
            };
            picker.ColorChanged += (_, e) =>
            {
                string hex = $"#FF{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                if (start)
                {
                    _bar.ColorStart = hex;
                }
                else
                {
                    _bar.ColorEnd = hex;
                }

                _bar.ColorMode = BarColorMode.Custom;
                swatch.Background = new SolidColorBrush(e.NewColor);
                SaveBar();
            };
            new Flyout { Content = picker }.ShowAt(button);
        };
        return button;
    }

    /// <summary>Muestra en los controles el aspecto de la barra elegida.</summary>
    private void LoadBar()
    {
        if (_barGallery is null)
        {
            return;
        }

        _barLoading = true;
        _bar = (_barRole switch
        {
            BarRole.File => _app.Settings.FileBar,
            BarRole.Mini => _app.Settings.MiniBar,
            _ => _app.Settings.TotalBar,
        }).Clone();
        _barMore!.Refresh();
        _barColorBox!.SelectedIndex = (int)_bar.ColorMode;
        ((Border)_barStartButton!.Tag).Background = new SolidColorBrush(ThemeManager.Parse(_bar.ColorStart));
        ((Border)_barEndButton!.Tag).Background = new SolidColorBrush(ThemeManager.Parse(_bar.ColorEnd));
        _barCustomColors!.Visibility = _bar.ColorMode == BarColorMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        _barThickness!.Maximum = _barRole == BarRole.Mini ? 10 : 32;
        _barThickness.Value = _bar.Thickness;
        _barRadius!.Value = _bar.Radius;
        _barSpeed!.Value = _bar.Speed;
        _barGlow!.Value = _bar.Glow * 100;
        _barTextBox!.SelectedIndex = (int)_bar.Text;
        _barTextCard!.Visibility = _barRole == BarRole.Mini ? Visibility.Collapsed : Visibility.Visible;
        _barMotionBox!.SelectedIndex = (int)_bar.Motion;
        _barEffects!.IsOn = _bar.StateEffects;
        _barBorder!.IsOn = _bar.Border;
        _barReduce!.IsOn = _app.Settings.ReduceBarMotion;
        RefreshPreviews();
        _barLoading = false;
    }

    /// <summary>Guarda el aspecto editado, lo aplica a todas las barras y redibuja las vistas previas.</summary>
    private void SaveBar(bool store = true)
    {
        if (store)
        {
            switch (_barRole)
            {
                case BarRole.File: _app.Settings.FileBar = _bar.Clone(); break;
                case BarRole.Mini: _app.Settings.MiniBar = _bar.Clone(); break;
                default: _app.Settings.TotalBar = _bar.Clone(); break;
            }
        }

        _app.Settings.Save();
        BarAppearance.Apply(_app.Settings.TotalBar, _app.Settings.FileBar, _app.Settings.MiniBar, _app.Settings.ReduceBarMotion);
        _barCustomColors!.Visibility = _bar.ColorMode == BarColorMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        _barLoading = true;
        _barColorBox!.SelectedIndex = (int)_bar.ColorMode;
        _barLoading = false;
        RefreshPreviews();
    }

    private void RefreshPreviews()
    {
        var fill = _barRole == BarRole.File ? (Brush)ThemeManager.FileBrush : ThemeManager.TotalBrush;
        double height = _barRole == BarRole.Mini ? 4 : double.NaN;
        _barPreview!.Role = _barRole;
        _barPreview.FixedHeight = height;
        _barPreview.Fill = fill;
        _barPreview.Appearance = _bar.Clone();
        for (int i = 1; i < _barPreviews.Count; i++)
        {
            var look = _bar.Clone();
            look.Style = AllBarStyles[i - 1];
            look.Text = BarText.Outside;
            if (look.Thickness <= 0 || look.Thickness > 14)
            {
                look.Thickness = _barRole == BarRole.Mini ? 4 : 10;
            }

            _barPreviews[i].Fill = fill;
            _barPreviews[i].Appearance = look;
        }

        ShowPreviewState();
    }

    private void ShowPreviewState()
    {
        if (_barPreview is null)
        {
            return;
        }

        _barPreview.IsPaused = _barPreviewState == "Paused";
        _barPreview.IsError = _barPreviewState == "Error";
        _barPreview.IsActive = _barPreviewState != "Done";
        if (_barPreviewState == "Done")
        {
            _barPreview.IsActive = true;
            _barPreview.Value = 0.98;
            _barPreview.Value = 1;
            _barPreview.IsActive = false;
        }
        else
        {
            // Copiando, en pausa o con error: siempre a media barra, para ver bien el estilo y su movimiento.
            _barPreview.Value = PreviewValue;
        }
    }

    /// <summary>Hasta dónde llega la vista previa grande mientras no está «Terminada».</summary>
    private const double PreviewValue = 0.48;

    /// <summary>Las vistas previas avanzan solas (cada una con su ritmo) y vuelven a empezar.</summary>
    private void AnimatePreviews()
    {
        if (Sec_Interface.Visibility != Visibility.Visible)
        {
            return;
        }

        _barClock += 0.035;
        for (int i = 0; i < _barPreviews.Count; i++)
        {
            var bar = _barPreviews[i];
            if (bar == _barPreview)
            {
                continue; // la vista previa grande no avanza: se queda a media barra (o llena, si está «Terminada»)
            }

            double t = (_barClock + (i * 0.11)) % 1.25;
            bar.Value = Math.Min(1, t);
        }
    }
}

/// <summary>Texto del globo de los deslizadores de las barras.</summary>
internal sealed partial class BarSliderText(SettingsPage page, Slider slider) : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => page.SliderText(slider, (double)value);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
