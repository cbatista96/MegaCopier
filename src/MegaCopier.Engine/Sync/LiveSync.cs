namespace MegaCopier.Engine.Sync;

/// <summary>Resultado de una pasada de sincronización en tiempo real.</summary>
public sealed record LiveSyncResult(DateTime AtUtc, int Copied, int Deleted, int Failed, long Bytes, string? Error, CopyJobResult? Details);

/// <summary>
/// Sincronización en tiempo real (como Robocopy /MON o RealTimeSync): vigila una carpeta y, cuando deja de cambiar
/// un momento, pasa los cambios a su copia con el motor de siempre (lo idéntico se salta rápido; en espejo, lo que
/// desaparece del origen desaparece también de la copia). Si llegan cambios durante una pasada, se hace otra al
/// terminar. Si Windows pierde avisos (se llena su búfer), la siguiente pasada lo compara todo y no se pierde nada.
/// El destino queda como siempre en MegaCopier: la carpeta de origen dentro de la carpeta elegida.
/// </summary>
public sealed class LiveSync : IDisposable
{
    private readonly Func<CopyOptions> _options;
    private readonly TimeSpan _quiet;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private bool _running;
    private bool _pending;
    private bool _disposed;
    private CancellationTokenSource _cancel = new();

    /// <param name="source">Carpeta vigilada.</param>
    /// <param name="destination">Carpeta donde vive la copia (quedará destino\nombre-del-origen).</param>
    /// <param name="mirror">Borrar de la copia lo que se borra del origen.</param>
    /// <param name="options">Opciones de cada pasada (las de Ajustes); se ajustan para no preguntar nada.</param>
    /// <param name="quiet">Cuánto tiempo sin cambios antes de copiar (para no copiar un archivo a medio guardar).</param>
    public LiveSync(string source, string destination, bool mirror, Func<CopyOptions> options, TimeSpan? quiet = null)
    {
        Source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        Destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        Mirror = mirror;
        _options = options;
        _quiet = quiet ?? TimeSpan.FromSeconds(2);
        _timer = new Timer(_ => RunPass(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Source { get; }

    public string Destination { get; }

    /// <summary>Dónde queda la copia: destino\nombre-del-origen.</summary>
    public string Target => Path.Combine(Destination, Path.GetFileName(Source));

    public bool Mirror { get; }

    public bool IsSyncing
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public LiveSyncResult? Last { get; private set; }

    /// <summary>Al terminar cada pasada (en un hilo del sistema).</summary>
    public event Action<LiveSync, LiveSyncResult>? Synced;

    /// <summary>
    /// ¿Se puede vigilar este par? Null si sí; si no, por qué: el origen tiene que existir y la copia no puede quedar
    /// dentro del origen (se vigilaría a sí misma sin fin) ni al revés.
    /// </summary>
    public static string? Validate(string source, string destination)
    {
        string from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        string to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        string target = Path.Combine(to, Path.GetFileName(from));
        if (!Directory.Exists(from))
        {
            return Texts.F("E_SourceMissing", from);
        }

        if (IsInside(to, from) || IsInside(from, target) || string.Equals(from, target, StringComparison.OrdinalIgnoreCase))
        {
            return Texts.F("E_IntoItself", from);
        }

        return null;
    }

    private static bool IsInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Empieza a vigilar; con <paramref name="syncNow"/>, hace una primera pasada completa.</summary>
    public void Start(bool syncNow = true)
    {
        var watcher = new FileSystemWatcher(Source)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
        };
        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Deleted += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.Error += (_, _) => Schedule(); // búfer lleno: la próxima pasada lo compara todo
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
        if (syncNow)
        {
            SyncNow();
        }
    }

    /// <summary>Pasada inmediata (botón "Sincronizar ahora", o al arrancar).</summary>
    public void SyncNow()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _timer.Change(0, Timeout.Infinite);
            }
        }
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_running)
            {
                _pending = true;
            }
            else
            {
                _timer.Change(_quiet, Timeout.InfiniteTimeSpan); // se reinicia con cada cambio: copia cuando hay calma
            }
        }
    }

    private void RunPass()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_running || _disposed)
            {
                _pending = true;
                return;
            }

            _running = true;
            _pending = false;
            token = _cancel.Token;
        }

        LiveSyncResult result;
        try
        {
            var options = _options();
            options.Mode = Mirror ? CopyMode.Mirror : CopyMode.Copy;
            options.Move = false;
            options.DryRun = false;
            options.Operation = JobOperation.Copy;
            options.Conflict = ConflictPolicy.Overwrite;        // sin nadie a quien preguntar: la copia sigue al origen
            options.FolderConflict = FolderConflictPolicy.Merge;
            options.WaitForDevice = false;                       // un USB quitado no deja la pasada colgada
            options.WaitForSpace = false;
            options.AllOrNothing = false;
            var engine = new CopyEngine(options);
            var details = engine.Run(new CopyJobRequest([Source], Destination), token);
            result = new LiveSyncResult(DateTime.UtcNow, details.FilesCopied, details.FilesDeleted, details.Failures.Count, details.BytesTransferred, null, details);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CopyPlanException or ArgumentException)
        {
            result = new LiveSyncResult(DateTime.UtcNow, 0, 0, 1, 0, ex.Message, null);
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
                if (_pending && !_disposed)
                {
                    _pending = false;
                    _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
                }
            }
        }

        Last = result;
        Synced?.Invoke(this, result);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _cancel.Cancel();
        }

        _watcher?.Dispose();
        _timer.Dispose();
    }
}
