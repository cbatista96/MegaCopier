using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCopier.Engine.Network;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine.Tests;

/// <summary>Un archivo o carpeta en una nube de prueba (en memoria).</summary>
internal sealed class CloudNode
{
    public string Id { get; init; } = "";

    public string Name { get; set; } = "";

    public bool IsFolder { get; init; }

    public string ParentId { get; set; } = "";

    public byte[] Data { get; set; } = [];

    public DateTime Modified { get; set; } = DateTime.UtcNow;

    public bool Trashed { get; set; }
}

/// <summary>
/// Base de las nubes de prueba: el servidor de tokens de OAuth (códigos, tokens de acceso y de renovación, que se pueden
/// "caducar" para probar la renovación) y el árbol de archivos en memoria. Las páginas del listado son de 2 elementos
/// para probar la paginación.
/// </summary>
internal abstract class FakeCloud : IDisposable
{
    protected const int PageSize = 2;

    private readonly FakeHttpServer _server;
    private int _tokens;
    private readonly HashSet<string> _valid = [];

    protected FakeCloud()
    {
        _server = new FakeHttpServer((request, body) =>
        {
            lock (Nodes)
            {
                if (request.Path == "/authorize")
                {
                    // "Navegador": vuelve a la dirección de la app con un código.
                    string redirect = request.Query("redirect_uri")!;
                    return new FakeResponse(302, Headers: new() { ["Location"] = $"{redirect}?code=CODIGO&state={request.Query("state")}" });
                }

                if (request.Path == "/token")
                {
                    return Token(Encoding.UTF8.GetString(body));
                }

                if (OpenPath(request.Path))
                {
                    return Handle(request, body);
                }

                string? auth = request.Headers.GetValueOrDefault("Authorization");
                return auth is null || !_valid.Contains(auth["Bearer ".Length..]) ? new FakeResponse(401) : Handle(request, body);
            }
        });
        Nodes[RootId] = new CloudNode { Id = RootId, IsFolder = true };
    }

    public string Url => $"http://127.0.0.1:{_server.Port}/";

    public Dictionary<string, CloudNode> Nodes { get; } = [];

    public List<string> Requests => _server.Requests;

    public abstract string RootId { get; }

    public abstract ConnectionKind Kind { get; }

    /// <summary>Los tokens de acceso dados hasta ahora dejan de valer (como si hubieran caducado).</summary>
    public void ExpireAccessTokens()
    {
        lock (Nodes)
        {
            _valid.Clear();
        }
    }

    public Connection Connection() => new()
    {
        Name = "Nube",
        Kind = Kind,
        Host = Url,
        ClientId = "app-de-prueba",
        AuthUrl = Url + "authorize",
        TokenUrl = Url + "token",
        Site = Kind == ConnectionKind.SharePoint ? "https://empresa.sharepoint.com/sites/Equipo" : "",
    };

    protected virtual bool OpenPath(string path) => false;

    private FakeResponse Token(string form)
    {
        var fields = form.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        if (fields["client_id"] != "app-de-prueba")
        {
            return new FakeResponse(400, "{\"error\":\"invalid_client\"}"u8.ToArray());
        }

        bool ok = fields["grant_type"] switch
        {
            "authorization_code" => fields["code"] == "CODIGO" && fields.ContainsKey("code_verifier"),
            "refresh_token" => fields["refresh_token"].StartsWith("RT-", StringComparison.Ordinal),
            _ => false,
        };
        if (!ok)
        {
            return new FakeResponse(400, "{\"error\":\"invalid_grant\"}"u8.ToArray());
        }

        string access = "AT-" + Interlocked.Increment(ref _tokens);
        _valid.Add(access);
        return Json(new JsonObject { ["access_token"] = access, ["expires_in"] = 3600, ["refresh_token"] = "RT-" + _tokens });
    }

    protected abstract FakeResponse Handle(HttpRequest request, byte[] body);

    protected static FakeResponse Json(JsonNode node, int status = 200) => new(status, Encoding.UTF8.GetBytes(node.ToJsonString()));

    protected IEnumerable<CloudNode> ChildrenOf(string id) => Nodes.Values.Where(n => n.ParentId == id && n.Id != RootId && !n.Trashed).OrderBy(n => n.Name, StringComparer.Ordinal);

    protected CloudNode Add(string parentId, string name, bool folder, byte[]? data = null)
    {
        var existing = ChildrenOf(parentId).FirstOrDefault(n => n.Name == name && n.IsFolder == folder);
        if (existing is not null && !folder)
        {
            existing.Data = data ?? [];
            existing.Modified = DateTime.UtcNow;
            return existing;
        }

        var node = new CloudNode { Id = NewId(parentId, name), Name = name, IsFolder = folder, ParentId = parentId, Data = data ?? [] };
        Nodes[node.Id] = node;
        return node;
    }

    protected virtual string NewId(string parentId, string name) => "id" + Nodes.Count.ToString(CultureInfo.InvariantCulture);

    public void Dispose() => _server.Dispose();
}

/// <summary>OneDrive de prueba (Microsoft Graph).</summary>
/// <param name="sharePoint">La biblioteca de un sitio de SharePoint: rutas «sites/{id}/drive» en lugar de «me/drive».</param>
internal sealed class FakeOneDrive(bool sharePoint = false) : FakeCloud
{
    private readonly Dictionary<string, (string Parent, string Name, MemoryStream Data)> _sessions = [];

    public override string RootId => "raiz";

    public override ConnectionKind Kind => sharePoint ? ConnectionKind.SharePoint : ConnectionKind.OneDrive;

    /// <summary>La dirección del sitio que pidió el cliente («empresa.sharepoint.com:/sites/Equipo»).</summary>
    public string? SiteRequested { get; private set; }

    protected override bool OpenPath(string path) => path.StartsWith("/download/", StringComparison.Ordinal) || path.StartsWith("/session/", StringComparison.Ordinal);

    protected override FakeResponse Handle(HttpRequest request, byte[] body)
    {
        string path = Uri.UnescapeDataString(request.Path);
        if (path.StartsWith("/download/", StringComparison.Ordinal))
        {
            return new FakeResponse(200, Nodes[path["/download/".Length..]].Data);
        }

        if (path.StartsWith("/session/", StringComparison.Ordinal))
        {
            Assert.Null(request.Headers.GetValueOrDefault("Authorization")); // OneDrive no quiere el token aquí
            var (parent, name, data) = _sessions[path["/session/".Length..]];
            data.Write(body);
            string range = request.Headers["Content-Range"]; // bytes a-b/total
            long total = long.Parse(range[(range.IndexOf('/') + 1)..], CultureInfo.InvariantCulture);
            return data.Length < total ? Json(new JsonObject(), 202) : Json(new JsonObject { ["id"] = Add(parent, name, false, data.ToArray()).Id }, 201);
        }

        if (sharePoint)
        {
            // sites/{servidor}:/{ruta} da el id del sitio; después todo va por sites/{id}/drive.
            if (path.StartsWith("/sites/", StringComparison.Ordinal) && !path.Contains("/drive", StringComparison.Ordinal))
            {
                SiteRequested = path["/sites/".Length..];
                return Json(new JsonObject { ["id"] = "sitio-1" });
            }

            if (!path.StartsWith("/sites/sitio-1/drive", StringComparison.Ordinal))
            {
                return new FakeResponse(404);
            }

            path = "/me/drive" + path["/sites/sitio-1/drive".Length..];
        }

        string items = "/me/drive/items/";
        if (path.StartsWith("/me/drive/root:/", StringComparison.Ordinal))
        {
            string id = RootId;
            foreach (string part in path["/me/drive/root:/".Length..].Split('/'))
            {
                id = ChildrenOf(id).First(n => n.Name == part).Id;
            }

            return Json(new JsonObject { ["id"] = id });
        }

        if (path.StartsWith(items, StringComparison.Ordinal) && path.Contains(':', StringComparison.Ordinal))
        {
            // items/{padre}:/{nombre}:/content  o  :/createUploadSession
            string[] parts = path[items.Length..].Split(':');
            string parent = parts[0], name = parts[1].Trim('/');
            if (parts[2] == "/content" && request.Method == "PUT")
            {
                return Json(new JsonObject { ["id"] = Add(parent, name, false, body).Id });
            }

            string session = "s" + _sessions.Count;
            _sessions[session] = (parent, name, new MemoryStream());
            return Json(new JsonObject { ["uploadUrl"] = $"{Url}session/{session}" });
        }

        string rest = path[items.Length..];
        string itemId = rest.Split('/')[0];
        if (rest.EndsWith("/children", StringComparison.Ordinal) && request.Method == "GET")
        {
            int skip = int.TryParse(request.Query("skip"), out int s) ? s : 0;
            var all = ChildrenOf(itemId).ToList();
            var value = new JsonArray([.. all.Skip(skip).Take(PageSize).Select(n => (JsonNode)(n.IsFolder
                ? new JsonObject { ["id"] = n.Id, ["name"] = n.Name, ["folder"] = new JsonObject(), ["lastModifiedDateTime"] = n.Modified.ToString("O") }
                : new JsonObject { ["id"] = n.Id, ["name"] = n.Name, ["size"] = n.Data.Length, ["file"] = new JsonObject(), ["lastModifiedDateTime"] = n.Modified.ToString("O") }))]);
            var page = new JsonObject { ["value"] = value };
            if (skip + PageSize < all.Count)
            {
                page["@odata.nextLink"] = $"{Url}{(sharePoint ? "sites/sitio-1/drive" : "me/drive")}/items/{itemId}/children?skip={skip + PageSize}";
            }

            return Json(page);
        }

        if (rest.EndsWith("/children", StringComparison.Ordinal) && request.Method == "POST")
        {
            string name = JsonNode.Parse(body)!["name"]!.GetValue<string>();
            return ChildrenOf(itemId).Any(n => n.Name == name) ? new FakeResponse(409) : Json(new JsonObject { ["id"] = Add(itemId, name, true).Id }, 201);
        }

        if (rest.EndsWith("/content", StringComparison.Ordinal))
        {
            return new FakeResponse(302, Headers: new() { ["Location"] = $"{Url}download/{itemId}" });
        }

        if (request.Method == "DELETE")
        {
            Nodes[itemId].Trashed = true;
            return new FakeResponse(204);
        }

        return new FakeResponse(404);
    }
}

/// <summary>Google Drive de prueba (API v3).</summary>
internal sealed class FakeGoogleDrive : FakeCloud
{
    private readonly Dictionary<string, (string Parent, string Name)> _uploads = [];

    public override string RootId => "root";

    public override ConnectionKind Kind => ConnectionKind.GoogleDrive;

    /// <summary>Un documento de Google (no descargable): no debe aparecer.</summary>
    public void AddGoogleDoc(string name) => Nodes["doc-" + name] = new CloudNode { Id = "doc-" + name, Name = name + "#gdoc", ParentId = RootId };

    protected override FakeResponse Handle(HttpRequest request, byte[] body)
    {
        string path = Uri.UnescapeDataString(request.Path);
        if (path == "/drive/v3/files" && request.Method == "GET")
        {
            string query = Uri.UnescapeDataString(request.Query("q")!);
            string parent = query[1..query.IndexOf('\'', 1)];
            int skip = int.TryParse(request.Query("pageToken"), out int s) ? s : 0;
            var all = ChildrenOf(parent).ToList();
            var files = new JsonArray([.. all.Skip(skip).Take(PageSize).Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.Id,
                ["name"] = n.Name.Replace("#gdoc", ""),
                ["mimeType"] = n.IsFolder ? "application/vnd.google-apps.folder" : n.Name.EndsWith("#gdoc", StringComparison.Ordinal) ? "application/vnd.google-apps.document" : "application/octet-stream",
                ["size"] = n.IsFolder ? null : n.Data.Length.ToString(CultureInfo.InvariantCulture),
                ["modifiedTime"] = n.Modified.ToString("O"),
            })]);
            var page = new JsonObject { ["files"] = files };
            if (skip + PageSize < all.Count)
            {
                page["nextPageToken"] = (skip + PageSize).ToString(CultureInfo.InvariantCulture);
            }

            return Json(page);
        }

        if (path == "/drive/v3/files" && request.Method == "POST")
        {
            var metadata = JsonNode.Parse(body)!;
            return Json(new JsonObject { ["id"] = Add(metadata["parents"]![0]!.GetValue<string>(), metadata["name"]!.GetValue<string>(), true).Id });
        }

        if (path == "/upload/drive/v3/files")
        {
            var metadata = JsonNode.Parse(body)!;
            string upload = "u" + _uploads.Count;
            _uploads[upload] = (metadata["parents"]![0]!.GetValue<string>(), metadata["name"]!.GetValue<string>());
            return new FakeResponse(200, [], new() { ["Location"] = $"{Url}upload/session/{upload}" });
        }

        if (path.StartsWith("/upload/session/", StringComparison.Ordinal))
        {
            var (parent, name) = _uploads[path["/upload/session/".Length..]];
            var node = new CloudNode { Id = "id" + Nodes.Count, Name = name, ParentId = parent, Data = body }; // Drive permite nombres repetidos
            Nodes[node.Id] = node;
            return Json(new JsonObject { ["id"] = node.Id });
        }

        string id = path["/drive/v3/files/".Length..];
        if (request.Method == "PATCH")
        {
            Nodes[id].Trashed = JsonNode.Parse(body)!["trashed"]!.GetValue<bool>();
            return Json(new JsonObject { ["id"] = id });
        }

        return request.Query("alt") == "media" ? new FakeResponse(200, Nodes[id].Data) : new FakeResponse(404);
    }
}

/// <summary>Dropbox de prueba (API v2): ids = rutas.</summary>
internal sealed class FakeDropbox : FakeCloud
{
    private readonly Dictionary<string, MemoryStream> _sessions = [];

    public override string RootId => "";

    public override ConnectionKind Kind => ConnectionKind.Dropbox;

    protected override string NewId(string parentId, string name) => parentId + "/" + name;

    private static JsonObject Metadata(CloudNode n) => n.IsFolder
        ? new JsonObject { [".tag"] = "folder", ["name"] = n.Name, ["path_display"] = n.Id }
        : new JsonObject { [".tag"] = "file", ["name"] = n.Name, ["path_display"] = n.Id, ["size"] = n.Data.Length, ["client_modified"] = n.Modified.ToString("yyyy-MM-ddTHH:mm:ssZ") };

    private (string Parent, string Name) Split(string path) => (path[..path.LastIndexOf('/')], path[(path.LastIndexOf('/') + 1)..]);

    protected override FakeResponse Handle(HttpRequest request, byte[] body)
    {
        string endpoint = request.Path["/2/".Length..];
        var argument = request.Headers.TryGetValue("Dropbox-API-Arg", out string? header) ? JsonNode.Parse(header)! : JsonNode.Parse(body.Length > 0 && endpoint is not ("files/upload" or "files/upload_session/start" or "files/upload_session/append_v2" or "files/upload_session/finish") ? body : "{}"u8.ToArray())!;
        if (header is not null)
        {
            Assert.All(header, c => Assert.True(c < 128)); // la cabecera tiene que ser ASCII
        }

        switch (endpoint)
        {
            case "files/list_folder":
            case "files/list_folder/continue":
            {
                string path = endpoint == "files/list_folder" ? argument["path"]!.GetValue<string>() : argument["cursor"]!.GetValue<string>().Split('|')[0];
                int skip = endpoint == "files/list_folder" ? 0 : int.Parse(argument["cursor"]!.GetValue<string>().Split('|')[1], CultureInfo.InvariantCulture);
                var all = ChildrenOf(path).ToList();
                return Json(new JsonObject
                {
                    ["entries"] = new JsonArray([.. all.Skip(skip).Take(PageSize).Select(n => (JsonNode)Metadata(n))]),
                    ["has_more"] = skip + PageSize < all.Count,
                    ["cursor"] = $"{path}|{skip + PageSize}",
                });
            }

            case "files/download":
                return Nodes.TryGetValue(argument["path"]!.GetValue<string>(), out var file) ? new FakeResponse(200, file.Data) : new FakeResponse(409);
            case "files/upload":
            {
                var (parent, name) = Split(argument["path"]!.GetValue<string>());
                return Json(Metadata(Add(parent, name, false, body)));
            }

            case "files/upload_session/start":
            {
                string session = "s" + _sessions.Count;
                _sessions[session] = new MemoryStream();
                _sessions[session].Write(body);
                return Json(new JsonObject { ["session_id"] = session });
            }

            case "files/upload_session/append_v2":
            case "files/upload_session/finish":
            {
                var data = _sessions[argument["cursor"]!["session_id"]!.GetValue<string>()];
                Assert.Equal(data.Length, argument["cursor"]!["offset"]!.GetValue<long>());
                data.Write(body);
                if (endpoint.EndsWith("append_v2", StringComparison.Ordinal))
                {
                    return new FakeResponse(200, "null"u8.ToArray());
                }

                var (parent, name) = Split(argument["commit"]!["path"]!.GetValue<string>());
                return Json(Metadata(Add(parent, name, false, data.ToArray())));
            }

            case "files/create_folder_v2":
            {
                var (parent, name) = Split(argument["path"]!.GetValue<string>());
                return ChildrenOf(parent).Any(n => n.Name == name)
                    ? new FakeResponse(409, "{\"error_summary\":\"path/conflict/folder/..\"}"u8.ToArray())
                    : Json(new JsonObject { ["metadata"] = Metadata(Add(parent, name, true)) });
            }

            case "files/delete_v2":
                Nodes[argument["path"]!.GetValue<string>()].Trashed = true;
                return Json(new JsonObject { ["metadata"] = new JsonObject() });
            default:
                return new FakeResponse(404);
        }
    }
}
