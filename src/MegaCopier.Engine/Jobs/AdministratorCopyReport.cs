using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Jobs;

/// <summary>Resultado real del proceso elevado: evita volver a leer carpetas protegidas sin permisos.</summary>
public sealed class AdministratorCopyReport
{
    public string JobId { get; set; } = "";
    public int FilesCopied { get; set; }
    public int FilesSkipped { get; set; }
    public long BytesTransferred { get; set; }
    public long BytesResumed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public List<FileFailure> Failures { get; set; } = [];
    public List<AdministratorFileResult> Files { get; set; } = [];

    public static AdministratorCopyReport Create(string id, CopyEngine engine, CopyJobResult result) => new()
    {
        JobId = id, FilesCopied = result.FilesCopied, FilesSkipped = result.FilesSkipped,
        BytesTransferred = result.BytesTransferred, BytesResumed = result.BytesResumed, Elapsed = result.Elapsed,
        Failures = [.. result.Failures],
        Files = [.. engine.Files.Snapshot().Select(f => new AdministratorFileResult(f.Source, f.Destination, f.Length,
            f.LastWriteUtc, f.State, f.Message, f.ErrorKind, f.SourceHash, f.DestinationHash))],
    };

    /// <summary>Recupera los archivos en uso en el mismo proceso elevado.</summary>
    public void RecoverLockedFiles(ISnapshotProvider snapshots, CopyOptions options)
    {
        var request = new LockedFilesRequest { Files = [.. Files
            .Where(f => f.State == FileState.Failed && f.ErrorKind == FileErrorKind.InUse)
            .Select(f => new LockedFile(f.Source, f.Destination))] };
        if (request.Files.Count == 0) return;
        var recovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LockedFiles.Copy(request, snapshots, options, (file, engine, result) =>
        {
            var actual = engine.Files.Snapshot().Single();
            int index = Files.FindIndex(f => string.Equals(f.Source, file.Source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Destination, file.Destination, StringComparison.OrdinalIgnoreCase));
            Files[index] = new(file.Source, file.Destination, actual.Length, actual.LastWriteUtc,
                actual.State, actual.Message, actual.ErrorKind, actual.SourceHash, actual.DestinationHash);
            recovered.Add(file.Source);
            FilesCopied += result.FilesCopied;
            FilesSkipped += result.FilesSkipped;
            BytesTransferred += result.BytesTransferred;
            BytesResumed += result.BytesResumed;
            Elapsed += result.Elapsed;
        });
        Failures.RemoveAll(f => recovered.Contains(f.Source)
            && !Files.Any(row => string.Equals(row.Source, f.Source, StringComparison.OrdinalIgnoreCase) && row.State == FileState.Failed));
        for (int i = 0; i < Failures.Count; i++)
            if (request.Errors.TryGetValue(Failures[i].Source, out string? message))
                Failures[i] = Failures[i] with { Message = message };
    }

    public void Save(string path)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this, AdministratorReportJson.Default.AdministratorCopyReport));
        File.Move(temporary, path, overwrite: true);
    }

    public static AdministratorCopyReport? Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize(File.ReadAllBytes(path), AdministratorReportJson.Default.AdministratorCopyReport) : null;

    internal CopyJobResult ToResult()
    {
        var result = new CopyJobResult { FilesCopied = FilesCopied, FilesSkipped = FilesSkipped,
            BytesTransferred = BytesTransferred, BytesResumed = BytesResumed, Elapsed = Elapsed };
        result.Failures.AddRange(Failures);
        return result;
    }
}

public sealed record AdministratorFileResult(string Source, string Destination, long Length, DateTime LastWriteUtc,
    FileState State, string? Message, FileErrorKind ErrorKind, string? SourceHash, string? DestinationHash);

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(AdministratorCopyReport))]
internal partial class AdministratorReportJson : JsonSerializerContext;
