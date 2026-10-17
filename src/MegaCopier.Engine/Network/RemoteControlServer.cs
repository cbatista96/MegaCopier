using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Network;

/// <summary>Una copia tal como se ve desde el móvil.</summary>
public sealed record RemoteJobView(string Id, string Title, string State, double Percent, string Detail, bool CanPause, bool CanResume, bool CanCancel);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<RemoteJobView>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class RemoteJsonContext : JsonSerializerContext;

/// <summary>
/// Control desde el móvil: una página web pequeña en la red local (http://ip-del-pc:53318) para ver las copias, su
/// progreso, y pausarlas, seguirlas o cancelarlas desde el navegador del móvil, sin instalar nada. Hace falta el código
/// que muestra MegaCopier en Ajustes; tras 10 códigos malos desde la misma dirección, se bloquea 10 minutos.
/// </summary>
public sealed class RemoteControlServer : IDisposable
{
    public const int DefaultPort = 53318;
    private const int MaxFailures = 10;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(10);

    private readonly byte[] _code;
    private readonly Func<IReadOnlyList<RemoteJobView>> _jobs;
    private readonly Func<string, string, bool> _act;
    private readonly Dictionary<string, string> _labels;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<IPAddress, (int Count, DateTime Until)> _failures = new();

    /// <param name="code">El código que hay que escribir en el móvil.</param>
    /// <param name="jobs">Las copias de ahora.</param>
    /// <param name="act">Hace "pause", "resume" o "cancel" en una copia; false si ya no existe.</param>
    /// <param name="labels">Textos de la página en el idioma de la app (title, code, enter, empty, pause, resume, cancel, wrong).</param>
    /// <param name="loopbackOnly">Pruebas: solo este equipo (sin aviso del cortafuegos).</param>
    public RemoteControlServer(string code, Func<IReadOnlyList<RemoteJobView>> jobs, Func<string, string, bool> act, Dictionary<string, string> labels,
        int port = DefaultPort, bool loopbackOnly = false)
    {
        _code = Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant());
        _jobs = jobs;
        _act = act;
        _labels = labels;
        _listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Un código nuevo: 8 letras y cifras sin las que se confunden (0/O, 1/I...).</summary>
    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string([.. Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])]);
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            var stream = client.GetStream();
            try
            {
                while (await HttpRequest.ReadAsync(stream, _stop.Token).ConfigureAwait(false) is { } request)
                {
                    var (status, type, body) = Handle(request, remote);
                    string reason = status switch { 200 => "OK", 204 => "No Content", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", _ => "Bad Request" };
                    string head = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {body.Length}\r\nContent-Type: {type}\r\nCache-Control: no-store\r\n"
                        + "X-Content-Type-Options: nosniff\r\nConnection: keep-alive\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
                    await stream.WriteAsync(body).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
            }
        }
    }

    private (int Status, string Type, byte[] Body) Handle(HttpRequest request, IPAddress remote)
    {
        if (request.Method == "GET" && request.Path is "/" or "/index.html")
        {
            return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()));
        }

        if (!request.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return (404, "text/plain", []);
        }

        if (_failures.TryGetValue(remote, out var failures) && failures.Count >= MaxFailures && failures.Until > DateTime.UtcNow)
        {
            return (429, "text/plain", []);
        }

        byte[] given = Encoding.UTF8.GetBytes((request.Headers.GetValueOrDefault("X-MegaCopier-Code") ?? "").Trim().ToUpperInvariant());
        if (!CryptographicOperations.FixedTimeEquals(given, _code))
        {
            _failures.AddOrUpdate(remote, _ => (1, DateTime.UtcNow + Lockout), (_, old) => (old.Count + 1, DateTime.UtcNow + Lockout));
            return (401, "text/plain", []);
        }

        _failures.TryRemove(remote, out _);
        if (request.Method == "GET" && request.Path == "/api/jobs")
        {
            return (200, "application/json", JsonSerializer.SerializeToUtf8Bytes([.. _jobs()], RemoteJsonContext.Default.ListRemoteJobView));
        }

        // POST /api/jobs/{id}/{pause|resume|cancel}
        string[] parts = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (request.Method == "POST" && parts.Length == 4 && parts[1] == "jobs" && parts[3] is "pause" or "resume" or "cancel")
        {
            return _act(Uri.UnescapeDataString(parts[2]), parts[3]) ? (204, "text/plain", []) : (404, "text/plain", []);
        }

        return (404, "text/plain", []);
    }

    private string Page()
    {
        string labels = JsonSerializer.Serialize(_labels, RemoteJsonContext.Default.DictionaryStringString);
        return """
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>MegaCopier</title>
            <style>
            :root{--bg:#f5f5f7;--card:#fff;--text:#1c1c1e;--muted:#6e6e73;--accent:#3a6df0;--bar:#e5e5ea}
            @media (prefers-color-scheme:dark){:root{--bg:#1c1c1e;--card:#2c2c2e;--text:#f2f2f7;--muted:#a1a1a6;--accent:#6e95ff;--bar:#3a3a3c}}
            body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:var(--bg);color:var(--text)}
            header{padding:18px 16px 6px;font-size:22px;font-weight:600}
            main{padding:8px 16px 24px;display:flex;flex-direction:column;gap:12px}
            .card{background:var(--card);border-radius:14px;padding:14px;box-shadow:0 1px 3px rgba(0,0,0,.08)}
            .title{font-weight:600;word-break:break-word}.muted{color:var(--muted);font-size:14px;margin-top:4px}
            .bar{height:8px;background:var(--bar);border-radius:4px;margin:10px 0;overflow:hidden}.bar>div{height:100%;background:var(--accent)}
            button{font:inherit;border:0;border-radius:10px;padding:10px 14px;margin-right:8px;background:var(--bar);color:var(--text)}
            button.primary{background:var(--accent);color:#fff}input{font:inherit;padding:10px;border-radius:10px;border:1px solid var(--bar);width:100%;box-sizing:border-box;text-transform:uppercase}
            </style></head><body><header>MegaCopier</header><main id="main"></main>
            <script>
            const L =
            """ + labels + """
            ;
            const main=document.getElementById('main');let code=localStorage.getItem('mc-code')||'';
            function el(t,c,x){const e=document.createElement(t);if(c)e.className=c;if(x!==undefined)e.textContent=x;return e}
            function ask(wrong){main.replaceChildren();const c=el('div','card');c.append(el('div','title',L.code));
             if(wrong)c.append(el('div','muted',L.wrong));const i=el('input');i.value=code;i.autocapitalize='characters';
             const b=el('button','primary',L.enter);b.style.marginTop='10px';b.onclick=()=>{code=i.value.trim();localStorage.setItem('mc-code',code);load()};
             c.append(i,b);main.append(c)}
            async function act(id,what){await fetch('/api/jobs/'+encodeURIComponent(id)+'/'+what,{method:'POST',headers:{'X-MegaCopier-Code':code}});load()}
            async function load(){if(!code){ask(false);return}
             let r;try{r=await fetch('/api/jobs',{headers:{'X-MegaCopier-Code':code}})}catch{return}
             if(r.status==401||r.status==429){ask(true);return}
             const jobs=await r.json();main.replaceChildren();
             if(!jobs.length){main.append(el('div','card muted',L.empty));return}
             for(const j of jobs){const c=el('div','card');c.append(el('div','title',j.title),el('div','muted',j.state+' · '+j.detail));
              const bar=el('div','bar'),fill=el('div');fill.style.width=Math.round(j.percent)+'%';bar.append(fill);c.append(bar);
              if(j.canPause){const b=el('button','',L.pause);b.onclick=()=>act(j.id,'pause');c.append(b)}
              if(j.canResume){const b=el('button','primary',L.resume);b.onclick=()=>act(j.id,'resume');c.append(b)}
              if(j.canCancel){const b=el('button','',L.cancel);b.onclick=()=>{if(confirm(L.cancel+'?'))act(j.id,'cancel')};c.append(b)}
              main.append(c)}}
            load();setInterval(()=>{if(code&&!document.querySelector('input'))load()},2000);
            </script></body></html>
            """;
    }
}
