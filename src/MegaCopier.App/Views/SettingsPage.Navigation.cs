using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using Microsoft.UI.Xaml;

namespace MegaCopier.App.Views;

internal sealed record SettingsRoute(int Section, int View = 0, string Version = "", string Notes = "", string Tag = "");

public sealed partial class SettingsPage
{
    private bool _navigationEntering;
    internal double NavigationOffset => NavRevealTransform.TranslateX;
    internal int NavigationEntranceCount { get; private set; }

    /// <summary>Preparar antes de mostrar Ajustes: la lista entra desde el borde, sin cambiar de iconos a textos a mitad.</summary>
    internal bool PrepareNavigationEntrance()
    {
        NavigationEntranceCount++;
        Motion.ResetSlide(NavPane);
        _navigationEntering = !BarAppearance.ReduceMotion;
        ApplyNavWidth(ActualWidth);
        SetNavigationEntrance(_navigationEntering ? 0 : 1);
        return _navigationEntering;
    }

    /// <summary>Comparte el avance del marco al abrirse desde la ventana pequeña.</summary>
    internal void SetNavigationEntrance(double progress)
    {
        double pixel = NavPane.XamlRoot?.RasterizationScale ?? 1;
        NavRevealTransform.TranslateX = Math.Round(-NavColumn.Width.Value * (1 - progress) * pixel) / pixel;
        if (progress >= 1)
        {
            _navigationEntering = false;
            ApplyNavWidth(ActualWidth);
        }
    }

    /// <summary>También entra suavemente cuando la ventana ya tiene sitio y no necesita agrandarse.</summary>
    internal void AnimateNavigationEntrance() =>
        Motion.SlideX(NavPane, NavRevealTransform.TranslateX, 0, 320, () => SetNavigationEntrance(1));

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
        Motion.ResetSlide(SectionsHost);
    }
}
