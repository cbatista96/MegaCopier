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
        Place(WhatColumn, 0, 0, columns == 1 ? 3 : 1);
        Place(HowColumn, columns == 1 ? 1 : 0, columns == 1 ? 0 : 1, columns == 1 ? 3 : 1);
        Place(FinishColumn, columns == 3 ? 0 : columns == 2 ? 1 : 2, columns == 3 ? 2 : 0, columns == 3 ? 1 : 3);
        ScheduleRow.Orientation = width < 760 ? Orientation.Vertical : Orientation.Horizontal;
        TaskRow.Orientation = ScheduleRow.Orientation;
        ScheduleRow.HorizontalAlignment = HorizontalAlignment.Left;
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
        job.SetLiveOptions(verify: VerifyBox.IsChecked == true);
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
            : $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(record.Sources[0]))} → {Path.GetFileName(Path.TrimEndingDirectorySeparator(record.Destination))}";
        var frequency = (TaskFrequency)Math.Max(0, TaskFrequencyBox.SelectedIndex);
        var start = date.Date + ScheduleTime.Time;
        string? error = ScheduledTasks.Create(record, name, frequency, start);
        TaskInfo.Text = error is null
            ? L.F("Task_Saved" + frequency, ScheduledTasks.CleanName(name), start.ToString("t"), start.ToString("d"))
            : L.F("Task_Failed", error);
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
