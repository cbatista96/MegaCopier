using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class AdministratorRetryTests
{
    [Fact]
    public void AdministratorRetryIsActiveAndCannotBeStartedTwice()
    {
        using var temp = new TempDir();
        var job = new CopyJob(CopyJobRecord.New([temp.Combine("protegido")], temp.Combine("destino"), new CopyOptions()),
            CopyJobState.CompletedWithErrors, _ => { });
        var result = new CopyJobResult();
        result.Failures.Add(new FileFailure(temp.Combine("protegido"), "denegado", FileErrorKind.AccessDenied));
        job.SetResult(result);
        Assert.True(job.CanRetryWithAdministrator);
        Assert.True(job.BeginAdministratorRetry());
        Assert.True(job.IsActive);
        Assert.False(job.CanRetryWithAdministrator);
        Assert.False(job.BeginAdministratorRetry());
        job.EndAdministratorRetry();
        Assert.False(job.IsActive);
        Assert.False(job.Result!.Success); // Rechazar el aviso de Windows no borra el fallo real.
        Assert.True(job.CanRetryWithAdministrator);
    }

    [Fact]
    [Trait("Category", "ExternalVolume")]
    public void ARealProtectedFileIsNeverReportedAsCopiedWithoutPermission()
    {
        string? source = Environment.GetEnvironmentVariable("MEGACOPIER_TEST_PROTECTED_SOURCE");
        if (string.IsNullOrWhiteSpace(source)) return;
        using var temp = new TempDir();
        var engine = new CopyEngine(new CopyOptions { RetryCount = 0, Verify = true });
        var result = engine.Run(new CopyJobRequest([source], temp.Path));
        Assert.False(result.Success);
        Assert.Equal(0, result.FilesCopied);
        Assert.Equal(FileErrorKind.AccessDenied, Assert.Single(result.Failures).Kind);
        Assert.False(File.Exists(temp.Combine(Path.GetFileName(source))));
    }

    [Fact]
    public void OtherFailuresDoNotAskForAdministratorPermissions()
    {
        using var temp = new TempDir();
        var job = new CopyJob(CopyJobRecord.New([temp.Combine("archivo")], temp.Combine("destino"), new CopyOptions()),
            CopyJobState.CompletedWithErrors, _ => { });
        var result = new CopyJobResult();
        result.Failures.Add(new FileFailure("archivo", "parametro", FileErrorKind.Other));
        job.SetResult(result);
        Assert.False(job.BeginAdministratorRetry());
        Assert.False(job.IsActive);
    }
}
