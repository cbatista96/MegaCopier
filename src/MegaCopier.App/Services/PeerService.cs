using System.Security.Cryptography.X509Certificates;
using MegaCopier.Engine;
using MegaCopier.Engine.Network;

namespace MegaCopier.App.Services;

/// <summary>
/// "Recibir de otros equipos": escucha en la red, contesta a quien busca equipos con MegaCopier y deja lo recibido en
/// la carpeta elegida. El certificado propio se guarda en la carpeta de datos; el código lo muestra Ajustes.
/// </summary>
internal sealed class PeerService(App app) : IDisposable
{
    private PeerReceiver? _receiver;
    private PeerDiscovery? _discovery;

    private AppSettings Settings => app.Settings;

    public bool IsRunning => _receiver is not null;

    public int Port => _receiver?.Port ?? Settings.ReceivePort;

    /// <summary>Carpeta de recepción (por defecto, Descargas\MegaCopier).</summary>
    public string Folder => Settings.ReceiveFolder.Length > 0
        ? Settings.ReceiveFolder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MegaCopier");

    /// <summary>El código que hay que darle a quien envía (se crea la primera vez).</summary>
    public string Code
    {
        get
        {
            if (Settings.ReceiveCode.Length == 0)
            {
                Settings.ReceiveCode = PeerProtocol.NewCode();
                Settings.Save();
            }

            return Settings.ReceiveCode;
        }
    }

    public string? Error { get; private set; }

    /// <summary>Arranca o para según Ajustes (se llama al abrir y al cambiar Ajustes).</summary>
    public void Apply()
    {
        Stop();
        Error = null;
        if (!Settings.ReceiveEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);

            // En pruebas solo se escucha en este equipo: así no aparece el aviso del cortafuegos.
            bool loopback = App.TestMode;
            _receiver = new PeerReceiver(Folder, Code, Certificate(), Settings.ReceivePort, loopback);
            _receiver.Event += e => app.Dispatcher.TryEnqueue(() => OnEvent(e));
            _receiver.Start();
            _discovery = new PeerDiscovery(Environment.MachineName, _receiver.Port, loopbackOnly: loopback);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Error = ex.Message;
            Stop();
        }
    }

    /// <summary>Nuevo código: quien tuviera el anterior ya no puede enviar.</summary>
    public void NewCode()
    {
        Settings.ReceiveCode = PeerProtocol.NewCode();
        Settings.Save();
        Apply();
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        _discovery?.Dispose();
        _discovery = null;
        _receiver?.Dispose();
        _receiver = null;
    }

    private static X509Certificate2 Certificate()
    {
        string path = DataFolder.Combine("peer.pfx");
        try
        {
            if (File.Exists(path))
            {
                return X509CertificateLoader.LoadPkcs12FromFile(path, null);
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
        }

        byte[] pfx = PeerProtocol.CreateCertificatePfx(Environment.MachineName);
        Directory.CreateDirectory(DataFolder.Path);
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    private void OnEvent(PeerEvent e)
    {
        if (App.TestMode)
        {
            App.TestLog("peer.log", $"{e.Kind} {e.Peer} archivos={e.Files} bytes={e.Bytes} {e.Detail}");
        }

        switch (e.Kind)
        {
            case PeerEventKind.Started:
                app.Notify(L.Get("Peer_ReceivingTitle"), L.F("Peer_Receiving", e.Peer, e.Files, ViewModels.Format.Bytes(e.Bytes)));
                break;
            case PeerEventKind.Finished:
                app.Notify(L.Get("Peer_ReceivedTitle"), L.F("Peer_Received", e.Files, e.Peer, Folder));
                break;
            case PeerEventKind.Rejected:
                app.Notify(L.Get("Peer_RejectedTitle"), L.F("Peer_Rejected", e.Peer));
                break;
        }
    }
}
