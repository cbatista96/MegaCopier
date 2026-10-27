using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App;

/// <summary>
/// Barra de herramientas personalizable: el usuario elige en Ajustes › Interfaz qué accesos se ven y en qué orden. Los
/// que no caben en la ventana pasan, empezando por el último, al menú «…» del final; «Nueva» se queda siempre en la barra.
/// </summary>
public sealed partial class MainWindow
{
    private Dictionary<string, FrameworkElement>? _toolbarElements;

    /// <summary>La vista para la que se repartió la barra por última vez (la pequeña la reparte distinto).</summary>
    private bool _toolbarExpanded;

    /// <summary>Ancho medido de cada acceso, también de los que ahora están en el menú «…» (fuera de la barra no se miden).</summary>
    private readonly Dictionary<string, double> _toolbarWidths = [];

    /// <summary>Lo que el usuario quiere ver, en su orden.</summary>
    private List<string> _toolbarShown = [];

    /// <summary>Lo que está en la barra ahora; el resto de <see cref="_toolbarShown"/>, en el menú «…».</summary>
    private List<string> _toolbarKept = [];

    private string _finishTextMeasured = "\0";
    private double _finishTextWidth;

    private Dictionary<string, FrameworkElement> ToolbarElements => _toolbarElements ??= new()
    {
        [ToolbarItems.Sidebar] = SidebarItem,
        [ToolbarItems.New] = NewTransferButton,
        [ToolbarItems.Separator1] = ToolSeparator1,
        [ToolbarItems.PlayPause] = PlayPauseAllButton,
        [ToolbarItems.ClearDone] = ClearDoneButton,
        [ToolbarItems.Separator2] = ToolSeparator2,
        [ToolbarItems.Timeline] = TimelineButton,
        [ToolbarItems.History] = HistoryButton,
        [ToolbarItems.Settings] = SettingsButton,
        [ToolbarItems.About] = AboutButton,
        [ToolbarItems.AlwaysOnTop] = OnTopButton,
        [ToolbarItems.Shelf] = ShelfButton,
        [ToolbarItems.Compare] = CompareButton,
    };

    /// <summary>Coloca los accesos según los ajustes (al abrir la ventana y al cambiarlos en Ajustes).</summary>
    internal void ApplyToolbar()
    {
        foreach (var (button, id) in new[] { (OnTopButton, ToolbarItems.AlwaysOnTop), (ShelfButton, ToolbarItems.Shelf), (CompareButton, ToolbarItems.Compare) })
        {
            string label = ToolbarItems.Label(id);
            ToolTipService.SetToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        }

        string more = L.Get("Toolbar_More");
        ToolTipService.SetToolTip(OverflowButton, more);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(OverflowButton, more);
        UpdateOnTopButton();

        FinishButton.Visibility = _app.Settings.ToolbarShowFinish ? Visibility.Visible : Visibility.Collapsed;
        _toolbarShown = ToolbarItems.Visible(_app.Settings);

        // Todos en la barra un momento, para medirlos; después se reparte con el ancho que hay.
        _toolbarKept = [];
        SetToolbarChildren([.. _toolbarShown], overflow: true);
        ToolButtons.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        RememberToolbarWidths();
        _toolbarKept = [.. _toolbarShown];
        if (Toolbar.ActualWidth > 0)
        {
            FitToolbar(Toolbar.ActualWidth);
        }
        else
        {
            SetToolbarChildren(_toolbarKept, overflow: false);
        }
    }

    private void SetToolbarChildren(IReadOnlyList<string> ids, bool overflow)
    {
        ToolButtons.Children.Clear();
        foreach (string id in ids)
        {
            ToolButtons.Children.Add(ToolbarElements[id]);
        }

        if (overflow)
        {
            ToolButtons.Children.Add(OverflowButton);
        }
    }

    private void RememberToolbarWidths()
    {
        foreach (var (id, element) in ToolbarElements)
        {
            if (element.Parent == ToolButtons && element.DesiredSize.Width > 0)
            {
                _toolbarWidths[id] = element.DesiredSize.Width;
            }
        }

        if (OverflowButton.Parent == ToolButtons && OverflowButton.DesiredSize.Width > 0)
        {
            _toolbarWidths["…"] = OverflowButton.DesiredSize.Width;
        }
    }

    /// <summary>
    /// Reparte el ancho. En la ventana pequeña, siempre los mismos huecos que de serie y «Al terminar» con su texto: lo
    /// que sobra va al menú «…», que ocupa el último hueco. En la grande, todo lo que quepa con el texto de «Al terminar»;
    /// si no cabe, «Al terminar» se queda en su icono y, si aun así no cabe, los últimos accesos pasan al menú «…».
    /// </summary>
    private void FitToolbar(double width)
    {
        if (_toolbarShown.Count == 0 && ToolButtons.Children.Count == 0)
        {
            return; // todavía sin colocar
        }

        RememberToolbarWidths();
        var unlimited = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        if (_finishTextMeasured != FinishText.Text)
        {
            var text = new TextBlock { Text = FinishText.Text, Style = FinishText.Style }; // aunque ahora esté oculto
            text.Measure(unlimited);
            _finishTextWidth = text.DesiredSize.Width;
            _finishTextMeasured = FinishText.Text;
        }

        FinishIcon.Measure(unlimited);
        bool finish = _app.Settings.ToolbarShowFinish;
        double finishIcon = finish ? FinishIcon.DesiredSize.Width + 44 : 0; // los márgenes del botón y su flecha
        double finishFull = finish ? finishIcon + 8 + _finishTextWidth : 0;
        double available = width - Toolbar.Padding.Left - Toolbar.Padding.Right - (Toolbar.ColumnSpacing * 2);

        // El botón de la lista solo se ve en la ventana grande; en la pequeña no ocupa sitio.
        bool sidebarShown = SidebarButton.Visibility == Visibility.Visible;
        double WidthOf(string id) => id == ToolbarItems.Sidebar && !sidebarShown ? 0 : _toolbarWidths.GetValueOrDefault(id, 36);

        double Needed(IReadOnlyList<string> ids, bool overflow)
        {
            double sum = ids.Sum(WidthOf);
            int count = ids.Count(id => WidthOf(id) > 0) + (overflow ? 1 : 0);
            if (overflow)
            {
                sum += _toolbarWidths.GetValueOrDefault("…", 36);
            }

            return sum + (ToolButtons.Spacing * Math.Max(0, count - 1));
        }

        // Se quitan desde el final; «Nueva» no se va nunca al menú (tiene su propio desplegable).
        static bool RemoveLast(List<string> kept)
        {
            if (kept.FindLastIndex(id => id != ToolbarItems.New) is not (int last and >= 0))
            {
                return false;
            }

            kept.RemoveAt(last);
            while (kept.Count > 0 && ToolbarItems.IsSeparator(kept[^1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }

            return true;
        }

        var kept = new List<string>(_toolbarShown);
        bool showText = finish;
        if (!_expanded)
        {
            // Ventana pequeña: siempre el mismo número de accesos que trae de serie y «Al terminar» con su texto. Si se
            // muestran más, el último hueco es el «…» con el resto.
            int Buttons(IEnumerable<string> ids) => ids.Count(id => !ToolbarItems.IsSeparator(id) && id != ToolbarItems.Sidebar);
            if (Buttons(kept) > CompactToolbarSlots)
            {
                while (Buttons(kept) > CompactToolbarSlots - 1 && RemoveLast(kept))
                {
                }
            }

            // Solo con letra muy grande o una ventana más estrecha de lo normal se quitan más, nunca el texto.
            bool overflowing = kept.Count < _toolbarShown.Count;
            while (Needed(kept, overflowing) + finishFull > available && RemoveLast(kept))
            {
                overflowing = true;
            }
        }
        else if (Needed(kept, false) + finishFull > available)
        {
            showText = false;
            if (Needed(kept, false) + finishIcon > available)
            {
                while (Needed(kept, true) + finishIcon > available && RemoveLast(kept))
                {
                }
            }
        }

        _finishTextRequiredWidth = Needed(_toolbarShown, false) + finishFull; // para la comprobación de textos
        FinishText.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
        bool overflowNow = ToolbarOverflowOf(kept).Any();
        bool changed = !kept.SequenceEqual(_toolbarKept) || (OverflowButton.Parent == ToolButtons) != overflowNow
            || ToolButtons.Children.Count != kept.Count + (overflowNow ? 1 : 0);
        _toolbarKept = kept;
        if (changed)
        {
            SetToolbarChildren(kept, overflowNow);
        }

        UpdateOverflowBadge();
    }

    /// <summary>Los accesos que están ahora en el menú «…».</summary>
    private IEnumerable<string> ToolbarOverflow => ToolbarOverflowOf(_toolbarKept);

    /// <summary>La lista de copias no va al menú en la ventana pequeña, donde no se puede abrir.</summary>
    private IEnumerable<string> ToolbarOverflowOf(List<string> kept) => _toolbarShown.Where(id =>
        !ToolbarItems.IsSeparator(id) && !kept.Contains(id) && (_expanded || id != ToolbarItems.Sidebar));

    /// <summary>Huecos de la barra en la ventana pequeña: los accesos que se ven de serie (la lista no está en ella).</summary>
    private static readonly int CompactToolbarSlots = ToolbarItems.Default.Count(id =>
        !ToolbarItems.IsSeparator(id) && id != ToolbarItems.Sidebar && !ToolbarItems.DefaultHidden.Contains(id));

    /// <summary>El aviso de actualización de Ajustes pasa al botón «…» cuando Ajustes está dentro de su menú.</summary>
    private void UpdateOverflowBadge() =>
        OverflowBadge.Visibility = UpdateBadge.Visibility == Visibility.Visible && ToolbarOverflow.Contains(ToolbarItems.Settings)
            ? Visibility.Visible : Visibility.Collapsed;

    private void OverflowMenu_Opening(object sender, object e)
    {
        OverflowMenu.Items.Clear();
        foreach (string id in ToolbarOverflow)
        {
            MenuFlyoutItem item;
            if (id == ToolbarItems.AlwaysOnTop)
            {
                item = new ToggleMenuFlyoutItem { IsChecked = _app.Settings.AlwaysOnTop };
            }
            else
            {
                item = new MenuFlyoutItem();
            }

            item.Text = id switch
            {
                ToolbarItems.PlayPause => L.Get(_toolbarShowsPause != false ? "Toolbar_PauseAll" : "Toolbar_ResumeAll"),
                _ => ToolbarItems.Label(id),
            };
            item.Icon = new FontIcon
            {
                Glyph = id == ToolbarItems.PlayPause ? (_toolbarShowsPause != false ? "" : "") : ToolbarItems.Glyph(id),
            };
            RoutedEventHandler click = id switch
            {
                ToolbarItems.Sidebar => Sidebar_Click,
                ToolbarItems.PlayPause => PlayPauseAll_Click,
                ToolbarItems.ClearDone => ClearDone_Click,
                ToolbarItems.Timeline => Timeline_Click,
                ToolbarItems.History => History_Click,
                ToolbarItems.Settings => Settings_Click,
                ToolbarItems.About => About_Click,
                ToolbarItems.AlwaysOnTop => OnTop_Click,
                ToolbarItems.Shelf => Shelf_Click,
                _ => Compare_Click,
            };
            item.Click += click;
            OverflowMenu.Items.Add(item);
        }
    }

    /// <summary>Siempre visible desde la barra: el mismo ajuste que en Ajustes › Interfaz, para todas las ventanas.</summary>
    private void OnTop_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AlwaysOnTop = !_app.Settings.AlwaysOnTop;
        _app.Settings.Save();
        foreach (var window in _app.AllWindows)
        {
            window.ApplyAlwaysOnTop(_app.Settings.AlwaysOnTop);
            window.UpdateOnTopButton();
        }

        SettingsView.SyncAlwaysOnTop();
    }

    /// <summary>Encendido, la chincheta clavada y del color de acento.</summary>
    internal void UpdateOnTopButton()
    {
        bool on = _app.Settings.AlwaysOnTop;
        OnTopIcon.Glyph = on ? "" : "";
        OnTopIcon.Accent = on;
    }
}
