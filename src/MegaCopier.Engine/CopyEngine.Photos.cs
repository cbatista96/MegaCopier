using System.IO.Hashing;

namespace MegaCopier.Engine;

public sealed partial class CopyEngine
{
    private readonly Lock _duplicateLock = new();
    private Dictionary<long, List<string>>? _bySize;
    private readonly Dictionary<string, string> _contentHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Organizar por fecha: cada archivo va a "destino\año\año-mes" (o el patrón elegido) según la fecha en que se hizo
    /// (EXIF o grabación; si no hay, la de modificación). La estructura de carpetas del origen no se reproduce. Si dos
    /// archivos acabarían con el mismo nombre en la misma carpeta, el segundo se llama "nombre (2)".
    /// </summary>
    private CopyPlan OrganizeByDate(CopyPlan plan)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<PlannedFile>(plan.Files.Count);
        int withoutDate = 0;
        foreach (var file in plan.Files)
        {
            DateTime when = PhotoDate.Read(file.Source) ?? Count(ref withoutDate, file.LastWriteUtc.ToLocalTime());
            string folder = _options.OrganizePattern
                .Replace("{yyyy}", when.ToString("yyyy"), StringComparison.Ordinal)
                .Replace("{MM}", when.ToString("MM"), StringComparison.Ordinal)
                .Replace("{dd}", when.ToString("dd"), StringComparison.Ordinal);
            string target = Path.Combine(_destinationRoot, folder, Path.GetFileName(file.Source));
            for (int n = 2; !used.Add(target); n++)
            {
                target = Path.Combine(_destinationRoot, folder, $"{Path.GetFileNameWithoutExtension(file.Source)} ({n}){Path.GetExtension(file.Source)}");
            }

            files.Add(file with { Destination = target });
        }

        if (withoutDate > 0)
        {
            Log(LogLevel.Info, $"{withoutDate} archivos sin fecha de toma ni de grabación: se ordenan por su fecha de modificación.");
        }

        return new CopyPlan(files, [], [], plan.Warnings);

        static DateTime Count(ref int counter, DateTime date)
        {
            counter++;
            return date;
        }
    }

    /// <summary>
    /// ¿Ya está este contenido en el destino (con cualquier nombre, en cualquier carpeta)? Primero se compara el tamaño
    /// (un único recorrido del destino) y solo si coincide se calcula el hash de los dos.
    /// </summary>
    private string? FindDuplicate(TransferItem item)
    {
        if (item.Length == 0)
        {
            return null;
        }

        List<string> candidates;
        lock (_duplicateLock)
        {
            _bySize ??= IndexDestination();
            if (!_bySize.TryGetValue(item.Length, out var list))
            {
                return null;
            }

            candidates = [.. list];
        }

        string? sourceHash = null;
        foreach (string candidate in candidates)
        {
            if (string.Equals(candidate, item.Destination, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            {
                continue; // el mismo destino lo decide la regla de conflictos
            }

            sourceHash ??= ContentHash(item.Source);
            if (sourceHash is not null && sourceHash == ContentHash(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Lo recién copiado también cuenta: dos copias del mismo archivo en el origen se copian una vez.</summary>
    private void RememberForDuplicates(TransferItem item, string target)
    {
        if (!_options.SkipDuplicates || item.Length == 0)
        {
            return;
        }

        lock (_duplicateLock)
        {
            _bySize ??= IndexDestination();
            if (!_bySize.TryGetValue(item.Length, out var list))
            {
                _bySize[item.Length] = list = [];
            }

            list.Add(target);
        }
    }

    private Dictionary<long, List<string>> IndexDestination()
    {
        var index = new Dictionary<long, List<string>>();
        if (!Directory.Exists(_destinationRoot))
        {
            return index;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in new DirectoryInfo(_destinationRoot).EnumerateFiles("*", options))
        {
            if (file.Length == 0 || file.Name.EndsWith(FileTransfer.PartialSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!index.TryGetValue(file.Length, out var list))
            {
                index[file.Length] = list = [];
            }

            list.Add(file.FullName);
        }

        return index;
    }

    private string? ContentHash(string path)
    {
        lock (_contentHashes)
        {
            if (_contentHashes.TryGetValue(path, out string? known))
            {
                return known;
            }
        }

        try
        {
            var hash = new XxHash128();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                hash.Append(stream);
            }

            string value = Convert.ToHexString(hash.GetCurrentHash());
            lock (_contentHashes)
            {
                _contentHashes[path] = value;
            }

            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
