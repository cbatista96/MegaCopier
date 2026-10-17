using MegaCopier.App.Services;
using MegaCopier.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>Las reglas que comparten una copia y los valores predeterminados: filtros, enlaces, nombres y errores.</summary>
public sealed class CopyRules
{
    public FileFilter Filter { get; set; } = new();

    public LinkHandling Links { get; set; }

    public string KeepBothPattern { get; set; } = CopyOptions.DefaultKeepBothPattern;

    public int RetryCount { get; set; } = 2;

    public double RetryDelaySeconds { get; set; } = 1;

    public ErrorPolicy OnError { get; set; }

    public string VersionsFolder { get; set; } = "";

    public FolderConflictPolicy FolderConflict { get; set; }

    public double TimeToleranceSeconds { get; set; } = 2;

    public bool VerifySource { get; set; }

    public bool SaveHashStream { get; set; }

    public bool PreserveHardLinks { get; set; }

    public bool WipeSource { get; set; }

    public WipeMethod WipeMethod { get; set; }

    public bool EfsRaw { get; set; }

    public bool WaitForDevice { get; set; } = true;

    public bool WaitForSpace { get; set; } = true;

    public bool AllOrNothing { get; set; }

    public bool RescueMode { get; set; }

    public bool DeltaCopy { get; set; }

    public bool SkipDuplicates { get; set; }

    public static CopyRules From(Engine.Jobs.CopyJobRecord r) => new()
    {
        Filter = r.Filter,
        Links = r.Links,
        KeepBothPattern = r.KeepBothPattern,
        RetryCount = r.RetryCount,
        RetryDelaySeconds = r.RetryDelaySeconds,
        OnError = r.OnError,
        VersionsFolder = r.VersionsFolder ?? "",
        FolderConflict = r.FolderConflict,
        TimeToleranceSeconds = r.TimeToleranceSeconds,
        VerifySource = r.VerifySource,
        SaveHashStream = r.SaveHashStream,
        PreserveHardLinks = r.PreserveHardLinks,
        WipeSource = r.WipeSource,
        WipeMethod = r.WipeMethod,
        EfsRaw = r.EfsRaw,
        WaitForDevice = r.WaitForDevice,
        WaitForSpace = r.WaitForSpace,
        AllOrNothing = r.AllOrNothing,
        RescueMode = r.RescueMode,
        DeltaCopy = r.DeltaCopy,
        SkipDuplicates = r.SkipDuplicates,
    };

    public void ApplyTo(Engine.Jobs.CopyJobRecord r)
    {
        r.Filter = Filter;
        r.Links = Links;
        r.KeepBothPattern = KeepBothPattern;
        r.RetryCount = RetryCount;
        r.RetryDelaySeconds = RetryDelaySeconds;
        r.OnError = OnError;
        r.VersionsFolder = VersionsFolder;
        r.FolderConflict = FolderConflict;
        r.TimeToleranceSeconds = TimeToleranceSeconds;
        r.VerifySource = VerifySource;
        r.SaveHashStream = SaveHashStream;
        r.PreserveHardLinks = PreserveHardLinks;
        r.WipeSource = WipeSource;
        r.WipeMethod = WipeMethod;
        r.EfsRaw = EfsRaw;
        r.WaitForDevice = WaitForDevice;
        r.WaitForSpace = WaitForSpace;
        r.AllOrNothing = AllOrNothing;
        r.RescueMode = RescueMode;
        r.DeltaCopy = DeltaCopy;
        r.SkipDuplicates = SkipDuplicates;
    }
}

/// <summary>
/// Editor de filtros (incluir, excluir, tamaño, fechas, ocultos, sistema) y reglas (enlaces, patrón de nombres de
/// "conservar ambos", reintentos y qué hacer ante un error). Lo usan la pestaña Opciones de cada copia y Ajustes.
/// </summary>
public sealed partial class RulesEditor : UserControl
{
    private const double MiB = 1024 * 1024;
    private bool _loading;

    public RulesEditor()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            // Dos columnas si caben; si no, los filtros encima de las reglas.
            bool one = e.NewSize.Width < 600;
            Grid.SetColumnSpan(FiltersColumn, one ? 2 : 1);
            Grid.SetRow(RulesColumn, one ? 1 : 0);
            Grid.SetColumn(RulesColumn, one ? 0 : 1);
            Grid.SetColumnSpan(RulesColumn, one ? 2 : 1);
            Grid.SetRow(SafetyHeader, one ? 2 : 1);
            Grid.SetRow(SafetyLeft, one ? 3 : 2);
            Grid.SetColumnSpan(SafetyLeft, one ? 2 : 1);
            Grid.SetRow(SafetyRight, one ? 4 : 2);
            Grid.SetColumn(SafetyRight, one ? 0 : 1);
            Grid.SetColumnSpan(SafetyRight, one ? 2 : 1);
        };
    }

    /// <summary>El usuario cambió algo (ya está en <see cref="Read"/>).</summary>
    public event Action? RulesChanged;

    public void Load(CopyRules rules)
    {
        _loading = true;
        var f = rules.Filter;
        IncludeBox.Text = string.Join("; ", f.Include);
        ExcludeBox.Text = string.Join("; ", f.Exclude);
        MinSizeBox.Value = f.MinSize is { } min ? Math.Round(min / MiB, 2) : double.NaN;
        MaxSizeBox.Value = f.MaxSize is { } max ? Math.Round(max / MiB, 2) : double.NaN;
        AfterBox.Date = f.ModifiedAfterUtc is { } after ? new DateTimeOffset(after.ToLocalTime()) : null;
        BeforeBox.Date = f.ModifiedBeforeUtc is { } before ? new DateTimeOffset(before.ToLocalTime()) : null;
        SkipHiddenBox.IsChecked = f.SkipHidden;
        SkipSystemBox.IsChecked = f.SkipSystem;
        LinksBox.SelectedIndex = (int)rules.Links;
        PatternBox.Text = rules.KeepBothPattern;
        RetriesBox.Value = rules.RetryCount;
        DelayBox.Value = rules.RetryDelaySeconds;
        OnErrorBox.SelectedIndex = (int)rules.OnError;
        SkipJunkBox.IsChecked = f.SkipJunk;
        FolderConflictBox.SelectedIndex = (int)rules.FolderConflict;
        VersionsBox.Text = rules.VersionsFolder;
        ToleranceBox.Value = rules.TimeToleranceSeconds;
        VerifySourceBox.IsChecked = rules.VerifySource;
        HashStreamBox.IsChecked = rules.SaveHashStream;
        HardLinksBox.IsChecked = rules.PreserveHardLinks;
        WipeBox.IsChecked = rules.WipeSource;
        WipeMethodBox.SelectedIndex = (int)rules.WipeMethod;
        EfsRawBox.IsChecked = rules.EfsRaw;
        WaitDeviceBox.IsChecked = rules.WaitForDevice;
        WaitSpaceBox.IsChecked = rules.WaitForSpace;
        AllOrNothingBox.IsChecked = rules.AllOrNothing;
        RescueBox.IsChecked = rules.RescueMode;
        DeltaBox.IsChecked = rules.DeltaCopy;
        DuplicatesBox.IsChecked = rules.SkipDuplicates;
        UpdatePreview();
        _loading = false;
    }

    public CopyRules Read() => new()
    {
        Filter = new FileFilter
        {
            Include = Masks(IncludeBox.Text),
            Exclude = Masks(ExcludeBox.Text),
            MinSize = double.IsNaN(MinSizeBox.Value) || MinSizeBox.Value <= 0 ? null : (long)(MinSizeBox.Value * MiB),
            MaxSize = double.IsNaN(MaxSizeBox.Value) || MaxSizeBox.Value <= 0 ? null : (long)(MaxSizeBox.Value * MiB),
            ModifiedAfterUtc = AfterBox.Date?.Date.ToUniversalTime(),

            // "Hasta" incluye todo ese día.
            ModifiedBeforeUtc = BeforeBox.Date?.Date.AddDays(1).AddTicks(-1).ToUniversalTime(),
            SkipHidden = SkipHiddenBox.IsChecked == true,
            SkipSystem = SkipSystemBox.IsChecked == true,
            SkipJunk = SkipJunkBox.IsChecked == true,
        },
        Links = (LinkHandling)Math.Max(0, LinksBox.SelectedIndex),
        KeepBothPattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? CopyOptions.DefaultKeepBothPattern : PatternBox.Text.Trim(),
        RetryCount = double.IsNaN(RetriesBox.Value) ? 2 : (int)RetriesBox.Value,
        RetryDelaySeconds = double.IsNaN(DelayBox.Value) ? 1 : DelayBox.Value,
        OnError = (ErrorPolicy)Math.Max(0, OnErrorBox.SelectedIndex),
        FolderConflict = (FolderConflictPolicy)Math.Max(0, FolderConflictBox.SelectedIndex),
        VersionsFolder = VersionsBox.Text.Trim(),
        TimeToleranceSeconds = double.IsNaN(ToleranceBox.Value) ? 2 : ToleranceBox.Value,
        VerifySource = VerifySourceBox.IsChecked == true,
        SaveHashStream = HashStreamBox.IsChecked == true,
        PreserveHardLinks = HardLinksBox.IsChecked == true,
        WipeSource = WipeBox.IsChecked == true,
        WipeMethod = (WipeMethod)Math.Max(0, WipeMethodBox.SelectedIndex),
        EfsRaw = EfsRawBox.IsChecked == true,
        WaitForDevice = WaitDeviceBox.IsChecked == true,
        WaitForSpace = WaitSpaceBox.IsChecked == true,
        AllOrNothing = AllOrNothingBox.IsChecked == true,
        RescueMode = RescueBox.IsChecked == true,
        DeltaCopy = DeltaBox.IsChecked == true,
        SkipDuplicates = DuplicatesBox.IsChecked == true,
    };

    private static List<string> Masks(string text) =>
        [.. text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void UpdatePreview()
    {
        string pattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? CopyOptions.DefaultKeepBothPattern : PatternBox.Text;
        string example = pattern.Replace("{name}", "foto").Replace("{ext}", ".jpg").Replace("{n}", "2")
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
        PatternPreview.Text = L.F("Rules_PatternPreview", example);
    }

    private void Pattern_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void Describe_Click(object sender, RoutedEventArgs e) => ApplyDescription();

    private void Describe_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyDescription();
            e.Handled = true;
        }
    }

    /// <summary>"Fotos de más de 2 MB del último mes sin los .tmp" → los campos de los filtros.</summary>
    internal void ApplyDescription()
    {
        if (DescribeBox.Text.Trim().Length == 0)
        {
            return;
        }

        var result = FilterParser.Parse(DescribeBox.Text, DateTime.Now);
        var current = Read();
        var f = result.Filter;
        f.SkipHidden |= current.Filter.SkipHidden;
        f.SkipSystem |= current.Filter.SkipSystem;
        f.SkipJunk |= current.Filter.SkipJunk;
        current.Filter = f;
        Load(current);
        DescribeResult.Text = (result.Understood.Count > 0 ? L.F("Rules_DescribeUnderstood", string.Join(" · ", result.Understood)) : L.Get("Rules_DescribeNothing"))
            + (result.Unknown.Count > 0 ? "  " + L.F("Rules_DescribeUnknown", string.Join(", ", result.Unknown)) : "");
        DescribeResult.Visibility = Visibility.Visible;
        Raise();
    }

    private void Changed(object sender, RoutedEventArgs e) => Raise();

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => Raise();

    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Raise();

    private void Date_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Raise();

    private void Raise()
    {
        if (!_loading)
        {
            RulesChanged?.Invoke();
        }
    }
}
