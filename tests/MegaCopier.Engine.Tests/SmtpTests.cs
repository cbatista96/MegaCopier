using System.Net;
using System.Net.Sockets;
using System.Text;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine.Tests;

/// <summary>Un servidor SMTP de prueba (sin cifrar) que guarda lo que recibe.</summary>
internal sealed class FakeSmtp : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public FakeSmtp(bool plain)
    {
        _listener.Start();
        _ = Task.Run(() => Serve(plain));
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = [];

    public string Data { get; private set; } = "";

    public TaskCompletionSource Done { get; } = new();

    private void Serve(bool plain)
    {
        using var client = _listener.AcceptTcpClient();
        var stream = client.GetStream();
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        writer.WriteLine("220 smtp de prueba");
        while (reader.ReadLine() is { } line)
        {
            Commands.Add(line);
            if (line.StartsWith("EHLO", StringComparison.Ordinal))
            {
                writer.WriteLine("250-hola");
                writer.WriteLine(plain ? "250 AUTH PLAIN LOGIN" : "250 AUTH LOGIN");
            }
            else if (line.StartsWith("AUTH PLAIN ", StringComparison.Ordinal))
            {
                writer.WriteLine(Encoding.UTF8.GetString(Convert.FromBase64String(line[11..])) == "\0ana\0clave ñ" ? "235 Dentro" : "535 No");
            }
            else if (line == "AUTH LOGIN")
            {
                writer.WriteLine("334 VXNlcm5hbWU6");
                Commands.Add(Encoding.UTF8.GetString(Convert.FromBase64String(reader.ReadLine()!)));
                writer.WriteLine("334 UGFzc3dvcmQ6");
                writer.WriteLine(Encoding.UTF8.GetString(Convert.FromBase64String(reader.ReadLine()!)) == "clave ñ" ? "235 Dentro" : "535 No");
            }
            else if (line == "DATA")
            {
                writer.WriteLine("354 Adelante");
                var data = new StringBuilder();
                while (reader.ReadLine() is { } dataLine && dataLine != ".")
                {
                    data.Append(dataLine).Append('\n');
                }

                Data = data.ToString();
                writer.WriteLine("250 Recibido");
            }
            else if (line == "QUIT")
            {
                writer.WriteLine("221 Adiós");
                break;
            }
            else
            {
                writer.WriteLine("250 Vale");
            }
        }

        Done.TrySetResult();
    }

    public void Dispose() => _listener.Stop();
}

public sealed class SmtpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendsAnAuthenticatedUtf8Message(bool plain)
    {
        using var server = new FakeSmtp(plain);
        var settings = new SmtpSettings("127.0.0.1", server.Port, SmtpSecurity.None, "ana", "clave ñ", "megacopier@casa.es", ["ana@casa.es", "luis@casa.es"]);

        await SmtpSender.SendAsync(settings, "✔ Copiado «Fotos de año»", "Línea uno\n.empieza con punto\n漢字");
        await server.Done.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("MAIL FROM:<megacopier@casa.es>", server.Commands);
        Assert.Contains("RCPT TO:<ana@casa.es>", server.Commands);
        Assert.Contains("RCPT TO:<luis@casa.es>", server.Commands);
        string subject = server.Data.Split('\n').First(l => l.StartsWith("Subject:", StringComparison.Ordinal));
        Assert.Equal("✔ Copiado «Fotos de año»", Encoding.UTF8.GetString(Convert.FromBase64String(subject["Subject: =?UTF-8?B?".Length..^2])));
        string body = string.Concat(server.Data.Split("\n\n", 2)[1].Split('\n'));
        Assert.Equal("Línea uno\r\n.empieza con punto\r\n漢字", Encoding.UTF8.GetString(Convert.FromBase64String(body)));
    }

    [Fact]
    public async Task AWrongPasswordIsAccessDenied()
    {
        using var server = new FakeSmtp(plain: true);
        var settings = new SmtpSettings("127.0.0.1", server.Port, SmtpSecurity.None, "ana", "mala", "a@b.es", ["c@d.es"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SmtpSender.SendAsync(settings, "x", "y"));
    }
}
