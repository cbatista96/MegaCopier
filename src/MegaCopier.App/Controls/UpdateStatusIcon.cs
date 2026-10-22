using MegaCopier.App.Theming;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MegaCopier.App.Controls;

internal enum UpdateIconState { Unknown, Current, Attention, Disabled, Offline }

/// <summary>Flechas de actualización y distintivo de estado con el conjunto de iconos elegido.</summary>
internal sealed partial class UpdateStatusIcon : UserControl
{
    private readonly McIcon _arrows = new() { Glyph = "\uE895", FontSize = 54, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly McIcon _mark = new() { FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badge = new() { Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private UpdateIconState _state;
    private bool _busy;

    public UpdateStatusIcon()
    {
        Width = 96; Height = 96; IsTabStop = false; IsHitTestVisible = false;
        var root = new Grid(); root.Children.Add(_arrows);
        _badge.Child = _mark; root.Children.Add(_badge); Content = root;
        Loaded += (_, _) => { IconPacks.Changed += Render; Render(); };
        Unloaded += (_, _) => { IconPacks.Changed -= Render; _arrows.SetSpinning(false); };
    }

    internal void Show(UpdateIconState state, bool busy)
    {
        if (_state == state && _busy == busy) return;
        _state = state; _busy = busy; Render();
    }

    private void Render()
    {
        bool native = IconPacks.Current.Style == IconStyle.Mono;
        if (native) _arrows.Foreground = new SolidColorBrush(Color.FromArgb(255, 23, 139, 207));
        else _arrows.ClearValue(ForegroundProperty);
        _arrows.SetSpinning(_busy);
        _badge.Visibility = !_busy && _state != UpdateIconState.Unknown ? Visibility.Visible : Visibility.Collapsed;
        Color color = _state switch
        {
            UpdateIconState.Current => Color.FromArgb(255, 21, 147, 68),
            UpdateIconState.Disabled or UpdateIconState.Offline => Color.FromArgb(255, 209, 52, 56),
            _ => Color.FromArgb(255, 187, 128, 0),
        };
        _mark.Glyph = _state switch { UpdateIconState.Current => "\uE73E", UpdateIconState.Disabled or UpdateIconState.Offline => "\uE711", _ => "\uE7BA" };
        _mark.Foreground = new SolidColorBrush(native ? Colors.White : color);
        _badge.Background = native ? new SolidColorBrush(color) : null;
        _badge.CornerRadius = new CornerRadius(native ? 17 : 0);
    }
}
