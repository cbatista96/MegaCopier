using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>Identidad de un archivo de origen: si cambia, una copia parcial ya no es válida.</summary>
internal readonly record struct SourceIdentity(long Length, long LastWriteFileTime, uint VolumeSerial, ulong FileIndex);

internal readonly record struct SourceInfo(
    SourceIdentity Identity,
    FileAttributes Attributes,
    DateTime CreationUtc,
    DateTime LastWriteUtc);

internal static partial class Win32
{
    /// <summary>
    /// Ruta con el prefijo \\?\ para las llamadas nativas que no lo añaden solas (las de .NET sí): sin él, más de 260
    /// caracteres fallan.
    /// </summary>
    public static string LongPath(string path)
    {
        if (path.Length < 248 || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        string full = Path.GetFullPath(path);
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    /// <summary>Fecha de creación y de modificación con una sola apertura del archivo (en vez de una por fecha).</summary>
    public static unsafe void SetTimes(string path, DateTime creationUtc, DateTime lastWriteUtc)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        long creation = creationUtc.ToFileTimeUtc(), lastWrite = lastWriteUtc.ToFileTimeUtc();
        if (!SetFileTime(handle, &creation, null, &lastWrite))
        {
            throw new IOException(Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError()), Marshal.GetHRForLastWin32Error());
        }
    }

    /// <summary>Marca el archivo como disperso (FSCTL_SET_SPARSE). False si el sistema de archivos no lo admite.</summary>
    public static unsafe bool SetSparse(SafeFileHandle file) =>
        DeviceIoControl(file, 0x000900C4, null, 0, null, 0, out _, null);

    /// <summary>Lo que ocupa de verdad un archivo en el disco (menos que su tamaño si es disperso o comprimido).</summary>
    public static unsafe long AllocatedSize(string path)
    {
        uint high;
        uint low = GetCompressedFileSizeW(LongPath(path), &high);
        return low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0 ? -1 : ((long)high << 32) | low;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetCompressedFileSizeW(string path, uint* high);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, void* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileTime(SafeFileHandle file, long* creation, long* access, long* lastWrite);

    /// <summary>FILE_FLAG_NO_BUFFERING: E/S directa al dispositivo, sin pasar por la caché de Windows.</summary>
    public const FileOptions NoBuffering = (FileOptions)0x20000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(SafeFileHandle hFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation info);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    /// <summary>
    /// Obliga a que los datos escritos lleguen al medio físico (vacía la caché de Windows y la del disco).
    /// Es la base de toda la recuperación tras un apagón: nada se anota en el journal antes de esto.
    /// </summary>
    public static void Flush(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
        {
            throw new IOException(Texts.Get("E_FlushFailed"), Marshal.GetHRForLastWin32Error());
        }
    }

    public static SourceInfo GetSourceInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw new IOException(Texts.Get("E_SourceInfoFailed"), Marshal.GetHRForLastWin32Error());
        }

        long length = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        long creation = ((long)info.CreationTimeHigh << 32) | info.CreationTimeLow;
        long lastWrite = ((long)info.LastWriteTimeHigh << 32) | info.LastWriteTimeLow;
        ulong index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;

        return new SourceInfo(
            new SourceIdentity(length, lastWrite, info.VolumeSerialNumber, index),
            (FileAttributes)info.FileAttributes,
            DateTime.FromFileTimeUtc(creation),
            DateTime.FromFileTimeUtc(lastWrite));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumePathName([MarshalAs(UnmanagedType.LPWStr)] string fileName, char* volumePath, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumeInformation(string rootPath, char* name, uint nameLength, uint* serial, uint* maxComponentLength, uint* flags, char* fileSystem, uint fileSystemLength);

    /// <summary>
    /// Lo más largo que puede ser un nombre (de archivo o carpeta) en el volumen que contiene la ruta: 255 en NTFS, exFAT
    /// y FAT32; algunos NAS anuncian menos. Null si no se puede saber.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, nint overlapped);

    /// <summary>Comprime un archivo con la compresión de NTFS (como «Comprimir contenido» en sus propiedades).</summary>
    public static unsafe void Compress(string path)
    {
        const uint FsctlSetCompression = 0x9C040;
        using var handle = File.OpenHandle(LongPath(path), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        ushort format = 1; // COMPRESSION_FORMAT_DEFAULT
        uint returned;
        if (!DeviceIoControl(handle, FsctlSetCompression, &format, sizeof(ushort), null, 0, &returned, 0))
        {
            throw new IOException($"FSCTL_SET_COMPRESSION: {Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>Número de serie del volumen de esa raíz ("F:\"), o null si no hay disco.</summary>
    public static unsafe uint? VolumeSerial(string root)
    {
        uint serial, length, flags;
        return GetVolumeInformation(root, null, 0, &serial, &length, &flags, null, 0) ? serial : null;
    }

    /// <summary>Nombre del volumen de esa raíz ("Copias"), o null si no hay disco.</summary>
    public static unsafe string? VolumeLabel(string root)
    {
        char* name = stackalloc char[261];
        uint serial, length, flags;
        return GetVolumeInformation(root, name, 261, &serial, &length, &flags, null, 0) ? new string(name) : null;
    }

    public static unsafe int? MaxNameLength(string path)
    {
        if (VolumeRoot(path) is not { } root)
        {
            return null;
        }

        uint serial, length, flags;
        return GetVolumeInformation(root, null, 0, &serial, &length, &flags, null, 0) && length > 0 ? (int)length : null;
    }

    /// <summary>Raíz del volumen (unidad, punto de montaje o recurso de red) que contiene la ruta, o null.</summary>
    public static unsafe string? VolumeRoot(string path)
    {
        string? existing = path;
        while (existing is not null && !File.Exists(existing) && !Directory.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing);
        }

        if (existing is null)
        {
            return null;
        }

        char* buffer = stackalloc char[1024];
        return GetVolumePathName(LongPath(existing), buffer, 1024) ? new string(buffer) : null;
    }

    /// <summary>Espacio libre para el usuario en el volumen que contiene <paramref name="path"/> (o su ancestro existente).</summary>
    public static long? GetFreeBytes(string path)
    {
        string? directory = path;
        while (directory is not null && !Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory is null)
        {
            return null;
        }

        return GetDiskFreeSpaceEx(LongPath(directory), out ulong available, out _, out _) ? (long)available : null;
    }
}
