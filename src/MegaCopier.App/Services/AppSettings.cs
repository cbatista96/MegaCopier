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

public enum AppBackdrop
{
    Mica,
    MicaAlt,
    Acrylic,
    Solid,
}

/// <summary>Cómo se abre la ventana de copia.</summary>
public enum WindowMode
{
    /// <summary>Compacta con una copia; completa cuando hay varias a la vez.</summary>
    Auto,
    Compact,
    Expanded,
}

/// <summary>Qué hace la ventana cuando terminan las copias.</summary>
public enum FinishWindow
{
    /// <summary>Se cierra si todo fue bien; se queda abierta si hubo problemas.</summary>
    Auto,
    Close,
    KeepOpen,
}

public enum ProcessPriority
{
    Low,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
}

/// <summary>Preferencias del usuario, en %LOCALAPPDATA%\MegaCopier\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Tema completo de la interfaz ("fluent-auto" sigue el modo claro/oscuro de Windows).</summary>
    public string ThemeId { get; set; } = "fluent-auto";

    public WindowMode WindowMode { get; set; } = WindowMode.Auto;

    public FinishWindow FinishWindow { get; set; } = FinishWindow.Auto;

    /// <summary>Sonidos por momento, volumen y silencio con pantalla completa.</summary>
    public SoundSettings Sounds { get; set; } = new();

    /// <summary>Ajuste antiguo (activar sonidos); equivale a Sounds.Enabled.</summary>
    public bool PlaySound
    {
        get => Sounds.Enabled;
        set => Sounds.Enabled = value;
    }

    /// <summary>Máximo de trabajos activos a la vez (0 = sin límite).</summary>
    public int MaxActiveJobs { get; set; } = 3;

    /// <summary>Rutas de dispositivos rápidos separadas por comas ("C:, D:\Datos, \\servidor\recurso").</summary>
    public string FastDevices { get; set; } = "";

    public ProcessPriority Priority { get; set; } = ProcessPriority.Normal;

    public bool CheckFreeSpace { get; set; } = true;

    public bool UseSystemCache { get; set; }

    public bool HideFinishedFiles { get; set; } = true;

    public bool VerifySkipped { get; set; }

    public FileHashAlgorithm HashAlgorithm { get; set; } = FileHashAlgorithm.XxHash3;

    public bool CopyStreams { get; set; } = true;

    public Engine.Native.SecurityParts CopySecurity { get; set; }

    public bool PreserveTimestamps { get; set; } = true;

    public bool PreserveAttributes { get; set; } = true;

    public bool SaveChecksumFile { get; set; }

    public bool ExportReport { get; set; }

    public int BlockSizeMB { get; set; } = 4;

    public int QueueDepth { get; set; } = 4;

    public List<string> Scripts { get; set; } = [];

    public FileFilter Filter { get; set; } = new();

    public LinkHandling Links { get; set; }

    public string KeepBothPattern { get; set; } = CopyOptions.DefaultKeepBothPattern;

    public int RetryCount { get; set; } = 2;

    public double RetryDelaySeconds { get; set; } = 1;

    public ErrorPolicy OnError { get; set; }

    /// <summary>Carpetas de destino favoritas (menú "Copiar a" y diálogo de destinos).</summary>
    public List<string> FavoriteFolders { get; set; } = [];

    /// <summary>Últimos destinos usados (los 10 más recientes primero).</summary>
    public List<string> RecentDestinations { get; set; } = [];

    /// <summary>Anota un destino como el más reciente.</summary>
    public void RememberDestination(string folder)
    {
        RecentDestinations.RemoveAll(d => string.Equals(d, folder, StringComparison.OrdinalIgnoreCase));
        RecentDestinations.Insert(0, folder);
        if (RecentDestinations.Count > 10)
        {
            RecentDestinations.RemoveRange(10, RecentDestinations.Count - 10);
        }
    }

    [JsonIgnore]
    public Views.CopyRules Rules
    {
        get => new() { Filter = Filter, Links = Links, KeepBothPattern = KeepBothPattern, RetryCount = RetryCount, RetryDelaySeconds = RetryDelaySeconds, OnError = OnError };
        set
        {
            Filter = value.Filter;
            Links = value.Links;
            KeepBothPattern = value.KeepBothPattern;
            RetryCount = value.RetryCount;
            RetryDelaySeconds = value.RetryDelaySeconds;
            OnError = value.OnError;
        }
    }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>"system" o un color "#RRGGBB".</summary>
    public string Accent { get; set; } = "system";

    public AppBackdrop Backdrop { get; set; } = AppBackdrop.Mica;

    public bool AlwaysOnTop { get; set; }

    /// <summary>"" = el de Windows; "es-ES" o "en-US". Se aplica al reiniciar la app.</summary>
    public string Language { get; set; } = "";

    public bool VerifyAfterCopy { get; set; }

    /// <summary>Por defecto pregunta, como el Explorador.</summary>
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;

    /// <summary>Límite de velocidad de las copias nuevas en MB/s (0 = sin límite).</summary>
    public int SpeedLimitMBps { get; set; }

    /// <summary>Al arrancar, continuar sin preguntar las copias que quedaron a medias.</summary>
    public bool AutoResumeInterrupted { get; set; }

    /// <summary>Mostrar la ventana cuando llega una copia nueva.</summary>
    public bool ShowWindowOnNewCopy { get; set; } = true;

    /// <summary>Aviso del sistema al terminar una copia si la ventana no está a la vista.</summary>
    public bool NotifyOnFinish { get; set; } = true;

    /// <summary>Al pegar archivos con Ctrl+V en el Explorador, copiar con MegaCopier.</summary>
    public bool InterceptExplorerPaste { get; set; } = true;

    /// <summary>
    /// Interruptor general (doble clic en el icono de la bandeja). Desactivado, MegaCopier no intercepta nada
    /// y copia el Explorador de Windows como siempre.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>settings.json no se pudo leer (se guardó una copia en settings.json.bad).</summary>
    [JsonIgnore]
    public bool LoadFailed { get; private init; }

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaCopier", "settings.json");

    public CopyOptions NewJobOptions(IpcRequest request)
    {
        var options = new CopyOptions
        {
            Verify = request.Verify ?? VerifyAfterCopy,
            Conflict = request.Conflict ?? Conflict,
            Move = request.Move ?? false,
            MaxBytesPerSecond = SpeedLimitMBps * (long)CopyOptions.MiB,
            CheckFreeSpace = CheckFreeSpace,
            UseUnbufferedIo = !UseSystemCache,
            VerifySkipped = VerifySkipped,
            HashAlgorithm = HashAlgorithm,
            CopyStreams = CopyStreams,
            CopySecurity = CopySecurity,
            PreserveTimestamps = PreserveTimestamps,
            PreserveAttributes = PreserveAttributes,
            SaveChecksumFile = SaveChecksumFile,
            ReadQueueDepth = Math.Clamp(QueueDepth, 1, 16),
            WriteQueueDepth = Math.Clamp(QueueDepth, 1, 16),
            Filter = Filter,
            Links = Links,
            KeepBothPattern = KeepBothPattern,
            RetryCount = Math.Clamp(RetryCount, 0, 100),
            RetryDelay = TimeSpan.FromSeconds(Math.Clamp(RetryDelaySeconds, 0, 3600)),
            OnError = OnError,
        };

        int block = Math.Clamp(BlockSizeMB, 1, 16) * CopyOptions.MiB;
        if (options.MaxBytesPerSecond > 0)
        {
            // Con límite de velocidad, bloques de ~1/8 de segundo: avance y velocidad suaves en lugar de ráfagas.
            int smooth = (int)Math.Clamp(options.MaxBytesPerSecond / 8 / 65536 * 65536, 65536, block);
            block = smooth;
        }

        options.BlockSize = block;
        options.ChunkSize = 64L * CopyOptions.MiB / block * block;
        return options;
    }

    /// <summary>"Guardar como predeterminado" desde la pestaña Opciones de un trabajo.</summary>
    public void CopyDefaultsFrom(Engine.Jobs.CopyJobRecord r)
    {
        PreserveTimestamps = r.PreserveTimestamps;
        PreserveAttributes = r.PreserveAttributes;
        CopyStreams = r.CopyStreams;
        CopySecurity = r.CopySecurity;
        Conflict = r.Conflict;
        HashAlgorithm = r.HashAlgorithm;
        BlockSizeMB = r.BlockSize > 0 ? r.BlockSize / CopyOptions.MiB : 4;
        QueueDepth = r.QueueDepth > 0 ? r.QueueDepth : 4;
        VerifyAfterCopy = r.Verify;
        VerifySkipped = r.VerifySkipped;
        UseSystemCache = r.UseSystemCache;
        SaveChecksumFile = r.SaveChecksumFile;
        ExportReport = r.ExportReport;
        Scripts = [.. r.Scripts];
        Filter = r.Filter;
        Links = r.Links;
        KeepBothPattern = r.KeepBothPattern;
        RetryCount = r.RetryCount;
        RetryDelaySeconds = r.RetryDelaySeconds;
        OnError = r.OnError;
    }

    /// <summary>"Restablecer": devuelve el trabajo a las opciones predeterminadas.</summary>
    public void ApplyDefaultsTo(Engine.Jobs.CopyJobRecord r)
    {
        r.PreserveTimestamps = PreserveTimestamps;
        r.PreserveAttributes = PreserveAttributes;
        r.CopyStreams = CopyStreams;
        r.CopySecurity = CopySecurity;
        r.Conflict = Conflict;
        r.HashAlgorithm = HashAlgorithm;
        r.BlockSize = BlockSizeMB * CopyOptions.MiB;
        r.ChunkSize = 64L * CopyOptions.MiB / r.BlockSize * r.BlockSize;
        r.QueueDepth = QueueDepth;
        r.Verify = VerifyAfterCopy;
        r.VerifySkipped = VerifySkipped;
        r.UseSystemCache = UseSystemCache;
        r.SaveChecksumFile = SaveChecksumFile;
        r.ExportReport = ExportReport;
        r.Scripts = [.. Scripts];
        r.Filter = Filter;
        r.Links = Links;
        r.KeepBothPattern = KeepBothPattern;
        r.RetryCount = RetryCount;
        r.RetryDelaySeconds = RetryDelaySeconds;
        r.OnError = OnError;
    }

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
        catch (JsonException)
        {
            // Preferencias dañadas: se usan las de fábrica, pero el archivo se aparta (no se pisa) para poder recuperarlo.
            try
            {
                File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return new AppSettings { LoadFailed = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
