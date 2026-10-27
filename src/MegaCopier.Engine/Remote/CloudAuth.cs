using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine.Remote;

/// <summary>Direcciones y permisos de cada nube (OAuth 2 con PKCE, como cualquier programa de escritorio).</summary>
internal sealed record CloudProvider(string AuthUrl, string TokenUrl, string Scopes, string ApiBase, string ExtraAuthParameters)
{
    public static CloudProvider For(ConnectionKind kind) => kind switch
    {
        ConnectionKind.OneDrive => new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "Files.ReadWrite offline_access", "https://graph.microsoft.com/v1.0/", ""),
        // SharePoint: el mismo inicio de sesión de Microsoft, con permiso también sobre los sitios de la empresa.
        ConnectionKind.SharePoint => new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "Files.ReadWrite.All Sites.ReadWrite.All offline_access", "https://graph.microsoft.com/v1.0/", ""),
        ConnectionKind.GoogleDrive => new("https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
            "https://www.googleapis.com/auth/drive", "https://www.googleapis.com/", "&access_type=offline&prompt=consent"),
        ConnectionKind.Dropbox => new("https://www.dropbox.com/oauth2/authorize", "https://api.dropboxapi.com/oauth2/token",
            "", "https://api.dropboxapi.com/", "&token_access_type=offline"),
        _ => throw new NotSupportedException(kind.ToString()),
    };
}

/// <summary>
/// Inicio de sesión en una nube: se abre el navegador en la página del servicio, el usuario acepta y el servicio vuelve a
/// http://127.0.0.1:puerto con un código que aquí se cambia por los tokens. MegaCopier nunca ve la contraseña; solo
/// guarda el token de renovación (en el Administrador de credenciales, como las contraseñas de los servidores).
/// </summary>
public static class CloudAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static bool IsCloud(ConnectionKind kind) => kind is ConnectionKind.OneDrive or ConnectionKind.SharePoint or ConnectionKind.GoogleDrive or ConnectionKind.Dropbox;

    /// <summary>Abre el inicio de sesión y espera la vuelta (como mucho 5 minutos). Devuelve el token de renovación.</summary>
    public static async Task<string> SignInAsync(Connection connection, Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        var provider = CloudProvider.For(connection.Kind);
        string authUrl = connection.AuthUrl.Length > 0 ? connection.AuthUrl : provider.AuthUrl;
        using var listener = new TcpListener(IPAddress.Loopback, 0); // solo este equipo: sin aviso del cortafuegos
        listener.Start();
        string redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url(RandomNumberGenerator.GetBytes(16));
        string url = $"{authUrl}?client_id={Uri.EscapeDataString(connection.ClientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirect)}"
            + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}"
            + (provider.Scopes.Length > 0 ? "&scope=" + Uri.EscapeDataString(provider.Scopes) : "") + provider.ExtraAuthParameters;
        openBrowser(url);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        string? code = null;
        string? error = null;
        while (code is null && error is null)
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            if (await HttpRequest.ReadAsync(stream, timeout.Token).ConfigureAwait(false) is not { } request)
            {
                continue;
            }

            if (request.Query("state") == state)
            {
                code = request.Query("code");
                error = code is null ? request.Query("error_description") ?? request.Query("error") ?? "?" : null;
            }

            string page = $"<!doctype html><meta charset=utf-8><title>MegaCopier</title><body style=\"font-family:system-ui;padding:40px\"><h2>MegaCopier</h2><p>{WebUtility.HtmlEncode(code is not null ? Texts.Get("E_CloudSignedIn") : Texts.Get("E_CloudSignInFailed"))}</p>";
            byte[] body = Encoding.UTF8.GetBytes(page);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync(body, timeout.Token).ConfigureAwait(false);
        }

        if (code is null)
        {
            throw new UnauthorizedAccessException(Texts.F("E_CloudSignInError", error));
        }

        var tokens = await TokenRequestAsync(connection, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        }, cancellationToken).ConfigureAwait(false);
        return tokens.Refresh ?? throw new UnauthorizedAccessException(Texts.F("E_CloudSignInError", "refresh_token"));
    }

    /// <summary>Pide tokens al servicio. Devuelve el de acceso, cuánto dura y (si lo cambia) el nuevo de renovación.</summary>
    internal static async Task<(string Access, TimeSpan Lifetime, string? Refresh)> TokenRequestAsync(Connection connection, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        form["client_id"] = connection.ClientId;
        if (connection.ClientSecret.Length > 0)
        {
            form["client_secret"] = connection.ClientSecret;
        }

        string tokenUrl = connection.TokenUrl.Length > 0 ? connection.TokenUrl : CloudProvider.For(connection.Kind).TokenUrl;
        using var response = await Http.PostAsync(tokenUrl, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedAccessException(Texts.F("E_CloudSignInError", $"{(int)response.StatusCode} {json}"));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string access = root.GetProperty("access_token").GetString()!;
        int seconds = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out int value) ? value : 3600;
        string? refresh = root.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
        return (access, TimeSpan.FromSeconds(seconds), refresh);
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// HTTP con el token de acceso de una nube: lo renueva cuando caduca (o si el servicio dice 401), guarda el nuevo token
/// de renovación si el servicio lo cambia, y espera lo que pida el servicio cuando dice "demasiadas peticiones".
/// </summary>
internal sealed class CloudHttp : IDisposable
{
    private readonly Connection _connection;
    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Lock _gate = new();
    private string _refresh;
    private string? _access;
    private DateTime _expires;

    public CloudHttp(Connection connection, string refreshToken)
    {
        _connection = connection;
        _refresh = refreshToken;
    }

    private string AccessToken(bool forceRefresh)
    {
        lock (_gate)
        {
            if (_access is null || forceRefresh || DateTime.UtcNow > _expires)
            {
                var (access, lifetime, refresh) = CloudAuth.TokenRequestAsync(_connection, new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _refresh,
                }, CancellationToken.None).GetAwaiter().GetResult();
                _access = access;
                _expires = DateTime.UtcNow + lifetime - TimeSpan.FromMinutes(2);
                if (refresh is not null && refresh != _refresh)
                {
                    _refresh = refresh;
                    if (Connections.Find(_connection.Id) is not null)
                    {
                        Connections.SetSecret(_connection.Id, refresh); // algunos servicios lo cambian cada vez
                    }
                }
            }

            return _access;
        }
    }

    /// <summary>
    /// Envía la petición que crea <paramref name="build"/> (se vuelve a crear para cada intento). Lanza
    /// <see cref="UnauthorizedAccessException"/> si el servicio no da permiso e <see cref="IOException"/> en otros errores.
    /// </summary>
    public HttpResponseMessage Send(Func<HttpRequestMessage> build, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default, bool retryable = true)
    {
        bool refreshed = false;
        for (int attempt = 0; ; attempt++)
        {
            var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken(forceRefresh: false));
            HttpResponseMessage response;
            try
            {
                response = _http.Send(request, option, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new IOException(ex.Message, ex);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed && retryable)
            {
                response.Dispose();
                AccessToken(forceRefresh: true);
                refreshed = true;
                continue;
            }

            if (retryable && attempt < 4 && response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                response.Dispose();
                Task.Delay(wait < TimeSpan.FromSeconds(60) ? wait : TimeSpan.FromSeconds(60), cancellationToken).GetAwaiter().GetResult();
                continue;
            }

            string body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            string message = $"{request.Method} {request.RequestUri?.AbsolutePath}: {(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim();
            var status = response.StatusCode;
            response.Dispose();
            throw status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? new UnauthorizedAccessException(message)
                : status == HttpStatusCode.NotFound ? new FileNotFoundException(message)
                : new IOException(message);
        }
    }

    /// <summary>Envía y devuelve el JSON de la respuesta.</summary>
    public JsonDocument SendJson(Func<HttpRequestMessage> build, CancellationToken cancellationToken = default, bool retryable = true)
    {
        using var response = Send(build, cancellationToken: cancellationToken, retryable: retryable);
        using var stream = response.Content.ReadAsStream(cancellationToken);
        return JsonDocument.Parse(stream);
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public void Dispose() => _http.Dispose();
}
