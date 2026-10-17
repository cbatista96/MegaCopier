using System.Net;
using System.Text.Json;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine.Tests;

public sealed class RemoteControlTests : IDisposable
{
    private readonly List<(string Id, string Action)> _actions = [];
    private readonly RemoteControlServer _server;
    private readonly HttpClient _http = new();

    public RemoteControlTests()
    {
        var labels = new Dictionary<string, string> { ["code"] = "Código", ["enter"] = "Entrar", ["empty"] = "Nada", ["pause"] = "Pausar", ["resume"] = "Seguir", ["cancel"] = "Cancelar", ["wrong"] = "Mal" };
        _server = new RemoteControlServer("ABCD2345", () => [new RemoteJobView("j1", "fotos → NAS <b>", "Copiando", 42.5, "35 MB/s · 2 min", true, false, true)],
            (id, action) =>
            {
                _actions.Add((id, action));
                return id == "j1";
            }, labels, port: 0, loopbackOnly: true);
        _server.Start();
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}");
    }

    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? code)
    {
        var request = new HttpRequestMessage(method, path);
        if (code is not null)
        {
            request.Headers.Add("X-MegaCopier-Code", code);
        }

        return request;
    }

    [Fact]
    public async Task ThePageIsServedAndTheJobsNeedTheCode()
    {
        string page = await _http.GetStringAsync("/");
        Assert.Contains("\"pause\":\"Pausar\"", page);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Request(HttpMethod.Get, "/api/jobs", null))).StatusCode);
        var response = await _http.SendAsync(Request(HttpMethod.Get, "/api/jobs", "abcd2345")); // sin distinguir mayúsculas
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("fotos → NAS <b>", json.RootElement[0].GetProperty("title").GetString());
        Assert.Equal(42.5, json.RootElement[0].GetProperty("percent").GetDouble());
    }

    [Fact]
    public async Task ActionsReachTheAppAndUnknownJobsAre404()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(Request(HttpMethod.Post, "/api/jobs/j1/pause", "ABCD2345"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Request(HttpMethod.Post, "/api/jobs/otro/cancel", "ABCD2345"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Request(HttpMethod.Post, "/api/jobs/j1/borrar", "ABCD2345"))).StatusCode);
        Assert.Equal([("j1", "pause"), ("otro", "cancel")], _actions);
    }

    [Fact]
    public async Task GuessingTheCodeGetsBlocked()
    {
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Request(HttpMethod.Get, "/api/jobs", $"MAL{i}"))).StatusCode);
        }

        Assert.Equal((HttpStatusCode)429, (await _http.SendAsync(Request(HttpMethod.Get, "/api/jobs", "ABCD2345"))).StatusCode); // ni con el bueno
    }

    [Fact]
    public void CodesAreEightUnambiguousCharacters()
    {
        string code = RemoteControlServer.NewCode();
        Assert.Equal(8, code.Length);
        Assert.DoesNotContain(code, c => "01OI".Contains(c));
    }
}
