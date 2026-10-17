using System.Security.Cryptography;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine.Tests;

/// <summary>OneDrive, Google Drive y Dropbox contra servidores de prueba en 127.0.0.1 (con su inicio de sesión OAuth).</summary>
[Collection("Cloud")] // los límites de subida simple son estáticos
public sealed class CloudStoreTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly long _oneDriveLimit = OneDriveStore.SimpleUploadLimit;
    private readonly long _dropboxLimit = DropboxStore.SimpleUploadLimit;

    public CloudStoreTests()
    {
        // Límites pequeños: así un archivo de unos MB ya se sube por sesión, en trozos.
        OneDriveStore.SimpleUploadLimit = 1_000_000;
        DropboxStore.SimpleUploadLimit = 1_000_000;
    }

    public void Dispose()
    {
        OneDriveStore.SimpleUploadLimit = _oneDriveLimit;
        DropboxStore.SimpleUploadLimit = _dropboxLimit;
        _temp.Dispose();
    }

    public static TheoryData<string> Clouds => ["onedrive", "google", "dropbox"];

    private static FakeCloud Create(string name) => name switch
    {
        "onedrive" => new FakeOneDrive(),
        "google" => new FakeGoogleDrive(),
        _ => new FakeDropbox(),
    };

    /// <summary>El inicio de sesión de verdad: el "navegador" sigue la redirección hasta la app, que cambia el código por tokens.</summary>
    private static string SignIn(Connection connection)
    {
        using var browser = new HttpClient();
        return CloudAuth.SignInAsync(connection, url => _ = browser.GetAsync(url)).GetAwaiter().GetResult();
    }

    [Theory]
    [MemberData(nameof(Clouds))]
    public void SignInListUploadDownloadAndDelete(string cloud)
    {
        using var fake = Create(cloud);
        var connection = fake.Connection();
        string refresh = SignIn(connection);
        Assert.StartsWith("RT-", refresh);

        using var store = Connections.Open(connection, refresh);
        string folder = store.CreateFolder(store.RootId, "Fotos ñ");
        Assert.Equal(folder, store.CreateFolder(store.RootId, "Fotos ñ")); // ya existía: la misma, sin duplicarla
        byte[] small = RandomNumberGenerator.GetBytes(300_000);
        byte[] big = RandomNumberGenerator.GetBytes(23_000_000); // por sesión, en varios trozos
        string smallId = store.Upload(folder, "pequeño.bin", new MemoryStream(small), small.Length, CancellationToken.None);
        string bigId = store.Upload(folder, "grande.bin", new MemoryStream(big), big.Length, CancellationToken.None);
        for (int i = 0; i < 3; i++)
        {
            store.Upload(folder, $"extra{i}.txt", new MemoryStream([(byte)i]), 1, CancellationToken.None);
        }

        var children = store.Children(folder); // 5 elementos en páginas de 2
        Assert.Equal(5, children.Count);
        Assert.Equal(big.Length, children.Single(c => c.Name == "grande.bin").Size);
        using (var read = store.OpenRead(bigId))
        using (var copy = new MemoryStream())
        {
            read.CopyTo(copy);
            Assert.Equal(big, copy.ToArray());
        }

        // El token de acceso caduca: se renueva solo.
        fake.ExpireAccessTokens();
        Assert.Contains(store.Children(store.RootId), c => c.IsFolder && c.Name == "Fotos ñ");

        store.Delete(smallId, recursive: false);
        Assert.DoesNotContain(store.Children(folder), c => c.Name == "pequeño.bin");
    }

    [Theory]
    [MemberData(nameof(Clouds))]
    public void TheEngineCopiesToAndFromTheCloud(string cloud)
    {
        using var fake = Create(cloud);
        var connection = fake.Connection();
        string refresh = SignIn(connection);
        string local = Directory.CreateDirectory(_temp.Combine(cloud, "viaje", "día 2")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(local, "día 2", "vídeo.mp4"), RandomNumberGenerator.GetBytes(3_500_000));
        File.WriteAllText(Path.Combine(local, "notas.txt"), "apuntes del viaje");

        CopyEngine Engine() => new(new CopyOptions { Verify = true, ProgressInterval = TimeSpan.Zero }) { RemoteOpener = _ => Connections.Open(connection, refresh) };
        string remote = RemotePath.Format(connection.Id, connection.Name, "");
        var up = Engine().Run(new CopyJobRequest([local], remote));
        Assert.Empty(up.Failures);
        Assert.Equal(2, up.FilesCopied);

        string back = _temp.Combine(cloud, "vuelta");
        var down = Engine().Run(new CopyJobRequest([RemotePath.Combine(remote, "viaje")], back));
        Assert.Empty(down.Failures);
        Assert.Equal(File.ReadAllBytes(Path.Combine(local, "día 2", "vídeo.mp4")), File.ReadAllBytes(Path.Combine(back, "viaje", "día 2", "vídeo.mp4")));
        Assert.Equal("apuntes del viaje", File.ReadAllText(Path.Combine(back, "viaje", "notas.txt")));
    }

    [Fact]
    public void GoogleDocsAreNotListedAndAWrongAppIsRefused()
    {
        using var fake = new FakeGoogleDrive();
        fake.AddGoogleDoc("Presupuesto");
        var connection = fake.Connection();
        using (var store = Connections.Open(connection, SignIn(connection)))
        {
            Assert.Empty(store.Children(store.RootId));
        }

        connection.ClientId = "otra-app";
        Assert.Throws<UnauthorizedAccessException>(() => SignIn(connection));
    }
}
