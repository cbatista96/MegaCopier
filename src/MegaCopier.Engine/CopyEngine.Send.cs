using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>"equipo:puerto" (o solo "equipo", con el puerto por defecto).</summary>
    public static (string Host, int Port) ParsePeer(string destination)
    {
        string text = destination.Trim();
        if (text.StartsWith("mc://", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..];
        }

        int colon = text.LastIndexOf(':');
        return colon > 0 && !text.EndsWith(']') && int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            ? (text[..colon].Trim('[', ']'), port)
            : (text.Trim('[', ']'), PeerProtocol.DefaultPort);
    }

    /// <summary>
    /// Envía los orígenes al MegaCopier de otro equipo. Cada archivo se comprueba al llegar; lo que ya estaba allí
    /// (mismo tamaño y fecha) no se envía, y si la red se corta, al reanudar se sigue desde lo que llegó.
    /// </summary>
    private CopyJobResult RunSend(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        string dummy = Path.Combine(Path.GetTempPath(), "MegaCopier-send-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, dummy, _options.Filter, LinkHandling.FollowTarget);
        var relatives = new Dictionary<int, string>();
        foreach (var file in plan.Files)
        {
            var item = Files.Add(file.Source, file.Source, file.Length, file.LastWriteUtc);
            relatives[item.Id] = Path.GetRelativePath(dummy, file.Destination);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        var (host, port) = ParsePeer(request.Destination);
        Log(LogLevel.Info, $"Enviar {totalFiles} archivos ({FormatBytes(totalBytes)}) a {host}:{port}.");
        var throttle = new Throttle(() => _options.MaxBytesPerSecond);

        try
        {
            SendAllAsync().GetAwaiter().GetResult();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new CopyPlanException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is SocketException or AuthenticationException)
        {
            throw new IOException(Texts.F("E_ConnectFailed", host, port, ex.Message), ex);
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(LogLevel.Info, $"Enviados {result.FilesCopied} ({FormatBytes(result.BytesTransferred)}), {result.FilesSkipped} ya estaban.");
        return result;

        async Task SendAllAsync()
        {
            await using var sender = await PeerSender.ConnectAsync(host, port, _options.PeerCode, cancellationToken).ConfigureAwait(false);
            await sender.HelloAsync(Environment.MachineName, totalFiles, totalBytes, cancellationToken).ConfigureAwait(false);
            while (Files.TakeNext() is { } item)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                progress.BeginFile(item.Source, item.Length);
                var outcome = PeerFileResult.Corrupted;
                for (int attempt = 0; attempt < 2 && outcome == PeerFileResult.Corrupted; attempt++)
                {
                    outcome = await sender.SendFileAsync(item.Source, relatives[item.Id], async (count, token) =>
                    {
                        await Controller.WaitIfPausedAsync(token).ConfigureAwait(false);
                        await throttle.WaitAsync(count, token).ConfigureAwait(false);
                        progress.AddTransferred(count);
                    }, cancellationToken).ConfigureAwait(false);
                }

                switch (outcome)
                {
                    case PeerFileResult.Sent:
                        result.FilesCopied++;
                        result.BytesTransferred += item.Length;
                        Files.Update(item, FileState.Verified);
                        break;
                    case PeerFileResult.AlreadyThere:
                        result.FilesSkipped++;
                        item.Action = PlannedAction.SkipIdentical;
                        Files.Update(item, FileState.Skipped, Texts.Get("E_AlreadyOnPeer"));
                        break;
                    default:
                        Fail(item, result, Texts.Get("E_DamagedTwice"), FileErrorKind.HashMismatch);
                        break;
                }

                progress.EndFile();
            }

            await sender.EndAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
