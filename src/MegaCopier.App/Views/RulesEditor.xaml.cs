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
        },
        Links = (LinkHandling)Math.Max(0, LinksBox.SelectedIndex),
        KeepBothPattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? CopyOptions.DefaultKeepBothPattern : PatternBox.Text.Trim(),
        RetryCount = double.IsNaN(RetriesBox.Value) ? 2 : (int)RetriesBox.Value,
        RetryDelaySeconds = double.IsNaN(DelayBox.Value) ? 1 : DelayBox.Value,
        OnError = (ErrorPolicy)Math.Max(0, OnErrorBox.SelectedIndex),
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
