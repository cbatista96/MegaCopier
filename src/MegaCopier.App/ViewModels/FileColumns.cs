using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace MegaCopier.App.ViewModels;

/// <summary>
/// Anchos de las columnas de la lista de archivos (Origen, Tamaño, Destino), compartidos por la cabecera y todas las
/// filas. El usuario los cambia arrastrando las líneas divisorias de la cabecera; se recuerdan en los ajustes.
/// Origen y Tamaño se guardan en píxeles (0 = el reparto de siempre); Destino ocupa lo que queda.
/// </summary>
public sealed partial class FileColumns : INotifyPropertyChanged
{
    public const double MinSource = 120, MinSize = 56, MinDestination = 90, DefaultSize = 80;

    public static FileColumns Current { get; } = new();

    private double _source;
    private double _size;
    private bool _showDestination = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Ancho elegido para Origen en píxeles (0 = reparto automático, tres quintos del espacio).</summary>
    public double SourcePixels => _source;

    public double SizePixels => _size > 0 ? _size : DefaultSize;

    /// <summary>Con la lista estrecha, Destino se oculta y Origen ocupa todo el ancho que queda.</summary>
    public bool ShowDestination
    {
        get => _showDestination;
        set
        {
            if (_showDestination != value)
            {
                _showDestination = value;
                Notify();
            }
        }
    }

    public GridLength Source => _source > 0 && _showDestination ? new GridLength(_source) : new GridLength(3, GridUnitType.Star);

    public GridLength Size => new(SizePixels);

    public GridLength Destination => !_showDestination ? new GridLength(0) : new GridLength(_source > 0 ? 1 : 2, GridUnitType.Star);

    /// <summary>Aplica los anchos guardados (o los cambiados al arrastrar). Valores fuera de rango se ajustan.</summary>
    public void Set(double source, double size)
    {
        source = source <= 0 ? 0 : Math.Max(MinSource, Math.Round(source));
        size = size <= 0 ? 0 : Math.Max(MinSize, Math.Round(size));
        if (source != _source || size != _size)
        {
            _source = source;
            _size = size;
            Notify();
        }
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Destination)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDestination)));
    }
}
