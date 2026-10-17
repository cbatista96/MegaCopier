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
        foreach (var (code, name) in Engine.Texts.Languages)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }
        foreach (var hash in OptionsTab.Hashes)
        {
            HashBox.Items.Add(new ComboBoxItem { Content = OptionsTab.HashName(hash) });
        }

        AboutText.Text = L.F("About", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "")
            + (Engine.DataFolder.IsPortable ? " · " + L.F("About_Portable", Engine.DataFolder.Path) : "");
        _app.EnabledChanged += () => DispatcherQueue.TryEnqueue(Load);
        Rules.RulesChanged += Store;
        SizeChanged += (_, e) =>
        {
            // Filas de ajuste "icono · texto · control": si no cabe, el control baja bajo el texto.
            bool stacked = e.NewSize.Width < 720;
            foreach (var row in Responsive.Descendants<Grid>(this).Where(g => g.ColumnDefinitions.Count >= 3 && (g.Parent is Border || g.Tag is "SettingRow")).ToList())
            {
                Responsive.StackSettingRow(row, stacked);
            }
        };
    }

    public event Action? CloseRequested;

    /// <summary>Carga los valores actuales (se llama cada vez que se abre la página).</summary>
    public void Load()
    {
        _loading = true;
        var s = _app.Settings;
        EnabledToggle.IsOn = s.Enabled;
        PasteToggle.IsOn = s.InterceptExplorerPaste;
        DragToggle.IsOn = s.InterceptExplorerDrag;
        DragConfirmBox.SelectedIndex = (int)s.DragConfirm;
        DragConfirmBox.IsEnabled = s.InterceptExplorerDrag;
        ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
        ShellMenuToggle.IsEnabled = App.ShellExtensionPath is not null;
        StartupToggle.IsOn = StartupRegistration.IsEnabled;

        WindowModeBox.SelectedIndex = (int)s.WindowMode;
        FinishWindowBox.SelectedIndex = (int)s.FinishWindow;
        OnTopToggle.IsOn = s.AlwaysOnTop;
        NotifyToggle.IsOn = s.NotifyOnFinish;
        LoadSounds();
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
        SmbBox.IsChecked = s.SmbCompression;
        AutoResumeBox.IsChecked = s.AutoResumeInterrupted;
        SpeedBox.SelectedItem = SpeedBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.SpeedLimitMBps.ToString()) ?? SpeedBox.Items[0];
        BufferBox.SelectedItem = BufferBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.BlockSizeMB.ToString()) ?? BufferBox.Items[2];
        ThreadsBox.Value = s.QueueDepth;
        MaxJobsBox.Value = s.MaxActiveJobs;
        FastBox.Text = s.FastDevices;
        PriorityBox.SelectedIndex = (int)s.Priority;
        UnitsBox.SelectedIndex = (int)s.Units;
        SlowBox.SelectedItem = SlowBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.SlowWhenActiveMBps.ToString()) ?? SlowBox.Items[0];
        LogsToggle.IsOn = s.SaveLogs;
        WebhookBox.Text = s.WebhookUrl;
        ScriptsBox.Text = string.Join(Environment.NewLine, s.Scripts);
        Rules.Load(s.Rules);
        LoadFavorites();
        LoadTasks();
        LoadGuard();
        LoadLiveSync();
        LoadReceive();
        LoadDisks();
        AutoTuneToggle.IsOn = s.AutoTune;
        ShowProfiles();
        ScheduleLimitBox.SelectedItem = ScheduleLimitBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.ScheduleLimitMBps.ToString()) ?? ScheduleLimitBox.Items[0];
        ScheduleFromBox.Time = s.ScheduleFrom;
        ScheduleToBox.Time = s.ScheduleTo;
        PauseBatteryToggle.IsOn = s.PauseOnBattery;
        PauseFullToggle.IsOn = s.PauseOnFullScreen;
        GuardIntervalBox.SelectedItem = GuardIntervalBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.GuardIntervalDays.ToString()) ?? GuardIntervalBox.Items[1];

        LoadThemes();
        UpdateRestartInfo();
        _loading = false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Ajustes simples: cualquier cambio vuelca todos los controles en las preferencias y guarda.

    private void Changed(object sender, RoutedEventArgs e) => Store();

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => Store();

    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Store();

    private void Time_Changed(object sender, TimePickerValueChangedEventArgs e) => Store();

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

        s.DragConfirm = (DragConfirm)Math.Max(0, DragConfirmBox.SelectedIndex);
        DragConfirmBox.IsEnabled = DragToggle.IsOn;
        if (DragToggle.IsOn != s.InterceptExplorerDrag)
        {
            s.InterceptExplorerDrag = DragToggle.IsOn;
            _app.SetExplorerDrag(DragToggle.IsOn);
        }

        s.WindowMode = (WindowMode)Math.Max(0, WindowModeBox.SelectedIndex);
        s.FinishWindow = (FinishWindow)Math.Max(0, FinishWindowBox.SelectedIndex);
        s.AlwaysOnTop = OnTopToggle.IsOn;
        s.NotifyOnFinish = NotifyToggle.IsOn;
        s.Sounds.Enabled = SoundBox.IsOn;
        s.Sounds.QuietWhenBusy = QuietBox.IsChecked == true;

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
        s.SmbCompression = SmbBox.IsChecked == true;
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
        s.Units = (SizeUnits)Math.Max(0, UnitsBox.SelectedIndex);
        if (SlowBox.SelectedItem is ComboBoxItem { Tag: string slow } && int.TryParse(slow, out int slowLimit))
        {
            s.SlowWhenActiveMBps = slowLimit;
        }

        s.SaveLogs = LogsToggle.IsOn;
        s.AutoTune = AutoTuneToggle.IsOn;
        s.WebhookUrl = WebhookBox.Text.Trim();
        if (ScheduleLimitBox.SelectedItem is ComboBoxItem { Tag: string scheduleTag } && int.TryParse(scheduleTag, out int scheduleLimit))
        {
            s.ScheduleLimitMBps = scheduleLimit;
        }

        s.ScheduleFrom = ScheduleFromBox.Time;
        s.ScheduleTo = ScheduleToBox.Time;
        s.PauseOnBattery = PauseBatteryToggle.IsOn;
        s.PauseOnFullScreen = PauseFullToggle.IsOn;
        if (GuardIntervalBox.SelectedItem is ComboBoxItem { Tag: string days } && int.TryParse(days, out int interval))
        {
            s.GuardIntervalDays = interval;
        }
        s.Scripts = [.. ScriptsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        s.Rules = Rules.Read();
        s.Save();
        _app.ApplySettings();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JobNotices.LogFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{JobNotices.LogFolder}\"") { UseShellExecute = true });
    }

    private async void TestWebhook_Click(object sender, RoutedEventArgs e)
    {
        Store();
        WebhookResult.Text = L.Get("Settings_WebhookSending");
        bool ok = await JobNotices.SendWebhookAsync(_app.Settings.WebhookUrl, L.Get("Settings_WebhookTestMessage"), null);
        WebhookResult.Text = L.Get(ok ? "Settings_WebhookOk" : "Settings_WebhookFailed");
    }

    // ---------------------------------------------------------------------------------------------------------
    // Sonidos

    private const string ChooseFileTag = "\u0001elegir";

    /// <summary>Una fila por momento: nombre, lista (sin sonido, el de Windows, los de C:\Windows\Media, uno propio) y ▶.</summary>
    private void LoadSounds()
    {
        var s = _app.Settings.Sounds;
        SoundBox.IsOn = s.Enabled;
        VolumeSlider.Value = s.Volume;
        VolumeText.Text = $"{s.Volume} %";
        QuietBox.IsChecked = s.QuietWhenBusy;

        SoundRows.Children.Clear();
        foreach (var e in Enum.GetValues<SoundEvent>())
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = L.Get("Sound_" + e), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            row.Children.Add(label);

            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label.Text);
            FillSoundChoices(box, s.Get(e));
            var captured = e;
            box.SelectionChanged += async (_, _) => await SoundChosen(box, captured);
            Grid.SetColumn(box, 1);
            row.Children.Add(box);

            var play = new Button
            {
                Content = new FontIcon { Glyph = "\uE768", FontSize = 12 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(play, L.Get("Sound_Test"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(play, L.Get("Sound_Test") + ": " + label.Text);
            play.Click += (_, _) => Sounds.Preview(_app.Settings.Sounds.Get(captured), captured, (int)VolumeSlider.Value);
            Grid.SetColumn(play, 2);
            row.Children.Add(play);
            SoundRows.Children.Add(row);
        }
    }

    private static void FillSoundChoices(ComboBox box, string current)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = L.Get("Sound_None"), Tag = "" });
        box.Items.Add(new ComboBoxItem { Content = L.Get("Sound_Default"), Tag = "default" });
        foreach (string path in Sounds.WindowsSounds)
        {
            box.Items.Add(new ComboBoxItem { Content = Path.GetFileNameWithoutExtension(path), Tag = path });
        }

        // Un archivo propio que no está en la lista de Windows.
        if (current.Length > 0 && current != "default" && !Sounds.WindowsSounds.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            box.Items.Add(new ComboBoxItem { Content = Path.GetFileName(current), Tag = current });
        }

        box.Items.Add(new ComboBoxItem { Content = L.Get("Sound_Choose"), Tag = ChooseFileTag });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, current, StringComparison.OrdinalIgnoreCase))
            ?? box.Items[1];
    }

    private async Task SoundChosen(ComboBox box, SoundEvent e)
    {
        if (_loading || box.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var s = _app.Settings.Sounds;
        if (tag == ChooseFileTag)
        {
            // Sonido propio: cualquier formato que reproduzca Windows.
            string? file = await Pickers.PickFileAsync(".wav", ".mp3", ".wma", ".m4a", ".aac", ".flac", ".ogg");
            _loading = true;
            if (file is not null)
            {
                s.Set(e, file);
            }

            FillSoundChoices(box, s.Get(e));
            _loading = false;
        }
        else
        {
            s.Set(e, tag);
        }

        _app.Settings.Save();
        Sounds.Preview(s.Get(e), e, s.Volume);
    }

    private void Volume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        VolumeText.Text = $"{(int)e.NewValue} %";
        if (!_loading)
        {
            _app.Settings.Sounds.Volume = (int)e.NewValue;
            _app.Settings.Save();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Carpetas favoritas

    private void LoadFavorites()
    {
        FavoritesList.Children.Clear();
        foreach (string folder in _app.Settings.FavoriteFolders)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.AccentBrush });
            var text = new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; // entera, aunque sea larga
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var remove = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, L.F("Settings_FavoritesRemove", folder));
            ToolTipService.SetToolTip(remove, L.F("Settings_FavoritesRemove", folder));
            string captured = folder;
            remove.Click += (_, _) =>
            {
                _app.Settings.FavoriteFolders.Remove(captured);
                _app.Settings.Save();
                LoadFavorites();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            FavoritesList.Children.Add(row);
        }

        if (_app.Settings.FavoriteFolders.Count == 0)
        {
            FavoritesList.Children.Add(new TextBlock { Text = L.Get("Settings_FavoritesEmpty"), FontSize = 12, Foreground = ThemeManager.TextSecondaryBrush });
        }
    }

    /// <summary>Tareas guardadas: una fila con "Ejecutar ahora" y "Borrar" (también del Programador de tareas).</summary>
    private void LoadTasks()
    {
        TasksList.Children.Clear();
        foreach (string name in ScheduledTasks.List())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeManager.AccentBrush });
            var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(text, ScheduledTasks.PathOf(name));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            string captured = name;
            var run = IconButton("", L.F("Settings_TaskRun", name));
            run.Click += (_, _) => _app.RunList(ScheduledTasks.PathOf(captured));
            Grid.SetColumn(run, 2);
            row.Children.Add(run);
            var remove = IconButton("", L.F("Settings_TaskDelete", name));
            remove.Click += (_, _) =>
            {
                if (ScheduledTasks.Delete(captured) is { } error)
                {
                    _app.Notify(L.Get("Notify_ProblemTitle"), error);
                }

                LoadTasks();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            TasksList.Children.Add(row);
        }

        if (TasksList.Children.Count == 0)
        {
            TasksList.Children.Add(new TextBlock { Text = L.Get("Settings_TasksEmpty"), FontSize = 12, Foreground = ThemeManager.TextSecondaryBrush, TextWrapping = TextWrapping.Wrap });
        }
    }

    /// <summary>Carpetas vigiladas: estado de la última comprobación, "Comprobar ahora" y "Dejar de vigilar".</summary>
    private void LoadGuard()
    {
        GuardList.Children.Clear();
        foreach (var folder in _app.Settings.GuardedFolders.ToList())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool damaged = folder.Damaged > 0;
            row.Children.Add(new FontIcon
            {
                Glyph = damaged ? "" : "",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = damaged ? ThemeManager.WarningBrush : ThemeManager.AccentBrush,
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = folder.Path, TextTrimming = TextTrimming.CharacterEllipsis });
            string state = folder.LastCheckUtc is not { } last ? L.Get("Guard_NotYet")
                : damaged ? (folder.Damaged == 1 ? L.F("Guard_LastDamagedOne", ViewModels.Format.Date(last)) : L.F("Guard_LastDamaged", ViewModels.Format.Date(last), folder.Damaged))
                : L.F("Guard_LastOk", ViewModels.Format.Date(last));
            text.Children.Add(new TextBlock { Text = state, FontSize = 12, Foreground = damaged ? ThemeManager.WarningBrush : ThemeManager.TextSecondaryBrush, TextWrapping = TextWrapping.Wrap });
            ToolTipService.SetToolTip(text, folder.Path);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var captured = folder;
            var check = IconButton("", L.F("Settings_GuardCheck", folder.Path));
            check.Click += (_, _) => _app.Guard?.Check(captured);
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
            var remove = IconButton("", L.F("Settings_GuardRemove", folder.Path));
            remove.Click += (_, _) =>
            {
                _app.Settings.GuardedFolders.Remove(captured);
                _app.Settings.Save();
                LoadGuard();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            GuardList.Children.Add(row);
        }

        if (GuardList.Children.Count == 0)
        {
            GuardList.Children.Add(new TextBlock { Text = L.Get("Settings_GuardEmpty"), FontSize = 12, Foreground = ThemeManager.TextSecondaryBrush, TextWrapping = TextWrapping.Wrap });
        }
    }

    private async void AddGuarded_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is not { } path
            || _app.Settings.GuardedFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var folder = new GuardedFolder { Path = path };
        _app.Settings.GuardedFolders.Add(folder);
        _app.Settings.Save();
        _app.Guard?.Check(folder); // la primera vez se protege en seguida
        LoadGuard();
    }

    /// <summary>Recibir de otros equipos: interruptor, código, estado y carpeta.</summary>
    private void LoadReceive()
    {
        bool loading = _loading;
        _loading = true;
        ReceiveToggle.IsOn = _app.Settings.ReceiveEnabled;
        _loading = loading;
        var peers = _app.Peers;
        ReceivePanel.Visibility = _app.Settings.ReceiveEnabled ? Visibility.Visible : Visibility.Collapsed;
        ReceiveCodeText.Text = peers?.Code ?? "";
        ReceiveFolderText.Text = L.F("Settings_PeerFolderIs", peers?.Folder ?? "");
        ToolTipService.SetToolTip(ReceiveFolderText, peers?.Folder);
        ReceiveInfo.Text = peers is { Error: { } error } ? L.F("Settings_PeerError", error)
            : peers?.IsRunning == true ? L.F("Settings_PeerListening", Environment.MachineName, peers.Port)
            : "";

        loading = _loading;
        _loading = true;
        LocalSendToggle.IsOn = _app.Settings.LocalSendEnabled;
        LocalSendAutoAcceptBox.IsChecked = _app.Settings.LocalSendAutoAccept;
        _loading = loading;
        LocalSendDescription.Text = L.F("Settings_LocalSendDescription", Environment.MachineName, peers?.Folder ?? "");
        LocalSendPanel.Visibility = _app.Settings.LocalSendEnabled ? Visibility.Visible : Visibility.Collapsed;
        LocalSendInfo.Text = peers is { LocalSendError: { } localError } ? L.F("Settings_PeerError", localError)
            : peers?.LocalSendRunning == true ? L.F("LocalSend_Listening", peers.LocalSendPort)
            : "";
    }

    private void LocalSend_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _app.Settings.LocalSendEnabled = LocalSendToggle.IsOn;
        _app.Settings.Save();
        _app.Peers?.Apply();
        LoadReceive();
    }

    private void LocalSendAutoAccept_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.LocalSendAutoAccept = LocalSendAutoAcceptBox.IsChecked == true;
        _app.Settings.Save();
    }

    private void Receive_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _app.Settings.ReceiveEnabled = ReceiveToggle.IsOn;
        _app.Settings.Save();
        _app.Peers?.Apply();
        LoadReceive();
    }

    private void NewCode_Click(object sender, RoutedEventArgs e)
    {
        _app.Peers?.NewCode();
        LoadReceive();
    }

    private async void ReceiveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            _app.Settings.ReceiveFolder = folder;
            _app.Settings.Save();
            _app.Peers?.Apply();
            LoadReceive();
        }
    }

    private bool _disksHooked;

    /// <summary>Estado de cada disco (SMART, según Windows).</summary>
    private void LoadDisks()
    {
        if (_app.Disks is not { } watcher)
        {
            return;
        }

        if (!_disksHooked)
        {
            _disksHooked = true;
            watcher.Changed += LoadDisks;
        }

        DiskList.Children.Clear();
        foreach (var disk in watcher.Disks.OrderBy(d => d.Number))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            (string glyph, Brush brush, string state) = disk.Health switch
            {
                DiskHealthState.Healthy => ("", ThemeManager.SuccessBrush, L.Get("Disk_Healthy")),
                DiskHealthState.Unknown => ("", ThemeManager.TextSecondaryBrush, L.Get("Disk_Unknown")),
                _ => ("", ThemeManager.WarningBrush, disk.Detail.Length > 0 ? disk.Detail : L.Get("Disk_HealthWarning")),
            };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = brush, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = L.F("Disk_Row", disk.Number, disk.Name, state), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            DiskList.Children.Add(row);
        }

        if (DiskList.Children.Count == 0)
        {
            DiskList.Children.Add(new TextBlock { Text = L.Get("Disk_Checking"), FontSize = 12, Foreground = ThemeManager.TextSecondaryBrush });
        }
    }

    private void CheckDisks_Click(object sender, RoutedEventArgs e) => _app.Disks?.Refresh(force: true);

    /// <summary>Los valores medidos de cada disco.</summary>
    private void ShowProfiles()
    {
        var profiles = DeviceTuner.Load();
        TuneResult.Text = profiles.Count == 0
            ? L.Get("Settings_TuneNone")
            : string.Join(Environment.NewLine, profiles.OrderBy(p => p.Key).Select(p =>
                L.F("Settings_TuneProfile", p.Key.Replace("DISK", L.Get("Settings_TuneDisk").TrimEnd() + " "), p.Value.BlockSizeMB, p.Value.QueueDepth, p.Value.MBps)));
    }

    /// <summary>Mide cada disco físico una vez (en la carpeta temporal o en la raíz de la unidad), sin bloquear la ventana.</summary>
    private async void Tune_Click(object sender, RoutedEventArgs e)
    {
        TuneButton.IsEnabled = false;
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var folders = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.AvailableFreeSpace > 2L * 1024 * 1024 * 1024)
            .Select(d => string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase) ? Path.GetTempPath() : d.Name)
            .GroupBy(DeviceTuner.DeviceOf)
            .Select(g => g.First())
            .ToList();
        int done = 0;
        foreach (string folder in folders)
        {
            TuneResult.Text = L.F("Settings_TuneMeasuring", Path.GetPathRoot(folder) ?? folder, ++done, folders.Count);
            await Task.Run(() => DeviceTuner.Measure(folder, App.TestMode ? 64 : 256));
        }

        ShowProfiles();
        TuneButton.IsEnabled = true;
    }

    private bool _liveSyncHooked;

    /// <summary>Sincronización en tiempo real: cada carpeta con su copia, su estado y sus botones.</summary>
    private void LoadLiveSync()
    {
        if (_app.LiveSyncs is not { } service)
        {
            return;
        }

        if (!_liveSyncHooked)
        {
            _liveSyncHooked = true;
            service.StatusChanged += LoadLiveSync;
        }

        LiveSyncList.Children.Clear();
        foreach (var pair in _app.Settings.LiveSyncs.ToList())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var (state, problem) = service.Status(pair);
            row.Children.Add(new FontIcon
            {
                Glyph = problem ? "" : "",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = problem ? ThemeManager.WarningBrush : ThemeManager.AccentBrush,
            });
            string route = $"{pair.Source} → {LiveSyncService.Target(pair)}";
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = route, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = state, FontSize = 12, Foreground = problem ? ThemeManager.WarningBrush : ThemeManager.TextSecondaryBrush, TextWrapping = TextWrapping.Wrap });
            ToolTipService.SetToolTip(text, route);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var captured = pair;
            var mirror = new CheckBox { Content = L.Get("LiveSync_Mirror"), IsChecked = pair.Mirror, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(mirror, L.Get("LiveSync_MirrorTip"));
            mirror.Click += (_, _) =>
            {
                captured.Mirror = mirror.IsChecked == true;
                _app.Settings.Save();
                service.Apply();
            };
            Grid.SetColumn(mirror, 2);
            row.Children.Add(mirror);
            var now = IconButton("", L.F("Settings_LiveSyncNow", pair.Source));
            now.Click += (_, _) => service.SyncNow(captured);
            Grid.SetColumn(now, 3);
            row.Children.Add(now);
            var remove = IconButton("", L.F("Settings_LiveSyncRemove", pair.Source));
            remove.Click += (_, _) =>
            {
                _app.Settings.LiveSyncs.Remove(captured);
                _app.Settings.Save();
                service.Apply();
            };
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);
            LiveSyncList.Children.Add(row);
        }

        if (LiveSyncList.Children.Count == 0)
        {
            LiveSyncList.Children.Add(new TextBlock { Text = L.Get("Settings_LiveSyncEmpty"), Foreground = ThemeManager.TextSecondaryBrush, FontSize = 12 });
        }
    }

    private async void LiveSyncAdd_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is not { } source
            || await Pickers.PickFolderAsync(L.Get("LiveSync_PickTarget")) is not { } destination)
        {
            return;
        }

        AddLiveSync(source, destination);
    }

    /// <summary>Añade una carpeta a sincronizar (también desde las pruebas); si no se puede, dice por qué.</summary>
    internal void AddLiveSync(string source, string destination)
    {
        string? problem = _app.LiveSyncs?.Add(source, destination);
        LiveSyncProblem.Text = problem is null ? "" : L.F("LiveSync_Invalid", problem);
        LiveSyncProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button IconButton(string glyph, string name)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private async void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder && !_app.Settings.FavoriteFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            _app.Settings.FavoriteFolders.Add(folder);
            _app.Settings.Save();
            LoadFavorites();
        }
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
