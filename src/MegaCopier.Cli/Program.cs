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
                "copy" => Copy(args[1..], move: false),
                "move" => Copy(args[1..], move: true),
                "send" => Send(args[1..]),
                "test" => Test(args[1..]),
                "delete" => Delete(args[1..]),
                "resume" => Resume(args.Length > 1 ? args[1] : null),
                "jobs" => Jobs(),
                "status" => Status(),
                _ => Usage($"Comando desconocido: {args[0]}"),
            };
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message);
        }
    }

    private static int Copy(string[] args, bool move)
    {
        var options = new CopyOptions { Move = move };
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
                        "ask" => ConflictPolicy.Ask,
                        var other => throw new UsageException($"Modo de conflicto desconocido: {other}"),
                    };
                    break;
                case "--limit-mb":
                    options.MaxBytesPerSecond = NextInt(args, ref i) * (long)CopyOptions.MiB;
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
                case "--mirror":
                    options.Mode = CopyMode.Mirror;
                    break;
                case "--update-only":
                    options.Mode = CopyMode.UpdateExisting;
                    break;
                case "--folders-only":
                    options.Mode = CopyMode.FoldersOnly;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--versions":
                    options.VersionsFolder = Next(args, ref i);
                    break;
                case "--skip-junk":
                    options.Filter.SkipJunk = true;
                    break;
                case "--smb-compress":
                    options.SmbCompression = true;
                    break;
                case "--atomic":
                    options.AllOrNothing = true;
                    break;
                case "--rescue":
                    options.RescueMode = true;
                    break;
                case "--delta":
                    options.DeltaCopy = true;
                    break;
                case "--organize":
                    options.Mode = CopyMode.OrganizeByDate;
                    break;
                case "--skip-duplicates":
                    options.SkipDuplicates = true;
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
        var also = new List<string>();
        bool verify = false;
        bool move = false;
        bool secure = false;
        JobOperation operation = JobOperation.Copy;
        DateTime? startAt = null;
        CopyMode? mode = null;
        bool dryRun = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--verify":
                    verify = true;
                    break;
                case "--move":
                    move = true;
                    break;
                case "--test":
                    operation = JobOperation.Test;
                    break;
                case "--delete":
                    operation = JobOperation.Delete;
                    break;
                case "--secure":
                    secure = true;
                    break;
                case "--hash":
                    operation = JobOperation.Hash;
                    break;
                case "--mirror":
                    mode = CopyMode.Mirror;
                    break;
                case "--update-only":
                    mode = CopyMode.UpdateExisting;
                    break;
                case "--folders-only":
                    mode = CopyMode.FoldersOnly;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--also" when i + 1 < args.Length:
                    also.Add(Path.GetFullPath(args[++i]));
                    break;
                case "--at" when i + 1 < args.Length:
                    startAt = ParseTime(args[++i]);
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new UsageException($"Opción desconocida para send: {arg}");
                    }

                    paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }

        bool needsDestination = operation == JobOperation.Copy;
        if (paths.Count < (needsDestination ? 2 : 1))
        {
            throw new UsageException(needsDestination ? "Indica al menos un origen y una carpeta de destino." : "Indica al menos un archivo o carpeta.");
        }

        var request = new IpcRequest
        {
            Command = "copy",
            Sources = needsDestination ? paths[..^1] : paths,
            Destination = needsDestination ? paths[^1] : null,
            Verify = verify ? true : null,
            Move = move ? true : null,
            Operation = operation == JobOperation.Copy ? null : operation,
            SecureDelete = secure ? true : null,
            ExtraDestinations = also.Count > 0 ? also : null,
            StartAtUtc = startAt?.ToUniversalTime(),
            Mode = mode,
            DryRun = dryRun ? true : null,
        };

        var watch = Stopwatch.StartNew();

        // Si la app está (o está arrancando), se le espera; si no existe, se lanza sin perder tiempo esperando la tubería.
        var response = IpcClient.IsAppRunning()
            ? IpcClient.TrySendAsync(request, connectTimeoutMs: 3000).GetAwaiter().GetResult()
            : null;
        if (response is null && (operation != JobOperation.Copy || also.Count > 0 || startAt is not null || mode is not null || dryRun))
        {
            // Lo que no cabe en la línea de comandos de la app: se arranca en la bandeja y se le envía la petición.
            if (FindApp() is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = true });
                for (int attempt = 0; attempt < 50 && response is null; attempt++)
                {
                    Thread.Sleep(100);
                    response = IpcClient.IsAppRunning() ? IpcClient.TrySendAsync(request, connectTimeoutMs: 1000).GetAwaiter().GetResult() : null;
                }
            }
        }

        if (response is not null)
        {
            Console.WriteLine(response.Ok
                ? $"Copia entregada a MegaCopier (trabajo {response.JobId}) en {watch.Elapsed.TotalMilliseconds:F0} ms."
                : $"MegaCopier rechazó la copia: {response.Error}");
            return response.Ok ? ExitOk : ExitFatal;
        }

        string? app = FindApp();
        if (app is null || !needsDestination)
        {
            Console.Error.WriteLine("MegaCopier no está abierto y no se encontró MegaCopier.exe (define MEGACOPIER_APP con su ruta).");
            return ExitFatal;
        }

        var start = new ProcessStartInfo(app) { UseShellExecute = true };
        start.ArgumentList.Add("copy");
        start.ArgumentList.Add("--dest");
        start.ArgumentList.Add(request.Destination!);
        if (verify)
        {
            start.ArgumentList.Add("--verify");
        }

        if (move)
        {
            start.ArgumentList.Add("--move");
        }

        foreach (string source in request.Sources)
        {
            start.ArgumentList.Add(source);
        }

        Process.Start(start);
        Console.WriteLine("MegaCopier no estaba abierto: se ha iniciado con la copia.");
        return ExitOk;
    }

    /// <summary>"HH:mm" (hoy, o mañana si ya pasó) o una fecha y hora completas.</summary>
    private static DateTime ParseTime(string text)
    {
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1))
        {
            var today = DateTime.Today + time;
            return today > DateTime.Now ? today : today.AddDays(1);
        }

        if (DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var when))
        {
            return when;
        }

        throw new UsageException($"Hora no válida: {text} (usa HH:mm o una fecha y hora).");
    }

    /// <summary>mcopy test: comprueba archivos de sumas sin la app.</summary>
    private static int Test(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException("Indica archivos de sumas o carpetas que los contengan.");
        }

        var sources = args.Select(Path.GetFullPath).ToList();
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Test });
        var result = engine.Run(new CopyJobRequest(sources, sources[0]));
        foreach (var item in engine.Files.Snapshot())
        {
            string mark = item.State == FileState.Verified ? "OK   " : "FALLO";
            Console.WriteLine($"{mark} {item.Source}{(item.Message is { } m ? "  (" + m + ")" : "")}");
        }

        Console.WriteLine($"{result.FilesCopied} correctos, {result.Failures.Count} con errores en {result.Elapsed.TotalSeconds:F1} s.");
        return result.Success ? ExitOk : ExitFailures;
    }

    /// <summary>mcopy delete [--secure] --yes: borra (o borra de forma segura) sin la app.</summary>
    private static int Delete(string[] args)
    {
        bool secure = args.Contains("--secure");
        bool confirmed = args.Contains("--yes");
        var paths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(Path.GetFullPath).ToList();
        if (paths.Count == 0)
        {
            throw new UsageException("Indica qué borrar.");
        }

        if (!confirmed)
        {
            throw new UsageException("Borrar no se puede deshacer: añade --yes para confirmarlo.");
        }

        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Delete, SecureDelete = secure });
        var result = engine.Run(new CopyJobRequest(paths, Path.GetDirectoryName(paths[0]) ?? paths[0]));
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"FALLO {failure.Source}: {failure.Message}");
        }

        Console.WriteLine($"{result.FilesCopied} archivos borrados{(secure ? " de forma segura" : "")}, {result.Failures.Count} con errores.");
        return result.Success ? ExitOk : ExitFailures;
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

    /// <summary>Estado de la app residente (si está abierta).</summary>
    private static int Status()
    {
        if (!IpcClient.IsAppRunning())
        {
            Console.WriteLine("running=False");
            return ExitOk;
        }

        var response = IpcClient.TrySendAsync(new IpcRequest { Command = "status" }, connectTimeoutMs: 3000).GetAwaiter().GetResult();
        Console.WriteLine($"running=True;{response?.Status}");
        return ExitOk;
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
        if (options.Conflict == ConflictPolicy.Ask && !Console.IsInputRedirected)
        {
            engine.ConflictResolver = (conflict, _) => AskInConsole(conflict, renderer);
        }

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
            if (options.DryRun)
            {
                // Simulación: qué se haría con cada archivo.
                foreach (var item in engine.Files.Snapshot())
                {
                    Console.WriteLine($"{item.Message,-48} {item.Source}");
                }
            }

            PrintSummary(result);
            if (options.DryRun)
            {
                Console.WriteLine("  Simulación: no se ha cambiado nada. Quita --dry-run para hacerlo de verdad.");
                store.Delete(job.Id);
                return ExitOk;
            }


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

    private static ConflictAnswer AskInConsole(ConflictInfo conflict, ProgressRenderer renderer)
    {
        lock (renderer)
        {
            renderer.Clear();
            Console.WriteLine($"Ya existe: {conflict.Destination}");
            Console.WriteLine($"  nuevo:     {Format.Bytes(conflict.SourceLength),10}  {conflict.SourceLastWriteUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
            Console.WriteLine($"  existente: {Format.Bytes(conflict.DestinationLength),10}  {conflict.DestinationLastWriteUtc.ToLocalTime():dd/MM/yyyy HH:mm}");
            while (true)
            {
                Console.Write("[r]eemplazar, [o]mitir, [c]onservar ambos (mayúscula = para todos): ");
                string answer = (Console.ReadLine() ?? "o").Trim();
                if (answer.Length == 0)
                {
                    continue;
                }

                bool all = char.IsUpper(answer[0]);
                ConflictDecision? decision = char.ToLowerInvariant(answer[0]) switch
                {
                    'r' => ConflictDecision.Replace,
                    'o' => ConflictDecision.Skip,
                    'c' => ConflictDecision.KeepBoth,
                    _ => null,
                };
                if (decision is { } chosen)
                {
                    return new ConflictAnswer(chosen, all);
                }
            }
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
              mcopy move <origen>... <carpeta-destino> [opciones]   Mueve (instantáneo en el mismo disco)
              mcopy send <origen>... <carpeta-destino> [--verify] [--move] [--also <otra-carpeta>]... [--at HH:mm]
                                          Entrega la copia a la app MegaCopier (ventana y bandeja)
              mcopy send --test <sumas|carpeta>...         Comprueba archivos de sumas en la app
              mcopy send --delete [--secure] <ruta>...     Borra (de forma segura) desde la app
              mcopy send --hash <carpeta|archivos>...      Crea un archivo de sumas en la app
              mcopy send ... [--mirror|--update-only|--folders-only] [--dry-run]
              mcopy test <sumas|carpeta>...   Comprueba .sha256, .md5, .sfv, .xxh3... sin la app
              mcopy delete [--secure] --yes <ruta>...   Borra sin la app (--secure: sobrescribe antes)
              mcopy resume [id]      Continúa los trabajos interrumpidos (o solo uno)
              mcopy jobs             Lista los trabajos pendientes

            Opciones de copy:
              --verify               Relee el destino sin caché, compara hashes y repara bloques dañados
              --conflict <modo>      overwrite (por defecto) | skip | newer | rename | fail | ask
              --limit-mb <n>         Limita la velocidad a n MB/s
              --force                Copia aunque el destino sea idéntico
              --chunk-mb <n>         Cada cuántos MB se confirma en disco (por defecto 64)
              --block-mb <n>         Tamaño de cada lectura/escritura en MB
              --queue <n>            Lecturas y escrituras simultáneas en vuelo
              --no-unbuffered        Usa la caché de Windows en lugar de E/S directa
              --mirror               Sincroniza: quita del destino lo que ya no está en el origen
              --update-only          Solo actualiza los archivos que ya existen en el destino
              --folders-only         Solo crea la estructura de carpetas
              --dry-run              Simula: dice qué haría con cada archivo sin cambiar nada
              --versions <carpeta>   Guarda lo reemplazado o quitado en esa carpeta del destino
              --skip-junk            Omite Thumbs.db, desktop.ini, .DS_Store, ~$...
              --smb-compress         Pide compresión SMB al copiar por red (redes lentas)
              --atomic               Todo o nada: la carpeta solo aparece cuando está completa
              --rescue               Modo rescate: salva lo legible de un disco dañado (lo ilegible en ceros)
              --delta                Al reemplazar archivos grandes, escribe solo los bloques que cambiaron
              --organize             Organiza fotos y vídeos en carpetas añoño-mes por la fecha de toma
              --skip-duplicates      No copia lo que ya está en el destino (mismo contenido, cualquier nombre)
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
