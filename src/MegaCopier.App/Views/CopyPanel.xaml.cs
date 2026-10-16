using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Panel de una copia: barra del progreso total arriba, barra del archivo actual abajo, unidades de origen y destino,
/// y los botones. Es la ventana compacta y también la cabecera de la vista completa.
/// </summary>
public sealed partial class CopyPanel : UserControl
{
    public static readonly DependencyProperty JobProperty =
        DependencyProperty.Register(nameof(Job), typeof(JobViewModel), typeof(CopyPanel), new PropertyMetadata(null, (d, _) => ((CopyPanel)d).Bindings.Update()));

    public CopyPanel()
    {
        InitializeComponent();
        SetExpanded(false);
    }

    /// <summary>"Más" / "Menos": pasar entre la ventana compacta y la completa.</summary>
    public event Action? ExpandRequested;

    public JobViewModel? Job
    {
        get => (JobViewModel?)GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }

    public void SetExpanded(bool expanded)
    {
        ExpandGlyph.Glyph = expanded ? "" : "";
        ExpandText.Text = L.Get(expanded ? "Panel_Less" : "Panel_More");
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();
}
