using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace MegaCopier.App.Views;

/// <summary>
/// Ajustes › Interfaz › Barra de herramientas: qué accesos se ven y en qué orden. La lista va plegada y se despliega con
/// «Personalizar». Al marcar o mover un acceso no se vuelve a crear la lista: solo cambia esa fila.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>Fila de cada acceso, para moverla sin crear las demás otra vez.</summary>
    private readonly Dictionary<string, Grid> _toolbarRows = [];

    private void LoadToolbarItems()
    {
        ToolbarItemsList.Children.Clear();
        _toolbarRows.Clear();
        foreach (string id in ToolbarItems.Order(_app.Settings))
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            label.Children.Add(new Controls.McIcon { Glyph = ToolbarItems.Glyph(id), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = ToolbarItems.Label(id), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var show = new CheckBox
            {
                Content = label,
                IsChecked = ToolbarItems.IsVisible(_app.Settings, id),
                IsEnabled = ToolbarItems.CanHide(id),
                MinWidth = 0,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(show, ToolbarItems.Label(id));
            show.Click += (_, _) =>
            {
                ToolbarItems.SetVisible(_app.Settings, id, show.IsChecked == true);
                SaveToolbar(reload: false);
            };
            row.Children.Add(show);
            row.Children.Add(MoveButton(id, "", "Settings_ToolbarUp", () => Move(id, -1), column: 1));
            row.Children.Add(MoveButton(id, "", "Settings_ToolbarDown", () => Move(id, +1), column: 2));
            _toolbarRows[id] = row;
            ToolbarItemsList.Children.Add(row);
        }

        UpdateMoveButtons();
        ToolbarFinishBox.IsChecked = _app.Settings.ToolbarShowFinish;
    }

    private static Button MoveButton(string id, string glyph, string key, Action move, int column)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Desactivada (el primero no sube, el último no baja), solo la flecha más tenue, sin el fondo gris del botón.
        button.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBorderBrushDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        string text = L.F(key, ToolbarItems.Label(id));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
        button.Click += (_, _) => move();
        Grid.SetColumn(button, column);
        return button;
    }

    /// <summary>El primero no sube y el último no baja.</summary>
    private void UpdateMoveButtons()
    {
        int count = ToolbarItemsList.Children.Count;
        for (int i = 0; i < count; i++)
        {
            if (ToolbarItemsList.Children[i] is Grid row)
            {
                ((Control)row.Children[1]).IsEnabled = i > 0;
                ((Control)row.Children[2]).IsEnabled = i < count - 1;
            }
        }
    }

    private void Move(string id, int delta)
    {
        var order = ToolbarItems.Order(_app.Settings);
        int from = order.IndexOf(id);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= order.Count)
        {
            return;
        }

        (order[from], order[to]) = (order[to], order[from]);
        _app.Settings.ToolbarOrder = order;
        ToolbarItemsList.Children.Move((uint)from, (uint)to);
        UpdateMoveButtons();
        SaveToolbar(reload: false);

        // El foco sigue al acceso movido, para poder moverlo varias veces seguidas con el teclado.
        var row = _toolbarRows[id];
        if (row.Children[delta < 0 ? 1 : 2] is Button { IsEnabled: true } button)
        {
            button.Focus(FocusState.Programmatic);
        }
        else if (row.Children[0] is Control check)
        {
            check.Focus(FocusState.Programmatic);
        }
    }

    private bool _toolbarListOpen;
    private Storyboard? _toolbarListStory;

    /// <summary>
    /// Despliega o pliega la lista de accesos deslizándola: crece hacia abajo (lo de debajo baja con ella) y se recoge
    /// igual. La flecha gira hacia arriba al desplegar. «Restablecer» tiene su hueco siempre reservado, aunque solo se
    /// ve desplegada: así la cabecera no cambia de alto y su icono no se mueve.
    /// </summary>
    private void ToolbarExpand_Click(object sender, RoutedEventArgs e)
    {
        bool open = _toolbarListOpen = !_toolbarListOpen;
        double from = ToolbarItemsHost.Visibility == Visibility.Visible ? ToolbarItemsHost.ActualHeight : 0;
        _toolbarListStory?.Stop();
        _toolbarListStory = null;
        ToolbarResetButton.IsHitTestVisible = open;
        ToolbarResetButton.IsTabStop = open;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(ToolbarResetButton,
            open ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        ToolbarItemsHost.Visibility = Visibility.Visible;
        double width = ToolbarItemsHost.ActualWidth > 0 ? ToolbarItemsHost.ActualWidth : ((FrameworkElement)ToolbarItemsHost.Parent).ActualWidth;
        ToolbarItemsPanel.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        double full = ToolbarItemsPanel.DesiredSize.Height;
        if (Theming.BarAppearance.ReduceMotion)
        {
            ToolbarExpandRotation.Angle = open ? 180 : 0;
            ToolbarResetButton.Opacity = open ? 1 : 0;
            ToolbarItemsHost.Height = double.NaN;
            ToolbarItemsHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(open ? 260 : 200);
        var story = new Storyboard();
        void Animate(DependencyObject target, string property, double? start, double end, bool layout = false)
        {
            var animation = new DoubleAnimation { From = start, To = end, Duration = duration, EasingFunction = ease, EnableDependentAnimation = layout };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }

        // El alto, de lo que ya se ve hasta todo o nada (la lista se recorta mientras tanto); el contenido entra
        // deslizándose desde arriba y apareciendo, y sale igual.
        Animate(ToolbarItemsHost, "Height", from, open ? full : 0, layout: true);
        Animate(ToolbarItemsShift, "Y", null, open ? 0 : -12);
        Animate(ToolbarItemsPanel, "Opacity", null, open ? 1 : 0);
        Animate(ToolbarExpandRotation, "Angle", null, open ? 180 : 0);
        Animate(ToolbarResetButton, "Opacity", null, open ? 1 : 0);
        if (open && from == 0)
        {
            ToolbarItemsShift.Y = -12;
            ToolbarItemsPanel.Opacity = 0;
        }

        story.Completed += (_, _) =>
        {
            if (_toolbarListStory != story)
            {
                return; // otro clic lo cambió a mitad
            }

            story.Stop();
            _toolbarListStory = null;
            ToolbarItemsShift.Y = 0;
            ToolbarItemsPanel.Opacity = 1;
            ToolbarExpandRotation.Angle = open ? 180 : 0;
            ToolbarResetButton.Opacity = open ? 1 : 0;
            ToolbarItemsHost.Height = double.NaN; // desplegada, crece sola si cambia algo dentro
            ToolbarItemsHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        };
        _toolbarListStory = story;
        story.Begin();
    }

    private void ToolbarFinish_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.ToolbarShowFinish = ToolbarFinishBox.IsChecked == true;
        SaveToolbar(reload: false);
    }

    private void ToolbarReset_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.ToolbarOrder = [];
        _app.Settings.ToolbarHidden = null;
        _app.Settings.ToolbarShowFinish = true;
        SaveToolbar(reload: true);
    }

    /// <summary>Guarda y aplica la barra en todas las ventanas; la lista solo se crea otra vez al restablecer.</summary>
    private void SaveToolbar(bool reload)
    {
        _app.Settings.Save();
        foreach (var window in _app.AllWindows)
        {
            window.ApplyToolbar(); // la misma barra en todas las ventanas
        }

        if (reload)
        {
            LoadToolbarItems();
        }
    }

    /// <summary>«Siempre visible» se cambió desde la barra: el interruptor de Ajustes lo refleja.</summary>
    internal void SyncAlwaysOnTop()
    {
        bool loading = _loading;
        _loading = true;
        OnTopToggle.IsOn = _app.Settings.AlwaysOnTop;
        _loading = loading;
    }
}
