using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Jobs;

public enum CopyJobState
{
    /// <summary>Esperando a que quede libre el disco.</summary>
    Queued,
    Running,
    Paused,
    Completed,

    /// <summary>Terminó, pero algunos archivos fallaron.</summary>
    CompletedWithErrors,

    /// <summary>Error que impidió continuar (origen inexistente, sin espacio...).</summary>
    Failed,

    /// <summary>Detenida (por el usuario, un apagón o un cierre): se puede reanudar desde donde quedó.</summary>
    Interrupted,
}

/// <summary>Un trabajo de la cola, observable desde la interfaz.</summary>
public sealed class CopyJob
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cancellation;
    private CopyEngine? _engine;

    private readonly List<LogEntry> _log = [];
    private readonly Action<CopyJobRecord> _save;
    private CopyEngine? _lastEngine;

    internal CopyJob(CopyJobRecord record, CopyJobState state, Action<CopyJobRecord> save)
    {
        Record = record;
        State = state;
        _save = save;
        Devices = record.Sources.Append(record.Destination).Select(VolumeInfo.DeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Lista de archivos (existe desde que el trabajo empieza y se conserva al terminar).</summary>
    public TransferList? Files { get; private set; }

    /// <summary>Opciones del trabajo en curso; verificar y desatendido se pueden cambiar en caliente.</summary>
    public CopyOptions? Options => _lastEngine?.Options;

    public float[] SpeedHistory => _lastEngine?.SpeedHistory ?? [];

    public string? ChecksumFile => _lastEngine?.ChecksumFile;

    public DateTime? StartedAt { get; private set; }

    public DateTime? FinishedAt { get; internal set; }

    /// <summary>Nueva entrada en el registro del trabajo (desde hilos de copia).</summary>
    public event Action<CopyJob, LogEntry>? Logged;

    public IReadOnlyList<LogEntry> LogSnapshot()
    {
        lock (_log)
        {
            return [.. _log];
        }
    }

    /// <summary>Salta el archivo que se está copiando.</summary>
    public void SkipCurrent() => _engine?.SkipCurrent();

    /// <summary>Quita un archivo pendiente; se recuerda aunque el trabajo se reanude más tarde.</summary>
    public bool RemoveFile(int id)
    {
        var item = Files?.Snapshot().FirstOrDefault(i => i.Id == id);
        if (item is null || Files?.Remove(id) != true)
        {
            return false;
        }

        Record.Excluded.Add(item.Source);
        SaveIfPending();
        return true;
    }

    /// <summary>Añade orígenes a un trabajo en curso.</summary>
    public int AddSources(IReadOnlyList<string> sources, int? beforeId = null)
    {
        var engine = _engine ?? throw new InvalidOperationException(Texts.Get("E_JobNotRunning"));
        int added = engine.AddSources(sources, beforeId);
        Record.Sources.AddRange(sources);
        SaveIfPending();
        if (beforeId is not null)
        {
            SaveOrder();
        }
        return added;
    }

    /// <summary>Arrastrar en la lista: coloca un pendiente antes de otro (null = al final) y guarda el orden.</summary>
    public bool MoveFile(int id, int? beforeId)
    {
        if (Files?.MoveBefore(id, beforeId) != true)
        {
            return false;
        }

        SaveOrder();
        return true;
    }

    /// <summary>Guarda el orden actual de los pendientes para que reanudar lo respete (hasta 100 000 archivos).</summary>
    public void SaveOrder()
    {
        if (Files is { } files && files.Count <= 100_000)
        {
            Record.FileOrder = [.. files.PendingOrder()];
            SaveIfPending();
        }
    }

    /// <summary>
    /// Límite temporal (no se guarda): "más lento mientras usas el equipo". null = volver al límite del trabajo.
    /// El más restrictivo de los dos manda.
    /// </summary>
    public void SetSpeedOverride(long? bytesPerSecond)
    {
        SpeedOverride = bytesPerSecond;
        ApplySpeed();
    }

    /// <summary>Contraseña de una copia cifrada o de un descifrado: solo en memoria (tras reiniciar hay que volver a darla).</summary>
    public string Password { get; set; } = "";

    /// <summary>Límite temporal en vigor (null = ninguno).</summary>
    public long? SpeedOverride { get; private set; }

    private void ApplySpeed()
    {
        if (_lastEngine is not { } engine)
        {
            return;
        }

        long own = Record.MaxBytesPerSecond;
        engine.Options.MaxBytesPerSecond = SpeedOverride is not { } temporary ? own
            : own > 0 ? Math.Min(own, temporary)
            : temporary;
    }

    /// <summary>Cambia el límite de velocidad del trabajo en caliente (y lo guarda).</summary>
    public void SetSpeedLimit(long bytesPerSecond)
    {
        Record.MaxBytesPerSecond = Math.Max(0, bytesPerSecond);
        ApplySpeed();
        SaveIfPending();
        Changed?.Invoke(this);
    }

    /// <summary>Cambia en caliente "desatendido" y "verificar" (y lo guarda para los reintentos).</summary>
    public void SetLiveOptions(bool? unattended = null, bool? verify = null)
    {
        if (unattended is { } u)
        {
            Record.Unattended = u;
            if (_lastEngine is { } engine)
            {
                engine.Options.Unattended = u;
            }
        }

        if (verify is { } v)
        {
            Record.Verify = v;
            if (_lastEngine is { } engine)
            {
                engine.Options.Verify = v;
            }
        }

        SaveIfPending();
        Changed?.Invoke(this);
    }

    /// <summary>Guarda el registro tras cambiarlo desde fuera (informe, scripts...).</summary>
    public void SaveRecord() => SaveIfPending();

    internal void NotifyChanged() => Changed?.Invoke(this);

    /// <summary>Solo se guarda lo que sigue pendiente: un trabajo terminado ya no está en el almacén.</summary>
    private void SaveIfPending()
    {
        if (State is not (CopyJobState.Completed or CopyJobState.Failed))
        {
            _save(Record);
        }
    }

    internal void AddLog(LogEntry entry)
    {
        lock (_log)
        {
            if (_log.Count < 20_000)
            {
                _log.Add(entry);
            }
        }

        Logged?.Invoke(this, entry);
    }

    public string Id => Record.Id;

    public CopyJobRecord Record { get; }

    public CopyJobState State { get; private set; }

    public CopyProgress? Progress { get; private set; }

    public CopyJobResult? Result { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Discos físicos que usa (claves de <see cref="Native.VolumeInfo"/>): en el mismo disco, de uno en uno.</summary>
    public IReadOnlySet<string> Devices { get; }

    public bool IsActive => State is CopyJobState.Queued or CopyJobState.Running or CopyJobState.Paused;

    /// <summary>Cambió el estado o el progreso. Se dispara desde hilos de trabajo.</summary>
    public event Action<CopyJob>? Changed;

    internal void SetState(CopyJobState state, string? error = null)
    {
        lock (_lock)
        {
            State = state;
            if (error is not null || state is CopyJobState.Queued or CopyJobState.Running)
            {
                Error = error;
            }
        }

        Changed?.Invoke(this);
    }

    internal void SetProgress(CopyProgress progress)
    {
        Progress = progress;
        Changed?.Invoke(this);
    }

    internal void SetResult(CopyJobResult result) => Result = result;

    internal void Attach(CopyEngine engine, CancellationTokenSource cancellation)
    {
        lock (_lock)
        {
            _engine = engine;
            _lastEngine = engine;
            _cancellation = cancellation;
            Files = engine.Files;
            StartedAt ??= DateTime.Now;
        }

        ApplySpeed();
    }

    internal void Detach()
    {
        lock (_lock)
        {
            _engine = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    internal bool Pause()
    {
        lock (_lock)
        {
            if (_engine is null || State != CopyJobState.Running)
            {
                return false;
            }

            _engine.Controller.Pause();
        }

        SetState(CopyJobState.Paused);
        return true;
    }

    internal bool Unpause()
    {
        lock (_lock)
        {
            if (_engine is null || State != CopyJobState.Paused)
            {
                return false;
            }

            _engine.Controller.Resume();
        }

        SetState(CopyJobState.Running);
        return true;
    }

    internal void RequestCancel()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
            _engine?.Controller.Resume(); // si estaba en pausa, que despierte para ver la cancelación
        }
    }
}

/// <summary>
/// Cola de trabajos del proceso residente. Cada trabajo se guarda en <see cref="JobStore"/> antes de empezar y se
/// borra al terminar bien, así que tras un apagón la cola sabe exactamente qué quedó a medias.
/// Los trabajos que comparten disco físico se ejecutan de uno en uno; en discos distintos, en paralelo.
/// </summary>
public sealed class CopyQueue
{
    private readonly Lock _lock = new();
    private readonly List<CopyJob> _jobs = [];
    private readonly JobStore _store;
    private readonly Func<CopyJobRecord, CopyOptions> _optionsFor;

    public CopyQueue(JobStore store, Func<CopyJobRecord, CopyOptions>? optionsFor = null)
    {
        _store = store;
        _optionsFor = optionsFor ?? (record => record.ToOptions());
    }

    private Timer? _scheduleTimer;

    /// <summary>
    /// Programa el inicio de un trabajo (null = ahora). Uno en marcha se pausa hasta esa hora; uno detenido se reanuda
    /// a esa hora.
    /// </summary>
    public void ScheduleAt(CopyJob job, DateTime? startAtUtc)
    {
        job.Record.StartAtUtc = startAtUtc;
        job.SaveRecord();
        if (startAtUtc is null)
        {
            Resume(job);
            Schedule();
            return;
        }

        if (job.State == CopyJobState.Running)
        {
            job.Pause();
        }
        else if (job.State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors)
        {
            _store.Save(job.Record);
            job.SetState(CopyJobState.Queued);
        }

        EnsureScheduleTimer();
        job.NotifyChanged();
    }

    private void EnsureScheduleTimer() =>
        _scheduleTimer ??= new Timer(_ => CheckSchedules(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    /// <summary>Arranca (o reanuda) lo que tenía hora programada y ya le toca.</summary>
    private void CheckSchedules()
    {
        var due = Jobs.Where(j => j.Record.StartAtUtc is { } at && at <= DateTime.UtcNow).ToList();
        foreach (var job in due)
        {
            job.Record.StartAtUtc = null;
            job.SaveRecord();
            if (job.State == CopyJobState.Paused)
            {
                job.Unpause();
            }

            job.NotifyChanged();
        }

        if (due.Count > 0)
        {
            Schedule();
        }
    }

    /// <summary>
    /// Responde a los conflictos de los trabajos con <see cref="ConflictPolicy.Ask"/>. Se llama desde el hilo de copia
    /// y puede bloquear hasta que el usuario decida. Sin él, los conflictos se omiten.
    /// </summary>
    public Func<CopyJob, ConflictInfo, CancellationToken, ConflictAnswer>? ConflictResolver { get; set; }

    /// <summary>Máximo de trabajos en marcha a la vez (0 = sin límite; siempre de uno en uno por disco).</summary>
    public int MaxActiveJobs { get; set; }

    /// <summary>
    /// Rutas de dispositivos "rápidos" (SSD NVMe, RAID...): admiten varios trabajos a la vez en lugar de esperar turno.
    /// </summary>
    public IReadOnlyList<string> FastDevices
    {
        get => _fastDevices;
        set
        {
            _fastDevices = value;
            _fastKeys = value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(VolumeInfo.DeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private IReadOnlyList<string> _fastDevices = [];

    /// <summary>Claves de disco de <see cref="FastDevices"/> (para prever la cola con sus mismas reglas).</summary>
    public IReadOnlySet<string> FastDeviceKeys => _fastKeys;

    private HashSet<string> _fastKeys = new(StringComparer.OrdinalIgnoreCase);

    public event Action<CopyJob>? JobAdded;

    public event Action<CopyJob>? JobRemoved;

    /// <summary>Se dispara cuando un trabajo termina (bien, con errores, fallido o interrumpido).</summary>
    public event Action<CopyJob>? JobFinished;

    public IReadOnlyList<CopyJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return [.. _jobs];
            }
        }
    }

    public bool HasActiveJobs
    {
        get
        {
            lock (_lock)
            {
                return _jobs.Any(j => j.IsActive);
            }
        }
    }

    /// <summary>Carga los trabajos que quedaron pendientes (cierre, apagón...) como interrumpidos.</summary>
    public IReadOnlyList<CopyJob> LoadInterrupted()
    {
        var loaded = new List<CopyJob>();
        foreach (var record in _store.List())
        {
            lock (_lock)
            {
                if (_jobs.Any(j => j.Id == record.Id))
                {
                    continue;
                }
            }

            if (record.StartAtUtc is not null)
            {
                // Una copia programada sigue esperando su hora tras reiniciar.
                Add(new CopyJob(record, CopyJobState.Queued, _store.Save));
                EnsureScheduleTimer();
                continue;
            }

            loaded.Add(Add(new CopyJob(record, CopyJobState.Interrupted, _store.Save)));
        }

        return loaded;
    }

    public CopyJob Submit(IEnumerable<string> sources, string destination, CopyOptions options, DateTime? startAtUtc = null)
    {
        var record = CopyJobRecord.New(sources, destination, options);
        record.StartAtUtc = startAtUtc;
        if (startAtUtc is not null)
        {
            EnsureScheduleTimer();
        }

        _store.Save(record);
        var job = Add(new CopyJob(record, CopyJobState.Queued, _store.Save));
        job.Password = options.EncryptionPassword;
        Schedule();
        return job;
    }

    /// <summary>Tras una simulación: la misma copia, con las mismas opciones, pero de verdad.</summary>
    public CopyJob RunForReal(CopyJob simulation)
    {
        var options = simulation.Record.ToOptions();
        options.DryRun = false;
        var job = Submit(simulation.Record.Sources, simulation.Record.Destination, options);
        job.Record.ExportReport = simulation.Record.ExportReport;
        job.Record.Scripts = [.. simulation.Record.Scripts];
        job.SaveRecord();
        return job;
    }

    public void Resume(CopyJob job)
    {
        if (job.State is CopyJobState.Paused)
        {
            job.Unpause();
            return;
        }

        if (job.State is CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors)
        {
            _store.Save(job.Record);
            job.SetState(CopyJobState.Queued);
            Schedule();
        }
    }

    /// <summary>
    /// Archivos soltados sobre una copia: en marcha se añaden a su lista (en la posición indicada); en cola o detenida
    /// se suman a sus orígenes; si ya terminó, se copian al mismo destino con las mismas opciones en una copia nueva.
    /// </summary>
    public CopyJob AddToJob(CopyJob job, IReadOnlyList<string> sources, int? beforeId = null)
    {
        if (job.State is CopyJobState.Running or CopyJobState.Paused && job.Files is not null)
        {
            job.AddSources(sources, beforeId);
            return job;
        }

        if (job.State is CopyJobState.Queued or CopyJobState.Interrupted or CopyJobState.Failed or CopyJobState.CompletedWithErrors)
        {
            job.Record.Sources.AddRange(sources.Where(s => !job.Record.Sources.Contains(s, StringComparer.OrdinalIgnoreCase)));
            _store.Save(job.Record);
            if (job.State != CopyJobState.Queued)
            {
                Resume(job);
            }

            job.NotifyChanged();
            return job;
        }

        return Submit(sources, job.Record.Destination, _optionsFor(job.Record));
    }

    /// <summary>Cambia el orden de la cola: <paramref name="job"/> pasa delante de <paramref name="before"/> (null = al final).</summary>
    public void MoveJob(CopyJob job, CopyJob? before)
    {
        lock (_lock)
        {
            if (!_jobs.Remove(job))
            {
                return;
            }

            int index = before is null ? -1 : _jobs.IndexOf(before);
            _jobs.Insert(index < 0 ? _jobs.Count : index, job);
        }

        JobsReordered?.Invoke();
        Schedule();
    }

    /// <summary>Cambió el orden de la cola.</summary>
    public event Action? JobsReordered;

    public void Pause(CopyJob job) => job.Pause();

    /// <summary>Detiene el trabajo conservando lo copiado: se puede reanudar más tarde.</summary>
    public void Stop(CopyJob job)
    {
        if (job.State == CopyJobState.Queued)
        {
            job.SetState(CopyJobState.Interrupted);
            return;
        }

        job.RequestCancel();
    }

    /// <summary>Olvida el trabajo y borra los archivos a medias (.mcpart / .mcjournal) que dejó.</summary>
    public void Discard(CopyJob job)
    {
        if (job.IsActive)
        {
            throw new InvalidOperationException(Texts.Get("E_StopBeforeDiscard"));
        }

        _store.Delete(job.Id);
        CopyEngine.DeletePartialFiles(new CopyJobRequest(job.Record.Sources, job.Record.Destination));
        Remove(job);
    }

    /// <summary>Quita de la lista un trabajo terminado.</summary>
    public void Clear(CopyJob job)
    {
        if (job.State is CopyJobState.Completed)
        {
            Remove(job);
        }
    }

    public void PauseAll()
    {
        foreach (var job in Jobs)
        {
            job.Pause();
        }
    }

    public void ResumeAll()
    {
        foreach (var job in Jobs.Where(j => j.State == CopyJobState.Paused))
        {
            job.Unpause();
        }
    }

    /// <summary>Detiene todo (para salir de la aplicación) y espera a que los trabajos suelten los archivos.</summary>
    public void StopAll(TimeSpan timeout)
    {
        foreach (var job in Jobs.Where(j => j.IsActive))
        {
            Stop(job);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (Jobs.Any(j => j.State is CopyJobState.Running or CopyJobState.Paused) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
    }

    private CopyJob Add(CopyJob job)
    {
        lock (_lock)
        {
            _jobs.Add(job);
        }

        JobAdded?.Invoke(job);
        return job;
    }

    private void Remove(CopyJob job)
    {
        bool removed;
        lock (_lock)
        {
            removed = _jobs.Remove(job);
        }

        if (removed)
        {
            JobRemoved?.Invoke(job);
        }
    }

    private void Schedule()
    {
        var toStart = new List<CopyJob>();
        lock (_lock)
        {
            var busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int active = 0;
            foreach (var running in _jobs.Where(j => j.State is CopyJobState.Running or CopyJobState.Paused))
            {
                busy.UnionWith(running.Devices);
                active++;
            }

            // Los dispositivos marcados como rápidos no obligan a esperar turno.
            busy.ExceptWith(_fastKeys);

            foreach (var job in _jobs.Where(j => j.State == CopyJobState.Queued))
            {
                if (job.Record.StartAtUtc is { } at && at > DateTime.UtcNow)
                {
                    continue; // programado para más tarde
                }

                if (MaxActiveJobs > 0 && active >= MaxActiveJobs)
                {
                    break;
                }

                if (job.Devices.Overlaps(busy))
                {
                    continue;
                }

                active++;
                busy.UnionWith(job.Devices.Except(_fastKeys, StringComparer.OrdinalIgnoreCase));
                job.SetState(CopyJobState.Running);
                toStart.Add(job);
            }
        }

        foreach (var job in toStart)
        {
            Start(job);
        }
    }

    private void Start(CopyJob job)
    {
        var options = _optionsFor(job.Record);
        options.EncryptionPassword = job.Password; // solo en memoria: no está en el trabajo guardado
        var engine = new CopyEngine(options);
        var cancellation = new CancellationTokenSource();
        job.Attach(engine, cancellation);
        engine.ProgressChanged += job.SetProgress;
        engine.ConflictResolver = (info, token) =>
            ConflictResolver?.Invoke(job, info, token) ?? new ConflictAnswer(ConflictDecision.Skip, ApplyToAll: false);
        engine.Logged += job.AddLog;

        bool resumed = job.Record.Started;
        if (!resumed)
        {
            job.Record.Started = true;
            _store.Save(job.Record);
        }

        var request = new CopyJobRequest(job.Record.Sources, job.Record.Destination) { Excluded = job.Record.Excluded, Order = job.Record.FileOrder, Resumed = resumed };
        _ = engine.RunAsync(request, cancellation.Token)
            .ContinueWith(
                task =>
                {
                    job.Detach();
                    job.FinishedAt = DateTime.Now;
                    if (task.IsCanceled || task.Exception?.InnerException is OperationCanceledException)
                    {
                        job.SetState(CopyJobState.Interrupted);
                    }
                    else if (task.Exception?.InnerException is { } error)
                    {
                        // Un error de plan no se arregla reintentando; el resto queda reanudable.
                        if (error is CopyPlanException)
                        {
                            _store.Delete(job.Id);
                        }

                        job.SetState(CopyJobState.Failed, error.Message);
                    }
                    else
                    {
                        var result = task.Result;
                        job.SetResult(result);
                        if (result.Success)
                        {
                            _store.Delete(job.Id);
                            job.SetState(CopyJobState.Completed);
                        }
                        else
                        {
                            job.SetState(CopyJobState.CompletedWithErrors, Texts.F("E_JobErrors", result.Failures.Count));
                        }
                    }

                    JobFinished?.Invoke(job);
                    Schedule();
                },
                TaskScheduler.Default);
    }
}
