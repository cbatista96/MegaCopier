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

    /// <SUMMARY>FILE_FLAG_NO_BUFFERING: E/S DIRECTA AL DISPOSITIVO, SIN PASAR POR LA CACHé DE WINDOWS.</SUMMARY>
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
            throw new IOException("No se pudieron vaciar los datos al disco.", Marshal.GetHRForLastWin32Error());
        }
    }

    public static SourceInfo GetSourceInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw new IOException("No se pudo leer la información del archivo de origen.", Marshal.GetHRForLastWin32Error());
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
