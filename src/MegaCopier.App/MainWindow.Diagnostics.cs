using MegaCopier.App.Services;
using MegaCopier.Engine.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App;

public sealed partial class MainWindow
{
    internal void ShowDiagnostics()
    {
        OpenSettings(); SettingsView.OpenDiagnostics();
    }
    internal bool DiagnosticsSettingsOpen => SettingsView.Visibility == Visibility.Visible;
    internal bool DiagnosticsRootReady => Root.XamlRoot is not null;
    internal async Task<(bool Send, bool Always)> ShowCrashReportAsync(CrashReport report, bool send)
    {
        await _dialogGate.WaitAsync();
        bool grow = !_expanded;
        SizeChangedEventHandler? fitPreview = null;
        try
        {
            // La vista compacta tiene alto fijo: reservar espacio mientras se lee el informe.
            if (grow)
            {
                _shrinkTicket++;
                double scale = Scaled(1000) / 1000.0;
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
                _roomForDialog = (Math.Min(640, area.Width / scale - 24), Math.Min(720, area.Height / scale - 24));
                AnimateResizeTo(Math.Min(Math.Max(AppWindow.Size.Width / scale, _roomForDialog.Value.Width), area.Width / scale - 24), _roomForDialog.Value.Height);
            }
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = L.Get("Crash_Description"), TextWrapping = TextWrapping.Wrap });
            var always = new CheckBox { Content = new TextBlock { Text = L.Get("Crash_Always"), TextWrapping = TextWrapping.Wrap },
                Visibility = send ? Visibility.Visible : Visibility.Collapsed };
            var exact = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"), Height = 260, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top, Text = CrashQueue.Preview(report) };
            ScrollViewer.SetVerticalScrollBarVisibility(exact, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(exact, ScrollMode.Enabled);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(exact, L.Get("Crash_Preview"));
            // El código tiene su propio desplazamiento; no puede expulsar la casilla del área visible.
            void FitPreview() => exact.Height = Math.Clamp((Root.XamlRoot?.Size.Height ?? Root.ActualHeight) - 420, 72, 260);
            fitPreview = (_, _) => FitPreview();
            Root.SizeChanged += fitPreview;
            FitPreview();
            var preview = new Expander { Header = L.Get("Crash_Preview"), Content = exact, IsExpanded = !send, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(preview); content.Children.Add(always);
            var dialog = new ContentDialog { Title = L.Get(send ? "Crash_Unexpected" : "Crash_Preview"), Content = content,
                PrimaryButtonText = send ? L.Get("Crash_Send") : "", CloseButtonText = L.Get(send ? "Crash_NoSend" : "Crash_Close"),
                DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
                Style = (Style)Application.Current.Resources["McContentDialogStyle"] };
            dialog.Opened += (_, _) =>
            {
                // La plantilla común desactiva este desplazamiento. Aquí hace falta como respaldo
                // para pantallas bajas, escalado grande o traducciones que ocupen más líneas.
                var scroll = Responsive.Descendants<ScrollViewer>(dialog).FirstOrDefault(s => s.Name == "ContentScrollViewer");
                if (scroll is not null)
                {
                    scroll.VerticalScrollMode = ScrollMode.Enabled;
                    scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
            };
            bool accepted = await dialog.ShowAsync() == ContentDialogResult.Primary;
            return (accepted, accepted && always.IsChecked == true);
        }
        finally
        {
            if (fitPreview is not null) Root.SizeChanged -= fitPreview;
            if (grow) _ = ShrinkAfterDialogAsync(++_shrinkTicket);
            _dialogGate.Release();
        }
    }
}
