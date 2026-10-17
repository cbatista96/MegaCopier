namespace MegaCopier.Engine.Mtp;

/// <summary>
/// Una ubicación dentro de un móvil o una cámara conectados por USB (MTP), escrita como texto para que viaje por la
/// cola, el historial y la tubería como cualquier otra ruta:
///   mtp:&lt;id del dispositivo&gt;|&lt;nombre del dispositivo&gt;|&lt;almacenamiento/carpeta/archivo&gt;
/// El id es el de Windows (\\?\usb#vid_...); el nombre solo sirve para mostrarlo. La ruta interna va por nombres,
/// no por identificadores de objeto: así sigue valiendo al desconectar y volver a conectar (para reanudar).
/// </summary>
public static class MtpPath
{
    public const string Prefix = "mtp:";

    public static bool IsMtp(string? path) => path is not null && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Format(string deviceId, string deviceName, string innerPath) =>
        $"{Prefix}{deviceId}|{deviceName.Replace('|', '_')}|{Normalize(innerPath)}";

    public static bool TryParse(string path, out string deviceId, out string deviceName, out string innerPath)
    {
        deviceId = deviceName = innerPath = "";
        if (!IsMtp(path))
        {
            return false;
        }

        var parts = path[Prefix.Length..].Split('|', 3);
        if (parts.Length != 3 || parts[0].Length == 0)
        {
            return false;
        }

        (deviceId, deviceName, innerPath) = (parts[0], parts[1], Normalize(parts[2]));
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

    /// <summary>Para mostrar: "Pixel 8\Almacenamiento interno\DCIM".</summary>
    public static string Display(string path) =>
        TryParse(path, out _, out string device, out string inner)
            ? string.Join('\\', new[] { device }.Concat(Segments(inner)))
            : path;

    /// <summary>Clave del "dispositivo" para la cola: las copias con el mismo móvil van de una en una.</summary>
    public static string DeviceKey(string path) =>
        TryParse(path, out string id, out _, out _) ? "MTP:" + id.ToUpperInvariant() : path;

    private static string Normalize(string inner) => string.Join('/', inner.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Un archivo o carpeta del dispositivo. Los almacenamientos (memoria interna, tarjeta SD) cuentan como carpetas.</summary>
public sealed record MtpObject(string Id, string Name, bool IsFolder, long Size, DateTime? ModifiedUtc);

/// <summary>Un móvil o cámara conectado.</summary>
public sealed record MtpDeviceInfo(string Id, string Name);

/// <summary>
/// Lo que el motor necesita de un dispositivo MTP. La implementación real habla con Windows Portable Devices;
/// las pruebas usan una de mentira en memoria.
/// </summary>
public interface IMtpDevice : IDisposable
{
    /// <summary>Id del objeto raíz (en WPD, "DEVICE").</summary>
    string RootId { get; }

    IReadOnlyList<MtpObject> Children(string parentId);

    Stream OpenRead(string objectId);

    /// <summary>Crea un archivo con el contenido de <paramref name="data"/>; devuelve su id.</summary>
    string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken);

    string CreateFolder(string parentId, string name);

    /// <summary>Borra un archivo, o una carpeta con todo lo que tenga dentro.</summary>
    void Delete(string objectId, bool recursive);
}

public static class MtpDevices
{
    private static string? TestFolder => Environment.GetEnvironmentVariable("MEGACOPIER_TEST_MTP") is { Length: > 0 } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>Móviles y cámaras conectados (con MEGACOPIER_TEST_MTP, además uno de prueba que es una carpeta).</summary>
    public static IReadOnlyList<MtpDeviceInfo> List() =>
        TestFolder is null ? WpdDevice.List() : [new MtpDeviceInfo(FolderMtpDevice.TestId, "Teléfono de prueba"), .. WpdDevice.List()];

    public static IMtpDevice Open(string deviceId) =>
        deviceId == FolderMtpDevice.TestId && TestFolder is { } folder ? new FolderMtpDevice(folder) : WpdDevice.Open(deviceId);

    /// <summary>Busca un objeto por su ruta de nombres, sin distinguir mayúsculas. Null si no existe.</summary>
    public static MtpObject? Resolve(IMtpDevice device, string innerPath)
    {
        var current = new MtpObject(device.RootId, "", true, 0, null);
        foreach (string name in MtpPath.Segments(innerPath))
        {
            var next = device.Children(current.Id).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
