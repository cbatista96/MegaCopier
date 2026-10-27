using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Pestaña Estado: gráfica de velocidad, resumen por categorías (archivos y bytes), carpetas y unidades.</summary>
public sealed partial class StatusTab : UserControl
{
    public StatusTab()
    {
        InitializeComponent();
        // La gráfica ocupa el sitio que dejan las tablas (mínimo 140 px; si no cabe, se desplaza).
        SizeChanged += (_, _) => FitGraph();
        TableCard.SizeChanged += (_, _) => FitGraph();
        DrivesCard.SizeChanged += (_, _) => FitGraph();
    }

    private void FitGraph() => Graph.Height = Math.Max(140, ActualHeight - TableCard.ActualHeight - DrivesCard.ActualHeight - 24);

    public JobViewModel? Job { get; set; }

    /// <summary>Lo llama la ventana una vez por segundo mientras la pestaña está a la vista.</summary>
    public void Refresh()
    {
        if (Job is null)
        {
            return;
        }

        Graph.Update(Job.Job.SpeedHistory);
        var s = Job.Statistics();
        Set(CopiedFiles, CopiedBytes, s.CopiedFiles, s.CopiedBytes);
        Set(VerifiedFiles, VerifiedBytes, s.VerifiedFiles, s.VerifiedBytes);
        Set(SkippedFiles, SkippedBytes, s.SkippedFiles, s.SkippedBytes);
        Set(FailedFiles, FailedBytes, s.FailedFiles, s.FailedBytes);
        Set(PendingFiles, PendingBytes, s.PendingFiles, s.PendingBytes);
        Set(
            TotalFiles,
            TotalBytes,
            s.CopiedFiles + s.VerifiedFiles + s.SkippedFiles + s.FailedFiles + s.PendingFiles,
            s.CopiedBytes + s.VerifiedBytes + s.SkippedBytes + s.FailedBytes + s.PendingBytes);
        FolderCount.Text = Job.Job.Files?.FolderCount is int folders ? folders.ToString("N0") : "—"; // «—» hasta recorrer los orígenes
        ShowDrive(Job.SourceDrive, SourceDriveGlyph, SourceDriveName, SourceDriveInfo);
        ShowDrive(Job.DestinationDrive, TargetDriveGlyph, TargetDriveName, TargetDriveInfo);
    }

    /// <summary>«H: PNY» y debajo «USB · exFAT · 51.8 GB libres de 64 GB».</summary>
    private static void ShowDrive(DriveDescription drive, Controls.McIcon glyph, TextBlock name, TextBlock info)
    {
        glyph.Glyph = drive.Glyph.Length > 0 ? drive.Glyph : "";
        name.Text = drive.Text;
        var parts = new List<string>();
        if (drive.Kind.Length > 0)
        {
            parts.Add(drive.Kind);
        }

        if (drive.Format.Length > 0)
        {
            parts.Add(drive.Format);
        }

        if (drive.TotalBytes > 0)
        {
            parts.Add(L.F("Status_DriveFree", Format.Bytes(drive.FreeBytes), Format.Bytes(drive.TotalBytes)));
        }

        info.Text = string.Join(" · ", parts);
    }

    private static void Set(TextBlock files, TextBlock bytes, int fileCount, long byteCount)
    {
        files.Text = fileCount.ToString("N0");
        bytes.Text = Format.Bytes(byteCount);
        bytes.SetValue(ToolTipService.ToolTipProperty, L.F("Status_ExactBytes", byteCount.ToString("N0")));
    }
}
