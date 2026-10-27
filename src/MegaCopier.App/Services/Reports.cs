using System.Net;
using System.Text;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;

namespace MegaCopier.App.Services;

/// <summary>Listas de copia (.mclist) e informes (HTML para leer, CSV para hojas de cálculo).</summary>
internal static class Reports
{
    public static void SaveList(CopyJobRecord record, string path) =>
        File.WriteAllText(path, record.ToJson(), new UTF8Encoding(false));

    /// <summary>
    /// Guarda lo que le queda a una copia (pendientes, el que se estaba copiando y los fallidos) con su destino, sus
    /// opciones y el orden de la lista. Lo ya copiado u omitido va a «quitados», así que al cargarla se sigue justo donde
    /// estaba. Devuelve cuántos archivos quedan.
    /// </summary>
    public static int SaveRemaining(CopyJob job, string path)
    {
        var record = CopyJobRecord.FromJson(job.Record.ToJson())!;
        var items = job.Files?.Snapshot() ?? [];
        int remaining = items.Count;
        if (items.Count > 0)
        {
            var left = items.Where(i => i.State is FileState.Pending or FileState.Copying or FileState.Verifying or FileState.Failed).ToList();
            var done = items.Except(left).Select(i => i.Source);
            record.Excluded = [.. record.Excluded.Union(done, StringComparer.OrdinalIgnoreCase)];
            record.FileOrder = [.. left.Select(i => i.Source)];
            remaining = left.Count;
        }

        record.Started = false;
        record.StartAtUtc = null;
        File.WriteAllText(path, record.ToJson(), new UTF8Encoding(false));
        return remaining;
    }

    /// <summary>Nombre de archivo sugerido a partir del título de una copia.</summary>
    public static string FileNameFor(string title) =>
        string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

    public static CopyJobRecord? LoadList(string path)
    {
        try
        {
            // Si un disco de la lista volvió con otra letra, sus rutas se pasan a la nueva (se reconoce por su número de serie).
            var record = CopyJobRecord.FromJson(File.ReadAllText(path));
            if (record is { Volumes.Count: > 0 })
            {
                Engine.Jobs.Volumes.Remap(record);
            }

            return record;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void ExportCsv(JobViewModel job, string path)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Join(';', L.Get("Report_State"), L.Get("Report_Source"), L.Get("Report_Destination"), "Bytes",
            L.Get("Report_SourceHash"), L.Get("Report_TargetHash"), L.Get("Report_Message")));
        foreach (var item in job.Job.Files?.Snapshot() ?? [])
        {
            text.AppendLine(string.Join(';', StateName(item.State), Csv(item.Source), Csv(item.Destination), item.Length,
                item.SourceHash ?? "", item.DestinationHash ?? "", Csv(MessageText(item))));
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true)); // con BOM: Excel lo abre bien
    }

    public static void ExportHtml(JobViewModel job, string path)
    {
        var items = job.Job.Files?.Snapshot() ?? [];
        var stats = TransferStatistics.From(items);
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html><head><meta charset="utf-8"><title>MegaCopier</title>
            <style>
            body{font:14px "Segoe UI",system-ui,sans-serif;margin:32px;color:#1b1f24;background:#f6f7fb}
            h1{font-size:22px;margin:0 0 4px} .sub{color:#5b6472;margin-bottom:20px}
            .cards{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:20px}
            .card{background:#fff;border:1px solid #e3e6ee;border-radius:10px;padding:12px 16px;min-width:140px}
            .card b{display:block;font-size:20px} table{border-collapse:collapse;width:100%;background:#fff;border-radius:10px;overflow:hidden}
            th,td{padding:8px 10px;border-bottom:1px solid #eef0f5;text-align:left;font-size:13px} th{background:#eef1f8}
            td.mono{font-family:Consolas,monospace} .Verified,.Copied{color:#1a7f37} .Failed{color:#cf222e} .Skipped{color:#6e7781}
            </style></head><body>
            """);
        html.Append($"<h1>{Enc(job.Title)}</h1><div class=\"sub\">{Enc(job.SourceFolder)} → {Enc(job.DestinationFolder)} · {DateTime.Now:g}</div>");
        html.Append("<div class=\"cards\">");
        Card(html, L.Get("Report_Copied"), stats.CopiedFiles, stats.CopiedBytes);
        Card(html, L.Get("Report_Verified"), stats.VerifiedFiles, stats.VerifiedBytes);
        Card(html, L.Get("Report_Skipped"), stats.SkippedFiles, stats.SkippedBytes);
        Card(html, L.Get("Report_Failed"), stats.FailedFiles, stats.FailedBytes);
        html.Append($"</div><table><tr><th>{L.Get("Report_State")}</th><th>{L.Get("Report_File")}</th><th>{L.Get("Report_Size")}</th><th>{L.Get("Report_SourceHash")}</th><th>{L.Get("Report_TargetHash")}</th><th>{L.Get("Report_Message")}</th></tr>");
        foreach (var item in items)
        {
            html.Append($"<tr><td class=\"{item.State}\">{StateName(item.State)}</td><td>{Enc(item.Source)}</td><td>{Format.Bytes(item.Length)}</td>")
                .Append($"<td class=\"mono\">{Enc(item.SourceHash ?? "")}</td><td class=\"mono\">{Enc(item.DestinationHash ?? "")}</td><td>{Enc(MessageText(item))}</td></tr>");
        }

        html.Append("</table></body></html>");
        File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
    }

    private static void Card(StringBuilder html, string title, int files, long bytes) =>
        html.Append($"<div class=\"card\">{title}<b>{files:N0}</b>{Format.Bytes(bytes)}</div>");

    private static string StateName(FileState state) => L.Get("File_" + state);

    private static string MessageText(TransferItem item) =>
        item.ErrorKind == FileErrorKind.Other ? item.Message ?? "" : $"{L.Get("Error_" + item.ErrorKind)} ({item.Message})";

    private static string Enc(string text) => WebUtility.HtmlEncode(text);

    private static string Csv(string text) => text.Contains(';') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
