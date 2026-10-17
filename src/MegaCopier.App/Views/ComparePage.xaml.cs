using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Ipc;
using MegaCopier.Engine.Sync;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Comparar y sincronizar dos carpetas: qué hay distinto, qué se hará con cada cosa (y por qué), y desmarcar lo que no
/// se quiera tocar antes de sincronizar. La comparación usa las mismas reglas y el mismo recuerdo que la sincronización
/// (FolderSync), así que lo que se ve es exactamente lo que se hará. Leer carpetas grandes tarda: va fuera de la interfaz.
/// </summary>
public sealed partial class ComparePage : UserControl
{
    private readonly List<CompareRow> _rows = [];
    private string _left = "";
    private string _right = "";
    private int _generation;
    private bool _loading;

    public ComparePage()
    {
        InitializeComponent();
        _loading = true;
        DirectionBox.SelectedIndex = 0;
        _loading = false;
    }

    public event Action? CloseRequested;

    /// <summary>"Sincronizar" o "Simular": la copia que hay que lanzar.</summary>
    public event Action<IpcRequest>? SyncRequested;

    private static App App => (App)Application.Current;

    private SyncDirection Direction => (SyncDirection)Math.Max(0, DirectionBox.SelectedIndex);

    /// <summary>Abre la comparación de estas dos carpetas y compara ya.</summary>
    public void Show(string left, string right)
    {
        _left = Path.TrimEndingDirectorySeparator(left);
        _right = Path.TrimEndingDirectorySeparator(right);
        LeftPath.Text = _left;
        RightPath.Text = _right;
        ToolTipService.SetToolTip(LeftPath, _left);
        ToolTipService.SetToolTip(RightPath, _right);
        _ = CompareAsync();
    }

    private async Task CompareAsync()
    {
        int generation = ++_generation;
        _rows.Clear();
        List.ItemsSource = null;
        Empty.Visibility = Visibility.Collapsed;
        Summary.Text = L.Get("Compare_Comparing");
        SyncButton.IsEnabled = SimulateButton.IsEnabled = false;
        var direction = Direction;
        var filter = App.Settings.Filter;
        var tolerance = TimeSpan.FromSeconds(Math.Clamp(App.Settings.TimeToleranceSeconds, 0, 3600));
        string left = _left, right = _right;
        try
        {
            var items = await Task.Run(() =>
            {
                var last = direction == SyncDirection.TwoWay ? FolderSync.LoadState(FolderSync.StateFile(left, right)) : [];
                return FolderSync.Plan(FolderSync.Scan(left, filter), FolderSync.Scan(right, filter), last, direction, tolerance, left, right);
            });
            if (generation != _generation)
            {
                return;
            }

            _rows.AddRange(items.Select(i => new CompareRow(i)));
            ShowRows();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Summary.Text = L.F("Compare_Error", ex.Message);
        }
    }

    private void ShowRows()
    {
        bool showEqual = ShowEqualBox.IsChecked == true;
        var visible = _rows.Where(r => showEqual || r.CanInclude).ToList();
        List.ItemsSource = visible;
        int copies = _rows.Count(r => r.CanInclude && !r.IsRemoval);
        int removals = _rows.Count(r => r.IsRemoval);
        int equal = _rows.Count(r => !r.CanInclude);
        Summary.Text = L.F("Compare_Summary", copies, removals, equal);
        Empty.Text = L.Get(copies + removals == 0 ? "Compare_Nothing" : "Compare_OnlyEqualHidden");
        Empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncButton.IsEnabled = SimulateButton.IsEnabled = copies + removals > 0;
    }

    private void Launch(bool dryRun)
    {
        SyncRequested?.Invoke(new IpcRequest
        {
            Command = "copy",
            Operation = JobOperation.Sync,
            Sources = [_left],
            Destination = _right,
            SyncDirection = Direction,
            DryRun = dryRun,
            Excluded = [.. _rows.Where(r => r.CanInclude && !r.Include).Select(r => r.Relative)],
        });
    }

    private void Sync_Click(object sender, RoutedEventArgs e) => Launch(dryRun: false);

    private void Simulate_Click(object sender, RoutedEventArgs e) => Launch(dryRun: true);

    private void Compare_Click(object sender, RoutedEventArgs e) => _ = CompareAsync();

    private void ShowEqual_Click(object sender, RoutedEventArgs e) => ShowRows();

    private void Direction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && _left.Length > 0)
        {
            _ = CompareAsync();
        }
    }

    private async void PickLeft_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync() is { } folder)
        {
            Show(folder, _right);
        }
    }

    private async void PickRight_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync(L.Get("Compare_PickRight")) is { } folder)
        {
            Show(_left, folder);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
