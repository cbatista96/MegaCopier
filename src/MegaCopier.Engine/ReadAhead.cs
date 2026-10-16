using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Lectura anticipada de archivos pequeños para calcular y comprobar sumas: mientras se calcula el hash de uno, los
/// siguientes de la cola ya se están leyendo en lote con IoRing (Windows 11). Si el usuario reordena la cola, lo leído de
/// más no se usa; si algo falla, ese archivo se lee como siempre (y el error, si lo hay, sale de ahí).
/// </summary>
internal sealed class ReadAhead : IDisposable
{
    public const int MaxFile = 256 * 1024;
    private const int Batch = 64;

    private readonly TransferList _files;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, byte[]> _ready = [];
    private readonly HashSet<int> _inFlight = [];
    private Task? _task;
    private bool _broken;

    private ReadAhead(TransferList files) => _files = files;

    public static ReadAhead? TryCreate(TransferList files, CopyOptions options) => options.BatchedReads && IoRing.IsAvailable ? new(files) : null;

    /// <summary>El contenido de <paramref name="item"/> (ya tomado de la cola), o null si hay que leerlo de la forma normal.</summary>
    public byte[]? Take(TransferItem item, CancellationToken cancellationToken)
    {
        if (item.Length > MaxFile)
        {
            return null;
        }

        bool waited = false;
        while (true)
        {
            Task? wait;
            lock (_gate)
            {
                if (_ready.Remove(item.Id, out byte[]? data))
                {
                    StartNext();
                    return data;
                }

                if (_broken || (waited && _task is null))
                {
                    return null; // se intentó y no se pudo leer en lote
                }

                if (_task is null)
                {
                    Start([item, .. _files.PeekPending(Batch - 1, Wanted)]);
                }

                waited = waited || _inFlight.Contains(item.Id);
                wait = _task;
            }

            wait?.Wait(cancellationToken);
        }
    }

    public void Dispose()
    {
        try
        {
            _task?.Wait();
        }
        catch (AggregateException)
        {
        }
    }

    private bool Wanted(TransferItem item) => item.Length <= MaxFile && !_ready.ContainsKey(item.Id);

    /// <summary>Con la reserva a medias, se pide ya el siguiente lote (se lee mientras se calculan los hashes).</summary>
    private void StartNext()
    {
        if (_task is null && !_broken && _ready.Count < Batch / 2 && _files.PeekPending(Batch, Wanted) is { Count: > 0 } next)
        {
            Start(next);
        }
    }

    private void Start(List<TransferItem> items)
    {
        // Lo que se quedó sin usar (cola reordenada, archivos quitados) no se acumula.
        if (_ready.Count > Batch * 4)
        {
            _ready.Clear();
        }

        _inFlight.Clear();
        _inFlight.UnionWith(items.Select(i => i.Id));
        _task = Task.Run(() =>
        {
            try
            {
                IoRing.ReadAll([.. items.Select(i => i.Source)], MaxFile, unbuffered: false, (index, data, error) =>
                {
                    if (error is null && data.Length == items[index].Length)
                    {
                        lock (_gate)
                        {
                            _ready[items[index].Id] = data.ToArray();
                        }
                    }
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
            {
                lock (_gate)
                {
                    _broken = true;
                }
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Clear();
                    _task = null;
                }
            }
        });
    }
}
