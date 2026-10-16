using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using MegaCopier.App.Services;
using MegaCopier.App.Theming;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace MegaCopier.App.Views;

/// <summary>Un archivo o carpeta de la bandeja.</summary>
public sealed class ShelfItem(string path)
{
    public string Path => path;

    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    public string FullFolder => System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) ?? "";

    /// <summary>La carpeta abreviada como en el panel (unidad, ... y las dos últimas carpetas): entera no cabe.</summary>
    public string Folder => Format.ShortPath(FullFolder, 2);

    /// <summary>Nombre accesible del botón de quitar (con el nombre del elemento, no un "Quitar" repetido).</summary>
    public string RemoveName => L.F("Shelf_RemoveNamed", Name);

    public string Glyph =>Directory.Exists(path) ? "" : "";
}

/// <summary>
/// Bandeja de arrastre: una ventanita siempre visible donde se van soltando archivos y carpetas de cualquier sitio
/// (varias carpetas, varias ventanas del Explorador) para luego copiarlos o moverlos todos juntos, o arrastrarlos fuera.
/// Lo que hay en ella se guarda: sigue ahí al cerrarla o reiniciar.
/// </summary>
public sealed partial class ShelfWindow : Window
{
    private readonly App _app;
    private readonly ObservableCollection<ShelfItem> _items = [];
    private CancellationTokenSource? _measuring;

    public ShelfWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Title = L.Get("Shelf_WindowTitle");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "MegaCopier.ico"));

        // Siempre encima, sin maximizar ni minimizar, a la derecha de la pantalla.
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int width = (int)(320 * scale), height = (int)(420 * scale);
        var work = DisplayArea.GetFromWindowId(app.Window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + work.Width - width - (int)(24 * scale), work.Y + ((work.Height - height) / 2), width, height));

        List.ItemsSource = _items;
        ApplyTheme();
        foreach (string path in Load())
        {
            _items.Add(new ShelfItem(path));
        }

        Update();
        Root.Loaded += (_, _) => MainWindow.NameIconButtons(Root);
        Root.SizeChanged += (_, _) => Audit();
    }

    private static string StorePath => DataFolder.Combine("shelf.txt");

    public IReadOnlyList<string> Paths => [.. _items.Select(i => i.Path)];

    public void ShowWindow()
    {
        // Modo de prueba: sin quitar el foco a lo que el usuario esté haciendo.
        AppWindow.Show(activateWindow: !App.TestMode);
        if (!App.TestMode)
        {
            Activate();
        }
    }

    public void ApplyTheme()
    {
        var theme = ThemeManager.Current;
        Root.RequestedTheme = theme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        AppWindow.TitleBar.PreferredTheme = theme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        SystemBackdrop = Appearance.CreateBackdrop(theme.Backdrop);
    }

    /// <summary>Añade (sin repetir y solo lo que existe).</summary>
    public void Add(IEnumerable<string> paths)
    {
        foreach (string raw in paths)
        {
            string path = Path.GetFullPath(raw);
            if ((File.Exists(path) || Directory.Exists(path)) && !_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _items.Add(new ShelfItem(path));
            }
        }

        Save();
        Update();
    }

    /// <summary>Copiar o mover todo lo de la bandeja: se elige el destino en la ventana principal, como siempre.</summary>
    public async Task<bool> SendAsync(bool move)
    {
        var paths = _items.Select(i => i.Path).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (paths.Count == 0)
        {
            return false;
        }

        _app.Window.ShowAndActivate();
        if (!await _app.Window.PickDestinationAndCopy(paths, move))
        {
            return false;
        }

        // Ya va en la cola: la bandeja queda libre para lo siguiente.
        _items.Clear();
        Save();
        Update();
        return true;
    }

    private void Update()
    {
        bool any = _items.Count > 0;
        DropZone.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        List.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = MoveButton.IsEnabled = ClearButton.IsEnabled = any;
        string count = _items.Count == 1 ? L.Get("Shelf_One") : L.F("Shelf_Many", _items.Count);
        Summary.Text = any ? count : "";

        // El tamaño total, en segundo plano (una carpeta grande tarda en medirse).
        _measuring?.Cancel();
        if (!any)
        {
            return;
        }

        var cancellation = _measuring = new CancellationTokenSource();
        var paths = _items.Select(i => i.Path).ToList();
        _ = Task.Run(() =>
        {
            try
            {
                long size = QueueTimeline.MeasureSources(paths, cancellation.Token);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        Summary.Text = $"{count} · {Format.Bytes(size)}";
                        Audit();
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private static List<string> Load()
    {
        try
        {
            return File.Exists(StorePath) ? [.. File.ReadAllLines(StorePath).Where(p => p.Length > 0 && (File.Exists(p) || Directory.Exists(p)))] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllLines(StorePath, _items.Select(i => i.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Audit()
    {
        if (TextAudit.Enabled && AppWindow.IsVisible)
        {
            // Cuando los textos nuevos ya están colocados (justo después de cambiarlos aún miden lo de antes).
            var audit = DispatcherQueue.CreateTimer();
            audit.Interval = TimeSpan.FromMilliseconds(400);
            audit.IsRepeating = false;
            audit.Tick += (_, _) => TextAudit.Run(Root, $"Bandeja {AppWindow.Size.Width}px");
            audit.Start();
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = L.Get("Shelf_DropCaption");
            DropZone.BorderBrush = ThemeManager.AccentBrush;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = ThemeManager.BorderBrush;

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = ThemeManager.BorderBrush;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            Add(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)));
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Arrastrar desde la bandeja: al Explorador, al escritorio o a la ventana de MegaCopier.</summary>
    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<ShelfItem>().Select(i => i.Path).ToList();
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (string path in paths)
                {
                    items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
                }

                request.SetData(items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && _items.FirstOrDefault(i => i.Path == path) is { } item)
        {
            _items.Remove(item);
            Save();
            Update();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        Save();
        Update();
    }

    private async void Copy_Click(object sender, RoutedEventArgs e) => await SendAsync(move: false);

    private async void Move_Click(object sender, RoutedEventArgs e) => await SendAsync(move: true);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
