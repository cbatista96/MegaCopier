using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.Engine.Ipc;
using MegaCopier.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MegaCopier.App;

/// <summary>Lo que pide la línea de comandos al arrancar.</summary>
internal sealed record StartupArgs(bool Background, IpcRequest? Request, bool Restart = false)
{
    /// <summary>
    /// MegaCopier.exe                                         → abrir la ventana
    /// MegaCopier.exe --background                            → solo bandeja (arranque con Windows)
    /// MegaCopier.exe copy --dest D:\x [--verify] [--move] a b → copiar (o mover) a y b a D:\x
    /// MegaCopier.exe --restart                               → esperar a que se cierre la instancia anterior y abrir
    /// MegaCopier.exe run lista.mclist                         → lanzar una lista guardada (tareas programadas)
    /// MegaCopier.exe test|hash|wipe rutas...                → comprobar sumas, crear sumas, borrar de forma segura
    /// </summary>
    public static StartupArgs Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new StartupArgs(false, new IpcRequest { Command = "show" });
        }

        switch (args[0])
        {
            case "--background":
                return new StartupArgs(true, null);
            case "--restart":
                return new StartupArgs(false, new IpcRequest { Command = "show" }, Restart: true);
            case "test" or "hash" or "wipe" when args.Length > 1:
                // Desde el submenú MegaCopier del Explorador con la app cerrada.
                var paths = args[1..].Select(Path.GetFullPath).ToList();
                return new StartupArgs(false, args[0] == "wipe"
                    ? new IpcRequest { Command = "confirm-delete", SecureDelete = true, Sources = paths }
                    : new IpcRequest { Command = "copy", Operation = args[0] == "test" ? Engine.JobOperation.Test : Engine.JobOperation.Hash, Sources = paths });
            case "run" when args.Length > 1:
                // Tarea programada: MegaCopier.exe run "lista.mclist"
                return new StartupArgs(false, new IpcRequest { Command = "run-list", Sources = [Path.GetFullPath(args[1])] });
            case "copy":
                var request = new IpcRequest { Command = "copy" };
                for (int i = 1; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--dest" when i + 1 < args.Length:
                            request.Destination = args[++i];
                            break;
                        case "--verify":
                            request.Verify = true;
                            break;
                        case "--move":
                            request.Move = true;
                            break;
                        default:
                            request.Sources.Add(args[i]);
                            break;
                    }
                }

                return new StartupArgs(false, request);
            default:
                return new StartupArgs(false, new IpcRequest { Command = "show" });
        }
    }
}

public static class Program
{
    public static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    internal static StartupArgs Startup { get; private set; } = new(false, null);

    [STAThread]
    public static int Main(string[] args)
    {
        // Tareas del instalador: no abren ninguna ventana.
        if (args.Length > 0 && args[0] is "--install" or "--uninstall" or "--exit")
        {
            return Setup(args);
        }

        // Archivos en uso: este proceso (con permisos de administrador) los copia desde una instantánea y sale.
        if (args.Length > 1 && args[0] == "--vss")
        {
            return CopyLockedFiles(args[1]);
        }

        // Modo copia de seguridad: este proceso (con permisos de administrador) repite la copia saltándose los permisos.
        if (args.Length > 1 && args[0] == "--backup")
        {
            return CopyWithBackupPrivileges(args[1]);
        }

        var startup = StartupArgs.Parse(args);

        // Instancia única: el primer proceso se queda residente; los siguientes le pasan su petición y salen.
        using var mutex = new Mutex(initiallyOwned: true, IpcProtocol.MutexName, out bool firstInstance);
        if (!firstInstance && startup.Restart)
        {
            // Reinicio: la instancia anterior se está cerrando; se espera a que suelte el mutex.
            try
            {
                firstInstance = mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                firstInstance = true;
            }
        }

        if (!firstInstance)
        {
            if (startup.Request is null)
            {
                return 0; // "--background" con la app ya abierta: nada que hacer
            }

            var response = IpcClient.TrySendAsync(startup.Request, connectTimeoutMs: 5000).GetAwaiter().GetResult();
            return response?.Ok == true ? 0 : 1;
        }

        Startup = startup;
        LanguageSetting.ApplyAtStartup();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        return 0;
    }

    /// <summary>"--vss petición.json": instantánea, copia de los archivos en uso y resultado en la misma petición.</summary>
    private static int CopyLockedFiles(string requestPath)
    {
        if (Engine.LockedFiles.Load(requestPath) is not { } request)
        {
            return 2;
        }

        // En pruebas, una carpeta hace de instantánea (sin pedir permisos de administrador).
        Engine.ISnapshotProvider snapshots = App.TestMode && Environment.GetEnvironmentVariable("MEGACOPIER_TEST_SNAPSHOT") is { Length: > 0 } fake
            ? new FolderSnapshot(fake)
            : new Engine.WmiSnapshotProvider();
        var settings = AppSettings.Load();
        var options = settings.NewJobOptions(new IpcRequest { Command = "copy" });
        Engine.LockedFiles.Copy(request, snapshots, options);
        Engine.LockedFiles.Save(request, requestPath);
        return request.Errors.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// "--backup id": repite el trabajo con los privilegios de copia y restauración (lo ya copiado se omite como
    /// idéntico). Deja en Datos\backup-id.txt cuántos fallaron y el primer error, para el aviso de la app.
    /// </summary>
    private static int CopyWithBackupPrivileges(string jobId)
    {
        string resultPath = Engine.DataFolder.Combine($"backup-{jobId}.txt");
        if (new Engine.Jobs.JobStore().Get(jobId) is not { } record)
        {
            return 2;
        }

        var options = record.ToOptions();
        options.BackupMode = true;
        options.DryRun = false;
        try
        {
            var result = new Engine.CopyEngine(options).Run(new Engine.CopyJobRequest(record.Sources, record.Destination) { Excluded = record.Excluded, Resumed = true });
            File.WriteAllText(resultPath, $"{result.Failures.Count}\n{result.Failures.FirstOrDefault()?.Message}");
            return result.Failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Engine.CopyPlanException)
        {
            File.WriteAllText(resultPath, $"-1\n{ex.Message}");
            return 2;
        }
    }

    private sealed class FolderSnapshot(string root) : Engine.ISnapshotProvider
    {
        public (string Root, string Id) Create(string volume) => (root, "prueba");

        public void Delete(string id)
        {
        }
    }

    /// <summary>
    /// --install [--startup] [--shellmenu]: registra lo elegido en el instalador.
    /// --exit: cierra la app si está abierta (antes de actualizar).
    /// --uninstall: cierra la app si está abierta y deshace todos los registros.
    /// </summary>
    private static int Setup(string[] args)
    {
        if (args[0] == "--install")
        {
            if (args.Contains("--shellmenu") && App.ShellExtensionPath is { } dll)
            {
                ShellExtensionRegistration.Register(dll);
                ShellMenuPackage.Register();
            }

            if (args.Contains("--startup"))
            {
                StartupRegistration.Set(true);
            }

            return 0;
        }

        // Pedir a la instancia residente que se cierre (las copias en curso quedan reanudables).
        if (IpcClient.IsAppRunning())
        {
            IpcClient.TrySendAsync(new IpcRequest { Command = "exit" }, connectTimeoutMs: 3000).GetAwaiter().GetResult();
            for (int i = 0; i < 100 && IpcClient.IsAppRunning(); i++)
            {
                Thread.Sleep(100);
            }
        }

        if (args[0] == "--exit")
        {
            return 0; // actualización: solo había que cerrar la app
        }

        ShellExtensionRegistration.Unregister();
        ShellMenuPackage.Unregister();
        StartupRegistration.Set(false);
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\MegaCopier", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return 0;
    }
}
