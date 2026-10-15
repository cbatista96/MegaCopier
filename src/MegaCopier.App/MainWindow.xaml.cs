using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace MegaCopier.App;

public sealed partial class MainWindow : Window
{
    private readonly App _app;
    private readonly DispatcherQueue _dispatcher;
    private bool _loadingSettings;

    internal MainWindow(App app)
    {
        _app = app;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();

        Handle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));
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
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    internal nint Handle { get; }

    internal bool IsVisibleToUser => AppWindow.IsVisible && GetForegroundWindow() == Handle;

    internal void ShowAndActivate()
    {
        AppWindow.Show(activateWindow: true);
        Activate();
        SetForegroundWindow(Handle);
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
            StartupToggle.IsOn = StartupRegistration.IsEnabled;
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
        var destination = await new FolderPicker(AppWindow.Id) { CommitButtonText = "Copiar aquí" }.PickSingleFolderAsync();
        if (destination is null)
        {
            return;
        }

        _app.Submit(new Engine.Ipc.IpcRequest { Command = "copy", Sources = sources, Destination = destination.Path });
        ShowJobs();
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        var settings = _app.Settings;
        ThemeBox.SelectedIndex = (int)settings.Theme;
        VerifyToggle.IsOn = settings.VerifyAfterCopy;
        ConflictBox.SelectedIndex = settings.Conflict switch
        {
            ConflictPolicy.Skip => 1,
            ConflictPolicy.OverwriteIfNewer => 2,
            ConflictPolicy.KeepBoth => 3,
            _ => 0,
        };
        AutoResumeToggle.IsOn = settings.AutoResumeInterrupted;
        NotifyToggle.IsOn = settings.NotifyOnFinish;
        StartupToggle.IsOn = StartupRegistration.IsEnabled;
        AboutText.Text = $"MegaCopier {typeof(App).Assembly.GetName().Version?.ToString(3)} · motor con reanudación tras apagón";
        _loadingSettings = false;
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

    private void VerifyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _app.Settings.VerifyAfterCopy = VerifyToggle.IsOn;
        SaveSettings();
    }

    private void ConflictBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _app.Settings.Conflict = ConflictBox.SelectedIndex switch
        {
            1 => ConflictPolicy.Skip,
            2 => ConflictPolicy.OverwriteIfNewer,
            3 => ConflictPolicy.KeepBoth,
            _ => ConflictPolicy.Overwrite,
        };
        SaveSettings();
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

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings && StartupToggle.IsOn != StartupRegistration.IsEnabled)
        {
            StartupRegistration.Set(StartupToggle.IsOn);
        }
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
}
