using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

/// <summary>
/// Qué hay en un punto de la pantalla según la accesibilidad de Windows (UI Automation), sin tocar el Explorador:
/// un elemento de una lista de archivos (con su nombre visible), el fondo de la lista u otra cosa.
/// Llamadas COM por la tabla de métodos, válidas con Native AOT. Debe llamarse desde un hilo STA.
/// </summary>
public static unsafe partial class UiaHitTest
{
    private static readonly Guid ClsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid IidUiAutomation = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");

    private const int ListItem = 50007, List = 50008, DataItem = 50029;

    public static DropSpot At(int x, int y, out string? itemName)
    {
        itemName = null;
        void* automation = null;
        void* walker = null;
        void* element = null;
        Guid clsid = ClsidCuiAutomation, iid = IidUiAutomation;
        if (CoCreateInstance(&clsid, null, 1 /* CLSCTX_INPROC_SERVER */, &iid, &automation) < 0 || automation == null)
        {
            return DropSpot.Unknown;
        }

        try
        {
            // IUIAutomation::ElementFromPoint (índice 7) e IUIAutomation::get_ControlViewWalker (índice 14)
            if (((delegate* unmanaged[Stdcall]<void*, Point, void**, int>)(*(void***)automation)[7])(automation, new Point(x, y), &element) < 0 || element == null
                || ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)automation)[14])(automation, &walker) < 0 || walker == null)
            {
                return DropSpot.Unknown;
            }

            // Del elemento más profundo (el texto del nombre, el icono...) hacia arriba hasta el elemento de la lista.
            for (int depth = 0; depth < 8 && element != null; depth++)
            {
                int type = 0;
                // IUIAutomationElement::get_CurrentControlType (índice 21)
                ((delegate* unmanaged[Stdcall]<void*, int*, int>)(*(void***)element)[21])(element, &type);
                if (type is ListItem or DataItem)
                {
                    itemName = Name(element);
                    return string.IsNullOrEmpty(itemName) ? DropSpot.Unknown : DropSpot.Item;
                }

                if (type == List)
                {
                    return DropSpot.Background;
                }

                void* parent = null;
                // IUIAutomationTreeWalker::GetParentElement (índice 3)
                ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)(*(void***)walker)[3])(walker, element, &parent);
                ExplorerLocator.Release(element);
                element = parent;
            }

            return DropSpot.Unknown;
        }
        finally
        {
            ExplorerLocator.Release(element);
            ExplorerLocator.Release(walker);
            ExplorerLocator.Release(automation);
        }
    }

    private static string? Name(void* element)
    {
        nint bstr = 0;
        // IUIAutomationElement::get_CurrentName (índice 23)
        if (((delegate* unmanaged[Stdcall]<void*, nint*, int>)(*(void***)element)[23])(element, &bstr) < 0 || bstr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, void** result);
}
