using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>Enlaces simbólicos y uniones de carpetas.</summary>
internal static partial class Links
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;

    /// <summary>
    /// Recrea un enlace. Primero como enlace simbólico (necesita el modo de desarrollador o permisos de administrador);
    /// si no se puede y es una carpeta, como unión, que cualquier usuario puede crear.
    /// </summary>
    /// <returns>Qué se creó: "enlace" o "unión".</returns>
    public static string Create(string link, string target, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }

            return "enlace";
        }
        catch (IOException) when (isDirectory)
        {
            // Las uniones necesitan una ruta absoluta.
            string absolute = Path.IsPathFullyQualified(target) ? target : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, target));
            CreateJunction(link, absolute);
            return "unión";
        }
        catch (UnauthorizedAccessException) when (isDirectory)
        {
            string absolute = Path.IsPathFullyQualified(target) ? target : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, target));
            CreateJunction(link, absolute);
            return "unión";
        }
    }

    /// <summary>Crea una unión (punto de montaje) con FSCTL_SET_REPARSE_POINT.</summary>
    public static void CreateJunction(string link, string target)
    {
        // El núcleo no acepta "/" ni rutas relativas dentro de una unión.
        link = Path.GetFullPath(link);
        target = Path.GetFullPath(target);
        Directory.CreateDirectory(link);
        string substitute = @"\??\" + Path.TrimEndingDirectorySeparator(target);
        string print = Path.TrimEndingDirectorySeparator(target);
        byte[] substituteBytes = Encoding.Unicode.GetBytes(substitute);
        byte[] printBytes = Encoding.Unicode.GetBytes(print);

        // REPARSE_DATA_BUFFER (MountPointReparseBuffer): cabecera de 8 bytes + 4 ushort + nombres con su terminador.
        int pathBufferLength = substituteBytes.Length + 2 + printBytes.Length + 2;
        int dataLength = 8 + pathBufferLength;
        byte[] buffer = new byte[8 + dataLength];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), IoReparseTagMountPoint);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), (ushort)dataLength);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), (ushort)0);                                   // SubstituteNameOffset
        BitConverter.TryWriteBytes(buffer.AsSpan(10), (ushort)substituteBytes.Length);              // SubstituteNameLength
        BitConverter.TryWriteBytes(buffer.AsSpan(12), (ushort)(substituteBytes.Length + 2));        // PrintNameOffset
        BitConverter.TryWriteBytes(buffer.AsSpan(14), (ushort)printBytes.Length);                   // PrintNameLength
        substituteBytes.CopyTo(buffer, 16);
        printBytes.CopyTo(buffer, 16 + substituteBytes.Length + 2);

        using var handle = CreateFileW(Win32.LongPath(link), GenericWrite, 0, 0, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            throw new IOException($"No se pudo abrir {link} para crear la unión.", Marshal.GetHRForLastWin32Error());
        }

        if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, 0, 0, out _, 0))
        {
            int error = Marshal.GetHRForLastWin32Error();
            handle.Dispose();
            Directory.Delete(link);
            throw new IOException($"No se pudo crear la unión {link} → {target}.", error);
        }
    }

    /// <summary>Cuántos nombres tiene el archivo y su identidad (volumen + índice): iguales = mismo archivo.</summary>
    public static (uint Count, uint Volume, ulong Index)? GetLinkInfo(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(handle, out var info))
            {
                return null;
            }

            return (info.NumberOfLinks, info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void CreateHardLink(string link, string existing)
    {
        if (!CreateHardLinkW(Win32.LongPath(link), Win32.LongPath(existing), 0))
        {
            throw new IOException($"No se pudo crear el enlace duro {link}.", Marshal.GetHRForLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInfo
    {
        public uint FileAttributes;
        // FILETIME son dos DWORD: con long la estructura se alinearía a 8 y los campos quedarían desplazados.
        public uint CreationLow;
        public uint CreationHigh;
        public uint AccessLow;
        public uint AccessHigh;
        public uint WriteLow;
        public uint WriteHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string link, string existing, nint security);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, nint output, int outputSize, out int returned, nint overlapped);
}
