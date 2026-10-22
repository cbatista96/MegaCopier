using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using MegaCopier.Engine.Updates;
using Microsoft.Win32;
using Windows.Networking.Connectivity;

namespace MegaCopier.App.Services;

/// <summary>Un actualizador por proceso; la página solo presenta su estado. Nunca depende de que Ajustes esté abierto.</summary>
internal sealed class UpdateCoordinator : IDisposable
{
    private readonly App _app;
    private readonly UpdateClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operation = new(1);
    private CancellationTokenSource? _download;
    private readonly string _cachePath = DataFolder.Combine("updates.json");
    private DateTimeOffset _attempt;
    private DateTimeOffset _downloadAttempt;
    private DateTimeOffset? _idleSince;
    private bool _resumeNetwork, _overrideNetwork, _networkPaused;
    private bool _hadInternet;
    private UpdateNetwork _network;
    private Task<UpdateNetwork>? _networkRead;
    private SignedUpdateAsset? _verified;
    private string? _package;
    private readonly Task _loop;
    public UpdateCache Cache { get; private set; } = new();
    public UpdatePhase Phase { get; private set; }
    public UpdateError? Error { get; private set; }
    public UpdateProgress Progress { get; private set; }
    public ReleaseVersion Current { get; }
    public bool Portable => DataFolder.IsPortable;
    public bool Spanish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    public bool HasUpdate => Cache.Release is { } r && ReleaseVersion.Parse(r.Tag)?.CompareTo(Current) > 0;
    public bool CompletedUpdateUnread => Cache.UpdatedVersion == Current.ToString() && Cache.ViewedVersion != Cache.UpdatedVersion;
    public bool Badge => CompletedUpdateUnread || HasUpdate && Cache.DismissedVersion != Cache.Release!.Tag;
    public string Version => ReleaseVersion.Parse(Cache.Release?.Tag)?.ToString() ?? Current.ToString();
    public string Notes { get; private set; } = "";
    public UpdatePhase LastKnownPhase => _package is not null && _verified is { } asset
        && Cache.Release?.Assets.Any(a => a.Name == asset.Name) == true ? UpdatePhase.Ready
        : HasUpdate ? UpdatePhase.Available : Cache.Checked is not null ? UpdatePhase.Current : UpdatePhase.Unknown;
    public event Action? Changed;
    public UpdateNotifications Notifications { get; }

    public UpdateCoordinator(App app)
    {
        _app = app;
        _hadInternet = HasInternet;
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        Current = new(version.Major, version.Minor, Math.Max(0, version.Build));
        string key = Resource("MegaCopier.UpdatePublicKey.pem");
        Uri? test = null;
        if (App.TestMode && Uri.TryCreate(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_URL"), UriKind.Absolute, out var testUri))
        {
            test = testUri;
            string? testKey = Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_KEY");
            if (testKey is not null && testUri.Host == "127.0.0.1") key = File.ReadAllText(testKey);
        }
        _client = new(Current.ToString(), key, test);
        try
        {
            if (File.Exists(_cachePath)) Cache = JsonSerializer.Deserialize(File.ReadAllText(_cachePath), UpdateJsonContext.Default.UpdateCache) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (UpdatePolicy.CompleteInstallation(Cache, Current)) Save();
        Phase = HasUpdate ? UpdatePhase.Available : Cache.Checked is null ? UpdatePhase.Unknown : UpdatePhase.Current;
        Notes = HasUpdate ? Spanish ? Cache.SpanishNotes : Cache.EnglishNotes : InstalledNotes();
        Notifications = new(app);
        NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
        NetworkChange.NetworkAddressChanged += AddressChanged;
        NetworkInformation.NetworkStatusChanged += StatusChanged;
        _ = RestorePackageAsync();
        _loop = LoopAsync();
        _ = RefreshNetworkAsync();
    }

    private string DownloadDirectory => Path.Combine(App.TestMode ? DataFolder.Combine("update-downloads") : Path.Combine(Path.GetTempPath(), "MegaCopier-update"), Version);
    private async Task RestorePackageAsync()
    {
        if (!HasUpdate || !Directory.Exists(DownloadDirectory) || !await _operation.WaitAsync(0)) return;
        try
        {
            var expected = _client.VerifyCachedRelease(Cache.Release!, Portable || !IsInstalled, DownloadDirectory);
            string path = Path.Combine(DownloadDirectory, expected.Name);
            if (!File.Exists(path)) return;
            Set(UpdatePhase.Verifying);
            await UpdateClient.VerifyFileAsync(path, expected, _lifetime.Token);
            _package = path; _verified = expected;
            Set(UpdatePhase.Ready);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateException or OperationCanceledException)
        { Set(UpdatePhase.Available); }
        finally { _operation.Release(); }
    }

    public static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string HistoryText => Resource(Spanish ? "MegaCopier.Changelog.es.md" : "MegaCopier.Changelog.en.md");
    private string InstalledNotes() => ReleaseNotes.History(HistoryText, Current).FirstOrDefault(v => v.Version == Current.ToString())?.Notes ?? "";
    public bool CanUseNetwork => UpdatePolicy.Allows(Network(), _app.Settings.UpdateAllowWifiEthernet, _app.Settings.UpdateAllowMobileMetered);
    public bool HasInternet => Network().Known;
    public bool IsInstalled
    {
        get
        {
            if (Portable) return false;
            if (App.TestMode) return Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_INSTALLED") == "1";
            using var entry = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B7E2C1D-4F6A-4E9B-9C3D-2A1F5E7B8C90}_is1");
            return entry?.GetValue("InstallLocation") is string path
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        }
    }

    private UpdateNetwork Network()
    {
        string? simulated = App.TestMode ? Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_NETWORK") : null;
        if (App.TestMode)
        {
            try
            {
                string path = DataFolder.Combine("test-update-network.txt");
                if (File.Exists(path)) simulated = File.ReadAllText(path).Trim();
            }
            catch (IOException) { }
        }
        if (simulated is not null)
            return simulated switch
            {
                "wifi" or "ethernet" => new(true, true, false, false, false),
                "wifi-metered" or "ethernet-metered" => new(true, true, false, true, false),
                "mobile" => new(true, false, true, true, false),
                _ => default,
            };
        return _network;
    }

    // Consultar y consumir los objetos WinRT en el mismo hilo MTA. La interfaz solo recibe datos,
    // sin provocar llamadas COM que puedan reentrar en XAML mientras se crea la ventana.
    private async Task RefreshNetworkAsync()
    {
        if (App.TestMode && (Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_NETWORK") is not null
            || File.Exists(DataFolder.Combine("test-update-network.txt")))) return;
        try
        {
            _networkRead ??= Task.Run(ReadWindowsNetwork);
            _network = await _networkRead;
            if (!_lifetime.IsCancellationRequested) Changed?.Invoke();
        }
        finally { _networkRead = null; }
    }

    private static UpdateNetwork ReadWindowsNetwork()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null || profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.InternetAccess) return default;
            var cost = profile.GetConnectionCost();
            bool mobile = profile.IsWwanConnectionProfile;
            bool local = profile.IsWlanConnectionProfile || profile.NetworkAdapter?.IanaInterfaceType == 6;
            return new(true, local, mobile,
                cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable,
                cost.NetworkCostType == NetworkCostType.Unknown || cost.Roaming || cost.OverDataLimit || cost.BackgroundDataUsageRestricted);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return default; }
    }

    private void StatusChanged(object sender) => OnNetworkChanged();
    private void AddressChanged(object? sender, EventArgs e) => OnNetworkChanged();
    private void NetworkChanged(object? sender, NetworkAvailabilityEventArgs e) => OnNetworkChanged();
    private void OnNetworkChanged()
    {
        _app.Dispatcher.TryEnqueue(() =>
        {
            if (_lifetime.IsCancellationRequested) return;
            if (_download is not null && !_overrideNetwork && !CanUseNetwork)
            {
                _networkPaused = true;
                _download.Cancel();
            }
            _ = TickAsync(reconnected: true);
        });
    }

    private async Task LoopAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(App.TestMode ? 2 : 30), _lifetime.Token);
            bool startup = true;
            while (!_lifetime.IsCancellationRequested)
            {
                await TickAsync(startup: startup);
                startup = false;
                await Task.Delay(TimeSpan.FromMinutes(1), _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogCrash(ex, "Actualizaciones: temporizador"); }
    }

    private async Task TickAsync(bool reconnected = false, bool startup = false)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (startup || reconnected || _download is not null || _resumeNetwork || UpdatePolicy.CheckDue(Cache.Checked, DateTimeOffset.UtcNow))
            await RefreshNetworkAsync();
        var now = DateTimeOffset.UtcNow;
        if (Cache.UpdatedVersion == Current.ToString() && Cache.UpdatedNotice != Cache.UpdatedVersion
            && await Notifications.ShowAsync(ready: false, completed: true))
        { Cache.UpdatedNotice = Cache.UpdatedVersion; Save(); }
        bool online = HasInternet, recovered = online && !_hadInternet;
        _hadInternet = online;
        if (_download is not null && !_overrideNetwork && !CanUseNetwork) { _networkPaused = true; _download.Cancel(); }
        if (recovered && Error == UpdateError.Network)
            Set(_package is not null && _verified is not null ? UpdatePhase.Ready : HasUpdate ? UpdatePhase.Available : Cache.Checked is null ? UpdatePhase.Unknown : UpdatePhase.Current);
        Changed?.Invoke();
        if (online && _app.Settings.CheckUpdatesAutomatically && UpdatePolicy.CheckDue(Cache.Checked, now)
            && (recovered || now - _attempt > TimeSpan.FromMinutes(1))
            && now >= _client.RetryAfter)
            await CheckAsync(manual: false);
        if (HasUpdate && _download is null && Phase is not (UpdatePhase.Downloading or UpdatePhase.Verifying or UpdatePhase.Checking or UpdatePhase.Installing))
        {
            bool automatic = UpdatePolicy.CanDownloadAutomatically(Cache, _app.Settings.CheckUpdatesAutomatically,
                _app.Settings.DownloadUpdatesAutomatically);
            if ((automatic || _resumeNetwork) && CanUseNetwork && (recovered || now - _downloadAttempt > TimeSpan.FromMinutes(1)) && Phase != UpdatePhase.Ready)
                await DownloadAsync(manual: false);
            if (_app.Settings.InstallUpdatesWhenIdle && IsInstalled && Phase == UpdatePhase.Ready)
            {
                if (!_app.UpdateIdle) _idleSince = null;
                else if (_idleSince is null) _idleSince = now;
                else if (now - _idleSince > TimeSpan.FromSeconds(30)) await InstallAsync(automatic: true);
            }
        }
    }

    public async Task CheckAsync(bool manual = true)
    {
        await RefreshNetworkAsync();
        if (!await _operation.WaitAsync(0)) return;
        var previous = Phase;
        string? previousTag = Cache.Release?.Tag;
        try
        {
            if (!HasInternet)
            {
                _hadInternet = false;
                Set(UpdatePhase.Error, UpdateError.Network); return;
            }
            _hadInternet = true;
            _attempt = DateTimeOffset.UtcNow;
            Set(UpdatePhase.Checking);
            var found = await _client.LatestAsync(Cache.ETag, _lifetime.Token);
            if (found is not null)
            {
                if (found.Tag != Cache.Release?.Tag) { Cache.SpanishNotes = ""; Cache.EnglishNotes = ""; Cache.AutomaticVersion = ""; _downloadAttempt = default; }
                Cache.Release = found; Cache.ETag = _client.ETag;
            }
            if (Cache.Release is null) throw new UpdateException(UpdateError.InvalidRelease);
            Cache.Checked = DateTimeOffset.UtcNow;
            if (HasUpdate)
            {
                if (!manual) Cache.AutomaticVersion = Cache.Release.Tag;
                try
                {
                    Notes = await _client.NotesAsync(Cache.Release, Spanish, _lifetime.Token);
                    if (Spanish) Cache.SpanishNotes = Notes; else Cache.EnglishNotes = Notes;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UpdateException or OperationCanceledException or UriFormatException)
                { Notes = Spanish ? Cache.SpanishNotes : Cache.EnglishNotes; }
            }
            else Notes = InstalledNotes();
            Save();
            Set(HasUpdate ? previous == UpdatePhase.Ready && previousTag == Cache.Release.Tag ? UpdatePhase.Ready : UpdatePhase.Available : UpdatePhase.Current);
            if (HasUpdate && Cache.AvailableNotice != Cache.Release.Tag)
            {
                if (await Notifications.ShowAsync(ready: false)) { Cache.AvailableNotice = Cache.Release.Tag; Save(); }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UpdateException or JsonException or OperationCanceledException or UriFormatException)
        {
            if (!HasInternet) _hadInternet = false;
            if (manual) Set(UpdatePhase.Error, ex is UpdateException update ? update.Error : UpdateError.Network);
            else Set(previous);
        }
        finally { _operation.Release(); }
    }

    public async Task DownloadAsync(bool manual = true, bool allowOnce = false)
    {
        await RefreshNetworkAsync();
        if (!HasUpdate || !await _operation.WaitAsync(0)) return;
        _downloadAttempt = DateTimeOffset.UtcNow;
        _networkPaused = false;
        _overrideNetwork = allowOnce;
        _download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        Progress = default;
        Set(UpdatePhase.Downloading);
        try
        {
            if (!HasInternet) throw new UpdateException(UpdateError.Network);
            if (!CanUseNetwork && !allowOnce) throw new UpdateException(UpdateError.RestrictedNetwork);
            var release = Cache.Release!;
            _verified = await _client.VerifyReleaseAsync(release, Portable || !IsInstalled, _download.Token, DownloadDirectory);
            var download = _download;
            _package = await _client.DownloadAsync(release, _verified, DownloadDirectory, progress =>
            {
                _app.Dispatcher.TryEnqueue(() =>
                {
                    if (_download != download || download.IsCancellationRequested || Phase is not (UpdatePhase.Downloading or UpdatePhase.Verifying)) return;
                    Progress = progress; Set(progress.Verifying ? UpdatePhase.Verifying : UpdatePhase.Downloading);
                });
            }, _download.Token);
            _resumeNetwork = false;
            Set(UpdatePhase.Ready);
            if (Cache.ReadyNotice != release.Tag && await Notifications.ShowAsync(ready: true)) { Cache.ReadyNotice = release.Tag; Save(); }
        }
        catch (OperationCanceledException)
        {
            _resumeNetwork = _networkPaused;
            Set(_networkPaused ? UpdatePhase.WaitingForNetwork : UpdatePhase.Available);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UpdateException or JsonException or UnauthorizedAccessException or UriFormatException)
        {
            var error = ex is UpdateException update ? update.Error : UpdateError.Network;
            if (manual || error is not (UpdateError.Network or UpdateError.RestrictedNetwork)) Set(UpdatePhase.Error, error);
            else Set(UpdatePhase.WaitingForNetwork);
        }
        finally { _download.Dispose(); _download = null; _operation.Release(); }
    }

    public void CancelDownload()
    {
        Cache.CancelledVersion = Cache.Release?.Tag ?? "";
        _networkPaused = false; _resumeNetwork = false;
        _download?.Cancel(); Save();
    }

    public void Dismiss() { Cache.DismissedVersion = Cache.Release?.Tag ?? ""; Save(); Changed?.Invoke(); }
    public void OpenedUpdates()
    {
        if (!CompletedUpdateUnread) return;
        Cache.ViewedVersion = Cache.UpdatedVersion; Save(); Changed?.Invoke();
    }
    public void NoteInteraction() => _idleSince = null;
    internal void RefreshNetworkPolicy() => OnNetworkChanged();
    internal Task RefreshConnectionAsync() => RefreshNetworkAsync();
    internal Task TestTickAsync()
    {
        if (!App.TestMode) return Task.CompletedTask;
        _attempt = DateTimeOffset.MinValue;
        _downloadAttempt = DateTimeOffset.MinValue;
        return TickAsync();
    }

    public async Task InstallAsync(bool automatic = false)
    {
        if (Phase != UpdatePhase.Ready || _package is null || _verified is null || !await _operation.WaitAsync(0)) return;
        bool maintenance = false, started = false;
        try
        {
            if (!IsInstalled)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + _package + "\"" });
                return;
            }
            if (automatic && !_app.UpdateIdle || !_app.CanUpdate || !_app.Queue.TryBeginMaintenance()) throw new UpdateException(UpdateError.Busy);
            maintenance = true;
            Set(UpdatePhase.Verifying);
            await UpdateClient.VerifyFileAsync(_package, _verified, _lifetime.Token);
            if (!_app.CanUpdate || automatic && !_app.UpdateIdle) throw new UpdateException(UpdateError.Busy);
            if (App.TestMode)
            {
                App.TestLog("update-install.log", "Verificado; instalación simulada sin ejecutar archivos.");
                Set(UpdatePhase.Ready); return;
            }
            Set(UpdatePhase.Installing);
            var start = new ProcessStartInfo(_package) { UseShellExecute = true };
            start.ArgumentList.Add("/VERYSILENT"); start.ArgumentList.Add("/SUPPRESSMSGBOXES"); start.ArgumentList.Add("/NORESTART");
            start.ArgumentList.Add("/UPDATE");
            start.ArgumentList.Add(automatic ? "/UPDATEBACKGROUND" : "/UPDATEFOREGROUND");
            start.ArgumentList.Add("/DIR=" + AppContext.BaseDirectory);
            start.ArgumentList.Add("/LOG=" + DataFolder.Combine("update-install.log"));
            var tasks = new List<string>();
            if (MegaCopier.Shell.ShellExtensionRegistration.IsRegistered) tasks.Add("shellmenu");
            if (StartupRegistration.IsEnabled) tasks.Add("startup");
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MegaCopier.lnk"))) tasks.Add("desktopicon");
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MegaCopier.lnk"))) tasks.Add("startmenuicon");
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "MegaCopier.lnk"))) tasks.Add("taskbaricon");
            start.ArgumentList.Add("/TASKS=" + string.Join(',', tasks));
            Cache.InstallingFromVersion = Current.ToString(); Cache.InstallingVersion = Version; Save();
            using var process = Process.Start(start) ?? throw new UpdateException(UpdateError.Installer);
            started = true;
            _app.ExitApp();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UpdateException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Cache.InstallingFromVersion = Cache.InstallingVersion = ""; Save();
            Set(UpdatePhase.Ready, ex is UpdateException update ? update.Error : UpdateError.Installer);
        }
        finally { if (maintenance && !started) _app.Queue.EndMaintenance(); _operation.Release(); }
    }

    private void Set(UpdatePhase phase, UpdateError? error = null)
    {
        Phase = phase; Error = error; Changed?.Invoke();
    }
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath + ".tmp", JsonSerializer.Serialize(Cache, UpdateJsonContext.Default.UpdateCache));
            File.Move(_cachePath + ".tmp", _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _download?.Cancel();
        NetworkChange.NetworkAvailabilityChanged -= NetworkChanged;
        NetworkChange.NetworkAddressChanged -= AddressChanged;
        NetworkInformation.NetworkStatusChanged -= StatusChanged;
        Notifications.Dispose();
    }
}
