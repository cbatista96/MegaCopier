using System.Diagnostics;
using MegaCopier.Engine.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;
using MegaCopier.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MegaCopier.App;

/// <summary>
/// Proceso residente: vive en la bandeja, recibe peticiones por la tubería con nombre y ejecuta la cola.
/// La ventana se crea oculta al arrancar para que mostrarla sea inmediato cuando llega una copia.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _tray;
    private TrayProgressIcons? _trayProgress;
    private IpcServer? _server;
    private PasteInterceptor? _paste;
    private DragInterceptor? _drag;
    private TrayMenuWindow? _trayMenu;

    /// <summary>Copias a las que ya se les pidió permiso de administrador (no se insiste si vuelve a fallar).</summary>
    private readonly HashSet<string> _adminRetried = [];
    private TaskbarProgress? _taskbar;
    private DispatcherQueueTimer? _statusTimer;

    internal IntegrityGuard? Guard { get; private set; }

    internal Comfort? Comfort { get; private set; }

    internal PeerService? Peers { get; private set; }

    internal RemoteControlService? Remote { get; private set; }

    internal LiveSyncService? LiveSyncs { get; private set; }

    internal DiskWatcher? Disks { get; private set; }
    private MainWindow? _window;
    private Views.ShelfWindow? _shelf;
    private bool _exiting;
    private static CrashSession? _crashSession;

    /// <summary>Copias que el usuario detuvo con el botón (no las que se interrumpen al salir o por un apagón).</summary>
    internal readonly HashSet<string> StoppedByUser = [];

    /// <summary>La entrada del historial de cada copia terminada (para «Eliminar» desde la lista de copias).</summary>
    internal readonly Dictionary<string, string> HistoryIds = [];

    public App()
    {
        _crashSession = new CrashSession(Engine.DataFolder.Path, CrashReporter.Version);
        UnhandledException += (_, e) => LogCrash(e.Exception, e.Message, fatal: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception, null, fatal: true);
        InitializeComponent();
    }

    /// <summary>Deja constancia de un fallo inesperado en %LOCALAPPDATA%\MegaCopier\crash.log (las copias se reanudan al volver).</summary>
    internal static void LogCrash(Exception? exception, string? message, bool fatal = false)
    {
        if (fatal) _crashSession?.Fatal(exception);
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Modo de prueba (MEGACOPIER_TEST=1): no intercepta el Ctrl+V del Explorador, no toca el registro y muestra la
    /// ventana sin quitar el foco. Para probar la interfaz mientras se usa el equipo.
    /// </summary>
    internal static bool TestMode { get; } = Environment.GetEnvironmentVariable("MEGACOPIER_TEST") == "1";

    internal AppSettings Settings { get; } = AppSettings.Load();

    internal UpdateCoordinator? Updates { get; private set; }
    internal CrashReporter? CrashReports { get; private set; }

    /// <summary>Los trabajos aceptados durante el cierre de actualización se guardan, sin empezar una copia nueva.</summary>
    internal bool CanUpdate => !_exiting && !Queue.Jobs.Any(j => j.IsActive || j.State == CopyJobState.Interrupted)
        && Settings.LiveSyncs.Count == 0 && AllWindows.All(w => w.IsClosed || !w.UpdateDialogOpen);
    internal bool UpdateIdle => CanUpdate && AllWindows.All(w => w.IsClosed || !w.AppWindow.IsVisible)
        && (_shelf is null || !_shelf.AppWindow.IsVisible) && _trayMenu?.IsOpen != true && !UpdateActivity.PointerDown;
    internal void ShowUpdates()
    {
        Window.ShowAndActivate();
        Window.ShowUpdates();
    }

    internal CopyQueue Queue { get; } = new(new JobStore());

    internal DispatcherQueue Dispatcher { get; private set; } = null!;

    internal MainWindow Window => _window!;

    /// <summary>Acción al terminar todas las copias (solo para esta sesión; vuelve a "nada" al ejecutarse).</summary>
    internal FinishAction FinishAction { get; set; }

    /// <summary>
    /// La extensión del Explorador que acompaña a esta app. Lleva la versión en el nombre (MegaCopierShell-1.0.0.dll):
    /// el Explorador mantiene cargada la anterior, así que una actualización instala la nueva al lado.
    /// </summary>
    internal static string? ShellExtensionPath =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "MegaCopierShell*.dll")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            LogCrash(ex, "OnLaunched", fatal: true);
            throw;
        }
    }

    private void Launch()
    {
        // Sin pausas largas del recolector de memoria: los ganchos del ratón y del teclado tienen que responder siempre al
        // momento (si una vez tardan, Windows los quita) y la interfaz no se queda parada con copias grandes en marcha.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

        // Primero los pinceles del tema, luego los estilos que los usan (Resources no existe aún en el constructor).
        ThemeManager.Register(Resources);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Styles.xaml") });

        // Sin ventanas visibles la app sigue viva en la bandeja hasta que el usuario elige "Salir".
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        Dispatcher = DispatcherQueue.GetForCurrentThread();
        var theme = ThemeManager.Find(Settings.ThemeId);
        theme = WithBackdrop(theme, Settings.EffectiveBackdrop);
        ThemeManager.Apply(theme);
        IconPacks.Apply(Settings.IconPack);
        BarAppearance.Apply(Settings.TotalBar, Settings.FileBar, Settings.MiniBar, Settings.ReduceBarMotion);
        ApplyControlAccent(theme);

        var interrupted = Queue.LoadInterrupted();

        // Cada copia nueva, en la ventana que le toca (antes de que las ventanas se enteren de que existe).
        Queue.JobAdded += job =>
        {
            _owners.TryAdd(job, _nextOwner);
            if (_submittingAsk)
            {
                _awaitingAnswer.TryAdd(job, 0);
            }
        };
        _window = new MainWindow(this);
        _window.ApplyTheme(theme);
        ApplySettings();
        // Las preguntas de una copia salen en su ventana.
        Queue.ConflictResolver = (job, conflict, cancellation) => WindowFor(job).AskConflict(job, conflict, cancellation);
        Queue.ErrorResolver = (job, error, cancellation) => WindowFor(job).AskError(job, error, cancellation);
        Queue.FolderResolver = (job, folder, cancellation) => WindowFor(job).AskFolder(job, folder, cancellation);
        Queue.SpaceResolver = (job, shortage, cancellation) => WindowFor(job).AskSpace(job, shortage, cancellation);

        _tray = new TrayIcon(
            Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier-off.ico"),
            "MegaCopier",
            BuildTrayMenu);
        _tray.Activated += () => ShowWindow();
        _tray.DoubleClicked += () => SetEnabled(!Settings.Enabled, notify: true);
        _trayProgress = new TrayProgressIcons(_tray);
        if (!Settings.TraySlotsPrepared && Settings.TrayProgressIcons && !TestMode)
        {
            _tray.PrepareExtraSlots(101, 110); // desde el próximo inicio de sesión, junto al reloj
            Settings.TraySlotsPrepared = true;
            Settings.Save();
        }

        _tray.ExtraActivated += id =>
        {
            if (_trayProgress.JobFor(id) is { } job)
            {
                WindowFor(job).ShowJob(job);
            }
        };
        _tray.ExtraMenu = id => _trayProgress.JobFor(id) is { } job
            ?
            [
                new(L.Get("Tray_Open"), () => WindowFor(job).ShowJob(job), Glyph: "\uE8A7"),
                TrayMenuItem.Separator,
                new(L.Get("Tray_JobPause"), () => Queue.Pause(job), Enabled: job.State == CopyJobState.Running, Glyph: "\uE769"),
                new(L.Get("Job_Resume"), () => Queue.Resume(job), Enabled: job.State == CopyJobState.Paused, Glyph: "\uE768"),
                new(L.Get("Tray_JobStop"), () => WindowFor(job).StopJob(job), Enabled: job.IsActive, Glyph: "\uE71A"),
            ]
            : [];

        // El menú del clic derecho, con el aspecto de WinUI (no el clásico de Windows).
        _tray.PresentMenu = (items, x, y) => Dispatcher.TryEnqueue(() => (_trayMenu ??= new TrayMenuWindow()).Show(items, x, y));
        Queue.JobFinished += job => Dispatcher.TryEnqueue(() => OnJobFinished(job));
        if (TestMode)
        {
            Engine.Remote.Connections.UseMemorySecrets(); // las pruebas nunca tocan el Administrador de credenciales
        }

        Guard = new IntegrityGuard(this);
        Comfort = new Comfort(this);
        Disks = new DiskWatcher(this);
        Disks.Refresh(force: true);
        Peers = new PeerService(this);
        Peers.Apply();
        Engine.FileFilter.ImageTextReader = WindowsOcr.Read; // "fotos con el texto…"
        Remote = new RemoteControlService(this);
        Remote.Apply();
        LiveSyncs = new LiveSyncService(this);
        LiveSyncs.Apply();

        _taskbar = new TaskbarProgress(Window.Handle);
        _statusTimer = Dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(500);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        _server = new IpcServer(HandleRequestAsync);
        _server.Start();
        Updates = new UpdateCoordinator(this);
        Updates.Changed += () =>
        {
            foreach (var window in AllWindows.Where(w => !w.IsClosed)) window.RefreshUpdateBadge();
        };
        Window.RefreshUpdateBadge();
        CrashReports = new CrashReporter(this, _crashSession!);

        // La principal ya debe estar asignada: durante su constructor WindowFor aún no podía resolver las copias recuperadas.
        foreach (var restored in Queue.Jobs) Window.TakeJob(restored, select: false);

        _paste = new PasteInterceptor(OnExplorerPaste);
        _paste.PassedThrough += reason => LogDrag($"Ctrl+V devuelto al Explorador: {reason}");
        _paste.Traced += line => LogDrag(line);
        _drag = new DragInterceptor(OnExplorerDrag);
        _drag.PassedThrough += reason => LogDrag($"devuelto al Explorador: {reason}");
        _drag.Traced += line => LogDrag(line);
        SetEnabled(Settings.Enabled, notify: false);

        if (Settings.AutoResumeInterrupted)
        {
            foreach (var job in interrupted)
            {
                Queue.Resume(job);
            }
        }

        if (Settings.LoadFailed)
        {
            _tray.ShowNotification(L.Get("Notify_SettingsResetTitle"), L.Get("Notify_SettingsResetBody"));
        }

        var startup = Program.Startup;
        if (startup.Request is { } request)
        {
            Handle(request);
        }
        else if (interrupted.Count > 0 && !Settings.AutoResumeInterrupted)
        {
            // Arranque tras un apagón: avisar de que hay copias que se pueden continuar.
            _tray.ShowNotification(
                L.Get("Notify_InterruptedTitle"),
                interrupted.Count == 1 ? L.Get("Notify_InterruptedOne") : L.F("Notify_InterruptedMany", interrupted.Count));
        }
    }

    /// <summary>
    /// Lanza una lista guardada (.mclist) con todas sus opciones: "Cargar lista", tareas programadas y
    /// "MegaCopier.exe run lista.mclist". Si no tiene un destino completo, se pide.
    /// </summary>
    /// <summary>
    /// Copia los archivos que fallaron por estar en uso leyéndolos de una instantánea de volumen: un proceso aparte pide
    /// permiso de administrador (el aviso de Windows), los copia, y después se reintenta la copia (ya los encuentra).
    /// </summary>
    internal async Task CopyWithAdminAsync(CopyJob job)
    {
        // Modo copia de seguridad: un proceso con permisos de administrador (aviso de Windows) repite la copia
        // saltándose los permisos de los archivos; después se reintenta aquí y lo copiado se omite como idéntico.
        string resultPath = DataFolder.Combine($"backup-{job.Id}.txt");
        int exit;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!, $"--backup {job.Id}") { UseShellExecute = !TestMode };
            if (!TestMode)
            {
                info.Verb = "runas";
            }

            using var process = Process.Start(info)!;
            await process.WaitForExitAsync();
            exit = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Notify(L.Get("Backup_Title"), L.Get("Locked_Cancelled"));
            return;
        }

        string[] lines = File.Exists(resultPath) ? File.ReadAllLines(resultPath) : ["-1", ""];
        File.Delete(resultPath);
        if (TestMode)
        {
            TestLog("backup.log", $"salida={exit} {string.Join(" | ", lines)}");
        }

        Queue.Resume(job);
        Notify(L.Get("Backup_Title"), exit == 0 ? L.Get("Backup_Done") : L.F("Backup_Partial", lines.ElementAtOrDefault(1) ?? ""));
    }

    internal async Task CopyLockedFilesAsync(CopyJob job)
    {
        var locked = job.Files?.Snapshot().Where(i => i.State == FileState.Failed && i.ErrorKind == FileErrorKind.InUse)
            .Select(i => new LockedFile(i.Source, i.Destination)).ToList() ?? [];
        if (locked.Count == 0)
        {
            return;
        }

        string requestPath = DataFolder.Combine($"vss-{job.Id}.json");
        LockedFiles.Save(new LockedFilesRequest { Files = locked }, requestPath);
        int exit;
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!, $"--vss \"{requestPath}\"") { UseShellExecute = !TestMode };
            if (!TestMode)
            {
                info.Verb = "runas"; // aviso de control de cuentas de Windows
            }

            using var process = Process.Start(info)!;
            await process.WaitForExitAsync();
            exit = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Notify(L.Get("Locked_Title"), L.Get("Locked_Cancelled")); // no se dio permiso
            return;
        }

        var result = LockedFiles.Load(requestPath);
        File.Delete(requestPath);
        int copied = locked.Count - (result?.Errors.Count ?? locked.Count);
        if (TestMode)
        {
            TestLog("vss.log", $"salida={exit} copiados={copied} errores={result?.Errors.Count} {string.Join(" | ", result?.Errors.Values.ToList() ?? [])}");
        }

        if (copied > 0)
        {
            Queue.Resume(job); // reintentar: los copiados ya están y se omiten como idénticos
        }

        Notify(L.Get("Locked_Title"), result is { Errors.Count: 0 }
            ? L.F("Locked_Done", copied)
            : L.F("Locked_Partial", copied, result?.Errors.Values.FirstOrDefault() ?? ""));
    }

    internal CopyJob? RunList(string path, string? destination = null)
    {
        if (Reports.LoadList(path) is not { Sources.Count: > 0 } list)
        {
            Notify(L.Get("Files_LoadFailed"), path);
            return null;
        }

        destination ??= list.Destination;
        if (!Path.IsPathFullyQualified(destination))
        {
            return null;
        }

        // Lo que ya se copió, el orden de la lista y las carpetas renombradas viajan con la lista.
        var job = Queue.Submit(list.Sources, destination, list.ToOptions(), configure: record =>
        {
            record.Excluded = [.. list.Excluded];
            record.FileOrder = [.. list.FileOrder];
            record.RenamedFolders = new(list.RenamedFolders, StringComparer.OrdinalIgnoreCase);
        });
        job.Record.ExportReport = list.ExportReport;
        job.Record.Scripts = [.. list.Scripts];
        job.SaveRecord();
        if (Settings.ShowWindowOnNewCopy)
        {
            WindowFor(job).ShowJob(job);
        }

        return job;
    }

    /// <summary>Encola una copia (desde la ventana o desde otro proceso).</summary>
    internal CopyJob Submit(IpcRequest request)
    {
        var operation = request.Operation ?? JobOperation.Copy;
        if (operation != JobOperation.Copy && request.Sources.Count > 0 && string.IsNullOrWhiteSpace(request.Destination))
        {
            // Comprobar y borrar no tienen destino: se muestra la carpeta de los orígenes.
            string first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Sources[0]));
            request.Destination = File.Exists(first) || operation == JobOperation.Delete ? Path.GetDirectoryName(first) ?? first : first;
        }

        if (request.Sources.Count == 0 || string.IsNullOrWhiteSpace(request.Destination))
        {
            throw new ArgumentException(L.Get("Error_NeedsPaths"));
        }

        var destinations = new List<string> { request.Destination };
        destinations.AddRange((request.ExtraDestinations ?? []).Where(d => !destinations.Contains(d, StringComparer.OrdinalIgnoreCase)));

        CopyJob? firstJob = null;
        foreach (string destination in destinations)
        {
            var options = Settings.NewJobOptions(request);
            options.Operation = operation;
            options.SecureDelete = request.SecureDelete ?? false;
            options.PeerCode = request.PeerCode ?? "";
            options.Encrypt = request.Encrypt ?? false;
            options.Zip = request.Zip ?? false;
            options.SnapshotId = request.SnapshotId ?? "";
            options.ArchiveFormat = request.ArchiveFormat ?? Engine.IO.ArchiveFormat.Zip;
            options.EncryptionPassword = request.Password ?? "";
            if (operation != JobOperation.Copy)
            {
                options.CheckFreeSpace = false;
                options.Move = false;
            }

            // La misma copia ya estaba detenida (por cerrar MegaCopier, un apagón...): se continúa esa en lugar de
            // tener dos iguales en la lista.
            if (request.StartAtUtc is null && Queue.FindInterrupted(request.Sources, destination, operation, options.Move) is { } same)
            {
                Queue.Resume(same);
                firstJob ??= same;
                continue;
            }

            // ¿Hay otra copia en marcha hacia el mismo disco? Según «Copia nueva al mismo disco», MegaCopier sale al
            // frente y pregunta, la suma a ella, la deja aparte esperando su turno o la empieza a la vez. Hacia otro
            // disco empieza ya (cada disco recibe a su velocidad), salvo que se pida que espere su turno.
            bool now = request.StartAtUtc is null;
            var sameDrive = now ? FindSameDriveJob(destination) : null;
            var policy = sameDrive is null ? SameDriveCopy.Queue : Settings.SameDriveCopies;
            bool canMerge = sameDrive is not null && operation == JobOperation.Copy && CanMerge(sameDrive, options);
            if (policy == SameDriveCopy.Merge && canMerge)
            {
                _ = AddToJobAsync(sameDrive!, request.Sources, destination: destination); // a su carpeta, que puede ser otra del mismo disco
                firstJob ??= sameDrive;
                continue;
            }

            // Si hay que preguntar, la copia nueva espera (programada) hasta la respuesta. Con «Cada copia en su propia
            // ventana», la que va aparte se abre en una ventana suya y empieza ya, a la vez que las demás.
            bool ask = policy == SameDriveCopy.Ask && canMerge;
            bool ownWindow = Settings.IndependentWindows;
            bool startNow = now && (ownWindow || (sameDrive is null ? Settings.OtherDriveCopies == OtherDriveCopy.Parallel : policy == SameDriveCopy.Parallel));
            _nextOwner = ask ? WindowFor(sameDrive!) : ownWindow ? WindowForNewCopy() : null; // mientras pregunta, en la ventana de la otra copia
            CopyJob job;
            _submittingAsk = ask;
            try
            {
                job = Queue.Submit(request.Sources, destination, options, ask ? DateTime.UtcNow.AddYears(50) : request.StartAtUtc, request.Excluded, startNow: startNow && !ask);
            }
            finally
            {
                _nextOwner = null;
                _submittingAsk = false;
            }

            if (ask)
            {
                _ = AskSameDriveAsync(job, sameDrive!, [.. request.Sources]);
            }

            Disks?.Check(request.Sources.Append(destination));
            if (operation == JobOperation.Copy && (Settings.ExportReport || Settings.Scripts.Count > 0))
            {
                job.Record.ExportReport = Settings.ExportReport;
                job.Record.Scripts = [.. Settings.Scripts];
                job.SaveRecord();
            }

            if (operation == JobOperation.Copy)
            {
                Settings.RememberDestination(destination);
            }

            firstJob ??= job;
        }

        if (operation == JobOperation.Copy)
        {
            Settings.Save();
        }

        return firstJob!;
    }

    /// <summary>
    /// Opciones de fin de copia: informe de errores, borrar lo que quedó a medias al detener y
    /// expulsar la unidad de destino.
    /// </summary>
    private void ApplyEndOfJobOptions(CopyJob job)
    {
        if (Settings.SaveErrorReport && job.Result is { Failures.Count: > 0 } result)
        {
            WriteErrorReport(job, result);
        }

        bool stoppedByUser = job.State == CopyJobState.Interrupted && StoppedByUser.Remove(job.Id) && !_exiting;
        bool failed = job.State == CopyJobState.Failed;
        if (Settings.DeleteIncomplete && (stoppedByUser || (failed && !Settings.KeepIncompleteOnError)) && !job.IsActive)
        {
            try
            {
                Queue.Discard(job);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (Settings.EjectWhenDone && job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors)
        {
            string root = Path.GetPathRoot(job.Record.Destination) ?? "";
            bool inUse = Queue.Jobs.Any(j => j != job && j.IsActive && string.Equals(Path.GetPathRoot(j.Record.Destination), root, StringComparison.OrdinalIgnoreCase));
            if (root.Length > 0 && !inUse && DriveEject.IsRemovable(root))
            {
                if (TestMode)
                {
                    TestLog("eject.log", $"{root} force={Settings.EjectForce}");
                }
                else
                {
                    _ = Task.Run(() => DriveEject.Eject(root, Settings.EjectForce)).ContinueWith(
                        t => Dispatcher.TryEnqueue(() => Notify(L.Get(t.Result ? "Eject_DoneTitle" : "Eject_FailedTitle"), root)),
                        TaskScheduler.Default);
                }
            }
        }
    }

    /// <summary>Informe de errores: los archivos que fallaron y por qué, en la carpeta de destino o en la elegida.</summary>
    private void WriteErrorReport(CopyJob job, CopyJobResult result)
    {
        try
        {
            string folder = Settings.ErrorReportInDestination || Settings.ErrorReportFolder.Length == 0
                ? job.Record.Destination
                : Settings.ErrorReportFolder;
            string name = string.Join("_", Settings.ErrorReportName.Split(Path.GetInvalidFileNameChars())).Trim();
            if (name.Length == 0)
            {
                name = "errorlog.txt";
            }

            Directory.CreateDirectory(folder);
            var lines = new List<string>
            {
                $"MegaCopier · {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"{string.Join(" | ", job.Record.Sources)} → {job.Record.Destination}",
                L.F("ErrorReport_Count", result.Failures.Count),
                "",
            };
            lines.AddRange(result.Failures.Select(f => $"{f.Source}\t{(f.Kind == FileErrorKind.Other ? f.Message : L.Get("Error_" + f.Kind))}"));
            File.AppendAllLines(Path.Combine(folder, name), lines.Append(""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LogCrash(ex, "Informe de errores");
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // «Cada copia en su propia ventana»: varias ventanas de copia de la misma aplicación (un solo proceso, el mismo
    // tema, la misma cola). Cada copia sabe en qué ventana está; sin esa opción, todas van a la principal.

    /// <summary>Ventanas más de copia (la principal es <see cref="Window"/>).</summary>
    private readonly List<MainWindow> _copyWindows = [];

    /// <summary>En qué ventana está cada copia (las que no están, en la principal).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CopyJob, MainWindow?> _owners = new();

    /// <summary>La ventana para la copia que se está creando (mientras dura <see cref="CopyQueue.Submit"/>).</summary>
    private MainWindow? _nextOwner;

    /// <summary>
    /// Copias nuevas al mismo disco que esperan la respuesta a «¿añadirla a la copia en marcha?». Aún no cuentan como otra
    /// copia: la ventana no pasa por ellas a la vista completa (la pregunta sale en la ventana tal como esté).
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CopyJob, byte> _awaitingAnswer = new();
    private bool _submittingAsk;

    internal bool IsAwaitingAnswer(CopyJob job) => _awaitingAnswer.ContainsKey(job);

    /// <summary>La principal y las ventanas de copia abiertas.</summary>
    internal IReadOnlyList<MainWindow> AllWindows => _window is null ? [.. _copyWindows] : [_window, .. _copyWindows];

    /// <summary>La ventana que muestra una copia (la principal si no tiene otra o la suya se cerró).</summary>
    internal MainWindow WindowFor(CopyJob job) =>
        _owners.TryGetValue(job, out var owner) && owner is { IsClosed: false } ? owner : Window;

    /// <summary>
    /// Dónde va una copia nueva con «Cada copia en su propia ventana»: la principal si no tiene nada en marcha, otra
    /// ventana de copia libre o, si no hay, una ventana nueva, escalonada respecto a las demás.
    /// </summary>
    private MainWindow WindowForNewCopy()
    {
        if (!Window.HasActiveJobs)
        {
            return Window;
        }

        // Solo una que se ve: una oculta sin copias está a punto de cerrarse y la copia nueva se quedaría sin ventana.
        if (_copyWindows.FirstOrDefault(w => !w.IsClosed && w.AppWindow.IsVisible && !w.HasActiveJobs) is { } free)
        {
            return free;
        }

        var window = new MainWindow(this, copyWindow: true);
        window.ApplyTheme(ThemeManager.Current);
        window.ApplyAlwaysOnTop(Settings.AlwaysOnTop);
        window.PlaceAfter(_copyWindows.LastOrDefault(w => !w.IsClosed && w.AppWindow.IsVisible) ?? Window, 1);
        _copyWindows.Add(window);
        return window;
    }

    /// <summary>Una copia que esperaba respuesta en otra ventana pasa a una ventana propia.</summary>
    private void MoveToOwnWindow(CopyJob job)
    {
        var from = WindowFor(job);
        var to = WindowForNewCopy();
        if (to == from)
        {
            return;
        }

        _owners[job] = to;
        from.ReleaseJob(job);
        to.TakeJob(job);
        to.ShowJob(job);
    }

    /// <summary>
    /// Una ventana de copia se cerró: sus copias vuelven a la principal (y se ven en ella, también una que le llegó
    /// justo mientras se cerraba).
    /// </summary>
    internal void ForgetWindow(MainWindow window)
    {
        _copyWindows.Remove(window);
        foreach (var (job, owner) in _owners)
        {
            if (owner == window)
            {
                _owners[job] = null;
                if (Queue.Jobs.Contains(job))
                {
                    Window.TakeJob(job, select: false);
                }
            }
        }
    }

    /// <summary>
    /// La copia en marcha (o esperando turno) que va al mismo disco de destino que una nueva, o null. Las programadas
    /// para más tarde no cuentan.
    /// </summary>
    private CopyJob? FindSameDriveJob(string destination)
    {
        static string Root(string path) => Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\').ToUpperInvariant() ?? "";
        string root = Root(destination);
        return root.Length == 0 ? null : Queue.Jobs.FirstOrDefault(job => job.IsActive && !job.Cancelled && job.Record.StartAtUtc is null && Root(job.Record.Destination) == root);
    }

    /// <summary>Solo se puede sumar a una copia que hace lo mismo: copiar (o mover) con el mismo modo.</summary>
    private static bool CanMerge(CopyJob job, CopyOptions options) =>
        job.Record.Operation == JobOperation.Copy && job.Record.Mode == options.Mode && job.Record.Move == options.Move
        && !options.Zip && !options.Encrypt && !job.Record.Zip && !job.Record.Encrypt;

    /// <summary>
    /// Suma orígenes a una copia (a su carpeta de destino o, si se indica, a otra del mismo disco). Si está en marcha
    /// y lo nuevo no cabe en el destino (con lo que aún le queda por copiar), se pregunta antes: cancelar no añade
    /// nada; forzar los añade y, al llenarse el disco, la copia espera.
    /// </summary>
    internal async Task<CopyJob?> AddToJobAsync(CopyJob job, IReadOnlyList<string> sources, int? beforeId = null, string? destination = null)
    {
        if (job.State is CopyJobState.Running or CopyJobState.Paused)
        {
            SpaceShortage? shortage = null;
            try
            {
                shortage = await Task.Run(() => job.CheckSpaceFor(sources, destination));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CopyPlanException)
            {
                // Lo que no se pueda leer lo dirá la propia copia al añadirlo.
            }

            if (shortage is not null)
            {
                if (!await Window.AskSpaceAsync(job, shortage, adding: true))
                {
                    return null;
                }

                job.ForceSpace();
            }
        }

        try
        {
            return Queue.AddToJob(job, sources, beforeId, destination);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or CopyPlanException)
        {
            Notify(L.Get("Files_AddFailed"), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Una copia nueva va al mismo disco que otra en marcha: MegaCopier sale al frente y pregunta. Añadirla suma sus
    /// archivos al final de la lista de esa copia (comprobando antes el espacio); como copia aparte, espera a que
    /// termine la otra (con «Comenzar» empieza ya); cancelar no copia nada.
    /// </summary>
    private async Task AskSameDriveAsync(CopyJob added, CopyJob target, List<string> sources)
    {
        SameDriveAnswer answer;
        try
        {
            answer = await WindowFor(target).AskSameDriveAsync(target, added.Record.Destination, sources, Settings.IndependentWindows);
        }
        finally
        {
            _awaitingAnswer.TryRemove(added, out _);
        }

        if (answer == SameDriveAnswer.Add && target.IsActive && Queue.Withdraw(added))
        {
            await AddToJobAsync(target, sources, destination: added.Record.Destination); // si no cabe, pregunta
            return;
        }

        if (answer == SameDriveAnswer.Cancel && Queue.Withdraw(added))
        {
            return;
        }

        Queue.Unschedule(added); // aparte: espera su turno como las demás
        if (Settings.IndependentWindows)
        {
            // En su propia ventana, y empieza ya: no espera a que termine la otra.
            MoveToOwnWindow(added);
            Queue.Resume(added);
        }
    }

    /// <summary>Preferencias que no son de cada copia: cola, prioridad del proceso y ventana siempre visible.</summary>
    internal void ApplySettings()
    {
        Queue.MaxActiveJobs = Math.Max(0, Settings.MaxActiveJobs);
        Queue.FastDevices = Settings.FastDevices.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _tray?.SetVisible(Settings.ShowTrayIcon);
        Format.SizeUnits = Settings.Units;
        // Primero el modo de segundo plano (al entrar o salir, Windows deja la clase en Normal) y después la clase.
        SystemActions.SetBackgroundMode(Settings.Priority == ProcessPriority.Low);

        try
        {
            Process.GetCurrentProcess().PriorityClass = Settings.Priority switch
            {
                ProcessPriority.Low => ProcessPriorityClass.Idle,
                ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                ProcessPriority.High => ProcessPriorityClass.High,
                _ => ProcessPriorityClass.Normal,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }

        foreach (var window in AllWindows)
        {
            window.ApplyAlwaysOnTop(Settings.AlwaysOnTop);
            window.RefreshUiScale();
        }
    }

    /// <summary>Aplica el tema elegido en las preferencias.</summary>
    internal void ApplyTheme() => ApplyTheme(ThemeManager.Find(Settings.ThemeId));

    /// <summary>Aplica un tema (también la vista previa del editor, sin guardarlo).</summary>
    /// <summary>
    /// El tema con el fondo de ventana elegido en Ajustes: Mica, Mica Alt, acrílico o «Translúcida» (acrílico delgado, el más
    /// transparente). El color de fondo del tema se aclara para que el material se vea, sea cual sea el tema.
    /// </summary>
    private static ThemeDefinition WithBackdrop(ThemeDefinition theme, WindowBackdrop choice)
    {
        if (choice == WindowBackdrop.Theme)
        {
            return theme;
        }

        static string Fade(string color, double keep)
        {
            var c = ThemeManager.Parse(color);
            return ThemeManager.ToHex(Windows.UI.Color.FromArgb((byte)(c.A * keep), c.R, c.G, c.B));
        }

        (AppBackdrop backdrop, double keep) = choice switch
        {
            WindowBackdrop.Mica => (AppBackdrop.Mica, 0.3),
            WindowBackdrop.MicaAlt => (AppBackdrop.MicaAlt, 0.3),
            WindowBackdrop.AcrylicBase => (AppBackdrop.Acrylic, 0.55),
            _ => (AppBackdrop.AcrylicThin, 0.45), // translúcida
        };
        var clone = theme.Clone();
        clone.Backdrop = backdrop;
        clone.Background = Fade(theme.Background, keep);
        clone.BackgroundEnd = theme.BackgroundEnd.Length > 0 ? Fade(theme.BackgroundEnd, keep) : "";
        clone.TextureOpacity = theme.TextureOpacity * 0.5;
        return clone;
    }

    internal void ApplyTheme(ThemeDefinition theme)
    {
        theme = WithBackdrop(theme, Settings.EffectiveBackdrop);
        ThemeManager.Apply(theme);
        bool accentChanged = ApplyControlAccent(theme);
        foreach (var window in AllWindows)
        {
            window.ApplyTheme(theme, accentChanged); // el mismo tema en todas las ventanas
        }
        _shelf?.ApplyTheme();
    }

    private string _controlAccent = "";

    /// <summary>
    /// Acento de los controles estándar (casillas, interruptores, botón principal, pestañas). Devuelve si cambió: la
    /// ventana alterna entonces su tema para que los controles vuelvan a leer sus recursos. No se sustituye
    /// XamlControlsResources: un menú ya abierto antes conserva los recursos del diccionario viejo y WinUI se cierra
    /// (0xc000027b, «El parámetro no es correcto») al volver a abrirlo.
    /// </summary>
    private bool ApplyControlAccent(ThemeDefinition theme)
    {
        // El acento y, en los temas que lo piden, los colores de todos los controles (barras de desplazamiento, menús...).
        string accent = Appearance.ToRgbHex(theme.Accent);
        string controls = ControlColors.Apply(theme, Resources);
        string signature = accent + controls;
        if (signature == _controlAccent)
        {
            return false;
        }

        bool first = _controlAccent.Length == 0;
        _controlAccent = signature;
        Appearance.ApplyAccent(accent);
        return !first;
    }

    /// <summary>Aviso del sistema desde el icono de la bandeja.</summary>
    internal void Notify(string title, string body) => _tray?.ShowNotification(title, body);

    /// <summary>
    /// Interruptor general. Desactivado: el Ctrl+V vuelve a ser del Explorador, la extensión deja de ofrecer sus
    /// opciones y el icono de la bandeja pasa a gris con una X roja.
    /// </summary>
    internal void SetEnabled(bool enabled, bool notify)
    {
        Settings.Enabled = enabled;
        if (notify)
        {
            Settings.Save();
        }

        if (!TestMode)
        {
            ShellState.SetEnabled(enabled);
        }

        SetExplorerPaste(Settings.InterceptExplorerPaste);
        SetExplorerDrag(Settings.InterceptExplorerDrag);
        _tray?.SetDisabled(!enabled);
        _tray?.SetTooltip(enabled ? "MegaCopier" : L.Get("Tray_TooltipDisabled"));
        if (notify)
        {
            _tray?.ShowNotification(
                L.Get(enabled ? "Notify_EnabledTitle" : "Notify_DisabledTitle"),
                L.Get(enabled ? "Notify_EnabledBody" : "Notify_DisabledBody"));
        }

        EnabledChanged?.Invoke();
    }

    /// <summary>Cambió el interruptor general (para refrescar la ventana).</summary>
    internal event Action? EnabledChanged;

    /// <summary>Activa o desactiva la sustitución del Ctrl+V del Explorador (solo si MegaCopier está activado).</summary>
    internal void SetExplorerPaste(bool enabled)
    {
        if (enabled && Settings.Enabled && !TestMode)
        {
            _paste?.Enable();
        }
        else
        {
            _paste?.Disable();
        }
    }

    /// <summary>Activa o desactiva la sustitución del arrastre con el botón izquierdo (solo si MegaCopier está activado).</summary>
    internal void SetExplorerDrag(bool enabled)
    {
        if (enabled && Settings.Enabled && !TestMode)
        {
            _drag?.Enable();
        }
        else
        {
            _drag?.Disable();
        }
    }

    /// <summary>
    /// Un arrastre del Explorador que MegaCopier se ha quedado (en el hilo del gancho de arrastre). El arrastre del
    /// Explorador ya está cancelado: si el usuario dice que no en la confirmación, no se hace nada, como al pulsar Esc.
    /// </summary>
    private bool OnExplorerDrag(DragRequest drag)
    {
        CrashReports?.NoteDrag();
        bool ask = Settings.DragConfirm == DragConfirm.Always || (Settings.DragConfirm == DragConfirm.Moves && drag.Move);
        if (ask)
        {
            string what = drag.Sources.Count == 1 ? L.F("Job_OneItem", Path.GetFileName(drag.Sources[0])) : L.F("Job_ManyItems", drag.Sources.Count);
            string question = L.F(drag.Move ? "Drag_ConfirmMove" : "Drag_ConfirmCopy", what, drag.Destination);
            if (!SystemActions.Confirm(question))
            {
                return true;
            }
        }

        return OnExplorerPaste(new PasteRequest(drag.Sources, drag.Destination, drag.Move ? ClipboardOperation.Move : ClipboardOperation.Copy));
    }

    /// <summary>Reinicia la app (para aplicar el idioma). Las copias en curso quedan reanudables.</summary>
    internal void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
        ExitApp();
    }

    /// <summary>Un Ctrl+V del Explorador (llega desde el hilo del gancho).</summary>
    private bool OnExplorerPaste(PasteRequest paste)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = Dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            try
            {
                Handle(new IpcRequest
                {
                    Command = "copy",
                    Sources = [.. paste.Sources],
                    Destination = paste.Destination,
                    Move = paste.Operation == ClipboardOperation.Move,
                });
                completion.SetResult(true);
            }
            catch
            {
                completion.SetResult(false);
            }
        });

        // El hilo de los ganchos sigue libre. Una petición ya encolada no puede volver al Explorador por un
        // plazo arbitrario: la interfaz podría aceptarla después y los dos copiadores ejecutarían lo mismo.
        return queued && completion.Task.GetAwaiter().GetResult();
    }

    private Task<IpcResponse> HandleRequestAsync(IpcRequest request)
    {
        var completion = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = Dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            try
            {
                completion.SetResult(Handle(request));
            }
            catch (Exception ex)
            {
                completion.SetResult(new IpcResponse { Ok = false, Error = ex.Message });
            }
        });

        if (!queued)
        {
            completion.SetResult(new IpcResponse { Ok = false, Error = "MegaCopier is closing." });
        }

        return completion.Task;
    }

    private IpcResponse Handle(IpcRequest request)
    {
        switch (request.Command)
        {
            case "copy":
                var job = Submit(request);
                if (Settings.ShowWindowOnNewCopy)
                {
                    var shown = WindowFor(job); // la ventana de esa copia (con varias ventanas, la suya)
                    shown.TakeJob(job, select: !IsAwaitingAnswer(job)); // ya en su lista antes de enseñarla (el aviso llega después)
                    shown.ShowJobs();
                    if (Settings.StartMinimized)
                    {
                        shown.ShowMinimized();
                    }
                    else
                    {
                        shown.ShowAndActivate();
                    }
                }

                return new IpcResponse { Ok = true, JobId = job.Id };

            case "confirm-delete" when request.Sources.Count > 0:
                // Borrar desde el Explorador: siempre se confirma en la ventana.
                ShowWindow();
                _ = Window.ConfirmAndDeleteAsync(request.Sources, request.SecureDelete ?? false);
                return new IpcResponse { Ok = true };

            case "run-list" when request.Sources.Count == 1:
                return RunList(request.Sources[0]) is { } listJob
                    ? new IpcResponse { Ok = true, JobId = listJob.Id }
                    : new IpcResponse { Ok = false, Error = L.Get("Files_LoadFailed") };

            case "shelf":
                // Abrir la bandeja de arrastre (y dejar en ella estos archivos y carpetas).
                ShowShelf(request.Sources);
                return new IpcResponse { Ok = true, Status = string.Join(" | ", _shelf!.Paths) };

            case "debug-report-open" when TestMode:
                Window.ShowAndActivate();
                Window.ShowDiagnostics();
                return new IpcResponse { Ok = true };
            case "debug-report-status" when TestMode:
                return new IpcResponse { Ok = true, Status = $"configured={CrashReporter.ConfiguredEndpoint is not null}; report={CrashReports?.Last?.Id}; status={CrashReports?.StatusKey}; automatic={Settings.SendCrashReportsAutomatically}" };
            case "debug-report-crash" when TestMode && CanUpdate:
                Dispatcher.TryEnqueue(() => throw new InvalidOperationException(@"Prueba sintética: C:\Users\Prueba\privado.pdf"));
                return new IpcResponse { Ok = true };
            case "debug-update-status" when TestMode && Updates is { } updateStatus:
                return new IpcResponse { Ok = true, Status = $"{updateStatus.Phase}; error={updateStatus.Error}; current={updateStatus.Current}; available={updateStatus.Version}; badge={updateStatus.Badge}; installed={updateStatus.IsInstalled}; network={updateStatus.CanUseNetwork}; canInstall={CanUpdate}; idle={UpdateIdle}; bytes={updateStatus.Progress.Received}/{updateStatus.Progress.Total}; notes={(updateStatus.Spanish ? "es" : "en")}" };
            case "debug-update-check" when TestMode && Updates is { } updateCheck:
                _ = updateCheck.CheckAsync(); return new IpcResponse { Ok = true };
            case "debug-update-tick" when TestMode && Updates is { } updateTick:
                _ = updateTick.TestTickAsync(); return new IpcResponse { Ok = true };
            case "debug-update-badges" when TestMode && request.Destination is { } badgeReport:
                _ = Window.VerifyUpdateBadgePacksAsync(badgeReport); return new IpcResponse { Ok = true };
            case "debug-savelist" when TestMode && request.Destination is { } savePath && Queue.Jobs.LastOrDefault(j => j.Files is not null) is { } saveJob:
                // Solo pruebas: «Guardar la lista» de la última copia, sin el diálogo de guardar.
                return new IpcResponse { Ok = true, Status = Reports.SaveRemaining(saveJob, savePath).ToString(System.Globalization.CultureInfo.InvariantCulture) };

            case "debug-loadlist" when TestMode && request.Sources.Count == 1:
                // Solo pruebas: «Cargar la lista», sin el diálogo de abrir.
                _ = Window.RunListAsync(request.Sources[0]);
                return new IpcResponse { Ok = true };

            case "debug-shelf" when TestMode:
                // Solo pruebas: "copiar a…" (Destination = "copy") o "mover a…" ("move") de la bandeja.
                _ = _shelf?.SendAsync(request.Destination == "move");
                return new IpcResponse { Ok = _shelf is not null };

            case "show":
                ShowWindow();
                return new IpcResponse { Ok = true };

            case "show-updates":
                ShowUpdates();
                return new IpcResponse { Ok = true };

            case "debug-drag" when TestMode:
            {
                // Solo pruebas: Destination = "files" o "jobs"; Sources = "y=N", "row=N", "drop" y rutas.
                double y = request.Sources.Where(s => s.StartsWith("y=", StringComparison.Ordinal)).Select(s => double.Parse(s[2..], System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
                int[] rows = [.. request.Sources.Where(s => s.StartsWith("row=", StringComparison.Ordinal)).Select(s => int.Parse(s[4..], System.Globalization.CultureInfo.InvariantCulture))];
                bool drop = request.Sources.Contains("drop");
                var paths = request.Sources.Where(s => !s.StartsWith("y=", StringComparison.Ordinal) && !s.StartsWith("row=", StringComparison.Ordinal) && s != "drop").ToList();
                return new IpcResponse { Ok = true, Status = Window.DebugDrag(request.Destination ?? "files", y, rows, paths, drop) };
            }

            case "debug-states" when TestMode:
                return new IpcResponse { Ok = true, Status = Window.DebugStates() };

            case "debug-menu" when TestMode && request.Destination is { } menuButton:
                // Solo pruebas: abre el menú de un botón (por su nombre accesible), sin ratón.
                return new IpcResponse { Ok = Window.DebugOpenMenu(menuButton) };

            case "debug-click" when TestMode && request.Destination is { } clickName:
                // Solo pruebas: pulsa un botón (por su nombre accesible o su nombre en el XAML), sin ratón.
                return new IpcResponse { Ok = Window.DebugClick(clickName) };

            case "debug-redraw" when TestMode:
                Window.Redraw();
                return new IpcResponse { Ok = true };

            case "debug-render" when TestMode && request.Destination is { } renderPath:
                // Solo pruebas: lo que XAML dibuja (sin pasar por la composición en pantalla), en un PNG.
                _ = Window.DebugRenderAsync(renderPath);
                return new IpcResponse { Ok = true };

            case "debug-explorer" when TestMode:
                return new IpcResponse { Ok = true, Status = string.Join(" | ", ExplorerFolders.Open()) };

            case "debug-guard" when TestMode && request.Destination is { } guarded:
            {
                // Solo pruebas: vigilar una carpeta y comprobarla ya.
                var folder = Settings.GuardedFolders.FirstOrDefault(f => string.Equals(f.Path, guarded, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    Settings.GuardedFolders.Add(folder = new GuardedFolder { Path = guarded });
                    Settings.Save();
                }

                return new IpcResponse { Ok = true, JobId = Guard?.Check(folder)?.Id };
            }

            case "debug-send" when TestMode:
                // Solo pruebas: abrir el diálogo de enviar a otro equipo con estos orígenes.
                _ = Window.SendToPeerAsync(request.Sources);
                ShowWindow();
                return new IpcResponse { Ok = true };

            case "debug-mtp" when TestMode:
                // Solo pruebas: la ventana del móvil. Destination = "import" (Sources[0] = carpeta del PC) o "export" (Sources = qué copiar).
                _ = request.Destination == "export" ? Window.MtpExportAsync(request.Sources) : Window.MtpImportAsync(request.Sources.FirstOrDefault());
                ShowWindow();
                return new IpcResponse { Ok = true };

            case "debug-compare" when TestMode && request.Destination is { } compareRight && request.Sources.Count == 1:
                // Solo pruebas: abrir "Comparar y sincronizar" con Sources[0] (izquierda) y Destination (derecha).
                Window.ShowCompare(request.Sources[0], compareRight);
                ShowWindow();
                return new IpcResponse { Ok = true };

            case "debug-livesync" when TestMode && request.Destination is { } syncTarget && request.Sources.Count == 1:
                // Solo pruebas: sincronizar al momento Sources[0] en Destination (MOVE=1: en espejo).
                return LiveSyncs?.Add(request.Sources[0], syncTarget, request.Move == true) is { } syncProblem
                    ? new IpcResponse { Ok = false, Error = syncProblem }
                    : new IpcResponse { Ok = true };

            case "debug-icons" when TestMode && request.Destination is { } sheetPath:
                // Solo pruebas: los iconos de todos los conjuntos (menús y bandeja) en un PNG. Sources[0]: tamaño.
                _ = IconSheet.WriteAsync(sheetPath, request.Sources.Count > 0 ? int.Parse(request.Sources[0], System.Globalization.CultureInfo.InvariantCulture) : 32);
                return new IpcResponse { Ok = true };

            case "debug-traymenu" when TestMode:
            {
                // Solo pruebas: el menú de la bandeja (o el de una copia: Destination = "job"), abajo a la derecha.
                var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
                var items = request.Destination == "job" && Queue.Jobs.FirstOrDefault(j => j.IsActive) is { } menuJob && _tray?.ExtraMenu is { } extra
                    ? extra(_trayProgress!.IdFor(menuJob) ?? 0)
                    : BuildTrayMenu();
                (_trayMenu ??= new TrayMenuWindow()).Show(items, area.X + area.Width - 40, area.Y + area.Height - 8);
                return new IpcResponse { Ok = true };
            }

            case "debug-about" when TestMode:
                Views.AboutWindow.Show(Window);
                return new IpcResponse { Ok = true };

            case "debug-windows" when TestMode:
                return new IpcResponse
                {
                    Ok = true,
                    Status = string.Join(" | ", AllWindows.Select(w => $"{(w.IsCopyWindow ? "copia" : "principal")}:{w.Jobs.Count}:{(w.AppWindow.IsVisible ? "visible" : "oculta")}:{w.AppWindow.Position.X},{w.AppWindow.Position.Y}")),
                };

            case "debug-finish" when TestMode:
                // Solo pruebas: elegir "Al terminar" (Destination = Sleep, Shutdown...) sin abrir el menú.
                FinishAction = Enum.Parse<FinishAction>(request.Destination ?? "None");
                Window.UpdateFinishButton();
                return new IpcResponse { Ok = true, Status = FinishAction.ToString() };

            case "status":
                return new IpcResponse
                {
                    Ok = true,
                    Status = $"enabled={Settings.Enabled};pasteHook={_paste?.IsEnabled == true};jobs={Queue.Jobs.Count};active={Queue.HasActiveJobs}",
                };

            case "exit":
                // Lo usa el desinstalador.
                Dispatcher.TryEnqueue(ExitApp);
                return new IpcResponse { Ok = true };

            case "exit-update":
                if (_exiting) return new IpcResponse { Ok = true };
                if (!CanUpdate) return new IpcResponse { Ok = false };
                Dispatcher.TryEnqueue(ExitApp);
                return new IpcResponse { Ok = true };

            default:
                return new IpcResponse { Ok = false, Error = $"Unknown command: {request.Command}" };
        }
    }

    private void ShowWindow() => Window.ShowAndActivate();

    /// <summary>Abre la bandeja de arrastre (una sola, a la derecha de la pantalla) y añade lo que se le pase.</summary>
    internal void ShowShelf(IEnumerable<string>? add = null)
    {
        if (_shelf is null)
        {
            _shelf = new Views.ShelfWindow(this);
            _shelf.Closed += (_, _) => _shelf = null;
        }

        if (add is not null)
        {
            _shelf.Add(add);
        }

        _shelf.ShowWindow();
    }

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        bool anyRunning = Queue.Jobs.Any(j => j.State == CopyJobState.Running);
        bool anyPaused = Queue.Jobs.Any(j => j.State == CopyJobState.Paused);
        return
        [
            new(L.Get("Tray_Open"), ShowWindow, Glyph: "\uE8A7"),
            new(L.Get("Tray_Shelf"), () => ShowShelf(), Glyph: "\uE7B8"),
            new(L.Get(Settings.Enabled ? "Tray_Disable" : "Tray_Enable"), () => SetEnabled(!Settings.Enabled, notify: true), Glyph: "\uE7E8"),
            TrayMenuItem.Separator,
            new(L.Get("Tray_PauseAll"), Queue.PauseAll, Enabled: anyRunning, Glyph: "\uE769"),
            new(L.Get("Tray_ResumeAll"), Queue.ResumeAll, Enabled: anyPaused, Glyph: "\uE768"),
            TrayMenuItem.Separator,
            new(L.Get("Tray_Startup"), () => StartupRegistration.Set(!StartupRegistration.IsEnabled), Checked: StartupRegistration.IsEnabled, Checkable: true, Glyph: ""),
            TrayMenuItem.Separator,
            new(L.Get("Tray_Exit"), ExitApp, Glyph: "\uE711"),
        ];
    }

    /// <summary>Progreso global en el icono de la bandeja y en el botón de la barra de tareas.</summary>
    private void UpdateStatus()
    {
        CrashReports?.Tick();
        var running = Queue.Jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused).ToList();
        SystemActions.KeepAwake(running.Any(j => j.State == CopyJobState.Running));
        SlowDownWhileUserIsActive(running);
        Guard?.Tick(Queue.HasActiveJobs);
        _trayProgress?.Update(Queue.Jobs, Settings.TrayProgressIcons && Settings.ShowTrayIcon && Settings.Enabled, TrayText);
        if (running.Count == 0)
        {
            _tray?.SetTooltip(Settings.Enabled ? "MegaCopier" : L.Get("Tray_TooltipDisabled"));
            _taskbar?.Update(TaskbarProgress.NoProgress, 0, 0);
            return;
        }

        long done = running.Sum(j => j.Progress?.CompletedBytes ?? 0);
        long total = running.Sum(j => j.Progress?.TotalBytes ?? 0);
        int state = running.All(j => j.State == CopyJobState.Paused) ? TaskbarProgress.Paused
            : total == 0 ? TaskbarProgress.Indeterminate
            : TaskbarProgress.Normal;
        _taskbar?.Update(state, (ulong)done, (ulong)total);

        double speed = running.Where(j => j.State == CopyJobState.Running).Sum(j => j.Progress?.BytesPerSecond ?? 0);
        string percent = total > 0 ? $"{100.0 * done / total:0}%" : "…";
        string copies = running.Count == 1 ? L.Get("Tray_OneJob") : L.F("Tray_ManyJobs", running.Count);
        _tray?.SetTooltip($"MegaCopier · {copies} · {percent} · {Format.Speed(speed)}");
    }

    /// <summary>Texto del icono de progreso de una copia en la bandeja: qué hace, con qué y cómo va.</summary>
    private string TrayText(CopyJob job)
    {
        var model = WindowFor(job).Jobs.FirstOrDefault(j => j.Job == job);
        string what = job.Record.Sources.Count == 1 ? Path.GetFileName(job.Record.Sources[0].TrimEnd('\\')) : L.F("Job_ManyItems", job.Record.Sources.Count);
        string speed = job.State == CopyJobState.Running && job.Progress is { BytesPerSecond: > 0 } progress ? $" · {Format.Speed(progress.BytesPerSecond)}" : "";
        return $"{model?.StateText ?? "MegaCopier"}\n{what}{speed}";
    }

    /// <summary>
    /// "Ir más despacio mientras usas el equipo": si hubo teclado o ratón en los últimos segundos, las copias no pasan del
    /// límite elegido; cuando el equipo queda libre recuperan su velocidad.
    /// </summary>
    private void SlowDownWhileUserIsActive(List<CopyJob> running)
    {
        Comfort?.Tick(running);
        long? limit = Comfort?.SpeedLimit();

        foreach (var job in running.Where(j => j.SpeedOverride != limit))
        {
            job.SetSpeedOverride(limit);
            if (TestMode)
            {
                TestLog("slow.log", $"{job.Id} {limit?.ToString() ?? "libre"}");
            }
        }
    }

    /// <summary>
    /// Registro de los arrastres que se devolvieron al Explorador y por qué (drag.log en la carpeta de datos, como
    /// mucho 256 KB): sirve para saber por qué un arrastre no lo copió MegaCopier.
    /// </summary>
    private static void LogDrag(string line)
    {
        try
        {
            string path = Path.Combine(Engine.DataFolder.Path, "drag.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
            {
                File.Delete(path);
            }

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Solo en modo de prueba: deja constancia de lo que haría (apagar, avisar...) sin hacerlo.</summary>
    internal static void TestLog(string file, string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, file), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    private void OnJobFinished(CopyJob job)
    {
        bool stoppedByUser = job.State == CopyJobState.Interrupted && StoppedByUser.Contains(job.Id) && !_exiting;
        ApplyEndOfJobOptions(job);

        // «Detener»: la copia se cancela y ya no está en la lista. Si no queda ninguna en marcha, la ventana se cierra
        // sola, como al terminar bien.
        var owner = WindowFor(job); // su ventana: con varias, cada una se cierra cuando acaban las suyas
        if (stoppedByUser || job.Cancelled)
        {
            if (!owner.HasActiveJobs)
            {
                owner.OnAllFinished();
            }

            return;
        }

        bool finished = job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors or CopyJobState.Failed;
        if (finished)
        {
            Guard?.OnFinished(job);
        }

        if (finished)
        {
            HistoryIds[job.Id] = History.Add(job);
            AfterJob(job);
            // Con errores suena siempre su aviso; si era la última copia y fue bien, el de "todo terminado"
            // (si no hay uno elegido, el de copia terminada).
            bool ok = job.State == CopyJobState.Completed;
            bool last = !Queue.HasActiveJobs;
            var sound = !ok ? SoundEvent.Errors
                : last && Settings.Sounds.AllDone.Length > 0 ? SoundEvent.AllDone
                : SoundEvent.Done;
            Sounds.Play(Settings.Sounds, sound);
        }

        // Archivos que Windows no deja leer sin permiso de administrador: se piden los permisos (una vez por copia) y
        // se copian, como hace el Explorador. La ventana no se cierra: la copia vuelve a ponerse en marcha.
        if (job.State == CopyJobState.CompletedWithErrors && Settings.AutoAdminCopy && _adminRetried.Add(job.Id)
            && owner.Jobs.FirstOrDefault(j => j.Job == job) is { CanCopyAdmin: true })
        {
            _ = CopyWithAdminAsync(job);
            return;
        }

        if (!owner.HasActiveJobs && finished)
        {
            owner.OnAllFinished();
        }

        // "Al terminar": cuando ya no queda nada en marcha y la última copia acabó (no si el usuario la detuvo).
        if (FinishAction != FinishAction.None && !Queue.HasActiveJobs && job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors)
        {
            var action = FinishAction;
            FinishAction = FinishAction.None;
            foreach (var window in AllWindows)
            {
                window.UpdateFinishButton();
            }

            _ = RunFinishActionAsync(action);
        }

        if (!Settings.NotifyOnFinish || owner.IsVisibleToUser || job.State == CopyJobState.Interrupted)
        {
            return;
        }

        string what = job.Record.Sources.Count == 1 ? Path.GetFileName(job.Record.Sources[0].TrimEnd('\\')) : L.F("Job_ManyItems", job.Record.Sources.Count);
        switch (job.State)
        {
            case CopyJobState.Completed when job.Result is { } result:
                _tray?.ShowNotification(
                    L.Get("Notify_DoneTitle"),
                    $"{what} → {job.Record.Destination}\n{L.F("Job_TransferredIn", Format.Bytes(result.BytesTransferred), Format.Duration(result.Elapsed))}");
                break;
            case CopyJobState.CompletedWithErrors:
            case CopyJobState.Failed:
                _tray?.ShowNotification(L.Get("Notify_ProblemTitle"), $"{what}: {job.Error}");
                break;
        }
    }

    /// <summary>Apagar, suspender... tras una cuenta atrás de 30 s que se puede cancelar.</summary>
    private async Task RunFinishActionAsync(FinishAction action)
    {
        if (!await Window.ConfirmFinishAsync(action))
        {
            return;
        }

        _tray?.ShowNotification(L.Get("Notify_AllDoneTitle"), L.Get("Notify_Finish" + action));
        if (action == FinishAction.ExitApp)
        {
            if (TestMode)
            {
                TestLog("finish.log", action.ToString());
                return;
            }

            ExitApp();
            return;
        }

        SystemActions.Run(action);
    }

    /// <summary>Informe HTML automático y comandos "al terminar" de un trabajo.</summary>
    private void AfterJob(CopyJob job)
    {
        if (job.Record.ExportReport && WindowFor(job).Jobs.FirstOrDefault(j => j.Job == job) is { } model)
        {
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "reports");
                Directory.CreateDirectory(folder);
                Reports.ExportHtml(model, Path.Combine(folder, $"MegaCopier-{DateTime.Now:yyyyMMdd-HHmmss}-{job.Id.ToString()[..8]}.html"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Informe no guardado: {ex.Message}");
            }
        }

        if (Settings.SaveLogs)
        {
            JobNotices.SaveLog(job, Settings.LogRetentionDays);
        }

        if (Settings.WebhookUrl.Length > 0)
        {
            string url = Settings.WebhookUrl;
            string text = JobNotices.Summary(job);
            _ = Task.Run(() => JobNotices.SendWebhookAsync(url, text, job));
        }

        if (Settings.EmailEnabled && (!Settings.EmailOnlyProblems || job.State != CopyJobState.Completed))
        {
            var smtp = Settings.SmtpSettings();
            _ = Task.Run(() => JobNotices.SendEmailAsync(smtp, job));
        }

        if (job.Record.Scripts.Count > 0)
        {
            var environment = new Dictionary<string, string>
            {
                ["MEGACOPIER_STATE"] = job.State.ToString(),
                ["MEGACOPIER_SOURCES"] = string.Join(";", job.Record.Sources),
                ["MEGACOPIER_DESTINATION"] = job.Record.Destination,
                ["MEGACOPIER_FILES"] = (job.Result?.FilesCopied ?? 0).ToString(),
                ["MEGACOPIER_BYTES"] = (job.Result?.BytesTransferred ?? 0).ToString(),
                ["MEGACOPIER_FAILED"] = (job.Result?.Failures.Count ?? 0).ToString(),
                ["MEGACOPIER_CHECKSUM"] = job.ChecksumFile ?? "",
            };
            SystemActions.RunScripts(job.Record.Scripts, environment, job.State == CopyJobState.Completed);
        }
    }

    internal async void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        CrashReports?.Dispose();
        Updates?.Dispose();

        // Las copias en curso se detienen conservando lo copiado: al volver se continúan.
        _paste?.Dispose();
        _drag?.Dispose();
        LiveSyncs?.Dispose();
        await Task.Run(() => Queue.StopAll(TimeSpan.FromSeconds(5)));
        _statusTimer?.Stop();
        _taskbar?.Dispose();
        _tray?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _crashSession?.Clean();
        Exit();
    }
}
