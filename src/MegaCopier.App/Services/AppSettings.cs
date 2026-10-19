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

/// <summary>Qué pasa con una copia nueva que va al mismo disco que otra que ya está en marcha.</summary>
public enum SameDriveCopy
{
    /// <summary>MegaCopier sale al frente y pregunta si sumarla a la copia en marcha o dejarla aparte.</summary>
    Ask,

    /// <summary>Sus archivos se añaden al final de la lista de la copia en marcha.</summary>
    Merge,

    /// <summary>Copia aparte que espera a que termine la otra (dos copias al mismo disco a la vez van más lentas).</summary>
    Queue,

    /// <summary>Copia aparte que empieza ya, a la vez que la otra.</summary>
    Parallel,
}

/// <summary>Qué pasa con una copia nueva que va a un disco distinto del de las copias en marcha.</summary>
public enum OtherDriveCopy
{
    /// <summary>Empieza ya: cada disco recibe a su velocidad y ninguna copia frena a la otra.</summary>
    Parallel,

    /// <summary>Espera su turno con las reglas de la cola (discos compartidos y máximo de copias a la vez).</summary>
    Queue,
}

/// <summary>Cuándo preguntar antes de copiar o mover un arrastre del Explorador.</summary>
public enum DragConfirm
{
    Never,

    /// <summary>Solo al mover: un arrastre sin querer no se lleva los archivos de su sitio.</summary>
    Moves,
    Always,
}

/// <summary>Qué hace la X de la ventana.</summary>
public enum CloseButtonAction
{
    /// <summary>Cancela todas las copias (como «Detener» en cada una) y cierra la ventana; MegaCopier sigue en la bandeja.</summary>
    CancelCopies,

    /// <summary>Detiene las copias conservando lo copiado (se ofrecen continuar) y cierra la ventana.</summary>
    StopCopies,

    /// <summary>Solo oculta la ventana: las copias siguen en segundo plano, con el icono de la bandeja.</summary>
    HideToTray,

    /// <summary>Sale de MegaCopier del todo (las copias se detienen y se ofrecen continuar al volver).</summary>
    Exit,
}

/// <summary>Qué hace el botón de minimizar de la ventana.</summary>
public enum MinimizeButtonAction
{
    /// <summary>Como cualquier ventana: a la barra de tareas.</summary>
    Taskbar,

    /// <summary>Se oculta en la bandeja de iconos (junto al reloj).</summary>
    Tray,
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

    /// <summary>Conjunto de iconos, independiente del tema (fluent, color, mosaico, suave, degradado, neon, windows10).</summary>
    public string IconPack { get; set; } = "fluent";

    /// <summary>Aspecto de la barra del total, de la del archivo y de las mini-barras de la lista de copias.</summary>
    public Theming.BarSettings TotalBar { get; set; } = new();

    public Theming.BarSettings FileBar { get; set; } = new();

    public Theming.BarSettings MiniBar { get; set; } = new();

    /// <summary>Barras sin animaciones continuas (también si Windows tiene desactivados los efectos de animación).</summary>
    public bool ReduceBarMotion { get; set; }

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

    /// <summary>Conexiones a la vez con un servidor (FTP, SFTP, WebDAV, S3).</summary>
    public int RemoteConnections { get; set; } = 8;

    /// <summary>Se añadió la exclusión de Windows Defender (ver DefenderExclusion).</summary>
    public bool DefenderExcluded { get; set; }

    /// <summary>Rutas de dispositivos rápidos separadas por comas ("C:, D:\Datos, \\servidor\recurso").</summary>
    public string FastDevices { get; set; } = "";

    public ProcessPriority Priority { get; set; } = ProcessPriority.Normal;

    public bool CheckFreeSpace { get; set; } = true;

    public bool UseSystemCache { get; set; }

    /// <summary>Pedir compresión SMB al copiar por red.</summary>
    public bool SmbCompression { get; set; }

    public bool HideFinishedFiles { get; set; } = true;

    /// <summary>Columnas de sumas (hash) en la lista de archivos.</summary>
    public bool FilesShowHashes { get; set; }

    /// <summary>Anchos elegidos para las columnas Origen y Tamaño de la lista de archivos (0 = los de siempre).</summary>
    public double FilesSourceWidth { get; set; }

    public double FilesSizeWidth { get; set; }

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

    public int VersionsDaily { get; set; }

    public int VersionsWeekly { get; set; }

    public int VersionsMonthly { get; set; }

    public FolderConflictPolicy FolderConflict { get; set; } = FolderConflictPolicy.Ask;

    /// <summary>Versión de los ajustes guardados, para cambiar valores por defecto en las actualizaciones.</summary>
    public int SettingsVersion { get; set; }

    /// <summary>Ajustes guardados por una versión anterior: se ponen al día los valores por defecto que cambiaron.</summary>
    private void Upgrade()
    {
        if (SettingsVersion < 2 && FolderConflict == FolderConflictPolicy.Merge)
        {
            FolderConflict = FolderConflictPolicy.Ask; // 1.2: preguntar qué hacer cuando la carpeta ya existe
        }

        if (SettingsVersion < 3)
        {
            InterceptExplorerDrag = true; // 1.3: arrastrar entre ventanas del Explorador también copia con MegaCopier
        }

        SettingsVersion = 3;
    }

    public double TimeToleranceSeconds { get; set; } = 2;

    public bool VerifySource { get; set; }

    public bool SaveHashStream { get; set; }

    public bool PreserveHardLinks { get; set; }

    public bool WipeSource { get; set; }

    public WipeMethod WipeMethod { get; set; }

    public bool EfsRaw { get; set; }

    public bool RansomwareShield { get; set; } = true;

    public int RecoveryPercent { get; set; }

    public bool Forensic { get; set; }

    public List<string> AlternateSources { get; set; } = [];

    public bool WaitForDevice { get; set; } = true;

    public bool WaitForSpace { get; set; } = true;

    public bool AllOrNothing { get; set; }

    public bool RescueMode { get; set; }

    public bool DeltaCopy { get; set; }

    public bool SkipDuplicates { get; set; }

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
            VersionsFolder = VersionsFolder, VersionsDaily = VersionsDaily, VersionsWeekly = VersionsWeekly, VersionsMonthly = VersionsMonthly, FolderConflict = FolderConflict, TimeToleranceSeconds = TimeToleranceSeconds, VerifySource = VerifySource,
            SaveHashStream = SaveHashStream, PreserveHardLinks = PreserveHardLinks, WipeSource = WipeSource, WipeMethod = WipeMethod, EfsRaw = EfsRaw, RansomwareShield = RansomwareShield, RecoveryPercent = RecoveryPercent, Forensic = Forensic, AlternateSources = [.. AlternateSources], WaitForDevice = WaitForDevice, WaitForSpace = WaitForSpace,
            AllOrNothing = AllOrNothing,
            RescueMode = RescueMode,
            DeltaCopy = DeltaCopy,
            SkipDuplicates = SkipDuplicates,
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
            VersionsDaily = value.VersionsDaily;
            VersionsWeekly = value.VersionsWeekly;
            VersionsMonthly = value.VersionsMonthly;
            FolderConflict = value.FolderConflict;
            TimeToleranceSeconds = value.TimeToleranceSeconds;
            VerifySource = value.VerifySource;
            SaveHashStream = value.SaveHashStream;
            PreserveHardLinks = value.PreserveHardLinks;
            WipeSource = value.WipeSource;
            WipeMethod = value.WipeMethod;
            EfsRaw = value.EfsRaw;
            RansomwareShield = value.RansomwareShield;
            RecoveryPercent = value.RecoveryPercent;
            Forensic = value.Forensic;
            AlternateSources = [.. value.AlternateSources];
            WaitForDevice = value.WaitForDevice;
            WaitForSpace = value.WaitForSpace;
            AllOrNothing = value.AllOrNothing;
            RescueMode = value.RescueMode;
            DeltaCopy = value.DeltaCopy;
            SkipDuplicates = value.SkipDuplicates;
        }
    }

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>"system" o un color "#RRGGBB".</summary>
    public string Accent { get; set; } = "system";

    public AppBackdrop Backdrop { get; set; } = AppBackdrop.Mica;

    public bool AlwaysOnTop { get; set; }

    /// <summary>"" = el de Windows; si no, un código de Texts.Languages ("fr-FR"...). Se aplica al reiniciar la app.</summary>
    public string Language { get; set; } = "";

    public bool VerifyAfterCopy { get; set; }

    /// <summary>Por defecto pregunta, como el Explorador.</summary>
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;

    /// <summary>Límite de velocidad de las copias nuevas en MB/s (0 = sin límite).</summary>
    public int SpeedLimitMBps { get; set; }

    /// <summary>Mientras usas el teclado o el ratón, las copias no pasan de esta velocidad (0 = sin límite).</summary>
    public int SlowWhenActiveMBps { get; set; }

    /// <summary>Usar los valores medidos para cada disco (tamaño de bloque y operaciones a la vez).</summary>
    public bool AutoTune { get; set; } = true;

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

    /// <summary>Avisar por correo al terminar cada copia (la contraseña va al Administrador de credenciales).</summary>
    public bool EmailEnabled { get; set; }

    public string EmailServer { get; set; } = "";

    /// <summary>0 = el habitual del cifrado elegido (587, 465 o 25).</summary>
    public int EmailPort { get; set; }

    public Engine.Network.SmtpSecurity EmailSecurity { get; set; }

    public string EmailUser { get; set; } = "";

    public string EmailFrom { get; set; } = "";

    /// <summary>Destinatarios separados por comas o puntos y comas.</summary>
    public string EmailTo { get; set; } = "";

    /// <summary>Solo cuando una copia termina con errores o falla.</summary>
    public bool EmailOnlyProblems { get; set; }

    /// <summary>Cómo enviar, con la contraseña guardada.</summary>
    public Engine.Network.SmtpSettings SmtpSettings() => new(
        EmailServer.Trim(), EmailPort, EmailSecurity, EmailUser.Trim(), Engine.Remote.Connections.GetSecret(SmtpSecretId),
        EmailFrom.Trim(), [.. EmailTo.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);

    /// <summary>Nombre de la contraseña del correo en el Administrador de credenciales ("MegaCopier:smtp").</summary>
    public const string SmtpSecretId = "smtp";

    /// <summary>Carpetas vigiladas por el guardián de integridad.</summary>
    public List<GuardedFolder> GuardedFolders { get; set; } = [];

    /// <summary>Carpetas sincronizadas al momento (Ajustes > Sincronización en tiempo real).</summary>
    public List<LiveSyncPair> LiveSyncs { get; set; } = [];

    /// <summary>Cada cuántos días se comprueba cada carpeta vigilada.</summary>
    public int GuardIntervalDays { get; set; } = 7;

    /// <summary>Recibir archivos de otros equipos con MegaCopier.</summary>
    public bool ReceiveEnabled { get; set; }

    /// <summary>Dónde se guardan (vacío = DescargasMegaCopier).</summary>
    public string ReceiveFolder { get; set; } = "";

    /// <summary>Código que hay que dar a quien envía.</summary>
    public string ReceiveCode { get; set; } = "";

    public int ReceivePort { get; set; } = Engine.Network.PeerProtocol.DefaultPort;

    /// <summary>Recibir lo que se envíe desde un móvil con LocalSend (en la misma carpeta).</summary>
    /// <summary>Control desde el móvil (página web en la red local, con código).</summary>
    public bool RemoteControlEnabled { get; set; }

    public string RemoteControlCode { get; set; } = "";

    public bool LocalSendEnabled { get; set; }

    /// <summary>Aceptar los envíos de LocalSend sin preguntar (solo en redes de confianza).</summary>
    public bool LocalSendAutoAccept { get; set; }

    /// <summary>Identificador estable de este PC para LocalSend (se crea la primera vez).</summary>
    public string LocalSendFingerprint { get; set; } = "";

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
    /// Al arrastrar archivos con el botón izquierdo entre ventanas del Explorador, copiar con MegaCopier (como Ctrl+V).
    /// </summary>
    public bool InterceptExplorerDrag { get; set; } = true;

    /// <summary>
    /// Si al terminar quedaron archivos que Windows no deja leer o escribir (juegos protegidos, carpetas de otros
    /// usuarios...), pedir permiso de administrador y copiarlos sin preguntar más, como hace el Explorador.
    /// </summary>
    public bool AutoAdminCopy { get; set; } = true;

    public DragConfirm DragConfirm { get; set; } = DragConfirm.Moves;

    /// <summary>
    /// Interruptor general (doble clic en el icono de la bandeja). Desactivado, MegaCopier no intercepta nada
    /// y copia el Explorador de Windows como siempre.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // --- Comportamiento ---

    /// <summary>Al mover entre discos: copiar también los atributos.</summary>
    public bool MovePreserveAttributes { get; set; } = true;

    /// <summary>Al mover entre discos: permisos, propietario y auditoría que se conservan.</summary>
    public Engine.Native.SecurityParts MoveCopySecurity { get; set; }

    /// <summary>Copia nueva al mismo disco de destino que otra en marcha.</summary>
    public SameDriveCopy SameDriveCopies { get; set; } = SameDriveCopy.Ask;

    /// <summary>Copia nueva a otro disco mientras hay copias en marcha.</summary>
    public OtherDriveCopy OtherDriveCopies { get; set; } = OtherDriveCopy.Parallel;

    /// <summary>Al detener una copia (o si falla), borrar lo que quedó a medias: ya no se podrá continuar.</summary>
    public bool DeleteIncomplete { get; set; }

    /// <summary>Con DeleteIncomplete, no borrar lo que quedó a medias si la copia paró por un error.</summary>
    public bool KeepIncompleteOnError { get; set; } = true;

    /// <summary>Al terminar, expulsar la unidad de destino si es extraíble (USB, tarjeta).</summary>
    public bool EjectWhenDone { get; set; }

    /// <summary>Expulsarla aunque algún programa la esté usando.</summary>
    public bool EjectForce { get; set; }

    // --- Errores ---

    /// <summary>Guardar un informe con los archivos que fallaron al terminar una copia con errores.</summary>
    public bool SaveErrorReport { get; set; }

    /// <summary>El informe va a la carpeta de destino de la copia (si no, a ErrorReportFolder).</summary>
    public bool ErrorReportInDestination { get; set; } = true;

    public string ErrorReportFolder { get; set; } = "";

    public string ErrorReportName { get; set; } = "errorlog.txt";

    // --- Interfaz ---

    /// <summary>Mostrar el icono de MegaCopier en la bandeja del sistema.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Un icono más en la bandeja por cada copia en marcha, con su porcentaje y una barra que sube.</summary>
    public bool TrayProgressIcons { get; set; } = true;

    /// <summary>La X de la ventana.</summary>
    public CloseButtonAction CloseButton { get; set; } = CloseButtonAction.CancelCopies;

    /// <summary>El botón de minimizar de la ventana.</summary>
    public MinimizeButtonAction MinimizeButton { get; set; } = MinimizeButtonAction.Taskbar;

    /// <summary>En pantallas pequeñas (1366 × 768) la ventana se dibuja algo más pequeña, en proporción, para caber entera.</summary>
    public bool AdaptSmallScreens { get; set; } = true;

    /// <summary>Las copias nuevas empiezan con la ventana minimizada.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>Recordar dónde estaba la ventana y abrirla en el mismo sitio.</summary>
    public bool RememberPosition { get; set; }

    /// <summary>Recordar el tamaño de la ventana completa.</summary>
    public bool RememberSize { get; set; }

    /// <summary>Posición y tamaño guardados (en píxeles de pantalla).</summary>
    public int WindowX { get; set; } = int.MinValue;

    public int WindowY { get; set; } = int.MinValue;

    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    /// <summary>Ventana translúcida: se ve el escritorio a través del fondo, sea cual sea el tema.</summary>
    public bool Translucent { get; set; }

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
            RemoteConnections = Math.Clamp(RemoteConnections, 1, CopyOptions.MaxRemoteConnections),
            Filter = Filter,
            Links = Links,
            KeepBothPattern = KeepBothPattern,
            RetryCount = Math.Clamp(RetryCount, 0, 100),
            RetryDelay = TimeSpan.FromSeconds(Math.Clamp(RetryDelaySeconds, 0, 3600)),
            OnError = OnError,
            Mode = request.Mode ?? CopyMode.Copy,
            SyncDirection = request.SyncDirection ?? SyncDirection.TwoWay,
            DryRun = request.DryRun ?? false,
            VersionsFolder = VersionsFolder.Trim(),
            VersionsDaily = VersionsDaily,
            VersionsWeekly = VersionsWeekly,
            VersionsMonthly = VersionsMonthly,
            FolderConflict = FolderConflict,
            TimeTolerance = TimeSpan.FromSeconds(Math.Clamp(TimeToleranceSeconds, 0, 3600)),
            VerifySource = VerifySource,
            SaveHashStream = SaveHashStream,
            PreserveHardLinks = PreserveHardLinks,
            WipeSource = WipeSource,
            WipeMethod = WipeMethod,
            EfsRaw = EfsRaw,
            RansomwareShield = RansomwareShield,
            RecoveryPercent = RecoveryPercent,
            Forensic = Forensic,
            AlternateSources = [.. AlternateSources],
            WaitForDevice = WaitForDevice,
            WaitForSpace = WaitForSpace,
            AllOrNothing = AllOrNothing,
            RescueMode = RescueMode,
            DeltaCopy = DeltaCopy,
            SkipDuplicates = SkipDuplicates,
        };

        // Al mover, sus propias preferencias de atributos y seguridad.
        if (options.Move)
        {
            options.PreserveAttributes = MovePreserveAttributes;
            options.CopySecurity = MoveCopySecurity;
        }

        // Autoajuste: si se midió el disco de destino y no se cambiaron a mano los valores de rendimiento, se usan los medidos.
        int blockMB = BlockSizeMB;
        if (AutoTune && BlockSizeMB == 4 && QueueDepth == 4 && request.Destination is { Length: > 0 } target
            && request.Operation is null or JobOperation.Copy && DeviceTuner.ProfileFor(target) is { } profile)
        {
            blockMB = profile.BlockSizeMB;
            options.ReadQueueDepth = options.WriteQueueDepth = Math.Clamp(profile.QueueDepth, 1, 16);
        }

        int block = Math.Clamp(blockMB, 1, 16) * CopyOptions.MiB;
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
                var settings = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
                settings.Upgrade();
                return settings;
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

    /// <summary>Tras «Restablecer los ajustes»: estos ya no se guardan (la app se reinicia con los de fábrica).</summary>
    [JsonIgnore]
    public bool Frozen { get; set; }

    public void Save()
    {
        if (Frozen)
        {
            return;
        }

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
