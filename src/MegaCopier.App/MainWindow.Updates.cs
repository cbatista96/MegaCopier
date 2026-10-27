using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MegaCopier.App;

public sealed partial class MainWindow
{
    internal bool UpdateDialogOpen => _dialogGate.CurrentCount == 0
        || Root.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count != 0;

    internal void ShowUpdates()
    {
        OpenSettings();
        SettingsView.OpenUpdates();
    }

    internal void RefreshUpdateBadge()
    {
        UpdateBadge.Visibility = _app.Updates?.Badge == true ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(UpdateBadge,
            L.Get(_app.Updates?.CompletedUpdateUnread == true ? "Updates_NoticeCompleted" : "Updates_NoticeAvailable"));
        UpdateOverflowBadge();
    }

    /// <summary>Solo pruebas: comprueba los límites del botón real con cada pack, sin guardar cambios de preferencias.</summary>
    internal async Task VerifyUpdateBadgePacksAsync(string reportPath)
    {
        if (!App.TestMode) return;
        DependencyObject? parent = UpdateBadge;
        while (parent is not null && parent is not Button) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not Button button) throw new InvalidOperationException();
        string previous = Theming.IconPacks.Current.Id;
        var visibility = UpdateBadge.Visibility;
        var lines = new List<string>();
        try
        {
            UpdateBadge.Visibility = Visibility.Visible;
            foreach (var pack in Theming.IconPacks.All)
            {
                Theming.IconPacks.Apply(pack.Id);
                Root.UpdateLayout();
                await Task.Delay(40);
                var bounds = UpdateBadge.TransformToVisual(button).TransformBounds(new Windows.Foundation.Rect(0, 0, UpdateBadge.ActualWidth, UpdateBadge.ActualHeight));
                bool inside = bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= button.ActualWidth && bounds.Bottom <= button.ActualHeight;
                lines.Add($"{pack.Id}: {(inside ? "OK" : "FUERA")}; indicador={bounds}; botón={button.ActualWidth}x{button.ActualHeight}");
            }
            await File.WriteAllLinesAsync(reportPath, lines);
        }
        finally
        {
            Theming.IconPacks.Apply(previous);
            UpdateBadge.Visibility = visibility;
        }
    }

    /// <summary>Exporta el mismo icono McIcon del pack, sin una segunda colección de dibujos para Windows.</summary>
    internal async Task ExportUpdateIconAsync()
    {
        var icon = new McIcon { Glyph = "\uE895", FontSize = 32, Width = 48, Height = 48,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-1000, 0, 0, 0) };
        try
        {
            Grid.SetRowSpan(icon, Math.Max(1, Root.RowDefinitions.Count));
            Root.Children.Add(icon);
            icon.Measure(new Windows.Foundation.Size(48, 48));
            icon.Arrange(new Windows.Foundation.Rect(0, 0, 48, 48));
            var shot = new RenderTargetBitmap();
            await shot.RenderAsync(icon, 96, 96);
            var pixels = await shot.GetPixelsAsync();
            if (shot.PixelWidth == 0 || shot.PixelHeight == 0) return;
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)shot.PixelWidth, (uint)shot.PixelHeight, 192, 192, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
            await encoder.FlushAsync(); stream.Seek(0);
            Directory.CreateDirectory(Engine.DataFolder.Path);
            using var output = File.Create(Path.Combine(Engine.DataFolder.Path, "update-notification.png"));
            await stream.AsStreamForRead().CopyToAsync(output);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or InvalidOperationException) { }
        finally { Root.Children.Remove(icon); }
    }
}
