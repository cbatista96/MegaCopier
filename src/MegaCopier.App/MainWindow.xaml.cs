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
using Microsoft.UI.Xaml.Media;
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
    private const double NarrowWidth = 900;
    private bool _narrow;
    private bool _sidebarWide = true;
    private bool _sidebarOverlayOpen;

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
        _app.Queue.JobsReordered += () => OnUi(SyncJobOrder, "SyncJobOrder");
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

    /// <summary>Cuenta atrás antes de apagar, suspender... Devuelve false si el usuario la cancela.</summary>
    internal async Task<bool> ConfirmFinishAsync(FinishAction action)
    {
        const int Seconds = 30;
        ShowAndActivate();
        ShowJobs();
        int left = Seconds;
        bool timedOut = false;
        void Update()
        {
            FinishMessage.Text = L.F("Finish_Countdown" + action, left);
            FinishProgress.Value = 100.0 * left / Seconds;
        }

        Update();
        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            left--;
            Update();
            if (left <= 0)
            {
                timedOut = true;
                FinishDialog.Hide();
            }
        };
        timer.Start();
        FinishDialog.XamlRoot = Root.XamlRoot;
        FinishDialog.RequestedTheme = Root.RequestedTheme;
        try
        {
            var result = await FinishDialog.ShowAsync();
            return timedOut || result == ContentDialogResult.Primary;
        }
        finally
        {
            timer.Stop();
        }
    }

    /// <summary>Vuelve a la copia (cierra Historial o Ajustes si estaban abiertos).</summary>
    internal void ShowJobs()
    {
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        UpdateLayoutState();
    }

    /// <summary>Aplica el tema: claro/oscuro de los controles, fondo de la ventana y barra de título.</summary>
    internal void ApplyTheme(ThemeDefinition theme, bool reloadResources = false)
    {
        var requested = theme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        if (reloadResources && Root.RequestedTheme == requested)
        {
            // Cambiar de modo y volver obliga a todos los controles a releer sus recursos (el acento nuevo).
            Root.RequestedTheme = requested == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        }

        Root.RequestedTheme = requested;
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

        // En el orden de la cola: arrastrando las tarjetas se cambia qué copia va antes.
        var item = new JobViewModel(job, _app.Queue, _dispatcher, () => _app.Settings.HideFinishedFiles);
        Jobs.Add(item);
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
            if (_narrow && _sidebarOverlayOpen)
            {
                _sidebarOverlayOpen = false;
                ApplySidebar();
            }
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
        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            // La ventana no puede encogerse tanto que tape el paginador o los botones.
            overlapped.PreferredMinimumHeight = Scaled(_expanded ? 480 : CompactHeight + (pager == Visibility.Visible ? PagerHeight : 0));
        }

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
        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            // La vista completa necesita algo más de ancho que la compacta para no apretar sus pestañas.
            overlapped.PreferredMinimumWidth = Scaled(_expanded ? 640 : 460);
        }

        Panel.SetExpanded(_expanded);
        Toolbar.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        TabHost.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        ApplySidebar();
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
        if (_narrow)
        {
            _sidebarOverlayOpen = !_sidebarOverlayOpen;
        }
        else
        {
            _sidebarWide = !_sidebarWide;
        }

        ApplySidebar();
    }

    /// <summary>Con la ventana estrecha la lista de copias flota sobre el contenido en lugar de apretarlo.</summary>
    private void Body_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < NarrowWidth;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            _sidebarOverlayOpen = false;
            ApplySidebar();
        }
    }

    private void ApplySidebar()
    {
        bool page = HistoryView.Visibility == Visibility.Visible || SettingsView.Visibility == Visibility.Visible;
        bool show = _expanded && !page && (_narrow ? _sidebarOverlayOpen : _sidebarWide);
        Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_narrow)
        {
            SidebarColumn.Width = new GridLength(0);
            Grid.SetColumnSpan(Sidebar, 2);
            Sidebar.Width = SidebarWidth;
            Sidebar.HorizontalAlignment = HorizontalAlignment.Left;
            Sidebar.Background = new AcrylicBrush
            {
                TintColor = ThemeManager.Current.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 36) : Windows.UI.Color.FromArgb(255, 243, 243, 243),
                TintOpacity = 0.85,
                FallbackColor = ThemeManager.Current.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 36) : Windows.UI.Color.FromArgb(255, 243, 243, 243),
            };
            Sidebar.CornerRadius = new CornerRadius(0, 8, 8, 0);
            Canvas.SetZIndex(Sidebar, 10);
            Sidebar.Shadow ??= new ThemeShadow();
            Sidebar.Translation = new System.Numerics.Vector3(0, 0, 32);
        }
        else
        {
            SidebarColumn.Width = new GridLength(show ? SidebarWidth : 0);
            Grid.SetColumnSpan(Sidebar, 1);
            Sidebar.Width = double.NaN;
            Sidebar.HorizontalAlignment = HorizontalAlignment.Stretch;
            Sidebar.Background = null;
            Sidebar.CornerRadius = new CornerRadius(0);
            Canvas.SetZIndex(Sidebar, 0);
            Sidebar.Translation = default;
        }
    }

    /// <summary>Tocar fuera de la lista flotante la cierra.</summary>
    private void Detail_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_narrow && _sidebarOverlayOpen)
        {
            _sidebarOverlayOpen = false;
            ApplySidebar();
        }
    }

    /// <summary>Sin copias, con copias, o una página (Historial, Ajustes) encima.</summary>
    private void UpdateLayoutState()
    {
        bool page = HistoryView.Visibility == Visibility.Visible || SettingsView.Visibility == Visibility.Visible;
        bool empty = Jobs.Count == 0;
        EmptyState.Visibility = !page && empty ? Visibility.Visible : Visibility.Collapsed;
        Detail.Visibility = !page && !empty ? Visibility.Visible : Visibility.Collapsed;
        ApplySidebar();
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

        if (TextAudit.Enabled)
        {
            string where = HistoryView.Visibility == Visibility.Visible ? "Historial"
                : SettingsView.Visibility == Visibility.Visible ? "Ajustes"
                : _expanded ? "Completa/" + (Tabs.SelectedItem as SelectorBarItem)?.Text : "Compacta";
            TextAudit.Run(Root, $"{where} {AppWindow.Size.Width}px");
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

    /// <summary>Carga una lista guardada (.mclist) y la copia de nuevo a su destino.</summary>
    private async void LoadList_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFileAsync(".mclist") is not { } path)
        {
            return;
        }

        // Con todas las opciones guardadas (modo, filtros, verificar...); si la lista no trae destino, se pide.
        string? destination = Reports.LoadList(path) is { } list && !Path.IsPathFullyQualified(list.Destination)
            ? await Pickers.PickFolderAsync(L.Get("Picker_CopyHere"))
            : null;
        if (_app.RunList(path, destination) is not null)
        {
            ShowJobs();
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

    /// <summary>
    /// Solo pruebas (MEGACOPIER_TEST=1): ejecuta la misma lógica que un arrastre real sobre la lista de archivos
    /// ("files") o de copias ("jobs"), sin ratón: <paramref name="drop"/> = false muestra la línea y el aviso.
    /// </summary>
    internal string DebugDrag(string target, double y, int[] rows, List<string> paths, bool drop)
    {
        if (target == "files")
        {
            if (drop)
            {
                _filesTab.SimulateDrop(y, rows, paths);
                return "ok";
            }

            return _filesTab.SimulateDragOver(y, rows.Length > 0);
        }

        var spot = DropTarget.Find(JobList, new Windows.Foundation.Point(40, y), JobDropLayer);
        if (rows.Length > 0)
        {
            if (!drop)
            {
                DropTarget.ShowLine(JobDropLine, spot.LineY, 2, JobDropLayer.ActualWidth - 4);
                return L.F("Drop_JobOrder", spot.Index + 1);
            }

            HideJobDropFeedback();
            var dragged = Jobs[rows[0]];
            var before = spot.Index < Jobs.Count ? Jobs[spot.Index] : null;
            if (before != dragged)
            {
                _app.Queue.MoveJob(dragged.Job, before?.Job);
            }

            return "ok";
        }

        if (spot.OverIndex < 0)
        {
            return "fuera";
        }

        if (!drop)
        {
            DropTarget.ShowBox(JobDropBox, spot.OverBounds);
            return L.F("Drop_AddToNamedJob", Jobs[spot.OverIndex].Title);
        }

        HideJobDropFeedback();
        _app.Queue.AddToJob(Jobs[spot.OverIndex].Job, paths);
        return "ok";
    }

    // ---------------------------------------------------------------------------------------------------------
    // Arrastrar en la lista de copias: reordenar la cola y soltar archivos sobre la copia que se quiera.

    private JobViewModel? _draggedJob;

    private void JobList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedJob = e.Items.OfType<JobViewModel>().FirstOrDefault();
        e.Data.SetText("megacopier-job");
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void JobList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _draggedJob = null;
        HideJobDropFeedback();
    }

    private void JobList_DragOver(object sender, DragEventArgs e)
    {
        bool files = e.DataView.Contains(StandardDataFormats.StorageItems);
        if (_draggedJob is null && !files)
        {
            return;
        }

        var spot = DropTarget.Find(JobList, e.GetPosition(JobList), JobDropLayer);
        if (_draggedJob is not null)
        {
            e.Handled = true;
            JobDropBox.Visibility = Visibility.Collapsed;
            DropTarget.ShowLine(JobDropLine, spot.LineY, 2, JobDropLayer.ActualWidth - 4);
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = L.F("Drop_JobOrder", spot.Index + 1);
            e.DragUIOverride.IsGlyphVisible = false;
            return;
        }

        // Archivos del Explorador sobre una copia: se añaden a ella.
        if (spot.OverIndex >= 0 && Jobs[spot.OverIndex] is { } job)
        {
            e.Handled = true;
            JobDropLine.Visibility = Visibility.Collapsed;
            DropTarget.ShowBox(JobDropBox, spot.OverBounds);
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = L.F("Drop_AddToNamedJob", job.Title);
            e.DragUIOverride.IsGlyphVisible = false;
        }
        else
        {
            HideJobDropFeedback(); // fuera de las tarjetas: la ventana lo trata como una copia nueva
        }
    }

    private void JobList_DragLeave(object sender, DragEventArgs e) => HideJobDropFeedback();

    private async void JobList_Drop(object sender, DragEventArgs e)
    {
        var spot = DropTarget.Find(JobList, e.GetPosition(JobList), JobDropLayer);
        HideJobDropFeedback();
        if (_draggedJob is { } dragged)
        {
            e.Handled = true;
            var before = spot.Index < Jobs.Count ? Jobs[spot.Index] : null;
            if (before != dragged)
            {
                _app.Queue.MoveJob(dragged.Job, before?.Job);
            }

            _draggedJob = null;
            return;
        }

        if (spot.OverIndex < 0 || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        await AddDroppedFiles(e, Jobs[spot.OverIndex]);
    }

    private void HideJobDropFeedback()
    {
        JobDropLine.Visibility = Visibility.Collapsed;
        JobDropBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>La cola cambió de orden: la lista sigue el orden de ejecución.</summary>
    private void SyncJobOrder()
    {
        var order = _app.Queue.Jobs;
        for (int i = 0; i < order.Count; i++)
        {
            int current = -1;
            for (int j = 0; j < Jobs.Count; j++)
            {
                if (Jobs[j].Job == order[i])
                {
                    current = j;
                    break;
                }
            }

            if (current >= 0 && current != i && i < Jobs.Count)
            {
                Jobs.Move(current, i);
            }
        }
    }

    // Soltar sobre el panel (también en la ventana compacta): se añade a la copia que se está viendo.

    private void PanelCard_DragOver(object sender, DragEventArgs e)
    {
        if (_selected is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = L.F("Drop_AddToNamedJob", _selected.Title);
        e.DragUIOverride.IsGlyphVisible = false;
        PanelCard.BorderBrush = ThemeManager.AccentBrush;
        PanelCard.BorderThickness = new Thickness(2);
    }

    private void PanelCard_DragLeave(object sender, DragEventArgs e) => RestorePanelCard();

    private async void PanelCard_Drop(object sender, DragEventArgs e)
    {
        RestorePanelCard();
        if (_selected is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        await AddDroppedFiles(e, _selected);
    }

    private void RestorePanelCard()
    {
        PanelCard.BorderBrush = ThemeManager.BorderBrush;
        PanelCard.BorderThickness = new Thickness(1);
    }

    private async Task AddDroppedFiles(DragEventArgs e, JobViewModel job)
    {
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0)
            {
                var target = _app.Queue.AddToJob(job.Job, paths);
                if (Jobs.FirstOrDefault(j => j.Job == target) is { } shown)
                {
                    Select(shown);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CopyPlanException)
        {
            _app.Notify(L.Get("Files_AddFailed"), ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Al terminar


    private async Task PickDestinationAndCopy(IReadOnlyList<string> sources, bool move)
    {
        // Diálogo de destinos: favoritos y recientes marcables, añadir carpetas, mover y programar.
        var options = new List<string>();
        foreach (string folder in _app.Settings.FavoriteFolders.Concat(_app.Settings.RecentDestinations))
        {
            if (!options.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(folder);
            }
        }

        DestinationsList.Items.Clear();

        // Primero, las carpetas abiertas ahora en el Explorador (salvo las que se van a copiar).
        foreach (string folder in ExplorerFolders.Open().Where(f => !IsInsideAny(f, sources)))
        {
            options.RemoveAll(o => string.Equals(o, folder, StringComparison.OrdinalIgnoreCase));
            DestinationsList.Items.Add(DestinationItem(folder, favorite: false, explorer: true));
        }

        foreach (string folder in options)
        {
            DestinationsList.Items.Add(DestinationItem(folder, _app.Settings.FavoriteFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)));
        }

        DestinationsSummary.Text = sources.Count == 1
            ? L.F("Destinations_SummaryOne", Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0])))
            : L.F("Destinations_SummaryMany", sources.Count);
        DestinationsMove.IsChecked = move;
        DestinationsMode.SelectedIndex = 0;
        DestinationsDryRun.IsChecked = false;
        DestinationsSchedule.IsChecked = false;
        DestinationsWhen.Visibility = Visibility.Collapsed;
        var later = DateTime.Now.AddHours(1);
        DestinationsDate.Date = new DateTimeOffset(later.Date);
        DestinationsTime.Time = new TimeSpan(later.Hour, 0, 0);

        // Sin favoritos ni recientes, primero se pide una carpeta.
        if (options.Count == 0)
        {
            if (await Pickers.PickFolderAsync(L.Get(move ? "Picker_MoveHere" : "Picker_CopyHere")) is not { } first)
            {
                return;
            }

            DestinationsList.Items.Add(DestinationItem(first, favorite: false));
            DestinationsList.SelectedItems.Add(DestinationsList.Items[^1]);
        }

        DestinationsDialog.XamlRoot = Root.XamlRoot;
        DestinationsDialog.RequestedTheme = Root.RequestedTheme;
        if (await DestinationsDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var chosen = DestinationsList.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        DateTime? startAt = null;
        if (DestinationsSchedule.IsChecked == true && DestinationsDate.Date is { } date)
        {
            var when = date.Date + DestinationsTime.Time;
            startAt = when > DateTime.Now ? when.ToUniversalTime() : null;
        }

        Submit(new IpcRequest
        {
            Command = "copy",
            Sources = [.. sources],
            Destination = chosen[0],
            Move = DestinationsMove.IsChecked == true ? true : null,
            ExtraDestinations = chosen.Count > 1 ? chosen.Skip(1).ToList() : null,
            StartAtUtc = startAt,
            Mode = (Engine.CopyMode)Math.Max(0, DestinationsMode.SelectedIndex),
            DryRun = DestinationsDryRun.IsChecked == true ? true : null,
        });
    }

    /// <summary>La carpeta es uno de los orígenes o está dentro de uno (no tiene sentido copiar ahí).</summary>
    private static bool IsInsideAny(string folder, IEnumerable<string> sources)
    {
        string candidate = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return sources.Any(s => candidate.StartsWith(Path.TrimEndingDirectorySeparator(s) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static ListViewItem DestinationItem(string folder, bool favorite, bool explorer = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FontIcon { Glyph = explorer ? "" : favorite ? "" : "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var item = new ListViewItem { Content = row, Tag = folder };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, folder);
        return item;
    }

    private async void AddDestination_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is not { } folder)
        {
            return;
        }

        var existing = DestinationsList.Items.OfType<ListViewItem>().FirstOrDefault(i => string.Equals((string)i.Tag, folder, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = DestinationItem(folder, favorite: false);
            DestinationsList.Items.Add(existing);
        }

        DestinationsList.SelectedItems.Add(existing);
    }

    /// <summary>Sincronizar borra en el destino: se avisa y, por prudencia, se propone simular primero.</summary>
    private void DestinationsMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DestinationsMirrorInfo is null)
        {
            return;
        }

        bool mirror = DestinationsMode.SelectedIndex == (int)Engine.CopyMode.Mirror;
        DestinationsMirrorInfo.IsOpen = mirror;
        if (mirror)
        {
            DestinationsDryRun.IsChecked = true;
        }
    }

    private void DestinationsSchedule_Click(object sender, RoutedEventArgs e) =>
        DestinationsWhen.Visibility = DestinationsSchedule.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Submit(IReadOnlyList<string> sources, string destination, bool move, List<string>? extra = null, DateTime? startAtUtc = null) =>
        Submit(new IpcRequest
        {
            Command = "copy",
            Sources = [.. sources],
            Destination = destination,
            Move = move ? true : null,
            ExtraDestinations = extra is { Count: > 0 } ? extra : null,
            StartAtUtc = startAtUtc,
        });

    private void Submit(IpcRequest request)
    {
        try
        {
            _app.Submit(request);
            ShowJobs();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _app.Notify(L.Get("Notify_ProblemTitle"), ex.Message);
        }
    }

    /// <summary>"Copiar a": favoritos y recientes, se rehace cada vez que se abre el menú.</summary>
    private void NewMenu_Opening(object sender, object e)
    {
        CopyToMenu.Items.Clear();
        var open = ExplorerFolders.Open();
        foreach (string folder in open)
        {
            CopyToMenu.Items.Add(CopyToItem(folder, ""));
        }

        if (open.Count > 0)
        {
            CopyToMenu.Items.Add(new MenuFlyoutSeparator());
        }

        var favorites = _app.Settings.FavoriteFolders;
        var recents = _app.Settings.RecentDestinations.Where(r => !favorites.Contains(r, StringComparer.OrdinalIgnoreCase)).Take(8).ToList();
        foreach (string folder in favorites)
        {
            CopyToMenu.Items.Add(CopyToItem(folder, ""));
        }

        if (favorites.Count > 0 && recents.Count > 0)
        {
            CopyToMenu.Items.Add(new MenuFlyoutSeparator());
        }

        foreach (string folder in recents)
        {
            CopyToMenu.Items.Add(CopyToItem(folder, ""));
        }

        if (CopyToMenu.Items.Count == 0)
        {
            CopyToMenu.Items.Add(new MenuFlyoutItem { Text = L.Get("Tool_CopyToEmpty"), IsEnabled = false });
        }

        CopyToMenu.Items.Add(new MenuFlyoutSeparator());
        var manage = new MenuFlyoutItem { Text = L.Get("Tool_CopyToManage"), Icon = new FontIcon { Glyph = "" } };
        manage.Click += Settings_Click;
        CopyToMenu.Items.Add(manage);
    }

    private MenuFlyoutItem CopyToItem(string folder, string glyph)
    {
        var item = new MenuFlyoutItem { Text = Format.ShortPath(folder, 2), Icon = new FontIcon { Glyph = glyph } };
        ToolTipService.SetToolTip(item, folder);
        item.Click += async (_, _) =>
        {
            var files = await Pickers.PickFilesAsync();
            if (files.Count > 0)
            {
                Submit(files, folder, move: false);
            }
        };
        return item;
    }

    // Comprobar sumas

    private async void TestFiles_Click(object sender, RoutedEventArgs e)
    {
        var lists = await Pickers.PickFilesAsync(".sha256", ".sha1", ".sha512", ".md5", ".sfv", ".xxh3", ".xxh64", ".xxh128", ".sha3-256", ".sha3-512", ".b3", ".blake3");
        if (lists.Count > 0)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Test, Sources = [.. lists] });
        }
    }

    private async void TestFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Test, Sources = [folder] });
        }
    }

    // Crear archivo de sumas (con el algoritmo de Ajustes; SHA-256 si no hay ninguno)

    private async void HashFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Hash, Sources = [folder] });
        }
    }

    private async void HashFiles_Click(object sender, RoutedEventArgs e)
    {
        var files = await Pickers.PickFilesAsync();
        if (files.Count > 0)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Hash, Sources = [.. files] });
        }
    }

    // Borrar

    private void Delete_Click(object sender, RoutedEventArgs e) => _ = DeleteAsync(secure: false);

    private void Wipe_Click(object sender, RoutedEventArgs e) => _ = DeleteAsync(secure: true);

    private async Task DeleteAsync(bool secure)
    {
        var paths = await Pickers.PickFilesAsync();
        if (paths.Count > 0)
        {
            await ConfirmAndDeleteAsync(paths, secure);
        }
    }

    /// <summary>Borrar siempre pide confirmación (el borrado seguro no se puede deshacer de ningún modo).</summary>
    internal async Task ConfirmAndDeleteAsync(IReadOnlyList<string> paths, bool secure)
    {
        DeleteMessage.Text = paths.Count == 1
            ? L.F(secure ? "Delete_ConfirmSecureOne" : "Delete_ConfirmOne", paths[0])
            : L.F(secure ? "Delete_ConfirmSecureMany" : "Delete_ConfirmMany", paths.Count);
        DeleteWarning.IsOpen = secure;
        DeleteDialog.XamlRoot = Root.XamlRoot;
        DeleteDialog.RequestedTheme = Root.RequestedTheme;
        if (await DeleteDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Delete, SecureDelete = secure, Sources = [.. paths] });
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _app.FinishAction = sender switch
        {
            _ when ReferenceEquals(sender, FinishSleep) => FinishAction.Sleep,
            _ when ReferenceEquals(sender, FinishHibernate) => FinishAction.Hibernate,
            _ when ReferenceEquals(sender, FinishShutdown) => FinishAction.Shutdown,
            _ when ReferenceEquals(sender, FinishSignOut) => FinishAction.SignOut,
            _ when ReferenceEquals(sender, FinishExit) => FinishAction.ExitApp,
            _ => FinishAction.None,
        };
        UpdateFinishButton();
    }

    internal void UpdateFinishButton()
    {
        FinishNothing.IsChecked = _app.FinishAction == FinishAction.None;
        FinishSleep.IsChecked = _app.FinishAction == FinishAction.Sleep;
        FinishHibernate.IsChecked = _app.FinishAction == FinishAction.Hibernate;
        FinishShutdown.IsChecked = _app.FinishAction == FinishAction.Shutdown;
        FinishSignOut.IsChecked = _app.FinishAction == FinishAction.SignOut;
        FinishExit.IsChecked = _app.FinishAction == FinishAction.ExitApp;
        (FinishIcon.Glyph, FinishText.Text) = _app.FinishAction switch
        {
            FinishAction.Sleep => ("", L.Get("Finish_LabelSleep")),
            FinishAction.Hibernate => ("", L.Get("Finish_LabelHibernate")),
            FinishAction.Shutdown => ("", L.Get("Finish_LabelShutdown")),
            FinishAction.SignOut => ("", L.Get("Finish_LabelSignOut")),
            FinishAction.ExitApp => ("", L.Get("Finish_LabelExit")),
            _ => ("", L.Get("Finish_Label")),
        };
    }

    private bool _renameExisting;

    /// <summary>"Renombrar el que ya está": cierra el diálogo con esa decisión.</summary>
    private void ConflictRename_Click(object sender, RoutedEventArgs e)
    {
        _renameExisting = true;
        ConflictDialog.Hide();
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
                    Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
                    _renameExisting = false;
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

                    var decision = _renameExisting ? ConflictDecision.RenameExisting : result switch
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
