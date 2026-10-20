using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

internal sealed partial class FileTransfer
{
    /// <summary>
    /// Hash del archivo entero al reanudar sin hacer esperar a la copia. Antes se releía del origen todo lo ya copiado
    /// antes de escribir nada más (con 3 GB en una memoria USB parecía que volvía a empezar). Ahora una tarea lee esa
    /// parte mientras se copia el resto y, cuando alcanza a la copia, sigue con los bloques que esta le pasa: cada byte
    /// del origen se lee una sola vez, como antes, pero la escritura empieza al instante.
    /// </summary>
    private sealed class CatchUpHasher : IAsyncDisposable
    {
        private readonly FileTransfer _owner;
        private readonly IoContext _io;
        private readonly FileHasher _hasher;
        private readonly long _length;
        private readonly Lock _lock = new();
        private readonly CancellationTokenSource _stop;
        private readonly Task _reading;
        private long _hashed;   // bytes del principio del archivo que ya están en el hash
        private long _copied;   // hasta dónde ha llegado la copia

        public CatchUpHasher(FileTransfer owner, IoContext io, FileHasher hasher, long copied, long length, CancellationToken cancellationToken)
        {
            _owner = owner;
            _io = io;
            _hasher = hasher;
            _copied = copied;
            _length = length;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _reading = Task.Run(() => CatchUpAsync(_stop.Token), CancellationToken.None);
        }

        /// <summary>Lo llama la copia con cada bloque, en orden: si es justo lo siguiente del hash, entra sin releerlo.</summary>
        public void Offer(ReadOnlySpan<byte> data, long offset)
        {
            lock (_lock)
            {
                _copied = offset + data.Length;
                if (offset == _hashed)
                {
                    _hasher.Append(data);
                    _hashed += data.Length;
                }
            }
        }

        /// <summary>Lee del origen lo que la copia ya dejó atrás hasta alcanzarla.</summary>
        private async Task CatchUpAsync(CancellationToken cancellationToken)
        {
            // Búfer propio: los del grupo son de la copia, que no debe quedarse sin ellos.
            using var buffer = new AlignedBuffer(_owner._pool.BufferSize);
            while (true)
            {
                long position;
                lock (_lock)
                {
                    if (_hashed >= _copied)
                    {
                        return;
                    }

                    position = _hashed;
                }

                await _owner._controller.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                int want = (int)Math.Min(buffer.Capacity, _length - position);
                int read = await IoHelpers.ReadFullAsync(_io.Source, _io.SourceUnbuffered, buffer, want, position, cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    // Si la copia lo metió mientras se leía, este trozo sobra.
                    if (_hashed == position)
                    {
                        _hasher.Append(buffer.Span[..read]);
                        _hashed += read;
                    }
                }

                if (read < want)
                {
                    return; // origen más corto: la copia ya lo detecta y falla
                }
            }
        }

        /// <summary>Al terminar la copia: espera a la tarea y lee lo que falte (si la copia no le pasó sus bloques).</summary>
        public async Task<string?> FinishAsync(CancellationToken cancellationToken)
        {
            await _reading.ConfigureAwait(false);
            if (_hashed < _length)
            {
                await _owner.ReadSequentialAsync(_io.Source, _io.SourceUnbuffered, _hashed, _length - _hashed, (data, _) => _hasher.Append(data), cancellationToken).ConfigureAwait(false);
            }

            return _hasher.Finish();
        }

        /// <summary>Si la copia falla o se cancela, la tarea se para antes de cerrar el origen.</summary>
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await _reading.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                // El error que cuenta es el de la copia.
            }

            _stop.Dispose();
        }
    }
}
