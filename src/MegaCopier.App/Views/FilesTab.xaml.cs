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
        HideFinishedBox.IsChecked = ((App)Application.Current).Settings.HideFinishedFiles;
    }

    public JobViewModel? Job
    {
        get => (JobViewModel?)GetValue(JobProperty);
        set => SetValue(JobProperty, value);
    }

    public FileListViewModel? Files => _files;

    /// <summary>Estrecha: las casillas bajan a una segunda fila; muy estrecha: se ocultan las columnas de hash.</summary>
    private void Adapt(double width)
    {
        bool twoRows = width < 760;
        Grid.SetRow(ListToggles, twoRows ? 1 : 0);
        Grid.SetColumn(ListToggles, twoRows ? 0 : 2);
        Grid.SetColumnSpan(ListToggles, twoRows ? 4 : 1);

        bool hashes = width >= 640;
        if (hashes != FileRowViewModel.ShowHashes)
        {
            FileRowViewModel.ShowHashes = hashes;
            foreach (var row in _files?.Items ?? [])
            {
                row.Refresh();
            }
        }

        SourceHashHeader.Visibility = hashes ? Visibility.Visible : Visibility.Collapsed;
        TargetHashHeader.Visibility = hashes ? Visibility.Visible : Visibility.Collapsed;
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

    private void MoveTop_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Selected().AsEnumerable().Reverse())
        {
            _files?.MoveToTop(row);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Selected().ForEach(r => _files?.MoveUp(r));

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Selected().AsEnumerable().Reverse())
        {
            _files?.MoveDown(row);
        }
    }

    private void MoveBottom_Click(object sender, RoutedEventArgs e) => Selected().ForEach(r => _files?.MoveToBottom(r));

    private void Remove_Click(object sender, RoutedEventArgs e) => Selected().ForEach(r => _files?.Remove(r));

    private void SelectAll_Click(object sender, RoutedEventArgs e) => List.SelectAll();

    /// <summary>Selecciona todos los archivos con la extensión de los elegidos (como TeraCopy Pro).</summary>
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

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var files = await Pickers.PickFilesAsync();
        AddSources(files);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            AddSources([folder]);
        }
    }

    private void AddSources(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0 || Job is null)
        {
            return;
        }

        try
        {
            Job.Job.AddSources(sources);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Engine.CopyPlanException)
        {
            ((App)Application.Current).Notify(L.Get("Files_AddFailed"), ex.Message);
        }
    }

    private async void SaveList_Click(object sender, RoutedEventArgs e)
    {
        if (Job is not null && await Pickers.SaveAsync("MegaCopier", ".mclist", L.Get("Files_ListType")) is { } path)
        {
            Reports.SaveList(Job.Job.Record, path);
        }
    }

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
}
