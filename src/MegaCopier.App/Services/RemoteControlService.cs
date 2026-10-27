using System.Net.NetworkInformation;
using System.Net.Sockets;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using MegaCopier.Engine.Network;

namespace MegaCopier.App.Services;

/// <summary>
/// Control desde el móvil (Ajustes): arranca o para el servidor web de <see cref="RemoteControlServer"/> y le da las
/// copias de la cola. En pruebas solo escucha en este equipo y en un puerto libre (sin aviso del cortafuegos).
/// </summary>
internal sealed class RemoteControlService(App app) : IDisposable
{
    private RemoteControlServer? _server;

    public bool Running => _server is not null;

    public int Port => _server?.Port ?? RemoteControlServer.DefaultPort;

    public string? Error { get; private set; }

    /// <summary>Direcciones a escribir en el navegador del móvil (una por red).</summary>
    public IReadOnlyList<string> Addresses =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => $"http://{a.Address}:{Port}")];

    public void Apply()
    {
        _server?.Dispose();
        _server = null;
        Error = null;
        if (!app.Settings.RemoteControlEnabled)
        {
            return;
        }

        if (app.Settings.RemoteControlCode.Length == 0)
        {
            app.Settings.RemoteControlCode = RemoteControlServer.NewCode();
            app.Settings.Save();
        }

        var labels = new Dictionary<string, string>
        {
            ["code"] = L.Get("Remote_PageCode"),
            ["enter"] = L.Get("Remote_PageEnter"),
            ["empty"] = L.Get("Remote_PageEmpty"),
            ["pause"] = L.Get("Remote_PagePause"),
            ["resume"] = L.Get("Remote_PageResume"),
            ["cancel"] = L.Get("Remote_PageCancel"),
            ["wrong"] = L.Get("Remote_PageWrong"),
        };
        try
        {
            _server = new RemoteControlServer(app.Settings.RemoteControlCode, Jobs, Act, labels,
                port: App.TestMode ? 0 : RemoteControlServer.DefaultPort, loopbackOnly: App.TestMode);
            _server.Start();
        }
        catch (SocketException ex)
        {
            _server = null;
            Error = ex.Message;
        }
    }

    /// <summary>Otro código (el anterior deja de valer).</summary>
    public void NewCode()
    {
        app.Settings.RemoteControlCode = RemoteControlServer.NewCode();
        app.Settings.Save();
        Apply();
    }

    private IReadOnlyList<RemoteJobView> Jobs() =>
        [.. app.Queue.Jobs.Where(j => j.State is not (CopyJobState.Completed or CopyJobState.CompletedWithErrors) || j.FinishedAt > DateTime.Now.AddMinutes(-30))
            .Select(job =>
            {
                string first = Engine.SourceNames.TargetName(job.Record.Sources.FirstOrDefault() ?? "");
                string title = job.Record.Sources.Count > 1 ? L.F("Remote_TitleMany", first, job.Record.Sources.Count - 1) : first;
                title += " → " + ViewModels.Format.ItemName(job.Record.Destination);
                var progress = job.Progress;
                string detail = progress is { } p && job.State == CopyJobState.Running
                    ? $"{ViewModels.Format.Bytes(p.CompletedBytes)} / {ViewModels.Format.Bytes(p.TotalBytes)} · {ViewModels.Format.Bytes((long)p.BytesPerSecond)}/s"
                    : progress is { } done ? $"{ViewModels.Format.Bytes(done.CompletedBytes)} / {ViewModels.Format.Bytes(done.TotalBytes)}" : "";
                return new RemoteJobView(job.Id, title, L.Get("Remote_State" + job.State), (progress?.Fraction ?? 0) * 100, detail,
                    CanPause: job.State is CopyJobState.Running,
                    CanResume: job.State is CopyJobState.Paused or CopyJobState.Interrupted or CopyJobState.Failed,
                    CanCancel: job.State is CopyJobState.Running or CopyJobState.Paused or CopyJobState.Queued);
            })];

    private bool Act(string id, string action)
    {
        if (app.Queue.Jobs.FirstOrDefault(j => j.Id == id) is not { } job)
        {
            return false;
        }

        switch (action)
        {
            case "pause":
                app.Queue.Pause(job);
                break;
            case "resume":
                app.Queue.Resume(job);
                break;
            default:
                app.Queue.Stop(job); // se detiene conservando lo copiado: se puede seguir desde el PC
                break;
        }

        return true;
    }

    public void Dispose() => _server?.Dispose();
}
