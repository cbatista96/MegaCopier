using System.Diagnostics;
using System.Text;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Jobs;

Console.OutputEncoding = Encoding.UTF8;

// El mismo idioma que la app (lo publica en el registro); si no hay ninguno elegido, el de Windows.
if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\MegaCopier", "Language", null) is string { Length: > 0 } language)
{
    try
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
    }
    catch (System.Globalization.CultureNotFoundException)
    {
    }
}

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
                "decrypt" => Copy(args[1..], move: false, decrypt: true),
                "move" => Copy(args[1..], move: true),
                "send" => Send(args[1..]),
                "test" => Test(args[1..]),
                "delete" => Delete(args[1..]),
                "resume" => Resume(args.Length > 1 ? args[1] : null),
                "jobs" => Jobs(),
                "status" => Status(),
                _ => Usage(Texts.F("C_UnknownCommand", args[0])),
            };
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message);
        }
    }

    private static int Copy(string[] args, bool move, bool decrypt = false)
    {
        var options = new CopyOptions { Move = move, Operation = decrypt ? JobOperation.Decrypt : JobOperation.Copy };
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
                        var other => throw new UsageException(Texts.F("C_UnknownConflict", other)),
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
                case "--backup":
                    options.BackupMode = true; // como robocopy /B: en una consola de administrador
                    break;
                case "--efsraw":
                    options.EfsRaw = true;
                    break;
                case "--zip":
                    options.Zip = true;
                    break;
                case "--connections":
                    options.RemoteConnections = Math.Clamp(NextInt(args, ref i), 1, CopyOptions.MaxRemoteConnections);
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
                case "--encrypt":
                    options.Encrypt = true;
                    break;
                case "--crash-after-mb":
                    crashAfter = NextInt(args, ref i) * (long)CopyOptions.MiB;
                    break;
                case var flag when flag.StartsWith("--", StringComparison.Ordinal):
                    throw new UsageException(Texts.F("C_UnknownOption", flag));
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count < 2)
        {
            throw new UsageException(Texts.Get("C_NeedSourceAndDestination"));
        }

        if (options.Encrypt || decrypt)
        {
            options.EncryptionPassword = ReadPassword(confirm: options.Encrypt);
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
                        throw new UsageException(Texts.F("C_UnknownSendOption", arg));
                    }

                    paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }

        bool needsDestination = operation == JobOperation.Copy;
        if (paths.Count < (needsDestination ? 2 : 1))
        {
            throw new UsageException(Texts.Get(needsDestination ? "C_NeedSourceAndDestination" : "C_NeedItem"));
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
                ? Texts.F("C_Delivered", response.JobId, watch.Elapsed.TotalMilliseconds)
                : Texts.F("C_Rejected", response.Error));
            return response.Ok ? ExitOk : ExitFatal;
        }

        string? app = FindApp();
        if (app is null || !needsDestination)
        {
            Console.Error.WriteLine(Texts.Get("C_AppNotFound"));
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
        Console.WriteLine(Texts.Get("C_AppStarted"));
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

        throw new UsageException(Texts.F("C_BadTime", text));
    }

    /// <summary>mcopy test: comprueba archivos de sumas sin la app.</summary>
    private static int Test(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException(Texts.Get("C_NeedChecksums"));
        }

        var sources = args.Select(Path.GetFullPath).ToList();
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Test });
        var result = engine.Run(new CopyJobRequest(sources, sources[0]));
        foreach (var item in engine.Files.Snapshot())
        {
            string mark = item.State == FileState.Verified ? Texts.Get("C_Ok") : Texts.Get("C_Failed");
            Console.WriteLine($"{mark} {item.Source}{(item.Message is { } m ? "  (" + m + ")" : "")}");
        }

        Console.WriteLine(Texts.F("C_TestDone", result.FilesCopied, result.Failures.Count, result.Elapsed.TotalSeconds));
        return result.Success ? ExitOk : ExitFailures;
    }

    /// <summary>mcopy delete [--secure [--wipe zeros|random|dod3|dod7]] --yes: borra (o borra de forma segura) sin la app.</summary>
    private static int Delete(string[] args)
    {
        bool secure = args.Contains("--secure");
        bool confirmed = args.Contains("--yes");
        var method = WipeMethod.Zeros;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--wipe")
            {
                method = Next(args, ref i) switch
                {
                    "zeros" => WipeMethod.Zeros,
                    "random" => WipeMethod.Random,
                    "dod3" => WipeMethod.Dod3,
                    "dod7" => WipeMethod.Dod7,
                    var other => throw new UsageException(Texts.F("C_UnknownWipe", other)),
                };
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                paths.Add(Path.GetFullPath(args[i]));
            }
        }

        if (paths.Count == 0)
        {
            throw new UsageException(Texts.Get("C_NeedDelete"));
        }

        if (!confirmed)
        {
            throw new UsageException(Texts.Get("C_NeedYes"));
        }

        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Delete, SecureDelete = secure, WipeMethod = method });
        var result = engine.Run(new CopyJobRequest(paths, Path.GetDirectoryName(paths[0]) ?? paths[0]));
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"{Texts.Get("C_Failed")} {failure.Source}: {failure.Message}");
        }

        Console.WriteLine(Texts.F(secure ? "C_WipeDone" : "C_DeleteDone", result.FilesCopied, result.Failures.Count));
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
            var job = store.Get(id) ?? throw new UsageException(Texts.F("C_NoJob", id));
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
            Console.WriteLine(Texts.F("C_Resuming", job.Id, string.Join(", ", job.Sources), job.Destination));
            var options = job.ToOptions();
            if (options.Encrypt || options.Operation == JobOperation.Decrypt)
            {
                options.EncryptionPassword = ReadPassword(confirm: false); // la contraseña no se guarda en el trabajo
            }

            worst = Math.Max(worst, Execute(job, options, store, crashAfter: null));
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
                    Console.WriteLine(Texts.F("C_TestKill", Format.Bytes(bytes), Path.GetFileName(path)));
                    Console.WriteLine(Texts.F("C_TestContinue", job.Id));
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
                Console.WriteLine("  " + Texts.Get("C_DryRunNote"));
                store.Delete(job.Id);
                return ExitOk;
            }


            if (result.Success)
            {
                store.Delete(job.Id);
                return ExitOk;
            }

            Console.WriteLine(Texts.F("C_RetryHint", job.Id));
            return ExitFailures;
        }
        catch (OperationCanceledException)
        {
            renderer.Clear();
            Console.WriteLine(Texts.F("C_PausedHint", job.Id));
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
            Console.Error.WriteLine(Texts.F("C_JobPending", job.Id));
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
            Console.WriteLine(Texts.F("C_Exists", conflict.Destination));
            string newLabel = Texts.Get("C_New") + ":", existingLabel = Texts.Get("C_Existing") + ":";
            int labelWidth = Math.Max(newLabel.Length, existingLabel.Length) + 1;
            Console.WriteLine($"  {newLabel.PadRight(labelWidth)}{Format.Bytes(conflict.SourceLength),10}  {conflict.SourceLastWriteUtc.ToLocalTime():g}");
            Console.WriteLine($"  {existingLabel.PadRight(labelWidth)}{Format.Bytes(conflict.DestinationLength),10}  {conflict.DestinationLastWriteUtc.ToLocalTime():g}");
            string keys = Texts.Get("C_ConflictKeys"); // reemplazar, omitir, conservar ambos (una letra cada uno, en este idioma)
            while (true)
            {
                Console.Write(Texts.Get("C_ConflictPrompt") + " ");
                string answer = (Console.ReadLine() ?? keys[1].ToString()).Trim();
                if (answer.Length == 0)
                {
                    continue;
                }

                bool all = char.IsUpper(answer[0]);
                int choice = keys.IndexOf(char.ToLowerInvariant(answer[0]));
                ConflictDecision? decision = choice switch
                {
                    0 => ConflictDecision.Replace,
                    1 => ConflictDecision.Skip,
                    2 => ConflictDecision.KeepBoth,
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
        Console.WriteLine(Texts.F("C_DoneIn", result.Elapsed.ToString(@"hh\:mm\:ss\.f")));
        Console.WriteLine("  " + Texts.F("C_Summary", result.FilesCopied, result.FilesResumed, result.FilesSkipped, result.Failures.Count));
        Console.WriteLine("  " + Texts.F("C_Transferred", Format.Bytes(result.BytesTransferred), Format.Bytes(result.BytesResumed)));
        Console.WriteLine("  " + Texts.F("C_AverageSpeed", Format.Bytes((long)(result.BytesTransferred / seconds))));

        if (result.ChunksRejectedOnResume > 0 || result.ChunksRepaired > 0)
        {
            Console.WriteLine("  " + Texts.F("C_Blocks", result.ChunksRejectedOnResume, result.ChunksRepaired));
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
            throw new UsageException(Texts.F("C_MissingValue", args[i]));
        }

        return args[++i];
    }

    private static int NextInt(string[] args, ref int i)
    {
        string flag = args[i];
        return int.TryParse(Next(args, ref i), out int value) && value > 0
            ? value
            : throw new UsageException(Texts.F("C_NeedPositive", flag));
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintUsage();
        return ExitUsage;
    }

    /// <summary>De MEGACOPIER_PASSWORD o preguntada sin mostrarla (al cifrar, dos veces).</summary>
    private static string ReadPassword(bool confirm)
    {
        if (Environment.GetEnvironmentVariable("MEGACOPIER_PASSWORD") is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        string first = Prompt(Texts.Get("C_Password") + " ");
        if (confirm && (first.Length < 8 || Prompt(Texts.Get("C_Repeat") + " ") != first))
        {
            throw new UsageException(Texts.Get(first.Length < 8 ? "C_PasswordShort" : "C_PasswordMismatch"));
        }

        return first;

        static string Prompt(string label)
        {
            Console.Write(label);
            var text = new System.Text.StringBuilder();
            while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && text.Length > 0)
                {
                    text.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    text.Append(key.KeyChar);
                }
            }

            Console.WriteLine();
            return text.ToString();
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(Texts.Get("C_Usage"));
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
            CopyPhase.Validating => Texts.Get("C_Checking"),
            CopyPhase.Verifying => Texts.Get("C_Verifying"),
            _ => "",
        };
        string remaining = p.Remaining is { } r ? Texts.F("C_Left", r.ToString(@"hh\:mm\:ss")) : "";
        string line = $"{p.Fraction * 100,5:F1}% │ {Texts.F("C_Of", Format.Bytes(p.CompletedBytes), Format.Bytes(p.TotalBytes))} │ "
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
