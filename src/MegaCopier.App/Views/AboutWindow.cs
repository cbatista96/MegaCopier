using System.Diagnostics;
using System.Runtime.InteropServices;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MegaCopier.App.Views;

/// <summary>
/// «Acerca de MegaCopier»: una ventana pequeña y propia, con fondo acrílico, en lugar de un diálogo dentro de la ventana de
/// copia (en la ventana pequeña quedaba apretado). Mide lo que su contenido y se abre centrada sobre la ventana
/// principal; Esc o «Cerrar» la cierran.
/// </summary>
internal sealed partial class AboutWindow : Window
{
    private const double ContentWidth = 420;

    private static AboutWindow? _open;

    private readonly Grid _root = new();
    private readonly StackPanel _body = new() { Spacing = 10 };

    private AboutWindow(Window owner)
    {
        Title = L.Get("AboutDialog/Title");
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        var theme = ThemeManager.Current;
        SystemBackdrop = Appearance.CreateBackdrop(AppBackdrop.Acrylic, theme.Dark);
        _root.RequestedTheme = theme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        AppWindow.TitleBar.PreferredTheme = theme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var titleBar = new TextBlock
        {
            Text = Title,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var titleHost = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        titleHost.Children.Add(titleBar);
        _root.Children.Add(titleHost);
        SetTitleBar(titleHost);

        Build();
        Grid.SetRow(_body, 1);
        _root.Children.Add(_body);
        Content = _root;

        _root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                Close();
            }
        };
        _root.Loaded += (_, _) => FitTo(owner);
        Closed += (_, _) => _open = null;
    }

    /// <summary>Abre la ventana (o trae al frente la que ya está abierta).</summary>
    public static void Show(Window owner)
    {
        _open ??= new AboutWindow(owner);
        if (App.TestMode)
        {
            _open.AppWindow.Show(activateWindow: false); // en las pruebas, sin quitar el foco a nadie
            return;
        }

        _open.Activate();
    }

    private void Build()
    {
        _body.Padding = new Thickness(20, 4, 20, 18);
        _body.Width = ContentWidth;

        // Icono, nombre y versión.
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/MegaCopier.ico")),
            Width = 40,
            Height = 40,
        });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "MegaCopier", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        names.Children.Add(new TextBlock
        {
            Text = L.F("About", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "")
                + (Engine.DataFolder.IsPortable ? " · " + L.F("About_Portable", ViewModels.Format.ShortPath(Engine.DataFolder.Path, 2)) : ""),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = ContentWidth - 92,
        });
        header.Children.Add(names);
        _body.Children.Add(header);

        _body.Children.Add(new TextBlock { Text = L.Get("About_Description/Text"), TextWrapping = TextWrapping.Wrap });
        _body.Children.Add(new TextBlock
        {
            Text = L.Get("About_Features/Text"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.85,
        });

        // Quién lo hace: WML lleva a su web.
        var credit = new TextBlock { TextWrapping = TextWrapping.Wrap };
        credit.Inlines.Add(new Run { Text = L.Get("About_DevelopedBy/Text") + " " });
        var link = new Hyperlink { NavigateUri = new Uri("http://worldmarketinglabs.com/") };
        link.Inlines.Add(new Run { Text = "WML" });
        ToolTipService.SetToolTip(link, "worldmarketinglabs.com");
        credit.Inlines.Add(link);
        _body.Children.Add(credit);

        // Abajo: la carpeta de datos a la izquierda y «Cerrar» a la derecha.
        var footer = new Grid { Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var data = new HyperlinkButton
        {
            Content = new TextBlock { Text = L.Get("About_DataFolder/Content"), TextWrapping = TextWrapping.Wrap },
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        data.Click += (_, _) =>
        {
            string folder = Path.GetDirectoryName(AppSettings.FilePath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        };
        footer.Children.Add(data);
        var close = new Button
        {
            Content = L.Get("AboutDialog/CloseButtonText"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            MinWidth = 96,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        _body.Children.Add(footer);
    }

    /// <summary>El tamaño justo del contenido, centrada sobre la ventana principal (y dentro de la pantalla).</summary>
    private void FitTo(Window owner)
    {
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        _root.Measure(new Windows.Foundation.Size(ContentWidth, double.PositiveInfinity));
        int width = (int)Math.Ceiling(ContentWidth * scale);
        int height = (int)Math.Ceiling(_root.DesiredSize.Height * scale);
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var ownerRect = new Windows.Graphics.RectInt32(owner.AppWindow.Position.X, owner.AppWindow.Position.Y, owner.AppWindow.Size.Width, owner.AppWindow.Size.Height);
        int x = Math.Clamp(ownerRect.X + ((ownerRect.Width - width) / 2), area.X, Math.Max(area.X, area.X + area.Width - width));
        int y = Math.Clamp(ownerRect.Y + ((ownerRect.Height - height) / 2), area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
        AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
