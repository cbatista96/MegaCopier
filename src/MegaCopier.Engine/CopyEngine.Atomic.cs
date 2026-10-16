namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    /// <summary>Sufijo de la carpeta temporal de "todo o nada".</summary>
    public const string StagingSuffix = ".megacopier-parcial";

    /// <summary>
    /// "Todo o nada": cada carpeta de primer nivel se copia en una carpeta oculta ("nombre.megacopier-parcial") y solo
    /// toma su nombre final cuando todo salió bien; nunca aparece una carpeta a medias que parezca completa. Al reanudar
    /// se sigue en la misma carpeta oculta. Solo para carpetas nuevas (no se puede reemplazar una carpeta de golpe).
    /// </summary>
    private (CopyPlan Plan, List<(string Staging, string Final)> Staged) StageFolders(CopyJobRequest request, CopyPlan plan)
    {
        var staged = new List<(string Staging, string Final)>();
        if (!_options.AllOrNothing || _options.Move || _options.DryRun || _options.Mode != CopyMode.Copy)
        {
            return (plan, staged);
        }

        foreach (string raw in request.Sources)
        {
            string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            string final = Path.Combine(_destinationRoot, Path.GetFileName(source));
            if (!Directory.Exists(source))
            {
                continue;
            }

            if (Directory.Exists(final) || File.Exists(final))
            {
                Log(LogLevel.Warning, "Ya existe en el destino: se copia dentro de la carpeta existente (sin «todo o nada»).", source);
                continue;
            }

            staged.Add((final + StagingSuffix, final));
        }

        if (staged.Count == 0)
        {
            return (plan, staged);
        }

        string Map(string path)
        {
            foreach (var (staging, final) in staged)
            {
                if (string.Equals(path, final, StringComparison.OrdinalIgnoreCase))
                {
                    return staging;
                }

                if (path.StartsWith(final + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return staging + path[final.Length..];
                }
            }

            return path;
        }

        foreach (var (staging, _) in staged)
        {
            var folder = Directory.CreateDirectory(staging);
            folder.Attributes |= FileAttributes.Hidden;
        }

        return (new CopyPlan(
            [.. plan.Files.Select(f => f with { Destination = Map(f.Destination) })],
            [.. plan.Directories.Select(d => d with { Destination = Map(d.Destination) })],
            [.. plan.Links.Select(l => l with { Destination = Map(l.Destination) })],
            plan.Warnings), staged);
    }

    /// <summary>Si todo salió bien, cada carpeta oculta pasa a su nombre final de una vez.</summary>
    private void PublishStaged(List<(string Staging, string Final)> staged, CopyJobResult result)
    {
        foreach (var (staging, final) in staged)
        {
            if (result.Failures.Count > 0)
            {
                Log(LogLevel.Warning, $"Hubo errores: la carpeta no se publica todavía (sigue en {Path.GetFileName(staging)}, oculta). Reintenta y aparecerá completa.", final);
                continue;
            }

            try
            {
                var folder = new DirectoryInfo(staging);
                folder.Attributes &= ~FileAttributes.Hidden;
                folder.MoveTo(final);
                Log(LogLevel.Info, "Carpeta completa: publicada con su nombre.", final);
            }
            catch (IOException ex)
            {
                result.Failures.Add(new FileFailure(staging, $"No se pudo dar a la carpeta su nombre final: {ex.Message}"));
            }
        }
    }
}
