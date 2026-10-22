using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Updates;

public sealed class ReleaseAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

public sealed class UpdateRelease
{
    [JsonPropertyName("tag_name")] public string Tag { get; set; } = "";
    [JsonPropertyName("body")] public string Notes { get; set; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? Published { get; set; }
    [JsonPropertyName("assets")] public List<ReleaseAsset> Assets { get; set; } = [];

    public ReleaseAsset Asset(string name)
    {
        var found = Assets.Where(a => a.Name == name).ToList();
        return found.Count == 1 && found[0].Size > 0 ? found[0] : throw new UpdateException(UpdateError.InvalidRelease);
    }
}

public sealed class SignedUpdateManifest
{
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "win-x64";
    public List<SignedUpdateAsset> Assets { get; set; } = [];
}

public sealed class SignedUpdateAsset
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class UpdateCache
{
    public UpdateRelease? Release { get; set; }
    public DateTimeOffset? Checked { get; set; }
    public string? ETag { get; set; }
    public string AvailableNotice { get; set; } = "";
    public string ReadyNotice { get; set; } = "";
    public string CancelledVersion { get; set; } = "";
    public string DismissedVersion { get; set; } = "";
    public string SpanishNotes { get; set; } = "";
    public string EnglishNotes { get; set; } = "";
    // Solo una comprobación automática autoriza la descarga automática de esta versión.
    public string AutomaticVersion { get; set; } = "";
    public string InstallingVersion { get; set; } = "";
    public string InstallingFromVersion { get; set; } = "";
    public string UpdatedVersion { get; set; } = "";
    public string UpdatedNotice { get; set; } = "";
    public string ViewedVersion { get; set; } = "";
}

public enum UpdateError { Network, InvalidRelease, UntrustedUrl, MissingTrust, InvalidSignature, InvalidChecksum, TooLarge, Busy, Installer, RestrictedNetwork }
public sealed class UpdateException(UpdateError error) : Exception(error.ToString())
{
    public UpdateError Error { get; } = error;
}
public enum UpdatePhase { Unknown, Checking, Current, Available, Downloading, Verifying, Ready, WaitingForNetwork, Installing, Error }
public readonly record struct UpdateProgress(long Received, long Total, bool Verifying = false);
public readonly record struct UpdateNetwork(bool Known, bool WifiOrEthernet, bool Mobile, bool Metered, bool Restricted);

/// <summary>Decisiones puras para comprobarlas sin depender de Windows ni del reloj del equipo.</summary>
public static class UpdatePolicy
{
    public static bool CheckDue(DateTimeOffset? lastSuccess, DateTimeOffset now) =>
        lastSuccess is not { } date || date > now || now - date >= TimeSpan.FromHours(48);

    public static bool CanDownloadAutomatically(UpdateCache cache, bool check, bool download) =>
        check && download && cache.Release is { } release && cache.AutomaticVersion == release.Tag
        && cache.CancelledVersion != release.Tag;

    public static bool CompleteInstallation(UpdateCache cache, ReleaseVersion current)
    {
        var target = ReleaseVersion.Parse(cache.InstallingVersion);
        var source = ReleaseVersion.Parse(cache.InstallingFromVersion);
        if (target is null || source is null || target.CompareTo(source) <= 0 || current.CompareTo(target) != 0) return false;
        cache.UpdatedVersion = current.ToString();
        cache.InstallingVersion = cache.InstallingFromVersion = "";
        return true;
    }

    public static bool Allows(UpdateNetwork network, bool local, bool mobile) => network.Known && !network.Restricted
        && (network.Mobile || network.Metered ? mobile : network.WifiOrEthernet && local);

}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false, WriteIndented = true)]
[JsonSerializable(typeof(UpdateRelease))]
[JsonSerializable(typeof(UpdateCache))]
[JsonSerializable(typeof(SignedUpdateManifest))]
public sealed partial class UpdateJsonContext : JsonSerializerContext;
