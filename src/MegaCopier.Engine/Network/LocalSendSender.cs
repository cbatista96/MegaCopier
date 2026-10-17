using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace MegaCopier.Engine.Network;

/// <summary>Un móvil u ordenador con LocalSend encontrado en la red.</summary>
public sealed record LocalSendDevice(string Alias, string? DeviceModel, IPAddress Address, int Port, bool Https, string Fingerprint)
{
    /// <summary>Destino de una copia: localsend://ip:puerto/https (o /http)/huella.</summary>
    public string Destination => $"{LocalSendSender.Scheme}{(Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{Address}]" : Address.ToString())}:{Port}/{(Https ? "https" : "http")}/{Fingerprint}";
}

/// <summary>
/// Enviar a la app LocalSend de un móvil (o de otro ordenador): el otro lado lo acepta en su pantalla y los archivos
/// llegan a su carpeta de descargas. Es el protocolo de LocalSend v2: preparar el envío (prepare-upload) y subir cada
/// archivo (upload). Con HTTPS, LocalSend usa un certificado propio; se comprueba que su huella SHA-256 sea la que
/// anunció el equipo.
/// </summary>
public static class LocalSendSender
{
    public const string Scheme = "localsend://";

    public static bool IsDestination(string destination) => destination.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lee un destino localsend://ip:puerto/https/huella.</summary>
    public static (string Host, int Port, bool Https, string Fingerprint) Parse(string destination)
    {
        string[] parts = destination[Scheme.Length..].Split('/', 3);
        var (host, port) = CopyEngine.ParsePeer(parts[0]);
        return (host, port == PeerProtocol.DefaultPort ? LocalSendProtocol.DefaultPort : port,
            parts.Length < 2 || !parts[1].Equals("http", StringComparison.OrdinalIgnoreCase), parts.Length > 2 ? parts[2] : "");
    }

    /// <summary>
    /// Busca equipos con LocalSend: se anuncia por multidifusión y se recogen las respuestas durante <paramref name="wait"/>.
    /// </summary>
    public static async Task<List<LocalSendDevice>> DiscoverAsync(string alias, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, LocalSendDevice>();
        string fingerprint = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, LocalSendProtocol.DefaultPort));
        var group = IPAddress.Parse(LocalSendProtocol.MulticastAddress);
        foreach (var address in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
        {
            try
            {
                udp.JoinMulticastGroup(group, address.Address);
            }
            catch (SocketException)
            {
            }
        }

        // Sin servidor propio al que registrarse: los demás contestan por multidifusión.
        var me = new LocalSendInfo { Alias = alias, DeviceModel = "Windows", DeviceType = "desktop", Fingerprint = fingerprint, Port = LocalSendProtocol.DefaultPort, Protocol = "http", Download = false, Announce = true };
        byte[] message = JsonSerializer.SerializeToUtf8Bytes(me, LocalSendJsonContext.Default.LocalSendInfo);
        await udp.SendAsync(message, new IPEndPoint(group, LocalSendProtocol.DefaultPort), cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    var other = JsonSerializer.Deserialize(received.Buffer, LocalSendJsonContext.Default.LocalSendInfo);
                    if (other is { Fingerprint.Length: > 0 } && other.Fingerprint != fingerprint && other.Port is > 0)
                    {
                        found[other.Fingerprint] = new LocalSendDevice(other.Alias, other.DeviceModel, received.RemoteEndPoint.Address, other.Port.Value,
                            !string.Equals(other.Protocol, "http", StringComparison.OrdinalIgnoreCase), other.Fingerprint);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return [.. found.Values];
    }

    /// <summary>
    /// Envía los archivos (ruta en el disco y nombre con el que llegan, con "/" para las carpetas). Devuelve false si el
    /// otro lado lo rechazó. <paramref name="onSent"/> recibe cada bloque enviado y <paramref name="onFileDone"/> cada
    /// archivo terminado.
    /// </summary>
    public static async Task<bool> SendAsync(string destination, string alias, IReadOnlyList<(string Path, string Name)> files,
        Action<int> onSent, Action<int> onFileDone, CancellationToken cancellationToken)
    {
        var (host, port, https, fingerprint) = Parse(destination);
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                // LocalSend usa un certificado propio (no de una autoridad): vale si es el que anunció el equipo.
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && (fingerprint.Length == 0
                        || string.Equals(Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())), fingerprint, StringComparison.OrdinalIgnoreCase)),
            },
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        string bracketed = host.Contains(':') ? $"[{host}]" : host;
        var baseUri = new Uri($"{(https ? "https" : "http")}://{bracketed}:{port}{LocalSendProtocol.ApiPrefix}");

        var request = new LocalSendPrepareRequest
        {
            Info = new LocalSendInfo { Alias = alias, DeviceModel = "Windows", DeviceType = "desktop", Fingerprint = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), Port = port, Protocol = "http", Download = false },
        };
        for (int i = 0; i < files.Count; i++)
        {
            var info = new FileInfo(files[i].Path);
            request.Files[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new LocalSendFile
            {
                Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FileName = files[i].Name.Replace('\\', '/'),
                Size = info.Length,
                FileType = "application/octet-stream",
                Metadata = new LocalSendMetadata { Modified = info.LastWriteTimeUtc },
            };
        }

        // Preparar: el otro lado pregunta en su pantalla. 204 = no hace falta enviar nada; 403 = lo rechazó.
        using var prepare = await http.PostAsJsonAsync(new Uri(baseUri, "prepare-upload"), request, LocalSendJsonContext.Default.LocalSendPrepareRequest, cancellationToken).ConfigureAwait(false);
        if (prepare.StatusCode == HttpStatusCode.NoContent)
        {
            return true;
        }

        if (prepare.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return false;
        }

        prepare.EnsureSuccessStatusCode();
        var session = await prepare.Content.ReadFromJsonAsync(LocalSendJsonContext.Default.LocalSendPrepareResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException("LocalSend: respuesta vacía");
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                string id = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!session.Files.TryGetValue(id, out string? token))
                {
                    continue; // el otro lado no lo quiere (ya lo tenía)
                }

                await using var input = new FileStream(files[i].Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);
                var content = new StreamContent(new ReportingStream(input, onSent), 1 << 20);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Headers.ContentLength = input.Length;
                string query = $"upload?sessionId={Uri.EscapeDataString(session.SessionId)}&fileId={Uri.EscapeDataString(id)}&token={Uri.EscapeDataString(token)}";
                using var upload = await http.PostAsync(new Uri(baseUri, query), content, cancellationToken).ConfigureAwait(false);
                upload.EnsureSuccessStatusCode();
                onFileDone(i);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException)
        {
            // Avisar al otro lado de que se cancela, para que no se quede esperando.
            try
            {
                using var cancel = await http.PostAsync(new Uri(baseUri, $"cancel?sessionId={Uri.EscapeDataString(session.SessionId)}"), null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception other) when (other is HttpRequestException or IOException or OperationCanceledException)
            {
            }

            throw;
        }

        return true;
    }

    private sealed class ReportingStream(Stream inner, Action<int> onRead) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            onRead(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            onRead(read);
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
