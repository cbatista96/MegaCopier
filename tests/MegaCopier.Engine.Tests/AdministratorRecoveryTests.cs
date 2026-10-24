using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class AdministratorRecoveryTests
{
    [Fact]
    public void LockedFileUsesTheSameAdministratorRetry()
    {
        using var temp = new TempDir();
        var job = new CopyJob(CopyJobRecord.New([temp.Combine("source")], temp.Combine("dest"), new CopyOptions()),
            CopyJobState.CompletedWithErrors, _ => { });
        var result = new CopyJobResult();
        result.Failures.Add(new("source", "locked", FileErrorKind.InUse));
        job.SetResult(result);
        Assert.True(job.BeginAdministratorRetry());
        Assert.False(job.BeginAdministratorRetry());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotRecoveryReturnsTheRealResultWithoutReopeningTheOriginal(bool snapshotFails)
    {
        using var temp = new TempDir();
        string source = temp.Combine("original.bin"), destination = temp.Combine("dest", "renamed.bin");
        File.WriteAllText(source, "contenido");
        string root = temp.Combine("snapshot");
        string shadow = Path.Combine(root, Path.GetFullPath(source)[Path.GetPathRoot(source)!.Length..]);
        Directory.CreateDirectory(Path.GetDirectoryName(shadow)!);
        File.Copy(source, shadow);
        using var locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var options = new CopyOptions { Verify = true, RetryCount = 0 };
        var engine = new CopyEngine(options);
        var result = engine.Run(new CopyJobRequest([source], Path.GetDirectoryName(destination)!));
        Assert.Equal(FileErrorKind.InUse, Assert.Single(result.Failures).Kind);
        var report = AdministratorCopyReport.Create("job", engine, result);
        report.Files[0] = report.Files[0] with { Destination = destination };
        var snapshots = new Snapshot(root, snapshotFails);
        report.RecoverLockedFiles(snapshots, options);
        if (snapshotFails)
        {
            Assert.Single(report.Failures);
            Assert.Equal(FileState.Failed, report.Files[0].State);
            Assert.False(File.Exists(destination));
        }
        else
        {
            Assert.Empty(report.Failures);
            Assert.Equal(1, report.FilesCopied);
            var file = Assert.Single(report.Files);
            Assert.Equal(source, file.Source);
            Assert.Equal(destination, file.Destination);
            Assert.Equal(FileState.Verified, file.State);
            Assert.Equal(file.SourceHash, file.DestinationHash);
            Assert.NotNull(file.SourceHash);
            Assert.Equal("contenido", File.ReadAllText(destination));
            Assert.True(snapshots.Deleted);
        }
    }

    private sealed class Snapshot(string root, bool fail) : ISnapshotProvider
    {
        public bool Deleted { get; private set; }
        public (string Root, string Id) Create(string volume) => fail
            ? throw new IOException("snapshot unavailable") : (root, "test");
        public void Delete(string id) => Deleted = true;
    }
}
