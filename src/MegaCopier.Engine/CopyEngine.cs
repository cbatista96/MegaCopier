using System.Diagnostics;
using System.Text;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Ejecuta un trabajo de copia (o de movimiento) completo sobre una lista viva de archivos (<see cref="Files"/>):
/// la interfaz puede reordenar, quitar o añadir archivos y saltar el actual mientras se copia.
/// Es reanudable sin estado propio: al volver a lanzar el mismo trabajo, los archivos ya terminados se reconocen
/// como idénticos y los grandes a medias continúan desde su journal.
/// </summary>
public sealed partial class CopyEngine
{
    private readonly CopyOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?> _volumeCache = new(StringComparer.OrdinalIgnoreCase);
    private ConflictAnswer? _conflictForAll;
    private CancellationTokenSource? _currentFile;
    private ProgressTracker? _progress;
    private string _destinationRoot = "";

    public CopyEngine(CopyOptions? options = null)
    {
        _options = options ?? new CopyOptions();
        _options.Validate();
        Files.Changed += change =>
        {
            if (change.Kind is TransferListChangeKind.Added or TransferListChangeKind.Removed && _progress is { } progress)
            {
                var (bytes, files) = Files.Totals();
                progress.SetTotals(bytes, files);
            }
        };
    }

    public CopyController Controller { get; } = new();

    /// <summary>Opciones del trabajo. Algunas (verificar, desatendido, límite) se pueden cambiar en caliente.</summary>
    public CopyOptions Options => _options;

    /// <summary>La lista de archivos del trabajo.</summary>
    public TransferList Files { get; } = new();

    public event Action<CopyProgress>? ProgressChanged;

    /// <summary>Se dispara cada vez que un bloque queda confirmado en el disco (ruta destino, bytes confirmados del archivo).</summary>
    public event Action<string, long>? ChunkCommitted;

    /// <summary>Entradas del registro del trabajo (inicio, avisos, errores, resumen).</summary>
    public event Action<LogEntry>? Logged;

    /// <summary>
    /// Pregunta qué hacer cuando un destino ya existe (con <see cref="ConflictPolicy.Ask"/>). Se llama desde el hilo de
    /// copia y puede bloquear hasta que el usuario responda.
    /// </summary>
    public Func<ConflictInfo, CancellationToken, ConflictAnswer>? ConflictResolver { get; set; }

    /// <summary>Velocidad por segundo desde el inicio (para la gráfica).</summary>
    public float[] SpeedHistory => _progress?.SpeedHistory() ?? [];

    /// <summary>Ruta del archivo de sumas guardado al terminar (si se pidió).</summary>
    public string? ChecksumFile { get; private set; }

    internal IFaultInjector? FaultInjector { get; set; }

    /// <summary>Solo pruebas: decide si dos rutas están en el mismo volumen.</summary>
    internal Func<string, string, bool>? SameVolumeOverride { get; set; }

    public Task<CopyJobResult> RunAsync(CopyJobRequest request, CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => Run(request, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    /// <summary>Salta el archivo que se está copiando ahora (lo copiado de él se descarta).</summary>
    public void SkipCurrent()
    {
        lock (_lock)
        {
            _currentFile?.Cancel();
        }

        Controller.Resume();
    }

    /// <summary>Añade más orígenes al trabajo en curso (por ejemplo, arrastrados a la ventana).</summary>
    public int AddSources(IReadOnlyList<string> sources, int? beforeId = null)
    {
        var plan = CopyPlanner.Build(sources, _destinationRoot, _options.Filter, _options.Links);
        foreach (var directory in plan.Directories)
        {
            Directory.CreateDirectory(directory.Destination);
        }

        // Soltados en una posición concreta de la lista: se insertan ahí, en su orden.
        foreach (var file in plan.Files)
        {
            Files.Insert(file.Source, file.Destination, file.Length, file.LastWriteUtc, beforeId);
        }

        Log(LogLevel.Info, $"Añadidos {plan.Files.Count} archivos ({FormatBytes(plan.TotalBytes)}).");
        return plan.Files.Count;
    }

    public CopyJobResult Run(CopyJobRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        switch (_options.Operation)
        {
            case JobOperation.Test:
                return RunTest(request, stopwatch, cancellationToken);
            case JobOperation.Delete:
                return RunDelete(request, stopwatch, cancellationToken);
            case JobOperation.Hash:
                return RunHash(request, stopwatch, cancellationToken);
        }

        _destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        _conflictForAll = null;

        // Al reanudar un movimiento, un origen que ya no existe pero cuyo destino sí, es que ya se movió entero.
        if (_options.Move)
        {
            var pending = request.Sources.Where(s => File.Exists(s) || Directory.Exists(s) || !TargetExists(s, request.Destination)).ToList();
            if (pending.Count == 0)
            {
                return new CopyJobResult { Elapsed = stopwatch.Elapsed };
            }

            request = request with { Sources = pending };
        }

        var plan = CopyPlanner.Build(request.Sources, request.Destination, _options.Filter, _options.Links);
        foreach (string warning in plan.Warnings)
        {
            Log(LogLevel.Warning, warning);
        }

        var excluded = new HashSet<string>(request.Excluded, StringComparer.OrdinalIgnoreCase);
        if (_options.FolderConflict == FolderConflictPolicy.Skip && !request.Resumed)
        {
            plan = SkipExistingFolders(request, plan);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var result = new CopyJobResult();

        // Mover dentro del mismo volumen: cada elemento de primer nivel se renombra de una vez, sin copiar nada.
        var renamed = _options.Move && !_options.DryRun ? RenameWholeSources(request, plan, result) : [];

        if (_options.CheckFreeSpace && !_options.DryRun && _options.Mode != CopyMode.FoldersOnly)
        {
            EnsureFreeSpace(plan, request.Destination, renamed);
        }

        Log(LogLevel.Info, $"{(_options.Move ? "Mover" : "Copiar")} {plan.Files.Count} archivos ({FormatBytes(plan.TotalBytes)}) a {request.Destination}");

        var renamedItems = new List<TransferItem>();
        // El orden que el usuario eligió arrastrando (se guarda en el trabajo) se respeta al reanudar.
        var files = plan.Files.Where(f => !excluded.Contains(f.Source) && _options.Mode != CopyMode.FoldersOnly);
        if (request.Order.Count > 0)
        {
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < request.Order.Count; i++)
            {
                rank.TryAdd(request.Order[i], i);
            }

            files = files.Select((f, i) => (f, i)).OrderBy(x => rank.GetValueOrDefault(x.f.Source, int.MaxValue)).ThenBy(x => x.i).Select(x => x.f);
        }

        foreach (var file in files)
        {
            var item = Files.Add(file.Source, file.Destination, file.Length, file.LastWriteUtc);
            if (renamed.Contains(file.Source))
            {
                renamedItems.Add(item);
            }
        }

        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        _progress = progress;

        foreach (var item in renamedItems)
        {
            progress.BeginFile(item.Destination, item.Length);
            Files.Update(item, FileState.Copied);
            progress.EndFile();
        }

        if (!_options.DryRun)
        {
            Directory.CreateDirectory(request.Destination);
            foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source) && (_options.Mode != CopyMode.UpdateExisting || Directory.Exists(d.Destination))))
            {
                Directory.CreateDirectory(directory.Destination);
            }

            if (_options.Mode != CopyMode.FoldersOnly)
            {
                CreateLinks(plan.Links.Where(l => !renamed.Contains(l.Source)), result);
            }
        }

        using (var pool = new BufferPool(_options.BlockSize, FileTransfer.RequiredBuffers(_options)))
        {
            var transfer = new FileTransfer(_options, pool, Controller, progress, FaultInjector, (path, bytes) => ChunkCommitted?.Invoke(path, bytes));

            while (Files.TakeNext() is { } item)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                progress.BeginFile(item.Destination, item.Length);
                if (_options.DryRun)
                {
                    SimulateFile(item, result);
                }
                else
                {
                    ProcessFile(transfer, progress, item, result, cancellationToken);
                }

                progress.EndFile();
            }
        }

        if (_options.Mode == CopyMode.Mirror)
        {
            MirrorExtras(request, plan, result, cancellationToken);
        }

        if (_options.DryRun)
        {
            progress.Finish();
            result.Elapsed = stopwatch.Elapsed;
            Log(LogLevel.Info, $"Simulación: se copiarían {result.FilesCopied}, se omitirían {result.FilesSkipped} y se borrarían {result.FilesDeleted}.");
            return result;
        }

        // Las fechas de las carpetas se aplican al final (crear archivos dentro las cambia), de la más profunda a la raíz.
        foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source)).OrderByDescending(d => d.Depth))
        {
            CopyDirectoryMetadata(directory);
        }

        // Al mover, las carpetas de origen que quedaron vacías desaparecen (si algo se omitió, se conservan).
        if (_options.Move)
        {
            foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source)).OrderByDescending(d => d.Depth))
            {
                TryDeleteEmptyDirectory(directory.Source);
            }
        }

        if (_options.SaveChecksumFile && _options.HashAlgorithm != FileHashAlgorithm.None)
        {
            SaveChecksumFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(
            result.Success ? LogLevel.Info : LogLevel.Warning,
            $"Terminado en {result.Elapsed:hh\\:mm\\:ss}: {result.FilesCopied} copiados, {result.FilesSkipped} omitidos, {result.Failures.Count} con errores.");
        return result;
    }

    /// <summary>Borra los .mcpart / .mcjournal que un trabajo descartado dejó en el destino.</summary>
    public static int DeletePartialFiles(CopyJobRequest request)
    {
        CopyPlan plan;
        try
        {
            plan = CopyPlanner.Build(request.Sources, request.Destination);
        }
        catch (CopyPlanException)
        {
            return 0;
        }

        int deleted = 0;
        foreach (var file in plan.Files)
        {
            deleted += DeletePartials(file.Destination);
        }

        return deleted;
    }

    private static int DeletePartials(string destination)
    {
        int deleted = 0;
        foreach (string path in (string[])[destination + FileTransfer.PartialSuffix, destination + FileTransfer.JournalSuffix])
        {
            try
            {
                if (File.Exists(path))
                {
                    IoHelpers.DeleteIfExists(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Si no se puede borrar ahora, no impide continuar.
            }
        }

        return deleted;
    }

    private void ProcessFile(FileTransfer transfer, ProgressTracker progress, TransferItem item, CopyJobResult result, CancellationToken cancellationToken)
    {
        var file = new PlannedFile(item.Source, item.Destination, item.Length, item.LastWriteUtc);
        Resolution resolution;
        try
        {
            resolution = ResolveTarget(file, progress, cancellationToken);
        }
        catch (IOException ex)
        {
            Fail(item, result, ex.Message, FileErrors.Classify(ex));
            return;
        }

        if (resolution.Target is not { } target)
        {
            result.FilesSkipped++;
            item.Action = resolution.Skip;
            Files.Update(item, FileState.Skipped, SkipText(resolution.Skip));

            if (resolution.Identical && _options.VerifySkipped)
            {
                VerifySkippedFile(item, file);
            }

            // Al mover, un destino idéntico significa que el archivo ya llegó (por ejemplo, antes de un corte): falta borrar el origen.
            if (_options.Move && resolution.Identical && item.State == FileState.Skipped)
            {
                TryDeleteSource(item, result);
            }

            return;
        }

        if (!string.Equals(target, item.Destination, StringComparison.OrdinalIgnoreCase))
        {
            item.Destination = target; // "nombre (2).ext"
            Files.Update(item, item.State);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Para "Deshacer": lo que reemplazó a otro archivo no se borra al deshacer (se perdería el anterior).
        item.Action = File.Exists(target) ? PlannedAction.Replace : PlannedAction.Copy;

        if (_options.Move && SameVolume(item.Source, target))
        {
            MoveByRename(item, target, result);
            return;
        }

        // Lo que se va a reemplazar se guarda antes en la carpeta de versiones.
        if (_options.VersionsFolder.Length > 0 && File.Exists(target))
        {
            KeepVersion(target);
        }

        // Enlaces duros: si este archivo es el mismo que otro ya copiado, se enlaza en lugar de copiarlo otra vez.
        if (_options.PreserveHardLinks && TryHardLink(item, target, result))
        {
            return;
        }

        using var fileCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lock)
        {
            _currentFile = fileCancellation;
        }

        try
        {
            if (CopyWithRetry(transfer, progress, item, target, result, fileCancellation.Token) is not { } outcome)
            {
                return;
            }

            item.SourceHash = outcome.SourceHash;
            item.DestinationHash = outcome.DestinationHash;
            foreach (string warning in outcome.Warnings)
            {
                Log(LogLevel.Warning, warning, item.Source);
            }

            if (outcome.ChunksRepaired > 0)
            {
                Log(LogLevel.Warning, $"La verificación reparó {outcome.ChunksRepaired} bloques dañados.", item.Source);
            }

            if (outcome.BytesResumed > 0)
            {
                Log(LogLevel.Info, $"Reanudado desde {FormatBytes(outcome.BytesResumed)}.", item.Source);
            }

            bool verified = outcome.DestinationHash is not null;
            if (verified && outcome.SourceHash != outcome.DestinationHash)
            {
                Fail(item, result, "Los hashes no coinciden", FileErrorKind.HashMismatch);
                return;
            }

            // Verificación completa: también se relee el origen (detecta una lectura defectuosa del disco de origen).
            if (verified && _options.VerifySource && outcome.SourceHash is { } expected && HashFile(item.Source) != expected)
            {
                Fail(item, result, "El origen releído no coincide con lo copiado (lectura defectuosa o cambió durante la copia)", FileErrorKind.HashMismatch);
                return;
            }

            if (_options.SaveHashStream && outcome.SourceHash is { } hash)
            {
                WriteHashStream(target, hash, item.LastWriteUtc);
            }

            RememberHardLink(item, target);

            Files.Update(item, verified ? FileState.Verified : FileState.Copied);

            // El original solo se borra cuando su copia está completa (y verificada, si se pidió).
            if (_options.Move)
            {
                TryDeleteSource(item, result);
            }
        }
        catch (OperationCanceledException) when (fileCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Saltado por el usuario: se descarta lo copiado de este archivo.
            DeletePartials(target);
            result.FilesSkipped++;
            item.Action = PlannedAction.SkipByUser;
            Files.Update(item, FileState.Skipped, "Saltado por el usuario");
            Log(LogLevel.Info, "Saltado por el usuario.", item.Source);
        }
        finally
        {
            lock (_lock)
            {
                _currentFile = null;
            }
        }
    }

    private void VerifySkippedFile(TransferItem item, PlannedFile file)
    {
        try
        {
            string? sourceHash = HashFile(file.Source);
            string? destinationHash = HashFile(file.Destination);
            item.SourceHash = sourceHash;
            item.DestinationHash = destinationHash;
            if (sourceHash != destinationHash)
            {
                Files.Update(item, FileState.Failed, "El archivo existente no coincide con el origen");
                Log(LogLevel.Error, "El archivo existente tiene el mismo tamaño y fecha pero distinto contenido.", item.Source);
            }
            else
            {
                Files.Update(item, FileState.Skipped, "Idéntico (verificado por hash)");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Warning, $"No se pudo verificar el archivo omitido: {ex.Message}", item.Source);
        }
    }

    private string? HashFile(string path)
    {
        var hasher = FileHasher.Create(_options.HashAlgorithm == FileHashAlgorithm.None ? FileHashAlgorithm.XxHash3 : _options.HashAlgorithm);
        if (hasher is null)
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hasher.Append(buffer.AsSpan(0, read));
        }

        return hasher.Finish();
    }

    private void SaveChecksumFile()
    {
        var items = Files.Snapshot().Where(i => i.SourceHash is not null && i.State is FileState.Copied or FileState.Verified).ToList();
        if (items.Count == 0)
        {
            return;
        }

        string path = Path.Combine(_destinationRoot, $"MegaCopier_{DateTime.Now:yyyyMMdd_HHmmss}.{FileHasher.Extension(_options.HashAlgorithm)}");
        var text = new StringBuilder();
        foreach (var item in items)
        {
            string relative = Path.GetRelativePath(_destinationRoot, item.Destination);
            text.Append(_options.HashAlgorithm == FileHashAlgorithm.Crc32
                ? $"{relative} {item.SourceHash!.ToUpperInvariant()}\n"   // formato .sfv
                : $"{item.SourceHash} *{relative}\n");                     // formato de sha256sum y compañía
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        ChecksumFile = path;
        Log(LogLevel.Info, $"Archivo de sumas guardado: {path}");
    }

    private void Fail(TransferItem item, CopyJobResult result, string message, FileErrorKind kind = FileErrorKind.Other)
    {
        result.Failures.Add(new FileFailure(item.Source, message, kind));
        Files.Update(item, FileState.Failed, message, kind);
        Log(LogLevel.Error, message, item.Source);
    }

    private void Log(LogLevel level, string message, string? path = null) =>
        Logged?.Invoke(new LogEntry(DateTime.Now, level, message, path));

    /// <summary>Renombra de una vez cada origen de primer nivel que esté en el mismo volumen y no choque con nada.</summary>
    private HashSet<string> RenameWholeSources(CopyJobRequest request, CopyPlan plan, CopyJobResult result)
    {
        var renamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        if (request.Excluded.Count > 0)
        {
            return renamed; // con archivos excluidos hay que mover uno a uno
        }

        foreach (string raw in request.Sources)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            string target = Path.Combine(destination, Path.GetFileName(source));
            bool isDirectory = Directory.Exists(source);

            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
                || File.Exists(target) || Directory.Exists(target) || File.Exists(target + FileTransfer.PartialSuffix)
                || !SameVolume(source, destination))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(destination);
                if (isDirectory)
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // se moverá archivo por archivo
            }

            renamed.Add(source);
            foreach (var file in plan.Files.Where(f => IsSameOrInside(f.Source, source)))
            {
                renamed.Add(file.Source);
                result.FilesCopied++;
            }

            foreach (var directory in plan.Directories.Where(d => IsSameOrInside(d.Source, source)))
            {
                renamed.Add(directory.Source);
            }
        }

        return renamed;
    }

    private void MoveByRename(TransferItem item, string target, CopyJobResult result)
    {
        try
        {
            ClearReadOnly(target);
            File.Move(item.Source, target, overwrite: true);
            result.FilesCopied++;
            Files.Update(item, FileState.Copied);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(item, result, ex.Message, FileErrors.Classify(ex));
        }
    }

    private void TryDeleteSource(TransferItem item, CopyJobResult result)
    {
        try
        {
            ClearReadOnly(item.Source);
            if (_options.WipeSource)
            {
                Wipe(item.Source, new byte[CopyOptions.MiB], new ProgressTracker(null, TimeSpan.FromSeconds(1)), CancellationToken.None);
            }
            else
            {
                File.Delete(item.Source);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(item, result, ex.Message, FileErrorKind.DeleteFailed);
        }
    }

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se puede borrar, la carpeta vacía se queda: no se pierde nada.
        }
    }

    private bool SameVolume(string a, string b)
    {
        if (SameVolumeOverride is { } decide)
        {
            return decide(a, b);
        }

        string? rootA = CachedVolumeRoot(a);
        string? rootB = CachedVolumeRoot(b);
        return rootA is not null && string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El volumen se resuelve por carpeta: miles de archivos de la misma carpeta cuestan una sola consulta.</summary>
    private string? CachedVolumeRoot(string path)
    {
        string key = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        lock (_volumeCache)
        {
            if (!_volumeCache.TryGetValue(key, out string? root))
            {
                root = Win32.VolumeRoot(key);
                _volumeCache[key] = root;
            }

            return root;
        }
    }

    private static bool TargetExists(string source, string destination)
    {
        string target = Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))));
        return File.Exists(target) || Directory.Exists(target);
    }

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private FileTransferResult? CopyWithRetry(FileTransfer transfer, ProgressTracker progress, TransferItem item, string target, CopyJobResult result, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var outcome = transfer.Copy(item.Source, target, cancellationToken);
                result.FilesCopied++;
                result.BytesTransferred += outcome.BytesTransferred;
                result.BytesResumed += outcome.BytesResumed;
                result.ChunksRejectedOnResume += outcome.ChunksRejectedOnResume;
                result.ChunksRepaired += outcome.ChunksRepaired;
                if (outcome.BytesResumed > 0)
                {
                    result.FilesResumed++;
                }

                return outcome;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !cancellationToken.IsCancellationRequested)
            {
                // Red o unidad desconectada, o disco lleno: se espera a que se arregle sin gastar reintentos.
                var kind = FileErrors.Classify(ex);
                if (WaitForRecovery(kind, item, target, progress, cancellationToken))
                {
                    attempt--;
                    progress.RestartFile();
                    continue;
                }

                if (attempt >= _options.RetryCount)
                {
                    if (_options.OnError == ErrorPolicy.Stop)
                    {
                        throw;
                    }

                    // "Al final de la cola": se vuelve a intentar una vez cuando acabe todo lo demás.
                    if (_options.OnError == ErrorPolicy.MoveToEnd && _requeued.Add(item.Id))
                    {
                        Log(LogLevel.Warning, $"Se reintentará al final: {ex.Message}", item.Source);
                        Files.Requeue(item);
                        return null;
                    }

                    Fail(item, result, ex.Message, kind);
                    return null;
                }

                // Un reintento continúa desde el journal (útil si se desconectó un USB o la red un momento).
                Log(LogLevel.Warning, $"Reintentando ({attempt + 1}/{_options.RetryCount}): {ex.Message}", item.Source);
                progress.RestartFile();
                cancellationToken.WaitHandle.WaitOne(_options.RetryDelay);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private readonly record struct Resolution(string? Target, PlannedAction Skip = PlannedAction.None)
    {
        public bool Identical => Skip == PlannedAction.SkipIdentical;
    }

    private readonly HashSet<int> _requeued = [];

    /// <summary>Decide el nombre final del archivo, o ninguno si hay que omitirlo.</summary>
    private Resolution ResolveTarget(PlannedFile file, ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (string.Equals(file.Source, file.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return new Resolution(null, PlannedAction.SkipSamePath);
        }

        var existing = new FileInfo(file.Destination);
        if (!existing.Exists)
        {
            // "Solo actualizar los existentes": lo que no está en el destino no se añade.
            return _options.Mode == CopyMode.UpdateExisting
                ? new Resolution(null, PlannedAction.SkipNotInDestination)
                : new Resolution(file.Destination);
        }

        if (!_options.ForceRecopy && IsIdentical(existing, file))
        {
            return new Resolution(null, PlannedAction.SkipIdentical);
        }

        var policy = _options.Conflict;
        if (policy == ConflictPolicy.Ask)
        {
            // Desatendido: nunca se pregunta y lo dudoso se omite.
            var answer = _options.Unattended
                ? new ConflictAnswer(ConflictDecision.Skip, ApplyToAll: false)
                : _conflictForAll ?? AskUser(file, existing, progress, cancellationToken);
            policy = answer.Decision switch
            {
                ConflictDecision.Replace => ConflictPolicy.Overwrite,
                ConflictDecision.KeepBoth => ConflictPolicy.KeepBoth,
                ConflictDecision.RenameExisting => ConflictPolicy.RenameExisting,
                _ => ConflictPolicy.Skip,
            };
        }

        return policy switch
        {
            ConflictPolicy.Overwrite => new Resolution(file.Destination),
            ConflictPolicy.Skip => new Resolution(null, PlannedAction.SkipExists),
            ConflictPolicy.OverwriteIfNewer => file.LastWriteUtc > existing.LastWriteTimeUtc + _options.TimeTolerance ? new Resolution(file.Destination) : new Resolution(null, PlannedAction.SkipOlder),
            ConflictPolicy.OverwriteIfDifferentSize => file.Length != existing.Length ? new Resolution(file.Destination) : new Resolution(null, PlannedAction.SkipSameSize),
            ConflictPolicy.KeepBoth => UniqueTarget(file),
            ConflictPolicy.RenameExisting => RenameExisting(file),
            ConflictPolicy.Fail => throw new IOException($"El destino ya existe: {file.Destination}"),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };
    }

    /// <summary>Aparta el archivo que ya estaba con el patrón de nombres y deja libre el nombre original.</summary>
    private Resolution RenameExisting(PlannedFile file)
    {
        string renamed = FreeName(file.Destination, path => !File.Exists(path) && !Directory.Exists(path));
        File.Move(file.Destination, renamed);
        Log(LogLevel.Info, $"El archivo que ya existía se renombró a {Path.GetFileName(renamed)}.", file.Destination);
        return new Resolution(file.Destination);
    }

    private ConflictAnswer AskUser(PlannedFile file, FileInfo existing, ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (ConflictResolver is not { } resolver)
        {
            return new ConflictAnswer(ConflictDecision.Skip, ApplyToAll: false);
        }

        progress.SetPhase(CopyPhase.WaitingForUser);
        try
        {
            var answer = resolver(
                new ConflictInfo(file.Source, file.Destination, file.Length, file.LastWriteUtc, existing.Length, existing.LastWriteTimeUtc),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (answer.ApplyToAll)
            {
                _conflictForAll = answer;
            }

            return answer;
        }
        finally
        {
            progress.SetPhase(CopyPhase.Copying);
        }
    }

    /// <summary>"nombre (2).ext", "nombre (3).ext"... reutilizando uno a medias o ya idéntico (para que reanudar no duplique).</summary>
    private Resolution UniqueTarget(PlannedFile file)
    {
        string? reuse = null;
        bool identical = false;
        FreeName(file.Destination, candidate =>
        {
            if (File.Exists(candidate + FileTransfer.PartialSuffix))
            {
                reuse = candidate;
                return true;
            }

            var info = new FileInfo(candidate);
            if (!info.Exists)
            {
                reuse = candidate;
                return true;
            }

            if (IsIdentical(info, file))
            {
                identical = true;
                return true;
            }

            return false;
        });
        return identical ? new Resolution(null, PlannedAction.SkipIdentical) : new Resolution(reuse);
    }

    /// <summary>
    /// Primer nombre del patrón ({name}, {n}, {ext}, {date}) que <paramref name="accept"/> acepta, empezando por n = 2.
    /// </summary>
    private string FreeName(string path, Func<string, bool> accept)
    {
        string directory = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string pattern = string.IsNullOrWhiteSpace(_options.KeepBothPattern) ? CopyOptions.DefaultKeepBothPattern : _options.KeepBothPattern;
        if (!pattern.Contains("{n}", StringComparison.Ordinal))
        {
            pattern = pattern.Replace("{ext}", " ({n}){ext}", StringComparison.Ordinal);
            if (!pattern.Contains("{n}", StringComparison.Ordinal))
            {
                pattern += " ({n})";
            }
        }

        string date = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        for (int n = 2; ; n++)
        {
            string fileName = pattern
                .Replace("{name}", name, StringComparison.Ordinal)
                .Replace("{ext}", extension, StringComparison.Ordinal)
                .Replace("{date}", date, StringComparison.Ordinal)
                .Replace("{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            string candidate = Path.Combine(directory, fileName);
            if (accept(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Mismo tamaño y fecha (con la tolerancia de FAT/exFAT/NAS, que guardan la hora con menos precisión).</summary>
    private bool IsIdentical(FileInfo existing, PlannedFile file) =>
        existing.Length == file.Length && (existing.LastWriteTimeUtc - file.LastWriteUtc).Duration() <= _options.TimeTolerance;

    private void EnsureFreeSpace(CopyPlan plan, string destination, HashSet<string> renamed)
    {
        long? free = Win32.GetFreeBytes(Path.GetFullPath(destination));
        if (free is null)
        {
            return;
        }

        long needed = 0;
        foreach (var file in plan.Files)
        {
            if (renamed.Contains(file.Source) || (_options.Move && SameVolume(file.Source, destination)))
            {
                continue; // mover en el mismo volumen no ocupa espacio
            }

            var existing = new FileInfo(file.Destination);
            if (existing.Exists && IsIdentical(existing, file))
            {
                continue;
            }

            // Un .mcpart ya tiene reservado su tamaño completo.
            var partial = new FileInfo(file.Destination + FileTransfer.PartialSuffix);
            needed += partial.Exists ? Math.Max(0, file.Length - partial.Length) : file.Length;
        }

        if (needed > free)
        {
            throw new CopyPlanException(
                $"No hay espacio suficiente en el destino: se necesitan {needed / (double)(1L << 30):F2} GB y hay {free.Value / (double)(1L << 30):F2} GB libres.");
        }
    }

    private void CopyDirectoryMetadata(PlannedDirectory directory)
    {
        try
        {
            if (_options.CopySecurity != SecurityParts.None)
            {
                FileMetadata.CopySecurity(directory.Source, directory.Destination, _options.CopySecurity);
            }

            if (_options.PreserveAttributes)
            {
                var attributes = File.GetAttributes(directory.Source) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
                if (attributes != 0)
                {
                    File.SetAttributes(directory.Destination, File.GetAttributes(directory.Destination) | attributes);
                }
            }

            if (_options.PreserveTimestamps)
            {
                Directory.SetCreationTimeUtc(directory.Destination, Directory.GetCreationTimeUtc(directory.Source));
                Directory.SetLastWriteTimeUtc(directory.Destination, Directory.GetLastWriteTimeUtc(directory.Source));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Warning, $"Carpeta: {ex.Message}", directory.Source);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
