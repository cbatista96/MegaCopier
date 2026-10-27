using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// Azure Blob Storage con un token SAS (el permiso firmado que da el portal de Azure para un contenedor: no hace falta
/// guardar la clave de la cuenta). Como en S3, las "carpetas" son prefijos que acaban en "/" y los ids son el nombre
/// completo del blob. Los archivos grandes se suben por bloques de 16 MB y se confirman al final con la lista de bloques
/// (un fallo a medias no deja un archivo roto: los bloques sin confirmar caducan solos).
/// </summary>
internal sealed class AzureBlobStore : IRemoteStore
{
    /// <summary>Tamaño de cada bloque en las subidas grandes (las pruebas lo bajan).</summary>
    internal static int BlockSize { get; set; } = 16 << 20;
    private const string ApiVersion = "2021-08-06";

    private readonly HttpClient _http;
    private readonly string _container;
    private readonly string _prefix;
    private readonly string _account;
    private readonly string _sas;

    public AzureBlobStore(Connection connection, string sas)
    {
        var root = connection.Root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (root.Length == 0)
        {
            throw new IOException("Azure: falta el contenedor (Carpeta: \"contenedor\" o \"contenedor/prefijo\").");
        }

        _container = root[0];
        _prefix = root.Length > 1 ? string.Join('/', root[1..]) + "/" : "";
        string host = connection.Host.Trim();
        if (host.Length == 0)
        {
            throw new IOException("Azure: falta la cuenta (https://cuenta.blob.core.windows.net).");
        }

        // «cuenta» a secas también vale.
        _account = (host.Contains("://", StringComparison.Ordinal) ? host : host.Contains('.') ? "https://" + host : $"https://{host}.blob.core.windows.net").TrimEnd('/');
        _sas = sas.Trim().TrimStart('?');
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
        string? marker = null;
        do
        {
            string query = $"restype=container&comp=list&delimiter=%2F&prefix={Uri.EscapeDataString(parentId)}"
                + (marker is null ? "" : "&marker=" + Uri.EscapeDataString(marker));
            using var response = Send(HttpMethod.Get, "", query, null);
            marker = ParseList(response.Content.ReadAsStream(), parentId, items);
        }
        while (!string.IsNullOrEmpty(marker));

        return items;
    }

    private static string? ParseList(Stream xml, string prefix, List<RemoteItem> items)
    {
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? next = null, name = null;
        long size = 0;
        DateTime? modified = null;
        bool inBlob = false, inPrefix = false;
        reader.Read();
        while (!reader.EOF)
        {
            // Como en S3: ReadElementContentAsString ya avanza al nodo siguiente.
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "Name" or "Content-Length" or "Last-Modified" or "NextMarker")
            {
                string element = reader.LocalName;
                string value = reader.ReadElementContentAsString();
                switch (element)
                {
                    case "Name" when inBlob || inPrefix:
                        name = value;
                        break;
                    case "Content-Length" when inBlob:
                        size = long.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "Last-Modified" when inBlob:
                        modified = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                        break;
                    case "NextMarker":
                        next = value;
                        break;
                }

                continue;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Blob":
                        (inBlob, name, size, modified) = (true, null, 0, null);
                        break;
                    case "BlobPrefix":
                        (inPrefix, name) = (true, null);
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "Blob" && name is not null)
                {
                    if (name != prefix && !name.EndsWith('/'))
                    {
                        items.Add(new RemoteItem(name, name[prefix.Length..], false, size, modified));
                    }

                    inBlob = false;
                }
                else if (reader.LocalName == "BlobPrefix" && name is not null)
                {
                    items.Add(new RemoteItem(name, name[prefix.Length..].TrimEnd('/'), true, 0, null));
                    inPrefix = false;
                }
            }

            reader.Read();
        }

        return next;
    }

    public Stream OpenRead(string objectId)
    {
        var response = Send(HttpMethod.Get, objectId, "", null, HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string blob = parentId + name;
        if (length <= BlockSize)
        {
            var content = new StreamContent(data, 1 << 20) { Headers = { ContentLength = length } };
            using var response = Send(HttpMethod.Put, blob, "", content, cancellationToken: cancellationToken, blockBlob: true);
            return blob;
        }

        // Por bloques: cada uno con su id (base64 de la misma longitud) y al final la lista que los une en orden.
        var ids = new List<string>();
        byte[] buffer = new byte[BlockSize];
        while (true)
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
            string id = Convert.ToBase64String(Encoding.ASCII.GetBytes(ids.Count.ToString("D8", CultureInfo.InvariantCulture)));
            using var response = Send(HttpMethod.Put, blob, "comp=block&blockid=" + Uri.EscapeDataString(id), new ByteArrayContent(buffer, 0, filled), cancellationToken: cancellationToken);
            ids.Add(id);
        }

        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>");
        foreach (string id in ids)
        {
            xml.Append("<Latest>").Append(id).Append("</Latest>");
        }

        xml.Append("</BlockList>");
        using var done = Send(HttpMethod.Put, blob, "comp=blocklist", new StringContent(xml.ToString(), Encoding.UTF8, "application/xml"), cancellationToken: cancellationToken);
        return blob;
    }

    public string CreateFolder(string parentId, string name)
    {
        // Azure no tiene carpetas: un blob vacío acabado en "/" la hace visible aunque esté vacía (como en S3).
        string blob = parentId + name + "/";
        Send(HttpMethod.Put, blob, "", new ByteArrayContent([]), blockBlob: true).Dispose();
        return blob;
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

        try
        {
            Send(HttpMethod.Delete, objectId, "", null).Dispose();
        }
        catch (IOException) when (objectId.EndsWith('/'))
        {
            // Una «carpeta» sin blob marcador: ya no queda nada dentro.
        }
    }

    private HttpResponseMessage Send(HttpMethod method, string blob, string query, HttpContent? content,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default, bool blockBlob = false)
    {
        string path = blob.Length == 0 ? "" : "/" + string.Join('/', blob.Split('/').Select(Uri.EscapeDataString));
        string all = string.Join('&', new[] { query, _sas }.Where(q => q.Length > 0));
        var request = new HttpRequestMessage(method, $"{_account}/{Uri.EscapeDataString(_container)}{path}{(all.Length > 0 ? "?" + all : "")}") { Content = content };
        request.Headers.Add("x-ms-version", ApiVersion);
        request.Headers.Add("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
        if (blockBlob)
        {
            request.Headers.Add("x-ms-blob-type", "BlockBlob");
        }

        HttpResponseMessage response;
        try
        {
            response = _http.Send(request, completion, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException("Azure: " + ex.Message, ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string message = response.StatusCode == HttpStatusCode.Forbidden ? "permiso SAS no válido o caducado" : response.ReasonPhrase ?? "";
        response.Dispose();
        throw new IOException($"Azure: {(int)response.StatusCode} {message}");
    }

    public void Dispose() => _http.Dispose();
}
