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
