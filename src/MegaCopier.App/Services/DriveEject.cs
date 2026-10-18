using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.App.Services;

/// <summary>
/// Expulsa una unidad extraíble al terminar una copia (como «Desmontar» de SuperCopier): bloquea el volumen, lo
/// desmonta y pide expulsar el medio. Con «forzar», lo desmonta aunque algún programa lo tenga abierto.
/// </summary>
internal static partial class DriveEject
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018, FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageMediaRemoval = 0x002D4804, IoctlStorageEjectMedia = 0x002D4808, IoctlStorageQueryProperty = 0x002D1400;
    private const byte BusTypeUsb = 7, BusTypeSd = 0x0C, BusTypeMmc = 0x0D;

    /// <summary>Unidad extraíble: memoria USB o tarjeta, o un disco conectado por USB.</summary>
    public static bool IsRemovable(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Removable)
            {
                return true;
            }

            if (drive.DriveType != DriveType.Fixed)
            {
                return false;
            }

            using var handle = Open(root, access: 0);
            return !handle.IsInvalid && BusType(handle) is BusTypeUsb or BusTypeSd or BusTypeMmc;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool Eject(string root, bool force)
    {
        using var handle = Open(root, GenericRead | GenericWrite);
        if (handle.IsInvalid)
        {
            return false;
        }

        // Bloquear: falla si otro programa usa la unidad. Se reintenta un poco (el Explorador suele soltarla enseguida).
        bool locked = false;
        for (int i = 0; i < 10 && !locked; i++)
        {
            locked = DeviceIoControl(handle, FsctlLockVolume, 0, 0, 0, 0, out _, 0);
            if (!locked)
            {
                Thread.Sleep(300);
            }
        }

        if (!locked && !force)
        {
            return false;
        }

        if (!DeviceIoControl(handle, FsctlDismountVolume, 0, 0, 0, 0, out _, 0))
        {
            return false;
        }

        byte allow = 0; // PREVENT_MEDIA_REMOVAL.PreventMediaRemoval = FALSE
        unsafe
        {
            DeviceIoControl(handle, IoctlStorageMediaRemoval, (nint)(&allow), 1, 0, 0, out _, 0);
        }

        return DeviceIoControl(handle, IoctlStorageEjectMedia, 0, 0, 0, 0, out _, 0);
    }

    private static SafeFileHandle Open(string root, uint access)
    {
        string volume = @"\\.\" + root.TrimEnd('\\', '/');
        return CreateFileW(volume, access, ShareRead | ShareWrite, 0, OpenExisting, 0, 0);
    }

    /// <summary>Tipo de bus del disco (STORAGE_DEVICE_DESCRIPTOR.BusType).</summary>
    private static unsafe byte BusType(SafeFileHandle handle)
    {
        uint* query = stackalloc uint[3]; // PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0)
        query[0] = 0;
        query[1] = 0;
        query[2] = 0;
        byte* descriptor = stackalloc byte[1024];
        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, (nint)query, 12, (nint)descriptor, 1024, out _, 0))
        {
            return 0;
        }

        return descriptor[28]; // Version, Size, DeviceType, DeviceTypeModifier, RemovableMedia, CommandQueueing, 4 offsets → BusType
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}
