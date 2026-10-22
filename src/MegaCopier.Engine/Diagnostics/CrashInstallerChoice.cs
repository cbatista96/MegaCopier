using System.Text.Json;

namespace MegaCopier.Engine.Diagnostics;

/// <summary>Consulta de solo lectura para el instalador: 0 sí, 1 no, 2 sin elección válida.</summary>
public static class CrashInstallerChoice
{
    public static int Read(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath) || new FileInfo(settingsPath).Length > 4 * 1024 * 1024) return 2;
            using var stream = File.OpenRead(settingsPath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("SendCrashReportsAutomatically", out var value)) return 2;
            return value.ValueKind == JsonValueKind.True ? 0 : value.ValueKind == JsonValueKind.False ? 1 : 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return 2; }
    }
}
