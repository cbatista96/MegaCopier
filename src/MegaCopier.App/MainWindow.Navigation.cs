using MegaCopier.Engine;
using MegaCopier.App.Services;
using MegaCopier.App.Views;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MegaCopier.App;

public sealed partial class MainWindow
{
    private enum NavigationPage { Jobs, Settings, History, Timeline, Compare }
    private sealed record WindowRoute(NavigationPage Page, SettingsRoute? Settings = null);
    private readonly NavigationHistory<WindowRoute> _navigation = new(new(NavigationPage.Jobs));
    private bool _restoringNavigation;

    private void InitializeNavigation()
    {
        // Los atajos de toda la ventana no deben generar un tooltip «Atrás» sobre el contenido.
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        SettingsView.NavigationChanged += () =>
        {
            if (SettingsView.Visibility == Visibility.Visible) RememberNavigation();
        };
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var kind = e.GetCurrentPoint(Root).Properties.PointerUpdateKind;
            if (kind is PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed)
                e.Handled = Navigate(kind == PointerUpdateKind.XButton2Pressed);
        }), handledEventsToo: true);
        void Key(VirtualKey key, VirtualKeyModifiers modifiers, bool forward)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) => e.Handled = Navigate(forward);
            Root.KeyboardAccelerators.Add(accelerator);
        }
        Key(VirtualKey.GoBack, VirtualKeyModifiers.None, false);
        Key(VirtualKey.GoForward, VirtualKeyModifiers.None, true);
        Key(VirtualKey.Left, VirtualKeyModifiers.Menu, false);
        Key(VirtualKey.Right, VirtualKeyModifiers.Menu, true);
    }

    private void RememberNavigation()
    {
        if (_restoringNavigation) return;
        _navigation.Visit(SettingsView.Visibility == Visibility.Visible ? new(NavigationPage.Settings, SettingsView.CaptureRoute())
            : HistoryView.Visibility == Visibility.Visible ? new(NavigationPage.History)
            : TimelineView.Visibility == Visibility.Visible ? new(NavigationPage.Timeline)
            : CompareView.Visibility == Visibility.Visible ? new(NavigationPage.Compare) : new(NavigationPage.Jobs));
    }

    private bool Navigate(bool forward)
    {
        if (UpdateDialogOpen || !_navigation.Move(forward, out var route)) return false;
        _restoringNavigation = true;
        try
        {
            switch (route.Page)
            {
                case NavigationPage.Settings:
                    OpenSettings();
                    if (route.Settings is { } settings) SettingsView.RestoreRoute(settings, forward);
                    break;
                case NavigationPage.History: History_Click(this, new RoutedEventArgs()); break;
                case NavigationPage.Timeline: Timeline_Click(this, new RoutedEventArgs()); break;
                case NavigationPage.Compare:
                    HistoryView.Visibility = SettingsView.Visibility = TimelineView.Visibility = Visibility.Collapsed;
                    CompareView.Visibility = Visibility.Visible; EnsureRoomForPage(); UpdateLayoutState(); break;
                default: ShowJobs(); break;
            }
        }
        finally { _restoringNavigation = false; }
        return true;
    }

    private void NavigateBack()
    {
        if (!Navigate(false) && !UpdateDialogOpen) ShowJobs();
    }
}
