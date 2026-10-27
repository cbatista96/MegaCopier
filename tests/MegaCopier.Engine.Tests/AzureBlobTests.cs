using System.Net;
using System.Security.Cryptography;
using System.Text;
using MegaCopier.Engine.Network;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine.Tests;

/// <summary>Azure Blob de prueba en 127.0.0.1: lista con prefijos, Put Blob, Put Block + Put Block List, Get y Delete, con SAS.</summary>
internal sealed class FakeAzureBlob : IDisposable
{
    private readonly FakeHttpServer _server;
    private readonly Dictionary<string, Dictionary<string, byte[]>> _blocks = [];

    public FakeAzureBlob() => _server = new FakeHttpServer(Handle);

    public SortedDictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    public string Account => $"http://127.0.0.1:{_server.Port}";

    public int Forbidden { get; private set; }

    private FakeResponse Handle(HttpRequest request, byte[] body)
    {
        lock (Blobs)
        {
            // El SAS viaja en la dirección; sin él (o con otra firma) Azure responde 403.
            if (request.Query("sig") != "firma-buena" || request.Headers.GetValueOrDefault("x-ms-version") is null)
            {
                Forbidden++;
                return new FakeResponse(403);
            }

            string path = Uri.UnescapeDataString(request.Path);
            Assert.StartsWith("/copias", path);
            string blob = path["/copias".Length..].TrimStart('/');
            switch (request.Method)
            {
                case "GET" when request.Query("comp") == "list":
                    return List(request.Query("prefix") ?? "", request.Query("marker"));
                case "GET":
                    return Blobs.TryGetValue(blob, out var data) ? new FakeResponse(200, data) : new FakeResponse(404);
                case "PUT" when request.Query("comp") == "block":
                    (_blocks.TryGetValue(blob, out var blocks) ? blocks : _blocks[blob] = [])[request.Query("blockid")!] = body;
                    return new FakeResponse(201);
                case "PUT" when request.Query("comp") == "blocklist":
                    var ids = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(body)).Root!.Elements("Latest").Select(e => e.Value).ToList();
                    Blobs[blob] = [.. ids.SelectMany(id => _blocks[blob][id])];
                    _blocks.Remove(blob);
                    return new FakeResponse(201);
                case "PUT":
                    Assert.Equal("BlockBlob", request.Headers["x-ms-blob-type"]);
                    Blobs[blob] = body;
                    return new FakeResponse(201);
                case "DELETE":
                    return Blobs.Remove(blob) ? new FakeResponse(202) : new FakeResponse(404);
                default:
                    return new FakeResponse(405);
            }
        }
    }

    /// <summary>Páginas de 3 con NextMarker, como Azure con maxresults.</summary>
    private FakeResponse List(string prefix, string? marker)
    {
        var entries = new List<(string Name, bool Prefix)>();
        foreach (string name in Blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            int slash = name.IndexOf('/', prefix.Length);
            var entry = slash >= 0 ? (name[..(slash + 1)], true) : (name, false);
            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        var remaining = entries.Where(e => string.IsNullOrEmpty(marker) || string.CompareOrdinal(e.Name, marker) >= 0).ToList();
        var page = remaining.Take(3).ToList();
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><EnumerationResults><Blobs>");
        foreach (var (name, isPrefix) in page)
        {
            xml.Append(isPrefix
                ? $"<BlobPrefix><Name>{WebUtility.HtmlEncode(name)}</Name></BlobPrefix>"
                : $"<Blob><Name>{WebUtility.HtmlEncode(name)}</Name><Properties><Last-Modified>Sun, 01 Jun 2025 10:00:00 GMT</Last-Modified><Content-Length>{Blobs[name].Length}</Content-Length></Properties></Blob>");
        }

        xml.Append("</Blobs><NextMarker>").Append(remaining.Count > 3 ? WebUtility.HtmlEncode(remaining[3].Name) : "").Append("</NextMarker></EnumerationResults>");
        return new FakeResponse(200, Encoding.UTF8.GetBytes(xml.ToString()));
    }

    public void Dispose() => _server.Dispose();
}

[Collection("Cloud")] // el tamaño de bloque es estático
public sealed class AzureBlobTests : IDisposable
{
    private readonly int _blockSize = AzureBlobStore.BlockSize;

    public AzureBlobTests() => AzureBlobStore.BlockSize = 1 << 20;

    public void Dispose() => AzureBlobStore.BlockSize = _blockSize;

    [Fact]
    public void ListsUploadsInBlocksDownloadsAndDeletes()
    {
        using var server = new FakeAzureBlob();
        var connection = new Connection { Kind = ConnectionKind.AzureBlob, Host = server.Account, Root = "copias/fotos" };
        using var store = Connections.Open(connection, "?sv=2021-08-06&sp=racwdl&sig=firma-buena");

        Assert.Equal("fotos/", store.RootId);
        string folder = store.CreateFolder(store.RootId, "Verano 2025");
        byte[] small = RandomNumberGenerator.GetBytes(300_000);
        byte[] big = RandomNumberGenerator.GetBytes(3_500_000); // 4 bloques de 1 MB
        string bigId = store.Upload(folder, "vídeo grande.mp4", new MemoryStream(big), big.Length, CancellationToken.None);
        store.Upload(folder, "foto.jpg", new MemoryStream(small), small.Length, CancellationToken.None);
        for (int i = 0; i < 3; i++)
        {
            store.Upload(folder, $"nota{i}.txt", new MemoryStream([(byte)i]), 1, CancellationToken.None);
        }

        Assert.Equal(big, server.Blobs["fotos/Verano 2025/vídeo grande.mp4"]);
        var children = store.Children(folder); // 5 archivos en páginas de 3 (el marcador de la carpeta no cuenta)
        Assert.Equal(5, children.Count);
        Assert.Equal(big.Length, children.Single(c => c.Name == "vídeo grande.mp4").Size);
        Assert.Contains(store.Children(store.RootId), c => c.IsFolder && c.Name == "Verano 2025");

        using (var read = store.OpenRead(bigId))
        using (var copy = new MemoryStream())
        {
            read.CopyTo(copy);
            Assert.Equal(big, copy.ToArray());
        }

        store.Delete(folder, recursive: true);
        Assert.Empty(server.Blobs);
    }

    [Fact]
    public void AWrongSasIsReportedAsAPermissionProblem()
    {
        using var server = new FakeAzureBlob();
        var connection = new Connection { Kind = ConnectionKind.AzureBlob, Host = server.Account, Root = "copias" };
        using var store = Connections.Open(connection, "sv=2021-08-06&sig=caducada");
        var error = Assert.Throws<IOException>(() => store.Children(store.RootId));
        Assert.Contains("403", error.Message);
        Assert.Equal(1, server.Forbidden);
    }

    [Fact]
    public void GoogleCloudStorageUsesTheS3InteroperableApi()
    {
        using var server = new FakeS3("clave-hmac");
        var connection = new Connection { Kind = ConnectionKind.GoogleCloudStorage, Host = server.Endpoint, User = "GOOG1EXAMPLE", Root = "cubo" };
        using var store = Connections.Open(connection, "clave-hmac");
        store.Upload(store.RootId, "a.txt", new MemoryStream("hola"u8.ToArray()), 4, CancellationToken.None);
        Assert.Equal("hola"u8.ToArray(), server.Objects["a.txt"]);
        Assert.Equal(0, server.BadSignatures); // firmado con la región «auto» de serie
    }
}
