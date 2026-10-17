using System.Runtime.ExceptionServices;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Conexiones a la vez con un servidor (FTP, SFTP, WebDAV, S3): con muchos archivos, cada uno cuesta sobre todo
    /// esperas de la red, y varias conexiones las solapan. Un móvil (MTP) va siempre de uno en uno.
    /// </summary>
    private int RemoteConnections(string storeId) =>
        storeId.StartsWith("conn-", StringComparison.Ordinal) && !_options.DryRun ? Math.Clamp(_options.RemoteConnections, 1, CopyOptions.MaxRemoteConnections) : 1;

    /// <summary>
    /// Reparte los archivos de la lista entre <see cref="RemoteConnections"/> conexiones: la primera es
    /// <paramref name="main"/> y las demás se abren aquí (si una no se puede abrir, por ejemplo porque el servidor no
    /// admite tantas, se sigue con las que haya). Cada una lleva su resultado, que se suma al terminar.
    /// </summary>
    private void ForEachRemoteFile(string storeId, IRemoteStore main, ProgressTracker progress, CopyJobResult result, CancellationToken cancellationToken,
        Action<IRemoteStore, TransferItem, CopyJobResult, ProgressTracker, CancellationToken> work)
    {
        void Loop(IRemoteStore store, CopyJobResult own, ProgressTracker tracker, CancellationToken token)
        {
            while (Files.TakeNext() is { } item)
            {
                token.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(token);
                work(store, item, own, tracker, token);
            }
        }

        int count = RemoteConnections(storeId);
        if (count == 1)
        {
            Loop(main, result, progress, cancellationToken);
            return;
        }

        // Con ajuste automático, el número configurado es el máximo: se empieza con 2 y se suben o bajan según la
        // velocidad total (las conexiones que sobran ni se abren). Sin él, se usan todas desde el principio.
        var tuner = _options.RemoteConnectionsAuto ? new ConnectionTuner(count) : null;
        int limit = tuner?.Limit ?? count;
        Log(LogLevel.Info, Texts.F(tuner is null ? "E_RemoteConnections" : "E_RemoteConnectionsAuto", count));
        var results = new CopyJobResult?[count];
        Exception? failure = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var threads = new List<Thread>();

        // Una conexión por encima del límite actual espera (sin abrirse, o sin tomar archivos) hasta que se le dé paso.
        bool WaitTurn(int index)
        {
            while (index >= Volatile.Read(ref limit))
            {
                if (stop.IsCancellationRequested || !Files.HasPending())
                {
                    return false;
                }

                Thread.Sleep(200);
            }

            return true;
        }

        for (int i = 0; i < count; i++)
        {
            int index = i;
            var thread = new Thread(() =>
            {
                IRemoteStore? opened = null;
                try
                {
                    if (!WaitTurn(index))
                    {
                        return;
                    }

                    IRemoteStore store;
                    if (index == 0)
                    {
                        store = main;
                    }
                    else
                    {
                        try
                        {
                            store = opened = RemoteOpener(storeId);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
                        {
                            Log(LogLevel.Warning, Texts.F("E_RemoteConnectionFailed", index + 1, ex.Message));
                            return;
                        }
                    }

                    var own = results[index] = new CopyJobResult();
                    var tracker = new ProgressTracker(progress, forwardBytes: true);
                    while (WaitTurn(index) && Files.TakeNext() is { } item)
                    {
                        stop.Token.ThrowIfCancellationRequested();
                        Controller.WaitIfPaused(stop.Token);
                        work(store, item, own, tracker, stop.Token);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                    stop.Cancel();
                }
                finally
                {
                    opened?.Dispose();
                }
            })
            { IsBackground = true, Name = $"MegaCopier: conexión {index + 1}" };
            threads.Add(thread);
            thread.Start();
        }

        // Mientras trabajan: cada 2 segundos se mira la velocidad total y se decide cuántas conexiones usar.
        long lastBytes = progress.CompletedBytes;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (threads.Any(t => t.IsAlive))
        {
            threads.First(t => t.IsAlive).Join(TimeSpan.FromSeconds(2));
            if (tuner is null || Controller.IsPaused)
            {
                lastBytes = progress.CompletedBytes;
                clock.Restart();
                continue;
            }

            long bytes = progress.CompletedBytes;
            double rate = (bytes - lastBytes) / Math.Max(0.001, clock.Elapsed.TotalSeconds);
            lastBytes = bytes;
            clock.Restart();
            int next = tuner.Next(rate);
            if (next != Volatile.Read(ref limit))
            {
                Volatile.Write(ref limit, next);
                Log(LogLevel.Info, Texts.F("E_RemoteConnectionsTuned", next, FormatBytes((long)rate)));
            }
        }

        foreach (var own in results)
        {
            if (own is not null)
            {
                result.Add(own);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
