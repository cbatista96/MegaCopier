using MegaCopier.App.Theming;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MegaCopier.App.Services;

/// <summary>
/// El menú del clic derecho en la bandeja, con el aspecto de WinUI 3 (como los del resto de MegaCopier: esquinas
/// redondeadas, tema claro u oscuro, iconos del conjunto elegido) en lugar del menú clásico de Win32. Un menú de WinUI
/// necesita una ventana: se usa una diminuta, sin borde ni botón en la barra de tareas, en la posición del puntero; el
/// menú se dibuja fuera de ella y, al cerrarse, la ventana se oculta.
/// </summary>
internal sealed partial class TrayMenuWindow : Window
{
    private readonly Grid _root = new();
    private MenuFlyout? _menu;

    /// <summary>La AppWindow, guardada: pedírsela a WinUI al cerrar cada menú creaba otro envoltorio cada vez.</summary>
    private readonly AppWindow _appWindow;

    public TrayMenuWindow()
    {
        Title = "MegaCopier";
        Content = _root;
        ExtendsContentIntoTitleBar = true;
        var presenter = OverlappedPresenter.CreateForContextMenu();
        _appWindow = AppWindow;
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;

        // Windows no deja una ventana más pequeña que su mínimo (136 × 39): se hace del todo transparente para que solo
        // se vea el menú, que va en su propia ventana emergente.
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowLongPtrW(handle, -20 /* GWL_EXSTYLE */, GetWindowLongPtrW(handle, -20) | 0x80000 /* WS_EX_LAYERED */ | 0x80 /* WS_EX_TOOLWINDOW */);
        SetLayeredWindowAttributes(handle, 0, 0, 0x2 /* LWA_ALPHA */);
        Activated += (_, e) =>
        {
            // Al pulsar fuera del menú (la ventana deja de estar activa), se cierra como un menú normal.
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _menu?.Hide();
            }
        };
    }

    /// <summary>Abre el menú en ese punto de la pantalla (en píxeles).</summary>
    public void Show(IReadOnlyList<TrayMenuItem> items, int x, int y)
    {
        _menu?.Hide();
        _root.RequestedTheme = ThemeManager.Current.Dark ? ElementTheme.Dark : ElementTheme.Light;
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };

        // El mismo acrílico que los demás menús de MegaCopier (Materials), sea cual sea el material de la ventana.
        var presenter = new Style(typeof(MenuFlyoutPresenter));
        presenter.Setters.Add(new Setter(MenuFlyoutPresenter.SystemBackdropProperty, new PopupBackdrop()));
        menu.MenuFlyoutPresenterStyle = presenter;
        foreach (var item in items)
        {
            menu.Items.Add(Build(item));
        }

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
            {
                _menu = null;
                _appWindow.Hide();
            }
        };
        _menu = menu;

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, 1, 1));
        _appWindow.Show(activateWindow: !App.TestMode); // en las pruebas, sin quitar el foco a nadie
        if (!App.TestMode)
        {
            Activate();
        }

        void Open()
        {
            if (!ReferenceEquals(_menu, menu))
            {
                return; // mientras cargaba se pidió otro menú
            }

            ThemedIcons.Apply(menu.Items, _root.XamlRoot?.RasterizationScale ?? 1);
            menu.ShowAt(_root, new FlyoutShowOptions { Position = new Windows.Foundation.Point(0, 0), ShowMode = FlyoutShowMode.Standard });
        }

        // La primera vez, la ventana aún no ha cargado su contenido: el menú se abre en cuanto lo tiene.
        if (_root.XamlRoot is null)
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                _root.Loaded -= OnLoaded;
                Open();
            }

            _root.Loaded += OnLoaded;
            return;
        }

        Open();
    }

    private static MenuFlyoutItemBase Build(TrayMenuItem item)
    {
        if (item.Text is null)
        {
            return new MenuFlyoutSeparator();
        }

        MenuFlyoutItem entry = item.Checkable
            ? new ToggleMenuFlyoutItem { Text = item.Text, IsChecked = item.Checked }
            : new MenuFlyoutItem { Text = item.Text };
        entry.IsEnabled = item.Enabled;
        if (item.Glyph is { } glyph)
        {
            entry.Icon = new FontIcon { Glyph = glyph };
        }

        if (item.OnClick is { } click)
        {
            entry.Click += (_, _) => click();
        }

        return entry;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
