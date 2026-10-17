using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine.Tests;

/// <summary>Un móvil con LocalSend de mentira (HttpClient) contra el receptor en 127.0.0.1.</summary>
public sealed class LocalSendTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly List<PeerEvent> _events = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private LocalSendReceiver? _receiver;
    private bool _accept = true;
    private LocalSendOffer? _offer;

    public void Dispose()
    {
        _receiver?.Dispose();
        _http.Dispose();
        _temp.Dispose();
    }

    private string Folder => _temp.Combine("recibido");

    private string Url(string action) => $"http://127.0.0.1:{_receiver!.Port}/api/localsend/v2/{action}";

    private void Start()
    {
        _receiver = new LocalSendReceiver(Folder, "PC de prueba", "huella-pc", offer =>
        {
            _offer = offer;
            return Task.FromResult(_accept);
        }, port: 0, loopbackOnly: true);
        _receiver.Event += e =>
        {
            lock (_events)
            {
                _events.Add(e);
            }
        };
        _receiver.Start();
    }

    private static string Prepare(params (string Id, string Name, byte[] Data, bool Hash)[] files)
    {
        var json = new StringBuilder("""{"info":{"alias":"Pixel de Ana","version":"2.1","deviceModel":"Pixel 8","deviceType":"mobile","fingerprint":"abc","port":53317,"protocol":"https","download":false},"files":{""");
        json.Append(string.Join(',', files.Select(f =>
            $$$"""
            "{{{f.Id}}}":{"id":"{{{f.Id}}}","fileName":"{{{f.Name}}}","size":{{{f.Data.Length}}},"fileType":"image/jpeg","sha256":{{{(f.Hash ? $"\"{Convert.ToHexString(SHA256.HashData(f.Data)).ToLowerInvariant()}\"" : "null")}}},"preview":null,"metadata":{"modified":"2025-06-01T10:20:30Z"}}
            """)));
        json.Append("}}");
        return json.ToString();
    }

    private async Task<(HttpStatusCode Status, string? Session, Dictionary<string, string>? Tokens)> PrepareAsync(string body)
    {
        var response = await _http.PostAsync(Url("prepare-upload"), new StringContent(body, Encoding.UTF8, "application/json"));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, null, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tokens = doc.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        return (response.StatusCode, doc.RootElement.GetProperty("sessionId").GetString(), tokens);
    }

    private Task<HttpResponseMessage> UploadAsync(string session, string id, string token, byte[] data, bool chunked = false)
    {
        HttpContent content = chunked ? new StreamContent(new MemoryStream(data)) : new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var request = new HttpRequestMessage(HttpMethod.Post, Url($"upload?sessionId={session}&fileId={id}&token={token}")) { Content = content };
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }

        return _http.SendAsync(request);
    }

    [Fact]
    public async Task ReceivesFilesWithFoldersDatesAndHashes()
    {
        Start();
        byte[] photo = RandomNumberGenerator.GetBytes(3_000_000);
        byte[] note = Encoding.UTF8.GetBytes("hola desde el móvil");
        var (status, session, tokens) = await PrepareAsync(Prepare(("f1", "Vacaciones/IMG_1.jpg", photo, true), ("f2", "nota.txt", note, false)));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new LocalSendOffer("Pixel de Ana", "Pixel 8", 2, photo.Length + note.Length), _offer);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(session!, "f1", tokens!["f1"], photo, chunked: true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(session!, "f2", tokens["f2"], note)).StatusCode);

        Assert.Equal(photo, File.ReadAllBytes(Path.Combine(Folder, "Vacaciones", "IMG_1.jpg")));
        Assert.Equal("hola desde el móvil", File.ReadAllText(Path.Combine(Folder, "nota.txt")));
        Assert.Equal(new DateTime(2025, 6, 1, 10, 20, 30, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(Folder, "nota.txt")));
        Assert.Empty(Directory.GetFiles(Folder, "*.part", SearchOption.AllDirectories));
        lock (_events)
        {
            Assert.Equal([PeerEventKind.Started, PeerEventKind.Finished], _events.Select(e => e.Kind));
            Assert.Equal(2, _events[1].Files);
        }
    }

    [Fact]
    public async Task RejectedOffersAndWrongTokensAreRefused()
    {
        Start();
        _accept = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await PrepareAsync(Prepare(("f1", "a.jpg", [1, 2, 3], false)))).Status);
        Assert.False(Directory.Exists(Folder) && Directory.EnumerateFileSystemEntries(Folder).Any());

        _accept = true;
        var (_, session, tokens) = await PrepareAsync(Prepare(("f1", "a.jpg", [1, 2, 3], false)));
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(session!, "f1", "token-falso", [1, 2, 3])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync("otra-sesion", "f1", tokens!["f1"], [1, 2, 3])).StatusCode);

        // Mientras dura un envío, otro tiene que esperar.
        Assert.Equal(HttpStatusCode.Conflict, (await PrepareAsync(Prepare(("x", "b.jpg", [9], false)))).Status);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(session!, "f1", tokens["f1"], [1, 2, 3])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(session!, "f1", tokens["f1"], [1, 2, 3])).StatusCode); // ya llegó
    }

    [Fact]
    public async Task NamesCannotEscapeTheFolderAndNothingIsOverwritten()
    {
        Start();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "a.txt"), "ya estaba");
        var (_, session, tokens) = await PrepareAsync(Prepare(("f1", "../../fuera.txt", "malo"u8.ToArray(), false), ("f2", "a.txt", "nuevo"u8.ToArray(), false), ("f3", "C:/Windows/x:y?.txt", "raro"u8.ToArray(), false)));

        foreach (var (id, data) in new[] { ("f1", "malo"u8.ToArray()), ("f2", "nuevo"u8.ToArray()), ("f3", "raro"u8.ToArray()) })
        {
            Assert.Equal(HttpStatusCode.OK, (await UploadAsync(session!, id, tokens![id], data)).StatusCode);
        }

        Assert.False(File.Exists(_temp.Combine("fuera.txt")));
        Assert.Equal("malo", File.ReadAllText(Path.Combine(Folder, "fuera.txt")));
        Assert.Equal("ya estaba", File.ReadAllText(Path.Combine(Folder, "a.txt")));
        Assert.Equal("nuevo", File.ReadAllText(Path.Combine(Folder, "a (2).txt")));
        Assert.Equal("raro", File.ReadAllText(Path.Combine(Folder, "C_", "Windows", "x_y_.txt")));
    }

    [Fact]
    public async Task ABadHashIsRejectedAndCanBeResent()
    {
        Start();
        byte[] data = RandomNumberGenerator.GetBytes(10_000);
        var (_, session, tokens) = await PrepareAsync(Prepare(("f1", "a.bin", data, true)));
        byte[] damaged = (byte[])data.Clone();
        damaged[5000] ^= 0xFF;

        Assert.Equal(HttpStatusCode.InternalServerError, (await UploadAsync(session!, "f1", tokens!["f1"], damaged)).StatusCode);
        Assert.False(File.Exists(Path.Combine(Folder, "a.bin")));
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(session!, "f1", tokens["f1"], data)).StatusCode);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Folder, "a.bin")));
    }

    [Fact]
    public async Task CancelDeletesTheSessionAndInfoAnswers()
    {
        Start();
        var info = await _http.GetStringAsync(Url("info"));
        Assert.Contains("\"alias\":\"PC de prueba\"", info);
        Assert.Contains("\"protocol\":\"http\"", info);
        var register = await _http.PostAsync(Url("register"), new StringContent("""{"alias":"Pixel","version":"2.1","fingerprint":"x"}"""));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var (_, session, _) = await PrepareAsync(Prepare(("f1", "a.jpg", [1], false)));
        Assert.Equal(HttpStatusCode.OK, (await _http.PostAsync(Url($"cancel?sessionId={session}"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PrepareAsync(Prepare(("f2", "b.jpg", [2], false)))).Status); // ya no está ocupado
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"http://127.0.0.1:{_receiver!.Port}/otra/cosa")).StatusCode);
    }

    [Theory]
    [InlineData("a/b/c.jpg", @"a\b\c.jpg")]
    [InlineData("../x.jpg", "x.jpg")]
    [InlineData("/etc/passwd", @"etc\passwd")]
    [InlineData("..", null)]
    [InlineData("con. ", "_con")]
    [InlineData("fotos/COM1.txt", @"fotos\_COM1.txt")]
    public void SafeRelativePaths(string name, string? expected) => Assert.Equal(expected, LocalSendProtocol.SafeRelativePath(name));

    [Fact]
    public void MegaCopierSendsToLocalSend()
    {
        Start();
        string source = _temp.Combine("origen");
        Directory.CreateDirectory(Path.Combine(source, "fotos"));
        byte[] photo = RandomNumberGenerator.GetBytes(2_500_000);
        File.WriteAllBytes(Path.Combine(source, "fotos", "playa.jpg"), photo);
        File.WriteAllText(Path.Combine(source, "nota ñ.txt"), "hola");
        CopyProgress last = default;
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Send, ProgressInterval = TimeSpan.Zero });
        engine.ProgressChanged += p => last = p;

        var result = engine.Run(new CopyJobRequest([source], $"localsend://127.0.0.1:{_receiver!.Port}/http/huella-pc"));

        Assert.Equal(2, result.FilesCopied);
        Assert.Equal(2, _offer!.Files);
        Assert.Equal(photo, File.ReadAllBytes(Path.Combine(Folder, "origen", "fotos", "playa.jpg")));
        Assert.Equal("hola", File.ReadAllText(Path.Combine(Folder, "origen", "nota ñ.txt")));
        Assert.Equal(last.TotalBytes, last.CompletedBytes);
    }

    [Fact]
    public void ARejectedLocalSendTransferIsReported()
    {
        Start();
        _accept = false;
        File.WriteAllText(_temp.Combine("a.txt"), "a");
        var engine = new CopyEngine(new CopyOptions { Operation = JobOperation.Send });
        Assert.Throws<CopyPlanException>(() => engine.Run(new CopyJobRequest([_temp.Combine("a.txt")], $"localsend://127.0.0.1:{_receiver!.Port}/http/")));
        Assert.False(File.Exists(Path.Combine(Folder, "a.txt")));
    }

    [Theory]
    [InlineData("localsend://192.168.1.20:53317/https/ABCD", "192.168.1.20", 53317, true, "ABCD")]
    [InlineData("localsend://10.0.0.5:4000/http/", "10.0.0.5", 4000, false, "")]
    [InlineData("localsend://[fe80::1]:53317/https/X", "fe80::1", 53317, true, "X")]
    public void LocalSendDestinationsParse(string destination, string host, int port, bool https, string fingerprint) =>
        Assert.Equal((host, port, https, fingerprint), LocalSendSender.Parse(destination));
}
