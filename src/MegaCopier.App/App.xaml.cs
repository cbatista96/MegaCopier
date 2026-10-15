using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
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
    private DispatcherQueueTimer? _trayTimer;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    internal AppSettings Settings { get; } = AppSettings.Load();

    internal CopyQueue Queue { get; } = new(new JobStore());

    internal DispatcherQueue Dispatcher { get; private set; } = null!;

    internal MainWindow Window => _window!;

    /// <summary>Acción al terminar todas las copias (solo para esta sesión; vuelve a "nada" al ejecutarse).</summary>
    internal FinishAction FinishAction { get; set; }

    private TaskbarProgress? _taskbar;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Sin ventanas visibles la app sigue viva en la bandeja hasta que el usuario elige "Salir".
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        Dispatcher = DispatcherQueue.GetForCurrentThread();

        var interrupted = Queue.LoadInterrupted();
        _window = new MainWindow(this);
        Queue.ConflictResolver = (job, conflict, cancellation) => Window.AskConflict(job, conflict, cancellation);

        _tray = new TrayIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"), "MegaCopier", BuildTrayMenu);
        _tray.Activated += () => ShowWindow();
        Queue.JobFinished += job => Dispatcher.TryEnqueue(() => OnJobFinished(job));

        _taskbar = new TaskbarProgress(Window.Handle);
        _trayTimer = Dispatcher.CreateTimer();
        _trayTimer.Interval = TimeSpan.FromMilliseconds(500);
        _trayTimer.Tick += (_, _) => UpdateStatus();
        _trayTimer.Start();

        _server = new IpcServer(HandleRequestAsync);
        _server.Start();

        _paste = new PasteInterceptor(OnExplorerPaste);
        _paste.PassedThrough += reason => System.Diagnostics.Debug.WriteLine($"Ctrl+V devuelto al Explorador: {reason}");
        SetExplorerPaste(Settings.InterceptExplorerPaste);

        if (Settings.AutoResumeInterrupted)
        {
            foreach (var job in interrupted)
            {
                Queue.Resume(job);
            }
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
                "Hay copias interrumpidas",
                interrupted.Count == 1 ? "Una copia quedó a medias. Ábrela para continuarla desde donde quedó." : $"{interrupted.Count} copias quedaron a medias. Ábrelas para continuarlas.");
        }
    }

    /// <summary>Encola una copia (desde la ventana o desde otro proceso).</summary>
    internal CopyJob Submit(IpcRequest request)
    {
        if (request.Sources.Count == 0 || string.IsNullOrWhiteSpace(request.Destination))
        {
            throw new ArgumentException("La petición de copia necesita orígenes y un destino.");
        }

        return Queue.Submit(request.Sources, request.Destination, Settings.NewJobOptions(request));
    }

    /// <summary>Activa o desactiva la sustitución del Ctrl+V del Explorador.</summary>
    internal void SetExplorerPaste(bool enabled)
    {
        if (enabled)
        {
            _paste?.Enable();
        }
        else
        {
            _paste?.Disable();
        }
    }

    /// <summary>Ruta de la extensión del Explorador que acompaña a esta app.</summary>
    internal static string ShellExtensionPath => Path.Combine(AppContext.BaseDirectory, ShellExtensionRegistration.DllName);

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
            completion.SetResult(new IpcResponse { Ok = false, Error = "La aplicación se está cerrando." });
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

            default:
                return new IpcResponse { Ok = false, Error = $"Comando desconocido: {request.Command}" };
        }
    }

    private void ShowWindow() => Window.ShowAndActivate();

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        bool anyRunning = Queue.Jobs.Any(j => j.State == CopyJobState.Running);
        bool anyPaused = Queue.Jobs.Any(j => j.State == CopyJobState.Paused);
        return
        [
            new("Abrir MegaCopier", ShowWindow),
            TrayMenuItem.Separator,
            new("Pausar todo", Queue.PauseAll, Enabled: anyRunning),
            new("Reanudar todo", Queue.ResumeAll, Enabled: anyPaused),
            TrayMenuItem.Separator,
            new("Iniciar con Windows", () => StartupRegistration.Set(!StartupRegistration.IsEnabled), Checked: StartupRegistration.IsEnabled),
            TrayMenuItem.Separator,
            new("Salir", ExitApp),
        ];
    }

    /// <summary>Progreso global en el icono de la bandeja y en el botón de la barra de tareas.</summary>
    private void UpdateStatus()
    {
        var running = Queue.Jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused).ToList();
        if (running.Count == 0)
        {
            _tray?.SetTooltip("MegaCopier");
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
        string copies = running.Count == 1 ? "1 copia" : $"{running.Count} copias";
        _tray?.SetTooltip($"MegaCopier · {copies} · {percent} · {Format.Speed(speed)}");
    }

    private void OnJobFinished(CopyJob job)
    {
        // "Al terminar": cuando ya no queda nada en marcha y la última copia acabó (no si el usuario la detuvo).
        if (FinishAction != FinishAction.None && !Queue.HasActiveJobs && job.State is CopyJobState.Completed or CopyJobState.CompletedWithErrors)
        {
            var action = FinishAction;
            FinishAction = FinishAction.None;
            Window.UpdateFinishButton();
            _tray?.ShowNotification("Copias terminadas", action == FinishAction.Shutdown ? "El equipo se apagará en 1 minuto (cancelar: shutdown /a)." : "Suspendiendo el equipo…");
            SystemActions.Run(action);
        }

        if (!Settings.NotifyOnFinish || Window.IsVisibleToUser || job.State == CopyJobState.Interrupted)
        {
            return;
        }

        string what = job.Record.Sources.Count == 1 ? Path.GetFileName(job.Record.Sources[0].TrimEnd('\\')) : $"{job.Record.Sources.Count} elementos";
        switch (job.State)
        {
            case CopyJobState.Completed when job.Result is { } result:
                _tray?.ShowNotification("Copia terminada", $"{what} → {job.Record.Destination}\n{Format.Bytes(result.BytesTransferred)} en {Format.Duration(result.Elapsed)}");
                break;
            case CopyJobState.CompletedWithErrors:
            case CopyJobState.Failed:
                _tray?.ShowNotification("La copia tuvo problemas", $"{what}: {job.Error}");
                break;
        }
    }

    private async void ExitApp()
    {
        // Las copias en curso se detienen conservando lo copiado: al volver se continúan.
        _paste?.Dispose();
        await Task.Run(() => Queue.StopAll(TimeSpan.FromSeconds(5)));
        _trayTimer?.Stop();
        _taskbar?.Dispose();
        _tray?.Dispose();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        Exit();
    }
}
