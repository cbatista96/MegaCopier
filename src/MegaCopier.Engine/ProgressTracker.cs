using System.Diagnostics;

namespace MegaCopier.Engine;

/// <summary>Acumula el avance del trabajo y lo publica como mucho cada <see cref="CopyOptions.ProgressInterval"/>.</summary>
internal sealed class ProgressTracker
{
    private static readonly long SpeedWindow = Stopwatch.Frequency * 3;

    private readonly Action<CopyProgress>? _sink;
    private readonly long _interval;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Queue<(long Timestamp, long Bytes)> _samples = new();

    private long _lastEmit;
    private long _windowBytes;
    private long _totalBytes;
    private int _totalFiles;
    private long _completedFilesBytes;
    private int _filesCompleted;
    private string _currentFile = "";
    private long _currentLength;
    private long _currentDone;
    private CopyPhase _phase = CopyPhase.Preparing;
    private readonly Lock _gate = new();

    public ProgressTracker(Action<CopyProgress>? sink, TimeSpan interval)
    {
        _sink = sink;
        _interval = (long)(interval.TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>Para un hilo de archivos pequeños: no publica nada, pero las esperas (disco, espacio, tu decisión) sí se ven.</summary>
    public ProgressTracker(ProgressTracker parent)
        : this(null, TimeSpan.Zero)
    {
        _parent = parent;
    }

    /// <summary>
    /// Para una conexión más de una copia con un servidor: cada byte se pasa en el acto al principal (así la barra
    /// avanza aunque haya varios archivos grandes a la vez) y el archivo cuenta como terminado al acabar.
    /// </summary>
    public ProgressTracker(ProgressTracker parent, bool forwardBytes)
        : this(parent)
    {
        _forward = forwardBytes;
    }

    private readonly ProgressTracker? _parent;
    private readonly bool _forward;
    private bool _parentWaiting;
    private long _parallelBytes;

    private void AddParallelBytes(long bytes, string? file)
    {
        lock (_gate)
        {
            _parallelBytes += bytes;
            if (file is not null)
            {
                _currentFile = file;
            }

            if (bytes > 0)
            {
                AddSample(bytes);
            }

            Emit(force: file is not null);
        }
    }

    private void FinishParallelFile(long sent, long length)
    {
        lock (_gate)
        {
            _parallelBytes -= sent;
            _completedFilesBytes += length;
            _filesCompleted++;
            Emit(force: false);
        }
    }

    private readonly List<float> _speedHistory = [];
    private long _lastHistorySample = Stopwatch.GetTimestamp();

    /// <summary>Velocidad en bytes/s, una muestra por segundo (para la gráfica de la pestaña Estado).</summary>
    public float[] SpeedHistory()
    {
        lock (_speedHistory)
        {
            return [.. _speedHistory];
        }
    }

    /// <summary>La lista cambió (se añadieron o quitaron archivos).</summary>
    public void SetTotals(long totalBytes, int totalFiles)
    {
        lock (_gate)
        {
            _totalBytes = totalBytes;
            _totalFiles = totalFiles;
            Emit(force: true);
        }
    }

    public void Start(long totalBytes, int totalFiles)
    {
        lock (_gate)
        {
            _totalBytes = totalBytes;
            _totalFiles = totalFiles;
            _phase = CopyPhase.Copying;
            Emit(force: true);
        }
    }

    public void BeginFile(string path, long length)
    {
        lock (_gate)
        {
            _currentFile = path;
            _currentLength = length;
            _currentDone = 0;
            _phase = CopyPhase.Copying;
            Emit(force: true);
        }

        if (_forward)
        {
            _parent!.AddParallelBytes(0, path);
        }
    }

    /// <summary>Reinicia el avance del archivo actual (antes de un reintento).</summary>
    public void RestartFile()
    {
        long undone;
        lock (_gate)
        {
            undone = _currentDone;
            _currentDone = 0;
        }

        if (_forward)
        {
            _parent!.AddParallelBytes(-undone, null);
        }
    }

    /// <summary>Un archivo pequeño que copió otro hilo (en paralelo con el actual): cuenta como terminado.</summary>
    public void AddFinishedFile(long length)
    {
        lock (_gate)
        {
            _completedFilesBytes += length;
            _filesCompleted++;
            AddSample(length);
            Emit(force: false);
        }
    }

    public void SetFileResumed(long bytes)
    {
        long delta;
        lock (_gate)
        {
            delta = bytes - _currentDone;
            _currentDone = bytes;
            Emit(force: true);
        }

        if (_forward)
        {
            _parent!.AddParallelBytes(delta, null);
        }
    }

    public void AddTransferred(long bytes)
    {
        lock (_gate)
        {
            _currentDone += bytes;
            AddSample(bytes);
            Emit(force: false);
        }

        if (_forward)
        {
            _parent!.AddParallelBytes(bytes, null);
        }
    }

    public void SetPhase(CopyPhase phase)
    {
        if (_parent is not null)
        {
            bool waiting = phase is CopyPhase.WaitingForDevice or CopyPhase.WaitingForSpace or CopyPhase.WaitingForUser;
            if (waiting || _parentWaiting)
            {
                _parentWaiting = waiting;
                _parent.SetPhase(waiting ? phase : CopyPhase.Copying);
            }

            return;
        }

        lock (_gate)
        {
            _phase = phase;
            Emit(force: true);
        }
    }

    public void EndFile()
    {
        long sent, length;
        lock (_gate)
        {
            sent = _currentDone;
            length = _currentLength;
            _completedFilesBytes += _currentLength;
            _filesCompleted++;
            _currentDone = 0;
            _currentLength = 0;
            Emit(force: true);
        }

        if (_forward)
        {
            _parent!.FinishParallelFile(sent, length);
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            _phase = CopyPhase.Completed;
            Emit(force: true);
        }
    }

    private void AddSample(long bytes)
    {
        long now = Stopwatch.GetTimestamp();
        _samples.Enqueue((now, bytes));
        _windowBytes += bytes;
        while (_samples.Count > 1 && now - _samples.Peek().Timestamp > SpeedWindow)
        {
            _windowBytes -= _samples.Dequeue().Bytes;
        }
    }

    private double CurrentSpeed()
    {
        if (_samples.Count == 0)
        {
            return 0;
        }

        long span = Stopwatch.GetTimestamp() - _samples.Peek().Timestamp;
        if (span < Stopwatch.Frequency / 5)
        {
            return 0;
        }

        return _windowBytes / ((double)span / Stopwatch.Frequency);
    }

    private void Emit(bool force)
    {
        if (_sink is null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (now - _lastHistorySample >= Stopwatch.Frequency)
        {
            _lastHistorySample = now;
            lock (_speedHistory)
            {
                if (_speedHistory.Count < 24 * 3600)
                {
                    _speedHistory.Add((float)CurrentSpeed());
                }
            }
        }

        if (!force && now - _lastEmit < _interval)
        {
            return;
        }

        _lastEmit = now;
        _sink(new CopyProgress(
            _phase,
            _currentFile,
            _currentDone,
            _currentLength,
            _completedFilesBytes + _currentDone + _parallelBytes,
            _totalBytes,
            _filesCompleted,
            _totalFiles,
            CurrentSpeed(),
            Stopwatch.GetElapsedTime(_started)));
    }
}
