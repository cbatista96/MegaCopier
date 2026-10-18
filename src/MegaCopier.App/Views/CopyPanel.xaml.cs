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
        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        ShowPercents();
        Loaded += (_, _) =>
        {
            Theming.BarAppearance.Changed -= ShowPercents;
            Theming.BarAppearance.Changed += ShowPercents;
            ShowPercents();
        };
        Unloaded += (_, _) => Theming.BarAppearance.Changed -= ShowPercents;
    }

    /// <summary>El porcentaje a la derecha de cada barra, salvo que se pida dentro de la barra u oculto.</summary>
    private void ShowPercents()
    {
        TotalPercentBlock.Visibility = Theming.BarAppearance.For(Theming.BarRole.Total).Text == Theming.BarText.Outside ? Visibility.Visible : Visibility.Collapsed;
        FilePercentBlock.Visibility = Theming.BarAppearance.For(Theming.BarRole.File).Text == Theming.BarText.Outside ? Visibility.Visible : Visibility.Collapsed;
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
        ToolTipService.SetToolTip(ExpandButton, ExpandText.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ExpandButton, ExpandText.Text);
    }

    /// <summary>
    /// Ancho disponible. Los botones y los chips («En pausa», «Archivo») conservan siempre su texto: la ventana no
    /// deja estrecharse tanto como para cortarlos. Con poco sitio se ocultan los datos secundarios (bytes, resumen) y,
    /// con menos aún, el número de archivos y la velocidad (siguen en el título de la ventana y en Estado).
    /// </summary>
    private void Adapt(double width)
    {
        bool narrow = width < 600;
        bool tiny = width < 520;
        TotalBytesBlock.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        FileBytesBlock.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        DetailHost.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;

        // Si no cabe junto a los botones, el resumen pasa a su propia línea debajo.
        bool below = width < 900;
        Grid.SetRow(DetailHost, below ? 1 : 0);
        Grid.SetColumn(DetailHost, below ? 0 : 1);
        Grid.SetColumnSpan(DetailHost, below ? 3 : 1);
        DetailBlock.Margin = below ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 8, 0);
        FilesCountBlock.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        SpeedBlock.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        int keep = width < 560 ? 1 : 2;
        if (keep != JobViewModel.PathKeep)
        {
            JobViewModel.PathKeep = keep;
            Job?.Refresh();
        }
    }

    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();
}
