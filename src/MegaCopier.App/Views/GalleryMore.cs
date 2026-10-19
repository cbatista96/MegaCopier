using MegaCopier.App.Controls;
using MegaCopier.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Galería que enseña solo los primeros elementos y, debajo, «Mostrar más» con una flecha: al pulsarlo aparecen todos y
/// el botón, que queda al final, pasa a «Mostrar menos». Si lo elegido está entre los ocultos, empieza desplegada para
/// que siempre se vea lo que está puesto.
/// </summary>
internal sealed class GalleryMore
{
    /// <summary>Como mucho se ven 15 de entrada (tres filas de cinco); con otro número de columnas, filas completas.</summary>
    public const int MaxLimit = 15;

    private int _limit = MaxLimit;
    private int _retries;

    private int Limit => _limit;

    private readonly GridView _gallery;
    private readonly Func<object?> _selected;
    private readonly Action<object?> _select;
    private readonly Button _button;
    private readonly McIcon _arrow = new() { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<object> _all = [];
    private bool _expanded;

    /// <param name="gallery">La galería; el botón se coloca justo debajo, en el mismo panel.</param>
    /// <param name="selected">Lo elegido ahora (para no ocultarlo y para volver a marcarlo).</param>
    /// <param name="select">Marca un elemento sin que cuente como un cambio del usuario.</param>
    public GalleryMore(GridView gallery, Func<object?> selected, Action<object?> select)
    {
        _gallery = gallery;
        _selected = selected;
        _select = select;
        _button = new Button
        {
            Style = (Style)Application.Current.Resources["McButton"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _label, _arrow } },
            Visibility = Visibility.Collapsed,
        };
        _button.Click += (_, _) => Toggle();
        gallery.SizeChanged += (_, _) => UpdateLimit();
        if (!Place())
        {
            gallery.Loaded += (_, _) => Place();
        }
    }

    /// <summary>Pone el botón justo debajo de la galería (en cuanto la galería tiene sitio en la página).</summary>
    private bool Place()
    {
        if (_button.Parent is null && (_gallery.Parent ?? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(_gallery)) is Panel panel)
        {
            panel.Children.Insert(panel.Children.IndexOf(_gallery) + 1, _button);
        }

        return _button.Parent is not null;
    }

    /// <summary>Pone los elementos (temas, conjuntos, estilos...) y enseña los primeros.</summary>
    public void SetItems(IReadOnlyList<object> items)
    {
        _all = items;
        int index = _selected() is { } current ? IndexOf(current) : -1;
        _expanded |= index >= Limit;
        Apply();
    }

    /// <summary>Lo elegido cambió por otro camino: se vuelve a marcar (y se despliega si estaba entre los ocultos).</summary>
    public void Refresh() => SetItems(_all);

    private int IndexOf(object item)
    {
        for (int i = 0; i < _all.Count; i++)
        {
            if (Equals(_all[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private void Toggle()
    {
        _expanded = !_expanded;
        Apply();
        if (!_expanded)
        {
            // Al plegar, que la galería no se quede fuera de la vista.
            _gallery.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.1 });
        }
    }

    private void Apply()
    {
        var current = _selected();
        _gallery.ItemsSource = _expanded || _all.Count <= Limit ? _all : _all.Take(Limit).ToList();
        _select(current);
        _button.Visibility = _all.Count > Limit ? Visibility.Visible : Visibility.Collapsed;
        _label.Text = _expanded ? L.Get("Gallery_Less") : L.F("Gallery_More", _all.Count - Limit);
        _arrow.Glyph = _expanded ? "" : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_button, _label.Text);
        _gallery.DispatcherQueue.TryEnqueue(UpdateLimit); // al medir los elementos ya se sabe cuántos caben por fila
    }

    /// <summary>
    /// Cuántos caben por fila según el ancho: así lo que se ve de entrada son filas completas (sin una última fila a
    /// medias) y «Mostrar más» queda justo debajo.
    /// </summary>
    private void UpdateLimit()
    {
        if (_gallery.ContainerFromIndex(0) is not FrameworkElement first || first.ActualWidth <= 0)
        {
            if (_gallery.IsLoaded && _retries++ < 20)
            {
                _gallery.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateLimit); // aún sin medir
            }

            return;
        }

        _retries = 0;

        double item = first.ActualWidth + first.Margin.Left + first.Margin.Right;
        double width = _gallery.ActualWidth - _gallery.Padding.Left - _gallery.Padding.Right;
        int columns = Math.Max(1, (int)(width / item));
        int limit = Math.Max(columns, MaxLimit / columns * columns);
        if (limit != _limit)
        {
            _limit = limit;
            Apply();
        }
    }
}
