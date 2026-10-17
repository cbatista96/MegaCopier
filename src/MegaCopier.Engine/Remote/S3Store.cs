using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// S3 y compatibles (AWS, Backblaze B2, Cloudflare R2, Wasabi, MinIO...) con firma SigV4 propia. Las "carpetas" son
/// prefijos que acaban en "/". Ids: la clave completa (o el prefijo de la carpeta). Los archivos grandes se suben por
/// partes de 64 MB (si algo falla, la subida a medias se cancela en el servidor). En AWS las direcciones son
/// bucket.s3.region.amazonaws.com; en los compatibles, servidor/bucket/clave.
/// </summary>
internal sealed class S3Store : IRemoteStore
{
    /// <summary>Tamaño de cada parte en las subidas grandes (las pruebas lo bajan para probar la subida por partes).</summary>
    internal static int PartSize { get; set; } = 64 << 20;
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly HttpClient _http;
    private readonly string _accessKey;
    private readonly string _secret;
    private readonly string _region;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly Uri _endpoint;
    private readonly bool _virtualHost;

    public S3Store(Connection connection, string secret)
    {
        _accessKey = connection.User;
        _secret = secret;
        _region = connection.Region.Trim() is { Length: > 0 } region ? region : "us-east-1";
        var root = connection.Root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (root.Length == 0)
        {
            throw new IOException("S3: falta el bucket (Carpeta: \"bucket\" o \"bucket/prefijo\").");
        }

        _bucket = root[0];
        _prefix = root.Length > 1 ? string.Join('/', root[1..]) + "/" : "";
        string host = connection.Host.Trim() is { Length: > 0 } h ? h : $"s3.{_region}.amazonaws.com";
        if (!host.Contains("://", StringComparison.Ordinal))
        {
            host = "https://" + host;
        }

        var endpoint = new Uri(host);
        _virtualHost = endpoint.Host.EndsWith("amazonaws.com", StringComparison.OrdinalIgnoreCase);
        _endpoint = _virtualHost ? new UriBuilder(endpoint) { Host = $"{_bucket}.{endpoint.Host}" }.Uri : endpoint;
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (connection.AcceptAnyCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public string RootId => _prefix;

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var items = new List<RemoteItem>();
        string? token = null;
        do
        {
            var query = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["list-type"] = "2", ["prefix"] = parentId, ["delimiter"] = "/" };
            if (token is not null)
            {
                query["continuation-token"] = token;
            }

            using var response = Send(HttpMethod.Get, "", query, null);
            token = ParseList(response.Content.ReadAsStream(), parentId, items, out bool truncated);
            if (!truncated)
            {
                token = null;
            }
        }
        while (token is not null);

        return items;
    }

    private static string? ParseList(Stream xml, string prefix, List<RemoteItem> items, out bool truncated)
    {
        truncated = false;
        string? next = null;
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? key = null, commonPrefix = null;
        long size = 0;
        DateTime? modified = null;
        bool inContents = false, inPrefixes = false;
        reader.Read();
        while (!reader.EOF)
        {
            // Los elementos con texto se leen enteros: ReadElementContentAsString ya deja el lector en el nodo
            // siguiente, así que aquí no se avanza otra vez (si no, se saltaría un nodo, como el cierre de Contents).
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "Key" or "Size" or "LastModified" or "Prefix" or "IsTruncated" or "NextContinuationToken")
            {
                string name = reader.LocalName;
                string value = reader.ReadElementContentAsString();
                switch (name)
                {
                    case "Key" when inContents:
                        key = value;
                        break;
                    case "Size" when inContents:
                        size = long.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "LastModified" when inContents:
                        modified = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                        break;
                    case "Prefix" when inPrefixes:
                        commonPrefix = value;
                        break;
                    case "IsTruncated":
                        truncated = value == "true";
                        break;
                    case "NextContinuationToken":
                        next = value;
                        break;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Contents":
                        (inContents, key, size, modified) = (true, null, 0, null);
                        break;
                    case "CommonPrefixes":
                        (inPrefixes, commonPrefix) = (true, null);
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "Contents" && key is not null && key != prefix)
                {
                    items.Add(new RemoteItem(key, key[prefix.Length..], false, size, modified));
                    inContents = false;
                }
                else if (reader.LocalName == "CommonPrefixes" && commonPrefix is not null)
                {
                    items.Add(new RemoteItem(commonPrefix, commonPrefix[prefix.Length..].TrimEnd('/'), true, 0, null));
                    inPrefixes = false;
                }
            }

            reader.Read();
        }

        return next;
    }

    public Stream OpenRead(string objectId)
    {
        var response = Send(HttpMethod.Get, objectId, null, null, HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string key = parentId + name;
        if (length <= PartSize)
        {
            using var response = Send(HttpMethod.Put, key, null, new StreamContent(data, 1 << 20) { Headers = { ContentLength = length } }, cancellationToken: cancellationToken);
            return key;
        }

        // Por partes: iniciar, subir cada parte, completar (o cancelar si algo falla).
        string uploadId;
        using (var start = Send(HttpMethod.Post, key, new(StringComparer.Ordinal) { ["uploads"] = "" }, null))
        {
            uploadId = ReadElement(start.Content.ReadAsStream(), "UploadId") ?? throw new IOException("S3: sin UploadId.");
        }

        try
        {
            var etags = new List<string>();
            byte[] buffer = new byte[PartSize];
            for (int part = 1; ; part++)
            {
                int filled = 0, read;
                while (filled < buffer.Length && (read = data.Read(buffer, filled, buffer.Length - filled)) > 0)
                {
                    filled += read;
                }

                if (filled == 0)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var query = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["partNumber"] = part.ToString(CultureInfo.InvariantCulture), ["uploadId"] = uploadId };
                using var response = Send(HttpMethod.Put, key, query, new ByteArrayContent(buffer, 0, filled), cancellationToken: cancellationToken);
                etags.Add(response.Headers.ETag?.Tag ?? (response.Headers.TryGetValues("ETag", out var values) ? values.First() : ""));
            }

            var xml = new StringBuilder("<CompleteMultipartUpload>");
            for (int i = 0; i < etags.Count; i++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<Part><PartNumber>{i + 1}</PartNumber><ETag>{WebUtility.HtmlEncode(etags[i])}</ETag></Part>");
            }

            xml.Append("</CompleteMultipartUpload>");
            using var done = Send(HttpMethod.Post, key, new(StringComparer.Ordinal) { ["uploadId"] = uploadId }, new StringContent(xml.ToString(), Encoding.UTF8, "application/xml"));
            return key;
        }
        catch
        {
            try
            {
                Send(HttpMethod.Delete, key, new(StringComparer.Ordinal) { ["uploadId"] = uploadId }, null).Dispose();
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    public string CreateFolder(string parentId, string name)
    {
        string key = parentId + name + "/";
        Send(HttpMethod.Put, key, null, new ByteArrayContent([])).Dispose();
        return key;
    }

    public void Delete(string objectId, bool recursive)
    {
        if (objectId.EndsWith('/') && recursive)
        {
            foreach (var child in Children(objectId))
            {
                Delete(child.Id, recursive: true);
            }
        }

        Send(HttpMethod.Delete, objectId, null, null).Dispose();
    }

    private static string? ReadElement(Stream xml, string name)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == name)
            {
                return reader.ReadElementContentAsString();
            }
        }

        return null;
    }

    // --- Firma SigV4 ---

    private HttpResponseMessage Send(HttpMethod method, string key, SortedDictionary<string, string>? query, HttpContent? content,
        HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default)
    {
        string path = (_virtualHost ? "" : "/" + _bucket) + "/" + string.Join('/', key.Split('/').Select(Encode));
        if (_virtualHost && path == "/")
        {
            path = "/";
        }

        string canonicalQuery = query is null ? "" : string.Join('&', query.Select(q => Encode(q.Key) + "=" + Encode(q.Value)));
        var url = new UriBuilder(_endpoint) { Path = _endpoint.AbsolutePath.TrimEnd('/') + path, Query = canonicalQuery }.Uri;
        var request = new HttpRequestMessage(method, url) { Content = content };
        var now = DateTime.UtcNow;
        string amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string payload = content is null ? EmptyHash : "UNSIGNED-PAYLOAD";
        string host = url.IsDefaultPort ? url.Host : $"{url.Host}:{url.Port}";
        string signature = Signature(method.Method, url.AbsolutePath, canonicalQuery, host, amzDate, payload, _secret, _region);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payload);
        request.Headers.TryAddWithoutValidation("Authorization", $"AWS4-HMAC-SHA256 Credential={_accessKey}/{date}/{_region}/s3/aws4_request, SignedHeaders={SignedHeaders}, Signature={signature}");

        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, option, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException(ex.Message, ex);
        }

        if (!response.IsSuccessStatusCode && !(method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound))
        {
            string detail = ReadElement(response.Content.ReadAsStream(), "Message") ?? response.ReasonPhrase ?? "";
            var status = response.StatusCode;
            response.Dispose();
            string message = $"S3 {method.Method} {key}: {(int)status} {detail}";
            throw status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ? new UnauthorizedAccessException(message) : new IOException(message);
        }

        return response;
    }

    private const string SignedHeaders = "host;x-amz-content-sha256;x-amz-date";

    /// <summary>Firma SigV4 de una petición con las cabeceras host, x-amz-content-sha256 y x-amz-date.</summary>
    internal static string Signature(string method, string path, string canonicalQuery, string host, string amzDate, string payloadHash, string secret, string region)
    {
        string date = amzDate[..8];
        string canonical = $"{method}\n{path}\n{canonicalQuery}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n\n{SignedHeaders}\n{payloadHash}";
        string scope = $"{date}/{region}/s3/aws4_request";
        string toSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        byte[] signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), date), region), "s3"), "aws4_request");
        return Hex(Hmac(signingKey, toSign));
    }

    /// <summary>Codificación de AWS: todo salvo A-Z a-z 0-9 - _ . ~ (RFC 3986).</summary>
    internal static string Encode(string value)
    {
        var text = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
            {
                text.Append(c);
            }
            else
            {
                text.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public void Dispose() => _http.Dispose();
}
