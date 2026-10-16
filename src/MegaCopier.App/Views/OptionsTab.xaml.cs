using MegaCopier.App.Services;
using MegaCopier.App.ViewModels;
using MegaCopier.Engine;
using MegaCopier.Engine.Jobs;
using MegaCopier.Engine.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MegaCopier.App.Views;

/// <summary>
/// Pestaña Opciones de un trabajo (como en TeraCopy). Se guardan en el propio trabajo: si está en marcha se aplican al
/// reanudarlo; "Verificar" y "Desatendido" del panel cambian en caliente.
/// </summary>
public sealed partial class OptionsTab : UserControl
{
    internal static readonly FileHashAlgorithm[] Hashes =
    [
        FileHashAlgorithm.None, FileHashAlgorithm.Crc32, FileHashAlgorithm.XxHash3, FileHashAlgorithm.XxHash64, FileHashAlgorithm.XxHash128,
        FileHashAlgorithm.Md5, FileHashAlgorithm.Sha1, FileHashAlgorithm.Sha256, FileHashAlgorithm.Sha512,
    ];

    private bool _loading;
    private JobViewModel? _job;

    public OptionsTab()
    {
        InitializeComponent();
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
        FileHashAlgorithm.Crc32 => "Hash: CRC32",
        FileHashAlgorithm.XxHash3 => "Hash: xxHash3-64",
        FileHashAlgorithm.XxHash64 => "Hash: xxHash64",
        FileHashAlgorithm.XxHash128 => "Hash: xxHash3-128",
        FileHashAlgorithm.Md5 => "Hash: MD5",
        FileHashAlgorithm.Sha1 => "Hash: SHA-1",
        FileHashAlgorithm.Sha256 => "Hash: SHA-256",
        _ => "Hash: SHA-512",
    };

    public static int ConflictIndex(ConflictPolicy conflict) => conflict switch
    {
        ConflictPolicy.Overwrite => 1,
        ConflictPolicy.Skip => 2,
        ConflictPolicy.OverwriteIfNewer => 3,
        ConflictPolicy.KeepBoth => 4,
        _ => 0,
    };

    public static ConflictPolicy ConflictAt(int index) => index switch
    {
        1 => ConflictPolicy.Overwrite,
        2 => ConflictPolicy.Skip,
        3 => ConflictPolicy.OverwriteIfNewer,
        4 => ConflictPolicy.KeepBoth,
        _ => ConflictPolicy.Ask,
    };

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
        job.SetLiveOptions(verify: VerifyBox.IsChecked == true);
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
