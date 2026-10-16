using System.Diagnostics;
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
    private IpcServer? _server;
    private PasteInterceptor? _paste;
    private TaskbarProgress? _taskbar;
    private DispatcherQueueTimer? _statusTimer;

    internal IntegrityGuard? Guard { get; private set; }

    internal Comfort? Comfort { get; private set; }

    internal PeerService? Peers { get; private set; }

    internal DiskWatcher? Disks { get; private set; }
    private MainWindow? _window;
    private Views.ShelfWindow? _shelf;
    private bool _exiting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => LogCrash(e.Exception, e.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception, null);
    }

    /// <summary>Deja constancia de un fallo inesperado en %LOCALAPPDATA%\MegaCopier\crash.log (las copias se reanudan al volver).</summary>
    internal static void LogCrash(Exception? exception, string? message)
    {
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
            LogCrash(ex, "OnLaunched");
            throw;
        }
    }

    private void Launch()
    {
        // Primero los pinceles del tema, luego los estilos que los usan (Resources no existe aún en el constructor).
        ThemeManager.Register(Resources);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Themes/Styles.xaml") });

        // Sin ventanas visibles la app sigue viva en la bandeja hasta que el usuario elige "Salir".
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        Dispatcher = DispatcherQueue.GetForCurrentThread();
        var theme = ThemeManager.Find(Settings.ThemeId);
        ThemeManager.Apply(theme);
        ApplyControlAccent(theme);

        var interrupted = Queue.LoadInterrupted();
        _window = new MainWindow(this);
        _window.ApplyTheme(theme);
        ApplySettings();
        Queue.ConflictResolver = (job, conflict, cancellation) => Window.AskConflict(job, conflict, cancellation);

        _tray = new TrayIcon(
            Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier-off.ico"),
            "MegaCopier",
            BuildTrayMenu);
        _tray.Activated += () => ShowWindow();
        _tray.DoubleClicked += () => SetEnabled(!Settings.Enabled, notify: true);
        Queue.JobFinished += job => Dispatcher.TryEnqueue(() => OnJobFinished(job));
        Guard = new IntegrityGuard(this);
        Comfort = new Comfort(this);
        Disks = new DiskWatcher(this);
        Disks.Refresh(force: true);
        Peers = new PeerService(this);
        Peers.Apply();

        _taskbar = new TaskbarProgress(Window.Handle);
        _statusTimer = Dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(500);
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();

        _server = new IpcServer(HandleRequestAsync);
        _server.Start();

        _paste = new PasteInterceptor(OnExplorerPaste);
        _paste.PassedThrough += reason => Debug.WriteLine($"Ctrl+V devuelto al Explorador: {reason}");
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

        var job = Queue.Submit(list.Sources, destination, list.ToOptions());
        job.Record.ExportReport = list.ExportReport;
        job.Record.Scripts = [.. list.Scripts];
        job.SaveRecord();
        if (Settings.ShowWindowOnNewCopy)
        {
            Window.ShowJobs();
            ShowWindow();
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
            options.EncryptionPassword = request.Password ?? "";
            if (operation != JobOperation.Copy)
            {
                options.CheckFreeSpace = false;
                options.Move = false;
            }

            var job = Queue.Submit(request.Sources, destination, options, request.StartAtUtc);
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

    /// <summary>Preferencias que no son de cada copia: cola, prioridad del proceso y ventana siempre visible.</summary>
    internal void ApplySettings()
    {
        Queue.MaxActiveJobs = Math.Max(0, Settings.MaxActiveJobs);
        Queue.FastDevices = Settings.FastDevices.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        _window?.ApplyAlwaysOnTop(Settings.AlwaysOnTop);
    }

    /// <summary>Aplica el tema elegido en las preferencias.</summary>
    internal void ApplyTheme() => ApplyTheme(ThemeManager.Find(Settings.ThemeId));

    /// <summary>Aplica un tema (también la vista previa del editor, sin guardarlo).</summary>
    internal void ApplyTheme(ThemeDefinition theme)
    {
        ThemeManager.Apply(theme);
        bool accentChanged = ApplyControlAccent(theme);
        _window?.ApplyTheme(theme, accentChanged);
        _shelf?.ApplyTheme();
    }

    private string _controlAccent = "";

    /// <summary>
    /// Acento de los controles estándar (casillas, interruptores, botón principal, pestañas). WinUI lo lee al cargar sus
    /// recursos: se vuelven a cargar con el color nuevo y la ventana fuerza que los controles los vuelvan a leer.
    /// </summary>
    private bool ApplyControlAccent(ThemeDefinition theme)
    {
        string accent = Appearance.ToRgbHex(theme.Accent);
        if (accent == _controlAccent)
        {
            return false;
        }

        bool first = _controlAccent.Length == 0;
        _controlAccent = accent;
        Appearance.ApplyAccent(accent);
        if (first)
        {
            return false;
        }

        var merged = Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i] is Microsoft.UI.Xaml.Controls.XamlControlsResources)
            {
                merged[i] = new Microsoft.UI.Xaml.Controls.XamlControlsResources();
                break;
            }
        }

        return true;
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

        return queued && completion.Task.Wait(TimeSpan.FromSeconds(5)) && completion.Task.Result;
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
                    Window.ShowJobs();
                    ShowWindow();
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

            case "debug-shelf" when TestMode:
                // Solo pruebas: "copiar a…" (Destination = "copy") o "mover a…" ("move") de la bandeja.
                _ = _shelf?.SendAsync(request.Destination == "move");
                return new IpcResponse { Ok = _shelf is not null };

            case "show":
                ShowWindow();
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
            new(L.Get("Tray_Open"), ShowWindow),
            new(L.Get("Tray_Shelf"), () => ShowShelf()),
            new(L.Get(Settings.Enabled ? "Tray_Disable" : "Tray_Enable"), () => SetEnabled(!Settings.Enabled, notify: true)),
            TrayMenuItem.Separator,
            new(L.Get("Tray_PauseAll"), Queue.PauseAll, Enabled: anyRunning),
            new(L.Get("Tray_ResumeAll"), Queue.ResumeAll, Enabled: anyPaused),
            TrayMenuItem.Separator,
            new(L.Get("Tray_Startup"), () => StartupRegistration.Set(!StartupRegistration.IsEnabled), Checked: StartupRegistration.IsEnabled),
            TrayMenuItem.Separator,
            new(L.Get("Tray_Exit"), ExitApp),
        ];
    }

    /// <summary>Progreso global en el icono de la bandeja y en el botón de la barra de tareas.</summary>
    private void UpdateStatus()
    {
        var running = Queue.Jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused).ToList();
        SystemActions.KeepAwake(running.Any(j => j.State == CopyJobState.Running));
        SlowDownWhileUserIsActive(running);
        Guard?.Tick(Queue.HasActiveJobs);
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
        bool finished = job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors or CopyJobState.Failed;
        if (finished)
        {
            Guard?.OnFinished(job);
        }

        if (finished)
        {
            History.Add(job);
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

        if (!Queue.HasActiveJobs && finished)
        {
            Window.OnAllFinished();
        }

        // "Al terminar": cuando ya no queda nada en marcha y la última copia acabó (no si el usuario la detuvo).
        if (FinishAction != FinishAction.None && !Queue.HasActiveJobs && job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors)
        {
            var action = FinishAction;
            FinishAction = FinishAction.None;
            Window.UpdateFinishButton();
            _ = RunFinishActionAsync(action);
        }

        if (!Settings.NotifyOnFinish || Window.IsVisibleToUser || job.State == CopyJobState.Interrupted)
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

    /// <summary>Apagar, suspender... tras una cuenta atrás de 30 s que se puede cancelar (como TeraCopy).</summary>
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
        if (job.Record.ExportReport && Window.Jobs.FirstOrDefault(j => j.Job == job) is { } model)
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

    private async void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        // Las copias en curso se detienen conservando lo copiado: al volver se continúan.
        _paste?.Dispose();
        await Task.Run(() => Queue.StopAll(TimeSpan.FromSeconds(5)));
        _statusTimer?.Stop();
        _taskbar?.Dispose();
        _tray?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        Exit();
    }
}
