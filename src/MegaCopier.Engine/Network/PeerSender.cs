using System.Globalization;
using System.IO.Hashing;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace MegaCopier.Engine.Network;

/// <summary>Qué pasó con un archivo enviado.</summary>
public enum PeerFileResult
{
    Sent,
    AlreadyThere,
    Corrupted,
}

/// <summary>Conexión con el receptor de otro equipo (ver <see cref="PeerProtocol"/>).</summary>
public sealed class PeerSender : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly SslStream _ssl;

    private PeerSender(TcpClient client, SslStream ssl)
    {
        _client = client;
        _ssl = ssl;
    }

    /// <summary>Conecta y se identifican mutuamente con el código. Lanza UnauthorizedAccessException si el código no vale.</summary>
    public static async Task<PeerSender> ConnectAsync(string host, int port, string code, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

            // El certificado del receptor es autofirmado: lo que lo hace de confianza es el código (abajo).
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "MegaCopier",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, cancellationToken).ConfigureAwait(false);

            byte[] certificateHash = SHA256.HashData(ssl.RemoteCertificate!.GetRawCertData());
            await PeerProtocol.WriteMessageAsync(ssl, PeerProtocol.Magic, cancellationToken).ConfigureAwait(false);
            byte[] challenge = await PeerProtocol.ReadBytesAsync(ssl, cancellationToken).ConfigureAwait(false);
            await PeerProtocol.WriteBytesAsync(ssl, PeerProtocol.Proof(code, challenge, certificateHash, "client"), cancellationToken).ConfigureAwait(false);
            if (await PeerProtocol.ReadMessageAsync(ssl, cancellationToken).ConfigureAwait(false) != "OK")
            {
                throw new UnauthorizedAccessException(Texts.Get("E_WrongCode"));
            }

            byte[] proof = await PeerProtocol.ReadBytesAsync(ssl, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(proof, PeerProtocol.Proof(code, challenge, certificateHash, "server")))
            {
                throw new UnauthorizedAccessException(Texts.Get("E_PeerUnknownCode"));
            }

            return new PeerSender(client, ssl);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task HelloAsync(string name, int files, long bytes, CancellationToken cancellationToken) =>
        PeerProtocol.WriteMessageAsync(_ssl, $"{name}\t{files}\t{bytes}", cancellationToken);

    /// <summary>Envía un archivo (desde donde se quedó, si ya llegó una parte). <paramref name="sent"/> recibe cada bloque.</summary>
    public async Task<PeerFileResult> SendFileAsync(string path, string relative, Func<int, CancellationToken, Task> sent, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        await PeerProtocol.WriteMessageAsync(
            _ssl,
            $"FILE\t{relative}\t{info.Length.ToString(CultureInfo.InvariantCulture)}\t{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}\t{((int)info.Attributes).ToString(CultureInfo.InvariantCulture)}",
            cancellationToken).ConfigureAwait(false);
        string reply = await PeerProtocol.ReadMessageAsync(_ssl, cancellationToken).ConfigureAwait(false);
        if (reply == "SKIP")
        {
            return PeerFileResult.AlreadyThere;
        }

        long offset = reply.StartsWith("OFFSET ", StringComparison.Ordinal) ? long.Parse(reply[7..], CultureInfo.InvariantCulture) : 0;
        var hash = new XxHash3();
        byte[] buffer = new byte[1 << 20];
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
        {
            for (long done = 0; done < info.Length;)
            {
                int count = (int)Math.Min(buffer.Length, info.Length - done);
                await file.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                hash.Append(buffer.AsSpan(0, count));
                if (done >= offset)
                {
                    await _ssl.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    await sent(count, cancellationToken).ConfigureAwait(false);
                }

                done += count;
            }
        }

        await PeerProtocol.WriteMessageAsync(_ssl, PeerProtocol.Hash(hash), cancellationToken).ConfigureAwait(false);
        return await PeerProtocol.ReadMessageAsync(_ssl, cancellationToken).ConfigureAwait(false) == "OK" ? PeerFileResult.Sent : PeerFileResult.Corrupted;
    }

    public Task EndAsync(CancellationToken cancellationToken) => PeerProtocol.WriteMessageAsync(_ssl, "END", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _ssl.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
