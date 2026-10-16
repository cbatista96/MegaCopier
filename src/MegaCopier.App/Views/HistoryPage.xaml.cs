using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine.Ipc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Una fila del historial.</summary>
public sealed class HistoryRow(HistoryEntry entry)
{
    public string Glyph => entry.State switch
    {
        "Completed" => "",
        "Interrupted" => "",
        _ => "",
    };

    public Microsoft.UI.Xaml.Media.Brush StateBrush => entry.State switch
    {
        "Completed" => Theming.ThemeManager.SuccessBrush,
        "Failed" => Theming.ThemeManager.ErrorBrush,
        _ => Theming.ThemeManager.WarningBrush,
    };

    public string Title
    {
        get
        {
            string what = entry.Sources.Count == 1
                ? L.F("Job_OneItem", Path.GetFileName(entry.Sources[0].TrimEnd('\\')))
                : L.F("Job_ManyItems", entry.Sources.Count);
            return L.F(entry.Move ? "Job_TitleMoved" : "Job_TitleCopied", what);
        }
    }

    public string Route =>
        $"{Format.ShortPath(Path.GetDirectoryName(entry.Sources.FirstOrDefault()?.TrimEnd('\\') ?? "") ?? "")} → {Format.ShortPath(entry.Destination)}";

    public string FullRoute => $"{string.Join("; ", entry.Sources)} → {entry.Destination}";

    public string Summary =>
        $"{entry.Finished:g} · {L.F((entry.Move ? "Job_FilesMoved" : "Job_FilesCopied") + (entry.Files == 1 ? "One" : "Many"), entry.Files)} · {Format.Bytes(entry.Bytes)} · {Format.Duration(TimeSpan.FromSeconds(entry.Seconds))}"
        + (entry.Skipped > 0 ? " · " + (entry.Skipped == 1 ? L.Get("Job_SkippedOne") : L.F("Job_Skipped", entry.Skipped)) : "")
        + (entry.Failed > 0 ? " · " + (entry.Failed == 1 ? L.Get("Job_FailedOne") : L.F("Job_FailedFiles", entry.Failed)) : "");

    /// <summary>Vuelve a lanzar la misma copia (lo idéntico se omite: solo copia lo nuevo o lo que cambió).</summary>
    public void Repeat() => ((App)Application.Current).Submit(new IpcRequest
    {
        Command = "copy",
        Sources = [.. entry.Sources],
        Destination = entry.Destination,
        Move = entry.Move ? true : null,
    });

    public void OpenDestination()
    {
        if (Directory.Exists(entry.Destination))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.Destination}\"") { UseShellExecute = true });
        }
    }
}

/// <summary>Página Historial: los trabajos terminados, con "Repetir" y "Abrir destino".</summary>
public sealed partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
        History.Changed += () => DispatcherQueue.TryEnqueue(Reload);
    }

    public event Action? CloseRequested;

    public void Reload()
    {
        var rows = History.Load().Select(e => new HistoryRow(e)).ToList();
        List.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => History.Clear();

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
