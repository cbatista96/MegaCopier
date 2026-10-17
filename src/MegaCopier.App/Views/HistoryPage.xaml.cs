using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine.Ipc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Una fila del historial.</summary>
public sealed class HistoryRow(HistoryEntry entry, Action<HistoryRow> undo)
{
    public HistoryEntry Entry => entry;

    public override string ToString() => Title;

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
                ? L.F("Job_OneItem", Format.ItemName(entry.Sources[0]))
                : L.F("Job_ManyItems", entry.Sources.Count);
            string title = L.F(entry.Operation switch
            {
                Engine.JobOperation.Test => "Job_TitleTested",
                Engine.JobOperation.Delete => "Job_TitleDeleted",
                Engine.JobOperation.Hash => "Job_TitleHashed",
                _ => entry.Move ? "Job_TitleMoved" : "Job_TitleCopied",
            }, what);
            return entry.Undone ? $"{title} · {L.Get("History_Undone")}" : title;
        }
    }

    public string Route =>
        $"{Format.ShortPath(Path.GetDirectoryName(Format.Display(entry.Sources.FirstOrDefault() ?? "").TrimEnd('\\')) ?? "")} → {Format.ShortPath(entry.Destination)}";

    public string FullRoute => $"{string.Join("; ", entry.Sources.Select(Format.Display))} → {Format.Display(entry.Destination)}";

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

    public bool IsCopy => entry.Operation == Engine.JobOperation.Copy;

    public bool CanUndo => HistoryActions.CanUndo(entry);

    public void Undo() => undo(this);

    /// <summary>Lo copiado vuelve a la carpeta de donde salió (restaurar).</summary>
    public void CopyBack() => HistoryActions.Reverse(entry, move: false);

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
        var rows = History.Load().Select(e => new HistoryRow(e, row => _ = UndoAsync(row))).ToList();
        List.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Deshacer siempre pide confirmación y luego dice qué se hizo.</summary>
    private async Task UndoAsync(HistoryRow row)
    {
        var entry = row.Entry;
        UndoDialog.XamlRoot = XamlRoot;
        UndoDialog.RequestedTheme = ActualTheme;
        UndoMessage.Text = entry.Move ? L.F("History_UndoMoveConfirm", entry.Destination) : L.F("History_UndoCopyConfirm", entry.Files);
        if (await UndoDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = HistoryActions.Undo(entry);
        if (!entry.Move)
        {
            string text = L.F("History_UndoDone", result.Removed);
            if (result.Changed + result.Replaced + result.Missing > 0)
            {
                text += " " + L.F("History_UndoKept", result.Changed, result.Replaced);
            }

            ResultInfo.Message = text;
            ResultInfo.IsOpen = true;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => History.Clear();

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
