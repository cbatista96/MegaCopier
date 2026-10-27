using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using MegaCopier.Engine.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Pestaña Opciones de un trabajo. Se guardan en el propio trabajo: si está en marcha se aplican al
/// reanudarlo; "Verificar" y "Desatendido" del panel cambian en caliente.
/// </summary>
public sealed partial class OptionsTab : UserControl
{
    internal static readonly FileHashAlgorithm[] Hashes =
    [
        FileHashAlgorithm.None, FileHashAlgorithm.Crc32, FileHashAlgorithm.XxHash3, FileHashAlgorithm.XxHash64, FileHashAlgorithm.XxHash128,
        FileHashAlgorithm.Md5, FileHashAlgorithm.Sha1, FileHashAlgorithm.Sha256, FileHashAlgorithm.Sha512,
        FileHashAlgorithm.Blake3,
        .. (System.Security.Cryptography.SHA3_256.IsSupported ? new[] { FileHashAlgorithm.Sha3_256, FileHashAlgorithm.Sha3_512 } : []),
    ];

    private bool _loading;
    private JobViewModel? _job;

    public OptionsTab()
    {
        InitializeComponent();
        Rules.RulesChanged += Rules_Changed;
        SizeChanged += (_, e) => Adapt(e.NewSize.Width);
        foreach (var hash in Hashes)
        {
            HashBox.Items.Add(new ComboBoxItem { Content = HashName(hash) });
        }
    }

    public JobViewModel? Job
    {
        get => _job;
        set
        {
            _job = value;
            Load();
        }
    }

    public static string HashName(FileHashAlgorithm hash) => hash switch
    {
        FileHashAlgorithm.None => L.Get("Hash_None"),
        FileHashAlgorithm.Crc32 => L.F("Hash_Name", "CRC32"),
        FileHashAlgorithm.XxHash3 => L.F("Hash_Name", "xxHash3-64"),
        FileHashAlgorithm.XxHash64 => L.F("Hash_Name", "xxHash64"),
        FileHashAlgorithm.XxHash128 => L.F("Hash_Name", "xxHash3-128"),
        FileHashAlgorithm.Md5 => L.F("Hash_Name", "MD5"),
        FileHashAlgorithm.Sha1 => L.F("Hash_Name", "SHA-1"),
        FileHashAlgorithm.Sha256 => L.F("Hash_Name", "SHA-256"),
        FileHashAlgorithm.Sha3_256 => L.F("Hash_Name", "SHA3-256"),
        FileHashAlgorithm.Sha3_512 => L.F("Hash_Name", "SHA3-512"),
        FileHashAlgorithm.Blake3 => L.F("Hash_Name", "BLAKE3"),
        _ => L.F("Hash_Name", "SHA-512"),
    };

    public static int ConflictIndex(ConflictPolicy conflict) => conflict switch
    {
        ConflictPolicy.Overwrite => 1,
        ConflictPolicy.Skip => 2,
        ConflictPolicy.OverwriteIfNewer => 3,
        ConflictPolicy.KeepBoth => 4,
        ConflictPolicy.OverwriteIfDifferentSize => 5,
        ConflictPolicy.RenameExisting => 6,
        _ => 0,
    };

    public static ConflictPolicy ConflictAt(int index) => index switch
    {
        1 => ConflictPolicy.Overwrite,
        2 => ConflictPolicy.Skip,
        3 => ConflictPolicy.OverwriteIfNewer,
        4 => ConflictPolicy.KeepBoth,
        5 => ConflictPolicy.OverwriteIfDifferentSize,
        6 => ConflictPolicy.RenameExisting,
        _ => ConflictPolicy.Ask,
    };

    /// <summary>Tres columnas si caben; dos o una si la ventana es estrecha (nada se corta).</summary>
    private void Adapt(double width)
    {
        int columns = width >= 980 ? 3 : width >= 640 ? 2 : 1;
        OptionsGrid.ColumnDefinitions.Clear();
        for (int i = 0; i < columns; i++) OptionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cards = new FrameworkElement[] { WhatCard, HowCard, FinishCard };
        for (int i = 0; i < cards.Length; i++) Place(cards[i], i / columns, i % columns, columns == 2 && i == 2 ? 2 : 1);
        bool narrow = width < 700;
        Place(ScheduleDate, 0, 0, 1);
        Place(ScheduleTime, 0, 1, 1);
        Place(ScheduleSetButton, narrow ? 1 : 0, narrow ? 0 : 2, 1);
        Place(StartNowButton, narrow ? 1 : 0, narrow ? 1 : 3, 1);
        ScheduleRow.HorizontalAlignment = HorizontalAlignment.Left;
        bool narrowTask = width < 620;
        Place(TaskNameBox, 0, 0, narrowTask ? 3 : 1);
        Place(TaskFrequencyBox, narrowTask ? 1 : 0, narrowTask ? 0 : 1, 1);
        Place(SaveTaskButton, narrowTask ? 1 : 0, narrowTask ? 1 : 2, 1);

    }

    private static void Place(FrameworkElement element, int row, int column, int span)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
    }

    public void Refresh() => RunningInfo.IsOpen = _job?.IsRunning == true || _job?.IsPaused == true;

    private void Load()
    {
        if (_job?.Job.Record is not { } r)
        {
            return;
        }

        _loading = true;
        TimestampsBox.IsChecked = r.PreserveTimestamps;
        AttributesBox.IsChecked = r.PreserveAttributes;
        StreamsBox.IsChecked = r.CopyStreams;
        AclBox.IsChecked = r.CopySecurity.HasFlag(SecurityParts.Acl);
        OwnerBox.IsChecked = r.CopySecurity.HasFlag(SecurityParts.Owner);
        AuditBox.IsChecked = r.CopySecurity.HasFlag(SecurityParts.Audit);
        ConflictBox.SelectedIndex = ConflictIndex(r.Conflict);
        HashBox.SelectedIndex = Array.IndexOf(Hashes, r.HashAlgorithm);
        int blockMb = r.BlockSize > 0 ? r.BlockSize / CopyOptions.MiB : 4;
        BufferBox.SelectedItem = BufferBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == blockMb.ToString()) ?? BufferBox.Items[2];
        ThreadsBox.Value = r.QueueDepth > 0 ? r.QueueDepth : 4;
        VerifyBox.IsChecked = r.Verify;
        VerifySkippedBox.IsChecked = r.VerifySkipped;
        CacheBox.IsChecked = r.UseSystemCache;
        ChecksumBox.IsChecked = r.SaveChecksumFile;
        ReportBox.IsChecked = r.ExportReport;
        ScriptsBox.Text = string.Join("\r", r.Scripts);
        Rules.Load(CopyRules.From(r));
        ModeBox.SelectedIndex = (int)r.Mode;
        DryRunBox.IsChecked = r.DryRun;
        MirrorInfo.IsOpen = r.Mode == CopyMode.Mirror;
        var start = r.StartAtUtc?.ToLocalTime() ?? DateTime.Now.AddHours(1);
        ScheduleDate.Date = new DateTimeOffset(start.Date);
        ScheduleTime.Time = new TimeSpan(start.Hour, start.Minute, 0);
        ScheduleInfo.Text = r.StartAtUtc is { } at ? L.F("Job_StateScheduled", at.ToLocalTime().ToString("g")) : "";
        LoadSpeed(r.MaxBytesPerSecond);
        _loading = false;
        Refresh();
    }

    private void Changed(object sender, RoutedEventArgs e) => Save();

    private void ThreadsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Save();

    private void Save()
    {
        if (_loading || _job?.Job is not { } job)
        {
            return;
        }

        var r = job.Record;
        r.PreserveTimestamps = TimestampsBox.IsChecked == true;
        r.PreserveAttributes = AttributesBox.IsChecked == true;
        r.CopyStreams = StreamsBox.IsChecked == true;
        r.CopySecurity = (AclBox.IsChecked == true ? SecurityParts.Acl : 0)
            | (OwnerBox.IsChecked == true ? SecurityParts.Owner : 0)
            | (AuditBox.IsChecked == true ? SecurityParts.Audit : 0);
        r.Conflict = ConflictAt(ConflictBox.SelectedIndex);
        r.HashAlgorithm = Hashes[Math.Max(0, HashBox.SelectedIndex)];
        if (BufferBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int mb))
        {
            r.BlockSize = mb * CopyOptions.MiB;
            r.ChunkSize = Math.Max(r.ChunkSize, 64L * CopyOptions.MiB) / r.BlockSize * r.BlockSize;
        }

        r.QueueDepth = double.IsNaN(ThreadsBox.Value) ? 4 : (int)ThreadsBox.Value;
        r.VerifySkipped = VerifySkippedBox.IsChecked == true;
        r.UseSystemCache = CacheBox.IsChecked == true;
        r.SaveChecksumFile = ChecksumBox.IsChecked == true;
        r.ExportReport = ReportBox.IsChecked == true;
        r.Scripts = ScriptsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Rules.Read().ApplyTo(r);
        r.Mode = (CopyMode)Math.Max(0, ModeBox.SelectedIndex);
        r.DryRun = DryRunBox.IsChecked == true;
        MirrorInfo.IsOpen = r.Mode == CopyMode.Mirror;
        r.Verify = VerifyBox.IsChecked == true;
        job.ApplyLiveOptions();
    }

    // Límite de velocidad: escalones de 256 KB/s a 1 GB/s; se aplica al momento a la copia en marcha.

    private static readonly double[] SpeedSteps = [0.25, 0.5, 1, 2, 3, 4, 5, 8, 10, 15, 20, 30, 40, 50, 75, 100, 150, 200, 300, 500, 750, 1000];

    private static long StepBytes(int index) => (long)(SpeedSteps[Math.Clamp(index, 0, SpeedSteps.Length - 1)] * CopyOptions.MiB);

    private static int StepFor(long bytes) =>
        Array.FindIndex(SpeedSteps, s => s * CopyOptions.MiB >= bytes) is int i and >= 0 ? i : SpeedSteps.Length - 1;

    private void LoadSpeed(long bytes)
    {
        SpeedSlider.Maximum = SpeedSteps.Length - 1;
        SpeedSwitch.IsOn = bytes > 0;
        SpeedSlider.Value = StepFor(bytes > 0 ? bytes : 20L * CopyOptions.MiB);
        SpeedSlider.IsEnabled = bytes > 0;
        ShowSpeed();
    }

    private void ShowSpeed() =>
        SpeedValue.Text = SpeedSwitch.IsOn ? Format.Bytes(StepBytes((int)SpeedSlider.Value)) + "/s" : L.Get("Speed_NoLimit");

    private void Speed_Changed(object sender, RoutedEventArgs e) => ApplySpeed();

    private void SpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => ApplySpeed();

    private void ApplySpeed()
    {
        SpeedSlider.IsEnabled = SpeedSwitch.IsOn;
        ShowSpeed();
        if (!_loading && _job?.Job is { } job)
        {
            job.SetSpeedLimit(SpeedSwitch.IsOn ? StepBytes((int)SpeedSlider.Value) : 0);
        }
    }

    private void Rules_Changed() => Save();

    /// <summary>Programar el inicio para el día y la hora elegidos.</summary>
    private void Schedule_Click(object sender, RoutedEventArgs e)
    {
        if (_job is null || ScheduleDate.Date is not { } date)
        {
            return;
        }

        var when = date.Date + ScheduleTime.Time;
        if (when <= DateTime.Now)
        {
            ScheduleInfo.Text = L.Get("Schedule_InThePast");
            return;
        }

        _job.Schedule(when);
        Load();
    }

    /// <summary>Guarda esta copia como tarea del Programador de tareas, a la hora elegida arriba.</summary>
    private void SaveTask_Click(object sender, RoutedEventArgs e)
    {
        if (_job?.Job.Record is not { } record || ScheduleDate.Date is not { } date)
        {
            return;
        }

        Save();
        string name = TaskNameBox.Text.Trim().Length > 0
            ? TaskNameBox.Text
            : $"{Engine.SourceNames.TargetName(record.Sources[0])} → {Engine.SourceNames.TargetName(record.Destination)}";
        var frequency = (TaskFrequency)Math.Max(0, TaskFrequencyBox.SelectedIndex);
        var start = date.Date + ScheduleTime.Time;
        string? error = ScheduledTasks.Create(record, name, frequency, start, _pairs, withoutSession: TaskNoSessionBox.IsChecked == true);
        TaskInfo.Text = error is null
            ? L.F("Task_Saved" + frequency, ScheduledTasks.CleanName(name), start.ToString("t"), start.ToString("d"))
            : L.F("Task_Failed", error);
    }

    /// <summary>
    /// Más parejas de carpetas para la tarea que se va a guardar (como FreeFileSync): cada una con su destino, todas en la
    /// misma copia. La copia de ahora no cambia; solo la tarea.
    /// </summary>
    private readonly Dictionary<string, string> _pairs = new(StringComparer.OrdinalIgnoreCase);

    private async void AddPair_Click(object sender, RoutedEventArgs e)
    {
        if (await Pickers.PickFolderAsync(L.Get("Task_PairSource")) is not { } source
            || await Pickers.PickFolderAsync(L.Get("Task_PairDestination")) is not { } destination)
        {
            return;
        }

        _pairs[source] = destination;
        ShowPairs();
    }

    private void ClearPairs_Click(object sender, RoutedEventArgs e)
    {
        _pairs.Clear();
        ShowPairs();
    }

    private void ShowPairs()
    {
        PairsInfo.Text = string.Join(Environment.NewLine, _pairs.Select(p => $"{p.Key}  →  {p.Value}"));
        PairsInfo.Visibility = ClearPairsButton.Visibility = _pairs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartNow_Click(object sender, RoutedEventArgs e)
    {
        _job?.Schedule(null);
        Load();
    }

    private void SaveDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_job?.Job.Record is { } r)
        {
            var app = (App)Application.Current;
            app.Settings.CopyDefaultsFrom(r);
            app.Settings.Save();
        }
    }

    private void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_job?.Job is { } job)
        {
            ((App)Application.Current).Settings.ApplyDefaultsTo(job.Record);
            job.SetLiveOptions(verify: job.Record.Verify);
            Load();
        }
    }
}
