using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MegaCopier.Engine.Diagnostics;

/// <summary>Solo almacena informes autorizados. Cuota persistente, deduplicación y envío sin redirecciones.</summary>
public sealed class CrashQueue(string directory, Uri endpoint, string version, bool loopbackTest = false) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource _lifetime = new();
    private string LedgerPath => Path.Combine(directory, "ledger.json");
    public static bool ValidEndpoint(Uri uri, bool test = false) => uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && (uri.Scheme == "https" && uri.IsDefaultPort || test && uri.Scheme == "http" && uri.Host == "127.0.0.1");
    public static string Preview(CrashReport report) => JsonSerializer.Serialize(report, CrashJsonContext.Default.CrashReport);
    private static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, type), Encoding.UTF8);
        File.Move(path + ".tmp", path, true);
    }
    private CrashLedger Ledger()
    {
        try
        {
            var ledger = File.Exists(LedgerPath) ? JsonSerializer.Deserialize(File.ReadAllText(LedgerPath), CrashJsonContext.Default.CrashLedger) : new();
            return ledger is { Sent: not null, Attempts: >= 0 } ? ledger : new() { Day = DateOnly.FromDateTime(DateTime.UtcNow), Attempts = 5 };
        }
        catch (JsonException) { return new() { Day = DateOnly.FromDateTime(DateTime.UtcNow), Attempts = 5 }; }
    }
    public async Task<bool> AuthorizeAsync(CrashReport report, bool consent, CancellationToken cancellation = default, bool automatic = false)
    {
        if (!consent || !ValidEndpoint(endpoint, loopbackTest)) return false;
        await _gate.WaitAsync(cancellation);
        try
        {
            report.SetHash(); if (!report.IsSafe()) return false;
            Directory.CreateDirectory(directory);
            var ledger = Ledger();
            if (ledger.Sent.ContainsKey(report.Hash)) return false;
            string path = Path.Combine(directory, report.Hash + ".json");
            if (File.Exists(path)) return false;
            if (Directory.GetFiles(directory, "*.json").Length > 20) return false;
            if (Encoding.UTF8.GetByteCount(Preview(report)) >= 64000) return false;
            Write(path, new CrashPending(report, automatic), CrashJsonContext.Default.CrashPending); return true;
        }
        finally { _gate.Release(); }
    }
    public async Task<int> FlushAsync(CancellationToken cancellation = default, DateTimeOffset? clock = null, bool automaticConsent = false)
    {
        if (!ValidEndpoint(endpoint, loopbackTest)) return 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (!Directory.Exists(directory)) return 0;
            var now = clock ?? DateTimeOffset.UtcNow;
            var ledger = Ledger(); var day = DateOnly.FromDateTime(now.UtcDateTime);
            if (ledger.Day != day) { ledger.Day = day; ledger.Attempts = 0; }
            foreach (var hash in ledger.Sent.Where(x => now - x.Value > TimeSpan.FromDays(90)).Select(x => x.Key).ToArray()) ledger.Sent.Remove(hash);
            int count = 0;
            foreach (string file in Directory.GetFiles(directory, "*.json").Where(f => f != LedgerPath).Order())
            {
                if (ledger.Attempts >= 5) break;
                linked.Token.ThrowIfCancellationRequested();
                if (new FileInfo(file).Length >= 65000) { File.Delete(file); continue; }
                CrashPending? pending;
                try { pending = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, linked.Token), CrashJsonContext.Default.CrashPending); }
                catch (JsonException) { File.Delete(file); continue; }
                var report = pending?.Report;
                if (report is null || !report.IsSafe() || pending!.AutomaticConsent && !automaticConsent || now - report.Created > TimeSpan.FromDays(14) || ledger.Sent.ContainsKey(report.Hash)) { File.Delete(file); continue; }
                // Se cuenta antes de transmitir, también si se cierra el proceso durante la petición.
                ledger.Attempts++; Write(LedgerPath, ledger, CrashJsonContext.Default.CrashLedger);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.UserAgent.ParseAdd("MegaCopier/" + version);
                    request.Content = JsonContent.Create(report, CrashJsonContext.Default.CrashReport);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1024) break;
                    await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
                    using var small = new MemoryStream(); byte[] buffer = new byte[1025];
                    int read; while ((read = await stream.ReadAsync(buffer, linked.Token)) > 0) { small.Write(buffer, 0, read); if (small.Length > 1024) break; }
                    if (small.Length > 1024 || JsonSerializer.Deserialize(small.ToArray(), CrashJsonContext.Default.CrashReceipt)?.Accepted != true) break;
                    ledger.Sent[report.Hash] = now; Write(LedgerPath, ledger, CrashJsonContext.Default.CrashLedger);
                    File.Delete(file); count++;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException) { break; }
            }
            return count;
        }
        finally { _gate.Release(); }
    }
    public void Cancel() => _lifetime.Cancel();
    public static void ClearPending(string directory)
    {
        if (Directory.Exists(directory)) foreach (string file in Directory.GetFiles(directory, "*.json").Where(f => Path.GetFileName(f) != "ledger.json")) File.Delete(file);
    }
    public async Task RevokeAsync()
    {
        Cancel(); await _gate.WaitAsync();
        try { ClearPending(directory); }
        finally { _gate.Release(); }
    }
    public void Dispose() { Cancel(); _http.Dispose(); }
}
