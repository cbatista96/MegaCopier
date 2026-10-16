using System.Diagnostics;

namespace MegaCopier.Engine;

/// <summary>
/// Limita la velocidad media de escritura de todo un trabajo. El límite se lee en cada bloque, así que se puede cambiar
/// en caliente (por ejemplo, "más lento mientras usas el equipo").
/// </summary>
internal sealed class Throttle(Func<long> bytesPerSecond)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _consumed;
    private long _lastLimit;

    public async Task WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        long limit = bytesPerSecond();
        if (limit <= 0)
        {
            _lastLimit = 0;
            return;
        }

        double elapsed = _clock.Elapsed.TotalSeconds;

        // Tras una pausa larga, o si el límite cambió, no se "recupera" el tiempo con una ráfaga: se empieza a contar de nuevo.
        if (limit != _lastLimit || elapsed - (_consumed / (double)limit) > 1)
        {
            _clock.Restart();
            _consumed = 0;
            elapsed = 0;
            _lastLimit = limit;
        }

        _consumed += bytes;
        double ahead = (_consumed / (double)limit) - elapsed;
        if (ahead > 0.002)
        {
            await Task.Delay(TimeSpan.FromSeconds(ahead), cancellationToken).ConfigureAwait(false);
        }
    }
}
