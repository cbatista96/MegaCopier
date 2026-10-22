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
        DependencyProperty.Register(nameof(Job), typeof(JobViewModel), typeof(CopyPanel), new PropertyMetadata(null, (d, _) =>
        {
            var panel = (CopyPanel)d;
            panel.Bindings.Update();
            panel.WatchJob();
        }));

    public CopyPanel()
    {
        InitializeComponent();
        MoreMeasure.Text = L.Get("Panel_More");
        LessMeasure.Text = L.Get("Panel_Less");
        SetExpanded(false);
        ShowPlayPause(animate: false);
        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        // El tamaño del archivo usa su ancho real: reservar el de un archivo anterior dejaba
        // un hueco entre la velocidad y el texto, además de la separación normal de la fila.
        foreach (var block in (TextBlock[])[FilesCountBlock, TotalBytesBlock, SpeedBlock])
        {
            block.SizeChanged += (_, e) => KeepWidth(block, e.NewSize.Width);
        }

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
    /// deja estrecharse tanto como para cortarlos. Los archivos copiados y los tamaños se ven siempre (el nombre de la
    /// copia y el del archivo se acortan antes); solo en la ventana más estrecha se ocultan la velocidad y el resumen.
    /// </summary>
    private void Adapt(double width)
    {
        bool tiny = width < 520;
        TotalBytesBlock.Visibility = Visibility.Visible;
        FileBytesBlock.Visibility = Visibility.Visible;
        FilesCountBlock.Visibility = Visibility.Visible;
        SpeedBlock.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        DetailHost.Visibility = width < 540 ? Visibility.Collapsed : Visibility.Visible;

        // Con poco sitio, los datos se juntan un poco más.
        double spacing = width < 700 ? 8 : 12;
        TotalRow.ColumnSpacing = spacing;
        FileRow.ColumnSpacing = spacing;

        // El resumen (al terminar: archivos, tamaño, tiempo y velocidad) va junto a los botones, en una o dos líneas,
        // nunca debajo: así la tarjeta no crece y no empuja lo de abajo (el paso entre copias de la ventana pequeña).
        DetailBlock.FontSize = width < 900 ? 12 : 14;
        DetailBlock.Margin = new Thickness(4, 0, 4, 0);
        int keep = width < 560 ? 1 : 2;
        if (keep != JobViewModel.PathKeep)
        {
            JobViewModel.PathKeep = keep;
            Job?.Refresh();
        }
    }

    /// <summary>
    /// Los contadores, el total y la velocidad conservan el mayor ancho que han necesitado y se alinean a la derecha.
    /// El tamaño del archivo conserva su ancho natural para mantenerlo junto a la velocidad, sin huecos adicionales.
    /// </summary>
    private static void KeepWidth(TextBlock block, double width)
    {
        if (width > block.MinWidth)
        {
            block.MinWidth = Math.Ceiling(width);
        }
    }

    private void ResetWidths()
    {
        foreach (var block in (TextBlock[])[FilesCountBlock, TotalBytesBlock, FileBytesBlock, SpeedBlock])
        {
            block.MinWidth = 0;
        }
    }

    /// <summary>El destino, a la derecha, ocupa lo que necesita hasta la mitad de la fila; el origen, el resto.</summary>
    private void DrivesRow_SizeChanged(object sender, SizeChangedEventArgs e) =>
        DestinationButton.MaxWidth = Math.Max(140, ((e.NewSize.Width - DrivesRow.ColumnSpacing) / 2) - DrivesArrow.ActualWidth - DrivesRow.ColumnSpacing);

    private void Expand_Click(object sender, RoutedEventArgs e) => ExpandRequested?.Invoke();
    // ---------------------------------------------------------------------------------------------------------
    // Pausa / Continuar: un solo botón; el icono de pausa se transforma en el de reproducir y al revés.

    private bool? _showingPause;
    private JobViewModel? _watched;

    private void WatchJob()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnJobPropertyChanged;
        }

        _watched = Job;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnJobPropertyChanged;
        }

        _showingPause = null; // otra copia: se muestra su estado sin animar
        ResetWidths();
        ShowPlayPause(animate: false);
    }

    private void OnJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(JobViewModel.CanPause) or nameof(JobViewModel.CanResume))
        {
            ShowPlayPause(animate: true);
        }
    }

    private void ShowPlayPause(bool animate)
    {
        bool pause = Job?.CanPause == true;
        string label = pause ? PauseLabel.Text : PlayLabel.Text;
        ToolTipService.SetToolTip(PlayPauseButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayPauseButton, label);
        if (_showingPause == pause)
        {
            return;
        }

        bool first = _showingPause is null;
        _showingPause = pause;
        animate &= !first && PlayPauseButton.ActualWidth > 0;
        PlayPauseIcon.MorphTo(play: !pause, animate);
        PauseLabel.Opacity = pause ? 1 : 0;
        PlayLabel.Opacity = pause ? 0 : 1;
    }
    // ---------------------------------------------------------------------------------------------------------
    // Menú «…»: las opciones que se activan llevan una marca a la derecha (sin el hueco de las casillas).

    private void OptionsMenu_Opening(object sender, object e)
    {
        UnattendedItem.KeyboardAcceleratorTextOverride = Job?.Unattended == true ? "✓" : "";
        VerifyItem.KeyboardAcceleratorTextOverride = Job?.Verify == true ? "✓" : "";
    }

    private void Unattended_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null)
        {
            Job.Unattended = !Job.Unattended;
        }
    }

    private void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null)
        {
            Job.Verify = !Job.Verify;
        }
    }
}
