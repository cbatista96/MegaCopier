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

    public ProgressTracker(Action<CopyProgress>? sink, TimeSpan interval)
    {
        _sink = sink;
        _interval = (long)(interval.TotalSeconds * Stopwatch.Frequency);
    }

    public void Start(long totalBytes, int totalFiles)
    {
        _totalBytes = totalBytes;
        _totalFiles = totalFiles;
        _phase = CopyPhase.Copying;
        Emit(force: true);
    }

    public void BeginFile(string path, long length)
    {
        _currentFile = path;
        _currentLength = length;
        _currentDone = 0;
        _phase = CopyPhase.Copying;
        Emit(force: true);
    }

    /// <summary>Reinicia el avance del archivo actual (antes de un reintento).</summary>
    public void RestartFile() => _currentDone = 0;

    public void SetFileResumed(long bytes)
    {
        _currentDone = bytes;
        Emit(force: true);
    }

    public void AddTransferred(long bytes)
    {
        _currentDone += bytes;
        long now = Stopwatch.GetTimestamp();
        _samples.Enqueue((now, bytes));
        _windowBytes += bytes;
        while (_samples.Count > 1 && now - _samples.Peek().Timestamp > SpeedWindow)
        {
            _windowBytes -= _samples.Dequeue().Bytes;
        }

        Emit(force: false);
    }

    public void SetPhase(CopyPhase phase)
    {
        _phase = phase;
        Emit(force: true);
    }

    public void EndFile()
    {
        _completedFilesBytes += _currentLength;
        _filesCompleted++;
        _currentDone = 0;
        _currentLength = 0;
        Emit(force: true);
    }

    public void Finish()
    {
        _phase = CopyPhase.Completed;
        Emit(force: true);
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
            _completedFilesBytes + _currentDone,
            _totalBytes,
            _filesCompleted,
            _totalFiles,
            CurrentSpeed(),
            Stopwatch.GetElapsedTime(_started)));
    }
}
