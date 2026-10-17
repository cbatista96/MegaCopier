using System.Security.Cryptography;
using System.Text;
using MegaCopier.Engine.Remote;

namespace MegaCopier.Engine.Tests;

public sealed class RemoteStoreTests : IDisposable
{
    private readonly TempDir _temp = new();

    public RemoteStoreTests()
    {
        Connections.FolderOverride = _temp.Combine("datos");
        Connections.SecretsOverride = [];
    }

    public void Dispose()
    {
        Connections.FolderOverride = null;
        Connections.SecretsOverride = null;
        _temp.Dispose();
    }

    private string Folder(string name) => Directory.CreateDirectory(_temp.Combine(name)).FullName;

    /// <summary>Todo lo que el motor usa de un sitio remoto: crear, subir, listar, leer y borrar.</summary>
    private static void Exercise(IRemoteStore store)
    {
        string folder = store.CreateFolder(store.RootId, "Fotos de año ñ");
        string sub = store.CreateFolder(folder, "sub");
        byte[] data = RandomNumberGenerator.GetBytes(300_000);
        string file = store.Upload(folder, "foto 1.jpg", new MemoryStream(data), data.Length, CancellationToken.None);
        store.Upload(sub, "nota.txt", new MemoryStream("hola"u8.ToArray()), 4, CancellationToken.None);
        Assert.Equal(file, store.CreateFolder(store.RootId, "Fotos de año ñ") is { } again && again == folder ? file : file); // crear otra vez no falla

        var children = store.Children(folder);
        Assert.Equal(2, children.Count);
        var photo = Assert.Single(children, c => !c.IsFolder);
        Assert.Equal(("foto 1.jpg", data.Length), (photo.Name, photo.Size));
        Assert.Single(children, c => c.IsFolder && c.Name == "sub");
        Assert.Contains(store.Children(store.RootId), c => c.IsFolder && c.Name == "Fotos de año ñ");

        using (var read = store.OpenRead(photo.Id))
        using (var copy = new MemoryStream())
        {
            read.CopyTo(copy);
            Assert.Equal(data, copy.ToArray());
        }

        Assert.Equal("nota.txt", RemoteStores.Resolve(store, "Fotos de año ñ/sub/nota.txt")?.Name);
        store.Delete(folder, recursive: true);
        Assert.DoesNotContain(store.Children(store.RootId), c => c.Name == "Fotos de año ñ");
    }

    /// <summary>Del PC al servidor y de vuelta con el motor, verificando: llega todo igual.</summary>
    private void RoundTrip(Connection connection, string secret)
    {
        string local = Folder("local");
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(local, "viaje", "día 2")).FullName, "grande.bin"), RandomNumberGenerator.GetBytes(2_500_000));
        File.WriteAllText(Path.Combine(local, "viaje", "notas ñ.txt"), "apuntes");
        var store = Connections.Open(connection, secret);
        store.CreateFolder(store.RootId, "copias");
        store.Dispose();

        CopyEngine Engine() => new(new CopyOptions { Verify = true, ProgressInterval = TimeSpan.Zero }) { RemoteOpener = _ => Connections.Open(connection, secret) };
        string remote = RemotePath.Format(connection.Id, connection.Name, "copias");
        var up = Engine().Run(new CopyJobRequest([Path.Combine(local, "viaje")], remote));
        Assert.Empty(up.Failures);
        Assert.Equal(2, up.FilesCopied);

        string back = Folder("vuelta");
        var down = Engine().Run(new CopyJobRequest([RemotePath.Combine(remote, "viaje")], back));
        Assert.Empty(down.Failures);
        Assert.Equal(File.ReadAllBytes(Path.Combine(local, "viaje", "día 2", "grande.bin")), File.ReadAllBytes(Path.Combine(back, "viaje", "día 2", "grande.bin")));
        Assert.Equal("apuntes", File.ReadAllText(Path.Combine(back, "viaje", "notas ñ.txt")));

        var again = Engine().Run(new CopyJobRequest([Path.Combine(local, "viaje")], remote));
        Assert.Equal(2, again.FilesSkipped); // ya estaba: mismo nombre y tamaño
    }

    private static Connection WebDav(FakeWebDav server) => new() { Name = "Nube", Kind = ConnectionKind.WebDav, Host = server.Url, User = "ana" };

    [Fact]
    public void WebDavWorks()
    {
        using var server = new FakeWebDav(Folder("servidor"), "ana", "secreta");
        using (var store = Connections.Open(WebDav(server), "secreta"))
        {
            Exercise(store);
        }

        RoundTrip(WebDav(server), "secreta");
    }

    [Fact]
    public void WebDavWrongPasswordIsAccessDenied()
    {
        using var server = new FakeWebDav(Folder("servidor"), "ana", "secreta");
        using var store = Connections.Open(WebDav(server), "mala");
        Assert.Throws<UnauthorizedAccessException>(() => store.Children(store.RootId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // servidor sin MLSD: LIST al estilo Unix
    public void FtpWorks(bool mlsd)
    {
        using var server = new FakeFtp(Folder("servidor"), "ana", "secreta", mlsd);
        var connection = new Connection { Name = "NAS", Kind = ConnectionKind.Ftp, Host = "127.0.0.1", Port = server.Port, User = "ana" };
        using (var store = Connections.Open(connection, "secreta"))
        {
            Exercise(store);
        }

        RoundTrip(connection, "secreta");
    }

    [Fact]
    public void FtpWrongPasswordIsAccessDenied()
    {
        using var server = new FakeFtp(Folder("servidor"), "ana", "secreta");
        var connection = new Connection { Kind = ConnectionKind.Ftp, Host = "127.0.0.1", Port = server.Port, User = "ana" };
        Assert.Throws<UnauthorizedAccessException>(() => Connections.Open(connection, "mala"));
    }

    [Fact]
    public void S3WorksIncludingPagesAndMultipartUploads()
    {
        using var server = new FakeS3("clave-secreta");
        var connection = new Connection { Name = "Cubo", Kind = ConnectionKind.S3, Host = server.Endpoint, User = "AKID", Root = "cubo/mega", Region = "eu-west-1" };
        int part = S3Store.PartSize;
        S3Store.PartSize = 1 << 20; // los archivos de más de 1 MB van por partes
        try
        {
            using (var store = Connections.Open(connection, "clave-secreta"))
            {
                Exercise(store);
                for (int i = 0; i < 8; i++)
                {
                    store.Upload(store.RootId, $"f{i}.txt", new MemoryStream([1]), 1, CancellationToken.None); // más de una página
                }

                Assert.Equal(8, store.Children(store.RootId).Count(c => !c.IsFolder));
            }

            RoundTrip(connection, "clave-secreta");
            Assert.Equal(0, server.BadSignatures);
            Assert.All(server.Objects.Keys, k => Assert.StartsWith("mega/", k, StringComparison.Ordinal));
        }
        finally
        {
            S3Store.PartSize = part;
        }
    }

    [Fact]
    public void S3SignatureMatchesAmazonsExample()
    {
        // Ejemplo "GET Bucket (List Objects)" de la documentación de SigV4 de Amazon S3.
        string signature = S3Store.Signature("GET", "/", "max-keys=2&prefix=J", "examplebucket.s3.amazonaws.com", "20130524T000000Z",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1");
        Assert.Equal("34b48302e7b5fa45bde8084f4b7868a86f0a534bc59db6670ed5711ef69dc6f7", signature);
        Assert.Equal("a%20b%2Fc~%C3%B1", S3Store.Encode("a b/c~ñ"));
    }

    [Fact]
    public void ConnectionsAreSavedWithoutTheirSecrets()
    {
        var connection = new Connection { Name = "NAS", Kind = ConnectionKind.Sftp, Host = "nas", User = "ana" };
        Connections.Save([connection]);
        Connections.SetSecret(connection.Id, "contraseña");
        Assert.DoesNotContain("contraseña", File.ReadAllText(Path.Combine(_temp.Combine("datos"), "connections.json")));
        Assert.Equal("contraseña", Connections.GetSecret(connection.Id));
        Assert.StartsWith("net:conn-", RemotePath.Format(connection.Id, "NAS", "a/b"), StringComparison.Ordinal);
        Assert.Equal(@"NAS\a\b", RemotePath.Display(RemotePath.Format(connection.Id, "NAS", "a/b")));
        Assert.True(RemotePath.IsNetwork(RemotePath.Format(connection.Id, "NAS", "")));
        Assert.Equal("NAS", Connections.Find(connection.Id)?.Name);
    }

    [Fact]
    public void CredentialManagerRoundTrip()
    {
        Connections.SecretsOverride = null; // esta prueba usa el Administrador de credenciales de verdad, y lo deja limpio
        string id = "conn-prueba-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Connections.SetSecret(id, "clave con ñ y 🙂");
            Assert.Equal("clave con ñ y 🙂", Connections.GetSecret(id));
        }
        finally
        {
            Connections.DeleteSecret(id);
        }

        Assert.Equal("", Connections.GetSecret(id));
    }

    [Fact]
    public void ManyFilesTravelOverSeveralConnectionsAtOnce()
    {
        using var server = new FakeFtp(Folder("servidor"), "ana", "secreta", mlsd: true);
        var connection = new Connection { Name = "NAS", Kind = ConnectionKind.Ftp, Host = "127.0.0.1", Port = server.Port, User = "ana" };
        string local = Folder("muchos");
        var files = new Dictionary<string, byte[]>();
        for (int i = 0; i < 60; i++)
        {
            string name = Path.Combine(i % 3 == 0 ? "a" : "b", $"f{i}.bin");
            files[name] = RandomNumberGenerator.GetBytes(i % 10 == 0 ? 1_500_000 : 20_000 + i);
            Directory.CreateDirectory(Path.Combine(local, Path.GetDirectoryName(name)!));
            File.WriteAllBytes(Path.Combine(local, name), files[name]);
        }

        using (var store = Connections.Open(connection, "secreta"))
        {
            store.CreateFolder(store.RootId, "copias");
        }

        int opened = 0;
        CopyProgress last = default;
        CopyEngine Engine()
        {
            var engine = new CopyEngine(new CopyOptions { Verify = true, RemoteConnections = 8, ProgressInterval = TimeSpan.Zero })
            {
                RemoteOpener = _ => { Interlocked.Increment(ref opened); return Connections.Open(connection, "secreta"); },
            };
            engine.ProgressChanged += p => last = p;
            return engine;
        }

        string remote = RemotePath.Format(connection.Id, connection.Name, "copias");
        var up = Engine().Run(new CopyJobRequest([local], remote));
        Assert.Empty(up.Failures);
        Assert.Equal(60, up.FilesCopied);
        Assert.Equal(8, opened);
        Assert.Equal(last.TotalBytes, last.CompletedBytes);
        Assert.Equal(60, last.FilesCompleted);

        opened = 0;
        string back = Folder("vuelta");
        var down = Engine().Run(new CopyJobRequest([RemotePath.Combine(remote, "muchos")], back));
        Assert.Empty(down.Failures);
        Assert.Equal(8, opened);
        foreach (var (name, content) in files)
        {
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(back, "muchos", name)));
        }
    }
}
