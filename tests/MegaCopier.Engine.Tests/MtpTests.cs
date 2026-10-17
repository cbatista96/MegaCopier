using System.Text;
using MegaCopier.Engine.Mtp;

namespace MegaCopier.Engine.Tests;

/// <summary>Un móvil de mentira en memoria: carpetas, archivos con fecha y un registro de lo que se borra.</summary>
internal sealed class FakePhone : IMtpDevice
{
    private int _next = 1;

    public FakePhone()
    {
        Add("DEVICE", "Almacenamiento interno", null, null);
    }

    public Dictionary<string, (string Parent, string Name, byte[]? Data, DateTime? Modified)> Objects { get; } = new()
    {
        ["DEVICE"] = ("", "", null, null),
    };

    public int Opened { get; set; }

    public bool Disposed { get; private set; }

    /// <summary>Si no es null, al leer ese archivo se devuelven estos bytes (un cable que falla).</summary>
    public Func<string, byte[], byte[]>? Corrupt { get; set; }

    public string RootId => "DEVICE";

    public string Add(string parent, string name, byte[]? data, DateTime? modified)
    {
        string id = "o" + _next++;
        Objects[id] = (parent, name, data, modified);
        return id;
    }

    public string Folder(string path)
    {
        string current = "DEVICE";
        foreach (string name in path.Split('/'))
        {
            current = Objects.FirstOrDefault(o => o.Value.Parent == current && o.Value.Name == name && o.Value.Data is null).Key
                ?? Add(current, name, null, null);
        }

        return current;
    }

    public string File(string path, string text, DateTime? modified = null)
    {
        string parent = Folder(path[..path.LastIndexOf('/')]);
        return Add(parent, path[(path.LastIndexOf('/') + 1)..], Encoding.UTF8.GetBytes(text), modified ?? new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    public string? Text(string path)
    {
        string current = "DEVICE";
        foreach (string name in path.Split('/'))
        {
            current = Objects.FirstOrDefault(o => o.Value.Parent == current && o.Value.Name == name).Key;
            if (current is null)
            {
                return null;
            }
        }

        return Objects[current].Data is { } data ? Encoding.UTF8.GetString(data) : null;
    }

    public IReadOnlyList<MtpObject> Children(string parentId) =>
        [.. Objects.Where(o => o.Value.Parent == parentId).Select(o => new MtpObject(o.Key, o.Value.Name, o.Value.Data is null, o.Value.Data?.Length ?? 0, o.Value.Modified))];

    public Stream OpenRead(string objectId)
    {
        byte[] data = Objects[objectId].Data!;
        return new MemoryStream(Corrupt?.Invoke(objectId, data) ?? data, writable: false);
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        data.CopyTo(buffer);
        Assert.Equal(length, buffer.Length);
        return Add(parentId, name, buffer.ToArray(), DateTime.UtcNow);
    }

    public string CreateFolder(string parentId, string name) => Add(parentId, name, null, null);

    public void Delete(string objectId, bool recursive)
    {
        foreach (var child in Objects.Where(o => o.Value.Parent == objectId).Select(o => o.Key).ToList())
        {
            Assert.True(recursive, "borrar una carpeta con contenido sin recursión");
            Delete(child, recursive);
        }

        Objects.Remove(objectId);
    }

    public void Dispose() => Disposed = true;
}

public sealed class MtpTests : IDisposable
{
    private const string Id = @"\\?\usb#vid_18d1&pid_4ee1#abc#{6ac27878-a6fa-4155-ba85-f98f491d4f33}";
    private readonly TempDir _temp = new();
    private readonly FakePhone _phone = new();

    public void Dispose() => _temp.Dispose();

    private static string Phone(string inner) => MtpPath.Format(Id, "Pixel 8", inner);

    private CopyEngine Engine(CopyOptions? options = null)
    {
        options ??= new CopyOptions();
        options.ProgressInterval = TimeSpan.Zero;
        return new CopyEngine(options) { MtpOpener = id => { Assert.Equal(Id, id); _phone.Opened++; return _phone; } };
    }

    [Fact]
    public void PathsRoundTripAndDisplayNicely()
    {
        string path = Phone(@"Almacenamiento interno\DCIM/Camera/");
        Assert.True(MtpPath.TryParse(path, out string id, out string name, out string inner));
        Assert.Equal((Id, "Pixel 8", "Almacenamiento interno/DCIM/Camera"), (id, name, inner));
        Assert.Equal(@"Pixel 8\Almacenamiento interno\DCIM\Camera", MtpPath.Display(path));
        Assert.Equal("Camera", MtpPath.Name(path));
        Assert.Equal(Phone("Almacenamiento interno/DCIM"), MtpPath.Parent(path));
        Assert.Equal(Phone("Almacenamiento interno/DCIM/Camera/a.jpg"), MtpPath.Combine(path, "a.jpg"));
        Assert.Equal(MtpPath.DeviceKey(Phone("x")), Native.VolumeInfo.DeviceKey(Phone("y/z")));
        Assert.False(MtpPath.IsMtp(@"C:\fotos"));
    }

    [Fact]
    public void ImportsAFolderWithItsDatesAndVerifies()
    {
        _phone.File("Almacenamiento interno/DCIM/Camera/IMG_1.jpg", "foto uno", new DateTime(2025, 5, 4, 3, 2, 1, DateTimeKind.Utc));
        _phone.File("Almacenamiento interno/DCIM/Camera/sub/IMG_2.jpg", "foto dos");
        _phone.File("Almacenamiento interno/DCIM/Camera/raro:nombre?.txt", "nombre de Android");

        var result = Engine(new CopyOptions { Verify = true }).Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM/Camera")], _temp.Path));

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.FilesCopied);
        Assert.Equal("foto uno", File.ReadAllText(_temp.Combine("Camera", "IMG_1.jpg")));
        Assert.Equal("foto dos", File.ReadAllText(_temp.Combine("Camera", "sub", "IMG_2.jpg")));
        Assert.Equal("nombre de Android", File.ReadAllText(_temp.Combine("Camera", "raro_nombre_.txt")));
        Assert.Equal(new DateTime(2025, 5, 4, 3, 2, 1, DateTimeKind.Utc), File.GetLastWriteTimeUtc(_temp.Combine("Camera", "IMG_1.jpg")));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.part", SearchOption.AllDirectories));
        Assert.True(_phone.Disposed);
        Assert.NotNull(_phone.Text("Almacenamiento interno/DCIM/Camera/IMG_1.jpg")); // copiar no borra nada
    }

    [Fact]
    public void ImportingAgainSkipsWhatAlreadyArrived()
    {
        _phone.File("Almacenamiento interno/DCIM/a.jpg", "aaa");
        _phone.File("Almacenamiento interno/DCIM/b.jpg", "bbb");
        Engine().Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM")], _temp.Path));
        _phone.File("Almacenamiento interno/DCIM/c.jpg", "ccc");

        var again = Engine().Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM")], _temp.Path));

        Assert.Equal(1, again.FilesCopied);
        Assert.Equal(2, again.FilesSkipped);
        Assert.Equal("ccc", File.ReadAllText(_temp.Combine("DCIM", "c.jpg")));
    }

    [Fact]
    public void MovingFromThePhoneDeletesOnlyWhatWasCopied()
    {
        _phone.File("Almacenamiento interno/DCIM/a.jpg", "aaa");
        string bad = _phone.File("Almacenamiento interno/DCIM/b.jpg", "bbb");
        bool failed = false;
        _phone.Corrupt = (id, data) => id == bad && !failed && (failed = true) ? Encoding.UTF8.GetBytes("bbX") : data; // el cable falla una vez con b.jpg

        var result = Engine(new CopyOptions { Move = true, Verify = true, VerifySource = true }).Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM")], _temp.Path));

        Assert.Single(result.Failures);
        Assert.Null(_phone.Text("Almacenamiento interno/DCIM/a.jpg"));         // movido
        Assert.Equal("bbb", _phone.Text("Almacenamiento interno/DCIM/b.jpg"));  // sigue en el móvil
        Assert.NotNull(_phone.Objects.Values.FirstOrDefault(o => o.Name == "DCIM").Name); // la carpeta se queda: no todo salió bien
        Assert.False(File.Exists(_temp.Combine("DCIM", "b.jpg")));

        _phone.Corrupt = null;
        var retry = Engine(new CopyOptions { Move = true, Verify = true, VerifySource = true }).Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM")], _temp.Path) { Resumed = true });
        Assert.Empty(retry.Failures);
        Assert.Null(_phone.Text("Almacenamiento interno/DCIM"));
        Assert.DoesNotContain(_phone.Objects.Values, o => o.Name == "DCIM"); // ahora sí: carpeta movida entera
        Assert.Equal("bbb", File.ReadAllText(_temp.Combine("DCIM", "b.jpg")));
    }

    [Fact]
    public void ImportConflictsFollowThePolicy()
    {
        _phone.File("Almacenamiento interno/x.txt", "nuevo del móvil");
        File.WriteAllText(_temp.Combine("x.txt"), "otro");

        var keep = Engine(new CopyOptions { Conflict = ConflictPolicy.KeepBoth }).Run(new CopyJobRequest([Phone("Almacenamiento interno/x.txt")], _temp.Path));
        Assert.Equal(1, keep.FilesCopied);
        Assert.Equal("otro", File.ReadAllText(_temp.Combine("x.txt")));
        Assert.Single(Directory.GetFiles(_temp.Path, "x*.txt"), f => File.ReadAllText(f) == "nuevo del móvil");

        var skip = Engine(new CopyOptions { Conflict = ConflictPolicy.Skip }).Run(new CopyJobRequest([Phone("Almacenamiento interno/x.txt")], _temp.Path));
        Assert.Equal(1, skip.FilesSkipped);

        var replace = Engine(new CopyOptions { Conflict = ConflictPolicy.Overwrite }).Run(new CopyJobRequest([Phone("Almacenamiento interno/x.txt")], _temp.Path));
        Assert.Equal(1, replace.FilesCopied);
        Assert.Equal("nuevo del móvil", File.ReadAllText(_temp.Combine("x.txt")));
    }

    [Fact]
    public void FiltersApplyToPhoneFiles()
    {
        _phone.File("Almacenamiento interno/DCIM/a.jpg", "aaa");
        _phone.File("Almacenamiento interno/DCIM/b.mp4", "video");
        var options = new CopyOptions { Filter = new FileFilter { Include = ["*.jpg"] } };

        var result = Engine(options).Run(new CopyJobRequest([Phone("Almacenamiento interno/DCIM")], _temp.Path));

        Assert.Equal(1, result.FilesCopied);
        Assert.False(File.Exists(_temp.Combine("DCIM", "b.mp4")));
    }

    [Fact]
    public void MissingPhoneFolderIsAPlanError()
    {
        _phone.Folder("Almacenamiento interno");
        Assert.Throws<CopyPlanException>(() => Engine().Run(new CopyJobRequest([Phone("Almacenamiento interno/No existe")], _temp.Path)));
    }

    [Fact]
    public void ExportsFoldersToThePhoneAndVerifiesByReadingBack()
    {
        Directory.CreateDirectory(_temp.Combine("musica", "disco"));
        File.WriteAllText(_temp.Combine("musica", "disco", "1.mp3"), "canción");
        File.WriteAllText(_temp.Combine("musica", "portada.jpg"), "portada");
        Directory.CreateDirectory(_temp.Combine("musica", "vacía"));
        _phone.Folder("Almacenamiento interno/Music");

        var result = Engine(new CopyOptions { Verify = true }).Run(new CopyJobRequest([_temp.Combine("musica")], Phone("Almacenamiento interno/Music")));

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.FilesCopied);
        Assert.Equal("canción", _phone.Text("Almacenamiento interno/Music/musica/disco/1.mp3"));
        Assert.Equal("portada", _phone.Text("Almacenamiento interno/Music/musica/portada.jpg"));
        Assert.Contains(_phone.Objects.Values, o => o.Name == "vacía" && o.Data is null);
    }

    [Fact]
    public void ExportSkipsSameSizeFilesAndMovesDeleteTheOriginals()
    {
        _phone.Folder("Almacenamiento interno/Docs");
        File.WriteAllText(_temp.Combine("a.txt"), "aaa");
        File.WriteAllText(_temp.Combine("b.txt"), "bbb");
        Engine().Run(new CopyJobRequest([_temp.Combine("a.txt")], Phone("Almacenamiento interno/Docs")));

        var move = Engine(new CopyOptions { Move = true }).Run(new CopyJobRequest([_temp.Combine("a.txt"), _temp.Combine("b.txt")], Phone("Almacenamiento interno/Docs")));

        Assert.Equal(1, move.FilesCopied);
        Assert.Equal(1, move.FilesSkipped);
        Assert.False(File.Exists(_temp.Combine("a.txt")));
        Assert.False(File.Exists(_temp.Combine("b.txt")));
        Assert.Equal("bbb", _phone.Text("Almacenamiento interno/Docs/b.txt"));
        Assert.Single(_phone.Objects.Values, o => o.Name == "a.txt");
    }

    [Fact]
    public void ExportKeepBothGivesTheNewFileAnotherName()
    {
        _phone.File("Almacenamiento interno/Docs/a.txt", "en el móvil");
        File.WriteAllText(_temp.Combine("a.txt"), "del PC, más largo");

        var result = Engine(new CopyOptions { Conflict = ConflictPolicy.KeepBoth }).Run(new CopyJobRequest([_temp.Combine("a.txt")], Phone("Almacenamiento interno/Docs")));

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal("en el móvil", _phone.Text("Almacenamiento interno/Docs/a.txt"));
        Assert.Contains(_phone.Objects.Values, o => o.Name != "a.txt" && o.Name.StartsWith("a", StringComparison.Ordinal) && o.Data is { } d && Encoding.UTF8.GetString(d) == "del PC, más largo");
    }

    [Fact]
    public void ListingDevicesWithoutAnyConnectedDoesNotThrow()
    {
        var devices = WpdDevice.List();
        Assert.All(devices, d => Assert.False(string.IsNullOrEmpty(d.Id)));
    }
}
