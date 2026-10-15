using System.Diagnostics;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MegaCopier.App;

/// <summary>Lo que pide la línea de comandos al arrancar.</summary>
internal sealed record StartupArgs(bool Background, IpcRequest? Request)
{
    /// <summary>
    /// MegaCopier.exe                                → abrir la ventana
    /// MegaCopier.exe --background                   → solo bandeja (arranque con Windows)
    /// MegaCopier.exe copy --dest D:\x [--verify] [--move] a b → copiar (o mover) a y b a D:\x
    /// </summary>
    public static StartupArgs Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new StartupArgs(false, new IpcRequest { Command = "show" });
        }

        if (args[0] == "--background")
        {
            return new StartupArgs(true, null);
        }

        if (args[0] == "copy")
        {
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
        }

        return new StartupArgs(false, new IpcRequest { Command = "show" });
    }
}

public static class Program
{
    public static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    internal static StartupArgs Startup { get; private set; } = new(false, null);

    [STAThread]
    public static int Main(string[] args)
    {
        var startup = StartupArgs.Parse(args);

        // Instancia única: el primer proceso se queda residente; los siguientes le pasan su petición y salen.
        using var mutex = new Mutex(initiallyOwned: true, IpcProtocol.MutexName, out bool firstInstance);
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
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        return 0;
    }

    public static double ElapsedMs => Stopwatch.GetElapsedTime(StartTimestamp).TotalMilliseconds;
}
