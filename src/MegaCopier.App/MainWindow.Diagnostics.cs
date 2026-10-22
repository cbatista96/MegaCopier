using MegaCopier.App.Services;
using MegaCopier.Engine.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MegaCopier.App;

public sealed partial class MainWindow
{
    private ContentDialog? _reportDialog;
    private CheckBox? _reportAlways;

    internal void ShowDiagnostics()
    {
        OpenSettings(); SettingsView.OpenDiagnostics();
    }
    internal bool DiagnosticsSettingsOpen => SettingsView.Visibility == Visibility.Visible;
    internal bool DiagnosticsRootReady => Root.XamlRoot is not null;
    internal async Task<(bool Send, bool Always)> ShowCrashReportAsync(CrashReport report, bool send)
    {
        await _dialogGate.WaitAsync();
        var before = AppWindow.Size;
        bool grow = false;
        SizeChangedEventHandler? fitPreview = null;
        try
        {
            // La preferencia puede haber cambiado mientras esperaba a otro diálogo.
            if (send && _app.Settings.SendCrashReportsAutomatically) return (true, true);
            grow = !_expanded;
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
            _reportDialog = dialog;
            _reportAlways = always;
            bool accepted = await dialog.ShowAsync() == ContentDialogResult.Primary;
            return (accepted, accepted && always.IsChecked == true);
        }
        finally
        {
            if (fitPreview is not null) Root.SizeChanged -= fitPreview;
            _reportDialog = null;
            _reportAlways = null;
            if (grow)
            {
                // Este informe no encadena preguntas de copia: restaurar al cerrar, sin la espera de conflictos.
                _shrinkTicket++;
                _roomForDialog = null;
                double scale = Scaled(1000) / 1000.0;
                AnimateResizeTo(before.Width / scale, before.Height / scale);
            }
            _dialogGate.Release();
        }
    }

    internal string DebugCrashLayout() => $"{AppWindow.Size.Width}x{AppWindow.Size.Height};dialog={_reportDialog is not null}";

    /// <summary>Solo pruebas aisladas contra un receptor local: ejecutar las acciones del diálogo real.</summary>
    internal bool DebugCrashChoice(string choice)
    {
        if (!App.TestMode || CrashReporter.ConfiguredEndpoint is not { Scheme: "http", Host: "127.0.0.1" }
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MEGACOPIER_TEST_DATA_DIRECTORY"))
            || _reportDialog is not { } dialog || _reportAlways is null
            || choice is not ("cancel" or "send" or "always")) return false;
        string name = choice == "cancel" ? "CloseButton" : "PrimaryButton";
        var button = Responsive.Descendants<Button>(dialog).FirstOrDefault(b => b.Name == name);
        if (button is null || !button.IsEnabled
            || Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button)
                is not Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke) return false;
        _reportAlways.IsChecked = choice == "always";
        invoke.Invoke();
        return true;
    }
}
