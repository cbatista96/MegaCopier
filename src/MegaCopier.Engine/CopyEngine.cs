using System.Diagnostics;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine;

/// <summary>
/// Ejecuta un trabajo de copia completo. Es reanudable sin estado propio: al volver a lanzar el mismo trabajo,
/// los archivos ya terminados se reconocen como idénticos y los grandes a medias continúan desde su journal.
/// </summary>
public sealed class CopyEngine
{
    private readonly CopyOptions _options;

    public CopyEngine(CopyOptions? options = null)
    {
        _options = options ?? new CopyOptions();
        _options.Validate();
    }

    public CopyController Controller { get; } = new();

    public event Action<CopyProgress>? ProgressChanged;

    /// <summary>Se dispara cada vez que un bloque queda confirmado en el disco (ruta destino, bytes confirmados del archivo).</summary>
    public event Action<string, long>? ChunkCommitted;

    internal IFaultInjector? FaultInjector { get; set; }

    public Task<CopyJobResult> RunAsync(CopyJobRequest request, CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => Run(request, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    public CopyJobResult Run(CopyJobRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = CopyPlanner.Build(request.Sources, request.Destination);

        if (_options.CheckFreeSpace)
        {
            EnsureFreeSpace(plan, request.Destination);
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        progress.Start(plan.TotalBytes, plan.Files.Count);

        Directory.CreateDirectory(request.Destination);
        foreach (var directory in plan.Directories)
        {
            Directory.CreateDirectory(directory.Destination);
        }

        var result = new CopyJobResult();
        using (var pool = new BufferPool(_options.BlockSize, FileTransfer.RequiredBuffers(_options)))
        {
            var transfer = new FileTransfer(_options, pool, Controller, progress, FaultInjector, (path, bytes) => ChunkCommitted?.Invoke(path, bytes));

            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Controller.WaitIfPaused(cancellationToken);
                progress.BeginFile(file.Destination, file.Length);

                string? target;
                try
                {
                    target = ResolveTarget(file);
                }
                catch (IOException ex)
                {
                    result.Failures.Add(new FileFailure(file.Source, ex.Message));
                    progress.EndFile();
                    continue;
                }

                if (target is null)
                {
                    result.FilesSkipped++;
                    progress.EndFile();
                    continue;
                }

                CopyWithRetry(transfer, progress, file, target, result, cancellationToken);
                progress.EndFile();
            }
        }

        // Las fechas de las carpetas se aplican al final (crear archivos dentro las cambia), de la más profunda a la raíz.
        if (_options.PreserveTimestamps)
        {
            foreach (var directory in plan.Directories.OrderByDescending(d => d.Depth))
            {
                TryCopyDirectoryTimes(directory);
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

    private void CopyWithRetry(FileTransfer transfer, ProgressTracker progress, PlannedFile file, string target, CopyJobResult result, CancellationToken cancellationToken)
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

                return;
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
                    return;
                }

                // Un reintento continúa desde el journal (útil si se desconectó un USB o la red un momento).
                progress.RestartFile();
                cancellationToken.WaitHandle.WaitOne(_options.RetryDelay);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>Decide el nombre final del archivo o null si hay que omitirlo.</summary>
    private string? ResolveTarget(PlannedFile file)
    {
        if (string.Equals(file.Source, file.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var existing = new FileInfo(file.Destination);
        if (!existing.Exists)
        {
            return file.Destination;
        }

        if (!_options.ForceRecopy && IsIdentical(existing, file))
        {
            return null;
        }

        return _options.Conflict switch
        {
            ConflictPolicy.Overwrite => file.Destination,
            ConflictPolicy.Skip => null,
            ConflictPolicy.OverwriteIfNewer => file.LastWriteUtc > existing.LastWriteTimeUtc ? file.Destination : null,
            ConflictPolicy.KeepBoth => UniqueTarget(file),
            ConflictPolicy.Fail => throw new IOException($"El destino ya existe: {file.Destination}"),
            _ => throw new ArgumentOutOfRangeException(nameof(file)),
        };
    }

    /// <summary>"nombre (2).ext", "nombre (3).ext"... reutilizando uno a medias o ya idéntico (para que reanudar no duplique).</summary>
    private static string? UniqueTarget(PlannedFile file)
    {
        string directory = Path.GetDirectoryName(file.Destination)!;
        string name = Path.GetFileNameWithoutExtension(file.Destination);
        string extension = Path.GetExtension(file.Destination);

        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(directory, $"{name} ({n}){extension}");
            if (File.Exists(candidate + FileTransfer.PartialSuffix))
            {
                return candidate;
            }

            var info = new FileInfo(candidate);
            if (!info.Exists)
            {
                return candidate;
            }

            if (IsIdentical(info, file))
            {
                return null;
            }
        }
    }

    private static bool IsIdentical(FileInfo existing, PlannedFile file) =>
        existing.Length == file.Length && existing.LastWriteTimeUtc == file.LastWriteUtc;

    private static void EnsureFreeSpace(CopyPlan plan, string destination)
    {
        long? free = Win32.GetFreeBytes(Path.GetFullPath(destination));
        if (free is null)
        {
            return;
        }

        long needed = 0;
        foreach (var file in plan.Files)
        {
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
