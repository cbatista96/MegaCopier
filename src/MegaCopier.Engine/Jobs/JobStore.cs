using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Jobs;

/// <summary>Lo necesario para volver a lanzar un trabajo interrumpido.</summary>
public sealed class CopyJobRecord
{
    public string Id { get; set; } = "";

    public List<string> Sources { get; set; } = [];

    public string Destination { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public ConflictPolicy Conflict { get; set; }

    public bool Verify { get; set; }

    public long ChunkSize { get; set; }

    public int BlockSize { get; set; }

    public int QueueDepth { get; set; }

    public bool Move { get; set; }

    public long MaxBytesPerSecond { get; set; }

    /// <summary>Archivos que el usuario quitó de la lista.</summary>
    public List<string> Excluded { get; set; } = [];

    public bool Unattended { get; set; }

    public FileHashAlgorithm HashAlgorithm { get; set; } = FileHashAlgorithm.XxHash3;

    public bool CopyStreams { get; set; } = true;

    public Native.SecurityParts CopySecurity { get; set; }

    public bool SaveChecksumFile { get; set; }

    public bool VerifySkipped { get; set; }

    public bool PreserveTimestamps { get; set; } = true;

    public bool PreserveAttributes { get; set; } = true;

    public bool UseSystemCache { get; set; }

    /// <summary>Al terminar, guardar un informe HTML junto al destino.</summary>
    public bool ExportReport { get; set; }

    /// <summary>Comandos o scripts a ejecutar al terminar (reciben las variables MEGACOPIER_*).</summary>
    public List<string> Scripts { get; set; } = [];

    public JobOperation Operation { get; set; }

    public bool SecureDelete { get; set; }

    public WipeMethod WipeMethod { get; set; }

    public bool EfsRaw { get; set; }

    public bool RansomwareShield { get; set; } = true;

    public int RecoveryPercent { get; set; }

    public bool Forensic { get; set; }

    public string SnapshotId { get; set; } = "";

    public bool Zip { get; set; }

    public IO.ArchiveFormat ArchiveFormat { get; set; }

    public int RemoteConnections { get; set; } = 8;

    public bool RemoteConnectionsAuto { get; set; } = true;

    public List<string> AlternateSources { get; set; } = [];

    public LinkHandling Links { get; set; } = LinkHandling.CopyLink;

    public string KeepBothPattern { get; set; } = CopyOptions.DefaultKeepBothPattern;

    public FileFilter Filter { get; set; } = new();

    public int RetryCount { get; set; } = 2;

    public double RetryDelaySeconds { get; set; } = 1;

    public ErrorPolicy OnError { get; set; }

    public bool CheckFreeSpace { get; set; } = true;

    public CopyMode Mode { get; set; }

    public SyncDirection SyncDirection { get; set; }

    public bool DryRun { get; set; }

    public string VersionsFolder { get; set; } = "";

    public int VersionsDaily { get; set; }

    public int VersionsWeekly { get; set; }

    public int VersionsMonthly { get; set; }

    public FolderConflictPolicy FolderConflict { get; set; }

    public double TimeToleranceSeconds { get; set; } = 2;

    public bool VerifySource { get; set; }

    public bool SaveHashStream { get; set; }

    public bool PreserveHardLinks { get; set; }

    public bool WipeSource { get; set; }

    public bool WaitForDevice { get; set; } = true;

    public bool WaitForSpace { get; set; } = true;

    public bool SmbCompression { get; set; }

    public bool AllOrNothing { get; set; }

    public bool RescueMode { get; set; }

    public bool DeltaCopy { get; set; }

    public bool SkipDuplicates { get; set; }

    public bool Encrypt { get; set; }

    public string OrganizePattern { get; set; } = CopyOptions.DefaultOrganizePattern;

    public string PeerCode { get; set; } = "";

    /// <summary>Ya se ejecutó alguna vez (al reanudar no se vuelve a aplicar la regla de carpetas existentes).</summary>
    public bool Started { get; set; }

    /// <summary>Orden de los pendientes elegido arrastrando (rutas de origen).</summary>
    public List<string> FileOrder { get; set; } = [];

    /// <summary>Carpetas de origen que el usuario eligió copiar con otro nombre (ruta de origen → nombre nuevo).</summary>
    public Dictionary<string, string> RenamedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hora programada de inicio (la cola no lo empieza antes).</summary>
    public DateTime? StartAtUtc { get; set; }

    public static CopyJobRecord New(IEnumerable<string> sources, string destination, CopyOptions options) => new()
    {
        Move = options.Move,
        MaxBytesPerSecond = options.MaxBytesPerSecond,
        Unattended = options.Unattended,
        HashAlgorithm = options.HashAlgorithm,
        CopyStreams = options.CopyStreams,
        CopySecurity = options.CopySecurity,
        SaveChecksumFile = options.SaveChecksumFile,
        VerifySkipped = options.VerifySkipped,
        PreserveTimestamps = options.PreserveTimestamps,
        PreserveAttributes = options.PreserveAttributes,
        UseSystemCache = !options.UseUnbufferedIo,
        Id = Guid.NewGuid().ToString("N")[..8],
        Sources = [.. sources],
        Destination = destination,
        CreatedUtc = DateTime.UtcNow,
        Conflict = options.Conflict,
        Verify = options.Verify,
        ChunkSize = options.ChunkSize,
        BlockSize = options.BlockSize,
        QueueDepth = options.WriteQueueDepth,
        Operation = options.Operation,
        SecureDelete = options.SecureDelete,
        WipeMethod = options.WipeMethod,
        EfsRaw = options.EfsRaw,
        RansomwareShield = options.RansomwareShield,
        RecoveryPercent = options.RecoveryPercent,
        Forensic = options.Forensic,
        SnapshotId = options.SnapshotId,
        Zip = options.Zip,
        ArchiveFormat = options.ArchiveFormat,
        RemoteConnections = options.RemoteConnections,
        RemoteConnectionsAuto = options.RemoteConnectionsAuto,
        AlternateSources = [.. options.AlternateSources],
        Links = options.Links,
        KeepBothPattern = options.KeepBothPattern,
        Filter = options.Filter,
        RetryCount = options.RetryCount,
        RetryDelaySeconds = options.RetryDelay.TotalSeconds,
        OnError = options.OnError,
        CheckFreeSpace = options.CheckFreeSpace,
        Mode = options.Mode,
        SyncDirection = options.SyncDirection,
        DryRun = options.DryRun,
        VersionsFolder = options.VersionsFolder,
        VersionsDaily = options.VersionsDaily,
        VersionsWeekly = options.VersionsWeekly,
        VersionsMonthly = options.VersionsMonthly,
        FolderConflict = options.FolderConflict,
        TimeToleranceSeconds = options.TimeTolerance.TotalSeconds,
        VerifySource = options.VerifySource,
        SaveHashStream = options.SaveHashStream,
        PreserveHardLinks = options.PreserveHardLinks,
        WipeSource = options.WipeSource,
        WaitForDevice = options.WaitForDevice,
        WaitForSpace = options.WaitForSpace,
        SmbCompression = options.SmbCompression,
        AllOrNothing = options.AllOrNothing,
        RescueMode = options.RescueMode,
        DeltaCopy = options.DeltaCopy,
        SkipDuplicates = options.SkipDuplicates,
        Encrypt = options.Encrypt,
        OrganizePattern = options.OrganizePattern,
        PeerCode = options.PeerCode,
    };

    /// <summary>Lista de copia guardada (.mclist): orígenes, destino, opciones y archivos excluidos.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JobJsonContext.Default.CopyJobRecord);

    public static CopyJobRecord? FromJson(string json) => JsonSerializer.Deserialize(json, JobJsonContext.Default.CopyJobRecord);

    public CopyOptions ToOptions()
    {
        // El bloque se guarda junto al tamaño de confirmación: ambos deben seguir encajando al reanudar.
        var options = new CopyOptions
        {
            Conflict = Conflict,
            Verify = Verify,
            Move = Move,
            MaxBytesPerSecond = MaxBytesPerSecond,
            Unattended = Unattended,
            HashAlgorithm = HashAlgorithm,
            CopyStreams = CopyStreams,
            CopySecurity = CopySecurity,
            SaveChecksumFile = SaveChecksumFile,
            VerifySkipped = VerifySkipped,
            PreserveTimestamps = PreserveTimestamps,
            PreserveAttributes = PreserveAttributes,
            UseUnbufferedIo = !UseSystemCache,
            Operation = Operation,
            SecureDelete = SecureDelete,
            WipeMethod = WipeMethod,
            EfsRaw = EfsRaw,
            RansomwareShield = RansomwareShield,
            RecoveryPercent = RecoveryPercent,
            Forensic = Forensic,
            SnapshotId = SnapshotId ?? "",
            Zip = Zip,
            ArchiveFormat = ArchiveFormat,
            RemoteConnections = RemoteConnections,
            RemoteConnectionsAuto = RemoteConnectionsAuto,
            AlternateSources = [.. AlternateSources ?? []],
            Links = Links,
            KeepBothPattern = KeepBothPattern,
            Filter = Filter ?? new FileFilter(),
            RetryCount = Math.Clamp(RetryCount, 0, 100),
            RetryDelay = TimeSpan.FromSeconds(Math.Clamp(RetryDelaySeconds, 0, 3600)),
            OnError = OnError,
            CheckFreeSpace = CheckFreeSpace,
            Mode = Mode,
            SyncDirection = SyncDirection,
            DryRun = DryRun,
            VersionsFolder = VersionsFolder ?? "",
            VersionsDaily = VersionsDaily,
            VersionsWeekly = VersionsWeekly,
            VersionsMonthly = VersionsMonthly,
            FolderConflict = FolderConflict,
            TimeTolerance = TimeSpan.FromSeconds(Math.Clamp(TimeToleranceSeconds, 0, 3600)),
            VerifySource = VerifySource,
            SaveHashStream = SaveHashStream,
            PreserveHardLinks = PreserveHardLinks,
            WipeSource = WipeSource,
            WaitForDevice = WaitForDevice,
            WaitForSpace = WaitForSpace,
            SmbCompression = SmbCompression,
            AllOrNothing = AllOrNothing,
            RescueMode = RescueMode,
            DeltaCopy = DeltaCopy,
            SkipDuplicates = SkipDuplicates,
            Encrypt = Encrypt,
            OrganizePattern = string.IsNullOrWhiteSpace(OrganizePattern) ? CopyOptions.DefaultOrganizePattern : OrganizePattern,
            PeerCode = PeerCode ?? "",
        };
        if (ChunkSize > 0 && BlockSize > 0)
        {
            options.ChunkSize = ChunkSize;
            options.BlockSize = BlockSize;
        }

        if (QueueDepth > 0)
        {
            options.ReadQueueDepth = options.WriteQueueDepth = QueueDepth;
        }

        return options;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CopyJobRecord))]
internal sealed partial class JobJsonContext : JsonSerializerContext;

/// <summary>Trabajos pendientes en %LOCALAPPDATA%\MegaCopier\jobs. Se escriben de forma atómica.</summary>
public sealed class JobStore
{
    public JobStore(string? directory = null)
    {
        Directory = directory ?? System.IO.Path.Combine(
            DataFolder.Path, "jobs");
    }

    public string Directory { get; }

    public void Save(CopyJobRecord job)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathFor(job.Id);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, job, JobJsonContext.Default.CopyJobRecord);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
    }

    public CopyJobRecord? Get(string id)
    {
        string path = PathFor(id);
        return File.Exists(path) ? Read(path) : null;
    }

    public IReadOnlyList<CopyJobRecord> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return System.IO.Directory.EnumerateFiles(Directory, "*.json")
            .Select(Read)
            .OfType<CopyJobRecord>()
            .OrderBy(j => j.CreatedUtc)
            .ToList();
    }

    public void Delete(string id)
    {
        string path = PathFor(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string id) => System.IO.Path.Combine(Directory, id + ".json");

    private static CopyJobRecord? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, JobJsonContext.Default.CopyJobRecord);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
