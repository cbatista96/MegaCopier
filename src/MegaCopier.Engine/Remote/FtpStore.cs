using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// Cliente FTP / FTPS (TLS explícito) mínimo y directo: una conexión de control para todo el trabajo (FtpWebRequest
/// está obsoleto y abre una por operación). Datos en modo pasivo (EPSV y, si no, PASV con la IP de la conexión de
/// control: funciona detrás de NAT). Lista con MLSD y, si el servidor no lo tiene, con LIST (Unix o Windows/IIS).
/// Los ids son rutas absolutas del servidor ("/fotos/2025/a.jpg"); las carpetas terminan en "/".
/// </summary>
internal sealed partial class FtpStore : IRemoteStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Connection _connection;
    private readonly string _password;
    private readonly bool _secure;
    private readonly Lock _gate = new();
    private TcpClient? _control;
    private Stream? _stream;
    private string _root = "/";
    private bool _mlsd = true;

    public FtpStore(Connection connection, string password)
    {
        _connection = connection;
        _password = password;
        _secure = connection.Kind == ConnectionKind.Ftps;
        Connect();
    }

    public string RootId => _root;

    private void Connect()
    {
        var host = _connection.Host.Trim();
        int port = _connection.Port > 0 ? _connection.Port : 21;
        _control = new TcpClient { ReceiveTimeout = (int)Timeout.TotalMilliseconds, SendTimeout = (int)Timeout.TotalMilliseconds };
        try
        {
            _control.Connect(host, port);
        }
        catch (SocketException ex)
        {
            throw new IOException($"{host}:{port}: {ex.Message}", ex);
        }

        _stream = _control.GetStream();
        Expect(Reply(), 220);
        if (_secure)
        {
            Expect(Command("AUTH TLS"), 234);
            var tls = new SslStream(_stream, leaveInnerStreamOpen: false, Validate);
            tls.AuthenticateAsClient(host);
            _stream = tls;
            Command("PBSZ 0");
            Expect(Command("PROT P"), 200);
        }

        var user = Command("USER " + (_connection.User.Length > 0 ? _connection.User : "anonymous"));
        if (user.Code == 331)
        {
            user = Command("PASS " + _password);
        }

        if (user.Code is 530)
        {
            throw new UnauthorizedAccessException(user.Text);
        }

        Expect(user, 230, 202);
        Command("OPTS UTF8 ON");
        Expect(Command("TYPE I"), 200);

        // La raíz: la carpeta indicada (absoluta, o relativa a la de inicio de sesión).
        var pwd = Command("PWD");
        string home = pwd.Code == 257 && Regex.Match(pwd.Text, "\"(.*)\"") is { Success: true } m ? m.Groups[1].Value.Replace("\"\"", "\"") : "/";
        string root = _connection.Root.Trim().Replace('\\', '/');
        _root = Folder(root.StartsWith('/') ? root : Combine(home, root));
    }

    private bool Validate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate, System.Security.Cryptography.X509Certificates.X509Chain? chain, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None || _connection.AcceptAnyCertificate;

    private static string Folder(string path) => (path.StartsWith('/') ? path : "/" + path).TrimEnd('/') + "/";

    private static string Combine(string folder, string name) => folder.TrimEnd('/') + "/" + name;

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        lock (_gate)
        {
            string folder = Folder(parentId);
            var lines = new List<string>();
            if (_mlsd)
            {
                if (ReadListing("MLSD " + folder, lines) is { } reply && reply.Code is 500 or 501 or 502)
                {
                    _mlsd = false; // el servidor no tiene MLSD: LIST de toda la vida
                }
                else
                {
                    return [.. lines.Select(l => ParseMlsd(folder, l)).OfType<RemoteItem>()];
                }
            }

            lines.Clear();
            var list = ReadListing("LIST -a " + folder, lines);
            if (list is { Code: >= 500 })
            {
                lines.Clear();
                Expect(ReadListing("LIST " + folder, lines) ?? new FtpReply(226, ""), 226, 250);
            }

            return [.. lines.Select(l => ParseList(folder, l)).OfType<RemoteItem>()];
        }
    }

    /// <summary>Lee un listado por la conexión de datos. Devuelve la respuesta si el servidor lo rechazó, si no null.</summary>
    private FtpReply? ReadListing(string command, List<string> lines)
    {
        using var data = OpenData(command, out var refused);
        if (data is null)
        {
            return refused;
        }

        using (var reader = new StreamReader(data, Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }

        Expect(Reply(), 226, 250);
        return null;
    }

    private static RemoteItem? ParseMlsd(string folder, string line)
    {
        int space = line.IndexOf(' ');
        if (space < 0)
        {
            return null;
        }

        string name = line[(space + 1)..];
        var facts = line[..space].Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Split('=', 2)).Where(f => f.Length == 2)
            .ToDictionary(f => f[0].ToLowerInvariant(), f => f[1], StringComparer.Ordinal);
        string type = facts.GetValueOrDefault("type", "file").ToLowerInvariant();
        if (type is "cdir" or "pdir" || name is "." or "..")
        {
            return null;
        }

        bool isFolder = type == "dir";
        long size = long.TryParse(facts.GetValueOrDefault("size"), CultureInfo.InvariantCulture, out long s) ? s : 0;
        DateTime? modified = DateTime.TryParseExact(facts.GetValueOrDefault("modify")?.Split('.')[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
        return new RemoteItem(Combine(folder, name) + (isFolder ? "/" : ""), name, isFolder, size, modified);
    }

    // drwxr-xr-x 1 owner group 4096 Jan 01 12:00 nombre   |   01-01-25  12:00PM  <DIR>  nombre
    [GeneratedRegex(@"^([\-dl])[rwxsStT\-]{9}\S*\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\w{3}\s+\d{1,2}\s+(?:\d{1,2}:\d{2}|\d{4}))\s+(.+)$")]
    private static partial Regex UnixLine();

    [GeneratedRegex(@"^(\d{2}-\d{2}-\d{2,4})\s+(\d{1,2}:\d{2}[AP]M)\s+(<DIR>|\d+)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DosLine();

    private static RemoteItem? ParseList(string folder, string line)
    {
        if (UnixLine().Match(line) is { Success: true } unix)
        {
            string name = unix.Groups[4].Value;
            if (unix.Groups[1].Value == "l")
            {
                name = name.Split(" -> ")[0]; // un enlace: se trata como archivo con su nombre
            }

            if (name is "." or "..")
            {
                return null;
            }

            bool isFolder = unix.Groups[1].Value == "d";
            long size = long.Parse(unix.Groups[2].Value, CultureInfo.InvariantCulture);
            string stamp = Regex.Replace(unix.Groups[3].Value, @"\s+", " ");
            DateTime? modified = DateTime.TryParseExact(stamp, ["MMM d HH:mm", "MMM d yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
                ? (date > DateTime.UtcNow.AddDays(1) ? date.AddYears(-1) : date)
                : null;
            return new RemoteItem(Combine(folder, name) + (isFolder ? "/" : ""), name, isFolder, isFolder ? 0 : size, modified);
        }

        if (DosLine().Match(line) is { Success: true } dos)
        {
            string name = dos.Groups[4].Value;
            bool isFolder = dos.Groups[3].Value.Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
            long size = isFolder ? 0 : long.Parse(dos.Groups[3].Value, CultureInfo.InvariantCulture);
            DateTime? modified = DateTime.TryParseExact($"{dos.Groups[1].Value} {dos.Groups[2].Value}", ["MM-dd-yy hh:mmtt", "MM-dd-yyyy hh:mmtt"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
            return new RemoteItem(Combine(folder, name) + (isFolder ? "/" : ""), name, isFolder, size, modified);
        }

        return null;
    }

    public Stream OpenRead(string objectId)
    {
        _gate.Enter(); // se suelta al cerrar el flujo: nadie más usa la conexión mientras tanto
        try
        {
            var data = OpenData("RETR " + objectId, out var refused) ?? throw Failure(refused!.Value);
            return new FtpTransfer(data, this, write: false);
        }
        catch
        {
            _gate.Exit();
            throw;
        }
    }

    public string Upload(string parentId, string name, Stream source, long length, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            string id = Combine(Folder(parentId), name);
            using (var data = OpenData("STOR " + id, out var refused) ?? throw Failure(refused!.Value))
            {
                byte[] buffer = new byte[1 << 20];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    data.Write(buffer, 0, read);
                }
            }

            Expect(Reply(), 226, 250);
            return id;
        }
    }

    public string CreateFolder(string parentId, string name)
    {
        lock (_gate)
        {
            string id = Combine(Folder(parentId), name);
            var reply = Command("MKD " + id);
            if (reply.Code != 257 && reply.Code != 550) // 550: casi siempre "ya existe"
            {
                throw Failure(reply);
            }

            return id + "/";
        }
    }

    public void Delete(string objectId, bool recursive)
    {
        if (objectId.EndsWith('/'))
        {
            if (recursive)
            {
                foreach (var child in Children(objectId))
                {
                    Delete(child.Id, recursive: true);
                }
            }

            lock (_gate)
            {
                Expect(Command("RMD " + objectId.TrimEnd('/')), 250);
            }
        }
        else
        {
            lock (_gate)
            {
                Expect(Command("DELE " + objectId), 250);
            }
        }
    }

    // --- Conexión de datos (pasiva) ---

    /// <summary>Abre la conexión de datos y envía la orden. Null (con la respuesta) si el servidor la rechaza.</summary>
    private Stream? OpenData(string command, out FtpReply? refused)
    {
        refused = null;
        var remote = ((IPEndPoint)_control!.Client.RemoteEndPoint!).Address;
        int port;
        var epsv = Command("EPSV");
        if (epsv.Code == 229 && Regex.Match(epsv.Text, @"\(\|\|\|(\d+)\|\)") is { Success: true } e)
        {
            port = int.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var pasv = Command("PASV");
            var p = Regex.Match(pasv.Text, @"(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)");
            if (pasv.Code != 227 || !p.Success)
            {
                throw Failure(pasv);
            }

            port = int.Parse(p.Groups[5].Value, CultureInfo.InvariantCulture) * 256 + int.Parse(p.Groups[6].Value, CultureInfo.InvariantCulture);
        }

        var data = new TcpClient { ReceiveTimeout = (int)Timeout.TotalMilliseconds, SendTimeout = (int)Timeout.TotalMilliseconds };
        data.Connect(remote, port);
        var reply = Command(command);
        if (reply.Code is not (125 or 150))
        {
            data.Dispose();
            refused = reply;
            return null;
        }

        Stream stream = data.GetStream();
        if (_secure)
        {
            var tls = new SslStream(stream, leaveInnerStreamOpen: false, Validate);
            tls.AuthenticateAsClient(_connection.Host.Trim());
            stream = tls;
        }

        return new OwnedStream2(stream, data);
    }

    /// <summary>Al cerrar una descarga: leer la respuesta final (226) y soltar la conexión de control.</summary>
    private void EndTransfer()
    {
        try
        {
            Expect(Reply(), 226, 250);
        }
        finally
        {
            _gate.Exit();
        }
    }

    // --- Órdenes y respuestas ---

    private readonly record struct FtpReply(int Code, string Text);

    private FtpReply Command(string command)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(command + "\r\n");
        _stream!.Write(bytes);
        _stream.Flush();
        return Reply();
    }

    private FtpReply Reply()
    {
        string first = ReadLine() ?? throw new IOException("El servidor FTP cerró la conexión.");
        if (first.Length < 3 || !int.TryParse(first.AsSpan(0, 3), CultureInfo.InvariantCulture, out int code))
        {
            throw new IOException("Respuesta FTP no válida: " + first);
        }

        var text = new StringBuilder(first.Length > 4 ? first[4..] : "");
        if (first.Length > 3 && first[3] == '-')
        {
            // Respuesta de varias líneas: hasta "NNN " con el mismo código.
            while (ReadLine() is { } line)
            {
                text.Append('\n').Append(line);
                if (line.Length >= 4 && line.StartsWith(first[..3], StringComparison.Ordinal) && line[3] == ' ')
                {
                    break;
                }
            }
        }

        return new FtpReply(code, text.ToString());
    }

    private string? ReadLine()
    {
        var bytes = new List<byte>();
        Span<byte> one = stackalloc byte[1];
        while (true)
        {
            int read = _stream!.Read(one);
            if (read == 0)
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

    private static void Expect(FtpReply reply, params int[] codes)
    {
        if (!codes.Contains(reply.Code))
        {
            throw Failure(reply);
        }
    }

    private static Exception Failure(FtpReply reply) =>
        reply.Code is 530 or 532 ? new UnauthorizedAccessException($"FTP {reply.Code}: {reply.Text}") : new IOException($"FTP {reply.Code}: {reply.Text}");

    public void Dispose()
    {
        try
        {
            if (_stream is not null)
            {
                _stream.Write("QUIT\r\n"u8);
            }
        }
        catch (IOException)
        {
        }

        _stream?.Dispose();
        _control?.Dispose();
    }

    /// <summary>Una descarga en curso: al cerrarla se lee el "226" y se libera la conexión de control.</summary>
    private sealed class FtpTransfer(Stream data, FtpStore owner, bool write) : Stream
    {
        private bool _closed;

        public override bool CanRead => !write;

        public override bool CanSeek => false;

        public override bool CanWrite => write;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => data.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) => data.Write(buffer, offset, count);

        public override void Flush() => data.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                data.Dispose();
                owner.EndTransfer();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>El flujo de datos y su conexión TCP, que se cierran juntos.</summary>
    private sealed class OwnedStream2(Stream inner, TcpClient client) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (inner is SslStream tls && tls.CanWrite)
                {
                    try
                    {
                        tls.ShutdownAsync().GetAwaiter().GetResult(); // close_notify: sin él, algunos servidores FTPS descartan la subida
                    }
                    catch (IOException)
                    {
                    }
                }

                inner.Dispose();
                client.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
