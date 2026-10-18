using System.Diagnostics;
using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Pestaña Destino: unidades de origen y destino (nombre, tipo, sistema de archivos y espacio).</summary>
public sealed partial class TargetTab : UserControl
{
    public TargetTab()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            // Origen y destino lado a lado si caben; si no, uno encima del otro.
            bool one = e.NewSize.Width < 560;
            Grid.SetColumnSpan(SourceCard, one ? 2 : 1);
            Grid.SetRow(TargetCard, one ? 1 : 0);
            Grid.SetColumn(TargetCard, one ? 0 : 1);
            Grid.SetColumnSpan(TargetCard, one ? 2 : 1);
        };
    }

    public JobViewModel? Job { get; set; }

    public void Refresh()
    {
        if (Job is null)
        {
            return;
        }

        Show(Job.SourceDrive, Job.SourceFolder, SourceGlyph, SourceName, SourceKind, SourceUsage, SourceSpace, SourcePath);
        Show(Job.DestinationDrive, Job.DestinationFolder, TargetGlyph, TargetName, TargetKind, TargetUsage, TargetSpace, TargetPath);
    }

    private static void Show(DriveDescription drive, string folder, McIcon glyph, TextBlock name, TextBlock kind, FlowBar usage, TextBlock space, TextBlock path)
    {
        glyph.Glyph = drive.Glyph;
        name.Text = drive.Text;
        kind.Text = string.Join(" · ", new[] { drive.Kind, drive.Format }.Where(s => s.Length > 0));
        usage.Value = drive.UsedFraction;
        space.Text = drive.TotalBytes > 0 ? L.F("Target_Space", Format.Bytes(drive.FreeBytes), Format.Bytes(drive.TotalBytes)) : "";
        path.Text = folder;
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e) => Open(Job?.SourceFolder);

    private void OpenTarget_Click(object sender, RoutedEventArgs e) => Open(Job?.DestinationFolder);

    private static void Open(string? folder)
    {
        if (folder is not null && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }
}
