using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MegaCopier.Engine;

namespace MegaCopier.App.Services;

/// <summary>Imágenes del historial embebidas; las nuevas se cachean con límite y respetan la red autorizada.</summary>
internal static class UpdateNoteImages
{
    private const int Maximum = 8 * 1024 * 1024;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Limit = new(2);
    public static Uri? Resolve(string value, string tag)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) return uri.Scheme == "https" && uri.UserInfo.Length == 0 ? uri : null;
        return Uri.TryCreate(new Uri("https://github.com/cbatista96/MegaCopier/blob/" + tag + "/"), value, out uri) ? uri : null;
    }
    private static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/cbatista96/MegaCopier/", StringComparison.Ordinal)
            || uri.Host == "raw.githubusercontent.com" && uri.AbsolutePath.StartsWith("/cbatista96/MegaCopier/", StringComparison.Ordinal));

    public static async Task<byte[]?> ReadAsync(string value, string tag)
    {
        var uri = Resolve(value, tag);
        if (uri is null || !Allowed(uri)) return null;
        int capture = uri.AbsolutePath.IndexOf("/docs/capturas/", StringComparison.Ordinal);
        if (capture >= 0)
        {
            string relative = Uri.UnescapeDataString(uri.AbsolutePath[(capture + "/docs/capturas/".Length)..]);
            if (!relative.Split('/').Any(p => p == ".."))
            {
                var assembly = Assembly.GetExecutingAssembly();
                string name = "MegaCopier.ChangelogImages." + relative.Replace('/', '.');
                string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '.').Replace('/', '.') == name);
                using var resource = resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
                if (resource is not null)
                {
                    using var output = new MemoryStream();
                    await resource.CopyToAsync(output);
                    return output.ToArray();
                }
            }
        }
        string directory = DataFolder.Combine("update-images");
        string path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))) + ".image");
        if (File.Exists(path) && new FileInfo(path).Length <= Maximum) return await File.ReadAllBytesAsync(path);
        if (((App)Microsoft.UI.Xaml.Application.Current).Updates?.CanUseNetwork != true) return null;
        await Limit.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            for (int i = 0; i <= 4; i++)
            {
                if (!Allowed(uri)) return null;
                using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is not { } next) return null;
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > Maximum) return null;
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream();
                byte[] buffer = new byte[16384]; int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (output.Length + read > Maximum || ((App)Microsoft.UI.Xaml.Application.Current).Updates?.CanUseNetwork != true) return null;
                    output.Write(buffer, 0, read);
                }
                Directory.CreateDirectory(directory);
                // Caché prescindible, con un máximo total aproximado de 64 MB.
                var cached = new DirectoryInfo(directory).GetFiles("*.image").OrderBy(f => f.LastWriteTimeUtc).ToList();
                long total = cached.Sum(f => f.Length);
                foreach (var old in cached)
                {
                    if (total + output.Length <= 64 * 1024 * 1024) break;
                    total -= old.Length; old.Delete();
                }
                byte[] bytes = output.ToArray();
                await File.WriteAllBytesAsync(path, bytes);
                return bytes;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException) { }
        finally { Limit.Release(); }
        return null;
    }
}
