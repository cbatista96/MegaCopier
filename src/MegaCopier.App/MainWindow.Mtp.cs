using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Mtp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App;

/// <summary>
/// Móviles y cámaras conectados por USB (MTP): una ventana para recorrer el dispositivo y elegir qué copiar de él
/// (o a qué carpeta suya copiar). La copia la hace el motor como cualquier otra, con rutas "mtp:...".
/// Leer el dispositivo puede tardar: todo se hace fuera del hilo de la interfaz.
/// </summary>
public sealed partial class MainWindow
{
    private IMtpDevice? _mtpDevice;
    private MtpDeviceInfo? _mtpInfo;
    private readonly List<MtpObject> _mtpStack = [];
    private bool _mtpExport;
    private int _mtpGeneration;

    private async void MtpImport_Click(object sender, RoutedEventArgs e) => await MtpImportAsync();

    private async void MtpExport_Click(object sender, RoutedEventArgs e)
    {
        var files = await Pickers.PickFilesAsync();
        if (files.Count > 0)
        {
            await MtpExportAsync(files);
        }
    }

    /// <summary>Copiar del dispositivo al PC: elegir qué en el dispositivo y luego la carpeta del PC.</summary>
    /// <param name="destination">Solo pruebas: la carpeta de destino, sin abrir el selector.</param>
    internal async Task MtpImportAsync(string? destination = null)
    {
        _mtpExport = false;
        MtpDialog.Title = L.Get("Mtp_ImportTitle");
        MtpDialog.PrimaryButtonText = L.Get("Mtp_ImportButton");
        MtpSummary.Text = L.Get("Mtp_ImportSummary");
        MtpMoveBox.Content = L.Get("Mtp_MoveImport");
        if (await ShowMtpDialogAsync() is not { } chosen)
        {
            return;
        }

        destination ??= await Pickers.PickFolderAsync(L.Get("Picker_CopyHere"));
        if (destination is not null)
        {
            Submit(new IpcRequest { Command = "copy", Sources = [.. chosen], Destination = destination, Move = MtpMoveBox.IsChecked == true });
        }
    }

    /// <summary>Copiar del PC al dispositivo: elegir la carpeta del dispositivo donde dejarlo.</summary>
    internal async Task MtpExportAsync(IReadOnlyList<string> sources)
    {
        _mtpExport = true;
        MtpDialog.Title = L.Get("Mtp_ExportTitle");
        MtpDialog.PrimaryButtonText = L.Get("Mtp_ExportButton");
        MtpSummary.Text = sources.Count == 1
            ? L.F("Mtp_ExportSummaryOne", Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0])))
            : L.F("Mtp_ExportSummaryMany", sources.Count);
        MtpMoveBox.Content = L.Get("Mtp_MoveExport");
        if (await ShowMtpDialogAsync() is [string folder])
        {
            Submit(new IpcRequest { Command = "copy", Sources = [.. sources], Destination = folder, Move = MtpMoveBox.IsChecked == true });
        }
    }

    /// <summary>
    /// Muestra la ventana. Al importar devuelve lo seleccionado (o la carpeta abierta si no hay nada); al exportar,
    /// la carpeta abierta. Null si se cancela.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ShowMtpDialogAsync()
    {
        MtpMoveBox.IsChecked = false;
        MtpList.Items.Clear();
        _mtpStack.Clear();
        MtpDialog.XamlRoot = Root.XamlRoot;
        MtpDialog.RequestedTheme = Root.RequestedTheme;
        _ = LoadMtpDevicesAsync();
        try
        {
            if (await MtpDialog.ShowAsync() != ContentDialogResult.Primary || _mtpInfo is not { } info)
            {
                return null;
            }

            string current = MtpPath.Format(info.Id, info.Name, string.Join('/', _mtpStack.Select(o => o.Name)));
            var selected = MtpList.SelectedItems.OfType<ListViewItem>().Select(i => (MtpObject)i.Tag).ToList();
            return _mtpExport || selected.Count == 0 ? [current] : [.. selected.Select(o => MtpPath.Combine(current, o.Name))];
        }
        finally
        {
            CloseMtpDevice();
        }
    }

    private void CloseMtpDevice()
    {
        _mtpGeneration++;
        var device = _mtpDevice;
        _mtpDevice = null;
        _mtpInfo = null;
        if (device is not null)
        {
            _ = Task.Run(device.Dispose);
        }
    }

    private async Task LoadMtpDevicesAsync()
    {
        int generation = ++_mtpGeneration;
        MtpDeviceBox.Items.Clear();
        MtpStatus.Text = L.Get("Mtp_Loading");
        UpdateMtpButtons();
        var devices = await Task.Run(MtpDevices.List);
        if (generation != _mtpGeneration)
        {
            return;
        }

        foreach (var device in devices)
        {
            MtpDeviceBox.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device });
        }

        MtpStatus.Text = devices.Count == 0 ? L.Get("Mtp_NoDevices") : "";
        if (devices.Count > 0)
        {
            MtpDeviceBox.SelectedIndex = 0; // abre el primero (SelectionChanged)
        }
    }

    private void MtpRefresh_Click(object sender, RoutedEventArgs e)
    {
        CloseMtpDevice();
        _mtpStack.Clear();
        MtpList.Items.Clear();
        _ = LoadMtpDevicesAsync();
    }

    private async void MtpDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MtpDeviceBox.SelectedItem is not ComboBoxItem { Tag: MtpDeviceInfo info } || info == _mtpInfo)
        {
            return;
        }

        var old = _mtpDevice;
        _mtpDevice = null;
        _mtpInfo = info;
        _mtpStack.Clear();
        if (old is not null)
        {
            _ = Task.Run(old.Dispose);
        }

        int generation = ++_mtpGeneration;
        MtpList.Items.Clear();
        MtpStatus.Text = L.Get("Mtp_Loading");
        try
        {
            var device = await Task.Run(() => MtpDevices.Open(info.Id));
            if (generation != _mtpGeneration)
            {
                _ = Task.Run(device.Dispose);
                return;
            }

            _mtpDevice = device;
            await ShowMtpFolderAsync();
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            MtpStatus.Text = L.F("Mtp_Error", ex.Message);
        }
    }

    private async Task ShowMtpFolderAsync()
    {
        if (_mtpDevice is not { } device || _mtpInfo is not { } info)
        {
            return;
        }

        int generation = ++_mtpGeneration;
        string parent = _mtpStack.Count == 0 ? device.RootId : _mtpStack[^1].Id;
        MtpPathText.Text = string.Join(" › ", new[] { info.Name }.Concat(_mtpStack.Select(o => o.Name)));
        MtpList.Items.Clear();
        MtpStatus.Text = L.Get("Mtp_Loading");
        UpdateMtpButtons();
        try
        {
            var children = await Task.Run(() => device.Children(parent));
            if (generation != _mtpGeneration)
            {
                return;
            }

            foreach (var child in children.OrderBy(c => !c.IsFolder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                // Pulsar la fila la marca; las carpetas se abren con su botón "›" o con doble clic.
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new FontIcon { Glyph = child.IsFolder ? "" : "", FontSize = 16 });
                var name = new TextBlock { Text = child.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                FrameworkElement trailing = child.IsFolder
                    ? new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Tag = child, Padding = new Thickness(10, 4, 10, 4), Background = null, BorderThickness = new Thickness(0) }
                    : new TextBlock { Text = Format.Bytes(child.Size), Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center };
                if (trailing is Button open)
                {
                    open.Click += MtpOpen_Click;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, L.F("Mtp_Open", child.Name));
                    ToolTipService.SetToolTip(open, L.F("Mtp_Open", child.Name));
                }

                Grid.SetColumn(trailing, 2);
                row.Children.Add(trailing);

                var item = new ListViewItem { Content = row, Tag = child, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                if (child.IsFolder)
                {
                    item.DoubleTapped += MtpItem_DoubleTapped;
                }

                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, child.Name);
                MtpList.Items.Add(item);
            }

            MtpStatus.Text = children.Count == 0 ? L.Get("Mtp_Empty") : "";
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            MtpStatus.Text = L.F("Mtp_Error", ex.Message);
        }

        UpdateMtpButtons();
    }

    private async void MtpOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MtpObject folder })
        {
            await OpenMtpFolderAsync(folder);
        }
    }

    private async void MtpItem_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is ListViewItem { Tag: MtpObject { IsFolder: true } folder })
        {
            e.Handled = true;
            await OpenMtpFolderAsync(folder);
        }
    }

    private async Task OpenMtpFolderAsync(MtpObject folder)
    {
        _mtpStack.Add(folder);
        await ShowMtpFolderAsync();
    }

    private async void MtpUp_Click(object sender, RoutedEventArgs e)
    {
        if (_mtpStack.Count > 0)
        {
            _mtpStack.RemoveAt(_mtpStack.Count - 1);
            await ShowMtpFolderAsync();
        }
    }

    private void MtpList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMtpButtons();

    /// <summary>
    /// En la raíz del dispositivo no hay carpeta que copiar ni donde dejar archivos (hay que entrar en un almacenamiento),
    /// salvo al importar algo seleccionado.
    /// </summary>
    private void UpdateMtpButtons()
    {
        bool inside = _mtpStack.Count > 0;
        MtpUpButton.IsEnabled = inside;
        MtpList.SelectionMode = _mtpExport ? ListViewSelectionMode.None : ListViewSelectionMode.Multiple;
        MtpDialog.IsPrimaryButtonEnabled = _mtpDevice is not null && (inside || (!_mtpExport && MtpList.SelectedItems.Count > 0));
    }
}
