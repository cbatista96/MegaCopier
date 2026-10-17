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
    private LocalSendReceiver? _localSend;

    /// <summary>LocalSend escuchando (para Ajustes).</summary>
    public bool LocalSendRunning => _localSend is not null;

    public int LocalSendPort => _localSend?.Port ?? LocalSendProtocol.DefaultPort;

    public string? LocalSendError { get; private set; }

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
        ApplyLocalSend();
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
            _discovery?.Dispose();
            _discovery = null;
            _receiver?.Dispose();
            _receiver = null; // LocalSend sigue: es independiente
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
        _localSend?.Dispose();
        _localSend = null;
    }

    /// <summary>
    /// LocalSend: el PC aparece con su nombre en la app del móvil. En pruebas solo escucha en este equipo y en un puerto
    /// libre (sin aviso del cortafuegos ni multidifusión), y no pregunta con una ventana encima del escritorio.
    /// </summary>
    private void ApplyLocalSend()
    {
        LocalSendError = null;
        if (!Settings.LocalSendEnabled)
        {
            return;
        }

        if (Settings.LocalSendFingerprint.Length == 0)
        {
            Settings.LocalSendFingerprint = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            Settings.Save();
        }

        try
        {
            Directory.CreateDirectory(Folder);
            bool test = App.TestMode;
            _localSend = new LocalSendReceiver(Folder, Environment.MachineName, Settings.LocalSendFingerprint, AskAsync,
                port: test ? 0 : LocalSendProtocol.DefaultPort, loopbackOnly: test);
            _localSend.Event += e => app.Dispatcher.TryEnqueue(() => OnEvent(e));
            _localSend.Start();
            if (test)
            {
                App.TestLog("peer.log", $"localsend puerto={_localSend.Port}");
            }
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            LocalSendError = ex.Message;
            _localSend?.Dispose();
            _localSend = null;
        }
    }

    /// <summary>¿Se acepta este envío? Sin "aceptar sin preguntar", con el cuadro Sí/No de Windows.</summary>
    private Task<bool> AskAsync(LocalSendOffer offer)
    {
        if (Settings.LocalSendAutoAccept)
        {
            return Task.FromResult(true);
        }

        if (App.TestMode)
        {
            App.TestLog("peer.log", $"localsend pregunta {offer.Sender} archivos={offer.Files} bytes={offer.Bytes}");
            return Task.FromResult(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_LOCALSEND") != "reject");
        }

        string question = L.F("LocalSend_Ask", offer.Sender, offer.Files, ViewModels.Format.Bytes(offer.Bytes));
        return Task.Run(() => SystemActions.Confirm(question));
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
