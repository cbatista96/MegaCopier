using System.Diagnostics;
using MegaCopier.Engine.IO;
using MegaCopier.Engine.Sync;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Comparar y sincronizar el contenido de dos carpetas (Sources[0] = izquierda, Destination = derecha), en dos
    /// sentidos o como espejo. Primero se guarda (Papelera o .megacopier-borrados) lo que se va a borrar o sobrescribir;
    /// después se copia con el bucle de siempre (verificación, archivos pequeños en paralelo, reintentos...); al final se
    /// recuerda el estado para la próxima vez. Lo excluido en la vista de comparación (rutas relativas) no se toca.
    /// </summary>
    /// <summary>Solo pruebas: dónde se recuerda el estado de las sincronizaciones (por defecto, Datossync).</summary>
    internal string? SyncStateFolder { get; set; }

    /// <summary>Solo pruebas: false para no llenar la Papelera del usuario (todo va a .megacopier-borrados).</summary>
    internal bool SyncUseRecycleBin { get; set; } = true;

    private CopyJobResult RunSync(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        string left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Sources[0]));
        string right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Destination));
        if (!Directory.Exists(left))
        {
            throw new CopyPlanException(Texts.F("E_SourceMissing", left));
        }

        if (!Directory.Exists(right) && _options.SyncDirection != SyncDirection.LeftToRight)
        {
            throw new CopyPlanException(Texts.F("E_SourceMissing", right));
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || left.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new CopyPlanException(Texts.F("E_IntoItself", left));
        }

        _destinationRoot = right;
        _options.Conflict = ConflictPolicy.Overwrite; // el plan ya decidió; lo sustituido se guardó antes
        _options.PreserveTimestamps = true;           // si no, los dos lados nunca parecerían iguales
        string stateFile = FolderSync.StateFile(left, right, SyncStateFolder);
        var leftFiles = FolderSync.Scan(left, _options.Filter);
        var rightFiles = FolderSync.Scan(right, _options.Filter);
        var last = _options.SyncDirection == SyncDirection.TwoWay ? FolderSync.LoadState(stateFile) : [];
        // Desmarcado en la vista de comparación (relativo) o quitado de la lista de archivos (ruta completa de un lado).
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in request.Excluded)
        {
            excluded.Add(path.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(left, path)
                : path.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(right, path)
                : path);
        }

        var plan = FolderSync.Plan(leftFiles, rightFiles, last, _options.SyncDirection, _options.TimeTolerance)
            .Where(i => i.Action != SyncAction.Equal && !excluded.Contains(i.Relative))
            .ToList();

        string Side(bool isLeft, string relative) => Path.Combine(isLeft ? left : right, relative);

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        _progress = progress;
        Log(LogLevel.Info, $"Sincronizar {left} ⇄ {right}: {plan.Count} cambios.");

        if (_options.DryRun)
        {
            foreach (var item in plan)
            {
                var entry = item.Left ?? item.Right;
                var row = Files.Add(From(item), To(item), Math.Max(0, entry?.Size ?? 0), entry?.ModifiedUtc ?? DateTime.MinValue);
                bool removes = item.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight or SyncAction.DeleteFolderLeft or SyncAction.DeleteFolderRight;
                if (removes)
                {
                    result.FilesDeleted++;
                }
                else
                {
                    result.FilesCopied++;
                }

                Files.Update(row, FileState.Planned, Texts.Get(removes ? "E_WouldDelete" : "E_WouldCopy"));
            }

            progress.Finish();
            result.Elapsed = stopwatch.Elapsed;
            Log(LogLevel.Info, Texts.F("E_DryRunSummary", result.FilesCopied, result.FilesSkipped, result.FilesDeleted));
            return result;
        }

        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture);

        // 1. Archivos que se borran, y los que se van a sobrescribir: a la Papelera (o a .megacopier-borrados).
        foreach (var item in plan.Where(i => i.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight))
        {
            bool onLeft = item.Action == SyncAction.DeleteLeft;
            string path = Side(onLeft, item.Relative);
            var row = Files.Add(path, path, item.Left?.Size ?? item.Right?.Size ?? 0, (item.Left ?? item.Right)?.ModifiedUtc ?? DateTime.MinValue);
            try
            {
                FolderSync.Discard(path, onLeft ? left : right, stamp, SyncUseRecycleBin);
                result.FilesDeleted++;
                Files.Update(row, FileState.Deleted, Texts.Get("E_Deleted"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail(row, result, ex.Message, FileErrors.Classify(ex));
            }
        }

        var copies = new List<(SyncItem Item, string From, string To)>();
        foreach (var item in plan.Where(i => i.Action is SyncAction.CopyToLeft or SyncAction.CopyToRight))
        {
            bool toRight = item.Action == SyncAction.CopyToRight;
            string from = Side(toRight, item.Relative);   // hacia la derecha, sale de la izquierda
            string to = Side(!toRight, item.Relative);
            try
            {
                if (File.Exists(to))
                {
                    FolderSync.Discard(to, toRight ? right : left, stamp, SyncUseRecycleBin);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                copies.Add((item, from, to));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var row = Files.Add(from, to, item.Left?.Size ?? 0, DateTime.MinValue);
                Fail(row, result, ex.Message, FileErrors.Classify(ex));
            }
        }

        // 2. Carpetas nuevas (también las vacías).
        foreach (var item in plan.Where(i => i.Action is SyncAction.CreateFolderLeft or SyncAction.CreateFolderRight))
        {
            Directory.CreateDirectory(Side(item.Action == SyncAction.CreateFolderLeft, item.Relative));
        }

        // 3. Copias, con el bucle de siempre.
        foreach (var (item, from, to) in copies)
        {
            var entry = item.Action == SyncAction.CopyToRight ? item.Left!.Value : item.Right!.Value;
            Files.Add(from, to, entry.Size, entry.ModifiedUtc);
        }

        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        using (var pool = new BufferPool(_options.BlockSize, FileTransfer.RequiredBuffers(_options)))
        {
            var transfer = new FileTransfer(_options, pool, Controller, progress, FaultInjector, (path, bytes) => ChunkCommitted?.Invoke(path, bytes));
            using var helpers = new SmallFileHelpers(this, progress, result, cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                helpers.ThrowIfFailed();
                if (Files.TakeNext() is not { } row)
                {
                    helpers.WaitIdle();
                    if (Files.HasPending())
                    {
                        continue;
                    }

                    break;
                }

                Controller.WaitIfPaused(cancellationToken);
                progress.BeginFile(row.Destination, row.Length);
                ProcessFile(transfer, progress, row, result, cancellationToken);
                progress.EndFile();
            }

            helpers.Stop();
        }

        // 4. Carpetas borradas en un lado: en el otro, si se han quedado vacías (de la más profunda a la raíz).
        foreach (var item in plan.Where(i => i.Action is SyncAction.DeleteFolderLeft or SyncAction.DeleteFolderRight).OrderByDescending(i => i.Relative.Length))
        {
            bool onLeft = item.Action == SyncAction.DeleteFolderLeft;
            string path = Side(onLeft, item.Relative);
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                }
            }
            catch (IOException)
            {
                // Algo nuevo dentro: se queda (la próxima vez se copiará al otro lado).
            }
        }

        // 5. Lo que quedó igual en los dos lados se recuerda para la próxima vez.
        try
        {
            FolderSync.SaveState(stateFile, FolderSync.Scan(left, _options.Filter), FolderSync.Scan(right, _options.Filter), _options.TimeTolerance);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogLevel.Warning, ex.Message);
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(result.Success ? LogLevel.Info : LogLevel.Warning,
            Texts.F("E_DoneCopy", result.Elapsed.ToString(@"hh\:mm\:ss"), result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;

        // De dónde sale cada cosa (o qué se borra) y adónde va (en los borrados, la misma ruta).
        string From(SyncItem item) => Side(item.Action is SyncAction.CopyToRight or SyncAction.CreateFolderRight or SyncAction.DeleteLeft or SyncAction.DeleteFolderLeft, item.Relative);

        string To(SyncItem item) => item.Action switch
        {
            SyncAction.CopyToRight or SyncAction.CreateFolderRight => Side(false, item.Relative),
            SyncAction.CopyToLeft or SyncAction.CreateFolderLeft => Side(true, item.Relative),
            _ => From(item),
        };
    }
}
