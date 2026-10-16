using System.Globalization;
using System.IO.Hashing;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MegaCopier.Engine.Network;

/// <summary>Lo que pasa en el receptor (para avisos y el registro).</summary>
public sealed record PeerEvent(PeerEventKind Kind, string Peer, int Files = 0, long Bytes = 0, string? Detail = null);

public enum PeerEventKind
{
    Started,
    Finished,
    Rejected,
    Failed,
}

/// <summary>Recibe archivos de otros equipos con MegaCopier en la carpeta elegida (ver <see cref="PeerProtocol"/>).</summary>
public sealed class PeerReceiver : IDisposable
{
    private readonly string _folder;
    private readonly string _code;
    private readonly X509Certificate2 _certificate;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();

    public PeerReceiver(string folder, string code, X509Certificate2 certificate, int port = PeerProtocol.DefaultPort, bool loopbackOnly = false)
    {
        _folder = Path.GetFullPath(folder);
        _code = code;
        _certificate = certificate;
        if (loopbackOnly)
        {
            _listener = new TcpListener(IPAddress.Loopback, port); // pruebas: solo este equipo (sin aviso del cortafuegos)
        }
        else
        {
            _listener = new TcpListener(IPAddress.IPv6Any, port);
            _listener.Server.DualMode = true; // IPv4 e IPv6
        }
    }

    public event Action<PeerEvent>? Event;

    /// <summary>Puerto en el que escucha (útil con el puerto 0 en las pruebas).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        string peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, _stop.Token).ConfigureAwait(false);

                if (await PeerProtocol.ReadMessageAsync(ssl, _stop.Token).ConfigureAwait(false) != PeerProtocol.Magic)
                {
                    return;
                }

                // El otro lado demuestra que conoce el código (sin enviarlo), ligado a este certificado.
                byte[] challenge = RandomNumberGenerator.GetBytes(32);
                byte[] certificateHash = SHA256.HashData(_certificate.RawData);
                await PeerProtocol.WriteBytesAsync(ssl, challenge, _stop.Token).ConfigureAwait(false);
                byte[] proof = await PeerProtocol.ReadBytesAsync(ssl, _stop.Token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(proof, PeerProtocol.Proof(_code, challenge, certificateHash, "client")))
                {
                    await PeerProtocol.WriteMessageAsync(ssl, "ERR code", _stop.Token).ConfigureAwait(false);
                    Event?.Invoke(new PeerEvent(PeerEventKind.Rejected, peer));
                    return;
                }

                await PeerProtocol.WriteMessageAsync(ssl, "OK", _stop.Token).ConfigureAwait(false);
                await PeerProtocol.WriteBytesAsync(ssl, PeerProtocol.Proof(_code, challenge, certificateHash, "server"), _stop.Token).ConfigureAwait(false);

                string[] hello = (await PeerProtocol.ReadMessageAsync(ssl, _stop.Token).ConfigureAwait(false)).Split('\t');
                string name = hello.Length > 0 ? hello[0] : peer;
                int files = hello.Length > 1 && int.TryParse(hello[1], out int f) ? f : 0;
                long bytes = hello.Length > 2 && long.TryParse(hello[2], out long b) ? b : 0;
                Event?.Invoke(new PeerEvent(PeerEventKind.Started, name, files, bytes));

                int received = 0;
                long receivedBytes = 0;
                while (true)
                {
                    string header = await PeerProtocol.ReadMessageAsync(ssl, _stop.Token).ConfigureAwait(false);
                    if (header == "END")
                    {
                        break;
                    }

                    long length = await ReceiveFileAsync(ssl, header, _stop.Token).ConfigureAwait(false);
                    if (length >= 0)
                    {
                        received++;
                        receivedBytes += length;
                    }
                }

                Event?.Invoke(new PeerEvent(PeerEventKind.Finished, name, received, receivedBytes));
            }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or FormatException)
            {
                Event?.Invoke(new PeerEvent(PeerEventKind.Failed, peer, Detail: ex.Message));
            }
        }
    }

    /// <summary>Un archivo: "FILE ruta tamaño fecha atributos". Devuelve los bytes recibidos, o -1 si se omitió.</summary>
    private async Task<long> ReceiveFileAsync(SslStream ssl, string header, CancellationToken cancellationToken)
    {
        string[] parts = header.Split('\t');
        if (parts.Length < 5 || parts[0] != "FILE" || PeerProtocol.SafeRelative(parts[1]) is not { } relative)
        {
            throw new IOException("Cabecera de archivo no válida.");
        }

        long length = long.Parse(parts[2], CultureInfo.InvariantCulture);
        var lastWrite = new DateTime(long.Parse(parts[3], CultureInfo.InvariantCulture), DateTimeKind.Utc);
        var attributes = (FileAttributes)int.Parse(parts[4], CultureInfo.InvariantCulture);
        string target = Path.Combine(_folder, relative);
        string partial = target + FileTransfer.PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var existing = new FileInfo(target);
        if (existing.Exists && existing.Length == length && Math.Abs((existing.LastWriteTimeUtc - lastWrite).TotalSeconds) <= 2)
        {
            await PeerProtocol.WriteMessageAsync(ssl, "SKIP", cancellationToken).ConfigureAwait(false);
            return -1;
        }

        // Lo que ya llegó en un intento anterior (en bloques de 1 MB): se sigue desde ahí.
        long offset = File.Exists(partial) ? Math.Min(new FileInfo(partial).Length, length) / (1 << 20) * (1 << 20) : 0;
        await PeerProtocol.WriteMessageAsync(ssl, $"OFFSET {offset}", cancellationToken).ConfigureAwait(false);

        var hash = new XxHash3();
        byte[] buffer = new byte[1 << 20];
        using (var file = new FileStream(partial, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1))
        {
            // La parte que ya estaba entra en el hash leyéndola del disco.
            for (long done = 0; done < offset;)
            {
                int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, offset - done)), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("El archivo parcial cambió.");
                }

                hash.Append(buffer.AsSpan(0, read));
                done += read;
            }

            file.SetLength(offset);
            file.Position = offset;
            for (long done = offset; done < length;)
            {
                int count = (int)Math.Min(buffer.Length, length - done);
                await ssl.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                hash.Append(buffer.AsSpan(0, count));
                await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                done += count;
            }

            file.Flush(flushToDisk: true);
        }

        string expected = await PeerProtocol.ReadMessageAsync(ssl, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expected, PeerProtocol.Hash(hash), StringComparison.Ordinal))
        {
            File.Delete(partial);
            await PeerProtocol.WriteMessageAsync(ssl, "BAD", cancellationToken).ConfigureAwait(false);
            return -1;
        }

        File.SetLastWriteTimeUtc(partial, lastWrite);
        File.Move(partial, target, overwrite: true);
        var kept = attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive);
        File.SetAttributes(target, kept == 0 ? FileAttributes.Normal : kept);
        await PeerProtocol.WriteMessageAsync(ssl, "OK", cancellationToken).ConfigureAwait(false);
        return length - offset;
    }
}
