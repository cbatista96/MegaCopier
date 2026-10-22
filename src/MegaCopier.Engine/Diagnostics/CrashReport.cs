using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MegaCopier.Engine.Diagnostics;

public sealed record NativeFault(string Module, string Code, string Offset);
public sealed class CrashReport
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public string Version { get; set; } = "";
    public string Windows { get; set; } = "";
    public string Language { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Theme { get; set; } = "";
    public string Activity { get; set; } = "idle";
    public string Kind { get; set; } = "unexpected";
    public string Trace { get; set; } = "";
    public string RecentLog { get; set; } = "";
    public NativeFault? Native { get; set; }
    public string Hash { get; set; } = "";
    private string Fingerprint() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Version}\n{Trace}\n{Native?.Module}\n{Native?.Code}\n{Native?.Offset}")));
    public void SetHash() => Hash = Fingerprint();
    public bool IsSafe() => new[] { Id, Version, Windows, Language, Mode, Theme, Activity, Kind, Trace, RecentLog, Hash }.All(x => x is not null)
        && Schema == 1 && Guid.TryParseExact(Id, "N", out _) && Hash == Fingerprint()
        && Regex.IsMatch(Version, @"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && Regex.IsMatch(Windows, @"^\d{1,6}(?:\.\d{1,6}){1,3}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && Regex.IsMatch(Language, @"^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,3}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && Regex.IsMatch(Theme, @"^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        && Mode is "installed" or "portable" or "development" && Activity is "idle" or "copying" or "paused" or "settings" or "dragging"
        && Kind is "unexpected" or "managed" or "native" && Trace.Length <= 12000 && RecentLog.Length <= 12000
        && CrashPrivacy.SafeTrace(Trace) == Trace && CrashPrivacy.SafeTrace(RecentLog) == RecentLog
        && (Native is null || Native.Module is not null && Native.Code is { Length: > 0 } && Native.Offset is { Length: > 0 }
            && CrashPrivacy.Module(Native.Module) == Native.Module && CrashPrivacy.Hex(Native.Code) == Native.Code && CrashPrivacy.Hex(Native.Offset) == Native.Offset);
}

/// <summary>Solo conserva tipos, códigos y nombres de funciones; descarta mensajes libres y parámetros.</summary>
public static class CrashPrivacy
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.CultureInvariant, Limit);

    public static string Redact(string text, params string[] identities)
    {
        if (text.Length > 65536) text = text[^65536..];
        foreach (string identity in identities.Where(i => !string.IsNullOrWhiteSpace(i)).OrderByDescending(i => i.Length))
            text = text.Replace(identity, "<privado>", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?i)[a-z]:[\\/](?:Users|Documents and Settings)[\\/][^\\/\s]+", "%USERPROFILE%", RegexOptions.CultureInvariant, Limit);
        text = Regex.Replace(text, @"(?i)(?:[a-z]:[\\/]|\\\\|%USERPROFILE%[\\/])[^\r\n\""<>|]*", "<ruta>", RegexOptions.CultureInvariant, Limit);
        text = Regex.Replace(text, @"(?i)\b(?:https?|ftp|sftp|smb)://\S+|\b[^\s@]+@[^\s@]+\.[^\s@]+|\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[0-9a-f]{0,4}(?::[0-9a-f]{0,4}){2,}\b", "<privado>", RegexOptions.CultureInvariant, Limit);
        return Regex.Replace(text, @"(?i)\b[^\s<>/\\:]+\.(pdf|docx?|xlsx?|pptx?|jpe?g|png|txt|mp4|zip|7z)\b", "<archivo>.$1", RegexOptions.CultureInvariant, Limit);
    }

    public static string SafeTrace(string text, params string[] identities)
    {
        var result = new List<string>();
        foreach (string raw in (text.Length > 65536 ? text[^65536..] : text).Split('\n').TakeLast(50))
        {
            string line = Redact(raw, identities).Trim();
            var frame = Match(line, @"^(?:at|en) ((?:System|Microsoft|MegaCopier|WinRT)\.[A-Za-z0-9_.+`<>]+)\(");
            if (frame.Success) { result.Add("at " + frame.Groups[1].Value[..Math.Min(240, frame.Groups[1].Value.Length)] + "()"); continue; }
            var type = Match(line, @"^((?:System|Microsoft|MegaCopier|WinRT)\.[A-Za-z0-9_.+`]+(?:Exception|Error))\b");
            if (type.Success)
            {
                var code = Match(raw, @"\b0x[0-9a-fA-F]{8}\b");
                result.Add(type.Value + (code.Success ? " " + code.Value : ""));
            }
        }
        // El límite nunca debe cortar una función por la mitad y volver inválido el informe.
        while (result.Count > 0 && result.Sum(line => line.Length + 1) - 1 > 12000) result.RemoveAt(result.Count - 1);
        return string.Join('\n', result);
    }

    public static string Module(string module)
    {
        string name = module.Replace('/', '\\').Split('\\').Last();
        return Match(name, @"^(?:MegaCopier\.exe|(?:Microsoft\.[A-Za-z0-9_.-]+|Windows\.[A-Za-z0-9_.-]+|System\.[A-Za-z0-9_.-]+|ntdll|kernel32|KERNELBASE|combase|ucrtbase|coreclr|CoreMessaging|CoreMessagingXP)\.dll)$").Success ? name : "<modulo>";
    }
    public static string Hex(string value) => Match(value, @"^(?:0x)?[0-9a-fA-F]{1,16}$").Success ? value.ToLowerInvariant() : "";
}

public sealed class CrashLedger
{
    public DateOnly Day { get; set; }
    public int Attempts { get; set; }
    public Dictionary<string, DateTimeOffset> Sent { get; set; } = [];
}
public sealed record CrashReceipt(bool Accepted);
public sealed record CrashPending(CrashReport Report, bool AutomaticConsent);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CrashReport))]
[JsonSerializable(typeof(CrashLedger))]
[JsonSerializable(typeof(CrashReceipt))]
[JsonSerializable(typeof(CrashPending))]
public sealed partial class CrashJsonContext : JsonSerializerContext;
