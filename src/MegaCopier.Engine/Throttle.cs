using System.Diagnostics;

namespace MegaCopier.Engine;

/// <summary>Limita la velocidad media de escritura de todo un trabajo.</summary>
internal sealed class Throttle(long bytesPerSecond)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _consumed;

    public async Task WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        double elapsed = _clock.Elapsed.TotalSeconds;

        // Tras una pausa larga no se "recupera" el tiempo perdido con una ráfaga.
        if (elapsed - (_consumed / (double)bytesPerSecond) > 1)
        {
            _clock.Restart();
            _consumed = 0;
            elapsed = 0;
        }

        _consumed += bytes;
        double ahead = (_consumed / (double)bytesPerSecond) - elapsed;
        if (ahead > 0.002)
        {
            await Task.Delay(TimeSpan.FromSeconds(ahead), cancellationToken).ConfigureAwait(false);
        }
    }
}
