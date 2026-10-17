using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace MegaCopier.Engine.Network;

public enum SmtpSecurity
{
    /// <summary>Puerto 587: conexión normal que pasa a cifrada con STARTTLS (Gmail, Outlook, la mayoría).</summary>
    StartTls,

    /// <summary>Puerto 465: cifrada desde el principio.</summary>
    Tls,

    /// <summary>Sin cifrar (solo servidores de la red local).</summary>
    None,
}

/// <summary>Cómo enviar correos (la contraseña aparte, del Administrador de credenciales).</summary>
public sealed record SmtpSettings(string Server, int Port, SmtpSecurity Security, string User, string Password, string From, IReadOnlyList<string> To);

/// <summary>
/// Cliente SMTP mínimo para los avisos por correo: STARTTLS (587), TLS directo (465) o sin cifrar; AUTH PLAIN o
/// LOGIN; texto en UTF-8 (base64) para que lleguen bien acentos y cualquier idioma. SmtpClient de .NET está
/// desaconsejado y no admite el puerto 465.
/// </summary>
public static class SmtpSender
{
    public static async Task SendAsync(SmtpSettings settings, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (settings.To.Count == 0 || settings.From.Length == 0)
        {
            throw new ArgumentException("Falta el remitente o el destinatario.");
        }

        int port = settings.Port > 0 ? settings.Port : settings.Security switch { SmtpSecurity.Tls => 465, SmtpSecurity.None => 25, _ => 587 };
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await client.ConnectAsync(settings.Server, port, timeout.Token).ConfigureAwait(false);
        Stream stream = client.GetStream();
        if (settings.Security == SmtpSecurity.Tls)
        {
            stream = await SecureAsync(stream, settings.Server, timeout.Token).ConfigureAwait(false);
        }

        var session = new Session(stream);
        await session.ExpectAsync(220, timeout.Token).ConfigureAwait(false);
        string domain = Environment.MachineName;
        var ehlo = await session.CommandAsync("EHLO " + domain, 250, timeout.Token).ConfigureAwait(false);
        if (settings.Security == SmtpSecurity.StartTls)
        {
            await session.CommandAsync("STARTTLS", 220, timeout.Token).ConfigureAwait(false);
            session = new Session(await SecureAsync(stream, settings.Server, timeout.Token).ConfigureAwait(false));
            ehlo = await session.CommandAsync("EHLO " + domain, 250, timeout.Token).ConfigureAwait(false);
        }

        if (settings.User.Length > 0)
        {
            if (ehlo.Contains("PLAIN", StringComparison.OrdinalIgnoreCase))
            {
                string plain = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{settings.User}\0{settings.Password}"));
                await session.CommandAsync("AUTH PLAIN " + plain, 235, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                await session.CommandAsync("AUTH LOGIN", 334, timeout.Token).ConfigureAwait(false);
                await session.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.User)), 334, timeout.Token).ConfigureAwait(false);
                await session.CommandAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Password)), 235, timeout.Token).ConfigureAwait(false);
            }
        }

        await session.CommandAsync($"MAIL FROM:<{settings.From}>", 250, timeout.Token).ConfigureAwait(false);
        foreach (string to in settings.To)
        {
            await session.CommandAsync($"RCPT TO:<{to}>", 250, timeout.Token, 251).ConfigureAwait(false);
        }

        await session.CommandAsync("DATA", 354, timeout.Token).ConfigureAwait(false);
        await session.WriteAsync(Message(settings, subject, body), timeout.Token).ConfigureAwait(false);
        await session.ExpectAsync(250, timeout.Token).ConfigureAwait(false);
        try
        {
            await session.CommandAsync("QUIT", 221, timeout.Token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // El correo ya se aceptó: que el servidor cierre de mala manera no importa.
        }
    }

    /// <summary>El mensaje completo en MIME, terminado en "\r\n.\r\n" y con los puntos de principio de línea doblados.</summary>
    internal static string Message(SmtpSettings settings, string subject, string body)
    {
        var text = new StringBuilder();
        text.Append("From: MegaCopier <").Append(settings.From).Append(">\r\n");
        text.Append("To: ").Append(string.Join(", ", settings.To.Select(t => $"<{t}>"))).Append("\r\n");
        text.Append("Subject: =?UTF-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))).Append("?=\r\n");
        text.Append("Date: ").Append(DateTimeOffset.Now.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture))
            .Append(DateTimeOffset.Now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "", StringComparison.Ordinal)).Append("\r\n");
        text.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append("@megacopier>\r\n");
        text.Append("MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n").Replace("\n", "\r\n")));
        for (int i = 0; i < encoded.Length; i += 76)
        {
            text.Append(encoded.AsSpan(i, Math.Min(76, encoded.Length - i))).Append("\r\n");
        }

        // (en base64 no hay puntos al principio de línea, pero las cabeceras son de quien configura: se doblan por si acaso)
        return text.ToString().Replace("\r\n.", "\r\n..", StringComparison.Ordinal) + ".\r\n";
    }

    private static async Task<Stream> SecureAsync(Stream stream, string host, CancellationToken cancellationToken)
    {
        var tls = new SslStream(stream, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken).ConfigureAwait(false);
        return tls;
    }

    private sealed class Session(Stream stream)
    {
        public async Task<string> CommandAsync(string command, int expected, CancellationToken cancellationToken, int alsoOk = 0)
        {
            await WriteAsync(command + "\r\n", cancellationToken).ConfigureAwait(false);
            return await ExpectAsync(expected, cancellationToken, alsoOk).ConfigureAwait(false);
        }

        public async Task WriteAsync(string text, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Lee una respuesta (de una o varias líneas "250-...") y falla si el código no es el esperado.</summary>
        public async Task<string> ExpectAsync(int expected, CancellationToken cancellationToken, int alsoOk = 0)
        {
            var all = new StringBuilder();
            int code;
            string line;
            do
            {
                line = await ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("El servidor de correo cerró la conexión.");
                all.Append(line).Append('\n');
                code = line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), CultureInfo.InvariantCulture, out int c) ? c : 0;
            }
            while (line.Length > 3 && line[3] == '-');

            if (code != expected && (alsoOk == 0 || code != alsoOk))
            {
                string message = $"SMTP {all.ToString().Trim()}";
                throw code is 530 or 535 or 534 ? new UnauthorizedAccessException(message) : new IOException(message);
            }

            return all.ToString();
        }

        private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            byte[] one = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
                {
                    return bytes.Count == 0 ? null : Encoding.UTF8.GetString([.. bytes]);
                }

                if (one[0] == '\n')
                {
                    return Encoding.UTF8.GetString([.. bytes]).TrimEnd('\r');
                }

                bytes.Add(one[0]);
            }
        }
    }
}
