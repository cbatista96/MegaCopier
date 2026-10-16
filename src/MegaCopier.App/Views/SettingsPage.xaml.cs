using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.Engine;
using MegaCopier.Engine.Native;
using MegaCopier.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App.Views;

/// <summary>Miniatura de un tema en la galería: una ventanita con sus dos barras y sus colores.</summary>
public sealed class ThemeTile(ThemeDefinition theme, string? name = null)
{
    public ThemeDefinition Theme { get; } = theme;

    public string Id => Theme.Id;

    public string Name { get; } = name ?? theme.Name;

    public Brush WindowBrush => Opaque(Theme.Background, Theme.Dark);

    public Brush SurfaceBrush => new SolidColorBrush(ThemeManager.Parse(Theme.Surface));

    public Brush BorderBrush => new SolidColorBrush(ThemeManager.Parse(Theme.Border));

    public Brush TrackBrush => new SolidColorBrush(ThemeManager.Parse(Theme.Track));

    public Brush AccentBrush => new SolidColorBrush(ThemeManager.Parse(Theme.Accent));

    public Brush TotalBrush => Gradient(Theme.TotalStart, Theme.TotalEnd);

    public Brush FileBrush => Gradient(Theme.FileStart, Theme.FileEnd);

    /// <summary>Nombre accesible del elemento de la galería.</summary>
    public override string ToString() => Name;

    /// <summary>Los temas con fondo transparente (Mica) se muestran sobre gris oscuro o claro.</summary>
    private static SolidColorBrush Opaque(string color, bool dark)
    {
        var c = ThemeManager.Parse(color);
        var baseColor = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 36) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        double a = c.A / 255.0;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            (byte)((c.R * a) + (baseColor.R * (1 - a))),
            (byte)((c.G * a) + (baseColor.G * (1 - a))),
            (byte)((c.B * a) + (baseColor.B * (1 - a)))));
    }

    private static LinearGradientBrush Gradient(string start, string end)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
        brush.GradientStops.Add(new GradientStop { Color = ThemeManager.Parse(start), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = ThemeManager.Parse(end), Offset = 1 });
        return brush;
    }
}

/// <summary>
/// Preferencias: general, ventana, temas (galería + editor completo), opciones de copia, cola y avanzado.
/// Todo se guarda al momento; solo el idioma necesita reiniciar.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly App _app = (App)Application.Current;
    private readonly string _startupLanguage;
    private ThemeDefinition _editing = ThemeManager.Current.Clone();
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        _startupLanguage = _app.Settings.Language;

        LanguageBox.Items.Add(new ComboBoxItem { Content = L.Get("Language_Auto"), Tag = "" });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "Español", Tag = "es-ES" });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        foreach (var hash in OptionsTab.Hashes)
        {
            HashBox.Items.Add(new ComboBoxItem { Content = OptionsTab.HashName(hash) });
        }

        AboutText.Text = L.F("About", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "");
        _app.EnabledChanged += () => DispatcherQueue.TryEnqueue(Load);
    }

    public event Action? CloseRequested;

    /// <summary>Carga los valores actuales (se llama cada vez que se abre la página).</summary>
    public void Load()
    {
        _loading = true;
        var s = _app.Settings;
        EnabledToggle.IsOn = s.Enabled;
        PasteToggle.IsOn = s.InterceptExplorerPaste;
        ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
        ShellMenuToggle.IsEnabled = App.ShellExtensionPath is not null;
        StartupToggle.IsOn = StartupRegistration.IsEnabled;

        WindowModeBox.SelectedIndex = (int)s.WindowMode;
        FinishWindowBox.SelectedIndex = (int)s.FinishWindow;
        OnTopToggle.IsOn = s.AlwaysOnTop;
        NotifyToggle.IsOn = s.NotifyOnFinish;
        SoundBox.IsChecked = s.PlaySound;
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Language) ?? LanguageBox.Items[0];

        VerifyToggle.IsOn = s.VerifyAfterCopy;
        ConflictBox.SelectedIndex = OptionsTab.ConflictIndex(s.Conflict);
        HashBox.SelectedIndex = Math.Max(0, Array.IndexOf(OptionsTab.Hashes, s.HashAlgorithm));
        TimestampsBox.IsChecked = s.PreserveTimestamps;
        AttributesBox.IsChecked = s.PreserveAttributes;
        StreamsBox.IsChecked = s.CopyStreams;
        AclBox.IsChecked = s.CopySecurity.HasFlag(SecurityParts.Acl);
        OwnerBox.IsChecked = s.CopySecurity.HasFlag(SecurityParts.Owner);
        AuditBox.IsChecked = s.CopySecurity.HasFlag(SecurityParts.Audit);
        VerifySkippedBox.IsChecked = s.VerifySkipped;
        ChecksumBox.IsChecked = s.SaveChecksumFile;
        ReportBox.IsChecked = s.ExportReport;
        FreeSpaceBox.IsChecked = s.CheckFreeSpace;
        CacheBox.IsChecked = s.UseSystemCache;
        AutoResumeBox.IsChecked = s.AutoResumeInterrupted;
        SpeedBox.SelectedItem = SpeedBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.SpeedLimitMBps.ToString()) ?? SpeedBox.Items[0];
        BufferBox.SelectedItem = BufferBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.BlockSizeMB.ToString()) ?? BufferBox.Items[2];
        ThreadsBox.Value = s.QueueDepth;
        MaxJobsBox.Value = s.MaxActiveJobs;
        FastBox.Text = s.FastDevices;
        PriorityBox.SelectedIndex = (int)s.Priority;
        ScriptsBox.Text = string.Join(Environment.NewLine, s.Scripts);

        LoadThemes();
        UpdateRestartInfo();
        _loading = false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Ajustes simples: cualquier cambio vuelca todos los controles en las preferencias y guarda.

    private void Changed(object sender, RoutedEventArgs e) => Store();

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => Store();

    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Store();

    private void Store()
    {
        if (_loading)
        {
            return;
        }

        var s = _app.Settings;
        if (EnabledToggle.IsOn != s.Enabled)
        {
            _app.SetEnabled(EnabledToggle.IsOn, notify: false);
        }

        if (PasteToggle.IsOn != s.InterceptExplorerPaste)
        {
            s.InterceptExplorerPaste = PasteToggle.IsOn;
            _app.SetExplorerPaste(PasteToggle.IsOn);
        }

        s.WindowMode = (WindowMode)Math.Max(0, WindowModeBox.SelectedIndex);
        s.FinishWindow = (FinishWindow)Math.Max(0, FinishWindowBox.SelectedIndex);
        s.AlwaysOnTop = OnTopToggle.IsOn;
        s.NotifyOnFinish = NotifyToggle.IsOn;
        s.PlaySound = SoundBox.IsChecked == true;

        s.VerifyAfterCopy = VerifyToggle.IsOn;
        s.Conflict = OptionsTab.ConflictAt(ConflictBox.SelectedIndex);
        s.HashAlgorithm = OptionsTab.Hashes[Math.Max(0, HashBox.SelectedIndex)];
        s.PreserveTimestamps = TimestampsBox.IsChecked == true;
        s.PreserveAttributes = AttributesBox.IsChecked == true;
        s.CopyStreams = StreamsBox.IsChecked == true;
        s.CopySecurity = (AclBox.IsChecked == true ? SecurityParts.Acl : 0)
            | (OwnerBox.IsChecked == true ? SecurityParts.Owner : 0)
            | (AuditBox.IsChecked == true ? SecurityParts.Audit : 0);
        s.VerifySkipped = VerifySkippedBox.IsChecked == true;
        s.SaveChecksumFile = ChecksumBox.IsChecked == true;
        s.ExportReport = ReportBox.IsChecked == true;
        s.CheckFreeSpace = FreeSpaceBox.IsChecked == true;
        s.UseSystemCache = CacheBox.IsChecked == true;
        s.AutoResumeInterrupted = AutoResumeBox.IsChecked == true;
        if (SpeedBox.SelectedItem is ComboBoxItem { Tag: string speed } && int.TryParse(speed, out int limit))
        {
            s.SpeedLimitMBps = limit;
        }

        if (BufferBox.SelectedItem is ComboBoxItem { Tag: string buffer } && int.TryParse(buffer, out int block))
        {
            s.BlockSizeMB = block;
        }

        if (!double.IsNaN(ThreadsBox.Value))
        {
            s.QueueDepth = (int)ThreadsBox.Value;
        }

        if (!double.IsNaN(MaxJobsBox.Value))
        {
            s.MaxActiveJobs = (int)MaxJobsBox.Value;
        }

        s.FastDevices = FastBox.Text.Trim();
        s.Priority = (ProcessPriority)Math.Max(0, PriorityBox.SelectedIndex);
        s.Scripts = [.. ScriptsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        s.Save();
        _app.ApplySettings();
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language })
        {
            return;
        }

        _app.Settings.Language = language;
        _app.Settings.Save();
        LanguageSetting.Save(language);
        UpdateRestartInfo();
    }

    private void UpdateRestartInfo() => RestartInfo.IsOpen = _app.Settings.Language != _startupLanguage;

    private void Restart_Click(object sender, RoutedEventArgs e) => _app.Restart();

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void ShellMenu_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || ShellMenuToggle.IsOn == ShellExtensionRegistration.IsRegistered)
        {
            return;
        }

        try
        {
            if (ShellMenuToggle.IsOn && App.ShellExtensionPath is { } dll)
            {
                ShellExtensionRegistration.Register(dll);
            }
            else
            {
                ShellExtensionRegistration.Unregister();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loading = true;
            ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
            _loading = false;
        }
    }

    private void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading && StartupToggle.IsOn != StartupRegistration.IsEnabled)
        {
            StartupRegistration.Set(StartupToggle.IsOn);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Temas

    private void LoadThemes()
    {
        var auto = ThemeManager.Find(ThemeManager.AutoId).Clone();
        auto.Id = ThemeManager.AutoId;
        var tiles = new List<ThemeTile> { new(auto, L.Get("Theme_Auto")) };
        tiles.AddRange(ThemeManager.All().Select(t => new ThemeTile(t, t.BuiltIn ? L.Get("Theme_" + t.Id) : t.Name)));
        ThemeGallery.ItemsSource = tiles;
        ThemeGallery.SelectedItem = tiles.FirstOrDefault(t => t.Id == _app.Settings.ThemeId) ?? tiles[0];
        LoadEditor(ThemeManager.Current);
    }

    private void ThemeGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeGallery.SelectedItem is not ThemeTile tile)
        {
            return;
        }

        _app.Settings.ThemeId = tile.Id;
        _app.Settings.Save();
        _app.ApplyTheme();
        _loading = true;
        LoadEditor(ThemeManager.Current);
        _loading = false;
    }

    /// <summary>Pone en el editor una copia del tema; los cambios se ven al momento y se guardan con "Guardar".</summary>
    private void LoadEditor(ThemeDefinition theme)
    {
        _editing = theme.Clone();
        bool wasLoading = _loading;
        _loading = true;
        ThemeNameBox.Text = theme.BuiltIn ? L.F("Theme_CopyOf", L.Get("Theme_" + theme.Id)) : theme.Name;
        ThemeBaseBox.SelectedIndex = theme.Dark ? 0 : 1;
        ThemeBackdropBox.SelectedIndex = (int)theme.Backdrop;
        RadiusSlider.Value = theme.CornerRadius;
        BarSlider.Value = theme.BarHeight;
        ThemeSaveText.Text = L.Get(theme.BuiltIn ? "Theme_SaveAsNew" : "Theme_Save");
        ThemeDeleteButton.IsEnabled = !theme.BuiltIn;
        BuildColorSlots();
        _loading = wasLoading;
    }

    private (string Key, Func<string> Get, Action<string> Set)[] Slots() =>
    [
        ("Theme_ColorBackground", () => _editing.Background, v => _editing.Background = v),
        ("Theme_ColorSurface", () => _editing.Surface, v => _editing.Surface = v),
        ("Theme_ColorSurfaceStrong", () => _editing.SurfaceStrong, v => _editing.SurfaceStrong = v),
        ("Theme_ColorBorder", () => _editing.Border, v => _editing.Border = v),
        ("Theme_ColorText", () => _editing.Text, v => _editing.Text = v),
        ("Theme_ColorTextSecondary", () => _editing.TextSecondary, v => _editing.TextSecondary = v),
        ("Theme_ColorAccent", () => _editing.Accent, v => _editing.Accent = v),
        ("Theme_ColorAccentText", () => _editing.AccentText, v => _editing.AccentText = v),
        ("Theme_ColorTotalStart", () => _editing.TotalStart, v => _editing.TotalStart = v),
        ("Theme_ColorTotalEnd", () => _editing.TotalEnd, v => _editing.TotalEnd = v),
        ("Theme_ColorFileStart", () => _editing.FileStart, v => _editing.FileStart = v),
        ("Theme_ColorFileEnd", () => _editing.FileEnd, v => _editing.FileEnd = v),
        ("Theme_ColorTrack", () => _editing.Track, v => _editing.Track = v),
        ("Theme_ColorSuccess", () => _editing.Success, v => _editing.Success = v),
        ("Theme_ColorWarning", () => _editing.Warning, v => _editing.Warning = v),
        ("Theme_ColorError", () => _editing.Error, v => _editing.Error = v),
    ];

    private readonly List<(Button Button, Border Swatch)> _slotViews = [];
    private int _slot = -1;

    /// <summary>
    /// Un botón por color con su muestra. Al pulsarlo se edita en el selector de debajo (sin ventanas emergentes: se
    /// pueden ir cambiando colores seguidos viendo el resultado en toda la interfaz).
    /// </summary>
    private void BuildColorSlots()
    {
        ColorSlots.Children.Clear();
        _slotViews.Clear();
        var slots = Slots();
        for (int i = 0; i < slots.Length; i++)
        {
            var (key, get, _) = slots[i];
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeManager.BorderBrush,
                Background = new SolidColorBrush(ThemeManager.Parse(get())),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock { Text = L.Get(key), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(swatch);
            row.Children.Add(label);

            int index = i;
            var button = new Button
            {
                Content = row,
                Width = 200,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(1),
            };
            button.Click += (_, _) => SelectSlot(index);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, L.Get(key));
            ColorSlots.Children.Add(button);
            _slotViews.Add((button, swatch));
        }

        SelectSlot(_slot < 0 ? -1 : Math.Min(_slot, slots.Length - 1));
    }

    private void SelectSlot(int index)
    {
        _slot = index;
        for (int i = 0; i < _slotViews.Count; i++)
        {
            _slotViews[i].Button.BorderBrush = i == index ? ThemeManager.AccentBrush : ThemeManager.BorderBrush;
        }

        SlotEditor.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (index >= 0)
        {
            var (key, get, _) = Slots()[index];
            SlotTitle.Text = L.Get(key);
            bool wasLoading = _loading;
            _loading = true;
            SlotPicker.Color = ThemeManager.Parse(get());
            _loading = wasLoading;
        }
    }

    private void SlotPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading || _slot < 0)
        {
            return;
        }

        var color = args.NewColor;
        if (color.A == 0 && args.OldColor.A >= 64)
        {
            // Un hex de 6 cifras escrito a mano llega sin opacidad (transparente): se entiende como opaco.
            color = Windows.UI.Color.FromArgb(args.OldColor.A, color.R, color.G, color.B);
            _loading = true;
            sender.Color = color;
            _loading = false;
        }

        Slots()[_slot].Set(ThemeManager.ToHex(color));
        _slotViews[_slot].Swatch.Background = new SolidColorBrush(color);
        PreviewEditing();
    }

    /// <summary>Aplica el tema en edición a toda la interfaz para verlo al momento (sin guardar).</summary>
    private void PreviewEditing()
    {
        if (_loading)
        {
            return;
        }

        // El selector de color avisa en cada movimiento: se aplica como mucho cada 150 ms.
        if (_previewTimer is null)
        {
            _previewTimer = DispatcherQueue.CreateTimer();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(150);
            _previewTimer.IsRepeating = false;
            _previewTimer.Tick += (_, _) => _app.ApplyTheme(_editing.Clone());
        }

        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _previewTimer;

    private void ThemeName_Changed(object sender, TextChangedEventArgs e) => _editing.Name = ThemeNameBox.Text.Trim();

    private void ThemeEditor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _editing.Dark = ThemeBaseBox.SelectedIndex != 1;
        _editing.Backdrop = (AppBackdrop)Math.Max(0, ThemeBackdropBox.SelectedIndex);
        PreviewEditing();
    }

    private void ThemeSlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _editing.CornerRadius = RadiusSlider.Value;
        _editing.BarHeight = BarSlider.Value;
        PreviewEditing();
    }

    private void ThemeSave_Click(object sender, RoutedEventArgs e)
    {
        var theme = _editing.Clone();
        if (theme.BuiltIn || theme.Id == ThemeManager.AutoId || string.IsNullOrEmpty(theme.Id))
        {
            theme.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            theme.Name = L.Get("Theme_Custom");
        }

        ThemeManager.Save(theme);
        _app.Settings.ThemeId = theme.Id;
        _app.Settings.Save();
        _app.ApplyTheme();
        Load();
        EditorExpander.IsExpanded = true;
    }

    private void ThemeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing.BuiltIn)
        {
            return;
        }

        ThemeManager.Delete(_editing);
        _app.Settings.ThemeId = ThemeManager.AutoId;
        _app.Settings.Save();
        _app.ApplyTheme();
        Load();
    }

    private async void ThemeImport_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(".json") is not { } path)
        {
            return;
        }

        if (ThemeManager.Import(path) is { } theme)
        {
            _app.Settings.ThemeId = theme.Id;
            _app.Settings.Save();
            _app.ApplyTheme();
            Load();
        }
        else
        {
            _app.Notify(L.Get("Theme_ImportFailed"), path);
        }
    }

    private async void ThemeExport_Click(object sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(_editing.Name) ? "tema" : _editing.Name;
        if (await Pickers.SaveAsync(name, ".json", L.Get("Theme_FileType")) is { } path)
        {
            var theme = _editing.Clone();
            theme.BuiltIn = false;
            ThemeManager.Export(theme, path);
        }
    }
}
