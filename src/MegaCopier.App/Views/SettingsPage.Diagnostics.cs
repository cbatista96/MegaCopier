using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

public sealed partial class SettingsPage
{
    private TextBlock _crashStatus = null!;
    private Button _crashPreview = null!, _crashSend = null!;
    private ToggleSwitch _crashAutomatic = null!;
    private bool _crashLoading;
    private void BuildCrashSection()
    {
        Sec_Advanced.Children.Add(UpdateText("Crash_Title", "McSection"));
        var content = new StackPanel { Spacing = 10 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button Action(string key, RoutedEventHandler click)
        {
            var button = new Button { Content = new TextBlock { Text = L.Get(key) },
                Style = (Style)Application.Current.Resources["McButton"], VerticalAlignment = VerticalAlignment.Center };
            button.Click += click; buttons.Children.Add(button); return button;
        }
        Action("Crash_OpenLogs", (_, _) =>
        {
            Directory.CreateDirectory(DataFolder.Path);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { DataFolder.Path } });
        });
        Action("Crash_GitHub", async (_, _) => await Windows.System.Launcher.LaunchUriAsync(CrashReporter.IssueLink()));
        _crashPreview = Action("Crash_LastReport", async (_, _) => { if (_app.CrashReports is { } reports) await reports.ShowAsync(false); });
        _crashSend = Action("Crash_Send", async (_, _) => { if (_app.CrashReports is { } reports) await reports.ShowAsync(true); });
        _crashAutomatic = new ToggleSwitch { Header = L.Get("Crash_Automatic"), OnContent = "", OffContent = "" };
        _crashAutomatic.Toggled += async (_, _) =>
        {
            if (_crashLoading || _app.CrashReports is not { } reports) return;
            try { await reports.SetAutomaticAsync(_crashAutomatic.IsOn); }
            catch (Exception ex) { App.LogCrash(ex, "Informe: preferencias"); }
        };
        content.Children.Add(_crashAutomatic); content.Children.Add(UpdateText("Crash_Help", "McSecondary"));
        content.Children.Add(UpdateText("Crash_Description", "McSecondary"));
        content.Children.Add(new ScrollViewer { Content = buttons, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 4) });
        _crashStatus = UpdateText("Crash_LocalOnly", "McSecondary"); content.Children.Add(_crashStatus);
        Sec_Advanced.Children.Add(UpdateCard(content));
        Loaded += (_, _) => { if (_app.CrashReports is { } reports) { reports.Changed -= RefreshCrashSettings; reports.Changed += RefreshCrashSettings; } RefreshCrashSettings(); };
        Unloaded += (_, _) => { if (_app.CrashReports is { } reports) reports.Changed -= RefreshCrashSettings; };
    }
    private void RefreshCrashSettings()
    {
        bool configured = CrashReporter.ConfiguredEndpoint is not null;
        _crashAutomatic.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        _crashSend.Visibility = configured && _app.CrashReports?.Last is not null ? Visibility.Visible : Visibility.Collapsed;
        _crashPreview.IsEnabled = _app.CrashReports?.Last is not null;
        string status = configured ? _app.CrashReports?.StatusKey ?? "" : "Crash_LocalOnly";
        _crashStatus.Text = status.Length > 0 ? L.Get(status) : "";
        _crashStatus.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _crashLoading = true; _crashAutomatic.IsOn = _app.Settings.SendCrashReportsAutomatically; _crashLoading = false;
    }
    internal void OpenDiagnostics()
    {
        SearchBox.Text = ""; SectionList.SelectedIndex = 9; ShowSection(9);
        SectionScroll.ChangeView(null, double.MaxValue, null, true); RefreshCrashSettings();
    }
}
