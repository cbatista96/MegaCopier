using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MegaCopier.Engine.Updates;

/// <summary>Cliente de releases con redirecciones explícitas, límites de lectura y verificación independiente del editor.</summary>
public sealed class UpdateClient : IDisposable
{
    public const string Repository = "cbatista96/MegaCopier";
    public const long MaxPackageBytes = 512 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Uri _api;
    private readonly bool _test;
    private readonly string _publicKey;
    public string? ETag { get; private set; }
    public DateTimeOffset RetryAfter { get; private set; }

    public UpdateClient(string version, string publicKey, Uri? testApi = null)
    {
        _test = testApi is not null;
        if (_test && (Environment.GetEnvironmentVariable("MEGACOPIER_TEST") != "1"
            || testApi!.Host != "127.0.0.1" || testApi.Scheme != "http" || testApi.IsDefaultPort))
            throw new UpdateException(UpdateError.UntrustedUrl);
        _api = testApi ?? new Uri("https://api.github.com/repos/" + Repository + "/releases/latest");
        _publicKey = publicKey;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MegaCopier/" + version);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static bool ProductionUrl(Uri uri, bool redirected = false) => uri.Scheme == "https" && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/" + Repository + "/", StringComparison.Ordinal)
            || redirected && uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com");

    private bool Allowed(Uri uri, bool redirected) => _test
        ? uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.Port == _api.Port && uri.UserInfo.Length == 0
        : ProductionUrl(uri, redirected);

    private async Task<HttpResponseMessage> GetAsync(Uri url, bool api, CancellationToken cancellation, string? etag = null)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            bool valid = api && redirects == 0 ? url == _api : Allowed(url, redirects > 0);
            if (!valid) throw new UpdateException(UpdateError.UntrustedUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (api && etag is not null && redirects == 0) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var reply = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if ((int)reply.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                Uri? location = reply.Headers.Location;
                reply.Dispose();
                if (location is null) throw new UpdateException(UpdateError.UntrustedUrl);
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }
            if ((int)reply.StatusCode is 403 or 429)
            {
                RetryAfter = reply.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (reply.Headers.RetryAfter?.Delta ?? TimeSpan.FromHours(1));
            }
            if (!reply.IsSuccessStatusCode && reply.StatusCode != HttpStatusCode.NotModified)
            {
                reply.Dispose();
                throw new UpdateException(UpdateError.Network);
            }
            return reply;
        }
        throw new UpdateException(UpdateError.UntrustedUrl);
    }

    private static async Task<byte[]> ReadBounded(HttpResponseMessage reply, int maximum, CancellationToken cancellation)
    {
        if (reply.Content.Headers.ContentLength > maximum) throw new UpdateException(UpdateError.TooLarge);
        await using var input = await reply.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximum) throw new UpdateException(UpdateError.TooLarge);
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public async Task<UpdateRelease?> LatestAsync(string? etag, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var reply = await GetAsync(_api, true, timeout.Token, etag).ConfigureAwait(false);
        if (reply.StatusCode == HttpStatusCode.NotModified) return null;
        var bytes = await ReadBounded(reply, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.UpdateRelease);
        if (release is null || release.Notes is null || release.Assets is null || release.Assets.Count > 100
            || release.Assets.Any(a => a is null || a.Name is null || a.Url is null) || release.Draft || release.Prerelease || ReleaseVersion.Parse(release.Tag) is not { Prerelease.Length: 0 })
            throw new UpdateException(UpdateError.InvalidRelease);
        ETag = reply.Headers.ETag?.ToString();
        return release;
    }

    private async Task<byte[]> SmallAsset(UpdateRelease release, string name, CancellationToken cancellation)
    {
        var asset = release.Asset(name);
        using var reply = await GetAsync(new Uri(asset.Url), false, cancellation).ConfigureAwait(false);
        return await ReadBounded(reply, 65536, cancellation).ConfigureAwait(false);
    }

    public async Task<string> NotesAsync(UpdateRelease release, bool spanish, CancellationToken cancellation)
    {
        string name = spanish ? "release-notes.es.md" : "release-notes.en.md";
        if (!release.Assets.Any(a => a.Name == name)) return spanish ? release.Notes : "";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var reply = await GetAsync(new Uri(release.Asset(name).Url), false, timeout.Token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(await ReadBounded(reply, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false));
    }

    public static SignedUpdateManifest VerifyManifest(byte[] bytes, byte[] signature, string publicKey, string version)
    {
        if (string.IsNullOrWhiteSpace(publicKey)) throw new UpdateException(UpdateError.MissingTrust);
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKey);
            if (key.KeySize != 256 || !key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                throw new UpdateException(UpdateError.InvalidSignature);
            var manifest = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.SignedUpdateManifest);
            if (manifest is null || manifest.Schema != 1 || manifest.Platform != "win-x64" || manifest.Version != version
                || manifest.Assets is null || manifest.Assets.Count > 100 || manifest.Assets.Any(a => a is null))
                throw new UpdateException(UpdateError.InvalidSignature);
            return manifest;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or JsonException)
        {
            throw new UpdateException(UpdateError.InvalidSignature);
        }
    }

    public async Task<SignedUpdateAsset> VerifyReleaseAsync(UpdateRelease release, bool portable, CancellationToken cancellation, string? cacheDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(_publicKey)) throw new UpdateException(UpdateError.MissingTrust);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        byte[] bytes = await SmallAsset(release, "update.json", timeout.Token).ConfigureAwait(false);
        byte[] signature = await SmallAsset(release, "update.sig", timeout.Token).ConfigureAwait(false);
        byte[] sumsBytes = await SmallAsset(release, "SHA256SUMS.txt", timeout.Token).ConfigureAwait(false);
        var expected = SelectAsset(release, portable, bytes, signature, sumsBytes);
        if (cacheDirectory is not null)
        {
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(Path.Combine(cacheDirectory, "update.json"), bytes, timeout.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(cacheDirectory, "update.sig"), signature, timeout.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(cacheDirectory, "SHA256SUMS.txt"), sumsBytes, timeout.Token).ConfigureAwait(false);
        }
        return expected;
    }

    public SignedUpdateAsset VerifyCachedRelease(UpdateRelease release, bool portable, string directory)
    {
        byte[] Read(string name)
        {
            string path = Path.Combine(directory, name);
            if (new FileInfo(path).Length > 65536) throw new UpdateException(UpdateError.TooLarge);
            return File.ReadAllBytes(path);
        }
        return SelectAsset(release, portable, Read("update.json"), Read("update.sig"), Read("SHA256SUMS.txt"));
    }

    private SignedUpdateAsset SelectAsset(UpdateRelease release, bool portable, byte[] bytes, byte[] signature, byte[] sumsBytes)
    {
        string version = ReleaseVersion.Parse(release.Tag)?.ToString() ?? throw new UpdateException(UpdateError.InvalidRelease);
        var manifest = VerifyManifest(bytes, signature, _publicKey, version);
        string name = portable ? $"MegaCopier-{version}-portable.zip" : $"MegaCopier-Setup-{version}.exe";
        var candidates = manifest.Assets.Where(a => a.Name == name).ToList();
        var asset = release.Asset(name);
        if (candidates.Count != 1 || candidates[0].Size != asset.Size || asset.Size > MaxPackageBytes || !ValidHash(candidates[0].Sha256))
            throw new UpdateException(UpdateError.InvalidChecksum);
        var expected = candidates[0];
        if (!string.IsNullOrEmpty(asset.Digest) && !string.Equals(asset.Digest, "sha256:" + expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateError.InvalidChecksum);
        string sums = Encoding.UTF8.GetString(sumsBytes);
        var matching = sums.Split('\n').Select(l => l.TrimEnd('\r').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart(' ', '*') == name).ToList();
        if (matching.Count != 1 || !string.Equals(matching[0][0], expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateError.InvalidChecksum);
        return expected;
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    public static async Task VerifyFileAsync(string path, SignedUpdateAsset expected, CancellationToken cancellation)
    {
        await using var file = File.OpenRead(path);
        if (file.Length != expected.Size || !ValidHash(expected.Sha256)
            || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation).ConfigureAwait(false)), expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateError.InvalidChecksum);
    }

    public async Task<string> DownloadAsync(UpdateRelease release, SignedUpdateAsset expected, string directory,
        Action<UpdateProgress> progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string final = Path.Combine(directory, expected.Name);
        if (File.Exists(final))
        {
            try { await VerifyFileAsync(final, expected, cancellation).ConfigureAwait(false); return final; }
            catch (UpdateException) { File.Delete(final); }
        }
        string partial = final + ".partial";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var reply = await GetAsync(new Uri(release.Asset(expected.Name).Url), false, timeout.Token).ConfigureAwait(false);
            if (reply.Content.Headers.ContentLength is { } length && length != expected.Size) throw new UpdateException(UpdateError.InvalidChecksum);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            await using (var input = await reply.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            {
                byte[] buffer = new byte[65536];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > expected.Size) throw new UpdateException(UpdateError.TooLarge);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    progress(new(received, expected.Size));
                }
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            progress(new(expected.Size, expected.Size, true));
            await VerifyFileAsync(partial, expected, timeout.Token).ConfigureAwait(false);
            File.Move(partial, final, overwrite: true);
            return final;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public void Dispose() => _http.Dispose();
}
