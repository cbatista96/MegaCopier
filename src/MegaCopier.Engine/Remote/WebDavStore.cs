using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// WebDAV (Nextcloud, ownCloud, muchos NAS, Box, pCloud...): PROPFIND para listar, GET, PUT, MKCOL y DELETE.
/// Los ids son rutas relativas a la dirección de la conexión ("" = su raíz; las carpetas terminan en "/").
/// </summary>
internal sealed class WebDavStore : IRemoteStore
{
    private const string PropFindBody = """<?xml version="1.0" encoding="utf-8"?><d:propfind xmlns:d="DAV:"><d:prop><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>""";

    private readonly HttpClient _http;
    private readonly Uri _base;

    public WebDavStore(Connection connection, string password)
    {
        string url = connection.Host.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        string root = string.Join('/', connection.Root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        _base = new Uri(url.TrimEnd('/') + "/" + (root.Length > 0 ? root + "/" : ""));
        var handler = new SocketsHttpHandler
        {
            Credentials = connection.User.Length > 0 ? new NetworkCredential(connection.User, password) : null,
            PreAuthenticate = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (connection.AcceptAnyCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        if (connection.User.Length > 0)
        {
            // Básica desde la primera petición (muchos servidores no piden antes de rechazar un PUT grande).
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.User}:{password}")));
        }
    }

    public string RootId => "";

    private Uri Url(string id) => new(_base, string.Join('/', id.Split('/').Select(s => s.Length == 0 ? s : Uri.EscapeDataString(s))));

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        string folder = parentId.Length == 0 || parentId.EndsWith('/') ? parentId : parentId + "/";
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), Url(folder)) { Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml") };
        request.Headers.Add("Depth", "1");
        using var response = Send(request);
        using var stream = response.Content.ReadAsStream();
        var items = new List<RemoteItem>();
        string self = Url(folder).AbsolutePath.TrimEnd('/');
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? href = null;
        bool collection = false;
        long size = 0;
        DateTime? modified = null;
        reader.Read();
        while (!reader.EOF)
        {
            // Los elementos con texto se leen enteros y dejan el lector ya en el nodo siguiente: no se avanza otra vez.
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == "DAV:" && reader.LocalName is "href" or "getcontentlength" or "getlastmodified")
            {
                string name = reader.LocalName;
                string value = reader.ReadElementContentAsString();
                switch (name)
                {
                    case "href":
                        href = value;
                        break;
                    case "getcontentlength":
                        _ = long.TryParse(value, CultureInfo.InvariantCulture, out size);
                        break;
                    default:
                        modified = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;
                        break;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI == "DAV:")
            {
                if (reader.LocalName == "response")
                {
                    (href, collection, size, modified) = (null, false, 0, null);
                }
                else if (reader.LocalName == "collection")
                {
                    collection = true;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "response" && reader.NamespaceURI == "DAV:" && href is not null)
            {
                string path = Uri.UnescapeDataString(new Uri(_base, href).AbsolutePath).TrimEnd('/');
                if (!string.Equals(path, Uri.UnescapeDataString(self), StringComparison.Ordinal))
                {
                    string name = path[(path.LastIndexOf('/') + 1)..];
                    items.Add(new RemoteItem(folder + name + (collection ? "/" : ""), name, collection, collection ? 0 : size, modified));
                }
            }

            reader.Read();
        }

        return items;
    }

    public Stream OpenRead(string objectId)
    {
        var response = Send(new HttpRequestMessage(HttpMethod.Get, Url(objectId)), HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string id = parentId + name;
        var content = new StreamContent(data, 1 << 20);
        content.Headers.ContentLength = length;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = Send(new HttpRequestMessage(HttpMethod.Put, Url(id)) { Content = content }, cancellationToken: cancellationToken);
        return id;
    }

    public string CreateFolder(string parentId, string name)
    {
        string id = parentId + name + "/";
        using var response = _http.Send(new HttpRequestMessage(new HttpMethod("MKCOL"), Url(id)));
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MethodNotAllowed) // 405: ya existe
        {
            throw new IOException($"MKCOL {name}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return id;
    }

    public void Delete(string objectId, bool recursive)
    {
        using var response = _http.Send(new HttpRequestMessage(HttpMethod.Delete, Url(objectId)));
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new IOException($"DELETE: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    private HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, option, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException(ex.Message, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string message = $"{request.Method} {Uri.UnescapeDataString(request.RequestUri!.AbsolutePath)}: {(int)response.StatusCode} {response.ReasonPhrase}";
            response.Dispose();
            throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new UnauthorizedAccessException(message) : new IOException(message);
        }

        return response;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Un flujo que al cerrarse cierra también lo que lo sostiene (la respuesta HTTP, la conexión de datos...).</summary>
internal sealed class OwnedStream(Stream inner, IDisposable owner) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner.Dispose();
        }

        base.Dispose(disposing);
    }
}
