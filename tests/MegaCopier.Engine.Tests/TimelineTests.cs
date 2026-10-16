using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

public sealed class TimelineTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> NoFast = [];

    private static TimelineInput Job(string id, string devices, bool running = false, long? mb = 100, double mbps = 100, DateTime? at = null) =>
        new(id, devices.Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase), running, running ? Now.AddMinutes(-1) : null, at, mb * 1_000_000, mbps * 1_000_000);

    private static Dictionary<string, TimelineEntry> Run(int max, HashSet<string> fast, params TimelineInput[] jobs) =>
        QueueTimeline.Estimate(jobs, fast, max, Now).ToDictionary(e => e.Id);

    [Fact]
    public void SameDiskWaitsOtherDiskRunsAtOnce()
    {
        var t = Run(0, NoFast,
            Job("a", "DISK0,DISK1", running: true, mb: 1000),     // 10 s más
            Job("b", "DISK1,DISK2", mb: 500),                      // espera a "a" (DISK1)
            Job("c", "DISK3,DISK4", mb: 200));                     // otro disco: ya

        Assert.Equal(Now.AddMinutes(-1), t["a"].StartUtc);
        Assert.Equal(Now.AddSeconds(10), t["a"].EndUtc);
        Assert.Equal(Now.AddSeconds(10), t["b"].StartUtc);
        Assert.Equal(Now.AddSeconds(15), t["b"].EndUtc);
        Assert.Equal(Now, t["c"].StartUtc);
        Assert.Equal(Now.AddSeconds(2), t["c"].EndUtc);
    }

    [Fact]
    public void FastDisksAndTheJobLimit()
    {
        var fast = Run(0, ["DISK1"], Job("a", "DISK1", running: true), Job("b", "DISK1"));
        Assert.Equal(Now, fast["b"].StartUtc);

        // Como mucho uno a la vez: "c" (otro disco) también espera.
        var limited = Run(1, NoFast, Job("a", "DISK0", running: true, mb: 300), Job("c", "DISK5", mb: 100));
        Assert.Equal(Now.AddSeconds(3), limited["c"].StartUtc);
        Assert.Equal(Now.AddSeconds(4), limited["c"].EndUtc);
    }

    [Fact]
    public void ScheduledJobsWaitForTheirTimeAndUnknownSizesAreGuessed()
    {
        var t = Run(0, NoFast,
            Job("p", "DISK0", at: Now.AddHours(2)),
            Job("q", "DISK0", mb: null));

        // "q" va primero (el programado deja libre el disco hasta su hora); su tamaño no se sabe.
        Assert.Equal(Now, t["q"].StartUtc);
        Assert.False(t["q"].Known);
        Assert.Equal(Now + QueueTimeline.UnknownDuration, t["q"].EndUtc);
        Assert.Equal(Now.AddHours(2), t["p"].StartUtc);
        Assert.True(t["p"].Known);
    }

    [Fact]
    public void ScheduledJobBlocksNothingButWaitsForBusyDisk()
    {
        var t = Run(0, NoFast,
            Job("a", "DISK0", running: true, mb: 100_000), // 1000 s
            Job("p", "DISK0", at: Now.AddMinutes(5)));
        Assert.Equal(Now.AddSeconds(1000), t["p"].StartUtc);
    }

    [Fact]
    public void MeasuresSourceSizes()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(temp.Combine("d/sub"));
        File.WriteAllBytes(temp.Combine("d/sub/x.bin"), new byte[1234]);
        File.WriteAllBytes(temp.Combine("d/y.bin"), new byte[66]);
        File.WriteAllBytes(temp.Combine("z.bin"), new byte[100]);
        Assert.Equal(1400, QueueTimeline.MeasureSources([temp.Combine("d"), temp.Combine("z.bin"), temp.Combine("no-existe")]));
    }
}
