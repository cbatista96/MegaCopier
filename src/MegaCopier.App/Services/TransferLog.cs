using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>
/// Registro único de todas las copias (como el de Ultracopier): una línea por archivo copiado o fallido en un solo archivo
/// de texto, con el formato que se quiera (%time%, %source%, %destination%, %size%, %path%, %error%, %hash%, %computer%,
/// %user%), y si se quiere, también a un servidor Syslog de la red (para quien administra varios equipos). Escribe en
/// segundo plano: la copia nunca espera al registro.
/// </summary>
internal sealed class TransferLog
{
    public const string DefaultTransferFormat = "[%time%] %source% (%size%) %destination%";
    public const string DefaultErrorFormat = "[%time%] %path%, %error%";

    private readonly App _app;
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<CopyJob> _watched = [];

    public TransferLog(App app)
    {
        _app = app;
        _ = WriteLoopAsync();
        foreach (var job in app.Queue.Jobs)
        {
            Watch(job);
        }

        app.Queue.JobAdded += Watch;
    }

    private void Watch(CopyJob job)
    {
        // La lista de archivos aparece cuando la copia empieza: se espera a ese momento.
        job.Changed += _ => Attach(job);
        Attach(job);
    }

    private void Attach(CopyJob job)
    {
        if (job.Files is not { } files)
        {
            return;
        }

        lock (_watched)
        {
            if (!_watched.Add(job))
            {
                return;
            }
        }

        var logged = new HashSet<int>();
        files.Changed += change =>
        {
            var s = _app.Settings;
            if (change.Kind != TransferListChangeKind.Updated || (!s.TransferLogEnabled && s.SyslogServer.Length == 0))
            {
                return;
            }

            var item = change.Item;
            string? line = item.State switch
            {
                FileState.Copied or FileState.Verified when Add(logged, item.Id) => Format(s.TransferLogFormat.Length > 0 ? s.TransferLogFormat : DefaultTransferFormat, item),
                FileState.Failed when Add(logged, item.Id) => Format(s.ErrorLogFormat.Length > 0 ? s.ErrorLogFormat : DefaultErrorFormat, item),
                _ => null,
            };
            if (line is not null)
            {
                _lines.Writer.TryWrite(line);
            }
        };
    }

    private static bool Add(HashSet<int> logged, int id)
    {
        lock (logged)
        {
            return logged.Add(id); // con verificar, «copiado» y luego «verificado»: una sola línea
        }
    }

    /// <summary>La línea de un archivo con las variables sustituidas.</summary>
    internal static string Format(string format, TransferItem item) => new StringBuilder(format)
        .Replace("%time%", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        .Replace("%source%", item.Source)
        .Replace("%path%", item.Source)
        .Replace("%destination%", item.Destination)
        .Replace("%size%", item.Length.ToString(CultureInfo.InvariantCulture))
        .Replace("%mtime%", item.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        .Replace("%error%", item.Message ?? "")
        .Replace("%hash%", item.DestinationHash ?? item.SourceHash ?? "")
        .Replace("%computer%", Environment.MachineName)
        .Replace("%user%", Environment.UserName)
        .ToString();

    private async Task WriteLoopAsync()
    {
        using var udp = new UdpClient();
        await foreach (string line in _lines.Reader.ReadAllAsync())
        {
            var s = _app.Settings;
            if (s.TransferLogEnabled && s.TransferLogPath.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(s.TransferLogPath))!);
                    await File.AppendAllTextAsync(s.TransferLogPath, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Un registro que no se puede escribir no detiene la copia.
                }
            }

            if (SyslogTarget(s.SyslogServer) is var (host, port))
            {
                try
                {
                    byte[] message = Encoding.UTF8.GetBytes(Syslog(line));
                    await udp.SendAsync(message, host, port);
                }
                catch (Exception ex) when (ex is SocketException or ArgumentException)
                {
                }
            }
        }
    }

    /// <summary>"servidor" o "servidor:puerto" (514 de serie), o null si no hay.</summary>
    internal static (string Host, int Port)? SyslogTarget(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        int colon = text.LastIndexOf(':');
        return colon > 0 && int.TryParse(text[(colon + 1)..], out int port) && port is > 0 and < 65536
            ? (text[..colon], port)
            : (text, 514);
    }

    /// <summary>Mensaje Syslog (RFC 5424): instalación «usuario», gravedad «aviso», con fecha y equipo.</summary>
    internal static string Syslog(string line) =>
        $"<13>1 {DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} {Environment.MachineName} MegaCopier - - - {line}";
}
