using MegaCopier.App.Services;
using MegaCopier.Engine.Diagnostics;
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
        try
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = L.Get("Crash_Description"), TextWrapping = TextWrapping.Wrap });
            var always = new CheckBox { Content = L.Get("Crash_Always"), Visibility = send ? Visibility.Visible : Visibility.Collapsed };
            var exact = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"), Height = 260, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top, Text = CrashQueue.Preview(report) };
            ScrollViewer.SetVerticalScrollBarVisibility(exact, ScrollBarVisibility.Auto);
            var preview = new Expander { Header = L.Get("Crash_Preview"), Content = exact, IsExpanded = !send, HorizontalAlignment = HorizontalAlignment.Stretch };
            content.Children.Add(preview); content.Children.Add(always);
            var dialog = new ContentDialog { Title = L.Get(send ? "Crash_Unexpected" : "Crash_Preview"), Content = content,
                PrimaryButtonText = send ? L.Get("Crash_Send") : "", CloseButtonText = L.Get(send ? "Crash_NoSend" : "Crash_Close"),
                DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
                Style = (Style)Application.Current.Resources["McContentDialogStyle"] };
            bool accepted = await dialog.ShowAsync() == ContentDialogResult.Primary;
            return (accepted, accepted && always.IsChecked == true);
        }
        finally { _dialogGate.Release(); }
    }
}
