using System.Diagnostics;
using MegaCopier.Engine.IO;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>
    /// Al terminar una copia con <see cref="CopyOptions.RecoveryPercent"/>: se crean los datos de recuperación de lo
    /// copiado (en &lt;destino&gt;\.megacopier-recuperacion), leyendo cada archivo ya escrito.
    /// </summary>
    private void CreateRecoveryData(ProgressTracker progress, CancellationToken cancellationToken)
    {
        if (_options.RecoveryPercent <= 0 || _options.DryRun || _options.Zip)
        {
            return;
        }

        var done = Files.Snapshot().Where(i => i.State is FileState.Copied or FileState.Verified && i.Length > 0 && File.Exists(i.Destination)).ToList();
        if (done.Count == 0)
        {
            return;
        }

        progress.SetPhase(CopyPhase.Verifying);
        int created = 0;
        foreach (var item in done)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RecoveryData.Create(item.Destination, RecoveryData.PathFor(_destinationRoot, item.Destination), _options.RecoveryPercent, null, cancellationToken);
                created++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log(LogLevel.Warning, Texts.F("E_RecoveryFailed", ex.Message), item.Destination);
            }
        }

        var folder = new DirectoryInfo(Path.Combine(_destinationRoot, RecoveryData.Folder));
        if (folder.Exists)
        {
            folder.Attributes |= FileAttributes.Hidden;
        }

        progress.SetPhase(CopyPhase.Copying);
        Log(LogLevel.Info, Texts.F("E_RecoveryCreated", created, _options.RecoveryPercent));
    }

    /// <summary>
    /// Reparar: revisa los archivos que tienen datos de recuperación y reconstruye los bloques dañados o ilegibles.
    /// </summary>
    private CopyJobResult RunRepair(CopyJobRequest request, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var result = new CopyJobResult();
        var recoveries = new Dictionary<int, string>();
        foreach (string source in request.Sources)
        {
            string selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            string root = RecoveryData.FindRoot(selected) ?? throw new CopyPlanException(Texts.F("E_NoRecoveryData", selected));
            string folder = Path.Combine(root, RecoveryData.Folder);
            foreach (string recovery in Directory.EnumerateFiles(folder, "*" + RecoveryData.Extension, SearchOption.AllDirectories))
            {
                string file = Path.Combine(root, Path.GetRelativePath(folder, recovery)[..^RecoveryData.Extension.Length]);
                bool inside = string.Equals(file, selected, StringComparison.OrdinalIgnoreCase)
                    || file.StartsWith(selected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(selected, root, StringComparison.OrdinalIgnoreCase);
                if (inside && File.Exists(file))
                {
                    var info = new FileInfo(file);
                    recoveries[Files.Add(file, file, info.Length, info.LastWriteTimeUtc).Id] = recovery;
                }
            }
        }

        if (recoveries.Count == 0)
        {
            throw new CopyPlanException(Texts.F("E_NoRecoveryData", string.Join(", ", request.Sources)));
        }

        var progress = new ProgressTracker(p => ProgressChanged?.Invoke(p), _options.ProgressInterval);
        var (totalBytes, totalFiles) = Files.Totals();
        progress.Start(totalBytes, totalFiles);
        progress.SetPhase(CopyPhase.Verifying);
        _progress = progress;
        while (Files.TakeNext() is { } item)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controller.WaitIfPaused(cancellationToken);
            progress.BeginFile(item.Source, item.Length);
            try
            {
                var (outcome, blocks) = RecoveryData.Repair(item.Source, recoveries[item.Id], read => progress.AddTransferred(read), cancellationToken);
                switch (outcome)
                {
                    case RepairOutcome.Intact:
                        result.FilesSkipped++;
                        Files.Update(item, FileState.Verified, Texts.Get("E_RepairIntact"));
                        break;
                    case RepairOutcome.Repaired:
                        result.FilesCopied++;
                        result.BytesTransferred += item.Length;
                        Files.Update(item, FileState.Copied, Texts.F("E_RepairDone", blocks));
                        Log(LogLevel.Warning, Texts.F("E_RepairDone", blocks), item.Source);
                        break;
                    case RepairOutcome.Changed:
                        result.FilesSkipped++;
                        Files.Update(item, FileState.Skipped, Texts.Get("E_RepairChanged"));
                        break;
                    default:
                        Fail(item, result, Texts.Get("E_RepairImpossible"), FileErrorKind.ChecksumMismatch);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Fail(item, result, ex.Message, FileErrors.Classify(ex));
            }

            progress.EndFile();
        }

        progress.Finish();
        result.Elapsed = stopwatch.Elapsed;
        Log(result.Success ? LogLevel.Info : LogLevel.Warning, Texts.F("E_RepairSummary", result.FilesCopied, result.FilesSkipped, result.Failures.Count));
        return result;
    }
}
