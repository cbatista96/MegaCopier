using System.Security.AccessControl;
using System.Security.Principal;
using MegaCopier.Engine.Jobs;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

public sealed class ProtectedFolderTests
{
    [Fact]
    public void BackupDirectoryEnumerationHandlesEmptyUnicodeAndMultiplePages()
    {
        using var temp = new TempDir();
        Assert.Empty(BackupPrivileges.EnumerateDirectory(temp.Path));
        Directory.CreateDirectory(temp.Combine("niños"));
        for (int i = 0; i < 700; i++) File.WriteAllText(temp.Combine($"archivo-{i:D4}.txt"), "x");
        var entries = BackupPrivileges.EnumerateDirectory(temp.Path);
        Assert.Equal(701, entries.Count);
        Assert.Single(entries.OfType<DirectoryInfo>());
        Assert.Equal(700, entries.OfType<FileInfo>().Count());
        Assert.Equal(701, entries.Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public async Task PlanningDenialKeepsTheJobAndTheElevatedResultFinishesItWithoutReadingTheSourceAgain()
    {
        using var temp = new TempDir();
        var source = Directory.CreateDirectory(temp.Combine("protegida"));
        File.WriteAllText(Path.Combine(source.FullName, "dato.txt"), "contenido comprobado");
        var denied = source.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        denied.AddAccessRule(rule);
        var store = new JobStore(temp.Combine("jobs"));
        var queue = new CopyQueue(store);
        var finished = new TaskCompletionSource<CopyJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.JobFinished += job => finished.TrySetResult(job);
        CopyJob job;
        try
        {
            source.SetAccessControl(denied);
            job = queue.Submit([source.FullName], temp.Combine("destino"), new CopyOptions { RetryCount = 0, Verify = true });
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(CopyJobState.Failed, job.State);
            Assert.Equal(FileErrorKind.AccessDenied, job.ErrorKind);
            Assert.NotNull(store.Get(job.Id));
            Assert.True(job.BeginAdministratorRetry());
        }
        finally
        {
            denied.RemoveAccessRuleSpecific(rule);
            source.SetAccessControl(denied);
        }

        // El trabajador ya autorizado devuelve las filas y hashes de lo que realmente copió.
        var worker = new CopyEngine(new CopyOptions { Verify = true });
        var result = worker.Run(new CopyJobRequest([source.FullName], job.Record.Destination));
        Assert.True(result.Success);
        var report = AdministratorCopyReport.Create(job.Id, worker, result);
        report.JobId = "otro-trabajo";
        Assert.False(queue.ApplyAdministratorReport(job, report));
        report.JobId = job.Id;
        var first = report.Files[0];
        report.Files[0] = first with { Destination = temp.Combine("fuera-del-destino.txt") };
        Assert.False(queue.ApplyAdministratorReport(job, report));
        report.Files[0] = first;
        report.Save(temp.Combine("resultado.json"));
        report = AdministratorCopyReport.Load(temp.Combine("resultado.json"))!;
        Directory.Move(source.FullName, source.FullName + "-no-disponible");
        Assert.True(queue.ApplyAdministratorReport(job, report));
        Assert.Equal(CopyJobState.Completed, job.State);
        Assert.Null(job.Error);
        Assert.True(job.Result!.Success);
        Assert.False(job.AdministratorRetryInProgress);
        Assert.Null(store.Get(job.Id));
        Assert.Equal(FileState.Verified, Assert.Single(job.Files!.Snapshot()).State);
        Assert.Equal("contenido comprobado", File.ReadAllText(temp.Combine("destino", "protegida", "dato.txt")));
    }

    [Theory]
    [InlineData(5, FileErrorKind.AccessDenied)]
    [InlineData(87, FileErrorKind.Other)]
    public void PlanningErrorsKeepTheirRealCause(int code, FileErrorKind expected) =>
        Assert.Equal(expected, FileErrors.Classify(new CopyPlanException("plan", new IOException("causa", unchecked((int)0x80070000) | code))));
}
