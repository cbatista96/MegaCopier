namespace MegaCopier.Engine;

public enum ConflictPolicy
{
    /// <summary>Reemplaza el destino (los archivos idénticos se omiten siempre, salvo <see cref="CopyOptions.ForceRecopy"/>).</summary>
    Overwrite,

    /// <summary>Nunca toca un archivo que ya existe.</summary>
    Skip,

    /// <summary>Reemplaza solo si el origen es más reciente.</summary>
    OverwriteIfNewer,

    /// <summary>Conserva ambos: copia como "nombre (2).ext".</summary>
    KeepBoth,

    /// <summary>Marca el archivo como error.</summary>
    Fail,

    /// <summary>Pregunta al usuario (con "hacer lo mismo con todos"). Sin quien responda, omite.</summary>
    Ask,

    /// <summary>Reemplaza solo si el tamaño es distinto.</summary>
    OverwriteIfDifferentSize,

    /// <summary>Renombra el archivo que ya estaba ("nombre (2).ext") y copia con el nombre original.</summary>
    RenameExisting,
}

/// <summary>Qué hace el trabajo con sus orígenes.</summary>
public enum JobOperation
{
    /// <summary>Copiar (o mover, con <see cref="CopyOptions.Move"/>).</summary>
    Copy,

    /// <summary>Comprobar archivos de sumas (.sha256, .md5, .sfv...) contra los archivos que describen.</summary>
    Test,

    /// <summary>Borrar los orígenes (con <see cref="CopyOptions.SecureDelete"/>, sobrescribiéndolos antes).</summary>
    Delete,

    /// <summary>Crear un archivo de sumas de los orígenes, sin copiar.</summary>
    Hash,
}

/// <summary>Qué copia el trabajo.</summary>
public enum CopyMode
{
    /// <summary>Copiar (lo idéntico se omite).</summary>
    Copy,

    /// <summary>Sincronizar: copiar y además borrar del destino lo que ya no está en el origen (espejo).</summary>
    Mirror,

    /// <summary>Solo actualizar los archivos que ya existen en el destino (no añade nuevos).</summary>
    UpdateExisting,

    /// <summary>Solo crear la estructura de carpetas, sin archivos.</summary>
    FoldersOnly,
}

/// <summary>Qué hacer si una carpeta de primer nivel ya existe en el destino.</summary>
public enum FolderConflictPolicy
{
    /// <summary>Mezclar: copiar dentro (cada archivo sigue su regla de conflicto).</summary>
    Merge,

    /// <summary>Omitir esa carpeta entera.</summary>
    Skip,
}

/// <summary>Qué hacer con los enlaces simbólicos y las uniones de carpetas.</summary>
public enum LinkHandling
{
    /// <summary>Crear en el destino un enlace igual (apunta al mismo sitio).</summary>
    CopyLink,

    /// <summary>Copiar el contenido al que apunta el enlace.</summary>
    FollowTarget,

    Skip,
}

public enum ErrorPolicy
{
    /// <summary>Tras agotar los reintentos, anota el error y sigue con el siguiente archivo.</summary>
    SkipAndContinue,

    /// <summary>Tras agotar los reintentos, detiene todo el trabajo.</summary>
    Stop,

    /// <summary>Tras agotar los reintentos, se vuelve a intentar una vez al final de la cola (como Ultracopier).</summary>
    MoveToEnd,
}

public sealed class CopyOptions
{
    public const int MiB = 1024 * 1024;

    /// <summary>
    /// Cada cuántos bytes se vacía al disco y se confirma en el journal. Es lo máximo que se pierde tras un apagón.
    /// </summary>
    public long ChunkSize { get; set; } = 64L * MiB;

    /// <summary>Tamaño de cada lectura/escritura. Debe ser múltiplo de 4096 y dividir a <see cref="ChunkSize"/>.</summary>
    public int BlockSize { get; set; } = 4 * MiB;

    /// <summary>Lecturas simultáneas en vuelo. Un SSD NVMe necesita varias peticiones a la vez para dar todo su rendimiento.</summary>
    public int ReadQueueDepth { get; set; } = 4;

    /// <summary>Escrituras simultáneas en vuelo.</summary>
    public int WriteQueueDepth { get; set; } = 4;

    /// <summary>Archivos de este tamaño o mayores usan journal y se pueden reanudar desde el último bloque.</summary>
    public long ResumableThreshold { get; set; } = 32L * MiB;

    /// <summary>Al reanudar, cuántos de los últimos bloques confirmados se releen del destino para comprobar su hash.</summary>
    public int ResumeVerifyChunks { get; set; } = 2;

    /// <summary>Relee todo el destino (sin caché) al terminar, compara hashes y repara los bloques dañados.</summary>
    public bool Verify { get; set; }

    /// <summary>Usa E/S sin caché en discos locales: más rápida en archivos grandes y verificación honesta.</summary>
    public bool UseUnbufferedIo { get; set; } = true;

    /// <summary>Vacía al disco también los archivos pequeños antes de renombrarlos (evita archivos "completos" con ceros tras un apagón).</summary>
    public bool FlushSmallFiles { get; set; } = true;

    /// <summary>En ReFS y Dev Drive, dentro del mismo volumen, clonar bloques en lugar de copiarlos (instantáneo).</summary>
    public bool BlockClone { get; set; } = true;

    /// <summary>Origen y destino en el mismo servidor de red: que copie el servidor (los datos no viajan por la red).</summary>
    public bool ServerSideCopy { get; set; } = true;

    /// <summary>Pedir compresión SMB al copiar por red (Windows 11 / Server 2022): ayuda en redes lentas.</summary>
    public bool SmbCompression { get; set; }

    /// <summary>Pruebas: usar siempre la copia del sistema (CopyFile2).</summary>
    internal bool ForceSystemCopy { get; set; }

    public bool PreserveTimestamps { get; set; } = true;

    public bool PreserveAttributes { get; set; } = true;

    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Overwrite;

    /// <summary>
    /// Mover en lugar de copiar. En el mismo volumen es un renombrado instantáneo; entre volúmenes se copia (reanudable)
    /// y el original se borra solo cuando su copia está completa.
    /// </summary>
    public bool Move { get; set; }

    /// <summary>Límite de velocidad de escritura en bytes por segundo (0 = sin límite).</summary>
    public long MaxBytesPerSecond { get; set; }

    /// <summary>Hash completo de cada archivo (lista, verificación y archivo de sumas). xxHash3 es casi gratis.</summary>
    public FileHashAlgorithm HashAlgorithm { get; set; } = FileHashAlgorithm.XxHash3;

    /// <summary>Copiar los streams alternativos (Zone.Identifier y otros), como hace el Explorador.</summary>
    public bool CopyStreams { get; set; } = true;

    /// <summary>Copiar permisos, propietario y/o auditoría (Native.SecurityParts).</summary>
    public Native.SecurityParts CopySecurity { get; set; }

    /// <summary>Al terminar, guardar un archivo de sumas (formato estándar) en la carpeta de destino.</summary>
    public bool SaveChecksumFile { get; set; }

    /// <summary>Comparar también por hash los archivos omitidos por idénticos.</summary>
    public bool VerifySkipped { get; set; }

    /// <summary>Desatendido: nunca pregunta (los conflictos se omiten). Se puede cambiar durante la copia.</summary>
    public bool Unattended { get; set; }

    /// <summary>Copia incluso si el destino parece idéntico (mismo tamaño y fecha).</summary>
    public bool ForceRecopy { get; set; }

    public ErrorPolicy OnError { get; set; } = ErrorPolicy.SkipAndContinue;

    public int RetryCount { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public bool CheckFreeSpace { get; set; } = true;

    /// <summary>Filtros de inclusión y exclusión (vacío = todo).</summary>
    public FileFilter Filter { get; set; } = new();

    public JobOperation Operation { get; set; }

    public CopyMode Mode { get; set; }

    /// <summary>Simular: calcula y muestra qué se haría (copiar, reemplazar, omitir, borrar) sin tocar nada.</summary>
    public bool DryRun { get; set; }

    /// <summary>Carpeta de versiones ("" = no): lo que se va a reemplazar o borrar se guarda ahí con fecha. Relativa al destino o absoluta.</summary>
    public string VersionsFolder { get; set; } = "";

    public FolderConflictPolicy FolderConflict { get; set; }

    /// <summary>Diferencia de fecha que se considera igual (FAT/exFAT guardan la hora cada 2 s; algunos NAS redondean).</summary>
    public TimeSpan TimeTolerance { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Verificación completa: además del destino, relee el origen y compara (detecta lecturas erróneas).</summary>
    public bool VerifySource { get; set; }

    /// <summary>Guarda el hash dentro del propio archivo copiado (flujo :MegaCopier.hash) para comprobar su integridad más adelante.</summary>
    public bool SaveHashStream { get; set; }

    /// <summary>Conservar los enlaces duros: los archivos que en el origen son el mismo se enlazan en el destino en vez de copiarse dos veces.</summary>
    public bool PreserveHardLinks { get; set; }

    /// <summary>Al mover entre discos, borrar los originales de forma segura (sobrescritos).</summary>
    public bool WipeSource { get; set; }

    /// <summary>Si se desconecta la red o la unidad, esperar a que vuelva (hasta <see cref="DeviceWaitTimeout"/>) en lugar de fallar.</summary>
    public bool WaitForDevice { get; set; } = true;

    public TimeSpan DeviceWaitTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Si el destino se llena, esperar a que haya espacio en lugar de marcar error.</summary>
    public bool WaitForSpace { get; set; } = true;

    /// <summary>Cada cuánto se comprueba si volvió la unidad o hay espacio.</summary>
    public TimeSpan WaitPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Al borrar: sobrescribir el contenido, cambiar el nombre y entonces borrar (no se puede recuperar).</summary>
    public bool SecureDelete { get; set; }

    public LinkHandling Links { get; set; } = LinkHandling.CopyLink;

    /// <summary>Nombre de "conservar ambos": {name}, {n}, {ext} y {date} (aaaa-mm-dd).</summary>
    public string KeepBothPattern { get; set; } = DefaultKeepBothPattern;

    public const string DefaultKeepBothPattern = "{name} ({n}){ext}";

    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    internal void Validate()
    {
        if (BlockSize <= 0 || BlockSize % 4096 != 0)
        {
            throw new ArgumentException("BlockSize debe ser un múltiplo positivo de 4096.");
        }

        if (ChunkSize < BlockSize || ChunkSize % BlockSize != 0 || ChunkSize > int.MaxValue)
        {
            throw new ArgumentException("ChunkSize debe ser múltiplo de BlockSize y menor de 2 GB.");
        }

        if (ReadQueueDepth < 1 || WriteQueueDepth < 1)
        {
            throw new ArgumentException("ReadQueueDepth y WriteQueueDepth deben ser al menos 1.");
        }

        if (ResumeVerifyChunks < 0 || RetryCount < 0)
        {
            throw new ArgumentException("ResumeVerifyChunks y RetryCount no pueden ser negativos.");
        }
    }
}
