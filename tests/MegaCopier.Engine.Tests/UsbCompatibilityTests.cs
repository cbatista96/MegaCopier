using System.Security.Cryptography;
using MegaCopier.Engine.Native;

namespace MegaCopier.Engine.Tests;

public sealed class UsbCompatibilityTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(87, true)]
    [InlineData(120, true)]
    [InlineData(5, false)]
    [InlineData(32, false)]
    [InlineData(112, false)]
    public void RenameFallbackDoesNotHidePermissionOrDiskErrors(int error, bool fallback) =>
        Assert.Equal(fallback, SmallFile.UnsupportedRename(error));

    [Fact]
    [Trait("Category", "ExternalVolume")]
    public void CopiesAndVerifiesSmallFilesAndAnOptionalRealFolderOnUsb()
    {
        string? volume = Environment.GetEnvironmentVariable("MEGACOPIER_TEST_USB_ROOT");
        if (string.IsNullOrWhiteSpace(volume)) return;
        volume = Path.GetFullPath(volume);
        Assert.True(Directory.Exists(volume));
        string destination = Path.Combine(volume, "MegaCopier-verificacion-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.TrimEndingDirectorySeparator(volume), Path.GetDirectoryName(destination), ignoreCase: true);
        using var source = new TempDir();
        Directory.CreateDirectory(source.Combine("Sub"));
        foreach (int size in new[] { 0, 1, 1300, 4096, 65_537, 1_048_577 })
            File.WriteAllBytes(source.Combine("Sub", $"archivo-{size}.bin"), RandomNumberGenerator.GetBytes(size));
        File.SetAttributes(source.Combine("Sub", "archivo-1300.bin"), FileAttributes.ReadOnly);
        var sources = new List<string> { source.Combine("Sub") };
        if (Environment.GetEnvironmentVariable("MEGACOPIER_TEST_USB_SOURCE") is { Length: > 0 } realSource)
            sources.Add(realSource);
        try
        {
            var engine = new CopyEngine(new CopyOptions { Verify = true, UseUnbufferedIo = true, CopyStreams = true, RetryCount = 0 });
            var result = engine.Run(new CopyJobRequest(sources, destination));
            Assert.True(result.Success, string.Join("\n", result.Failures.Select(f => f.Source + ": " + f.Message)));
            foreach (var item in engine.Files.Snapshot())
            {
                Assert.Equal(FileState.Verified, item.State);
                using var original = File.OpenRead(item.Source);
                using var copied = File.OpenRead(item.Destination);
                Assert.Equal(SHA256.HashData(original), SHA256.HashData(copied));
            }
            Assert.True(File.GetAttributes(Path.Combine(destination, "Sub", "archivo-1300.bin")).HasFlag(FileAttributes.ReadOnly));
            var replace = new CopyEngine(new CopyOptions { Verify = true, Conflict = ConflictPolicy.Overwrite, ForceRecopy = true, RetryCount = 0 });
            result = replace.Run(new CopyJobRequest([source.Combine("Sub")], destination));
            Assert.True(result.Success, string.Join("\n", result.Failures.Select(f => f.Message)));
            Assert.Equal(6, result.FilesCopied);
        }
        finally
        {
            if (Directory.Exists(destination))
            {
                foreach (string file in Directory.EnumerateFiles(destination, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(destination, recursive: true);
            }
        }
    }
}
