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

/// <summary>Unidades de tamaño: binarias con nombre clásico (como el Explorador), binarias IEC o decimales (como el fabricante del disco).</summary>
public enum SizeUnits
{
    Classic,
    Iec,
    Decimal,
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

    /// <summary>Pedir compresión SMB al copiar por red.</summary>
    public bool SmbCompression { get; set; }

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

    /// <summary>Carpeta (dentro del destino) donde guardar lo reemplazado o borrado; vacío = no guardar.</summary>
    public string VersionsFolder { get; set; } = "";

    public FolderConflictPolicy FolderConflict { get; set; }

    public double TimeToleranceSeconds { get; set; } = 2;

    public bool VerifySource { get; set; }

    public bool SaveHashStream { get; set; }

    public bool PreserveHardLinks { get; set; }

    public bool WipeSource { get; set; }

    public bool WaitForDevice { get; set; } = true;

    public bool WaitForSpace { get; set; } = true;

    public bool AllOrNothing { get; set; }

    public bool RescueMode { get; set; }

    public bool DeltaCopy { get; set; }

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
        get => new()
        {
            Filter = Filter, Links = Links, KeepBothPattern = KeepBothPattern, RetryCount = RetryCount, RetryDelaySeconds = RetryDelaySeconds, OnError = OnError,
            VersionsFolder = VersionsFolder, FolderConflict = FolderConflict, TimeToleranceSeconds = TimeToleranceSeconds, VerifySource = VerifySource,
            SaveHashStream = SaveHashStream, PreserveHardLinks = PreserveHardLinks, WipeSource = WipeSource, WaitForDevice = WaitForDevice, WaitForSpace = WaitForSpace,
            AllOrNothing = AllOrNothing,
            RescueMode = RescueMode,
            DeltaCopy = DeltaCopy,
        };
        set
        {
            Filter = value.Filter;
            Links = value.Links;
            KeepBothPattern = value.KeepBothPattern;
            RetryCount = value.RetryCount;
            RetryDelaySeconds = value.RetryDelaySeconds;
            OnError = value.OnError;
            VersionsFolder = value.VersionsFolder;
            FolderConflict = value.FolderConflict;
            TimeToleranceSeconds = value.TimeToleranceSeconds;
            VerifySource = value.VerifySource;
            SaveHashStream = value.SaveHashStream;
            PreserveHardLinks = value.PreserveHardLinks;
            WipeSource = value.WipeSource;
            WaitForDevice = value.WaitForDevice;
            WaitForSpace = value.WaitForSpace;
            AllOrNothing = value.AllOrNothing;
            RescueMode = value.RescueMode;
            DeltaCopy = value.DeltaCopy;
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

    /// <summary>Mientras usas el teclado o el ratón, las copias no pasan de esta velocidad (0 = sin límite).</summary>
    public int SlowWhenActiveMBps { get; set; }

    /// <summary>Límite por horario (0 = sin límite), entre <see cref="ScheduleFrom"/> y <see cref="ScheduleTo"/>.</summary>
    public int ScheduleLimitMBps { get; set; }

    public TimeSpan ScheduleFrom { get; set; } = new(8, 0, 0);

    public TimeSpan ScheduleTo { get; set; } = new(18, 0, 0);

    /// <summary>Pausar las copias con el portátil desenchufado (y seguir al enchufarlo).</summary>
    public bool PauseOnBattery { get; set; }

    /// <summary>Pausar durante juegos y presentaciones a pantalla completa.</summary>
    public bool PauseOnFullScreen { get; set; }

    /// <summary>Cómo se muestran los tamaños.</summary>
    public SizeUnits Units { get; set; }

    /// <summary>Guardar el registro de cada copia en disco (carpeta "logs"), los últimos <see cref="LogRetentionDays"/> días.</summary>
    public bool SaveLogs { get; set; } = true;

    public int LogRetentionDays { get; set; } = 30;

    /// <summary>Dirección a la que avisar (POST con JSON) al terminar cada copia: Discord, Slack, Teams, ntfy, Home Assistant...</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>Carpetas vigiladas por el guardián de integridad.</summary>
    public List<GuardedFolder> GuardedFolders { get; set; } = [];

    /// <summary>Cada cuántos días se comprueba cada carpeta vigilada.</summary>
    public int GuardIntervalDays { get; set; } = 7;

    /// <summary>Recibir archivos de otros equipos con MegaCopier.</summary>
    public bool ReceiveEnabled { get; set; }

    /// <summary>Dónde se guardan (vacío = DescargasMegaCopier).</summary>
    public string ReceiveFolder { get; set; } = "";

    /// <summary>Código que hay que dar a quien envía.</summary>
    public string ReceiveCode { get; set; } = "";

    public int ReceivePort { get; set; } = Engine.Network.PeerProtocol.DefaultPort;

    /// <summary>Último equipo al que se envió (para proponerlo la próxima vez).</summary>
    public string LastPeer { get; set; } = "";

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
        Engine.DataFolder.Path, "settings.json");

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
            SmbCompression = SmbCompression,
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
            Mode = request.Mode ?? CopyMode.Copy,
            DryRun = request.DryRun ?? false,
            VersionsFolder = VersionsFolder.Trim(),
            FolderConflict = FolderConflict,
            TimeTolerance = TimeSpan.FromSeconds(Math.Clamp(TimeToleranceSeconds, 0, 3600)),
            VerifySource = VerifySource,
            SaveHashStream = SaveHashStream,
            PreserveHardLinks = PreserveHardLinks,
            WipeSource = WipeSource,
            WaitForDevice = WaitForDevice,
            WaitForSpace = WaitForSpace,
            AllOrNothing = AllOrNothing,
            RescueMode = RescueMode,
            DeltaCopy = DeltaCopy,
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
        Rules = Views.CopyRules.From(r);
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
        Rules.ApplyTo(r);
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
