using System.Text.Json;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// Dropbox (API v2). Los ids son rutas ("" = la raíz, "/Fotos/2026"). Los archivos de más de 150 MB se suben por
/// sesión, en trozos de 8 MB. Al borrar, Dropbox los guarda un tiempo en sus archivos eliminados.
/// </summary>
internal sealed class DropboxStore : IRemoteStore
{
    internal static long SimpleUploadLimit = 150L * 1024 * 1024; // las pruebas lo bajan
    private const int ChunkSize = 8 * 1024 * 1024;

    private readonly CloudHttp _http;
    private readonly string _api;
    private readonly string _content;

    public DropboxStore(Connection connection, string refreshToken)
    {
        _http = new CloudHttp(connection, refreshToken);
        _api = (connection.Host.Length > 0 ? connection.Host : CloudProvider.For(ConnectionKind.Dropbox).ApiBase).TrimEnd('/') + "/";
        _content = connection.Host.Length > 0 ? _api : "https://content.dropboxapi.com/";
        string root = string.Join('/', connection.Root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        RootId = root.Length == 0 ? "" : "/" + root;
    }

    public string RootId { get; }

    private HttpRequestMessage Rpc(string endpoint, string json) => new(HttpMethod.Post, _api + "2/" + endpoint) { Content = CloudHttp.Json(json) };

    private HttpRequestMessage ContentCall(string endpoint, string argument, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _content + "2/" + endpoint) { Content = body };
        request.Headers.TryAddWithoutValidation("Dropbox-API-Arg", argument); // JSON con lo no ASCII escapado (\uXXXX)
        return request;
    }

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var items = new List<RemoteItem>();
        string endpoint = "files/list_folder";
        string body = $$"""{"path":{{OneDriveStore.JsonString(parentId)}},"limit":2000}""";
        while (true)
        {
            string currentEndpoint = endpoint, currentBody = body;
            using var json = _http.SendJson(() => Rpc(currentEndpoint, currentBody));
            foreach (var entry in json.RootElement.GetProperty("entries").EnumerateArray())
            {
                string tag = entry.GetProperty(".tag").GetString()!;
                if (tag is not ("file" or "folder"))
                {
                    continue;
                }

                bool folder = tag == "folder";
                items.Add(new RemoteItem(entry.GetProperty("path_display").GetString()!, entry.GetProperty("name").GetString()!, folder,
                    folder ? 0 : entry.GetProperty("size").GetInt64(),
                    entry.TryGetProperty("client_modified", out var modified) ? modified.GetDateTime().ToUniversalTime() : null));
            }

            if (!json.RootElement.GetProperty("has_more").GetBoolean())
            {
                return items;
            }

            endpoint = "files/list_folder/continue";
            body = $$"""{"cursor":{{OneDriveStore.JsonString(json.RootElement.GetProperty("cursor").GetString()!)}}}""";
        }
    }

    public Stream OpenRead(string objectId)
    {
        var response = _http.Send(() => ContentCall("files/download", $$"""{"path":{{OneDriveStore.JsonString(objectId)}}}"""), HttpCompletionOption.ResponseHeadersRead);
        return new OwnedStream(response.Content.ReadAsStream(), response);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string path = parentId.TrimEnd('/') + "/" + name;
        string commit = $$"""{"path":{{OneDriveStore.JsonString(path)}},"mode":"overwrite","mute":true}""";
        if (length <= SimpleUploadLimit)
        {
            byte[] bytes = new byte[length];
            data.ReadExactly(bytes);
            using var json = _http.SendJson(() => ContentCall("files/upload", commit, OneDriveStore.Octets(bytes, 0, bytes.Length)), cancellationToken);
            return json.RootElement.GetProperty("path_display").GetString()!;
        }

        byte[] buffer = new byte[ChunkSize];
        int count = data.ReadAtLeast(buffer, (int)Math.Min(ChunkSize, length), throwOnEndOfStream: true);
        string session;
        using (var start = _http.SendJson(() => ContentCall("files/upload_session/start", """{"close":false}""", OneDriveStore.Octets(buffer, 0, count)), cancellationToken))
        {
            session = start.RootElement.GetProperty("session_id").GetString()!;
        }

        long offset = count;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count = data.ReadAtLeast(buffer, (int)Math.Min(ChunkSize, length - offset), throwOnEndOfStream: true);
            bool last = offset + count == length;
            string cursor = $$"""{"session_id":{{OneDriveStore.JsonString(session)}},"offset":{{offset}}}""";
            if (last)
            {
                using var json = _http.SendJson(() => ContentCall("files/upload_session/finish", $$"""{"cursor":{{cursor}},"commit":{{commit}}}""", OneDriveStore.Octets(buffer, 0, count)), cancellationToken);
                return json.RootElement.GetProperty("path_display").GetString()!;
            }

            using var append = _http.Send(() => ContentCall("files/upload_session/append_v2", $$"""{"cursor":{{cursor}},"close":false}""", OneDriveStore.Octets(buffer, 0, count)), cancellationToken: cancellationToken);
            offset += count;
        }

        using (var finish = _http.SendJson(() => ContentCall("files/upload_session/finish",
            $$"""{"cursor":{"session_id":{{OneDriveStore.JsonString(session)}},"offset":{{offset}}},"commit":{{commit}}}""", OneDriveStore.Octets([], 0, 0)), cancellationToken))
        {
            return finish.RootElement.GetProperty("path_display").GetString()!;
        }
    }

    public string CreateFolder(string parentId, string name)
    {
        string path = parentId.TrimEnd('/') + "/" + name;
        try
        {
            using var json = _http.SendJson(() => Rpc("files/create_folder_v2", $$"""{"path":{{OneDriveStore.JsonString(path)}},"autorename":false}"""));
            return json.RootElement.GetProperty("metadata").GetProperty("path_display").GetString()!;
        }
        catch (IOException ex) when (ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            return path; // ya existía
        }
    }

    public void Delete(string objectId, bool recursive)
    {
        using var response = _http.Send(() => Rpc("files/delete_v2", $$"""{"path":{{OneDriveStore.JsonString(objectId)}}}"""));
    }

    public void Dispose() => _http.Dispose();
}
