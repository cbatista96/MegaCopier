namespace MegaCopier.Engine.Remote;

/// <summary>
/// Una ubicación fuera del sistema de archivos, escrita como texto para que viaje por la cola, el historial y la
/// tubería como cualquier otra ruta:
///   mtp:&lt;id&gt;|&lt;nombre&gt;|&lt;ruta&gt;   un móvil o una cámara por USB (id de Windows: \\?\usb#vid_...)
///   net:&lt;id&gt;|&lt;nombre&gt;|&lt;ruta&gt;   un servidor guardado en Conexiones (FTP, WebDAV, S3, SFTP; id "conn-...")
/// El nombre solo sirve para mostrarlo. La ruta interna va por nombres, no por identificadores de objeto: así sigue
/// valiendo al desconectar y volver a conectar (para reanudar).
/// </summary>
public static class RemotePath
{
    public const string MtpPrefix = "mtp:";
    public const string NetPrefix = "net:";

    /// <summary>Los ids de las conexiones guardadas empiezan así (y sus rutas, por "net:").</summary>
    public const string ConnectionIdPrefix = "conn-";

    public static bool IsRemote(string? path) =>
        path is not null && (path.StartsWith(MtpPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(NetPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>¿Es un servidor (y no un móvil)?</summary>
    public static bool IsNetwork(string? path) => path is not null && path.StartsWith(NetPrefix, StringComparison.OrdinalIgnoreCase);

    public static string Format(string id, string name, string innerPath) =>
        $"{(id.StartsWith(ConnectionIdPrefix, StringComparison.Ordinal) ? NetPrefix : MtpPrefix)}{id}|{name.Replace('|', '_')}|{Normalize(innerPath)}";

    public static bool TryParse(string path, out string id, out string name, out string innerPath)
    {
        id = name = innerPath = "";
        if (!IsRemote(path))
        {
            return false;
        }

        var parts = path[MtpPrefix.Length..].Split('|', 3); // los dos prefijos miden lo mismo
        if (parts.Length != 3 || parts[0].Length == 0)
        {
            return false;
        }

        (id, name, innerPath) = (parts[0], parts[1], Normalize(parts[2]));
        return true;
    }

    /// <summary>Los nombres de la ruta interna (almacenamiento, carpetas, archivo).</summary>
    public static string[] Segments(string innerPath) => innerPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public static string Combine(string path, string name) =>
        TryParse(path, out string id, out string device, out string inner)
            ? Format(id, device, inner.Length == 0 ? name : inner + "/" + name)
            : Path.Combine(path, name);

    /// <summary>La carpeta que contiene esa ruta.</summary>
    public static string Parent(string path) =>
        TryParse(path, out string id, out string device, out string inner)
            ? Format(id, device, string.Join('/', Segments(inner).SkipLast(1)))
            : Path.GetDirectoryName(path) ?? path;

    /// <summary>Último nombre de la ruta (el del archivo o carpeta).</summary>
    public static string Name(string path) =>
        TryParse(path, out _, out string device, out string inner) ? Segments(inner).LastOrDefault() ?? device : Path.GetFileName(path);

    /// <summary>Para mostrar: "Pixel 8\Almacenamiento interno\DCIM" o "NAS de casa\fotos\2025".</summary>
    public static string Display(string path) =>
        TryParse(path, out _, out string device, out string inner)
            ? string.Join('\\', new[] { device }.Concat(Segments(inner)))
            : path;

    /// <summary>Clave del "dispositivo" para la cola: las copias con el mismo móvil o servidor van de una en una.</summary>
    public static string DeviceKey(string path) =>
        TryParse(path, out string id, out _, out _) ? (IsNetwork(path) ? "NET:" : "MTP:") + id.ToUpperInvariant() : path;

    private static string Normalize(string inner) => string.Join('/', inner.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Un archivo o carpeta remoto. Los almacenamientos de un móvil (memoria interna, tarjeta SD) cuentan como carpetas.</summary>
public sealed record RemoteItem(string Id, string Name, bool IsFolder, long Size, DateTime? ModifiedUtc);

/// <summary>Un móvil o cámara conectado.</summary>
public sealed record MtpDeviceInfo(string Id, string Name);

/// <summary>
/// Lo que el motor necesita de un sitio remoto (móvil, FTP, WebDAV, S3, SFTP): recorrer carpetas, leer, subir, crear
/// carpetas y borrar. Cada protocolo tiene su implementación; las pruebas usan una de mentira en memoria.
/// </summary>
public interface IRemoteStore : IDisposable
{
    /// <summary>Id de la carpeta raíz (en WPD, "DEVICE"; en un servidor, "" o "/").</summary>
    string RootId { get; }

    IReadOnlyList<RemoteItem> Children(string parentId);

    Stream OpenRead(string objectId);

    /// <summary>Crea (o sustituye) un archivo con el contenido de <paramref name="data"/>; devuelve su id.</summary>
    string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken);

    string CreateFolder(string parentId, string name);

    /// <summary>Borra un archivo, o una carpeta con todo lo que tenga dentro.</summary>
    void Delete(string objectId, bool recursive);
}

public static class MtpDevices
{
    internal static string? TestFolder => Environment.GetEnvironmentVariable("MEGACOPIER_TEST_MTP") is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>Móviles y cámaras conectados (con MEGACOPIER_TEST_MTP, además uno de prueba que es una carpeta).</summary>
    public static IReadOnlyList<MtpDeviceInfo> List() =>
        TestFolder is null ? WpdDevice.List() : [new MtpDeviceInfo(FolderMtpDevice.TestId, "Teléfono de prueba"), .. WpdDevice.List()];
}

public static class RemoteStores
{
    /// <summary>Abre un móvil (id de Windows) o una conexión guardada (id "conn-...").</summary>
    public static IRemoteStore Open(string id)
    {
        if (id.StartsWith(RemotePath.ConnectionIdPrefix, StringComparison.Ordinal))
        {
            return Connections.Open(id);
        }

        return id == FolderMtpDevice.TestId && MtpDevices.TestFolder is { } folder ? new FolderMtpDevice(folder) : WpdDevice.Open(id);
    }

    /// <summary>Busca un elemento por su ruta de nombres, sin distinguir mayúsculas. Null si no existe.</summary>
    public static RemoteItem? Resolve(IRemoteStore store, string innerPath)
    {
        var current = new RemoteItem(store.RootId, "", true, 0, null);
        foreach (string name in RemotePath.Segments(innerPath))
        {
            var next = store.Children(current.Id).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
