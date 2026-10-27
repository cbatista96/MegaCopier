using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MegaCopier.Engine.Network;

/// <summary>
/// Recibe archivos enviados con LocalSend desde un móvil (o un ordenador) de la misma red, en la carpeta elegida.
///
/// Un servidor HTTP mínimo (sin dependencias: HttpListener exigiría permisos de administrador para escuchar en la red)
/// atiende las rutas del protocolo v2. Cada envío se pregunta antes (<c>accept</c>); aceptado, cada archivo se
/// escribe en un .part y se renombra al llegar entero, con su fecha y comprobando su SHA-256 si el móvil lo manda.
/// Los nombres se limpian: un envío nunca escribe fuera de la carpeta. Un envío a la vez (409 a los demás).
/// </summary>
public sealed class LocalSendReceiver : IDisposable
{
    private static readonly TimeSpan SessionIdle = TimeSpan.FromMinutes(10);

    private readonly string _folder;
    private readonly LocalSendInfo _info;
    private readonly Func<LocalSendOffer, Task<bool>> _accept;
    private readonly TcpListener _listener;
    private readonly bool _loopbackOnly;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Session? _session;
    private UdpClient? _udp;

    /// <summary>Qué hacer con un archivo que ya está en la carpeta (de serie, conservar los dos).</summary>
    public LocalSendConflict Conflict { get; set; } = LocalSendConflict.Rename;

    /// <summary>
    /// Solo se piden al móvil los archivos que pasan este filtro (nombre, extensión, tamaño): lo demás ni se descarga.
    /// </summary>
    public FileFilter? Filter { get; set; }

    /// <param name="folder">Dónde se guardan los archivos recibidos.</param>
    /// <param name="alias">El nombre con el que aparece este equipo en LocalSend.</param>
    /// <param name="fingerprint">Identificador estable de este equipo (en "http" basta con que sea único).</param>
    /// <param name="accept">Pregunta si se acepta un envío (true = aceptar).</param>
    /// <param name="loopbackOnly">Pruebas: solo este equipo y sin multidifusión (sin aviso del cortafuegos).</param>
    public LocalSendReceiver(string folder, string alias, string fingerprint, Func<LocalSendOffer, Task<bool>> accept, int port = LocalSendProtocol.DefaultPort, bool loopbackOnly = false)
    {
        _folder = Path.GetFullPath(folder);
        _accept = accept;
        _loopbackOnly = loopbackOnly;
        _listener = loopbackOnly ? new TcpListener(IPAddress.Loopback, port) : new TcpListener(IPAddress.Any, port);
        _info = new LocalSendInfo
        {
            Alias = alias,
            DeviceModel = "Windows",
            DeviceType = "desktop",
            Fingerprint = fingerprint,
            Protocol = "http",
            Download = false,
        };
    }

    public event Action<PeerEvent>? Event;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _info.Port = Port;
        _ = AcceptLoopAsync();
        if (!_loopbackOnly)
        {
            StartDiscovery();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _udp?.Dispose();
        lock (_gate)
        {
            _session?.DeletePartials();
            _session = null;
        }
    }

    // --- Descubrimiento: el móvil ve este PC en su lista de LocalSend ---

    private void StartDiscovery()
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, LocalSendProtocol.DefaultPort));
            var group = IPAddress.Parse(LocalSendProtocol.MulticastAddress);
            foreach (var address in LocalAddresses())
            {
                try
                {
                    udp.JoinMulticastGroup(group, address);
                }
                catch (SocketException)
                {
                    // Una interfaz sin multidifusión (VPN...): se sigue con las demás.
                }
            }

            _udp = udp;
            _ = ListenAnnouncementsAsync(udp);
            _ = AnnounceAsync(udp);
        }
        catch (SocketException)
        {
            // Sin descubrimiento el envío sigue funcionando escribiendo la IP en LocalSend.
        }
    }

    private static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address);

    private async Task AnnounceAsync(UdpClient udp)
    {
        for (int i = 0; i < 3 && !_stop.IsCancellationRequested; i++)
        {
            await SendAnnouncementAsync(udp, announce: true);
            await Task.Delay(TimeSpan.FromSeconds(i == 0 ? 0.1 : 1), _stop.Token).ConfigureAwait(false);
        }
    }

    private async Task SendAnnouncementAsync(UdpClient udp, bool announce)
    {
        var announcement = new LocalSendInfo
        {
            Alias = _info.Alias,
            DeviceModel = _info.DeviceModel,
            DeviceType = _info.DeviceType,
            Fingerprint = _info.Fingerprint,
            Port = _info.Port,
            Protocol = _info.Protocol,
            Download = _info.Download,
            Announce = announce,
        };
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(announcement, LocalSendJsonContext.Default.LocalSendInfo);
        try
        {
            await udp.SendAsync(message, new IPEndPoint(IPAddress.Parse(LocalSendProtocol.MulticastAddress), LocalSendProtocol.DefaultPort), _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task ListenAnnouncementsAsync(UdpClient udp)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                var other = JsonSerializer.Deserialize(received.Buffer, LocalSendJsonContext.Default.LocalSendInfo);
                if (other is { Announce: true } && other.Fingerprint != _info.Fingerprint)
                {
                    await SendAnnouncementAsync(udp, announce: false); // "aquí estoy" para quien acaba de llegar
                }
            }
            catch (JsonException)
            {
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    // --- HTTP ---

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            var stream = new BufferedStream(client.GetStream(), 1 << 16);
            try
            {
                // Varias peticiones por conexión (keep-alive), como hace LocalSend.
                while (!_stop.IsCancellationRequested && await HttpRequest.ReadAsync(stream, _stop.Token).ConfigureAwait(false) is { } request)
                {
                    var (status, body) = await HandleAsync(request, stream, remote).ConfigureAwait(false);
                    await request.SkipRestAsync(stream, _stop.Token).ConfigureAwait(false);
                    await WriteResponseAsync(stream, status, body).ConfigureAwait(false);
                    if (request.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
            }
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int status, byte[]? body)
    {
        string reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", _ => "Internal Server Error" };
        string head = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {body?.Length ?? 0}\r\n"
            + (body is null ? "" : "Content-Type: application/json\r\n")
            + "Connection: keep-alive\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
        if (body is not null)
        {
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
    }

    private async Task<(int Status, byte[]? Body)> HandleAsync(HttpRequest request, Stream stream, IPAddress remote)
    {
        if (!request.Path.StartsWith(LocalSendProtocol.ApiPrefix, StringComparison.Ordinal))
        {
            return (404, null);
        }

        string action = request.Path[LocalSendProtocol.ApiPrefix.Length..];
        switch (action)
        {
            case "info" when request.Method == "GET":
            case "register" when request.Method == "POST":
                return (200, JsonSerializer.SerializeToUtf8Bytes(_info, LocalSendJsonContext.Default.LocalSendInfo));
            case "prepare-upload" when request.Method == "POST":
                return await PrepareAsync(request, stream, remote).ConfigureAwait(false);
            case "upload" when request.Method == "POST":
                return (await UploadAsync(request, stream, remote).ConfigureAwait(false), null);
            case "cancel" when request.Method == "POST":
                Cancel(request.Query("sessionId"), remote);
                return (200, null);
            default:
                return (404, null);
        }
    }

    private async Task<(int, byte[]?)> PrepareAsync(HttpRequest request, Stream stream, IPAddress remote)
    {
        LocalSendPrepareRequest? prepare;
        try
        {
            prepare = JsonSerializer.Deserialize(await request.ReadBodyAsync(stream, 4 << 20, _stop.Token).ConfigureAwait(false), LocalSendJsonContext.Default.LocalSendPrepareRequest);
        }
        catch (JsonException)
        {
            return (400, null);
        }

        if (prepare is null || prepare.Files.Count == 0 || prepare.Files.Values.Any(f => LocalSendProtocol.SafeRelativePath(f.FileName) is null || f.Size < 0))
        {
            return (400, null);
        }

        lock (_gate)
        {
            if (_session is { } busy && !busy.IsIdle(SessionIdle))
            {
                return (409, null);
            }

            _session?.DeletePartials();
            _session = null;
        }

        string sender = prepare.Info.Alias.Length > 0 ? prepare.Info.Alias : remote.ToString();

        // Antes de aceptar: fuera lo que el filtro no quiere y, con «omitir», lo que ya está en la carpeta. El móvil solo
        // envía los archivos para los que recibe permiso.
        var filter = Filter is { IsEmpty: false } f ? f : null;
        var conflict = Conflict;
        var wanted = prepare.Files
            .Where(kv =>
            {
                string relative = LocalSendProtocol.SafeRelativePath(kv.Value.FileName)!;
                return (filter is null || filter.AcceptsFile(relative, kv.Value.Size, kv.Value.Metadata?.Modified?.UtcDateTime))
                    && !(conflict == LocalSendConflict.Skip && File.Exists(Path.Combine(_folder, relative)));
            })
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        long bytes = wanted.Values.Sum(f => f.Size);
        if (wanted.Count == 0)
        {
            // Nada que recibir: el protocolo responde 204 y el móvil da el envío por terminado.
            Event?.Invoke(new PeerEvent(PeerEventKind.Rejected, sender, prepare.Files.Count, prepare.Files.Values.Sum(f => f.Size), "LocalSend"));
            return (204, null);
        }

        bool accepted;
        try
        {
            accepted = await _accept(new LocalSendOffer(sender, prepare.Info.DeviceModel, wanted.Count, bytes)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            accepted = false;
        }

        if (!accepted)
        {
            Event?.Invoke(new PeerEvent(PeerEventKind.Rejected, sender, wanted.Count, bytes, "LocalSend"));
            return (403, null);
        }

        var session = new Session(_folder, sender, remote, wanted, conflict);
        lock (_gate)
        {
            if (_session is { } other && !other.IsIdle(SessionIdle))
            {
                return (409, null);
            }

            _session = session;
        }

        Directory.CreateDirectory(_folder);
        Event?.Invoke(new PeerEvent(PeerEventKind.Started, sender, wanted.Count, bytes, "LocalSend"));
        var response = new LocalSendPrepareResponse { SessionId = session.Id, Files = session.Tokens() };
        return (200, JsonSerializer.SerializeToUtf8Bytes(response, LocalSendJsonContext.Default.LocalSendPrepareResponse));
    }

    private async Task<int> UploadAsync(HttpRequest request, Stream stream, IPAddress remote)
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null || session.Id != request.Query("sessionId"))
        {
            return 403;
        }

        if (!session.Sender.Equals(remote) || !session.TryStart(request.Query("fileId"), request.Query("token"), out var file))
        {
            return 403;
        }

        string partial = file.Target + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
            long written = 0;
            using var sha = file.Meta.Sha256 is { Length: > 0 } ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await request.CopyBodyAsync(stream, async chunk =>
                {
                    written += chunk.Length;
                    if (written > file.Meta.Size && file.Meta.Size > 0)
                    {
                        throw new InvalidDataException("El archivo es más grande de lo anunciado.");
                    }

                    sha?.AppendData(chunk.Span);
                    session.Touch();
                    await output.WriteAsync(chunk, _stop.Token).ConfigureAwait(false);
                }, _stop.Token).ConfigureAwait(false);
                await output.FlushAsync(_stop.Token).ConfigureAwait(false);
            }

            if (written != file.Meta.Size
                || (sha is not null && !string.Equals(Convert.ToHexString(sha.GetHashAndReset()), file.Meta.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(partial);
                session.Fail(file);
                return 500;
            }

            string target = session.FreeTarget(file);
            File.Move(partial, target, overwrite: session.Replaces); // solo se pisa algo si se eligió sustituir
            if (file.Meta.Metadata?.Modified is { } modified)
            {
                File.SetLastWriteTimeUtc(target, modified.UtcDateTime);
            }

            if (session.Complete(file, written))
            {
                lock (_gate)
                {
                    if (_session == session)
                    {
                        _session = null;
                    }
                }

                Event?.Invoke(new PeerEvent(PeerEventKind.Finished, session.SenderName, session.Received, session.ReceivedBytes, "LocalSend"));
            }

            return 200;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try
            {
                File.Delete(partial);
            }
            catch (IOException)
            {
            }

            session.Fail(file);
            return 500;
        }
    }

    private void Cancel(string? sessionId, IPAddress remote)
    {
        Session? cancelled = null;
        lock (_gate)
        {
            if (_session is { } session && session.Id == sessionId && session.Sender.Equals(remote))
            {
                cancelled = session;
                _session = null;
            }
        }

        if (cancelled is not null)
        {
            cancelled.DeletePartials();
            Event?.Invoke(new PeerEvent(PeerEventKind.Failed, cancelled.SenderName, cancelled.Received, cancelled.ReceivedBytes, "LocalSend"));
        }
    }

    /// <summary>Un envío aceptado: qué archivos se esperan, con qué permiso (token) y cuáles han llegado.</summary>
    private sealed class Session
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Expected> _files = [];
        private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
        private long _lastActivity = Environment.TickCount64;

        private readonly LocalSendConflict _conflict;

        /// <summary>Se eligió sustituir lo que ya hay en la carpeta.</summary>
        public bool Replaces => _conflict == LocalSendConflict.Overwrite;

        public Session(string folder, string senderName, IPAddress sender, Dictionary<string, LocalSendFile> files, LocalSendConflict conflict = LocalSendConflict.Rename)
        {
            SenderName = senderName;
            _conflict = conflict;
            Sender = sender;
            foreach (var (id, meta) in files)
            {
                _files[id] = new Expected(meta, Path.Combine(folder, LocalSendProtocol.SafeRelativePath(meta.FileName)!), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
            }
        }

        public string Id { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        public string SenderName { get; }

        public IPAddress Sender { get; }

        public int Received { get; private set; }

        public long ReceivedBytes { get; private set; }

        public Dictionary<string, string> Tokens() => _files.ToDictionary(f => f.Key, f => f.Value.Token);

        public void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

        public bool IsIdle(TimeSpan limit) => Environment.TickCount64 - Interlocked.Read(ref _lastActivity) > limit.TotalMilliseconds;

        public bool TryStart(string? fileId, string? token, out Expected file)
        {
            lock (_gate)
            {
                Touch();
                if (fileId is not null && _files.TryGetValue(fileId, out file!) && file.Token == token && file.State == FileProgress.Waiting)
                {
                    file.State = FileProgress.Receiving;
                    return true;
                }

                file = null!;
                return false;
            }
        }

        /// <summary>
        /// El nombre definitivo: si ya existe un archivo con ese nombre, "foto (2).jpg" (no se pisa nada), salvo que se
        /// haya elegido sustituirlo. Dos archivos del mismo envío con el mismo nombre nunca se pisan entre sí.
        /// </summary>
        public string FreeTarget(Expected file)
        {
            lock (_gate)
            {
                string directory = Path.GetDirectoryName(file.Target)!;
                string name = Path.GetFileNameWithoutExtension(file.Target);
                string extension = Path.GetExtension(file.Target);
                string candidate = file.Target;
                bool replace = _conflict == LocalSendConflict.Overwrite;
                for (int n = 2; (!replace && File.Exists(candidate)) || Directory.Exists(candidate) || _reserved.Contains(candidate); n++)
                {
                    candidate = Path.Combine(directory, $"{name} ({n}){extension}");
                }

                _reserved.Add(candidate);
                return candidate;
            }
        }

        /// <summary>Un archivo llegó entero; true si era el último.</summary>
        public bool Complete(Expected file, long bytes)
        {
            lock (_gate)
            {
                file.State = FileProgress.Done;
                Received++;
                ReceivedBytes += bytes;
                return _files.Values.All(f => f.State is FileProgress.Done or FileProgress.Failed);
            }
        }

        /// <summary>Un archivo falló: el móvil puede volver a enviarlo con el mismo permiso.</summary>
        public void Fail(Expected file)
        {
            lock (_gate)
            {
                file.State = FileProgress.Waiting;
            }
        }

        public void DeletePartials()
        {
            foreach (var file in _files.Values.Where(f => f.State == FileProgress.Receiving))
            {
                try
                {
                    File.Delete(file.Target + ".part");
                }
                catch (IOException)
                {
                }
            }
        }

        public sealed class Expected(LocalSendFile meta, string target, string token)
        {
            public LocalSendFile Meta { get; } = meta;

            public string Target { get; } = target;

            public string Token { get; } = token;

            public FileProgress State { get; set; }
        }

        public enum FileProgress
        {
            Waiting,
            Receiving,
            Done,
            Failed,
        }
    }
}
