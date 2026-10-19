using System.Diagnostics;
using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MegaCopier.App.Views;

/// <summary>Pestaña Archivos: la lista viva del trabajo, con orden, filtros, búsqueda y exportaciones.</summary>
public sealed partial class FilesTab : UserControl
{
    public static readonly DependencyProperty JobProperty =
        DependencyProperty.Register(nameof(Job), typeof(JobViewModel), typeof(FilesTab), new PropertyMetadata(null, (d, _) => ((FilesTab)d).OnJobChanged()));

    private FileListViewModel? _files;

    public FilesTab()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        FilterBox.SelectedIndex = 0;
        var settings = ((App)Application.Current).Settings;
        HideFinishedBox.IsChecked = settings.HideFinishedFiles;
        ShowHashesCheck(settings.FilesShowHashes);
        UpdateRail();

        // Columnas: los anchos guardados, y las líneas divisorias de la cabecera para cambiarlos.
        FileColumns.Current.Set(settings.FilesSourceWidth, settings.FilesSizeWidth);
        FileColumns.Current.PropertyChanged += (_, _) => ApplyHeaderColumns();
        ApplyHeaderColumns();
        double startSource = 0, startSize = 0;
        SourceSplitter.Started += () => startSource = HeaderSourceColumn.ActualWidth;
        SourceSplitter.Dragged += delta => ResizeColumns(startSource + delta, FileColumns.Current.SizePixels);
        SizeSplitter.Started += () =>
        {
            startSource = HeaderSourceColumn.ActualWidth;
            startSize = HeaderSizeColumn.ActualWidth;
        };
        SizeSplitter.Dragged += delta => ResizeColumns(startSource, startSize + delta);
        SourceSplitter.Finished += SaveColumns;
        SizeSplitter.Finished += SaveColumns;
        SourceSplitter.Reset += () =>
        {
            FileColumns.Current.Set(0, FileColumns.Current.SizePixels);
            SaveColumns();
        };
        SizeSplitter.Reset += () =>
        {
            FileColumns.Current.Set(FileColumns.Current.SourcePixels, 0);
            SaveColumns();
        };
    }

    private bool ShowHashes { get; set; }

    /// <summary>«Mostrar las sumas»: con su icono, y una marca a la derecha cuando están a la vista.</summary>
    private void ShowHashesCheck(bool on)
    {
        ShowHashes = on;
        ShowHashesItem.KeyboardAcceleratorTextOverride = on ? "✓" : "";
    }

    /// <summary>
    /// Cambia los anchos al arrastrar una línea divisoria, sin dejar que una columna quede tan estrecha que no se lea
    /// (ni que Destino desaparezca).
    /// </summary>
    private void ResizeColumns(double source, double size)
    {
        double available = HeaderSourceColumn.ActualWidth + HeaderSizeColumn.ActualWidth + HeaderDestinationColumn.ActualWidth;
        double destinationMin = FileColumns.Current.ShowDestination ? FileColumns.MinDestination : 0;
        size = Math.Clamp(size, FileColumns.MinSize, Math.Max(FileColumns.MinSize, available - FileColumns.MinSource - destinationMin));
        source = Math.Clamp(source, FileColumns.MinSource, Math.Max(FileColumns.MinSource, available - size - destinationMin));
        FileColumns.Current.Set(source, size);
    }

    private void SaveColumns()
    {
        var settings = ((App)Application.Current).Settings;
        settings.FilesSourceWidth = FileColumns.Current.SourcePixels;
        settings.FilesSizeWidth = FileColumns.Current.SizePixels == FileColumns.DefaultSize ? 0 : FileColumns.Current.SizePixels;
        settings.Save();
    }

    private void ApplyHeaderColumns()
    {
        HeaderSourceColumn.Width = FileColumns.Current.Source;
        HeaderSizeColumn.Width = FileColumns.Current.Size;
        HeaderDestinationColumn.Width = FileColumns.Current.Destination;
        SizeSplitter.Visibility = FileColumns.Current.ShowDestination ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(PlaceSplitters); // tras medir las columnas nuevas
    }

    /// <summary>Las líneas divisorias van en el centro del hueco entre columnas.</summary>
    private void Header_SizeChanged(object sender, SizeChangedEventArgs e) => PlaceSplitters();

    private void PlaceSplitters()
    {
        double gap = Header.ColumnSpacing;
        double sourceEnd = Header.ColumnDefinitions[0].ActualWidth + gap + HeaderSourceColumn.ActualWidth;
        double sizeEnd = sourceEnd + gap + HeaderSizeColumn.ActualWidth;
        Canvas.SetLeft(SourceSplitter, sourceEnd + gap / 2 - SourceSplitter.Width / 2);
        Canvas.SetLeft(SizeSplitter, sizeEnd + gap / 2 - SizeSplitter.Width / 2);
        double top = (Header.ActualHeight - SourceSplitter.Height) / 2;
        Canvas.SetTop(SourceSplitter, top);
        Canvas.SetTop(SizeSplitter, top);
    }

    public JobViewModel? Job
    {
        get => (JobViewModel?)GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }

    public FileListViewModel? Files => _files;

    /// <summary>
    /// La barra de botones de la izquierda siempre enseña todos sus botones: si la lista es baja, los botones y sus
    /// iconos se hacen algo más pequeños; al agrandarla vuelven a su tamaño normal.
    /// </summary>
    private void ListArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double Normal = 34, Smallest = 22, Fixed = 46; // marco, separadores y espacios entre botones
        var buttons = Rail.Children.OfType<Button>().ToList();
        // La barra empieza a la altura del primer archivo: cuenta solo el alto de la lista, sin la cabecera.
        double listHeight = e.NewSize.Height - Header.ActualHeight - ListArea.RowSpacing;
        double height = Math.Clamp(Math.Floor((listHeight - Fixed) / buttons.Count) - 2, Smallest, Normal);
        double scale = height / Normal;
        foreach (var button in buttons)
        {
            button.Height = height;
            button.Width = Math.Round(38 * Math.Max(0.8, scale));
            if (button.Content is Controls.McIcon icon)
            {
                icon.FontSize = Math.Round((icon.Tag as double? ?? (double)(icon.Tag = icon.FontSize)) * Math.Max(0.75, scale));
            }
        }

        foreach (var line in Rail.Children.OfType<Microsoft.UI.Xaml.Shapes.Rectangle>())
        {
            line.Margin = new Thickness(6, scale < 0.9 ? 2 : 4, 6, scale < 0.9 ? 2 : 4);
        }
    }

    /// <summary>
    /// Las casillas van siempre junto a la búsqueda, que es la que encoge; solo en un ancho mínimo bajan a una segunda
    /// fila. La columna Destino se oculta si no cabe (queda en la ayuda de cada fila); las sumas solo caben si la lista
    /// es ancha y se pidieron en el menú.
    /// </summary>
    private void Adapt(double width)
    {
        bool twoRows = width < 560;
        Grid.SetRow(ListToggles, twoRows ? 1 : 0);
        Grid.SetColumn(ListToggles, twoRows ? 0 : 1);
        Grid.SetColumnSpan(ListToggles, twoRows ? 5 : 1);

        bool destination = width >= 620;
        bool hashes = width >= 900 && ShowHashes;
        FileColumns.Current.ShowDestination = destination;
        if (hashes != FileRowViewModel.ShowHashes || destination != FileRowViewModel.ShowDestination)
        {
            FileRowViewModel.ShowHashes = hashes;
            FileRowViewModel.ShowDestination = destination;
            foreach (var row in _files?.Items ?? [])
            {
                row.Refresh();
            }
        }

        SourceHashHeader.Visibility = hashes ? Visibility.Visible : Visibility.Collapsed;
        TargetHashHeader.Visibility = hashes ? Visibility.Visible : Visibility.Collapsed;
        DestinationHeader.Visibility = destination ? Visibility.Visible : Visibility.Collapsed;
        ApplyHeaderColumns();
    }

    private void ShowHashes_Click(object sender, RoutedEventArgs e)
    {
        var settings = ((App)Application.Current).Settings;
        ShowHashesCheck(!ShowHashes);
        settings.FilesShowHashes = ShowHashes;
        settings.Save();
        Adapt(ActualWidth);
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRail();

    /// <summary>Ordenar y quitar solo valen para archivos que aún no han empezado.</summary>
    private void UpdateRail()
    {
        bool pending = Selected().Any(r => r.IsPending);
        TopButton.IsEnabled = UpButton.IsEnabled = DownButton.IsEnabled = BottomButton.IsEnabled = RemoveButton.IsEnabled = pending;
        SaveButton.IsEnabled = Job is not null;
    }

    private void OnJobChanged()
    {
        if (_files is not null)
        {
            _files.CurrentChanged -= OnCurrentChanged;
            _files.PropertyChanged -= OnFilesChanged;
        }

        _files = Job?.Files;
        if (_files is not null)
        {
            _files.HideFinished = HideFinishedBox.IsChecked == true;
            _files.Filter = (FileFilter)Math.Max(0, FilterBox.SelectedIndex);
            _files.Search = SearchBox.Text;
            _files.CurrentChanged += OnCurrentChanged;
            _files.PropertyChanged += OnFilesChanged;
        }

        List.ItemsSource = _files?.Items;
        UpdateCount();
        UpdateRail();
    }

    /// <summary>La lista se crea cuando el trabajo arranca: se reintenta al refrescar.</summary>
    public void Refresh()
    {
        if (_files is null && Job?.Files is not null)
        {
            OnJobChanged();
        }
    }

    private void OnFilesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = _files?.CountText ?? "";
        EmptyText.Visibility = _files is null || _files.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCurrentChanged(FileRowViewModel row)
    {
        if (AutoScrollBox.IsChecked == true)
        {
            List.ScrollIntoView(row);
        }
    }

    private List<FileRowViewModel> Selected() => List.SelectedItems.OfType<FileRowViewModel>().ToList();

    /// <summary>Selección en el orden de la lista (para mover varios sin que se adelanten unos a otros).</summary>
    private List<FileRowViewModel> SelectedInOrder() =>
        [.. Selected().OrderBy(r => _files?.Items.IndexOf(r) ?? 0)];

    private void MoveTop_Click(object sender, RoutedEventArgs e) =>
        Move(rows => rows.AsEnumerable().Reverse().ToList().ForEach(r => _files?.MoveToTop(r)));

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(rows => rows.ForEach(r => _files?.MoveUp(r)));

    private void MoveDown_Click(object sender, RoutedEventArgs e) =>
        Move(rows => rows.AsEnumerable().Reverse().ToList().ForEach(r => _files?.MoveDown(r)));

    private void MoveBottom_Click(object sender, RoutedEventArgs e) => Move(rows => rows.ForEach(r => _files?.MoveToBottom(r)));

    /// <summary>Mueve los elegidos y los deja elegidos (y a la vista), para poder seguir moviéndolos.</summary>
    private void Move(Action<List<FileRowViewModel>> move)
    {
        var rows = SelectedInOrder().Where(r => r.IsPending).ToList();
        if (rows.Count == 0 || _files is null)
        {
            return;
        }

        move(rows);
        List.SelectedItems.Clear();
        foreach (var row in rows.Where(_files.Items.Contains))
        {
            List.SelectedItems.Add(row);
        }

        List.ScrollIntoView(rows[0]);
        UpdateRail();
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => Selected().ForEach(r => _files?.Remove(r));

    private void SelectAll_Click(object sender, RoutedEventArgs e) => List.SelectAll();

    /// <summary>Selecciona todos los archivos con la extensión de los elegidos.</summary>
    private void SelectSameExtension_Click(object sender, RoutedEventArgs e) =>
        SelectWhere(Selected().Select(r => Path.GetExtension(r.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase), r => Path.GetExtension(r.Name));

    /// <summary>Selecciona todos los archivos de las carpetas de los elegidos.</summary>
    private void SelectSameFolder_Click(object sender, RoutedEventArgs e) =>
        SelectWhere(Selected().Select(r => r.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase), r => r.Folder);

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected().ToHashSet();
        List.SelectedItems.Clear();
        foreach (var row in List.Items.OfType<FileRowViewModel>().Where(r => !selected.Contains(r)))
        {
            List.SelectedItems.Add(row);
        }
    }

    private void SelectWhere(HashSet<string> keys, Func<FileRowViewModel, string> key)
    {
        if (keys.Count == 0)
        {
            return;
        }

        foreach (var row in List.Items.OfType<FileRowViewModel>().Where(r => keys.Contains(key(r)) && !List.SelectedItems.Contains(r)))
        {
            List.SelectedItems.Add(row);
        }
    }

    /// <summary>«+»: añadir archivos o carpetas al final de la lista, con el mismo destino.</summary>
    private void Add_Click(object sender, RoutedEventArgs e) => AddSources(Pickers.PickAny(L.Get("Picker_AddTitle")));

    private void AddSources(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0 || Job is null)
        {
            return;
        }

        try
        {
            _ = ((App)Application.Current).AddToJobAsync(Job.Job, sources);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Engine.CopyPlanException)
        {
            ((App)Application.Current).Notify(L.Get("Files_AddFailed"), ex.Message);
        }
    }

    /// <summary>Guarda lo que queda por copiar (con su destino y sus opciones) para seguir más tarde con «Cargar».</summary>
    private async void SaveList_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null && await Pickers.SaveAsync(Reports.FileNameFor(Job.Title), ".mclist", L.Get("Files_ListType")) is { } path)
        {
            int remaining = Reports.SaveRemaining(Job.Job, path);
            ((App)Application.Current).Notify(L.Get("Files_ListSaved"), L.F("Files_ListSavedText", remaining, Path.GetFileName(path)));
        }
    }

    /// <summary>Carga una lista guardada y la copia en seguida a su destino (o a otro, si ese ya no está).</summary>
    private void LoadList_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).Window.LoadListAsync();

    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null && await Pickers.SaveAsync("MegaCopier-informe", ".html", "HTML") is { } path)
        {
            Reports.ExportHtml(Job, path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null && await Pickers.SaveAsync("MegaCopier-informe", ".csv", "CSV") is { } path)
        {
            Reports.ExportCsv(Job, path);
        }
    }

    private void CopyHashes_Click(object sender, RoutedEventArgs e) => SetClipboard(_files?.HashesText() ?? "");

    private void Retry_Click(object sender, RoutedEventArgs e) => Job?.Resume();

    private void OpenSource_Click(object sender, RoutedEventArgs e) => Reveal(Selected().FirstOrDefault()?.SourcePath);

    private void OpenTarget_Click(object sender, RoutedEventArgs e) => Reveal(Selected().FirstOrDefault()?.DestinationPath);

    private void CopyPath_Click(object sender, RoutedEventArgs e) =>
        SetClipboard(string.Join("\r\n", Selected().Select(r => r.SourcePath)));

    private static void Reveal(string? path)
    {
        if (path is null)
        {
            return;
        }

        string arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    private static void SetClipboard(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_files is not null)
        {
            _files.Search = SearchBox.Text;
        }
    }

    private void HideFinished_Changed(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.Settings.HideFinishedFiles != (HideFinishedBox.IsChecked == true))
        {
            app.Settings.HideFinishedFiles = HideFinishedBox.IsChecked == true;
            app.Settings.Save();
        }

        if (_files is not null)
        {
            _files.HideFinished = HideFinishedBox.IsChecked == true;
        }
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_files is not null && FilterBox.SelectedIndex >= 0)
        {
            _files.Filter = (FileFilter)FilterBox.SelectedIndex;
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Arrastrar: reordenar los pendientes y soltar archivos del Explorador en la posición elegida.

    private List<FileRowViewModel>? _dragged;

    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Solo se mueven los que aún no han empezado.
        _dragged = e.Items.OfType<FileRowViewModel>().Where(r => r.IsPending).ToList();
        if (_dragged.Count == 0)
        {
            _dragged = null;
            e.Cancel = true;
            return;
        }

        e.Data.SetText("megacopier-rows");
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    private void List_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragged = null;
        HideDropFeedback();
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        bool files = e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems);
        if (_dragged is null && !files)
        {
            return;
        }

        e.Handled = true; // que no lo trate la ventana como una copia nueva
        if (Job is null)
        {
            return;
        }

        bool moving = _dragged is not null;
        e.AcceptedOperation = moving
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        _dragWhat = moving ? "" : DragItems.What(e.DataView);
        e.DragUIOverride.Caption = ShowDropFeedback(e.GetPosition(List), moving);
        e.DragUIOverride.IsGlyphVisible = false;
    }

    /// <summary>Línea en la posición de destino y aviso de lo que va a pasar. Devuelve el texto del aviso.</summary>
    private string ShowDropFeedback(Windows.Foundation.Point pointer, bool moving)
    {
        var spot = DropTarget.Find(List, pointer, DropLayer, FirstPendingIndex());
        DropTarget.ShowLine(DropLine, spot.LineY, 4, DropLayer.ActualWidth - 8);
        int position = spot.Index + 1;
        if (moving)
        {
            ShowHint("", L.F("Drop_MoveTo", position));
        }
        else
        {
            ShowHint("", (_files is null, _dragWhat.Length > 0) switch
            {
                (true, true) => L.F("Drop_AddWhatHere", _dragWhat),
                (true, false) => L.Get("Drop_AddToJob"),
                (false, true) => L.F("Drop_InsertWhatAt", _dragWhat, position),
                _ => L.F("Drop_InsertAt", position),
            });
        }

        return DropHintText.Text;
    }

    /// <summary>Lo que se arrastra desde fuera («Vídeos», «3 elementos»), para el aviso.</summary>
    private string _dragWhat = "";

    private int? BeforeIdAt(Windows.Foundation.Point pointer)
    {
        var spot = DropTarget.Find(List, pointer, DropLayer, FirstPendingIndex());
        return spot.Index < List.Items.Count && List.Items[spot.Index] is FileRowViewModel before ? before.Id : null;
    }

    private void MoveRows(IReadOnlyList<FileRowViewModel> rows, int? beforeId)
    {
        // En el orden de la lista: A y B soltados antes de X quedan A, B, X.
        foreach (var row in rows.Where(r => r.Id != beforeId))
        {
            Job?.Job.MoveFile(row.Id, beforeId);
        }
    }

    // Solo pruebas (modo de prueba): lo mismo que un arrastre real, sin ratón.

    internal string SimulateDragOver(double y, bool moving) => ShowDropFeedback(new Windows.Foundation.Point(40, y), moving);

    internal void SimulateDrop(double y, int[] rowIndexes, IReadOnlyList<string> paths)
    {
        var pointer = new Windows.Foundation.Point(40, y);
        int? beforeId = BeforeIdAt(pointer);
        HideDropFeedback();
        if (Job is null)
        {
            return;
        }

        if (rowIndexes.Length > 0)
        {
            MoveRows([.. rowIndexes.Where(i => i < List.Items.Count).Select(i => (FileRowViewModel)List.Items[i]).Where(r => r.IsPending)], beforeId);
        }
        else if (paths.Count > 0)
        {
            _ = ((App)Application.Current).AddToJobAsync(Job.Job, paths, beforeId);
        }
    }

    private void List_DragLeave(object sender, DragEventArgs e) => HideDropFeedback();

    private async void List_Drop(object sender, DragEventArgs e)
    {
        int? beforeId = BeforeIdAt(e.GetPosition(List));
        HideDropFeedback();
        if (Job is null)
        {
            return;
        }

        e.Handled = true;
        if (_dragged is { } rows)
        {
            MoveRows(rows, beforeId);
            _dragged = null;
            return;
        }

        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count > 0)
            {
                await ((App)Application.Current).AddToJobAsync(Job.Job, paths, beforeId);
                Refresh();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or MegaCopier.Engine.CopyPlanException)
        {
            ((App)Application.Current).Notify(L.Get("Files_AddFailed"), ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Primera fila que aún no ha empezado: nada puede colocarse delante.</summary>
    private int FirstPendingIndex()
    {
        for (int i = 0; i < List.Items.Count; i++)
        {
            if (List.Items[i] is FileRowViewModel { IsPending: true })
            {
                return i;
            }
        }

        return List.Items.Count;
    }

    private void ShowHint(string glyph, string text)
    {
        DropHintIcon.Glyph = glyph;
        DropHintText.Text = text;
        DropHint.Visibility = Visibility.Visible;
    }

    private void HideDropFeedback()
    {
        DropLine.Visibility = Visibility.Collapsed;
        DropHint.Visibility = Visibility.Collapsed;
    }
}
