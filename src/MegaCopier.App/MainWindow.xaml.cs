using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using MegaCopier.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace MegaCopier.App;

public sealed partial class MainWindow : Window
{
    private readonly App _app;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly string _startupLanguage;
    private readonly string _startupAccent;
    private bool _loadingSettings;

    internal MainWindow(App app)
    {
        _app = app;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _startupLanguage = app.Settings.Language;
        _startupAccent = app.Settings.Accent;
        InitializeComponent();

        Handle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        double scale = GetDpiForWindow(Handle) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(720 * scale), (int)(620 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(480 * scale);
            presenter.PreferredMinimumHeight = (int)(360 * scale);
        }

        CenterOnScreen();

        // Cerrar la ventana no cierra la app: sigue en la bandeja con las copias en marcha.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };

        _app.Queue.JobAdded += job => _dispatcher.TryEnqueue(() => AddJob(job));
        _app.Queue.JobRemoved += job => _dispatcher.TryEnqueue(() => RemoveJob(job));
        foreach (var job in _app.Queue.Jobs)
        {
            AddJob(job);
        }

        Jobs.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
        LoadSettings();
        ApplyTheme(_app.Settings.Theme);
        ApplyBackdrop(_app.Settings.Backdrop);
        ApplyAlwaysOnTop(_app.Settings.AlwaysOnTop);
        UpdateFinishButton();
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    internal nint Handle { get; }

    internal bool IsVisibleToUser => AppWindow.IsVisible && GetForegroundWindow() == Handle;

    internal void ShowAndActivate()
    {
        AppWindow.Show(activateWindow: true);
        Activate();
        if (SetForegroundWindow(Handle))
        {
            return;
        }

        // Tras un Ctrl+V interceptado nadie nos cedió el primer plano: se comparte un instante la cola de
        // entrada con la ventana activa (el Explorador) para poder pasar al frente en lugar de solo parpadear.
        nint foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint ownThread = GetCurrentThreadId();
        if (foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true))
        {
            SetForegroundWindow(Handle);
            BringWindowToTop(Handle);
            AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    internal void ShowJobs() => Sections.SelectedItem = JobsSection;

    internal void ApplyTheme(AppTheme theme)
    {
        Root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
        TrayIcon.SetMenuTheme(theme switch { AppTheme.Dark => true, AppTheme.Light => false, _ => null });
    }

    private void ApplyBackdrop(AppBackdrop backdrop)
    {
        SystemBackdrop = Appearance.CreateBackdrop(backdrop);
        SolidBackground.Visibility = backdrop == AppBackdrop.Solid ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyAlwaysOnTop(bool onTop)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = onTop;
        }
    }

    private void AddJob(CopyJob job)
    {
        if (Jobs.Any(j => j.Job == job))
        {
            return;
        }

        // Lo más reciente arriba.
        Jobs.Insert(0, new JobViewModel(job, _app.Queue, _dispatcher));
    }

    private void RemoveJob(CopyJob job)
    {
        var item = Jobs.FirstOrDefault(j => j.Job == job);
        if (item is not null)
        {
            Jobs.Remove(item);
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobsScroll.Visibility = Jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Sections_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool settings = sender.SelectedItem == SettingsSection;
        JobsView.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (settings)
        {
            _loadingSettings = true;
            StartupToggle.IsOn = StartupRegistration.IsEnabled;
            ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
            _loadingSettings = false;
        }
    }

    private async void CopyFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(AppWindow.Id) { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
        {
            await PickDestinationAndCopy(files.Select(f => f.Path).ToList());
        }
    }

    private async void CopyFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await new FolderPicker(AppWindow.Id).PickSingleFolderAsync();
        if (folder is not null)
        {
            await PickDestinationAndCopy([folder.Path]);
        }
    }

    private async Task PickDestinationAndCopy(List<string> sources)
    {
        var destination = await new FolderPicker(AppWindow.Id) { CommitButtonText = L.Get("Picker_CopyHere") }.PickSingleFolderAsync();
        if (destination is null)
        {
            return;
        }

        _app.Submit(new Engine.Ipc.IpcRequest { Command = "copy", Sources = sources, Destination = destination.Path });
        ShowJobs();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Ajustes

    private void LoadSettings()
    {
        _loadingSettings = true;
        var settings = _app.Settings;
        ThemeBox.SelectedIndex = (int)settings.Theme;
        BackdropBox.SelectedIndex = (int)settings.Backdrop;
        OnTopToggle.IsOn = settings.AlwaysOnTop;
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Language) ?? LanguageBox.Items[0];

        AccentBox.Items.Clear();
        AccentBox.Items.Add(AccentItem("system", L.Get("Accent_System"), null));
        foreach (var (key, hex) in Appearance.AccentPresets)
        {
            Appearance.TryParse(hex, out var color);
            AccentBox.Items.Add(AccentItem(hex, L.Get("Accent_" + key), color));
        }

        AccentBox.SelectedItem = AccentBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, settings.Accent, StringComparison.OrdinalIgnoreCase))
            ?? AccentBox.Items[0];

        VerifyToggle.IsOn = settings.VerifyAfterCopy;
        ConflictBox.SelectedIndex = settings.Conflict switch
        {
            ConflictPolicy.Overwrite => 1,
            ConflictPolicy.Skip => 2,
            ConflictPolicy.OverwriteIfNewer => 3,
            ConflictPolicy.KeepBoth => 4,
            _ => 0,
        };
        SpeedBox.SelectedItem = SpeedBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.SpeedLimitMBps.ToString())
            ?? SpeedBox.Items[0];
        AutoResumeToggle.IsOn = settings.AutoResumeInterrupted;
        NotifyToggle.IsOn = settings.NotifyOnFinish;
        StartupToggle.IsOn = StartupRegistration.IsEnabled;
        PasteToggle.IsOn = settings.InterceptExplorerPaste;
        ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
        ShellMenuToggle.IsEnabled = App.ShellExtensionPath is not null;
        AboutText.Text = L.F("About", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "");
        _loadingSettings = false;
    }

    private static ComboBoxItem AccentItem(string tag, string name, Windows.UI.Color? color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (color is { } c)
        {
            row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14 });
        }

        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new ComboBoxItem { Content = row, Tag = tag };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, name); // lectores de pantalla
        return item;
    }

    private void SaveSettings()
    {
        if (!_loadingSettings)
        {
            _app.Settings.Save();
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeBox.SelectedIndex < 0)
        {
            return;
        }

        _app.Settings.Theme = (AppTheme)ThemeBox.SelectedIndex;
        ApplyTheme(_app.Settings.Theme);
        SaveSettings();
    }

    private void AccentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || AccentBox.SelectedItem is not ComboBoxItem { Tag: string accent })
        {
            return;
        }

        // WinUI calcula los pinceles de acento al crear los controles: el color nuevo se aplica al reiniciar.
        _app.Settings.Accent = accent;
        SaveSettings();
        UpdateRestartInfo();
    }

    private void UpdateRestartInfo() =>
        RestartInfo.IsOpen = _app.Settings.Language != _startupLanguage
            || !string.Equals(_app.Settings.Accent, _startupAccent, StringComparison.OrdinalIgnoreCase);

    private void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || BackdropBox.SelectedIndex < 0)
        {
            return;
        }

        _app.Settings.Backdrop = (AppBackdrop)BackdropBox.SelectedIndex;
        ApplyBackdrop(_app.Settings.Backdrop);
        SaveSettings();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language })
        {
            return;
        }

        _app.Settings.Language = language;
        SaveSettings();
        LanguageSetting.Save(language);
        UpdateRestartInfo();
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => _app.Restart();

    private void OnTopToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _app.Settings.AlwaysOnTop = OnTopToggle.IsOn;
        ApplyAlwaysOnTop(OnTopToggle.IsOn);
        SaveSettings();
    }

    private void VerifyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _app.Settings.VerifyAfterCopy = VerifyToggle.IsOn;
        SaveSettings();
    }

    private void ConflictBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _app.Settings.Conflict = ConflictBox.SelectedIndex switch
        {
            1 => ConflictPolicy.Overwrite,
            2 => ConflictPolicy.Skip,
            3 => ConflictPolicy.OverwriteIfNewer,
            4 => ConflictPolicy.KeepBoth,
            _ => ConflictPolicy.Ask,
        };
        SaveSettings();
    }

    private void SpeedBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int limit))
        {
            _app.Settings.SpeedLimitMBps = limit;
            SaveSettings();
        }
    }

    private void AutoResumeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _app.Settings.AutoResumeInterrupted = AutoResumeToggle.IsOn;
        SaveSettings();
    }

    private void NotifyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _app.Settings.NotifyOnFinish = NotifyToggle.IsOn;
        SaveSettings();
    }

    private void PasteToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        _app.Settings.InterceptExplorerPaste = PasteToggle.IsOn;
        _app.SetExplorerPaste(PasteToggle.IsOn);
        SaveSettings();
    }

    private void ShellMenuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || ShellMenuToggle.IsOn == ShellExtensionRegistration.IsRegistered)
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
            _loadingSettings = true;
            ShellMenuToggle.IsOn = ShellExtensionRegistration.IsRegistered;
            _loadingSettings = false;
        }
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings && StartupToggle.IsOn != StartupRegistration.IsEnabled)
        {
            StartupRegistration.Set(StartupToggle.IsOn);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Al terminar

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _app.FinishAction = ReferenceEquals(sender, FinishSleep) ? FinishAction.Sleep
            : ReferenceEquals(sender, FinishShutdown) ? FinishAction.Shutdown
            : FinishAction.None;
        UpdateFinishButton();
    }

    internal void UpdateFinishButton()
    {
        FinishNothing.IsChecked = _app.FinishAction == FinishAction.None;
        FinishSleep.IsChecked = _app.FinishAction == FinishAction.Sleep;
        FinishShutdown.IsChecked = _app.FinishAction == FinishAction.Shutdown;
        (FinishIcon.Glyph, FinishText.Text) = _app.FinishAction switch
        {
            FinishAction.Sleep => ("", L.Get("Finish_LabelSleep")),
            FinishAction.Shutdown => ("", L.Get("Finish_LabelShutdown")),
            _ => ("", L.Get("Finish_Label")),
        };
    }

    // ---------------------------------------------------------------------------------------------------------
    // Conflictos

    /// <summary>
    /// Pregunta qué hacer con un archivo que ya existe. Se llama desde el hilo de copia y lo bloquea hasta que el
    /// usuario responde; los diálogos de varias copias se muestran de uno en uno.
    /// </summary>
    internal ConflictAnswer AskConflict(CopyJob job, ConflictInfo conflict, CancellationToken cancellation)
    {
        _dialogGate.Wait(cancellation);
        try
        {
            var answer = new TaskCompletionSource<ConflictAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    ShowJobs();
                    ShowAndActivate();
                    FillConflictDialog(conflict, job.Record.Move);
                    ConflictDialog.XamlRoot = Root.XamlRoot;
                    ConflictDialog.RequestedTheme = Root.RequestedTheme;
                    using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(ConflictDialog.Hide));
                    var result = await ConflictDialog.ShowAsync();
                    var decision = result switch
                    {
                        ContentDialogResult.Primary => ConflictDecision.Replace,
                        ContentDialogResult.Secondary => ConflictDecision.KeepBoth,
                        _ => ConflictDecision.Skip,
                    };
                    answer.TrySetResult(new ConflictAnswer(decision, ConflictApplyAll.IsChecked == true));
                }
                catch (Exception ex)
                {
                    answer.TrySetException(ex);
                }
            });

            return answer.Task.WaitAsync(cancellation).GetAwaiter().GetResult();
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    private void FillConflictDialog(ConflictInfo conflict, bool move)
    {
        string name = System.IO.Path.GetFileName(conflict.Destination);
        string folder = System.IO.Path.GetDirectoryName(conflict.Destination) ?? "";
        bool sourceNewer = conflict.SourceLastWriteUtc > conflict.DestinationLastWriteUtc;
        bool destinationNewer = conflict.DestinationLastWriteUtc > conflict.SourceLastWriteUtc;

        ConflictMessage.Text = L.F("Conflict_Message", name, folder);
        ConflictSourceTitle.Text = L.Get(move ? "Conflict_SourceMove" : "Conflict_SourceCopy");
        ConflictSourceSize.Text = Format.Bytes(conflict.SourceLength);
        ConflictSourceDate.Text = Format.Date(conflict.SourceLastWriteUtc) + (sourceNewer ? L.Get("Conflict_Newer") : "");
        ConflictDestinationSize.Text = Format.Bytes(conflict.DestinationLength);
        ConflictDestinationDate.Text = Format.Date(conflict.DestinationLastWriteUtc) + (destinationNewer ? L.Get("Conflict_Newer") : "");
        ConflictApplyAll.IsChecked = false;
    }

    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            area.X + ((area.Width - AppWindow.Size.Width) / 2),
            area.Y + ((area.Height - AppWindow.Size.Height) / 2)));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);
}
