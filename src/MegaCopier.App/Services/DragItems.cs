using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace MegaCopier.App.Services;

/// <summary>
/// Qué se está arrastrando, para los avisos al soltar («Añadir «Vídeos» a esta copia»). Los nombres se leen una vez
/// por arrastre, en segundo plano: mientras tanto el aviso es el general.
/// </summary>
internal static class DragItems
{
    private static DataPackageView? _view;
    private static long _lastAsked;
    private static IReadOnlyList<string> _names = [];

    /// <summary>«Vídeos» si es uno, «3 elementos» si son varios; vacío si aún no se sabe.</summary>
    public static string What(DataPackageView view)
    {
        // Un arrastre nuevo: otro paquete de datos, o el anterior terminó hace rato.
        if (!ReferenceEquals(view, _view) || Stopwatch.GetElapsedTime(_lastAsked).TotalMilliseconds > 800)
        {
            _view = view;
            _names = [];
            _ = LoadAsync(view);
        }

        _lastAsked = Stopwatch.GetTimestamp();
        return _names.Count switch
        {
            0 => "",
            1 => L.F("Drop_One", _names[0]),
            _ => L.F("Drop_Many", _names.Count),
        };
    }

    private static async Task LoadAsync(DataPackageView view)
    {
        try
        {
            var items = await view.GetStorageItemsAsync();
            if (ReferenceEquals(view, _view))
            {
                _names = [.. items.Select(i => i.Name).Where(n => n.Length > 0)];
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Sin nombres: el aviso se queda en el general.
        }
    }
}
