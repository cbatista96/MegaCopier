using System.Collections.Concurrent;
using MegaCopier.Engine.Network;

namespace MegaCopier.Engine.Tests;

/// <summary>Envío entre equipos (aquí, emisor y receptor en el mismo equipo).</summary>
public sealed class PeerTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _code = PeerProtocol.NewCode();
    private readonly PeerReceiver _receiver;
    private readonly ConcurrentQueue<PeerEvent> _events = new();

    public PeerTests()
    {
        Directory.CreateDirectory(_temp.Combine("recibidos"));
        _receiver = new PeerReceiver(_temp.Combine("recibidos"), _code, PeerProtocol.CreateCertificate("prueba"), port: 0, loopbackOnly: true);
        _receiver.Event += _events.Enqueue;
        _receiver.Start();
    }

    public void Dispose()
    {
        _receiver.Dispose();
        _temp.Dispose();
    }

    private string Destination => $"127.0.0.1:{_receiver.Port}";

    private CopyEngine Sender(string? code = null) => new(new CopyOptions
    {
        Operation = JobOperation.Send,
        PeerCode = code ?? _code.ToLowerInvariant().Replace("-", " "), // se escribe como sea
        ProgressInterval = TimeSpan.Zero,
    });

    private string Write(string relative, byte[] content, DateTime? lastWrite = null)
    {
        string path = _temp.Combine(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, lastWrite ?? new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        return path;
    }

    private PeerEvent WaitFor(PeerEventKind kind)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            if (_events.FirstOrDefault(e => e.Kind == kind) is { } found)
            {
                return found;
            }

            Thread.Sleep(20);
        }

        throw new TimeoutException(kind.ToString());
    }

    [Fact]
    public void FolderArrivesIdenticalAndIsNotSentTwice()
    {
        var big = new byte[3_500_000];
        new Random(3).NextBytes(big);
        Write("fotos/viaje/playa.jpg", big);
        Write("fotos/nota.txt", "hola"u8.ToArray());

        var engine = Sender();
        var result = engine.Run(new CopyJobRequest([_temp.Combine("fotos")], Destination));

        Assert.True(result.Success, string.Join("; ", result.Failures.Select(f => f.Message)));
        Assert.Equal(2, result.FilesCopied);
        Assert.Equal(big, File.ReadAllBytes(_temp.Combine("recibidos/fotos/viaje/playa.jpg")));
        Assert.Equal(new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc), File.GetLastWriteTimeUtc(_temp.Combine("recibidos/fotos/nota.txt")));
        Assert.All(engine.Files.Snapshot(), i => Assert.Equal(FileState.Verified, i.State));
        Assert.Equal(2, WaitFor(PeerEventKind.Finished).Files);

        var again = Sender().Run(new CopyJobRequest([_temp.Combine("fotos")], Destination));
        Assert.Equal(2, again.FilesSkipped);
        Assert.Equal(0, again.FilesCopied);
    }

    [Fact]
    public void WrongCodeIsRejected()
    {
        Write("secreto.txt", "nada"u8.ToArray());

        var ex = Assert.Throws<CopyPlanException>(() => Sender("AAAA-BBBB-CCCC").Run(new CopyJobRequest([_temp.Combine("secreto.txt")], Destination)));

        Assert.Contains("código", ex.Message, StringComparison.OrdinalIgnoreCase);
        WaitFor(PeerEventKind.Rejected);
        Assert.Empty(Directory.GetFileSystemEntries(_temp.Combine("recibidos")));
    }

    [Fact]
    public void InterruptedSendContinuesWhereItStopped()
    {
        var data = new byte[24 * 1024 * 1024];
        new Random(9).NextBytes(data);
        Write("video.mp4", data);
        using var cancellation = new CancellationTokenSource();
        var first = Sender();
        first.ProgressChanged += p =>
        {
            if (p.CompletedBytes > 10L * 1024 * 1024)
            {
                cancellation.Cancel();
            }
        };

        Assert.ThrowsAny<OperationCanceledException>(() => first.Run(new CopyJobRequest([_temp.Combine("video.mp4")], Destination), cancellation.Token));
        WaitFor(PeerEventKind.Failed); // el receptor ve la conexión cortada y guarda lo que llegó
        Assert.True(File.Exists(_temp.Combine("recibidos/video.mp4.mcpart")));

        var second = Sender().Run(new CopyJobRequest([_temp.Combine("video.mp4")], Destination));

        Assert.True(second.Success);
        Assert.Equal(data, File.ReadAllBytes(_temp.Combine("recibidos/video.mp4")));
        var finished = WaitFor(PeerEventKind.Finished);
        Assert.True(finished.Bytes < data.Length, $"reenvió {finished.Bytes} de {data.Length} bytes");
    }

    [Theory]
    [InlineData(@"fotos\a.jpg", true)]
    [InlineData(@"..\..\Windows\evil.dll", false)]
    [InlineData(@"C:\Windows\evil.dll", false)]
    [InlineData(@"\\servidor\x", false)]
    [InlineData(@"fotos\.\a.jpg", false)]
    [InlineData(@"fotos\a.jpg:oculto", false)]
    public void OnlySafeRelativePathsAreAccepted(string relative, bool accepted) =>
        Assert.Equal(accepted, PeerProtocol.SafeRelative(relative) is not null);

    [Fact]
    public async Task ReceiversCanBeFoundOnTheNetwork()
    {
        using var discovery = new PeerDiscovery("PC-PRUEBA", _receiver.Port, discoveryPort: 47912, loopbackOnly: true);
        var found = await PeerDiscovery.SearchAsync(TimeSpan.FromSeconds(1), discoveryPort: 47912);
        Assert.Contains(found, p => p.Name == "PC-PRUEBA" && p.Port == _receiver.Port);
    }

    [Fact]
    public void PeerAddressesAreParsed()
    {
        Assert.Equal(("PC-SALON", PeerProtocol.DefaultPort), CopyEngine.ParsePeer("PC-SALON"));
        Assert.Equal(("192.168.1.20", 5000), CopyEngine.ParsePeer("192.168.1.20:5000"));
        Assert.Equal(("fe80::1", 5000), CopyEngine.ParsePeer("[fe80::1]:5000"));
        Assert.Equal(("pc", 47801), CopyEngine.ParsePeer("mc://pc:47801"));
    }
}
