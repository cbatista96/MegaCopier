namespace MegaCopier.Engine.Remote;

/// <summary>
/// Un "móvil" de prueba que en realidad es una carpeta del disco: con la variable MEGACOPIER_TEST_MTP=&lt;carpeta&gt;
/// aparece como dispositivo conectado, para probar la ventana y las copias sin un móvil de verdad.
/// Los ids de objeto son rutas relativas a esa carpeta ("" es la raíz).
/// </summary>
internal sealed class FolderMtpDevice(string root) : IRemoteStore
{
    public const string TestId = @"\\?\test#megacopier-mtp";

    public string RootId => "";

    private string Full(string id) => id.Length == 0 ? root : Path.Combine(root, id);

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        var folder = new DirectoryInfo(Full(parentId));
        return
        [
            .. folder.EnumerateDirectories().Select(d => new RemoteItem(Path.GetRelativePath(root, d.FullName), d.Name, true, 0, null)),
            .. folder.EnumerateFiles().Select(f => new RemoteItem(Path.GetRelativePath(root, f.FullName), f.Name, false, f.Length, f.LastWriteTimeUtc)),
        ];
    }

    public Stream OpenRead(string objectId) => new FileStream(Full(objectId), FileMode.Open, FileAccess.Read, FileShare.Read);

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string id = Path.Combine(parentId, name);
        using (var output = new FileStream(Full(id), FileMode.CreateNew, FileAccess.Write))
        {
            data.CopyTo(output);
        }

        return id;
    }

    public string CreateFolder(string parentId, string name)
    {
        string id = Path.Combine(parentId, name);
        Directory.CreateDirectory(Full(id));
        return id;
    }

    public void Delete(string objectId, bool recursive)
    {
        if (Directory.Exists(Full(objectId)))
        {
            Directory.Delete(Full(objectId), recursive);
        }
        else
        {
            File.Delete(Full(objectId));
        }
    }

    public void Dispose()
    {
    }
}
