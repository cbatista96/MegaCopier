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

    public static CopyJobRecord? LoadList(string path)
    {
        try
        {
            return CopyJobRecord.FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void ExportCsv(JobViewModel job, string path)
    {
        var text = new StringBuilder();
        text.AppendLine("Estado;Origen;Destino;Bytes;Hash origen;Hash destino;Mensaje");
        foreach (var item in job.Job.Files?.Snapshot() ?? [])
        {
            text.AppendLine(string.Join(';', item.State, Csv(item.Source), Csv(item.Destination), item.Length,
                item.SourceHash ?? "", item.DestinationHash ?? "", Csv(item.Message ?? "")));
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true)); // con BOM: Excel lo abre bien
    }

    public static void ExportHtml(JobViewModel job, string path)
    {
        var items = job.Job.Files?.Snapshot() ?? [];
        var stats = TransferStatistics.From(items);
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="es"><head><meta charset="utf-8"><title>MegaCopier</title>
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
        Card(html, "Copiados", stats.CopiedFiles, stats.CopiedBytes);
        Card(html, "Verificados", stats.VerifiedFiles, stats.VerifiedBytes);
        Card(html, "Omitidos", stats.SkippedFiles, stats.SkippedBytes);
        Card(html, "Con errores", stats.FailedFiles, stats.FailedBytes);
        html.Append("</div><table><tr><th>Estado</th><th>Archivo</th><th>Tamaño</th><th>Hash origen</th><th>Hash destino</th><th>Mensaje</th></tr>");
        foreach (var item in items)
        {
            html.Append($"<tr><td class=\"{item.State}\">{item.State}</td><td>{Enc(item.Source)}</td><td>{Format.Bytes(item.Length)}</td>")
                .Append($"<td class=\"mono\">{Enc(item.SourceHash ?? "")}</td><td class=\"mono\">{Enc(item.DestinationHash ?? "")}</td><td>{Enc(item.Message ?? "")}</td></tr>");
        }

        html.Append("</table></body></html>");
        File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
    }

    private static void Card(StringBuilder html, string title, int files, long bytes) =>
        html.Append($"<div class=\"card\">{title}<b>{files:N0}</b>{Format.Bytes(bytes)}</div>");

    private static string Enc(string text) => WebUtility.HtmlEncode(text);

    private static string Csv(string text) => text.Contains(';') || text.Contains('"') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
}
