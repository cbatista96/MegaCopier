using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Identifica el disco físico de una ruta. Dos copias en el mismo disco se estorban (el cabezal o la cola del SSD
/// se reparten), así que la cola las ejecuta de una en una; en discos distintos van en paralelo.
/// </summary>
internal static partial class VolumeInfo
{
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    public static string DeviceKey(string path)
    {
        if (Mtp.MtpPath.IsMtp(path))
        {
            return Mtp.MtpPath.DeviceKey(path);
        }

        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? full;

        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Recurso de red: cada recurso compartido cuenta como un "dispositivo".
            return root.TrimEnd('\\').ToUpperInvariant();
        }

        string drive = root.TrimEnd('\\');
        return TryGetDiskNumber(drive) is uint disk ? $"DISK{disk}" : drive.ToUpperInvariant();
    }

    /// <summary>Número del disco físico de una ruta local (el de "Administración de discos"), o null.</summary>
    public static uint? DiskNumber(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
        return root.StartsWith(@"\\", StringComparison.Ordinal) || root.Length == 0 ? null : TryGetDiskNumber(root.TrimEnd('\\'));
    }

    private static uint? TryGetDiskNumber(string drive)
    {
        try
        {
            // Sin derechos de lectura ni escritura (basta para preguntar y no exige ser administrador). File.OpenHandle no
            // acepta "ningún acceso" (lanzaba ArgumentException y nunca se sabía el disco), así que se abre con CreateFileW.
            using SafeFileHandle volume = CreateFileW(@"\\.\" + drive, 0, 3 /* FILE_SHARE_READ | FILE_SHARE_WRITE */, 0, 3 /* OPEN_EXISTING */, 0, 0);
            if (volume.IsInvalid)
            {
                return null;
            }

            Span<byte> buffer = stackalloc byte[64];
            unsafe
            {
                fixed (byte* pointer = buffer)
                {
                    if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents, null, 0, pointer, (uint)buffer.Length, out _, null))
                    {
                        return null;
                    }
                }
            }

            // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (4) + relleno (4) + DISK_EXTENT { DiskNumber (4), ... }
            return MemoryMarshal.Read<uint>(buffer) >= 1 ? MemoryMarshal.Read<uint>(buffer[8..]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        void* inBuffer,
        uint inBufferSize,
        void* outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        void* overlapped);
}
