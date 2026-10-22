using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Diagnostics;

public sealed class CrashRun
{
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public int Pid { get; set; } = Environment.ProcessId;
    public string Executable { get; set; } = Environment.ProcessPath ?? "";
    public string Version { get; set; } = "";
    public string Activity { get; set; } = "idle";
    public string Fault { get; set; } = "";
}
[JsonSerializable(typeof(CrashRun))]
internal sealed partial class CrashRunJsonContext : JsonSerializerContext;

/// <summary>Marca local de sesión. Una marca sobrante no distingue caída, apagón o cierre forzado.</summary>
public sealed class CrashSession
{
    private readonly string _path;
    public CrashRun Current { get; }
    public CrashRun? Previous { get; }
    public CrashSession(string directory, string version)
    {
        _path = Path.Combine(directory, "running.json");
        Current = new() { Version = version };
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(_path))
            {
                try { Previous = JsonSerializer.Deserialize(File.ReadAllText(_path), CrashRunJsonContext.Default.CrashRun); }
                catch (JsonException) { Previous = new() { Started = File.GetLastWriteTimeUtc(_path), Pid = 0 }; }
            }
            Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void Save()
    {
        try
        {
            using (var file = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, Current, CrashRunJsonContext.Default.CrashRun); file.Flush(true);
            }
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public void Activity(string value)
    {
        if (value == Current.Activity) return;
        Current.Activity = value is "copying" or "paused" or "settings" or "dragging" ? value : "idle"; Save();
    }
    public void Fatal(Exception? error)
    {
        Current.Fault = error is null ? "" : CrashPrivacy.SafeTrace($"{error.GetType().FullName} 0x{error.HResult:X8}\n{error.StackTrace}"); Save();
    }
    public void Clean()
    {
        try { File.Delete(_path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    public static string RecentLog(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Max(0, stream.Length - 65536), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return CrashPrivacy.SafeTrace(string.Join('\n', reader.ReadToEnd().Split('\n').TakeLast(50)), Environment.UserName, Environment.MachineName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}
