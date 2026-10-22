using MegaCopier.App.Services;
using Microsoft.UI.Xaml;

namespace MegaCopier.App.Views;

internal sealed record SettingsRoute(int Section, int View = 0, string Version = "", string Notes = "", string Tag = "");

public sealed partial class SettingsPage
{
    internal event Action? NavigationChanged;
    private bool _restoringSettings;
    private int _visibleSection;
    internal SettingsRoute CaptureRoute() => _visibleSection == Sections.Length - 1
        ? new(_visibleSection, _updateView, _updateView == 2 ? _historyVersion : "", _updateView == 2 ? _historyMarkdown : "", _updateView == 2 ? _historyTag : "")
        : new(_visibleSection);

    internal void RestoreRoute(SettingsRoute route, bool forward)
    {
        _restoringSettings = true;
        try
        {
            SearchBox.Text = "";
            SectionList.SelectedIndex = route.Section;
            ShowSection(route.Section);
            if (route.Section == Sections.Length - 1)
            {
                if (route.View > 0) ShowUpdateHistory();
                if (route.View == 2) { _historyVersion = route.Version; ShowHistoryNotes(route.Notes, route.Tag); }
                SetUpdateView(route.View, animate: false);
                RefreshUpdates();
            }
        }
        finally { _restoringSettings = false; }
        Motion.SlideX(SectionsHost, forward ? 60 : -60, 0, 280, null, decelerate: true);
    }
}
