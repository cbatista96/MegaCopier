using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MegaCopier.App.Theming;
using MegaCopier.Engine;
using MegaCopier.Engine.Diagnostics;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

internal sealed class CrashReporter : IDisposable
{
    // Receptor privado desplegado por el propietario. Nunca contiene un token de GitHub.
    private const string Endpoint = "https://megacopier-informes.informes-worker.workers.dev/report";
    internal static Uri? ConfiguredEndpoint
    {
        get
        {
            if (App.TestMode && Uri.TryCreate(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_REPORT_URL"), UriKind.Absolute, out var test)
                && test.Scheme == "http" && test.Host == "127.0.0.1" && CrashQueue.ValidEndpoint(test, true)) return test;
            return Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && CrashQueue.ValidEndpoint(uri) ? uri : null;
        }
    }
    private readonly App _app;
    private readonly CrashSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operations = new(1);
    private CrashQueue? _queue;
    private bool _prompt, _showing;
    internal CrashReport? Last { get; private set; }
    internal string StatusKey { get; private set; } = "Crash_LocalOnly";
    internal event Action? Changed;
    internal static string Version => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    internal CrashReporter(App app, CrashSession session)
    {
        _app = app; _session = session; _queue = CreateQueue();
        if (_queue is not null) StatusKey = "";
        _ = InitializeAsync();
    }
    private CrashQueue? CreateQueue() => ConfiguredEndpoint is { } uri ? new(DataFolder.Combine("reports-pending"), uri, Version, App.TestMode) : null;
    private async Task InitializeAsync()
    {
        try
        {
            await Task.Delay(2000, _lifetime.Token);
            bool flushed = false;
            if (_session.Previous is { } previous)
            {
                var native = await Task.Run(() => NativeCrashEvents.Find(previous), _lifetime.Token);
                Last = new CrashReport
                {
                    Version = System.Version.TryParse(previous.Version, out _) ? previous.Version : Version,
                    Windows = Environment.OSVersion.Version.ToString(), Language = CultureInfo.CurrentUICulture.Name,
                    Mode = DataFolder.IsPortable ? "portable" : _app.Updates?.IsInstalled == true ? "installed" : "development",
                    Theme = ThemeManager.BuiltIn.FirstOrDefault(t => t.Id == _app.Settings.ThemeId)?.Id ?? "custom",
                    Activity = previous.Activity is "copying" or "paused" or "settings" or "dragging" ? previous.Activity : "idle",
                    Kind = native is not null ? "native" : !string.IsNullOrEmpty(previous.Fault) ? "managed" : "unexpected",
                    Trace = CrashPrivacy.SafeTrace(previous.Fault ?? "", Environment.UserName, Environment.MachineName), Native = native,
                    RecentLog = CrashSession.RecentLog(DataFolder.Combine("crash.log")),
                };
                Last.SetHash();
                if (Last.IsSafe()) File.WriteAllText(DataFolder.Combine("last-crash-report.json"), CrashQueue.Preview(Last)); else Last = null;
                _prompt = Last is not null && _queue is not null && !_app.Settings.SendCrashReportsAutomatically;
                if (Last is not null && _app.Settings.SendCrashReportsAutomatically) { await SendAsync(Last, true); flushed = true; }
            }
            else
            {
                try { Last = JsonSerializer.Deserialize(File.ReadAllText(DataFolder.Combine("last-crash-report.json")), CrashJsonContext.Default.CrashReport); if (Last?.IsSafe() != true) Last = null; }
                catch (Exception ex) when (ex is IOException or JsonException) { }
            }
            if (_queue is not null && !flushed) await FlushAsync();
            Changed?.Invoke(); Tick();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or JsonException) { }
    }
    internal void Tick()
    {
        _session.Activity(_app.Queue.Jobs.Any(j => j.State == CopyJobState.Running) ? "copying"
            : _app.Queue.Jobs.Any(j => j.State == CopyJobState.Paused) ? "paused" : _app.Window.DiagnosticsSettingsOpen ? "settings" : "idle");
        if (_prompt && !_showing && !_app.Queue.Jobs.Any(j => j.IsActive) && _app.Window.AppWindow.IsVisible && _app.Window.DiagnosticsRootReady && !_app.Window.UpdateDialogOpen)
        { _prompt = false; _ = ShowAsync(send: true); }
    }
    internal void NoteDrag() => _session.Activity("dragging");
    internal async Task ShowAsync(bool send)
    {
        if (Last is not { } report || _showing) return;
        _showing = true;
        try
        {
            var result = await _app.Window.ShowCrashReportAsync(report, send && _queue is not null);
            if (result.Send)
            {
                if (result.Always) { _app.Settings.SendCrashReportsAutomatically = true; _app.Settings.Save(); }
                await SendAsync(report, result.Always);
            }
        }
        catch (Exception ex) { App.LogCrash(ex, "Informe: diálogo"); }
        finally { _showing = false; }
    }
    private async Task SendAsync(CrashReport report, bool automatic)
    {
        await _operations.WaitAsync(_lifetime.Token);
        try
        {
            if (_queue is null || automatic && !_app.Settings.SendCrashReportsAutomatically) return;
            bool added = await _queue.AuthorizeAsync(report, true, _lifetime.Token, automatic);
            StatusKey = added ? "Crash_Queued" : "Crash_Duplicate"; Changed?.Invoke();
            if (await _queue.FlushAsync(_lifetime.Token, automaticConsent: _app.Settings.SendCrashReportsAutomatically) > 0) StatusKey = "Crash_Sent";
            Changed?.Invoke();
        }
        finally { _operations.Release(); }
    }
    private async Task FlushAsync()
    {
        await _operations.WaitAsync(_lifetime.Token);
        try { if (_queue is not null && await _queue.FlushAsync(_lifetime.Token, automaticConsent: _app.Settings.SendCrashReportsAutomatically) > 0) StatusKey = "Crash_Sent"; }
        finally { _operations.Release(); }
    }
    internal async Task SetAutomaticAsync(bool value)
    {
        _app.Settings.SendCrashReportsAutomatically = value; _app.Settings.Save();
        if (!value && _queue is { } old)
        {
            old.Cancel(); await _operations.WaitAsync();
            try { await old.RevokeAsync(); old.Dispose(); _queue = CreateQueue(); StatusKey = "Crash_Revoked"; }
            finally { _operations.Release(); }
        }
        Changed?.Invoke();
    }
    internal static Uri IssueLink() => new("https://github.com/cbatista96/MegaCopier/issues/new?body=" + Uri.EscapeDataString($"MegaCopier {Version}\nWindows {Environment.OSVersion.Version}\n\n"));
    public void Dispose() { _lifetime.Cancel(); _queue?.Cancel(); _queue?.Dispose(); }
}
