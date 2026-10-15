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

    private static uint? TryGetDiskNumber(string drive)
    {
        try
        {
            using SafeFileHandle volume = File.OpenHandle(@"\\.\" + drive, FileMode.Open, (FileAccess)0, FileShare.ReadWrite);
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
