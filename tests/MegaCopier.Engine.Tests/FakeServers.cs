using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MegaCopier.Engine.Network;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine.Tests;

/// <summary>Respuesta de los servidores HTTP de prueba.</summary>
internal sealed record FakeResponse(int Status, byte[]? Body = null, Dictionary<string, string>? Headers = null);

/// <summary>Un servidor HTTP mínimo en 127.0.0.1 (con el lector de peticiones del motor) para probar clientes.</summary>
internal sealed class FakeHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<HttpRequest, byte[], FakeResponse> _handler;
    private readonly CancellationTokenSource _stop = new();

    public FakeHttpServer(Func<HttpRequest, byte[], FakeResponse> handler)
    {
        _handler = handler;
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Requests { get; } = [];

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
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
            var stream = new BufferedStream(client.GetStream());
            try
            {
                while (await HttpRequest.ReadAsync(stream, _stop.Token) is { } request)
                {
                    byte[] body = await request.ReadBodyAsync(stream, 512 << 20, _stop.Token);
                    lock (Requests)
                    {
                        Requests.Add($"{request.Method} {request.Path}?{request.QueryString}");
                    }

                    var response = _handler(request, body);
                    var head = new StringBuilder($"HTTP/1.1 {response.Status} X\r\nContent-Length: {response.Body?.Length ?? 0}\r\n");
                    foreach (var (name, value) in response.Headers ?? [])
                    {
                        head.Append(name).Append(": ").Append(value).Append("\r\n");
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head.Append("\r\n").ToString()));
                    if (response.Body is { Length: > 0 })
                    {
                        await stream.WriteAsync(response.Body);
                    }

                    await stream.FlushAsync();
                }
            }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

/// <summary>WebDAV de prueba sobre una carpeta: PROPFIND (profundidad 1), GET, PUT, MKCOL y DELETE, con autenticación básica.</summary>
internal sealed class FakeWebDav : IDisposable
{
    private readonly string _root;
    private readonly FakeHttpServer _server;

    public FakeWebDav(string root, string user, string password)
    {
        _root = root;
        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        _server = new FakeHttpServer((request, body) =>
            request.Headers.GetValueOrDefault("Authorization") != expected ? new FakeResponse(401) : Handle(request, body));
    }

    public string Url => $"http://127.0.0.1:{_server.Port}/dav/";

    private string Local(string path) => Path.Combine(_root, Uri.UnescapeDataString(path)["/dav/".Length..].Replace('/', Path.DirectorySeparatorChar));

    private FakeResponse Handle(HttpRequest request, byte[] body)
    {
        string local = Local(request.Path);
        switch (request.Method)
        {
            case "PROPFIND":
                if (!Directory.Exists(local) && !File.Exists(local))
                {
                    return new FakeResponse(404);
                }

                var xml = new StringBuilder("<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\">");
                Entry(xml, request.Path, local);
                if (Directory.Exists(local) && request.Headers.GetValueOrDefault("Depth") == "1")
                {
                    foreach (string child in Directory.EnumerateFileSystemEntries(local))
                    {
                        Entry(xml, request.Path.TrimEnd('/') + "/" + Uri.EscapeDataString(Path.GetFileName(child)) + (Directory.Exists(child) ? "/" : ""), child);
                    }
                }

                return new FakeResponse(207, Encoding.UTF8.GetBytes(xml.Append("</d:multistatus>").ToString()));
            case "GET":
                return File.Exists(local) ? new FakeResponse(200, File.ReadAllBytes(local)) : new FakeResponse(404);
            case "PUT":
                File.WriteAllBytes(local, body);
                return new FakeResponse(201);
            case "MKCOL":
                if (Directory.Exists(local))
                {
                    return new FakeResponse(405);
                }

                Directory.CreateDirectory(local);
                return new FakeResponse(201);
            case "DELETE":
                if (Directory.Exists(local))
                {
                    Directory.Delete(local, recursive: true);
                }
                else
                {
                    File.Delete(local);
                }

                return new FakeResponse(204);
            default:
                return new FakeResponse(405);
        }
    }

    private static void Entry(StringBuilder xml, string href, string local)
    {
        bool folder = Directory.Exists(local);
        xml.Append("<d:response><d:href>").Append(WebUtility.HtmlEncode(href)).Append("</d:href><d:propstat><d:prop>")
            .Append(folder ? "<d:resourcetype><d:collection/></d:resourcetype>" : "<d:resourcetype/>")
            .Append(folder ? "" : $"<d:getcontentlength>{new FileInfo(local).Length}</d:getcontentlength>")
            .Append("<d:getlastmodified>").Append(File.GetLastWriteTimeUtc(local).ToString("R", CultureInfo.InvariantCulture)).Append("</d:getlastmodified>")
            .Append("</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");
    }

    public void Dispose() => _server.Dispose();
}

/// <summary>
/// S3 de prueba (estilo ruta, bucket "cubo"): ListObjectsV2 con prefijo, delimitador y páginas de 3, GET, PUT, borrar,
/// y subida por partes. Comprueba la firma SigV4 de cada petición recalculándola.
/// </summary>
internal sealed class FakeS3 : IDisposable
{
    private readonly FakeHttpServer _server;
    private readonly string _secret;
    private readonly Dictionary<string, Dictionary<int, byte[]>> _uploads = [];

    public FakeS3(string secret)
    {
        _secret = secret;
        _server = new FakeHttpServer(Handle);
    }

    public SortedDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public int BadSignatures { get; private set; }

    public string Endpoint => $"http://127.0.0.1:{_server.Port}";

    private FakeResponse Handle(HttpRequest request, byte[] body)
    {
        lock (Objects)
        {
            string auth = request.Headers.GetValueOrDefault("Authorization") ?? "";
            string signature = auth[(auth.IndexOf("Signature=", StringComparison.Ordinal) + 10)..];
            string region = auth.Split('/')[2];
            string expected = S3Store.Signature(request.Method, request.Path, request.QueryString, request.Headers["Host"],
                request.Headers["x-amz-date"], request.Headers["x-amz-content-sha256"], _secret, region);
            if (signature != expected)
            {
                BadSignatures++;
                return new FakeResponse(403, Encoding.UTF8.GetBytes("<Error><Message>SignatureDoesNotMatch</Message></Error>"));
            }

            string key = Uri.UnescapeDataString(request.Path)["/cubo".Length..].TrimStart('/');
            string? uploadId = request.Query("uploadId");
            switch (request.Method)
            {
                case "GET" when request.Query("list-type") == "2":
                    return List(request.Query("prefix") ?? "", request.Query("delimiter"), request.Query("continuation-token"));
                case "GET":
                    return Objects.TryGetValue(key, out var data) ? new FakeResponse(200, data) : new FakeResponse(404);
                case "PUT" when uploadId is not null:
                    int part = int.Parse(request.Query("partNumber")!, CultureInfo.InvariantCulture);
                    _uploads[uploadId][part] = body;
                    return new FakeResponse(200, null, new() { ["ETag"] = $"\"etag{part}\"" });
                case "PUT":
                    Objects[key] = body;
                    return new FakeResponse(200);
                case "POST" when request.Query("uploads") is not null:
                    string id = "subida" + _uploads.Count;
                    _uploads[id] = [];
                    return new FakeResponse(200, Encoding.UTF8.GetBytes($"<InitiateMultipartUploadResult><UploadId>{id}</UploadId></InitiateMultipartUploadResult>"));
                case "POST" when uploadId is not null:
                    string xml = Encoding.UTF8.GetString(body);
                    var parts = _uploads[uploadId];
                    Assert.All(parts.Keys, p => Assert.Contains($"<PartNumber>{p}</PartNumber><ETag>&quot;etag{p}&quot;</ETag>", xml));
                    Objects[key] = [.. parts.OrderBy(p => p.Key).SelectMany(p => p.Value)];
                    _uploads.Remove(uploadId);
                    return new FakeResponse(200);
                case "DELETE" when uploadId is not null:
                    _uploads.Remove(uploadId);
                    return new FakeResponse(204);
                case "DELETE":
                    return Objects.Remove(key) ? new FakeResponse(204) : new FakeResponse(404);
                default:
                    return new FakeResponse(405);
            }
        }
    }

    private FakeResponse List(string prefix, string? delimiter, string? after)
    {
        var entries = new List<(string Key, bool Prefix)>();
        foreach (string key in Objects.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            int slash = delimiter is null ? -1 : key.IndexOf('/', prefix.Length);
            var entry = slash >= 0 ? (key[..(slash + 1)], true) : (key, false);
            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        var page = entries.Where(e => after is null || string.CompareOrdinal(e.Key, after) > 0).Take(3).ToList();
        bool truncated = entries.Count(e => after is null || string.CompareOrdinal(e.Key, after) > 0) > 3;
        var xml = new StringBuilder("<ListBucketResult>");
        xml.Append("<IsTruncated>").Append(truncated ? "true" : "false").Append("</IsTruncated>");
        if (truncated)
        {
            xml.Append("<NextContinuationToken>").Append(WebUtility.HtmlEncode(page[^1].Key)).Append("</NextContinuationToken>");
        }

        foreach (var (key, isPrefix) in page)
        {
            xml.Append(isPrefix
                ? $"<CommonPrefixes><Prefix>{WebUtility.HtmlEncode(key)}</Prefix></CommonPrefixes>"
                : $"<Contents><Key>{WebUtility.HtmlEncode(key)}</Key><Size>{Objects[key].Length}</Size><LastModified>2025-06-01T10:00:00.000Z</LastModified></Contents>");
        }

        return new FakeResponse(200, Encoding.UTF8.GetBytes(xml.Append("</ListBucketResult>").ToString()));
    }

    public void Dispose() => _server.Dispose();
}

/// <summary>FTP de prueba sobre una carpeta (sin TLS): pasivo con EPSV, MLSD (o LIST estilo Unix), RETR, STOR, MKD, DELE, RMD.</summary>
internal sealed class FakeFtp : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _root;
    private readonly string _user;
    private readonly string _password;
    private readonly CancellationTokenSource _stop = new();

    public FakeFtp(string root, string user, string password, bool mlsd = true)
    {
        (_root, _user, _password, Mlsd) = (root, user, password, mlsd);
        _listener.Start();
        _ = AcceptAsync();
    }

    public bool Mlsd { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private string Local(string path) => Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Serve(client));
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private void Serve(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            TcpListener? passive = null;
            bool logged = false;
            writer.WriteLine("220-Bienvenido");
            writer.WriteLine("220 FTP de prueba");
            try
            {
                while (reader.ReadLine() is { } line)
                {
                    string verb = line.Split(' ')[0].ToUpperInvariant();
                    string argument = line.Length > verb.Length ? line[(verb.Length + 1)..] : "";
                    if (!logged && verb is not ("USER" or "PASS" or "QUIT"))
                    {
                        writer.WriteLine("530 Primero inicia sesión");
                        continue;
                    }

                    switch (verb)
                    {
                        case "USER":
                            writer.WriteLine(argument == _user ? "331 Contraseña" : "530 No");
                            break;
                        case "PASS":
                            logged = argument == _password;
                            writer.WriteLine(logged ? "230 Dentro" : "530 Contraseña incorrecta");
                            break;
                        case "PWD":
                            writer.WriteLine("257 \"/\" es la carpeta actual");
                            break;
                        case "TYPE" or "OPTS":
                            writer.WriteLine("200 Vale");
                            break;
                        case "EPSV":
                            passive?.Stop();
                            passive = new TcpListener(IPAddress.Loopback, 0);
                            passive.Start();
                            writer.WriteLine($"229 Modo pasivo extendido (|||{((IPEndPoint)passive.LocalEndpoint).Port}|)");
                            break;
                        case "MLSD" when !Mlsd:
                            writer.WriteLine("500 MLSD no");
                            break;
                        case "MLSD" or "LIST":
                            string folder = Local(argument.Replace("-a ", ""));
                            Data(writer, passive!, data =>
                            {
                                var lines = new StringBuilder();
                                foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                                {
                                    string stamp = info.LastWriteTimeUtc.ToString(verb == "MLSD" ? "yyyyMMddHHmmss" : "MMM dd HH:mm", CultureInfo.InvariantCulture);
                                    lines.Append(verb == "MLSD"
                                        ? info is DirectoryInfo ? $"type=dir;modify={stamp}; {info.Name}\r\n" : $"type=file;size={((FileInfo)info).Length};modify={stamp}; {info.Name}\r\n"
                                        : info is DirectoryInfo ? $"drwxr-xr-x 2 ana ana 4096 {stamp} {info.Name}\r\n" : $"-rw-r--r-- 1 ana ana {((FileInfo)info).Length} {stamp} {info.Name}\r\n");
                                }

                                data.Write(Encoding.UTF8.GetBytes(lines.ToString()));
                            });
                            break;
                        case "RETR":
                            if (!File.Exists(Local(argument)))
                            {
                                writer.WriteLine("550 No existe");
                                break;
                            }

                            Data(writer, passive!, data => data.Write(File.ReadAllBytes(Local(argument))));
                            break;
                        case "STOR":
                            Data(writer, passive!, data =>
                            {
                                using var file = File.Create(Local(argument));
                                data.CopyTo(file);
                            });
                            break;
                        case "MKD":
                            bool existed = Directory.Exists(Local(argument));
                            Directory.CreateDirectory(Local(argument));
                            writer.WriteLine(existed ? "550 Ya existe" : $"257 \"{argument}\" creada");
                            break;
                        case "DELE":
                            File.Delete(Local(argument));
                            writer.WriteLine("250 Borrado");
                            break;
                        case "RMD":
                            Directory.Delete(Local(argument));
                            writer.WriteLine("250 Borrada");
                            break;
                        case "QUIT":
                            writer.WriteLine("221 Adiós");
                            return;
                        default:
                            writer.WriteLine("502 No implementado");
                            break;
                    }
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Data(StreamWriter control, TcpListener passive, Action<NetworkStream> transfer)
    {
        var accept = passive.AcceptTcpClientAsync();
        control.WriteLine("150 Abriendo conexión de datos");
        using (var data = accept.GetAwaiter().GetResult())
        {
            transfer(data.GetStream());
        }

        passive.Stop();
        control.WriteLine("226 Hecho");
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
