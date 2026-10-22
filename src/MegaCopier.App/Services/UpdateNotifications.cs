using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MegaCopier.App.Services;

/// <summary>Avisos locales de actualización. No sustituye los avisos de copia ni instala al pulsarlos.</summary>
internal sealed class UpdateNotifications : IDisposable
{
    private readonly App _app;
    private bool _registered;
    private readonly bool _simulate = App.TestMode && Environment.GetEnvironmentVariable("MEGACOPIER_TEST_UPDATE_NOTIFICATIONS") != "1";
    public UpdateNotifications(App app)
    {
        _app = app;
        if (_simulate) return;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += Invoked;
            AppNotificationManager.Default.Register("MegaCopier", new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico")));
            _registered = true;
        }
        catch (Exception ex)
        { App.LogCrash(ex, "Actualizaciones: registrar aviso"); }
    }
    private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Argument.Contains("action=updates", StringComparison.Ordinal))
        {
            if (App.TestMode) App.TestLog("update-notifications.log", "activada");
            _app.Dispatcher.TryEnqueue(_app.ShowUpdates);
        }
    }

    public async Task<bool> ShowAsync(bool ready, bool completed = false)
    {
        if (_simulate)
        {
            App.TestLog("update-notifications.log", completed ? "actualizada" : ready ? "lista" : "disponible");
            return true;
        }
        if (!_registered) return false;
        try
        {
            await _app.Window.ExportUpdateIconAsync();
            var builder = new AppNotificationBuilder().AddArgument("action", "updates")
                .AddText(L.Get(completed ? "Updates_NoticeCompleted" : ready ? "Updates_NoticeReady" : "Updates_NoticeAvailable"))
                .AddText(L.F(completed ? "Updates_NoticeCompletedBody" : ready ? "Updates_NoticeReadyBody" : "Updates_NoticeAvailableBody",
                    completed ? _app.Updates!.Current.ToString() : _app.Updates!.Version));
            string icon = Path.Combine(Engine.DataFolder.Path, "update-notification.png");
            if (File.Exists(icon)) builder.SetAppLogoOverride(new Uri(icon));
            var notification = builder.BuildNotification();
            notification.Tag = "update";
            notification.Group = "updates";
            AppNotificationManager.Default.Show(notification);
            if (App.TestMode)
            {
                var delivered = await AppNotificationManager.Default.GetAllAsync();
                App.TestLog("update-notifications.log", $"nativa id={notification.Id}; presente={delivered.Any(n => n.Id == notification.Id)}");
            }
            return true;
        }
        catch (Exception ex)
        { App.LogCrash(ex, "Actualizaciones: mostrar aviso"); return false; }
    }

    public void Dispose()
    {
        if (!_registered) return;
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= Invoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (COMException) { }
        _registered = false;
    }
}
