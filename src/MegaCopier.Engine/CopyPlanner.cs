namespace MegaCopier.Engine;

internal sealed record PlannedFile(string Source, string Destination, long Length, DateTime LastWriteUtc);

internal sealed record PlannedDirectory(string Source, string Destination, int Depth);

internal sealed record CopyPlan(IReadOnlyList<PlannedFile> Files, IReadOnlyList<PlannedDirectory> Directories)
{
    public long TotalBytes { get; } = Files.Sum(f => f.Length);
}

/// <summary>Convierte la lista de orígenes en la lista concreta de archivos y carpetas a crear.</summary>
internal static class CopyPlanner
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // no sigue enlaces/uniones: evita bucles infinitos
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    public static CopyPlan Build(IReadOnlyList<string> sources, string destinationDirectory)
    {
        if (sources.Count == 0)
        {
            throw new CopyPlanException("No se indicó ningún origen.");
        }

        string destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        var files = new List<PlannedFile>();
        var directories = new List<PlannedDirectory>();

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

                if (File.Exists(source))
                {
                    var info = new FileInfo(source);
                    files.Add(new PlannedFile(source, target, info.Length, info.LastWriteTimeUtc));
                }
                else if (Directory.Exists(source))
                {
                    if (IsSameOrInside(target, source))
                    {
                        throw new CopyPlanException($"No se puede copiar una carpeta dentro de sí misma: {source}");
                    }

                    directories.Add(new PlannedDirectory(source, target, 0));
                    var root = new DirectoryInfo(source);

                    foreach (var directory in root.EnumerateDirectories("*", Recursive))
                    {
                        string relative = Path.GetRelativePath(source, directory.FullName);
                        directories.Add(new PlannedDirectory(directory.FullName, Path.Combine(target, relative), Depth(relative)));
                    }

                    foreach (var file in root.EnumerateFiles("*", Recursive))
                    {
                        if (IsTransferArtifact(file.Name))
                        {
                            continue;
                        }

                        string relative = Path.GetRelativePath(source, file.FullName);
                        files.Add(new PlannedFile(file.FullName, Path.Combine(target, relative), file.Length, file.LastWriteTimeUtc));
                    }
                }
                else
                {
                    throw new CopyPlanException($"No existe el origen: {raw}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CopyPlanException($"No se pudo leer el origen: {ex.Message}", ex);
        }

        return new CopyPlan(files, directories);
    }

    public static bool IsTransferArtifact(string fileName) =>
        fileName.EndsWith(FileTransfer.PartialSuffix, StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(FileTransfer.JournalSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static int Depth(string relative) => relative.Count(c => c == Path.DirectorySeparatorChar) + 1;
}
