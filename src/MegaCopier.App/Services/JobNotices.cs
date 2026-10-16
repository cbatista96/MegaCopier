using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Lo que queda de cada copia al terminar: el registro en disco y el aviso a una dirección web.</summary>
internal static class JobNotices
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string LogFolder { get; } = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "logs");

    /// <summary>Guarda el registro de la copia en "logs" y borra los de más de <paramref name="retentionDays"/> días.</summary>
    public static string? SaveLog(CopyJob job, int retentionDays)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            string path = Path.Combine(LogFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{job.Id[..Math.Min(8, job.Id.Length)]}.log");
            var text = new StringBuilder();
            text.AppendLine($"MegaCopier · {job.Record.Operation} · {job.State}");
            text.AppendLine($"{string.Join("; ", job.Record.Sources)} -> {job.Record.Destination}");
            if (job.Result is { } result)
            {
                text.AppendLine($"Copiados: {result.FilesCopied}  Omitidos: {result.FilesSkipped}  Errores: {result.Failures.Count}  Bytes: {result.BytesTransferred}  Tiempo: {Format.Duration(result.Elapsed)}");
            }

            text.AppendLine();
            foreach (var entry in job.LogSnapshot())
            {
                text.AppendLine($"{entry.Time:yyyy-MM-dd HH:mm:ss}\t{entry.Level}\t{entry.Message}{(entry.Path is null ? "" : "\t" + entry.Path)}");
            }

            foreach (var failure in job.Result?.Failures ?? [])
            {
                text.AppendLine($"ERROR\t{failure.Kind}\t{failure.Source}\t{failure.Message}");
            }

            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            Prune(retentionDays);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Registro no guardado: {ex.Message}");
            return null;
        }
    }

    private static void Prune(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return;
        }

        var limit = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in new DirectoryInfo(LogFolder).EnumerateFiles("*.log").Where(f => f.LastWriteTime < limit))
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Avisa a una dirección web con un POST JSON. Lleva el texto en "text" (Slack, Teams, Mattermost), "content"
    /// (Discord) y "message", más los datos de la copia, así funciona con los servicios habituales sin configurar nada.
    /// ntfy.sh recibe el texto tal cual.
    /// </summary>
    public static async Task<bool> SendWebhookAsync(string url, string text, CopyJob? job)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return false;
        }

        try
        {
            HttpContent content;
            if (uri.Host.EndsWith("ntfy.sh", StringComparison.OrdinalIgnoreCase))
            {
                content = new StringContent(text, Encoding.UTF8, "text/plain");
            }
            else
            {
                using var buffer = new MemoryStream();
                using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    json.WriteStartObject();
                    json.WriteString("text", text);
                    json.WriteString("content", text);
                    json.WriteString("message", text);
                    if (job is not null)
                    {
                        json.WriteString("state", job.State.ToString());
                        json.WriteString("destination", job.Record.Destination);
                        json.WriteStartArray("sources");
                        foreach (string source in job.Record.Sources)
                        {
                            json.WriteStringValue(source);
                        }

                        json.WriteEndArray();
                        json.WriteNumber("files", job.Result?.FilesCopied ?? 0);
                        json.WriteNumber("bytes", job.Result?.BytesTransferred ?? 0);
                        json.WriteNumber("failed", job.Result?.Failures.Count ?? 0);
                        json.WriteNumber("seconds", Math.Round(job.Result?.Elapsed.TotalSeconds ?? 0, 1));
                    }

                    json.WriteEndObject();
                }

                content = new ByteArrayContent(buffer.ToArray());
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }

            using (content)
            using (var response = await Http.PostAsync(uri, content))
            {
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Debug.WriteLine($"Aviso web fallido: {ex.Message}");
            return false;
        }
    }

    /// <summary>Texto del aviso: "✔ Copiado «Fotos» → D:\Copia · 1.234 archivos · 12,3 GB en 4 min".</summary>
    public static string Summary(CopyJob job)
    {
        string what = job.Record.Sources.Count == 1
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(job.Record.Sources[0]))
            : L.F("Job_ManyItems", job.Record.Sources.Count);
        string icon = job.State == CopyJobState.Completed ? "✔" : "⚠";
        string state = L.Get(job.State switch
        {
            CopyJobState.Completed => "Job_StateCompleted",
            CopyJobState.CompletedWithErrors => "Job_StateCompletedErrors",
            _ => "Job_StateFailed",
        });
        var text = new StringBuilder($"{icon} MegaCopier · {state}: {what} → {job.Record.Destination}");
        if (job.Result is { } result)
        {
            text.Append($" · {L.F(result.FilesCopied == 1 ? "Job_FilesCopiedOne" : "Job_FilesCopiedMany", result.FilesCopied)} · {Format.Bytes(result.BytesTransferred)} · {Format.Duration(result.Elapsed)}");
            if (result.Failures.Count > 0)
            {
                text.Append($" · {L.F("Job_FailedFiles", result.Failures.Count)}");
            }
        }

        return text.ToString();
    }
}
