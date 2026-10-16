namespace MegaCopier.Engine;

internal sealed record PlannedFile(string Source, string Destination, long Length, DateTime LastWriteUtc);

internal sealed record PlannedDirectory(string Source, string Destination, int Depth);

/// <summary>Un enlace simbólico o una unión de carpetas que se recrea en el destino apuntando al mismo sitio.</summary>
internal sealed record PlannedLink(string Source, string Destination, string Target, bool IsDirectory);

internal sealed record CopyPlan(
    IReadOnlyList<PlannedFile> Files,
    IReadOnlyList<PlannedDirectory> Directories,
    IReadOnlyList<PlannedLink> Links,
    IReadOnlyList<string> Warnings)
{
    public long TotalBytes { get; } = Files.Sum(f => f.Length);
}

/// <summary>
/// Convierte la lista de orígenes en la lista concreta de archivos, carpetas y enlaces a crear. Recorre las carpetas a
/// mano para decidir qué hacer con cada enlace: los "reparse points" que no son enlaces (archivos de OneDrive en la
/// nube, deduplicación...) se copian como archivos normales.
/// </summary>
internal static class CopyPlanner
{
    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    public static CopyPlan Build(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        FileFilter? filter = null,
        LinkHandling links = LinkHandling.CopyLink)
    {
        filter = filter is { IsEmpty: false } ? filter : null;
        if (sources.Count == 0)
        {
            throw new CopyPlanException("No se indicó ningún origen.");
        }

        string destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var walk = new Walk(filter, links);

        try
        {
            foreach (string raw in sources)
            {
                string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
                string name = Path.GetFileName(source);
                if (string.IsNullOrEmpty(name))
                {
                    throw new CopyPlanException($"Copiar una unidad completa todavía no está soportado: {raw}");
                }

                string target = Path.Combine(destinationRoot, name);
                FileSystemInfo info = Directory.Exists(source) ? new DirectoryInfo(source) : new FileInfo(source);
                if (!info.Exists && info.LinkTarget is null)
                {
                    throw new CopyPlanException($"No existe el origen: {raw}");
                }

                if (info is DirectoryInfo && IsSameOrInside(target, source))
                {
                    throw new CopyPlanException($"No se puede copiar una carpeta dentro de sí misma: {source}");
                }

                // Un origen elegido a mano se copia siempre, aunque los filtros no lo aceptaran.
                walk.Entry(info, target, relative: "", depth: 0, explicitSource: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CopyPlanException($"No se pudo leer el origen: {ex.Message}", ex);
        }

        return new CopyPlan(walk.Files, walk.Directories, walk.Links, walk.Warnings);
    }

    public static bool IsTransferArtifact(string fileName) =>
        fileName.EndsWith(FileTransfer.PartialSuffix, StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(FileTransfer.JournalSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed class Walk(FileFilter? filter, LinkHandling links)
    {
        /// <summary>
        /// Carpetas reales del camino actual: un enlace que apunta a una de ellas (su padre o un abuelo) haría un bucle
        /// infinito. Llegar a la misma carpeta por dos caminos distintos no es un bucle y se copia dos veces.
        /// </summary>
        private readonly HashSet<string> _ancestors = new(StringComparer.OrdinalIgnoreCase);

        public List<PlannedFile> Files { get; } = [];

        public List<PlannedDirectory> Directories { get; } = [];

        public List<PlannedLink> Links { get; } = [];

        public List<string> Warnings { get; } = [];

        public void Entry(FileSystemInfo info, string target, string relative, int depth, bool explicitSource = false)
        {
            bool isDirectory = info is DirectoryInfo;
            if (!explicitSource && filter is not null)
            {
                bool accepted = isDirectory
                    ? filter.AcceptsDirectory(relative, info.Attributes)
                    : filter.AcceptsFile(relative, (FileInfo)info);
                if (!accepted)
                {
                    return;
                }
            }

            if (!isDirectory && IsTransferArtifact(info.Name))
            {
                return;
            }

            // Solo los enlaces simbólicos y las uniones tienen destino; otros "reparse points" son archivos normales.
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is { } linkTarget)
            {
                switch (links)
                {
                    case LinkHandling.Skip:
                        Warnings.Add($"Enlace omitido: {info.FullName}");
                        return;
                    case LinkHandling.CopyLink:
                        Links.Add(new PlannedLink(info.FullName, target, linkTarget, isDirectory));
                        return;
                }

                // FollowTarget: se copia lo que hay detrás del enlace.
                if (!isDirectory)
                {
                    var resolved = new FileInfo(info.FullName).ResolveLinkTarget(returnFinalTarget: true) as FileInfo;
                    if (resolved is not { Exists: true })
                    {
                        Warnings.Add($"Enlace roto omitido: {info.FullName} → {linkTarget}");
                        return;
                    }

                    Files.Add(new PlannedFile(info.FullName, target, resolved.Length, resolved.LastWriteTimeUtc));
                    return;
                }
            }

            if (!isDirectory)
            {
                var file = (FileInfo)info;
                Files.Add(new PlannedFile(file.FullName, target, file.Length, file.LastWriteTimeUtc));
                return;
            }

            string real = RealPath((DirectoryInfo)info);
            if (!_ancestors.Add(real))
            {
                Warnings.Add($"Bucle de enlaces evitado: {info.FullName}");
                return;
            }

            Directories.Add(new PlannedDirectory(info.FullName, target, depth));
            foreach (var child in ((DirectoryInfo)info).EnumerateFileSystemInfos("*", OneLevel))
            {
                string childRelative = relative.Length == 0 ? child.Name : Path.Combine(relative, child.Name);
                Entry(child, Path.Combine(target, child.Name), childRelative, depth + 1);
            }

            _ancestors.Remove(real);
        }

        private static string RealPath(DirectoryInfo directory)
        {
            try
            {
                return directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
            }
            catch (IOException)
            {
                return directory.FullName;
            }
        }
    }
}
