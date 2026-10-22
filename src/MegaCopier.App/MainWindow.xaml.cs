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
    // Tres vistas: la pequeña (la de siempre al copiar), la de la lista de archivos («Más») y la de varias copias a la
    // vez (la misma, con la lista de copias a la izquierda).
    private const double CompactWidth = 600;
    /// <summary>Alto de la pequeña: la barra de herramientas (siempre a la vista) y el panel de la copia.</summary>
    private const double CompactHeight = 252 + ToolbarHeight;
    private const double ToolbarHeight = 44;
    private const double PagerHeight = 40;

    /// <summary>La pequeña solo se ensancha: más alta no enseñaría nada más. Más estrecha cortaría los botones.</summary>
    private const double CompactMinWidth = 560;

    /// <summary>Sin copias, la ventana compacta crece para que quepan el icono, los botones y los enlaces.</summary>
    private const double EmptyHeight = 360 + ToolbarHeight;
    /// <summary>Por debajo de esto la lista de archivos y sus botones ya no caben enteros.</summary>
    private const double ExpandedMinWidth = 760;
    private const double ExpandedMinHeight = 660;

    /// <summary>«Más» abre la vista de la lista de archivos con lo justo para que quepa todo (el usuario puede agrandarla).</summary>
    private const double ExpandedWidth = ExpandedMinWidth;
    private const double ExpandedHeight = ExpandedMinHeight;

    /// <summary>Con varias copias a la vez, la vista con la lista de copias a la izquierda, algo más grande.</summary>
    private const double MultiWidth = 820 + SidebarWidth;
    private const double MultiHeight = 780;
    private const double SidebarWidth = 290;

    private readonly Controls.SlidingIndicator _jobIndicator;
    private bool _wasEmpty = true;
    private readonly App _app;

    /// <summary>La ventana tiene el foco (para el material).</summary>
    private bool _windowActive = true;
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
    /// <summary>Con menos ancho, la lista de copias flota sobre el contenido en vez de quitarle sitio.</summary>
    private const double NarrowWidth = ExpandedMinWidth + SidebarWidth - 10;
    private bool _narrow;
    private bool _sidebarWide = true;
    private bool _sidebarOverlayOpen;
    private bool _hadSidebar;

    /// <summary>Tamaño de la vista completa antes de crecer para la lista de copias (null si no tuvo que crecer).</summary>
    private (double Width, double Height)? _beforeSidebar;

    /// <summary>Tamaño (en píxeles) al que creció para la lista: si sigue igual al quitarla, el usuario no la tocó.</summary>
    private (double Width, double Height) _sidebarGrownTo;

    /// <summary>El usuario eligió compacta o completa en esta sesión: el modo automático ya no cambia por su cuenta.</summary>
    private bool _userChoseMode;

    /// <summary>Ancho que el usuario le dio a la ventana pequeña (al volver de «Más» recupera ese ancho).</summary>
    private double _compactWidth = CompactWidth;

    /// <summary>Hasta cuándo los cambios de tamaño son de la animación (no del usuario).</summary>
    private long _programmaticUntil;

    /// <summary>Lo que queda por hacer al terminar la animación (ocultar lo que se desvanecía al volver a «Menos»).</summary>
    private Action? _afterResize;

    private (double Width, double Height) _resizeFrom;
    private (double Width, double Height) _resizeTo;
    private long _resizeStart;

    /// <param name="copyWindow">
    /// Una ventana más para una copia («Cada copia en su propia ventana»): no guarda su posición (es la de la principal)
    /// y, cuando se oculta sin copias en marcha, se cierra del todo.
    /// </param>
    internal MainWindow(App app, bool copyWindow = false)
    {
        _app = app;
        IsCopyWindow = copyWindow;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        _jobIndicator = new Controls.SlidingIndicator(JobList, JobIndicatorLayer);
        _resizeTimer = _dispatcher.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(12);
        _resizeTimer.Tick += (_, _) => ResizeStep();

        Handle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        PrepareUiScale();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        _expanded = app.Settings.WindowMode == WindowMode.Expanded;
        var (width, height) = TargetSize(_expanded);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(width), Scaled(height)));
        CenterOnScreen();
        RestorePosition();

        // El ancho que el usuario le da a la ventana pequeña se recuerda (al volver de «Más» lo recupera).
        AppWindow.Changed += (_, args) =>
        {
            // Solo cuenta lo que hace el usuario: los cambios de la animación (y los avisos que llegan justo después)
            // no, o al volver de «Más» la pequeña se quedaría con un ancho intermedio.
            if (args.DidSizeChange && !_expanded && !_resizeTimer.IsRunning && Jobs.Count > 0
                && Stopwatch.GetTimestamp() > _programmaticUntil && AppWindow.Size.Width != _resizeTo.Width)
            {
                _compactWidth = Math.Max(CompactMinWidth, AppWindow.Size.Width / (Scaled(1000) / 1000.0));
            }
        };

        // La X, según el ajuste: por defecto cancela las copias como «Detener» y cierra la ventana (MegaCopier sigue
        // en la bandeja, listo para la próxima copia).
        AppWindow.Closing += (_, args) =>
        {
            if (_closingForGood)
            {
                return;
            }

            args.Cancel = true;
            CloseButtonPressed();
        };
        Closed += (_, _) => IsClosed = true;

        // El material sigue a la ventana: el de serie pasa a Mica Alt sin el foco; los demás, como Windows.
        Activated += (_, args) =>
        {
            _app.Updates?.NoteInteraction();
            _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            (SystemBackdrop as MaterialBackdrop)?.SetActive(_windowActive);
        };

        // Al pasar a otra pantalla (más pequeña o más grande), la escala se ajusta a ella.
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange && !_resizeTimer.IsRunning && UpdateUiScale())
            {
                ApplyLayout(animate: false);
            }
        };

        // Minimizar puede ocultarla en la bandeja en lugar de dejarla en la barra de tareas.
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange && !_minimizingOnPurpose && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }
                && _app.Settings.MinimizeButton == MinimizeButtonAction.Tray && _app.Settings.ShowTrayIcon)
            {
                Hide();
            }
        };

        // Cada ventana muestra sus copias (con «Cada copia en su propia ventana» hay varias; si no, todas van aquí).
        _app.Queue.JobAdded += job => OnUi(() =>
        {
            if (_app.WindowFor(job) == this)
            {
                AddJob(job, select: true);
            }
        }, "AddJob");
        _app.Queue.JobRemoved += job => OnUi(() => RemoveJob(job), "RemoveJob");
        _app.Queue.JobsReordered += () => OnUi(SyncJobOrder, "SyncJobOrder");
        foreach (var job in _app.Queue.Jobs.Where(j => _app.WindowFor(j) == this))
        {
            AddJob(job, select: false);
        }

        Panel.ExpandRequested += () => SetExpanded(!_expanded, userChoice: true);
        InitializeNavigation();
        HistoryView.CloseRequested += NavigateBack;
        SettingsView.CloseRequested += NavigateBack;
        TimelineView.CloseRequested += NavigateBack;
        CompareView.CloseRequested += NavigateBack;
        CompareView.SyncRequested += request =>
        {
            ShowJobs();
            Submit(request);
        };
        TimelineView.JobChosen += job =>
        {
            ShowJobs();
            Select(job);
        };
        JobList.ItemsSource = Jobs;
        Root.Loaded += (_, _) => NameIconButtons(Root);
        Motion.Attach(Root);
        // Botones de solo icono: su ayuda emergente es también su nombre accesible (en cuanto se cargan). Los menús y
        // las pestañas, con los iconos del conjunto elegido.
        foreach (var part in new FrameworkElement[] { Root, SettingsView, HistoryView, TimelineView, CompareView, _filesTab, _targetTab, _optionsTab, _statusTab, _logTab })
        {
            part.Loaded += (s, _) =>
            {
                Responsive.NameFromToolTips((DependencyObject)s);
                ThemedIcons.Attach((DependencyObject)s);
            };
        }

        Jobs.CollectionChanged += (_, _) => UpdateLayoutState();
        Tabs.SelectedItem = FilesItem;

        _refreshTimer = _dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        _refreshTimer.Tick += (_, _) => RefreshTick();
        _refreshTimer.Start();


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

    /// <summary>
    /// Espera a que la ventana tenga su contenido cargado (XamlRoot). Si MegaCopier arranca con una copia que pregunta
    /// enseguida («ya existe en el destino»), el diálogo podía abrirse antes y fallaba con «El parámetro no es correcto».
    /// </summary>
    private async Task WaitForRootAsync()
    {
        if (Root.IsLoaded && Root.XamlRoot is not null)
        {
            return;
        }

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLoaded(object sender, RoutedEventArgs e) => loaded.TrySetResult();
        Root.Loaded += OnLoaded;
        try
        {
            if (!(Root.IsLoaded && Root.XamlRoot is not null))
            {
                await loaded.Task;
            }
        }
        finally
        {
            Root.Loaded -= OnLoaded;
        }
    }

    internal void ShowAndActivate()
    {
        // Modo de prueba: mostrar sin quitar el foco a lo que el usuario esté haciendo.
        if (App.TestMode)
        {
            AppWindow.Show(activateWindow: false);
            return;
        }

        AppWindow.Show(activateWindow: true);
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } minimized)
        {
            minimized.Restore(); // se ocultó en la bandeja estando minimizada
        }

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

    /// <summary>«Iniciar las copias con la ventana minimizada»: en la barra de tareas, sin taparle nada al usuario.</summary>
    internal void ShowMinimized()
    {
        if (AppWindow.IsVisible)
        {
            return;
        }

        AppWindow.Show(activateWindow: false);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            _minimizingOnPurpose = true; // esto no es el botón de minimizar: se queda en la barra de tareas
            presenter.Minimize();
            _minimizingOnPurpose = false;
        }
    }

    private bool _minimizingOnPurpose;

    internal void Hide()
    {
        // Una ventana más de una copia, sin nada en marcha: se cierra del todo (la próxima copia abre otra o usa la
        // principal). Con copias en marcha solo se oculta, como la principal (su icono de la bandeja la vuelve a abrir).
        if (IsCopyWindow && !HasActiveJobs)
        {
            CloseForGood();
            return;
        }

        if (!IsCopyWindow)
        {
            SavePlacement();
        }

        AppWindow.Hide();
    }

    /// <summary>Ventana más para una copia (ver el constructor).</summary>
    internal bool IsCopyWindow { get; }

    /// <summary>Ya se cerró (solo las ventanas de una copia se cierran del todo).</summary>
    internal bool IsClosed { get; private set; }

    private bool _closingForGood;

    /// <summary>¿Tiene alguna copia en marcha, en pausa o esperando su turno?</summary>
    internal bool HasActiveJobs => Jobs.Any(j => j.Job.IsActive);

    /// <summary>Cierra de verdad una ventana de copia (la X normal solo la oculta).</summary>
    private void CloseForGood()
    {
        if (_closingForGood)
        {
            return;
        }

        _closingForGood = true;
        _app.ForgetWindow(this);
        Close();
    }

    /// <summary>Esta ventana pasa a mostrar una copia (por ejemplo, la que se abrió aparte en una ventana nueva).</summary>
    internal void TakeJob(CopyJob job, bool select = true) => AddJob(job, select);

    /// <summary>La copia se va a otra ventana.</summary>
    internal void ReleaseJob(CopyJob job) => RemoveJob(job);

    /// <summary>La coloca escalonada respecto a otra ventana, para que no queden una encima de la otra.</summary>
    internal void PlaceAfter(MainWindow other, int step)
    {
        var area = DisplayArea.GetFromWindowId(other.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int offset = Scaled(32) * step;
        int x = Math.Clamp(other.AppWindow.Position.X + offset, area.X, Math.Max(area.X, area.X + area.Width - AppWindow.Size.Width));
        int y = Math.Clamp(other.AppWindow.Position.Y + offset, area.Y, Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    /// <summary>
    /// La X de la ventana, según «Al pulsar la X»: cancelar las copias (como «Detener» en cada una), detenerlas para
    /// continuarlas después, solo ocultar la ventana (las copias siguen) o salir de MegaCopier. Las copias ya
    /// terminadas pasan al historial al cerrar la ventana.
    /// </summary>
    internal void CloseButtonPressed()
    {
        var action = _app.Settings.CloseButton;
        if (action == CloseButtonAction.Exit)
        {
            SavePlacement();
            _app.ExitApp();
            return;
        }

        foreach (var job in Jobs.ToList())
        {
            if (job.Job.IsActive && action == CloseButtonAction.CancelCopies)
            {
                job.Stop();
            }
            else if (job.Job.IsActive && action == CloseButtonAction.StopCopies)
            {
                _app.Queue.Stop(job.Job);
            }
            else if (job.IsDone && action != CloseButtonAction.HideToTray)
            {
                job.Clear();
            }
        }

        Hide();
    }

    /// <summary>«Recordar la posición / el tamaño de la ventana».</summary>
    private void SavePlacement()
    {
        var s = _app.Settings;
        if (!s.RememberPosition && !s.RememberSize)
        {
            return;
        }

        if (s.RememberPosition)
        {
            s.WindowX = AppWindow.Position.X;
            s.WindowY = AppWindow.Position.Y;
        }

        if (s.RememberSize && _expanded)
        {
            double scale = Scaled(1000) / 1000.0;
            s.WindowWidth = (int)(AppWindow.Size.Width / scale);
            s.WindowHeight = (int)(AppWindow.Size.Height / scale);
        }

        s.Save();
    }

    private void RestorePosition()
    {
        var s = _app.Settings;
        if (!s.RememberPosition || s.WindowX == int.MinValue)
        {
            return;
        }

        // Dentro de la pantalla aunque haya cambiado la configuración de monitores.
        var area = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(s.WindowX, s.WindowY), DisplayAreaFallback.Nearest).WorkArea;
        int x = Math.Clamp(s.WindowX, area.X, Math.Max(area.X, area.X + area.Width - AppWindow.Size.Width));
        int y = Math.Clamp(s.WindowY, area.Y, Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

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
        TimelineView.Visibility = Visibility.Collapsed;
        CompareView.Visibility = Visibility.Collapsed;
        UpdateLayoutState();
        RestoreAfterPage();
        RememberNavigation();
    }

    /// <summary>Hay una página (Historial, Ajustes, Línea de tiempo) encima de las copias.</summary>
    private bool PageOpen => HistoryView.Visibility == Visibility.Visible || SettingsView.Visibility == Visibility.Visible
        || TimelineView.Visibility == Visibility.Visible || CompareView.Visibility == Visibility.Visible;

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
        PaintNativeBackground(theme);
        AppWindow.TitleBar.PreferredTheme = theme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        // El de serie (el del tema, Mica) no se apaga al perder el foco: pasa a Mica Alt.
        SystemBackdrop = Appearance.CreateBackdrop(theme.Backdrop, theme.Dark, altWhenInactive: _app.Settings.EffectiveBackdrop == WindowBackdrop.Theme, active: _windowActive);
        TrayIcon.SetMenuTheme(theme.Dark);
        ApplyTexture(TextureImage, theme);
    }

    private static readonly Dictionary<uint, nint> BackgroundBrushes = [];

    /// <summary>
    /// La ventana de Windows que contiene la interfaz se pinta del color de fondo del tema: al agrandarla con el ratón,
    /// la franja nueva que aún no ha dibujado XAML sale de ese color en lugar de negra.
    /// </summary>
    private void PaintNativeBackground(ThemeDefinition theme)
    {
        var color = ThemeManager.BackgroundBrush.GradientStops.Count > 0 ? ThemeManager.BackgroundBrush.GradientStops[0].Color : default;
        if (color.A < 200)
        {
            color = theme.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        }

        // Un pincel por color, que no se borra nunca: el fondo es de la clase de ventana (lo comparten otras ventanas de WinUI)
        // y borrar uno que aún usen podría dejarlas sin fondo válido.
        uint rgb = (uint)(color.R | (color.G << 8) | (color.B << 16));
        if (!BackgroundBrushes.TryGetValue(rgb, out nint brush))
        {
            brush = CreateSolidBrush(rgb);
            BackgroundBrushes[rgb] = brush;
        }

        SetClassLongPtr(Handle, -10 /* GCLP_HBRBACKGROUND */, brush);
    }

    /// <summary>
    /// Cambia de tema con un fundido: se fotografía la ventana tal como está, se aplica el tema debajo y la foto se
    /// desvanece. Sin esto, WinUI vuelve a pintar cada control por separado y el cambio se ve a saltos.
    /// </summary>
    internal async Task CrossfadeAsync(Action apply)
    {
        bool animate = AppWindow.IsVisible && Root.ActualWidth > 0 && !Theming.BarAppearance.ReduceMotion;
        if (animate)
        {
            try
            {
                var shot = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await shot.RenderAsync(Root);
                ThemeFade.Source = shot;
                ThemeFade.Opacity = 1;
                ThemeFade.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
            {
                animate = false; // sin foto, el tema se aplica sin fundido
            }
        }

        apply();
        if (!animate)
        {
            return;
        }

        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut },
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, ThemeFade);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);
        story.Completed += (_, _) =>
        {
            ThemeFade.Visibility = Visibility.Collapsed;
            ThemeFade.Source = null;
            story.Stop();
            Redraw(); // tras un fundido, WinUI puede dejar controles dibujados apagados: se dibuja todo de nuevo
        };
        story.Begin();
    }

    /// <summary>La textura del tema (Assets/Textures/&lt;nombre&gt;.jpg) detrás de todo, o ninguna.</summary>
    internal static void ApplyTexture(Image image, ThemeDefinition theme)
    {
        if (theme.Texture.Length == 0)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Textures", theme.Texture + ".jpg");
        if (!File.Exists(path))
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
        image.Opacity = Math.Clamp(theme.TextureOpacity, 0, 1);
        image.Visibility = Visibility.Visible;
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
        // Solo cuentan las copias que siguen en la cola (una recién quitada aún puede estar en la lista un instante).
        var queued = _app.Queue.Jobs;
        bool problems = Jobs.Any(j => (j.HasProblem || j.IsInterrupted) && queued.Contains(j.Job));
        bool stays = mode == FinishWindow.KeepOpen || (mode == FinishWindow.Auto && problems) || PageOpen;

        // Terminadas todas, las que acabaron bien salen de la lista (ya están en el historial). Si la ventana se
        // queda abierta, la que se está viendo sigue en el panel hasta que se cierre o empiece otra copia.
        foreach (var job in Jobs.Where(j => j.IsDone && !(stays && j == _selected)).ToList())
        {
            job.Clear();
        }

        if (stays)
        {
            return;
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
        if (_app.Settings.WindowMode == WindowMode.Auto && !_userChoseMode && !_expanded && Jobs.Count(j => j.IsActive && !_app.IsAwaitingAnswer(j.Job)) > 1)
        {
            SetExpanded(true, userChoice: false, animate: AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });
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

    /// <summary>Abre la ventana en esa copia (desde su icono de progreso de la bandeja).</summary>
    internal void ShowJob(CopyJob job)
    {
        ShowJobs();
        Select(Jobs.FirstOrDefault(j => j.Job == job) ?? _selected);
        ShowAndActivate();
    }

    /// <summary>«Detener» sobre una copia concreta (desde su icono de la bandeja): se cancela, como en el panel.</summary>
    internal void StopJob(CopyJob job) => Jobs.FirstOrDefault(j => j.Job == job)?.Stop();

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

    // ---------------------------------------------------------------------------------------------------------
    // Menú de cada copia de la lista (clic derecho o tecla de menú) y Supr para quitar las terminadas.

    private void JobList_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if ((args.OriginalSource as FrameworkElement)?.DataContext is not JobViewModel job)
        {
            return;
        }

        args.Handled = true;
        Select(job);
        var menu = new MenuFlyout();
        ThemedIcons.Hook(menu);
        void Item(string text, string glyph, Action action, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            if (shortcut is not null)
            {
                item.KeyboardAcceleratorTextOverride = shortcut;
            }

            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (job.CanPause)
        {
            Item(L.Get("Tray_JobPause"), "", () => _app.Queue.Pause(job.Job));
        }

        if (job.CanResume)
        {
            Item(job.ResumeLabel, "", job.TogglePause);
        }

        if (job.CanStop)
        {
            Item(L.Get("Tray_JobStop"), "", job.Stop);
        }

        if (job.CanArchive)
        {
            Item(L.Get("Jobs_Archive"), "", () => ArchiveJob(job), L.Get("Jobs_ArchiveKey"));
            Item(L.Get("Jobs_Delete"), "", () => DeleteJob(job));
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        Item(L.Get("Panel_OpenFolderItem/Text"), "", job.OpenDestination);
        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt(args.OriginalSource as FrameworkElement ?? (FrameworkElement)sender);
        }
    }

    private void JobList_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete && _selected is { CanArchive: true } job)
        {
            e.Handled = true;
            ArchiveJob(job);
        }
    }

    /// <summary>«Archivar en el historial»: sale de la lista; en el historial ya está desde que terminó.</summary>
    private void ArchiveJob(JobViewModel job) => _app.Queue.Clear(job.Job);

    /// <summary>«Eliminar»: sale de la lista y también del historial.</summary>
    private void DeleteJob(JobViewModel job)
    {
        _app.Queue.Clear(job.Job);
        if (_app.HistoryIds.Remove(job.Job.Id, out string? entry))
        {
            History.Remove(entry);
        }
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

    /// <summary>
    /// Pasa a la copia anterior o siguiente. En la ventana pequeña se desliza: la copia que se ve sale por un lado (una
    /// foto suya) mientras la otra entra por el contrario, hacia adelante o hacia atrás según el botón.
    /// </summary>
    private async void Page(int delta)
    {
        if (Jobs.Count == 0)
        {
            return;
        }

        int index = _selected is null ? 0 : Jobs.IndexOf(_selected);
        var next = Jobs[(index + delta + Jobs.Count) % Jobs.Count];
        double width = Panel.ActualWidth;
        if (_expanded || width <= 0 || next == _selected || Theming.BarAppearance.ReduceMotion)
        {
            Select(next);
            return;
        }

        var shot = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        try
        {
            await shot.RenderAsync(Panel);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Select(next);
            return;
        }

        PanelGhost.Source = shot;
        PanelGhost.Width = width;
        PanelGhost.Height = Panel.ActualHeight;
        PanelGhost.Visibility = Visibility.Visible;
        Select(next);
        double distance = width + 16;
        Motion.SlideX(PanelGhost, 0, -delta * distance, 260, decelerate: true, done: () =>
        {
            PanelGhost.Visibility = Visibility.Collapsed;
            PanelGhost.Source = null;
            Motion.ResetSlide(PanelGhost);
        });
        Motion.SlideX(Panel, delta * distance, 0, 260, null, decelerate: true);
    }

    /// <summary>Lo que se desliza no se sale de la tarjeta.</summary>
    private void PanelHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        PanelHost.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };

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
                return; // los límites se ponen al terminar la animación
            }
        }

        ApplyConstraints();
    }

    /// <summary>
    /// Límites de tamaño de cada vista. La pequeña: alto fijo (solo se ensancha) y sin maximizar. La completa: un
    /// mínimo con el que todo cabe, y se puede maximizar. Durante una animación no se aplican (la frenarían).
    /// </summary>
    private void ApplyConstraints()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || _resizeTimer.IsRunning)
        {
            return;
        }

        if (_expanded)
        {
            presenter.PreferredMaximumHeight = null;
            presenter.PreferredMinimumWidth = Scaled(ExpandedMinWidth);
            presenter.PreferredMinimumHeight = Scaled(ExpandedMinHeight);
            presenter.IsMaximizable = true;
            return;
        }

        // Con un diálogo abierto en la ventana pequeña (el de «ya existe»), el alto es el que él necesita.
        int height = Scaled(_roomForDialog?.Height ?? TargetSize(false).Height);
        presenter.IsMaximizable = false;
        presenter.PreferredMinimumWidth = Scaled(CompactMinWidth);
        presenter.PreferredMinimumHeight = height;
        presenter.PreferredMaximumHeight = height;
    }

    /// <summary>Tamaño que pide un diálogo abierto en la ventana pequeña (null si no hay ninguno).</summary>
    private (double Width, double Height)? _roomForDialog;

    private int _shrinkTicket;

    /// <summary>
    /// Devuelve la ventana pequeña a su tamaño un momento después de cerrar el diálogo de «ya existe», si no ha llegado
    /// otro: con «Uno a uno» el siguiente se abría mientras la ventana encogía y quedaba descolocado, fuera de la vista.
    /// </summary>
    private async Task ShrinkAfterDialogAsync(int ticket)
    {
        await Task.Delay(1500);
        if (ticket == _shrinkTicket && _roomForDialog is not null)
        {
            _roomForDialog = null;
            AnimateResize();
        }
    }

    /// <summary>Quita los límites mientras la ventana cambia de tamaño animada.</summary>
    private void RelaxConstraints()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMaximumHeight = null;
            presenter.PreferredMinimumWidth = Scaled(CompactMinWidth);
            presenter.PreferredMinimumHeight = Scaled(CompactHeight - 20);
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Compacta / completa

    private void SetExpanded(bool expanded, bool userChoice, bool animate = true)
    {
        _userChoseMode |= userChoice;
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        ApplyLayout(animate: animate && AppWindow.IsVisible);
    }

    private void ApplyLayout(bool animate)
    {
        _programmaticUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 3;
        if (!_expanded && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } maximized)
        {
            maximized.Restore(); // la pequeña no se maximiza: primero vuelve a su tamaño normal
        }

        Panel.SetExpanded(_expanded);
        _afterResize?.Invoke();
        _afterResize = null;
        if (animate)
        {
            // La animación empieza ya: los límites de tamaño de la vista nueva (más abajo) agrandarían la ventana de golpe.
            AnimateResize();
        }

        UIElement[] extras = [Tabs, TabHost]; // la barra de herramientas se ve siempre, también en la pequeña
        if (animate && !_expanded)
        {
            // «Menos»: la barra de herramientas y las pestañas se desvanecen subiendo mientras la ventana encoge, y
            // se ocultan al terminar (el mismo efecto que al abrirlas, al revés).
            foreach (var element in extras)
            {
                Motion.FadeOut(element);
            }

            _afterResize = () =>
            {
                foreach (var element in extras)
                {
                    element.Visibility = Visibility.Collapsed;
                    Motion.ResetFade(element);
                }
            };
        }
        else
        {
            foreach (var element in extras)
            {
                element.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
                if (animate && _expanded)
                {
                    Motion.FadeIn(element); // «Más»: aparecen bajando mientras la ventana crece
                }
            }
        }

        ApplySidebar();
        UpdatePager();
        UpdateLayoutState();
        RefreshTabs();

        var (width, height) = TargetSize(_expanded);
        if (!animate)
        {
            _resizeTimer.Stop();
            RelaxConstraints();
            _resizeTo = (Scaled(width), Scaled(height));
            _programmaticUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 3;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(Scaled(width), Scaled(height)));
            ApplyConstraints();
        }
    }

    /// <summary>Animación del tamaño (con aceleración suave), manteniendo la ventana dentro de la pantalla.</summary>
    private void AnimateResize()
    {
        var (width, height) = TargetSize(_expanded);
        AnimateResizeTo(width, height);
    }

    private void AnimateResizeTo(double width, double height)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            ApplyConstraints(); // maximizada no se anima: los límites cambian y ya está
            return;
        }

        RelaxConstraints();
        _resizeFrom = (AppWindow.Size.Width, AppWindow.Size.Height);
        _resizeTo = (Scaled(width), Scaled(height));
        _resizeStart = 0; // el reloj empieza tras el primer fotograma: preparar la vista nueva puede tardar
        _awaitingFrame = true;
        if (!_watchingFrames)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnResizeFrame;
            _watchingFrames = true;
        }

        _resizeTimer.Start();
    }

    /// <summary>
    /// Al crecer, la parte nueva de la ventana sale en negro hasta que WinUI la dibuja: cada paso de la animación espera a
    /// que esté dibujado el anterior, así esa franja es de unos pocos píxeles y no un recuadro negro.
    /// </summary>
    private bool _awaitingFrame;
    private bool _watchingFrames;

    private void OnResizeFrame(object? sender, object e)
    {
        _awaitingFrame = false;
        if (!_resizeTimer.IsRunning)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnResizeFrame;
            _watchingFrames = false;
        }
    }

    private void ResizeStep()
    {
        if (_awaitingFrame)
        {
            return;
        }

        if (_resizeStart == 0)
        {
            _resizeStart = Stopwatch.GetTimestamp();
        }

        double t = Math.Min(1, Stopwatch.GetElapsedTime(_resizeStart).TotalMilliseconds / 220);
        double eased = 1 - Math.Pow(1 - t, 3);
        int w = (int)(_resizeFrom.Width + ((_resizeTo.Width - _resizeFrom.Width) * eased));
        int h = (int)(_resizeFrom.Height + ((_resizeTo.Height - _resizeFrom.Height) * eased));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int x = Math.Clamp(AppWindow.Position.X, area.X, Math.Max(area.X, area.X + area.Width - w));
        int y = Math.Clamp(AppWindow.Position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        _awaitingFrame = true;
        _programmaticUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 3;
        if (t >= 1)
        {
            _resizeTimer.Stop();
            ApplyConstraints();
            _afterResize?.Invoke();
            _afterResize = null;
        }
    }

    /// <summary>
    /// Tamaño de cada vista. La pequeña conserva el ancho que le dio el usuario (solo se ensancha); la completa, el
    /// tamaño recordado, y con varias copias a la vez deja sitio a la lista de copias de la izquierda.
    /// </summary>
    private (double Width, double Height) TargetSize(bool expanded)
    {
        if (expanded)
        {
            var s = _app.Settings;
            double width = s.RememberSize && s.WindowWidth > 0 ? Math.Max(ExpandedMinWidth, s.WindowWidth) : ExpandedWidth;
            double height = s.RememberSize && s.WindowHeight > 0 ? Math.Max(ExpandedMinHeight, s.WindowHeight) : ExpandedHeight;
            return SidebarWanted ? (Math.Max(width, MultiWidth), Math.Max(height, MultiHeight)) : (width, height);
        }

        return Jobs.Count == 0 ? (Math.Max(CompactWidth, _compactWidth), EmptyHeight) : (_compactWidth, CompactHeight + (Jobs.Count > 1 ? PagerHeight : 0));
    }

    /// <summary>La vista de varias copias: la lista de copias a la izquierda solo tiene sentido con más de una.</summary>
    private bool SidebarWanted => Jobs.Count > 1;

    /// <summary>
    /// La barra de herramientas también va en la ventana pequeña: si no cabe entera, «Al terminar» se queda en su
    /// icono (lo elegido sigue en su descripción emergente).
    /// </summary>
    /// <summary>
    /// La barra de herramientas también va en la ventana pequeña: si no cabe entera, «Al terminar» se queda en su
    /// icono (lo elegido sigue en su descripción emergente).
    /// </summary>
    /// <summary>
    /// «Al terminar» conserva su texto mientras quepa junto a los botones; solo con la ventana muy estrecha se queda
    /// en el icono (se mide de verdad, no con un ancho fijo).
    /// </summary>
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e) => FitFinishText(e.NewSize.Width);

    private void FitFinishText(double width)
    {
        var unlimited = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        ToolButtons.Measure(unlimited);
        FinishIcon.Measure(unlimited);
        var text = new TextBlock { Text = FinishText.Text, Style = FinishText.Style }; // aunque ahora esté oculto
        text.Measure(unlimited);
        double needed = Toolbar.Padding.Left + Toolbar.Padding.Right + ToolButtons.DesiredSize.Width + (Toolbar.ColumnSpacing * 2)
            + FinishIcon.DesiredSize.Width + 8 + text.DesiredSize.Width + 44; // el hueco, los márgenes del botón y su flecha
        FinishText.Visibility = width >= needed ? Visibility.Visible : Visibility.Collapsed;
    }
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

        ApplySidebar(animate: true);
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

    private string? _sidebarTip;

    /// <summary>
    /// Con la lista de copias cerrada, el botón de las tres rayas lleva un número rojo con las copias activas, como
    /// el aviso de una notificación; su descripción lo dice con palabras.
    /// </summary>
    private void UpdateSidebarBadge()
    {
        int active = Jobs.Count(j => j.IsActive);
        bool show = SidebarButton.Visibility == Visibility.Visible && Sidebar.Visibility != Visibility.Visible && active > 0;
        SidebarBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarBadgeText.Text = active > 9 ? "9+" : active.ToString(System.Globalization.CultureInfo.CurrentCulture);
        _sidebarTip ??= ToolTipService.GetToolTip(SidebarButton) as string ?? ""; // la de siempre (x:Uid)
        string tip = show ? $"{_sidebarTip} · {L.F("Jobs_BadgeTip", active)}" : _sidebarTip;
        if (ToolTipService.GetToolTip(SidebarButton) as string != tip)
        {
            ToolTipService.SetToolTip(SidebarButton, tip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SidebarButton, tip);
        }
    }

    /// <param name="animate">
    /// Al abrirla o cerrarla con su botón: se despliega deslizándose (su columna crece poco a poco, o flota entrando
    /// desde la izquierda con la ventana estrecha) y se recoge igual, en lugar de aparecer y desaparecer de golpe.
    /// </param>
    private void ApplySidebar(bool animate = false)
    {
        bool page = PageOpen;
        bool show = _expanded && !page && SidebarWanted && (_narrow ? _sidebarOverlayOpen : _sidebarWide);
        bool wasShown = Sidebar.Visibility == Visibility.Visible;
        animate &= show != wasShown && AppWindow.IsVisible && !Theming.BarAppearance.ReduceMotion;
        StopSidebarAnimation();
        SidebarButton.Visibility = SidebarWanted && _expanded ? Visibility.Visible : Visibility.Collapsed; // en la pequeña, el paginador
        if (!show)
        {
            if (animate)
            {
                // Se recoge y, al acabar, se oculta.
                AnimateSidebar(opening: false, () =>
                {
                    Sidebar.Visibility = Visibility.Collapsed;
                    SidebarColumn.Width = new GridLength(0);
                    UpdateSidebarBadge();
                });
                return;
            }

            Sidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            UpdateSidebarBadge();
            return; // oculta: no se toca su aspecto (cambiarlo en cada cambio de tamaño solo da trabajo a WinUI)
        }

        Sidebar.Visibility = Visibility.Visible;
        UpdateSidebarBadge();
        if (_narrow)
        {
            SidebarColumn.Width = new GridLength(0);
            Grid.SetColumnSpan(Sidebar, 2);
            Sidebar.Width = SidebarWidth;
            Sidebar.HorizontalAlignment = HorizontalAlignment.Left;
            // Fondo opaco y un borde, sin acrílico ni sombra 3D (ThemeShadow + Translation): esos efectos crean un visual de
            // composición propio que, al ocultarse la lista con la ventana cambiando de tamaño, cerraba WinUI de golpe.
            Sidebar.Background = new SolidColorBrush(ThemeManager.Current.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 36) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
            Sidebar.BorderBrush = ThemeManager.BorderBrush;
            Sidebar.BorderThickness = new Thickness(0, 1, 1, 1);
            Sidebar.CornerRadius = new CornerRadius(0, 8, 8, 0);
            Canvas.SetZIndex(Sidebar, 10);
        }
        else
        {
            SidebarColumn.Width = new GridLength(SidebarWidth);
            Grid.SetColumnSpan(Sidebar, 1);
            Sidebar.Width = double.NaN;
            Sidebar.HorizontalAlignment = HorizontalAlignment.Stretch;
            Sidebar.Background = null;
            Sidebar.BorderThickness = new Thickness(0);
            Sidebar.CornerRadius = new CornerRadius(0);
            Canvas.SetZIndex(Sidebar, 0);
        }

        if (animate)
        {
            AnimateSidebar(opening: true, null);
        }
    }

    private Action? _sidebarDone;

    /// <summary>
    /// Despliega o recoge la lista de copias sin que el texto parpadee: la columna de la lista toma su ancho de una vez
    /// (el contenido se coloca una sola vez, no a cada fotograma) y lo que se mueve es solo la imagen ya dibujada.
    /// Al abrir, el contenido llega desde donde estaba empujado por la lista, que entra desde la izquierda; al cerrar,
    /// la lista se va y el contenido se desliza a ocupar su sitio. Flotando, la lista entra o sale deslizándose.
    /// </summary>
    private void AnimateSidebar(bool opening, Action? done)
    {
        if (_narrow)
        {
            _sidebarDone = done;
            Motion.SlideX(Sidebar, opening ? -SidebarWidth : 0, opening ? 0 : -SidebarWidth, opening ? 280 : 200, () =>
            {
                var finish = _sidebarDone;
                _sidebarDone = null;
                finish?.Invoke();
            });
            return;
        }

        if (opening)
        {
            Motion.SlideX(Detail, -SidebarWidth, 0, 300, null);
            Motion.SlideX(Sidebar, -SidebarWidth, 0, 300, null); // los dos juntos, como una sola hoja: no se solapan
            done?.Invoke();
            return;
        }

        done?.Invoke(); // la columna se cierra ya
        Motion.SlideX(Detail, SidebarWidth, 0, 240, null);
    }

    /// <summary>Si la lista estaba a medio desplegarse o recogerse, la deja ya en su sitio.</summary>
    private void StopSidebarAnimation()
    {
        var finish = _sidebarDone;
        _sidebarDone = null;
        finish?.Invoke();
        Motion.ResetSlide(Sidebar);
        Motion.ResetSlide(Detail);
    }

    /// <summary>Tocar fuera de la lista flotante la cierra.</summary>
    private void Detail_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_narrow && _sidebarOverlayOpen)
        {
            _sidebarOverlayOpen = false;
            ApplySidebar(animate: true);
        }
    }

    /// <summary>Sin copias, con copias, o una página (Historial, Ajustes) encima.</summary>
    private void UpdateLayoutState()
    {
        bool page = PageOpen;
        bool empty = Jobs.Count == 0;
        EmptyState.Visibility = !page && empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty != _wasEmpty)
        {
            // De «sin copias» a «copiando» (o al revés), la ventana compacta cambia de alto.
            _wasEmpty = empty;
            if (!_expanded && AppWindow.IsVisible)
            {
                AnimateResize();
            }
        }

        Detail.Visibility = !page && !empty ? Visibility.Visible : Visibility.Collapsed;
        bool sidebar = SidebarWanted;
        if (sidebar != _hadSidebar)
        {
            // De una copia a varias (o al revés), la vista completa gana (o devuelve) el sitio de la lista de copias.
            _hadSidebar = sidebar;
            if (_expanded && AppWindow.IsVisible && sidebar && (AppWindow.Size.Width < Scaled(MultiWidth) || AppWindow.Size.Height < Scaled(MultiHeight)))
            {
                double scale = Scaled(1000) / 1000.0;
                _beforeSidebar = (AppWindow.Size.Width / scale, AppWindow.Size.Height / scale);
                AnimateResize();
                _sidebarGrownTo = _resizeTo;
            }
            else if (!sidebar && _beforeSidebar is { } before)
            {
                // Vuelve al tamaño de antes (el de serie o el que le diste), salvo que la cambiaras mientras tanto.
                _beforeSidebar = null;
                bool untouched = Math.Abs(AppWindow.Size.Width - _sidebarGrownTo.Width) <= 2
                    && Math.Abs(AppWindow.Size.Height - _sidebarGrownTo.Height) <= 2;
                if (_expanded && AppWindow.IsVisible && untouched)
                {
                    AnimateResizeTo(before.Width, before.Height);
                }
            }
            else if (!sidebar && _expanded && AppWindow.IsVisible)
            {
                // Abrió ya con varias copias (no creció en esta sesión): si sigue con el tamaño justo de la lista de
                // copias, pasa al de la vista de una copia.
                var single = TargetSize(true);
                if (Math.Abs(AppWindow.Size.Width - Scaled(Math.Max(single.Width, MultiWidth))) <= 2
                    && Math.Abs(AppWindow.Size.Height - Scaled(Math.Max(single.Height, MultiHeight))) <= 2)
                {
                    AnimateResize();
                }
            }
        }

        ApplySidebar();
        JobsHeader.Text = L.F("Jobs_Header", Jobs.Count, Jobs.Count(j => j.IsActive));
        UpdatePager();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Pestañas

    private SelectorBarItem? _shownTab;

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // La línea de la pestaña elegida se desliza desde la anterior.
        if (_shownTab is { } previous && sender.SelectedItem is { } next && previous != next
            && Pill(previous) is { } from && Pill(next) is { } to)
        {
            Motion.SlideIndicator(from, to, vertical: false);
        }

        _shownTab = sender.SelectedItem;
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

    /// <summary>La línea de debajo de una pestaña (parte de su plantilla).</summary>
    private static FrameworkElement? Pill(SelectorBarItem item) =>
        Responsive.Descendants<Microsoft.UI.Xaml.Shapes.Rectangle>(item).FirstOrDefault(r => r.Name == "PART_SelectionVisual");

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

        // Red de seguridad: una copia de esta ventana que no está en su lista (le llegó mientras otra ventana se cerraba)
        // aparece en ella; si no, se veía «No hay copias» con la copia en marcha.
        foreach (var job in _app.Queue.Jobs)
        {
            if (job.IsActive && _app.WindowFor(job) == this && !Jobs.Any(j => j.Job == job))
            {
                AddJob(job, select: Jobs.Count == 0);
            }
        }

        UpdateSidebarBadge();

        if (TextAudit.Enabled)
        {
            string where = HistoryView.Visibility == Visibility.Visible ? "Historial"
                : SettingsView.Visibility == Visibility.Visible ? "Ajustes"
                : TimelineView.Visibility == Visibility.Visible ? "Línea de tiempo"
                : CompareView.Visibility == Visibility.Visible ? "Comparar"
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
        else if (TabHost.Content is FilesTab files)
        {
            files.Refresh(); // una copia que esperaba turno y acaba de empezar: ya tiene su lista
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Barra de herramientas

    /// <summary>«Copiar…»: un solo selector para archivos y carpetas (una carpeta marcada se copia entera).</summary>
    private async void CopyFiles_Click(object sender, RoutedEventArgs e)
    {
        var paths = Pickers.PickAny(L.Get("Picker_CopyTitle"));
        if (paths.Count > 0)
        {
            await PickDestinationAndCopy(paths, move: false);
        }
    }

    private async void MoveFiles_Click(object sender, RoutedEventArgs e)
    {
        var paths = Pickers.PickAny(L.Get("Picker_MoveTitle"));
        if (paths.Count > 0)
        {
            await PickDestinationAndCopy(paths, move: true);
        }
    }

    private void LoadList_Click(object sender, RoutedEventArgs e) => LoadListAsync();

    /// <summary>
    /// Carga una lista guardada (.mclist) y empieza a copiar en seguida lo que le quedaba, al mismo destino y con las
    /// mismas opciones. Si ese destino ya no está (una memoria USB que se quitó...), pregunta si copiar el resto en otro
    /// sitio.
    /// </summary>
    internal async void LoadListAsync()
    {
        if (await Pickers.PickFileAsync(".mclist") is not { } path)
        {
            return;
        }

        await RunListAsync(path);
    }

    internal async Task RunListAsync(string path)
    {
        if (Reports.LoadList(path) is not { } list)
        {
            _app.Notify(L.Get("Files_LoadFailed"), path);
            return;
        }

        string? destination = null;
        if (!Path.IsPathFullyQualified(list.Destination))
        {
            destination = await Pickers.PickFolderAsync(L.Get("Picker_CopyHere"));
            if (destination is null)
            {
                return;
            }
        }
        else if (!DestinationAvailable(list.Destination))
        {
            if (!await AskOtherDestinationAsync(list.Destination))
            {
                return;
            }

            destination = await Pickers.PickFolderAsync(L.Get("Picker_CopyHere"));
            if (destination is null)
            {
                return;
            }
        }

        if (_app.RunList(path, destination) is not null)
        {
            ShowJobs();
        }
    }

    /// <summary>La unidad (o el recurso de red) del destino sigue ahí; la carpeta en sí se crea si falta.</summary>
    private static bool DestinationAvailable(string destination)
    {
        try
        {
            string? root = Path.GetPathRoot(destination);
            return string.IsNullOrEmpty(root) || Directory.Exists(root) || Directory.Exists(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>«La carpeta de destino original ya no existe. ¿Copiar el resto en otro lugar?»</summary>
    private async Task<bool> AskOtherDestinationAsync(string destination)
    {
        await _dialogGate.WaitAsync();
        try
        {
            ShowAndActivate();
            Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
            var dialog = new ContentDialog
            {
                Title = L.Get("ListMissing_Title"),
                Content = new StackPanel
                {
                    Spacing = 10,
                    MaxWidth = 460,
                    Children =
                    {
                        new TextBlock { Text = L.Get("ListMissing_Message"), TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = destination, Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                    },
                },
                PrimaryButtonText = L.Get("ListMissing_Choose"),
                CloseButtonText = L.Get("ListMissing_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogGate.Release();
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

    private void Shelf_Click(object sender, RoutedEventArgs e) => _app.ShowShelf();

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } left && await Pickers.PickFolderAsync(L.Get("Compare_PickRight")) is { } right)
        {
            ShowCompare(left, right);
        }
    }

    /// <summary>Abre "Comparar y sincronizar" con estas dos carpetas (también desde las pruebas).</summary>
    internal void ShowCompare(string left, string right)
    {
        RememberBeforePage();
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        TimelineView.Visibility = Visibility.Collapsed;
        CompareView.Show(left, right);
        CompareView.Visibility = Visibility.Visible;
        EnsureRoomForPage();
        UpdateLayoutState();
        RememberNavigation();
    }

    private void Timeline_Click(object sender, RoutedEventArgs e)
    {
        RememberBeforePage();
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        CompareView.Visibility = Visibility.Collapsed;
        TimelineView.Show(Jobs);
        TimelineView.Visibility = Visibility.Visible;
        EnsureRoomForPage();
        UpdateLayoutState();
        RememberNavigation();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        RememberBeforePage();
        SettingsView.Visibility = Visibility.Collapsed;
        TimelineView.Visibility = Visibility.Collapsed;
        CompareView.Visibility = Visibility.Collapsed;
        HistoryView.Reload();
        HistoryView.Visibility = Visibility.Visible;
        EnsureRoomForPage();
        UpdateLayoutState();
        RememberNavigation();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsView.Visibility == Visibility.Visible) { ShowJobs(); return; }
        OpenSettings();
    }

    private void OpenSettings()
    {
        RememberBeforePage();
        HistoryView.Visibility = Visibility.Collapsed;
        TimelineView.Visibility = Visibility.Collapsed;
        CompareView.Visibility = Visibility.Collapsed;
        SettingsView.Load();
        SettingsView.Visibility = Visibility.Visible;
        EnsureRoomForPage(SettingsWidth);
        UpdateLayoutState();
        RememberNavigation();
    }

    /// <summary>
    /// Historial, Ajustes y las demás páginas necesitan la ventana grande; Ajustes, además, el ancho de sus tarjetas
    /// (hasta donde llegan, sin estirarse más) para que se lea todo de un vistazo. Lo justo para que quepa en la pantalla.
    /// </summary>
    private void EnsureRoomForPage(double? width = null)
    {
        bool fromCompact = !_expanded;
        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (fromCompact && AppWindow.IsVisible && !maximized)
        {
            // De la ventana pequeña a una página ancha (Ajustes): crece animada, como al encoger.
            SetExpanded(true, userChoice: false);
            double scale = Scaled(1000) / 1000.0;
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var expanded = TargetSize(true);
            double w = Math.Min(Math.Max(width ?? 0, expanded.Width), (area.Width / scale) - 24);
            double h = Math.Min(expanded.Height, (area.Height / scale) - 24);
            AnimateResizeTo(w, h);
            return;
        }

        if (fromCompact)
        {
            SetExpanded(true, userChoice: false);
        }

        if (width is not { } wanted || maximized)
        {
            return;
        }

        double scale2 = Scaled(1000) / 1000.0;
        var area2 = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double current = (_resizeTimer.IsRunning ? _resizeTo.Width : AppWindow.Size.Width) / scale2;
        double target = Math.Min(wanted, (area2.Width / scale2) - 24);
        if (target > current + 1)
        {
            double height = fromCompact ? TargetSize(true).Height : (_resizeTimer.IsRunning ? _resizeTo.Height : AppWindow.Size.Height) / scale2;
            AnimateResizeTo(target, Math.Min(height, (area2.Height / scale2) - 24));
        }
    }

    /// <summary>Cómo estaba la ventana antes de abrir una página: al volver, recupera ese tamaño (y la vista pequeña).</summary>
    private (bool Expanded, double Width, double Height)? _beforePage;

    /// <summary>Se apunta al abrir la primera página (si ya había una abierta, se conserva lo de antes).</summary>
    private void RememberBeforePage()
    {
        if (!PageOpen && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            _beforePage = (_expanded, _resizeTimer.IsRunning ? _resizeTo.Width : AppWindow.Size.Width, _resizeTimer.IsRunning ? _resizeTo.Height : AppWindow.Size.Height);
        }
    }

    /// <summary>Al cerrar la página, la ventana vuelve a como estaba (pequeña, o grande con su tamaño de antes).</summary>
    private void RestoreAfterPage()
    {
        if (_beforePage is not { } before || PageOpen)
        {
            return;
        }

        _beforePage = null;
        if (!before.Expanded)
        {
            SetExpanded(false, userChoice: false);
        }
        else if (AppWindow.IsVisible && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Maximized }
            && (Math.Abs(AppWindow.Size.Width - before.Width) > 2 || Math.Abs(AppWindow.Size.Height - before.Height) > 2))
        {
            double scale = Scaled(1000) / 1000.0;
            AnimateResizeTo(before.Width / scale, before.Height / scale);
        }
    }

    /// <summary>Ancho de Ajustes: la lista de secciones y las tarjetas enteras (con sus márgenes y la barra de desplazamiento).</summary>
    private const double SettingsWidth = 260 + 24 + 980 + 24 + 14;

    private void About_Click(object sender, RoutedEventArgs e) => AboutWindow.Show(this);

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
    /// <summary>
    /// Vuelve a dibujar toda la ventana. WinUI, tras cambiar el tamaño de la ventana o con animaciones, a veces deja
    /// textos, iconos y botones dibujados apagados (como desactivados) hasta pasar el ratón por encima: cambiar el
    /// tema claro/oscuro y volverlo a poner en el mismo instante obliga a dibujarlo todo de nuevo, sin parpadeo.
    /// </summary>
    internal void Redraw()
    {
        var theme = Root.RequestedTheme;
        Root.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = theme;
    }

    /// <summary>Solo pruebas: abre el menú de un botón por su nombre accesible.</summary>
    internal bool DebugOpenMenu(string name)
    {
        var button = Responsive.Descendants<Button>(Root).FirstOrDefault(b => (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == name || ToolTipService.GetToolTip(b) as string == name) && b.Flyout is not null);
        button?.Flyout.ShowAt(button);
        if (button is null && Responsive.Descendants<ComboBox>(Root).FirstOrDefault(c => c.Name == name) is { } combo)
        {
            combo.IsDropDownOpen = true; // o una lista desplegable, por su nombre en el XAML
            return true;
        }

        return button is not null;
    }

    internal bool DebugClick(string name)
    {
        var button = Responsive.Descendants<Microsoft.UI.Xaml.Controls.Primitives.ButtonBase>(Root).FirstOrDefault(b => b.Name == name
            || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == name || ToolTipService.GetToolTip(b) as string == name);
        if (button is null && Responsive.Descendants<ListViewItem>(Root).FirstOrDefault(i => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(i) == name) is { } item)
        {
            item.IsSelected = true; // o un elemento de una lista (las secciones de Ajustes)
            return true;
        }

        if (button is null || Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button) is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }

    /// <summary>Solo pruebas: foto de lo que XAML dibuja (RenderTargetBitmap), para compararla con la pantalla.</summary>
    internal async Task DebugRenderAsync(string path)
    {
        var shot = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await shot.RenderAsync(Root);
        var pixels = await shot.GetPixelsAsync();
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)shot.PixelWidth, (uint)shot.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
        await encoder.FlushAsync();
        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.AsStreamForRead().ReadExactlyAsync(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }

    /// <summary>Solo pruebas: estado visual de los botones del panel (para ver por qué se dibujan apagados).</summary>
    internal string DebugStates()
    {
        var lines = new List<string>();
        foreach (var button in Responsive.Descendants<Microsoft.UI.Xaml.Controls.Primitives.ButtonBase>(Panel))
        {
            string name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button);
            var root = VisualTreeHelper.GetChildrenCount(button) > 0 ? VisualTreeHelper.GetChild(button, 0) as FrameworkElement : null;
            string states = root is null ? "" : string.Join(",", VisualStateManager.GetVisualStateGroups(root).Select(g => $"{g.Name}={g.CurrentState?.Name}"));
            string fg = button.Foreground is SolidColorBrush s ? s.Color.ToString() : button.Foreground?.GetType().Name ?? "";
            lines.Add($"{name}|en={button.IsEnabled}|over={button.IsPointerOver}|pressed={button.IsPressed}|op={button.Opacity}|fg={fg}|w={button.ActualWidth:R}|h={button.ActualHeight:R}|{states}");
        }

        foreach (var element in Responsive.Descendants<UIElement>(Panel).Where(e => e.Opacity < 1))
        {
            lines.Add($"opacidad {element.Opacity}: {element.GetType().Name} {(element as FrameworkElement)?.Name}");
        }

        return string.Join("\n", lines);
    }

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
        _ = _app.AddToJobAsync(Jobs[spot.OverIndex].Job, paths);
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
            string what = DragItems.What(e.DataView);
            e.DragUIOverride.Caption = what.Length > 0 ? L.F("Drop_AddWhatTo", what, job.Title) : L.F("Drop_AddToNamedJob", job.Title);
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
        // El orden de la cola, solo con las copias de esta ventana.
        var order = _app.Queue.Jobs.Where(job => Jobs.Any(j => j.Job == job)).ToList();
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

    // Soltar en cualquier parte de la copia que se está viendo (el panel, las pestañas; también en la ventana compacta):
    // se añade al final de esa copia, al mismo destino. Sobre la lista de archivos se elige la posición exacta.

    private void PanelCard_DragOver(object sender, DragEventArgs e)
    {
        if (_selected is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Copy;
        string what = DragItems.What(e.DataView);
        e.DragUIOverride.Caption = what.Length > 0 ? L.F("Drop_AddWhatHere", what) : L.Get("Drop_AddToJob");
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
                var target = await _app.AddToJobAsync(job.Job, paths);
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


    internal async Task<bool> PickDestinationAndCopy(IReadOnlyList<string> sources, bool move)
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
        DestinationsZip.IsChecked = false;
        DestinationsFormat.SelectedIndex = 0;
        DestinationsFormat.Visibility = Visibility.Collapsed;
        DestinationsEncrypt.IsChecked = false;
        DestinationsEncryptPanel.Visibility = Visibility.Collapsed;
        DestinationsPassword.Password = "";
        DestinationsPassword2.Password = "";
        DestinationsPasswordInfo.Text = L.Get("Destinations_PasswordHint");
        DestinationsPasswordInfo.ClearValue(TextBlock.ForegroundProperty);
        DestinationsNone.Visibility = Visibility.Collapsed;
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
                return false;
            }

            DestinationsList.Items.Add(DestinationItem(first, favorite: false));
            DestinationsList.SelectedItems.Add(DestinationsList.Items[^1]);
        }

        DestinationsDialog.XamlRoot = Root.XamlRoot;
        DestinationsDialog.RequestedTheme = Root.RequestedTheme;
        if (await DestinationsDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        var chosen = DestinationsList.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
        if (chosen.Count == 0)
        {
            return false;
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
            Zip = DestinationsZip.IsChecked == true ? true : null,
            ArchiveFormat = DestinationsZip.IsChecked == true && DestinationsFormat.SelectedIndex == 1 ? Engine.IO.ArchiveFormat.SevenZip : null,
            Encrypt = DestinationsEncrypt.IsChecked == true ? true : null,
            Password = DestinationsEncrypt.IsChecked == true ? DestinationsPassword.Password : null,
        });

        return true;
    }

    /// <summary>La carpeta es uno de los orígenes o está dentro de uno (no tiene sentido copiar ahí).</summary>
    private static bool IsInsideAny(string folder, IEnumerable<string> sources)
    {
        string candidate = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return sources.Any(s => candidate.StartsWith(Path.TrimEndingDirectorySeparator(s) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static ListViewItem DestinationItem(string folder, bool favorite, bool explorer = false)
    {
        // Rutas enteras (en varias líneas si hace falta): para elegir destino hay que verlas completas.
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new FontIcon { Glyph = explorer ? "" : favorite ? "" : "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var item = new ListViewItem { Content = row, Tag = folder, HorizontalContentAlignment = HorizontalAlignment.Stretch };
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

    /// <summary>«Comprobar…»: archivos de sumas o carpetas (se buscan dentro los archivos de sumas).</summary>
    private void TestFiles_Click(object sender, RoutedEventArgs e)
    {
        var paths = Pickers.PickAny(
            L.Get("Picker_TestTitle"),
            (L.Get("Picker_ChecksumFiles"), "*.sha256;*.sha1;*.sha512;*.md5;*.sfv;*.xxh3;*.xxh64;*.xxh128;*.sha3-256;*.sha3-512;*.b3;*.blake3"));
        if (paths.Count > 0)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Test, Sources = [.. paths] });
        }
    }

    // Crear archivo de sumas (con el algoritmo de Ajustes; SHA-256 si no hay ninguno)

    // Cifrar y descifrar

    private void DestinationsEncrypt_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationsPasswordInfo is null)
        {
            return; // aún cargando la ventana
        }

        DestinationsFormat.Visibility = DestinationsZip.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DestinationsEncryptPanel.Visibility = DestinationsEncrypt.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DestinationsPasswordInfo.Text = L.Get(DestinationsZip.IsChecked != true ? "Destinations_PasswordHint" : DestinationsFormat.SelectedIndex == 1 ? "Destinations_PasswordHint7z" : "Destinations_PasswordHintZip");
        DestinationsPasswordInfo.ClearValue(TextBlock.ForegroundProperty);
    }

    /// <summary>Con cifrado, la contraseña debe tener 8 caracteres o más y repetirse igual (si no, el diálogo sigue abierto).</summary>
    private void Destinations_PrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Sin destino marcado, "Empezar" no puede cerrar el diálogo sin hacer nada.
        if (DestinationsList.SelectedItems.Count == 0)
        {
            args.Cancel = true;
            DestinationsNone.Foreground = ThemeManager.ErrorBrush;
            DestinationsNone.Visibility = Visibility.Visible;
            return;
        }

        DestinationsNone.Visibility = Visibility.Collapsed;
        if (DestinationsEncrypt.IsChecked != true)
        {
            return;
        }

        string? problem = DestinationsPassword.Password.Length < 8 ? L.Get("Destinations_PasswordShort")
            : DestinationsPassword.Password != DestinationsPassword2.Password ? L.Get("Destinations_PasswordMismatch")
            : null;
        if (problem is not null)
        {
            args.Cancel = true;
            DestinationsPasswordInfo.Text = problem;
            DestinationsPasswordInfo.Foreground = ThemeManager.ErrorBrush;
        }
    }

    /// <summary>Pide una contraseña (descifrar, o volver a darla tras reiniciar). Null si se cancela.</summary>
    internal async Task<string?> AskPasswordAsync(string message)
    {
        PasswordMessage.Text = message;
        PasswordBox.Password = "";
        PasswordDialog.XamlRoot = Root.XamlRoot;
        PasswordDialog.RequestedTheme = Root.RequestedTheme;
        ShowAndActivate();
        return await PasswordDialog.ShowAsync() == ContentDialogResult.Primary && PasswordBox.Password.Length > 0 ? PasswordBox.Password : null;
    }

    /// <summary>Descifrar los .mcenc de una carpeta (por ejemplo, un USB) en otra.</summary>
    private async void Decrypt_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is not { } from
            || await Pickers.PickFolderAsync(L.Get("Picker_DecryptHere")) is not { } to
            || await AskPasswordAsync(L.F("Decrypt_Ask", from)) is not { } password)
        {
            return;
        }

        Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Decrypt, Sources = [from], Destination = to, Password = password });
    }

    /// <summary>Copia de seguridad sin duplicados: una carpeta como instantánea nueva en un repositorio.</summary>
    private async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is not { } source
            || await Pickers.PickFolderAsync(L.Get("Picker_Repository")) is not { } repository)
        {
            return;
        }

        Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Snapshot, Sources = [source], Destination = repository });
    }

    /// <summary>Restaurar: se elige el repositorio, la instantánea (por fecha) y dónde dejarla.</summary>
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync(L.Get("Picker_Repository")) is not { } repository)
        {
            return;
        }

        var snapshots = new Engine.Backup.BackupRepository(repository).Snapshots();
        if (snapshots.Count == 0)
        {
            _app.Notify(L.Get("Restore_Title"), L.F("Restore_None", repository));
            return;
        }

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, L.Get("Restore_Title"));
        foreach (var snapshot in Enumerable.Reverse(snapshots))
        {
            string label = L.F("Restore_Item", snapshot.CreatedUtc.ToLocalTime().ToString("g"), snapshot.Files.Count, ViewModels.Format.Bytes(snapshot.TotalBytes));
            list.Items.Add(new ListViewItem { Content = label, Tag = snapshot.Id });
        }

        list.SelectedIndex = 0;
        var dialog = new ContentDialog
        {
            Title = L.Get("Restore_Title"),
            Content = list,
            PrimaryButtonText = L.Get("Restore_Choose"),
            CloseButtonText = L.Get("SendDialog/CloseButtonText"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.RequestedTheme,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || list.SelectedItem is not ListViewItem { Tag: string id }
            || await Pickers.PickFolderAsync(L.Get("Picker_RestoreHere")) is not { } target)
        {
            return;
        }

        Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Restore, Sources = [repository], Destination = target, SnapshotId = id });
    }

    /// <summary>Revisa una copia con datos de recuperación y reconstruye lo que el disco haya estropeado.</summary>
    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Repair, Sources = [folder], Destination = folder });
        }
    }

    // Enviar a otro equipo con MegaCopier

    /// <summary>«Enviar a otro equipo…»: archivos y carpetas con el mismo selector.</summary>
    private async void SendFiles_Click(object sender, RoutedEventArgs e)
    {
        var paths = Pickers.PickAny(L.Get("Picker_SendTitle"));
        if (paths.Count > 0)
        {
            await SendToPeerAsync(paths);
        }
    }

    /// <summary>Elegir el equipo (los que se encuentran en la red, o su nombre o IP) y el código que muestra.</summary>
    internal async Task SendToPeerAsync(IReadOnlyList<string> sources)
    {
        SendSummary.Text = sources.Count == 1
            ? L.F("Send_SummaryOne", Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0])))
            : L.F("Send_SummaryMany", sources.Count);
        SendPeers.Items.Clear();
        SendHostBox.Text = _app.Settings.LastPeer;
        SendCodeBox.Text = "";
        SendCodeBox.IsEnabled = true;
        SendSearching.Text = L.Get("Send_Searching");
        SendDialog.XamlRoot = Root.XamlRoot;
        SendDialog.RequestedTheme = Root.RequestedTheme;
        _ = SearchPeersAsync();
        if (await SendDialog.ShowAsync() != ContentDialogResult.Primary || SendHostBox.Text.Trim().Length == 0)
        {
            return;
        }

        // LocalSend no usa código: se acepta en la pantalla del otro equipo.
        bool localSend = Engine.Network.LocalSendSender.IsDestination(SendHostBox.Text.Trim());
        if (!localSend && SendCodeBox.Text.Trim().Length == 0)
        {
            return;
        }

        _app.Settings.LastPeer = SendHostBox.Text.Trim();
        _app.Settings.Save();
        Submit(new IpcRequest
        {
            Command = "copy",
            Operation = Engine.JobOperation.Send,
            Sources = [.. sources],
            Destination = SendHostBox.Text.Trim(),
            PeerCode = SendCodeBox.Text.Trim(),
        });
    }

    private async Task SearchPeersAsync()
    {
        try
        {
            // Equipos con MegaCopier y, a la vez, móviles y equipos con LocalSend (en pruebas no: la multidifusión
            // haría saltar el aviso del cortafuegos).
            var localSend = App.TestMode ? Task.FromResult(new List<Engine.Network.LocalSendDevice>())
                : Engine.Network.LocalSendSender.DiscoverAsync(Environment.MachineName, TimeSpan.FromSeconds(2));
            var found = await Engine.Network.PeerDiscovery.SearchAsync(TimeSpan.FromSeconds(1.5));
            foreach (var peer in found.Where(p => p.Name != Environment.MachineName || App.TestMode))
            {
                var item = new ListViewItem { Content = $"{peer.Name}  ({peer.Address})", Tag = $"{peer.Address}:{peer.Port}" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, peer.Name);
                SendPeers.Items.Add(item);
            }

            try
            {
                foreach (var device in (await localSend).Where(d => d.Fingerprint != _app.Settings.LocalSendFingerprint)) // no este mismo PC
                {
                    string label = L.F("Send_LocalSendDevice", device.Alias, device.DeviceModel ?? "", device.Address);
                    var item = new ListViewItem { Content = label, Tag = device.Destination };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label);
                    SendPeers.Items.Add(item);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                // El puerto de LocalSend puede estar ocupado por la propia app LocalSend: se sigue sin ella.
            }

            SendSearching.Text = SendPeers.Items.Count == 0 ? L.Get("Send_NoneFound") : "";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            SendSearching.Text = L.Get("Send_NoneFound");
        }
    }

    private void SendPeers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SendPeers.SelectedItem is ListViewItem { Tag: string address })
        {
            SendHostBox.Text = address;
            SendCodeBox.IsEnabled = !Engine.Network.LocalSendSender.IsDestination(address);
        }
    }

    /// <summary>«Crear archivo de sumas…»: de archivos sueltos o de carpetas enteras.</summary>
    private void HashFiles_Click(object sender, RoutedEventArgs e)
    {
        var paths = Pickers.PickAny(L.Get("Picker_HashTitle"));
        if (paths.Count > 0)
        {
            Submit(new IpcRequest { Command = "copy", Operation = Engine.JobOperation.Hash, Sources = [.. paths] });
        }
    }

    // Borrar

    private void Delete_Click(object sender, RoutedEventArgs e) => _ = DeleteAsync(secure: false);

    private void Wipe_Click(object sender, RoutedEventArgs e) => _ = DeleteAsync(secure: true);

    private async Task DeleteAsync(bool secure)
    {
        var paths = Pickers.PickAny(L.Get(secure ? "Picker_WipeTitle" : "Picker_DeleteTitle"));
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
        foreach (var window in _app.AllWindows)
        {
            window.UpdateFinishButton(); // «Al terminar» es de toda la aplicación: igual en todas las ventanas
        }
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
            FinishAction.Hibernate => ("", L.Get("Finish_LabelHibernate")),
            FinishAction.Shutdown => ("", L.Get("Finish_LabelShutdown")),
            FinishAction.SignOut => ("", L.Get("Finish_LabelSignOut")),
            FinishAction.ExitApp => ("", L.Get("Finish_LabelExit")),
            _ => ("", L.Get("Finish_Label")),
        };
        if (Toolbar.ActualWidth > 0)
        {
            FitFinishText(Toolbar.ActualWidth); // otro texto, otro ancho
        }
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

    /// <summary>Copias que ya avisaron con sonido de que hace falta decidir algo.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CopyJob, object> _attentionPlayed = [];

    /// <summary>
    /// El sonido de «hace falta tu decisión», solo la primera vez en cada copia: con «Uno a uno», cada archivo repetido
    /// volvía a sonar.
    /// </summary>
    private void PlayAttentionOnce(CopyJob job)
    {
        if (_attentionPlayed.TryAdd(job, job))
        {
            Sounds.Play(_app.Settings.Sounds, SoundEvent.Attention);
        }
    }

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
                    await WaitForRootAsync();
                    FillConflictDialog(conflict, job.Record.Move);
                    bool singleFile = job.Record.Sources.Count == 1 && File.Exists(job.Record.Sources[0])
                        && job.Record.ExtraTargets.Count == 0;
                    ConflictApplyAll.Visibility = singleFile ? Visibility.Collapsed : Visibility.Visible;
                    ConflictRenameExisting.Visibility = singleFile ? Visibility.Collapsed : Visibility.Visible;
                    PlayAttentionOnce(job);
                    _renameExisting = false;
                    ConflictDialog.XamlRoot = Root.XamlRoot;
                    ConflictDialog.RequestedTheme = Root.RequestedTheme;
                    bool grow = !_expanded;
                    _shrinkTicket++; // si la ventana iba a encoger tras el diálogo anterior, ya no
                    if (grow && _roomForDialog is null)
                    {
                        _roomForDialog = (CompactWidth + 40, 640); // con sitio para las miniaturas, también para los límites
                        AnimateResizeTo(CompactWidth + 40, 640);
                    }

                    using var registration = cancellation.Register(() => _dispatcher.TryEnqueue(ConflictDialog.Hide));
                    var result = await ConflictDialog.ShowAsync();
                    if (grow)
                    {
                        _ = ShrinkAfterDialogAsync(++_shrinkTicket);
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
        // Invisible pero en su sitio, para que el diálogo no cambie de alto de un archivo a otro.
        ConflictIdentical.Opacity = conflict.SourceLength == conflict.DestinationLength
            && (conflict.SourceLastWriteUtc - conflict.DestinationLastWriteUtc).Duration() <= TimeSpan.FromSeconds(2) ? 1 : 0;
        ConflictApplyAll.IsChecked = false;
        _ = ShowThumbnailAsync(ConflictSourceThumb, conflict.Source);
        _ = ShowThumbnailAsync(ConflictDestinationThumb, conflict.Destination);
    }

    /// <summary>
    /// Cómo lo enseña Windows: la miniatura en fotos y vídeos; en lo demás (PDF, música...), el icono de su programa.
    /// </summary>
    private static async Task ShowThumbnailAsync(Image image, string path)
    {
        image.Source = null; // el hueco se queda: el diálogo no cambia de tamaño de un archivo a otro
        image.Tag = path;
        var bitmap = await ShellImage.LoadAsync(path, 192);
        if (bitmap is not null && Equals(image.Tag, path)) // si mientras tanto llegó otro archivo, vale el nuevo
        {
            image.Source = bitmap;
        }
    }
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Escala de los tamaños de la ventana: el DPI de la pantalla y, en pantallas pequeñas, <see cref="UiScale"/>.</summary>
    private int Scaled(double value) => (int)(value * GetDpiForWindow(Handle) / 96.0 * UiScale);

    // ---------------------------------------------------------------------------------------------------------
    // Pantallas pequeñas

    /// <summary>
    /// Alto que necesita la ventana completa para verse entera (con la barra de título y un margen). En pantallas con
    /// menos sitio (1366 × 768, portátiles pequeños) todo se dibuja algo más pequeño, en proporción: el mismo diseño,
    /// con letras y botones un poco menores. En pantallas grandes la escala es 1 y nada cambia.
    /// </summary>
    private const double ComfortHeight = 840;

    /// <summary>Lo más que se encoge (por debajo, los textos ya costarían de leer).</summary>
    private const double MinUiScale = 0.8;

    /// <summary>Escala de toda la ventana (1 = tamaño normal).</summary>
    internal double UiScale { get; private set; } = 1;

    private Canvas? _scaleHost;

    /// <summary>
    /// Pone la ventana dentro de un lienzo para poder escalarla entera: el contenido se coloca con el tamaño que tendría
    /// sin escalar y una transformación lo reduce hasta llenar la ventana (el lienzo no lo recorta).
    /// </summary>
    private void PrepareUiScale()
    {
        var root = Root;
        Content = null;
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        _scaleHost = new Canvas { Background = root.Background };
        _scaleHost.Children.Add(root);
        _scaleHost.SizeChanged += (_, _) => FitScaledRoot();
        Content = _scaleHost;
        UpdateUiScale();
    }

    /// <summary>Recalcula la escala para la pantalla donde está la ventana. Devuelve si cambió.</summary>
    private bool UpdateUiScale()
    {
        double scale = 1;
        if (_app.Settings.AdaptSmallScreens)
        {
            double dpi = GetDpiForWindow(Handle) / 96.0;
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            scale = Math.Clamp(Math.Round(area.Height / dpi / ComfortHeight, 2), MinUiScale, 1);
        }

        if (App.TestMode && double.TryParse(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UISCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double forced))
        {
            scale = Math.Clamp(forced, MinUiScale, 1);
        }

        if (Math.Abs(scale - UiScale) < 0.005)
        {
            return false;
        }

        UiScale = scale;
        Root.RenderTransform = scale >= 1 ? null : new ScaleTransform { ScaleX = scale, ScaleY = scale };
        FitScaledRoot();
        return true;
    }

    /// <summary>«Ajustar a pantallas pequeñas» cambió: se recalcula la escala y la ventana se recoloca.</summary>
    internal void RefreshUiScale()
    {
        if (UpdateUiScale() && AppWindow.IsVisible)
        {
            ApplyLayout(animate: false);
        }
    }

    private void FitScaledRoot()
    {
        if (_scaleHost is not { } host)
        {
            return;
        }

        Root.Width = host.ActualWidth / UiScale;
        Root.Height = host.ActualHeight / UiScale;
    }


    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            area.X + ((area.Width - AppWindow.Size.Width) / 2),
            area.Y + ((area.Height - AppWindow.Size.Height) / 2)));
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern nint SetClassLongPtr(nint hwnd, int index, nint value);

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
