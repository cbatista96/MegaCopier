using System.Diagnostics;

namespace MegaCopier.Engine;

/// <summary>
/// Las opciones de <see cref="CopyExtras"/>: comparar fechas con el cambio de horario, la orden previa, aplanar, renombrar
/// con máscara, conservar la ruta común y, al terminar, quitar la marca «Archivo» y conservar la compresión NTFS.
/// </summary>
public sealed partial class CopyEngine
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>Misma fecha, dentro de la tolerancia (y, con «compensar el cambio de hora», también a una hora justa).</summary>
    internal bool SameTime(DateTime a, DateTime b)
    {
        var difference = (a - b).Duration();
        return difference <= _options.TimeTolerance
            || (_options.Extras.CompensateDst && (difference - OneHour).Duration() <= _options.TimeTolerance);
    }

    /// <summary>El origen es más reciente que lo que hay (sin contar una hora justa si se compensa el cambio de horario).</summary>
    internal bool IsNewer(DateTime source, DateTime existing) =>
        source > existing + _options.TimeTolerance && !SameTime(source, existing);

    /// <summary>
    /// La orden de «Ejecutar antes de empezar» (parar un servicio, cerrar una base de datos). Si falla o tarda más de
    /// media hora, la copia no empieza: copiar con el programa abierto daría una copia rota.
    /// </summary>
    private void RunBeforeCommand(CancellationToken cancellationToken)
    {
        string command = _options.Extras.BeforeCommand.Trim();
        if (command.Length == 0)
        {
            return;
        }

        Log(LogLevel.Info, Texts.F("E_BeforeRunning", command));
        int exit;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c {command}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;
            using (cancellationToken.Register(() =>
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }))
            {
                if (!process.WaitForExit(TimeSpan.FromMinutes(30)))
                {
                    process.Kill(entireProcessTree: true);
                    throw new CopyPlanException(Texts.F("E_BeforeFailed", command, "timeout"));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            exit = process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CopyPlanException(Texts.F("E_BeforeFailed", command, ex.Message), ex);
        }

        if (exit != 0)
        {
            throw new CopyPlanException(Texts.F("E_BeforeFailed", command, exit));
        }
    }

    /// <summary>Aplanar, conservar la ruta común y renombrar con máscara: cambian el destino de cada archivo del plan.</summary>
    private CopyPlan ApplyExtras(CopyPlan plan, string destination, IReadOnlyList<string> sources)
    {
        var extras = _options.Extras;
        if (!extras.Flatten && !extras.KeepRelativePaths && extras.RenameMask.Trim().Length == 0)
        {
            return plan;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        Func<string, string> move = path => path;
        if (extras.KeepRelativePaths && !extras.Flatten && CommonParent(sources) is { } common)
        {
            // Cada origen va a su ruta desde la carpeta común, en lugar de directamente al destino.
            var map = sources
                .Select(s => Path.TrimEndingDirectorySeparator(Path.GetFullPath(s)))
                .Where(s => !CopyPlanner.IsDriveRoot(s))
                .Select(s => (Old: Path.Combine(root, SourceNames.TargetName(s)), New: Path.Combine(root, Path.GetRelativePath(common, s))))
                .Where(m => !string.Equals(m.Old, m.New, StringComparison.OrdinalIgnoreCase))
                .ToList();
            move = path =>
            {
                foreach (var (old, now) in map)
                {
                    if (string.Equals(path, old, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(old + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return now + path[old.Length..];
                    }
                }

                return path;
            };
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limits = DestinationNames.Limits.For(root);
        var files = new List<PlannedFile>(plan.Files.Count);
        foreach (var file in plan.Files)
        {
            string target = move(file.Destination);
            string name = DestinationNames.Fix(CopyExtras.ApplyMask(Path.GetFileName(target), extras.RenameMask), limits);
            string folder = extras.Flatten ? root : Path.GetDirectoryName(target)!;
            string candidate = Path.Combine(folder, name);
            for (int number = 2; !taken.Add(candidate); number++)
            {
                // Al aplanar (o con una máscara que da el mismo nombre), los repetidos se numeran.
                candidate = Path.Combine(folder, DestinationNames.Numbered(name, number, limits));
            }

            files.Add(file with { Destination = candidate });
        }

        List<PlannedDirectory> directories = extras.Flatten ? [] : plan.Directories.Select(d => d with { Destination = move(d.Destination) }).ToList();
        List<PlannedLink> links = extras.Flatten ? [] : plan.Links.Select(l => l with { Destination = move(l.Destination) }).ToList();
        return new CopyPlan(files, directories, links, plan.Warnings);
    }

    /// <summary>La carpeta que contiene a todos los orígenes (si están en carpetas distintas del mismo disco).</summary>
    internal static string? CommonParent(IReadOnlyList<string> sources)
    {
        var parents = sources
            .Select(s => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(s))))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (parents.Count < 2)
        {
            return null; // todos en la misma carpeta: ya llegan juntos
        }

        string common = parents[0];
        foreach (string parent in parents.Skip(1))
        {
            while (!string.Equals(parent, common, StringComparison.OrdinalIgnoreCase)
                && !parent.StartsWith(Path.EndsInDirectorySeparator(common) ? common : common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (Path.GetDirectoryName(common) is not { } up)
                {
                    return null; // discos distintos
                }

                common = up;
            }
        }

        return common;
    }

    /// <summary>Al terminar: quitar la marca «Archivo» de lo copiado y comprimir en el destino lo que estaba comprimido.</summary>
    private void FinishExtras()
    {
        var extras = _options.Extras;
        if (!(extras.ResetArchive && !_options.Move && !_options.DryRun) && !extras.KeepNtfsCompression)
        {
            return;
        }

        foreach (var item in Files.Snapshot().Where(i => i.State is FileState.Copied or FileState.Verified))
        {
            try
            {
                var attributes = File.GetAttributes(item.Source);
                if (extras.KeepNtfsCompression && attributes.HasFlag(FileAttributes.Compressed) && File.Exists(item.Destination)
                    && !File.GetAttributes(item.Destination).HasFlag(FileAttributes.Compressed))
                {
                    Native.Win32.Compress(item.Destination);
                }

                if (extras.ResetArchive && !_options.Move && attributes.HasFlag(FileAttributes.Archive))
                {
                    File.SetAttributes(item.Source, attributes & ~FileAttributes.Archive);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log(LogLevel.Warning, ex.Message, item.Source);
            }
        }
    }
}
