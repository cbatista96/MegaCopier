using System.Diagnostics;
using System.Net.Sockets;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Enviar a LocalSend (un móvil u otro ordenador): el otro lado lo acepta en su pantalla. El protocolo no deja
    /// reanudar a medias ni comprobar lo que llegó, así que un corte obliga a enviar otra vez lo que faltaba.
    /// </summary>
    private CopyJobResult RunLocalSend(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        string dummy = Path.Combine(Path.GetTempPath(), "MegaCopier-send-" + Guid.NewGuid().ToString("N"));
        var plan = CopyPlanner.Build(request.Sources, dummy, _options.Filter, LinkHandling.FollowTarget);
        var items = new List<TransferItem>();
        var files = new List<(string Path, string Name)>();
        foreach (var file in plan.Files)
        {
            items.Add(Files.Add(file.Source, request.Destination, file.Length, file.LastWriteUtc));
            files.Add((file.Source, Path.GetRelativePath(dummy, file.Destination)));
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;
        var (host, port, _, _) = LocalSendSender.Parse(request.Destination);
        Log(LogLevel.Info, Texts.F("E_LocalSendStart", totalFiles, FormatBytes(totalBytes), host));
        var throttle = new Throttle(() => _options.MaxBytesPerSecond);
        int current = -1;
        bool accepted;
        try
        {
            accepted = LocalSendSender.SendAsync(request.Destination, Environment.MachineName, files, sent =>
            {
                if (current < 0 && items.Count > 0)
                {
                    current = 0;
                    progress.BeginFile(items[0].Source, items[0].Length);
                }

                Controller.WaitIfPaused(cancellationToken);
                if (_options.MaxBytesPerSecond > 0)
                {
                    throttle.WaitAsync(sent, cancellationToken).GetAwaiter().GetResult();
                }

                progress.AddTransferred(sent);
            }, index =>
            {
                var item = items[index];
                Files.Update(item, FileState.Copied);
                result.FilesCopied++;
                result.BytesTransferred += item.Length;
                progress.EndFile();
                current = index + 1;
                if (current < items.Count)
                {
                    progress.BeginFile(items[current].Source, items[current].Length);
                }
            }, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            throw new IOException(Texts.F("E_ConnectFailed", host, port, ex.Message), ex);
        }

        if (!accepted)
        {
            throw new CopyPlanException(Texts.Get("E_LocalSendRejected"));
        }

        // Lo que el otro lado no quiso (ya lo tenía) cuenta como omitido.
        foreach (var item in items.Where(i => i.State is not FileState.Copied))
        {
            Files.Update(item, FileState.Skipped, Texts.Get("E_SkipIdentical"));
            result.FilesSkipped++;
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(LogLevel.Info, Texts.F("E_LocalSendDone", result.FilesCopied, FormatBytes(result.BytesTransferred)));
        return result;
    }
}
