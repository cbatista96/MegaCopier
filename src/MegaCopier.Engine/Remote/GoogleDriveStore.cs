using System.Net.Http.Headers;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// Google Drive (API v3). Los ids son los de Drive ("root" = Mi unidad). Los documentos de Google (Docs, Hojas...) no son
/// archivos que se puedan descargar tal cual y no se muestran. Al borrar, van a la papelera de Drive.
/// </summary>
internal sealed class GoogleDriveStore : IRemoteStore
{
    private const string FolderType = "application/vnd.google-apps.folder";

    private readonly CloudHttp _http;
    private readonly string _base;

    public GoogleDriveStore(Connection connection, string refreshToken)
    {
        _http = new CloudHttp(connection, refreshToken);
        _base = (connection.Host.Length > 0 ? connection.Host : CloudProvider.For(ConnectionKind.GoogleDrive).ApiBase).TrimEnd('/') + "/";
        string id = "root";
        foreach (string part in connection.Root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            id = Children(id).FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new DirectoryNotFoundException(connection.Root);
        }

        RootId = id;
    }

    public string RootId { get; }

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var items = new List<RemoteItem>();
        string? page = null;
        do
        {
            string query = Uri.EscapeDataString($"'{parentId.Replace("'", "\\'")}' in parents and trashed = false");
            string url = $"{_base}drive/v3/files?q={query}&fields=nextPageToken,files(id,name,mimeType,size,modifiedTime)&pageSize=1000"
                + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Get, url));
            foreach (var file in json.RootElement.GetProperty("files").EnumerateArray())
            {
                string type = file.GetProperty("mimeType").GetString() ?? "";
                bool folder = type == FolderType;
                if (!folder && type.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal))
                {
                    continue; // Docs, Hojas, Presentaciones...
                }

                items.Add(new RemoteItem(file.GetProperty("id").GetString()!, file.GetProperty("name").GetString()!, folder,
                    !folder && file.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out long bytes) ? bytes : 0,
                    file.TryGetProperty("modifiedTime", out var modified) ? modified.GetDateTime().ToUniversalTime() : null));
            }

            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page is not null);

        return items;
    }

    public Stream OpenRead(string objectId)
    {
        var response = _http.Send(() => new HttpRequestMessage(HttpMethod.Get, $"{_base}drive/v3/files/{objectId}?alt=media"), HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        // Subida "reanudable" de Google: primero los datos del archivo, luego el contenido de una vez.
        string metadata = $$"""{"name":{{OneDriveStore.JsonString(name)}},"parents":[{{OneDriveStore.JsonString(parentId)}}]}""";
        Uri location;
        using (var start = _http.Send(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_base}upload/drive/v3/files?uploadType=resumable&fields=id") { Content = CloudHttp.Json(metadata) };
            request.Headers.Add("X-Upload-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return request;
        }, cancellationToken: cancellationToken))
        {
            location = start.Headers.Location ?? throw new IOException("Google Drive: sin dirección de subida");
        }

        var content = new StreamContent(data, 1 << 20);
        content.Headers.ContentLength = length;
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Put, location) { Content = content }, cancellationToken, retryable: false); // el flujo no se puede repetir
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public string CreateFolder(string parentId, string name)
    {
        // Drive admite dos carpetas con el mismo nombre: si ya hay una, se usa esa.
        if (Children(parentId).FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, name, StringComparison.Ordinal)) is { } existing)
        {
            return existing.Id;
        }

        string body = $$"""{"name":{{OneDriveStore.JsonString(name)}},"mimeType":"{{FolderType}}","parents":[{{OneDriveStore.JsonString(parentId)}}]}""";
        using var json = _http.SendJson(() => new HttpRequestMessage(HttpMethod.Post, $"{_base}drive/v3/files?fields=id") { Content = CloudHttp.Json(body) });
        return json.RootElement.GetProperty("id").GetString()!;
    }

    public void Delete(string objectId, bool recursive)
    {
        using var response = _http.Send(() => new HttpRequestMessage(HttpMethod.Patch, $"{_base}drive/v3/files/{objectId}") { Content = CloudHttp.Json("""{"trashed":true}""") });
    }

    public void Dispose() => _http.Dispose();
}
