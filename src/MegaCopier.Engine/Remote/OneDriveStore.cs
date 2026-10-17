using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// OneDrive con Microsoft Graph. Los ids son los de los elementos de OneDrive ("root" = la raíz). Los archivos grandes
/// se suben por sesión, en trozos de 10 MB (múltiplo de 320 KB, como pide OneDrive).
/// </summary>
internal sealed class OneDriveStore : IRemoteStore
{
    internal static long SimpleUploadLimit = 4 * 1024 * 1024; // las pruebas lo bajan
    private const int ChunkSize = 32 * 320 * 1024;

    private readonly CloudHttp _http;
    private readonly HttpClient _plain = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _base;

    public OneDriveStore(Connection connection, string refreshToken)
    {
        _http = new CloudHttp(connection, refreshToken);
        _base = (connection.Host.Length > 0 ? connection.Host : CloudProvider.For(ConnectionKind.OneDrive).ApiBase).TrimEnd('/') + "/";
        RootId = connection.Root.Trim('/', '\\').Length == 0 ? "root" : ResolvePath(connection.Root);
    }

    public string RootId { get; }

    private string ResolvePath(string path)
    {
        string escaped = string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Get, $"{_base}me/drive/root:/{escaped}"));
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var items = new List<RemoteItem>();
        string? url = $"{_base}me/drive/items/{parentId}/children?$select=id,name,size,folder,file,lastModifiedDateTime&$top=999";
        while (url is not null)
        {
            string current = url;
            using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Get, current));
            foreach (var item in json.RootElement.GetProperty("value").EnumerateArray())
            {
                bool folder = item.TryGetProperty("folder", out _);
                if (!folder && !item.TryGetProperty("file", out _))
                {
                    continue; // paquetes de OneNote y similares: no se pueden descargar como archivo
                }

                items.Add(new RemoteItem(item.GetProperty("id").GetString()!, item.GetProperty("name").GetString()!, folder,
                    folder ? 0 : item.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    item.TryGetProperty("lastModifiedDateTime", out var modified) ? modified.GetDateTime().ToUniversalTime() : null));
            }

            url = json.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
        }

        return items;
    }

    public Stream OpenRead(string objectId)
    {
        var response = _http.Send(() => new HttpRequestMessage(HttpMethod.Get, $"{_base}me/drive/items/{objectId}/content"), HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string target = $"{_base}me/drive/items/{parentId}:/{Uri.EscapeDataString(name)}:";
        if (length <= SimpleUploadLimit)
        {
            byte[] bytes = new byte[length];
            data.ReadExactly(bytes);
            using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Put, target + "/content") { Content = Octets(bytes, 0, bytes.Length) }, cancellationToken);
            return json.RootElement.GetProperty("id").GetString()!;
        }

        string uploadUrl;
        using (var session = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Post, target + "/createUploadSession")
        {
            Content = CloudHttp.Json("""{"item":{"@microsoft.graph.conflictBehavior":"replace"}}"""),
        }, cancellationToken))
        {
            uploadUrl = session.RootElement.GetProperty("uploadUrl").GetString()!;
        }

        // La dirección de la sesión ya lleva el permiso: no se manda el token (OneDrive lo rechaza).
        byte[] buffer = new byte[ChunkSize];
        long offset = 0;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = data.ReadAtLeast(buffer, (int)Math.Min(ChunkSize, length - offset), throwOnEndOfStream: true);
            var content = Octets(buffer, 0, count);
            content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + count - 1, length);
            using var response = _plain.Send(new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = content }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new IOException($"OneDrive: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            offset += count;
            if (offset == length)
            {
                using var json = JsonDocument.Parse(response.Content.ReadAsStream(cancellationToken));
                return json.RootElement.GetProperty("id").GetString()!;
            }
        }

        throw new IOException("OneDrive: subida incompleta");
    }

    public string CreateFolder(string parentId, string name)
    {
        try
        {
            using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Post, $"{_base}me/drive/items/{parentId}/children")
            {
                Content = CloudHttp.Json($$"""{"name":{{JsonString(name)}},"folder":{},"@microsoft.graph.conflictBehavior":"fail"}"""),
            });
            return json.RootElement.GetProperty("id").GetString()!;
        }
        catch (IOException) when (Children(parentId).FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            return existing.Id; // ya existía
        }
    }

    public void Delete(string objectId, bool recursive)
    {
        using var response = _http.Send(() => new HttpRequestMessage(HttpMethod.Delete, $"{_base}me/drive/items/{objectId}")); // va a la papelera de OneDrive
    }

    internal static string JsonString(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    internal static ByteArrayContent Octets(byte[] buffer, int offset, int count)
    {
        var content = new ByteArrayContent(buffer, offset, count);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    public void Dispose()
    {
        _http.Dispose();
        _plain.Dispose();
    }
}
