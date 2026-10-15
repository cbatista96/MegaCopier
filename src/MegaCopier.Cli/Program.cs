using System.Diagnostics;
using System.Text;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;

Console.OutputEncoding = Encoding.UTF8;
return Cli.Run(args);

internal static class Cli
{
    private const int ExitOk = 0;
    private const int ExitFailures = 1;
    private const int ExitUsage = 2;
    private const int ExitPaused = 3;
    private const int ExitFatal = 4;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        try
        {
            return args[0] switch
            {
                "copy" => Copy(args[1..]),
                "send" => Send(args[1..]),
                "resume" => Resume(args.Length > 1 ? args[1] : null),
                "jobs" => Jobs(),
                _ => Usage($"Comando desconocido: {args[0]}"),
            };
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message);
        }
    }

    private static int Copy(string[] args)
    {
        var options = new CopyOptions();
        var paths = new List<string>();
        long? crashAfter = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--verify":
                    options.Verify = true;
                    break;
                case "--force":
                    options.ForceRecopy = true;
                    break;
                case "--no-unbuffered":
                    options.UseUnbufferedIo = false;
                    break;
                case "--conflict":
                    options.Conflict = Next(args, ref i) switch
                    {
                        "overwrite" => ConflictPolicy.Overwrite,
                        "skip" => ConflictPolicy.Skip,
                        "newer" => ConflictPolicy.OverwriteIfNewer,
                        "rename" => ConflictPolicy.KeepBoth,
                        "fail" => ConflictPolicy.Fail,
                        var other => throw new UsageException($"Modo de conflicto desconocido: {other}"),
                    };
                    break;
                case "--chunk-mb":
                    options.ChunkSize = NextInt(args, ref i) * (long)CopyOptions.MiB;
                    break;
                case "--block-mb":
                    options.BlockSize = NextInt(args, ref i) * CopyOptions.MiB;
                    break;
                case "--queue":
                    options.ReadQueueDepth = options.WriteQueueDepth = NextInt(args, ref i);
                    break;
                case "--crash-after-mb":
                    crashAfter = NextInt(args, ref i) * (long)CopyOptions.MiB;
                    break;
                case var flag when flag.StartsWith("--", StringComparison.Ordinal):
                    throw new UsageException($"Opción desconocida: {flag}");
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count < 2)
        {
            throw new UsageException("Indica al menos un origen y una carpeta de destino.");
        }

        try
        {
            _ = new CopyEngine(options); // valida la combinación de opciones antes de guardar el trabajo
        }
        catch (ArgumentException ex)
        {
            throw new UsageException(ex.Message);
        }

        var store = new JobStore();
        var job = CopyJobRecord.New(paths[..^1], paths[^1], options);
        store.Save(job);
        return Execute(job, options, store, crashAfter);
    }

    /// <summary>
    /// Entrega la copia a la aplicación residente (la que muestra la ventana). Si no está abierta, la arranca
    /// con la petición como argumentos. Es el mismo camino que usará la integración con el Explorador.
    /// </summary>
    private static int Send(string[] args)
    {
        var paths = new List<string>();
        bool verify = false;
        foreach (string arg in args)
        {
            if (arg == "--verify")
            {
                verify = true;
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Opción desconocida para send: {arg}");
            }
            else
            {
                paths.Add(Path.GetFullPath(arg));
            }
        }

        if (paths.Count < 2)
        {
            throw new UsageException("Indica al menos un origen y una carpeta de destino.");
        }

        var request = new IpcRequest
        {
            Command = "copy",
            Sources = paths[..^1],
            Destination = paths[^1],
            Verify = verify ? true : null,
        };

        var watch = Stopwatch.StartNew();

        // Si la app está (o está arrancando), se le espera; si no existe, se lanza sin perder tiempo esperando la tubería.
        var response = IpcClient.IsAppRunning()
            ? IpcClient.TrySendAsync(request, connectTimeoutMs: 3000).GetAwaiter().GetResult()
            : null;
        if (response is not null)
        {
            Console.WriteLine(response.Ok
                ? $"Copia entregada a MegaCopier (trabajo {response.JobId}) en {watch.Elapsed.TotalMilliseconds:F0} ms."
                : $"MegaCopier rechazó la copia: {response.Error}");
            return response.Ok ? ExitOk : ExitFatal;
        }

        string? app = FindApp();
        if (app is null)
        {
            Console.Error.WriteLine("MegaCopier no está abierto y no se encontró MegaCopier.exe (define MEGACOPIER_APP con su ruta).");
            return ExitFatal;
        }

        var start = new ProcessStartInfo(app) { UseShellExecute = true };
        start.ArgumentList.Add("copy");
        start.ArgumentList.Add("--dest");
        start.ArgumentList.Add(request.Destination);
        if (verify)
        {
            start.ArgumentList.Add("--verify");
        }

        foreach (string source in request.Sources)
        {
            start.ArgumentList.Add(source);
        }

        Process.Start(start);
        Console.WriteLine("MegaCopier no estaba abierto: se ha iniciado con la copia.");
        return ExitOk;
    }

    private static string? FindApp()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("MEGACOPIER_APP");
        string[] candidates =
        [
            fromEnvironment ?? "",
            Path.Combine(AppContext.BaseDirectory, "MegaCopier.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "app", "MegaCopier.exe"),
        ];
        return candidates
            .Where(c => c.Length > 0 && File.Exists(c))
            .Select(Path.GetFullPath)
            .FirstOrDefault(c => !string.Equals(c, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)); // nunca a sí misma
    }

    private static int Resume(string? id)
    {
        var store = new JobStore();
        IReadOnlyList<CopyJobRecord> jobs;
        if (id is null)
        {
            jobs = store.List();
        }
        else
        {
            var job = store.Get(id) ?? throw new UsageException($"No existe el trabajo {id}.");
            jobs = [job];
        }

        if (jobs.Count == 0)
        {
            Console.WriteLine("No hay trabajos pendientes.");
            return ExitOk;
        }

        int worst = ExitOk;
        foreach (var job in jobs)
        {
            Console.WriteLine($"Reanudando {job.Id}: {string.Join(", ", job.Sources)} → {job.Destination}");
            worst = Math.Max(worst, Execute(job, job.ToOptions(), store, crashAfter: null));
            if (worst >= ExitPaused)
            {
                break;
            }
        }

        return worst;
    }

    private static int Jobs()
    {
        var jobs = new JobStore().List();
        if (jobs.Count == 0)
        {
            Console.WriteLine("No hay trabajos pendientes.");
            return ExitOk;
        }

        foreach (var job in jobs)
        {
            Console.WriteLine($"{job.Id}  {job.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {string.Join(", ", job.Sources)} → {job.Destination}");
        }

        return ExitOk;
    }

    private static int Execute(CopyJobRecord job, CopyOptions options, JobStore store, long? crashAfter)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        var engine = new CopyEngine(options);
        var renderer = new ProgressRenderer();
        engine.ProgressChanged += renderer.Render;

        if (crashAfter is long limit)
        {
            engine.ChunkCommitted += (path, bytes) =>
            {
                if (bytes >= limit)
                {
                    renderer.Clear();
                    Console.WriteLine($"[prueba] Matando el proceso tras confirmar {Format.Bytes(bytes)} de {Path.GetFileName(path)}.");
                    Console.WriteLine($"[prueba] Continúa con: mcopy resume {job.Id}");
                    Process.GetCurrentProcess().Kill();
                }
            };
        }

        try
        {
            var result = engine.Run(new CopyJobRequest(job.Sources, job.Destination), cancellation.Token);
            renderer.Clear();
            PrintSummary(result);

            if (result.Success)
            {
                store.Delete(job.Id);
                return ExitOk;
            }

            Console.WriteLine($"Hay archivos con errores. Reintenta con: mcopy resume {job.Id}");
            return ExitFailures;
        }
        catch (OperationCanceledException)
        {
            renderer.Clear();
            Console.WriteLine($"Copia en pausa. Continúa desde donde quedó con: mcopy resume {job.Id}");
            return ExitPaused;
        }
        catch (CopyPlanException ex)
        {
            renderer.Clear();
            Console.Error.WriteLine($"Error: {ex.Message}");
            store.Delete(job.Id);
            return ExitFatal;
        }
        catch (Exception ex)
        {
            renderer.Clear();
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine($"El trabajo queda pendiente: mcopy resume {job.Id}");
            return ExitFatal;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static void PrintSummary(CopyJobResult result)
    {
        double seconds = Math.Max(result.Elapsed.TotalSeconds, 0.001);
        Console.WriteLine($"Terminado en {result.Elapsed:hh\\:mm\\:ss\\.f}");
        Console.WriteLine($"  Copiados: {result.FilesCopied} (reanudados: {result.FilesResumed})   Omitidos: {result.FilesSkipped}   Errores: {result.Failures.Count}");
        Console.WriteLine($"  Transferido: {Format.Bytes(result.BytesTransferred)}   Sin recopiar gracias a la reanudación: {Format.Bytes(result.BytesResumed)}");
        Console.WriteLine($"  Velocidad media: {Format.Bytes((long)(result.BytesTransferred / seconds))}/s");

        if (result.ChunksRejectedOnResume > 0 || result.ChunksRepaired > 0)
        {
            Console.WriteLine($"  Bloques descartados al reanudar: {result.ChunksRejectedOnResume}   Bloques reparados en la verificación: {result.ChunksRepaired}");
        }

        foreach (var failure in result.Failures)
        {
            Console.WriteLine($"  ✗ {failure.Source}: {failure.Message}");
        }
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new UsageException($"Falta el valor de {args[i]}.");
        }

        return args[++i];
    }

    private static int NextInt(string[] args, ref int i)
    {
        string flag = args[i];
        return int.TryParse(Next(args, ref i), out int value) && value > 0
            ? value
            : throw new UsageException($"{flag} necesita un número positivo.");
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintUsage();
        return ExitUsage;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            MegaCopier — copia que sobrevive a apagones

            Uso:
              mcopy copy <origen>... <carpeta-destino> [opciones]
              mcopy send <origen>... <carpeta-destino> [--verify]
                                          Entrega la copia a la app MegaCopier (ventana y bandeja)
              mcopy resume [id]      Continúa los trabajos interrumpidos (o solo uno)
              mcopy jobs             Lista los trabajos pendientes

            Opciones de copy:
              --verify               Relee el destino sin caché, compara hashes y repara bloques dañados
              --conflict <modo>      overwrite (por defecto) | skip | newer | rename | fail
              --force                Copia aunque el destino sea idéntico
              --chunk-mb <n>         Cada cuántos MB se confirma en disco (por defecto 64)
              --block-mb <n>         Tamaño de cada lectura/escritura en MB
              --queue <n>            Lecturas y escrituras simultáneas en vuelo
              --no-unbuffered        Usa la caché de Windows en lugar de E/S directa
              --crash-after-mb <n>   [prueba] Mata el proceso tras confirmar n MB de un archivo

            Ctrl+C pausa la copia; 'resume' continúa desde el último bloque confirmado.
            """);
    }

    private sealed class UsageException(string message) : Exception(message);
}

internal sealed class ProgressRenderer
{
    private readonly bool _interactive = !Console.IsOutputRedirected;
    private long _lastTicks;
    private int _lastLength;

    public void Render(CopyProgress p)
    {
        long now = Environment.TickCount64;
        long interval = _interactive ? 200 : 2000;
        if (p.Phase != CopyPhase.Completed && now - _lastTicks < interval)
        {
            return;
        }

        _lastTicks = now;
        string phase = p.Phase switch
        {
            CopyPhase.Validating => "comprobando",
            CopyPhase.Verifying => "verificando",
            _ => "",
        };
        string remaining = p.Remaining is { } r ? $"quedan {r:hh\\:mm\\:ss}" : "";
        string line = $"{p.Fraction * 100,5:F1}% │ {Format.Bytes(p.CompletedBytes)} de {Format.Bytes(p.TotalBytes)} │ "
            + $"{Format.Bytes((long)p.BytesPerSecond)}/s │ {remaining} │ {phase} {Path.GetFileName(p.CurrentFile)}";

        if (!_interactive)
        {
            Console.WriteLine(line);
            return;
        }

        int width = SafeWidth() - 1;
        if (line.Length > width)
        {
            line = line[..width];
        }

        Console.Write("\r" + line.PadRight(Math.Max(_lastLength, line.Length)));
        _lastLength = line.Length;
    }

    public void Clear()
    {
        if (_interactive && _lastLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastLength) + "\r");
            _lastLength = 0;
        }
    }

    private static int SafeWidth()
    {
        try
        {
            return Math.Max(Console.WindowWidth, 40);
        }
        catch (IOException)
        {
            return 120;
        }
    }
}

internal static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:F2} {Units[unit]}";
    }
}
