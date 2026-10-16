using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MegaCopier.Engine.Network;

/// <summary>Un equipo de la red con MegaCopier recibiendo.</summary>
public sealed record PeerInfo(string Name, string Address, int Port);

/// <summary>
/// Descubrir equipos con MegaCopier en la red local: el que envía pregunta por difusión UDP y los que reciben contestan
/// con su nombre y su puerto. No se comparte nada más (el código nunca viaja).
/// </summary>
public sealed class PeerDiscovery : IDisposable
{
    private const string Question = "MEGACOPIER?";
    private const string Answer = "MEGACOPIER!";
    private readonly UdpClient _udp;
    private readonly string _name;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Contestar a las preguntas (en el equipo que recibe).</summary>
    public PeerDiscovery(string name, int receivePort, int discoveryPort = PeerProtocol.DiscoveryPort, bool loopbackOnly = false)
    {
        _name = name;
        _port = receivePort;
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, discoveryPort));
        _ = AnswerLoopAsync();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _udp.Dispose();
    }

    private async Task AnswerLoopAsync()
    {
        byte[] answer = Encoding.UTF8.GetBytes($"{Answer}{_name}|{_port}");
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var message = await _udp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                if (Encoding.UTF8.GetString(message.Buffer) == Question)
                {
                    await _udp.SendAsync(answer, message.RemoteEndPoint, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Pregunta por la red y devuelve los equipos que contestan en <paramref name="wait"/>.</summary>
    public static async Task<List<PeerInfo>> SearchAsync(TimeSpan wait, int discoveryPort = PeerProtocol.DiscoveryPort, CancellationToken cancellationToken = default)
    {
        var found = new List<PeerInfo>();
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        byte[] question = Encoding.UTF8.GetBytes(Question);
        await udp.SendAsync(question, new IPEndPoint(IPAddress.Broadcast, discoveryPort), cancellationToken).ConfigureAwait(false);
        await udp.SendAsync(question, new IPEndPoint(IPAddress.Loopback, discoveryPort), cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                var message = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                string text = Encoding.UTF8.GetString(message.Buffer);
                int bar = text.LastIndexOf('|');
                if (text.StartsWith(Answer, StringComparison.Ordinal) && bar > Answer.Length && int.TryParse(text[(bar + 1)..], out int port))
                {
                    var peer = new PeerInfo(text[Answer.Length..bar], message.RemoteEndPoint.Address.ToString(), port);
                    if (!found.Any(p => p.Name == peer.Name && p.Port == peer.Port))
                    {
                        found.Add(peer);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return found;
    }
}
