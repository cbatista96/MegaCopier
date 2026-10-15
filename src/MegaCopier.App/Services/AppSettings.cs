using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using Microsoft.Win32;

namespace MegaCopier.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Preferencias del usuario, en %LOCALAPPDATA%\MegaCopier\settings.json.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool VerifyAfterCopy { get; set; }

    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Overwrite;

    /// <summary>Al arrancar, continuar sin preguntar las copias que quedaron a medias.</summary>
    public bool AutoResumeInterrupted { get; set; }

    /// <summary>Mostrar la ventana cuando llega una copia nueva.</summary>
    public bool ShowWindowOnNewCopy { get; set; } = true;

    /// <summary>Aviso del sistema al terminar una copia si la ventana no está a la vista.</summary>
    public bool NotifyOnFinish { get; set; } = true;

    /// <summary>Al pegar archivos con Ctrl+V en el Explorador, copiar con MegaCopier.</summary>
    public bool InterceptExplorerPaste { get; set; } = true;

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaCopier", "settings.json");

    public CopyOptions NewJobOptions(IpcRequest request) => new()
    {
        Verify = request.Verify ?? VerifyAfterCopy,
        Conflict = request.Conflict ?? Conflict,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferencias dañadas: se usan las de fábrica.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, this, SettingsJsonContext.Default.AppSettings);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, FilePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Arranque con Windows (clave Run del usuario). En segundo plano: solo el icono de la bandeja.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MegaCopier";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
