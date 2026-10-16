using System.Runtime.ExceptionServices;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Hilos que copian archivos pequeños a la vez que el principal (como robocopy /MT): con miles de archivos pequeños,
    /// cada uno cuesta sobre todo esperas (abrir, crear, fechas, renombrar) y varios a la vez las solapan.
    /// Solo toman el siguiente de la lista si es pequeño (un solo bloque): el orden de inicio se respeta. Con un límite
    /// de velocidad o en pausa, no toman nada. Cada uno lleva su propio resultado, que se suma al terminar.
    /// </summary>
    /// <summary>
    /// Este archivo podría necesitar una pregunta (ya existe y la regla es "preguntar"): se deja al hilo principal para
    /// que las preguntas lleguen en el orden de la lista.
    /// </summary>
    private bool MightAsk(TransferItem item) =>
        _options.Conflict == ConflictPolicy.Ask && !_options.Unattended && Volatile.Read(ref _conflictForAll) is null && File.Exists(item.Destination);

    private sealed class SmallFileHelpers : IDisposable
    {
        private const int BuffersPerHelper = 4;

        private readonly CopyEngine _engine;
        private readonly ProgressTracker _progress;
        private readonly CopyJobResult _result;
        private readonly CancellationToken _token;
        private readonly List<Thread> _threads = [];
        private readonly List<CopyJobResult> _results = [];
        private volatile bool _stop;
        private int _busy;
        private Exception? _failure;
        private bool _merged;

        public SmallFileHelpers(CopyEngine engine, ProgressTracker progress, CopyJobResult result, CancellationToken token)
        {
            _engine = engine;
            _progress = progress;
            _result = result;
            _token = token;
            // Enlaces duros: el segundo nombre de un archivo debe ver que el primero ya se copió, así que de uno en uno.
            int count = engine._options.DryRun || engine._options.PreserveHardLinks || engine._options.SkipDuplicates ? 0 : Math.Clamp(engine._options.ParallelSmallFiles, 1, 16) - 1;
            for (int i = 0; i < count; i++)
            {
                var thread = new Thread(Work) { IsBackground = true, Name = $"MegaCopier: archivos pequeños {i + 1}" };
                _threads.Add(thread);
                thread.Start();
            }
        }

        private CopyOptions Options => _engine._options;

        private void Work()
        {
            t_helper = true;
            var result = new CopyJobResult();
            lock (_results)
            {
                _results.Add(result);
            }

            try
            {
                var tracker = new ProgressTracker(_progress);
                using var pool = new BufferPool(Options.BlockSize, BuffersPerHelper);
                var transfer = new FileTransfer(Options, pool, _engine.Controller, tracker, _engine.FaultInjector,
                    (path, bytes) => _engine.ChunkCommitted?.Invoke(path, bytes));
                while (!_stop && !_token.IsCancellationRequested)
                {
                    if (Options.MaxBytesPerSecond > 0 || _engine.Controller.IsPaused)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    // Se cuenta como ocupado antes de tomar el archivo: el hilo principal no da el trabajo por
                    // terminado mientras alguno pueda estar con uno.
                    Interlocked.Increment(ref _busy);
                    TransferItem? item = null;
                    try
                    {
                        item = _engine.Files.TakeNextIf(i => i.Length <= Options.BlockSize && !_engine.MightAsk(i));
                        if (item is not null)
                        {
                            _engine.ProcessFile(transfer, tracker, item, result, _token);
                            _progress.AddFinishedFile(item.Length);
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _busy);
                    }

                    if (item is null)
                    {
                        Thread.Sleep(3);
                    }
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _failure, ex, null);
                _stop = true;
            }
        }

        public void ThrowIfFailed()
        {
            if (Volatile.Read(ref _failure) is { } failure)
            {
                ExceptionDispatchInfo.Throw(failure);
            }
        }

        /// <summary>Espera a que ningún hilo esté copiando.</summary>
        public void WaitIdle()
        {
            while (Volatile.Read(ref _busy) > 0 && Volatile.Read(ref _failure) is null)
            {
                Thread.Sleep(2);
            }

            ThrowIfFailed();
        }

        /// <summary>Termina los hilos y suma lo que hicieron al resultado del trabajo.</summary>
        public void Stop()
        {
            Join();
            ThrowIfFailed();
        }

        public void Dispose() => Join();

        private void Join()
        {
            _stop = true;
            foreach (var thread in _threads)
            {
                thread.Join();
            }

            if (!_merged)
            {
                _merged = true;
                foreach (var result in _results)
                {
                    _result.Add(result);
                }
            }
        }
    }
}
