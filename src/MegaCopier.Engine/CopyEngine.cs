using System.Diagnostics;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Ejecuta un trabajo de copia (o de movimiento) completo. Es reanudable sin estado propio: al volver a lanzar el
/// mismo trabajo, los archivos ya terminados se reconocen como idénticos y los grandes a medias continúan desde su journal.
/// </summary>
public sealed class CopyEngine
{
    private readonly CopyOptions _options;
    private ConflictAnswer? _conflictForAll;

    public CopyEngine(CopyOptions? options = null)
    {
        _options = options ?? new CopyOptions();
        _options.Validate();
    }

    public CopyController Controller { get; } = new();

    public event Action<CopyProgress>? ProgressChanged;

    /// <summary>Se dispara cada vez que un bloque queda confirmado en el disco (ruta destino, bytes confirmados del archivo).</summary>
    public event Action<string, long>? ChunkCommitted;

    /// <summary>
    /// Pregunta qué hacer cuando un destino ya existe (con <see cref="ConflictPolicy.Ask"/>). Se llama desde el hilo de
    /// copia y puede bloquear hasta que el usuario responda.
    /// </summary>
    public Func<ConflictInfo, CancellationToken, ConflictAnswer>? ConflictResolver { get; set; }

    internal IFaultInjector? FaultInjector { get; set; }

    /// <summary>Solo pruebas: decide si dos rutas están en el mismo volumen.</summary>
    internal Func<string, string, bool>? SameVolumeOverride { get; set; }

    public Task<CopyJobResult> RunAsync(CopyJobRequest request, CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => Run(request, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    public CopyJobResult Run(CopyJobRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

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

        var plan = CopyPlanner.Build(request.Sources, request.Destination);
        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var result = new CopyJobResult();
        _conflictForAll = null;

        // Mover dentro del mismo volumen: cada elemento de primer nivel se renombra de una vez, sin copiar nada.
        var renamed = _options.Move ? RenameWholeSources(request, plan, result) : [];

        if (_options.CheckFreeSpace)
        {
            EnsureFreeSpace(plan, request.Destination, renamed);
        }

        progress.Start(plan.TotalBytes, plan.Files.Count);
        foreach (var file in plan.Files.Where(f => renamed.Contains(f.Source)))
        {
            progress.BeginFile(file.Destination, file.Length);
            progress.EndFile();
        }

        Directory.CreateDirectory(request.Destination);
        foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source)))
        {
            Directory.CreateDirectory(directory.Destination);
        }

        using (var pool = new BufferPool(_options.BlockSize, FileTransfer.RequiredBuffers(_options)))
        {
            var transfer = new FileTransfer(_options, pool, Controller, progress, FaultInjector, (path, bytes) => ChunkCommitted?.Invoke(path, bytes));

            foreach (var file in plan.Files.Where(f => !renamed.Contains(f.Source)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                progress.BeginFile(file.Destination, file.Length);
                ProcessFile(transfer, progress, file, result, cancellationToken);
                progress.EndFile();
            }
        }

        // Las fechas de las carpetas se aplican al final (crear archivos dentro las cambia), de la más profunda a la raíz.
        if (_options.PreserveTimestamps)
        {
            foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source)).OrderByDescending(d => d.Depth))
            {
                TryCopyDirectoryTimes(directory);
            }
        }

        // Al mover, las carpetas de origen que quedaron vacías desaparecen (si algo se omitió, se conservan).
        if (_options.Move)
        {
            foreach (var directory in plan.Directories.Where(d => !renamed.Contains(d.Source)).OrderByDescending(d => d.Depth))
            {
                TryDeleteEmptyDirectory(directory.Source);
            }
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
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
            foreach (string path in (string[])[file.Destination + FileTransfer.PartialSuffix, file.Destination + FileTransfer.JournalSuffix])
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
                    // Si no se puede borrar ahora, no impide descartar el trabajo.
                }
            }
        }

        return deleted;
    }

    private void ProcessFile(FileTransfer transfer, ProgressTracker progress, PlannedFile file, CopyJobResult result, CancellationToken cancellationToken)
    {
        Resolution resolution;
        try
        {
            resolution = ResolveTarget(file, progress, cancellationToken);
        }
        catch (IOException ex)
        {
            result.Failures.Add(new FileFailure(file.Source, ex.Message));
            return;
        }

        if (resolution.Target is not { } target)
        {
            result.FilesSkipped++;

            // Al mover, un destino idéntico significa que el archivo ya llegó (por ejemplo, antes de un corte): falta borrar el origen.
            if (_options.Move && resolution.Identical)
            {
                TryDeleteSource(file, result);
            }

            return;
        }

        if (_options.Move && SameVolume(file.Source, target))
        {
            MoveByRename(file, target, result);
            return;
        }

        if (CopyWithRetry(transfer, progress, file, target, result, cancellationToken) && _options.Move)
        {
            // El original solo se borra cuando su copia está completa (y verificada, si se pidió).
            TryDeleteSource(file, result);
        }
    }

    /// <summary>Renombra de una vez cada origen de primer nivel que esté en el mismo volumen y no choque con nada.</summary>
    private HashSet<string> RenameWholeSources(CopyJobRequest request, CopyPlan plan, CopyJobResult result)
    {
        var renamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));

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

    private static void MoveByRename(PlannedFile file, string target, CopyJobResult result)
    {
        try
        {
            ClearReadOnly(target);
            File.Move(file.Source, target, overwrite: true);
            result.FilesCopied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Failures.Add(new FileFailure(file.Source, ex.Message));
        }
    }

    private static void TryDeleteSource(PlannedFile file, CopyJobResult result)
    {
        try
        {
            ClearReadOnly(file.Source);
            File.Delete(file.Source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Failures.Add(new FileFailure(file.Source, $"Se copió, pero no se pudo borrar el original: {ex.Message}"));
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

    private readonly Dictionary<string, string?> _volumeCache = new(StringComparer.OrdinalIgnoreCase);

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
        if (!_volumeCache.TryGetValue(key, out string? root))
        {
            root = Win32.VolumeRoot(key);
            _volumeCache[key] = root;
        }

        return root;
    }

    private static bool TargetExists(string source, string destination)
    {
        string target = Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))));
        return File.Exists(target) || Directory.Exists(target);
    }

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private bool CopyWithRetry(FileTransfer transfer, ProgressTracker progress, PlannedFile file, string target, CopyJobResult result, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var outcome = transfer.Copy(file.Source, target, cancellationToken);
                result.FilesCopied++;
                result.BytesTransferred += outcome.BytesTransferred;
                result.BytesResumed += outcome.BytesResumed;
                result.ChunksRejectedOnResume += outcome.ChunksRejectedOnResume;
                result.ChunksRepaired += outcome.ChunksRepaired;
                if (outcome.BytesResumed > 0)
                {
                    result.FilesResumed++;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.RetryCount)
                {
                    if (_options.OnError == ErrorPolicy.Stop)
                    {
                        throw;
                    }

                    result.Failures.Add(new FileFailure(file.Source, ex.Message));
                    return false;
                }

                // Un reintento continúa desde el journal (útil si se desconectó un USB o la red un momento).
                progress.RestartFile();
                cancellationToken.WaitHandle.WaitOne(_options.RetryDelay);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private readonly record struct Resolution(string? Target, bool Identical = false);

    /// <summary>Decide el nombre final del archivo, o ninguno si hay que omitirlo.</summary>
    private Resolution ResolveTarget(PlannedFile file, ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (string.Equals(file.Source, file.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return new Resolution(null);
        }

        var existing = new FileInfo(file.Destination);
        if (!existing.Exists)
        {
            return new Resolution(file.Destination);
        }

        if (!_options.ForceRecopy && IsIdentical(existing, file))
        {
            return new Resolution(null, Identical: true);
        }

        var policy = _options.Conflict;
        if (policy == ConflictPolicy.Ask)
        {
            var answer = _conflictForAll ?? AskUser(file, existing, progress, cancellationToken);
            policy = answer.Decision switch
            {
                ConflictDecision.Replace => ConflictPolicy.Overwrite,
                ConflictDecision.KeepBoth => ConflictPolicy.KeepBoth,
                _ => ConflictPolicy.Skip,
            };
        }

        return policy switch
        {
            ConflictPolicy.Overwrite => new Resolution(file.Destination),
            ConflictPolicy.Skip => new Resolution(null),
            ConflictPolicy.OverwriteIfNewer => new Resolution(file.LastWriteUtc > existing.LastWriteTimeUtc ? file.Destination : null),
            ConflictPolicy.KeepBoth => UniqueTarget(file),
            ConflictPolicy.Fail => throw new IOException($"El destino ya existe: {file.Destination}"),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };
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
    private static Resolution UniqueTarget(PlannedFile file)
    {
        string directory = Path.GetDirectoryName(file.Destination)!;
        string name = Path.GetFileNameWithoutExtension(file.Destination);
        string extension = Path.GetExtension(file.Destination);

        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(directory, $"{name} ({n}){extension}");
            if (File.Exists(candidate + FileTransfer.PartialSuffix))
            {
                return new Resolution(candidate);
            }

            var info = new FileInfo(candidate);
            if (!info.Exists)
            {
                return new Resolution(candidate);
            }

            if (IsIdentical(info, file))
            {
                return new Resolution(null, Identical: true);
            }
        }
    }

    private static bool IsIdentical(FileInfo existing, PlannedFile file) =>
        existing.Length == file.Length && existing.LastWriteTimeUtc == file.LastWriteUtc;

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

    private static void TryCopyDirectoryTimes(PlannedDirectory directory)
    {
        try
        {
            Directory.SetCreationTimeUtc(directory.Destination, Directory.GetCreationTimeUtc(directory.Source));
            Directory.SetLastWriteTimeUtc(directory.Destination, Directory.GetLastWriteTimeUtc(directory.Source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No es crítico: los datos ya están copiados.
        }
    }
}
