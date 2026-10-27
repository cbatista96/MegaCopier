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

    /// <summary>Enviar a otro equipo con MegaCopier por la red (destino "equipo:puerto", con <see cref="CopyOptions.PeerCode"/>).</summary>
    Send,

    /// <summary>Descifrar archivos .mcenc (con <see cref="CopyOptions.EncryptionPassword"/>) en el destino.</summary>
    Decrypt,

    /// <summary>Comparar y sincronizar el contenido de dos carpetas (Sources[0] y Destination), en uno o dos sentidos.</summary>
    Sync,

    /// <summary>Reparar con los datos de recuperación (ver <see cref="CopyOptions.RecoveryPercent"/>).</summary>
    Repair,

    /// <summary>Copia de seguridad sin duplicados: los orígenes como instantánea nueva en el repositorio del destino.</summary>
    Snapshot,

    /// <summary>Restaurar una instantánea (<see cref="CopyOptions.SnapshotId"/>) del repositorio Sources[0] en el destino.</summary>
    Restore,
}

/// <summary>Hacia dónde se sincroniza (con <see cref="JobOperation.Sync"/>).</summary>
public enum SyncDirection
{
    /// <summary>En los dos sentidos: lo cambiado en cualquier lado pasa al otro (se recuerda la última vez).</summary>
    TwoWay,

    /// <summary>La derecha queda igual que la izquierda (espejo).</summary>
    LeftToRight,

    /// <summary>La izquierda queda igual que la derecha (espejo).</summary>
    RightToLeft,
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

    /// <summary>Organizar fotos y vídeos en carpetas por fecha (añoño-mes) según el EXIF o la fecha de grabación.</summary>
    OrganizeByDate,
}

/// <summary>Qué hacer si una carpeta de primer nivel ya existe en el destino.</summary>
public enum FolderConflictPolicy
{
    /// <summary>Mezclar: copiar dentro (cada archivo sigue su regla de conflicto).</summary>
    Merge,

    /// <summary>Omitir esa carpeta entera.</summary>
    Skip,

    /// <summary>Preguntar al usuario qué hacer (pasar los que ya están, sobrescribir, renombrar la carpeta o cancelar).</summary>
    Ask,
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

    /// <summary>Tras agotar los reintentos, se vuelve a intentar una vez al final de la cola.</summary>
    MoveToEnd,

    /// <summary>Tras agotar los reintentos, pregunta qué hacer: reintentar, omitir, omitir todos o detener.</summary>
    Ask,
}

/// <summary>Un archivo que sigue fallando tras los reintentos (para preguntar qué hacer).</summary>
public sealed record ErrorInfo(string Source, string Destination, string Message, FileErrorKind Kind);

public enum ErrorDecision
{
    Retry,
    Skip,

    /// <summary>Omitir este y todos los errores que vengan en esta copia sin volver a preguntar.</summary>
    SkipAll,
    Stop,
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

    /// <summary>
    /// Al calcular o comprobar sumas, leer los archivos pequeños en lote con IoRing (Windows 11): muchas lecturas en una
    /// sola petición al sistema. Si el sistema no lo tiene, se leen de uno en uno como siempre.
    /// </summary>
    public bool BatchedReads { get; set; } = true;

    /// <summary>Vacía al disco también los archivos pequeños antes de renombrarlos (evita archivos "completos" con ceros tras un apagón).</summary>
    public bool FlushSmallFiles { get; set; } = true;

    /// <summary>En ReFS y Dev Drive, dentro del mismo volumen, clonar bloques en lugar de copiarlos (instantáneo).</summary>
    public bool BlockClone { get; set; } = true;

    /// <summary>Carpetas "todo o nada": se copian ocultas y solo aparecen con su nombre cuando todo salió bien.</summary>
    public bool AllOrNothing { get; set; }

    /// <summary>
    /// Copia diferencial: al reemplazar un archivo grande, escribir solo los bloques que cambiaron (con registro para
    /// deshacer). Ideal para actualizar discos virtuales o archivos de correo en un disco lento.
    /// </summary>
    public bool DeltaCopy { get; set; }

    /// <summary>Al enviar a otro equipo: el código que muestra su MegaCopier.</summary>
    public string PeerCode { get; set; } = "";

    /// <summary>No copiar lo que ya está en el destino con otro nombre o en otra carpeta (mismo contenido).</summary>
    public bool SkipDuplicates { get; set; }

    /// <summary>
    /// Copia cifrada (AES-256-GCM, archivos .mcenc) con esta contraseña; vacío = sin cifrar. Nunca se guarda en disco: si
    /// la copia se interrumpe, hay que volver a escribirla.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EncryptionPassword { get; set; } = "";

    /// <summary>Esta copia va cifrada (se guarda en el trabajo; la contraseña no): sin contraseña, no se copia nada.</summary>
    public bool Encrypt { get; set; }

    /// <summary>Sal de este trabajo (la clave se deriva una vez para todos sus archivos).</summary>
    internal byte[] EncryptionSalt { get; } = EncryptedFile.NewSalt();

    /// <summary>Carpetas al organizar por fecha: {yyyy}, {MM}, {dd} (por defecto "{yyyy}\{yyyy}-{MM}").</summary>
    public string OrganizePattern { get; set; } = DefaultOrganizePattern;

    public const string DefaultOrganizePattern = @"{yyyy}\{yyyy}-{MM}";

    /// <summary>
    /// Modo rescate (como ddrescue): ante sectores dañados del origen se lee sector a sector, se reintenta y lo
    /// ilegible se rellena con ceros, en vez de abandonar el archivo. Se anota exactamente qué se perdió.
    /// </summary>
    public bool RescueMode { get; set; }

    /// <summary>
    /// Modo copia de seguridad (robocopy /B): leer y escribir saltándose los permisos con los privilegios de copia y
    /// restauración. Solo en un proceso con permisos de administrador (el motor los activa al empezar).
    /// </summary>
    public bool BackupMode { get; set; }

    /// <summary>
    /// Los archivos cifrados con EFS se copian sin descifrar (como robocopy /EFSRAW): el destino queda cifrado igual y no
    /// hace falta tener la clave. Sin esta opción se copian descifrados si se puede abrir su contenido.
    /// </summary>
    public bool EfsRaw { get; set; }

    /// <summary>Parar si muchos de los archivos que van a reemplazar a otros parecen recién cifrados (ransomware).</summary>
    public bool RansomwareShield { get; set; } = true;

    /// <summary>Datos de recuperación (paridad Reed-Solomon) para reparar la copia si el disco la estropea: % del tamaño, 0 = no.</summary>
    public int RecoveryPercent { get; set; }

    /// <summary>Modo forense: SHA-256 de todo y, al terminar, un manifiesto firmado en el destino (ver ForensicManifest).</summary>
    public bool Forensic { get; set; }

    /// <summary>Con <see cref="JobOperation.Restore"/>: qué instantánea (vacío = la última).</summary>
    public string SnapshotId { get; set; } = "";

    /// <summary>
    /// Copiar a un archivo ZIP en lugar de a una carpeta (el destino puede ser la carpeta donde dejarlo o el propio
    /// .zip). Con <see cref="Encrypt"/>, el ZIP va cifrado con AES-256 (lo abren 7-Zip, WinZip y WinRAR).
    /// </summary>
    public bool Zip { get; set; }

    /// <summary>ZIP o 7z (si el destino termina en .zip o .7z, manda la extensión).</summary>
    public IO.ArchiveFormat ArchiveFormat { get; set; }

    /// <summary>Archivos dispersos (con huecos): el destino también lo es, sin escribir los huecos.</summary>
    public bool PreserveSparse { get; set; } = true;

    /// <summary>Origen y destino en el mismo servidor de red: que copie el servidor (los datos no viajan por la red).</summary>
    /// <summary>
    /// Archivos pequeños (de un solo bloque) que se copian a la vez, como robocopy /MT: con miles de archivos pequeños
    /// es varias veces más rápido. 1 = de uno en uno. Con un límite de velocidad, siempre de uno en uno.
    /// </summary>
    public int ParallelSmallFiles { get; set; } = 4;

    public const int MaxRemoteConnections = 255;

    /// <summary>Conexiones a la vez con un servidor (FTP, SFTP, WebDAV, S3), de 1 a 255. Un móvil va siempre de uno en uno.</summary>
    public int RemoteConnections { get; set; } = 8;

    /// <summary>Ajustar las conexiones sobre la marcha según la velocidad (<see cref="RemoteConnections"/> es entonces el máximo).</summary>
    public bool RemoteConnectionsAuto { get; set; } = true;

    /// <summary>
    /// Otros sitios con el mismo contenido que el origen (otro NAS, la carpeta compartida de otro equipo...): los archivos
    /// grandes se leen de todos a la vez. Cada uno es el equivalente de la carpeta (o archivo) de origen.
    /// </summary>
    public List<string> AlternateSources { get; set; } = [];

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

    /// <summary>Con <see cref="JobOperation.Sync"/>: en dos sentidos o como espejo hacia un lado.</summary>
    public SyncDirection SyncDirection { get; set; }

    /// <summary>Carpeta de versiones ("" = no): lo que se va a reemplazar o borrar se guarda ahí con fecha. Relativa al destino o absoluta.</summary>
    public string VersionsFolder { get; set; } = "";

    /// <summary>Limpieza de la carpeta de versiones: cuántos días, semanas y meses guardar (todo 0 = guardar todas).</summary>
    public int VersionsDaily { get; set; }

    public int VersionsWeekly { get; set; }

    public int VersionsMonthly { get; set; }

    public FolderConflictPolicy FolderConflict { get; set; }

    /// <summary>Ctrl+arrastrar a la carpeta de origen: duplicar con nombres nuevos.</summary>
    public bool DuplicateInPlace { get; set; }

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

    /// <summary>Con qué se sobrescribe en el borrado seguro (y al mover con <see cref="WipeSource"/>).</summary>
    public WipeMethod WipeMethod { get; set; }

    public LinkHandling Links { get; set; } = LinkHandling.CopyLink;

    /// <summary>Nombre de "conservar ambos": {name}, {n}, {ext} y {date} (aaaa-mm-dd).</summary>
    public string KeepBothPattern { get; set; } = DefaultKeepBothPattern;

    /// <summary>Opciones menos habituales (bit Archivo, cambio de hora, aplanar, máscara de nombres…).</summary>
    public CopyExtras Extras { get; set; } = new();

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

/// <summary>Cómo sobrescribe el borrado seguro el contenido antes de borrarlo.</summary>
public enum WipeMethod
{
    /// <summary>Una pasada con ceros: suficiente para los discos actuales.</summary>
    Zeros,

    /// <summary>Una pasada con datos aleatorios.</summary>
    Random,

    /// <summary>DoD 5220.22-M, 3 pasadas: ceros, unos y aleatorio.</summary>
    Dod3,

    /// <summary>DoD 5220.22-M ECE, 7 pasadas: ceros, unos, aleatorio, aleatorio, ceros, unos y aleatorio.</summary>
    Dod7,
}
