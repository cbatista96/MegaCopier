using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.App.ViewModels;
using MegaCopier.App.Views;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace MegaCopier.App;

/// <summary>
/// Ventana de MegaCopier. Compacta: el panel de la copia con sus dos barras (total arriba, archivo abajo) y "Más".
/// Completa: barra de herramientas, lista de todos los trabajos a la izquierda y, del elegido, el panel y las pestañas
/// Archivos, Destino, Opciones, Estado y Registro. El paso de una a otra cambia el tamaño de la ventana con animación.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double CompactWidth = 600;
    private const double CompactHeight = 252;
    private const double PagerHeight = 40;
    private const double ExpandedWidth = 1060;
    private const double ExpandedHeight = 720;
    private const double SidebarWidth = 290;

    private readonly App _app;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly FilesTab _filesTab = new();
    private readonly TargetTab _targetTab = new();
    private readonly OptionsTab _optionsTab = new();
    private readonly StatusTab _statusTab = new();
    private readonly LogTab _logTab = new();
    private JobViewModel? _selected;
    private bool _expanded;
    private bool _sidebarOpen = true;

    /// <summary>El usuario eligió compacta o completa en esta sesión: el modo automático ya no cambia por su cuenta.</summary>
    private bool _userChoseMode;

    private (double Width, double Height) _resizeFrom;
    private (double Width, double Height) _resizeTo;
    private long _resizeStart;

    internal MainWindow(App app)
    {
        _app = app;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();

        Handle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Scaled(460);
            presenter.PreferredMinimumHeight = Scaled(250);
        }

        _expanded = app.Settings.WindowMode == WindowMode.Expanded;
        var (width, height) = TargetSize(_expanded);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(width), Scaled(height)));
        CenterOnScreen();

        // Cerrar la ventana no cierra la app: sigue en la bandeja con las copias en marcha.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };

        _app.Queue.JobAdded += job => OnUi(() => AddJob(job, select: true), "AddJob");
        _app.Queue.JobRemoved += job => OnUi(() => RemoveJob(job), "RemoveJob");
        foreach (var job in _app.Queue.Jobs)
        {
            AddJob(job, select: false);
        }

        Panel.ExpandRequested += () => SetExpanded(!_expanded, userChoice: true);
        HistoryView.CloseRequested += ShowJobs;
        SettingsView.CloseRequested += ShowJobs;
        JobList.ItemsSource = Jobs;
        Root.Loaded += (_, _) => NameIconButtons(Root);
        Jobs.CollectionChanged += (_, _) => UpdateLayoutState();
        Tabs.SelectedItem = FilesItem;

        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        _refreshTimer.Tick += (_, _) => RefreshTick();
        _refreshTimer.Start();

        _resizeTimer = _dispatcher.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(12);
        _resizeTimer.Tick += (_, _) => ResizeStep();

        Select(Jobs.FirstOrDefault());
        ApplyLayout(animate: false);
        UpdateFinishButton();
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>Los botones que solo llevan icono toman su tooltip como nombre accesible (lectores de pantalla).</summary>
    internal static void NameIconButtons(DependencyObject parent)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button && ToolTipService.GetToolTip(button) is string tip
                && string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)))
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
            }

            NameIconButtons(child);
        }
    }

    internal nint Handle { get; }

    internal bool IsVisibleToUser => AppWindow.IsVisible && GetForegroundWindow() == Handle;

    internal bool IsExpanded => _expanded;

    /// <summary>
    /// Ejecuta en el hilo de la interfaz. Un error dentro de un callback del dispatcher cierra la app sin pasar por
    /// UnhandledException: se registra en crash.log antes.
    /// </summary>
    private void OnUi(Action action, string what) => _dispatcher.TryEnqueue(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, what);
            throw;
        }
    });

    // ---------------------------------------------------------------------------------------------------------
    // Mostrar y aspecto

    internal void ShowAndActivate()
    {
        // Modo de prueba: mostrar sin quitar el foco a lo que el usuario esté haciendo.
        if (App.TestMode)
        {
            AppWindow.Show(activateWindow: false);
            return;
        }

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

    internal void Hide() => AppWindow.Hide();

    /// <summary>Vuelve a la copia (cierra Historial o Ajustes si estaban abiertos).</summary>
    internal void ShowJobs()
    {
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        UpdateLayoutState();
    }

    /// <summary>Aplica el tema: claro/oscuro de los controles, fondo de la ventana y barra de título.</summary>
    internal void ApplyTheme(ThemeDefinition theme)
    {
        Root.RequestedTheme = theme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        AppWindow.TitleBar.PreferredTheme = theme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        SystemBackdrop = Appearance.CreateBackdrop(theme.Backdrop);
        TrayIcon.SetMenuTheme(theme.Dark);
    }

    internal void ApplyAlwaysOnTop(bool onTop)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = onTop;
        }
    }

    /// <summary>Ya no queda nada en marcha: según la preferencia, la ventana se cierra o se queda.</summary>
    internal void OnAllFinished()
    {
        var mode = _app.Settings.FinishWindow;
        bool problems = Jobs.Any(j => j.HasProblem || j.IsInterrupted);
        if (mode == FinishWindow.KeepOpen || (mode == FinishWindow.Auto && problems) || HistoryView.Visibility == Visibility.Visible
            || SettingsView.Visibility == Visibility.Visible)
        {
            return;
        }

        // Como Ultracopier: al terminar bien, la ventana se va y las copias acabadas pasan al historial.
        foreach (var job in Jobs.Where(j => j.IsDone).ToList())
        {
            job.Clear();
        }

        Hide();
        _userChoseMode = false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Trabajos

    private void AddJob(CopyJob job, bool select)
    {
        if (Jobs.Any(j => j.Job == job))
        {
            return;
        }

        // Lo más reciente arriba.
        var item = new JobViewModel(job, _app.Queue, _dispatcher, () => _app.Settings.HideFinishedFiles);
        Jobs.Insert(0, item);
        if (select || _selected is null)
        {
            Select(item);
        }

        // Modo automático: con dos copias a la vez se pasa a la vista completa.
        if (_app.Settings.WindowMode == WindowMode.Auto && !_userChoseMode && !_expanded && Jobs.Count(j => j.IsActive) > 1)
        {
            SetExpanded(true, userChoice: false);
        }
    }

    private void RemoveJob(CopyJob job)
    {
        var item = Jobs.FirstOrDefault(j => j.Job == job);
        if (item is null)
        {
            return;
        }

        int index = Jobs.IndexOf(item);
        Jobs.Remove(item);
        if (_selected == item)
        {
            Select(Jobs.Count == 0 ? null : Jobs[Math.Min(index, Jobs.Count - 1)]);
        }
    }

    private void Select(JobViewModel? job)
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = job;
        if (job is not null)
        {
            job.IsSelected = true;
        }

        Panel.Job = job;
        _filesTab.Job = job;
        _targetTab.Job = job;
        _optionsTab.Job = job;
        _statusTab.Job = job;
        _logTab.Job = job;
        if (!ReferenceEquals(JobList.SelectedItem, job))
        {
            JobList.SelectedItem = job;
        }

        RefreshTabs();
        UpdatePager();
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JobList.SelectedItem is JobViewModel job && job != _selected)
        {
            Select(job);
        }
    }

    private void PagerPrevious_Click(object sender, RoutedEventArgs e) => Page(-1);

    private void PagerNext_Click(object sender, RoutedEventArgs e) => Page(1);

    private void Page(int delta)
    {
        if (Jobs.Count == 0)
        {
            return;
        }

        int index = _selected is null ? 0 : Jobs.IndexOf(_selected);
        Select(Jobs[(index + delta + Jobs.Count) % Jobs.Count]);
    }

    private void UpdatePager()
    {
        PagerText.Text = _selected is null ? "" : L.F("Pager_Text", Jobs.IndexOf(_selected) + 1, Jobs.Count);
        var pager = !_expanded && Jobs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (Pager.Visibility != pager)
        {
            Pager.Visibility = pager;
            if (!_expanded && AppWindow.IsVisible)
            {
                AnimateResize();
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Compacta / completa

    private void SetExpanded(bool expanded, bool userChoice)
    {
        _userChoseMode |= userChoice;
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        ApplyLayout(animate: AppWindow.IsVisible);
    }

    private void ApplyLayout(bool animate)
    {
        Panel.SetExpanded(_expanded);
        Toolbar.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        TabHost.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        bool sidebar = _expanded && _sidebarOpen;
        Sidebar.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(sidebar ? SidebarWidth : 0);
        UpdatePager();
        UpdateLayoutState();
        RefreshTabs();

        var (width, height) = TargetSize(_expanded);
        if (!animate)
        {
            _resizeTimer.Stop();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(width), Scaled(height)));
            return;
        }

        AnimateResize();
    }

    /// <summary>Animación del tamaño (con aceleración suave), manteniendo la ventana dentro de la pantalla.</summary>
    private void AnimateResize()
    {
        var (width, height) = TargetSize(_expanded);
        AnimateResizeTo(width, height);
    }

    private void AnimateResizeTo(double width, double height)
    {
        _resizeFrom = (AppWindow.Size.Width, AppWindow.Size.Height);
        _resizeTo = (Scaled(width), Scaled(height));
        _resizeStart = Stopwatch.GetTimestamp();
        _resizeTimer.Start();
    }

    private void ResizeStep()
    {
        double t = Math.Min(1, Stopwatch.GetElapsedTime(_resizeStart).TotalMilliseconds / 220);
        double eased = 1 - Math.Pow(1 - t, 3);
        int w = (int)(_resizeFrom.Width + ((_resizeTo.Width - _resizeFrom.Width) * eased));
        int h = (int)(_resizeFrom.Height + ((_resizeTo.Height - _resizeFrom.Height) * eased));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int x = Math.Clamp(AppWindow.Position.X, area.X, Math.Max(area.X, area.X + area.Width - w));
        int y = Math.Clamp(AppWindow.Position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        if (t >= 1)
        {
            _resizeTimer.Stop();
        }
    }

    private (double Width, double Height) TargetSize(bool expanded) =>
        expanded ? (ExpandedWidth, ExpandedHeight) : (CompactWidth, CompactHeight + (Jobs.Count > 1 ? PagerHeight : 0));

    private void Sidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarOpen = !_sidebarOpen;
        ApplyLayout(animate: false);
        var (width, height) = TargetSize(true);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(AppWindow.Size.Width, Scaled(width - (_sidebarOpen ? 0 : SidebarWidth))), Math.Max(AppWindow.Size.Height, Scaled(height))));
    }

    /// <summary>Sin copias, con copias, o una página (Historial, Ajustes) encima.</summary>
    private void UpdateLayoutState()
    {
        bool page = HistoryView.Visibility == Visibility.Visible || SettingsView.Visibility == Visibility.Visible;
        bool empty = Jobs.Count == 0;
        EmptyState.Visibility = !page && empty ? Visibility.Visible : Visibility.Collapsed;
        Detail.Visibility = !page && !empty ? Visibility.Visible : Visibility.Collapsed;
        Sidebar.Visibility = !page && _expanded && _sidebarOpen ? Visibility.Visible : Visibility.Collapsed;
        JobsHeader.Text = L.F("Jobs_Header", Jobs.Count, Jobs.Count(j => j.IsActive));
        UpdatePager();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Pestañas

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        TabHost.Content = sender.SelectedItem switch
        {
            var i when i == TargetItem => _targetTab,
            var i when i == OptionsItem => _optionsTab,
            var i when i == StatusItem => _statusTab,
            var i when i == LogItem => _logTab,
            _ => _filesTab,
        };
        RefreshTabs();
    }

    private void RefreshTabs()
    {
        if (!_expanded)
        {
            return;
        }

        switch (TabHost.Content)
        {
            case FilesTab files:
                files.Refresh();
                break;
            case TargetTab target:
                target.Refresh();
                break;
            case OptionsTab options:
                options.Refresh();
                break;
            case StatusTab status:
                status.Refresh();
                break;
        }
    }

    /// <summary>Cada segundo: barra de título, cabecera de la lista y la pestaña visible.</summary>
    private void RefreshTick()
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }

        var running = Jobs.Where(j => j.IsRunning || j.IsPaused).ToList();
        if (running.Count == 0)
        {
            TitleStatus.Text = _selected?.StateText ?? "";
            Title = "MegaCopier";
        }
        else
        {
            long done = running.Sum(j => j.Job.Progress?.CompletedBytes ?? 0);
            long total = running.Sum(j => j.Job.Progress?.TotalBytes ?? 0);
            double speed = running.Where(j => j.IsRunning).Sum(j => j.Job.Progress?.BytesPerSecond ?? 0);
            string percent = total > 0 ? $"{100.0 * done / total:0} %" : "…";
            string status = speed > 0 ? $"{percent} · {Format.Speed(speed)}" : percent;
            TitleStatus.Text = running.Count > 1 ? $"{status} · {L.F("Tray_ManyJobs", running.Count)}" : status;
            Title = $"{percent} — MegaCopier";
        }

        JobsHeader.Text = L.F("Jobs_Header", Jobs.Count, Jobs.Count(j => j.IsActive));
        if (TabHost.Content is StatusTab or TargetTab)
        {
            RefreshTabs();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Barra de herramientas

    private async void CopyFiles_Click(object sender, RoutedEventArgs e)
    {
        var files = await Pickers.PickFilesAsync();
        if (files.Count > 0)
        {
            await PickDestinationAndCopy(files, move: false);
        }
    }

    private async void MoveFiles_Click(object sender, RoutedEventArgs e)
    {
        var files = await Pickers.PickFilesAsync();
        if (files.Count > 0)
        {
            await PickDestinationAndCopy(files, move: true);
        }
    }

    private async void CopyFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            await PickDestinationAndCopy([folder], move: false);
        }
    }

    private async Task PickDestinationAndCopy(IReadOnlyList<string> sources, bool move)
    {
        if (await Pickers.PickFolderAsync(L.Get(move ? "Picker_MoveHere" : "Picker_CopyHere")) is not { } destination)
        {
            return;
        }

        Submit(sources, destination, move);
    }

    private void Submit(IReadOnlyList<string> sources, string destination, bool move)
    {
        try
        {
            _app.Submit(new IpcRequest { Command = "copy", Sources = [.. sources], Destination = destination, Move = move ? true : null });
            ShowJobs();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _app.Notify(L.Get("Notify_ProblemTitle"), ex.Message);
        }
    }

    /// <summary>Carga una lista guardada (.mclist) y la copia de nuevo a su destino.</summary>
    private async void LoadList_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(".mclist") is not { } path)
        {
            return;
        }

        if (Reports.LoadList(path) is { } list && list.Sources.Count > 0)
        {
            string? destination = Path.IsPathFullyQualified(list.Destination)
                ? list.Destination
                : await Pickers.PickFolderAsync(L.Get("Picker_CopyHere"));
            if (destination is not null)
            {
                Submit(list.Sources, destination, list.Move);
            }
        }
        else
        {
            _app.Notify(L.Get("Files_LoadFailed"), path);
        }
    }

    private void PauseAll_Click(object sender, RoutedEventArgs e) => _app.Queue.PauseAll();

    private void ResumeAll_Click(object sender, RoutedEventArgs e) => _app.Queue.ResumeAll();

    private void ClearDone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in Jobs.Where(j => j.CanClear).ToList())
        {
            job.Clear();
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        HistoryView.Reload();
        HistoryView.Visibility = Visibility.Visible;
        EnsureRoomForPage();
        UpdateLayoutState();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Load();
        SettingsView.Visibility = Visibility.Visible;
        EnsureRoomForPage();
        UpdateLayoutState();
    }

    /// <summary>Historial y Ajustes necesitan la ventana grande.</summary>
    private void EnsureRoomForPage()
    {
        if (!_expanded)
        {
            SetExpanded(true, userChoice: false);
        }
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        AboutVersion.Text = L.F("About", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "");
        AboutDialog.XamlRoot = Root.XamlRoot;
        AboutDialog.RequestedTheme = Root.RequestedTheme;
        await AboutDialog.ShowAsync();
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(AppSettings.FilePath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------------------------------------------------
    // Arrastrar y soltar: soltar archivos en la ventana pide la carpeta de destino.

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = L.Get("Drop_Caption");
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        List<string> paths;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }
        finally
        {
            deferral.Complete();
        }

        if (paths.Count > 0)
        {
            await PickDestinationAndCopy(paths, move: false);
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
                    Select(Jobs.FirstOrDefault(j => j.Job == job) ?? _selected);
                    ShowAndActivate();
                    FillConflictDialog(conflict, job.Record.Move);
                    ConflictDialog.XamlRoot = Root.XamlRoot;
                    ConflictDialog.RequestedTheme = Root.RequestedTheme;
                    bool grow = !_expanded;
                    if (grow)
                    {
                        AnimateResizeTo(CompactWidth + 40, 500);
                    }

                    using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(ConflictDialog.Hide));
                    var result = await ConflictDialog.ShowAsync();
                    if (grow)
                    {
                        AnimateResize();
                    }

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
        string name = Path.GetFileName(conflict.Destination);
        string folder = Path.GetDirectoryName(conflict.Destination) ?? "";
        bool sourceNewer = conflict.SourceLastWriteUtc > conflict.DestinationLastWriteUtc;
        bool destinationNewer = conflict.DestinationLastWriteUtc > conflict.SourceLastWriteUtc;

        ConflictMessage.Text = L.F("Conflict_Message", name, Format.ShortPath(folder, 3));
        ToolTipService.SetToolTip(ConflictMessage, conflict.Destination);
        ConflictSourceTitle.Text = L.Get(move ? "Conflict_SourceMove" : "Conflict_SourceCopy");
        ConflictSourceSize.Text = Format.Bytes(conflict.SourceLength);
        ConflictSourceDate.Text = Format.Date(conflict.SourceLastWriteUtc) + (sourceNewer ? L.Get("Conflict_Newer") : "");
        ConflictDestinationSize.Text = Format.Bytes(conflict.DestinationLength);
        ConflictDestinationDate.Text = Format.Date(conflict.DestinationLastWriteUtc) + (destinationNewer ? L.Get("Conflict_Newer") : "");
        ConflictApplyAll.IsChecked = false;
    }

    // ---------------------------------------------------------------------------------------------------------

    private int Scaled(double value) => (int)(value * GetDpiForWindow(Handle) / 96.0);

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
