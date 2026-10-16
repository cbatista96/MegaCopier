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
    private MainWindow? _window;
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
            if (operation != JobOperation.Copy)
            {
                options.CheckFreeSpace = false;
                options.Move = false;
            }

            var job = Queue.Submit(request.Sources, destination, options, request.StartAtUtc);
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

            case "show":
                ShowWindow();
                return new IpcResponse { Ok = true };

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

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        bool anyRunning = Queue.Jobs.Any(j => j.State == CopyJobState.Running);
        bool anyPaused = Queue.Jobs.Any(j => j.State == CopyJobState.Paused);
        return
        [
            new(L.Get("Tray_Open"), ShowWindow),
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

    private void OnJobFinished(CopyJob job)
    {
        bool finished = job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors or CopyJobState.Failed;
        if (finished)
        {
            History.Add(job);
            AfterJob(job);
            if (Settings.PlaySound)
            {
                SystemActions.PlayFinishSound(job.State == CopyJobState.Completed);
            }
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
            _tray?.ShowNotification(L.Get("Notify_AllDoneTitle"), L.Get("Notify_Finish" + action));
            if (action == FinishAction.ExitApp)
            {
                ExitApp();
                return;
            }

            SystemActions.Run(action);
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
            SystemActions.RunScripts(job.Record.Scripts, environment);
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
