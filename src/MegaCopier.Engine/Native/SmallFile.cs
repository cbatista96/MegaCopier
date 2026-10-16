using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Escritura de un archivo pequeño con una sola apertura: se crea el .mcpart, se escribe directo al disco
/// (write-through), y con el mismo identificador se ponen fechas y atributos y se renombra al nombre final. Cada
/// apertura cuesta (y el antivirus revisa cada una): con miles de archivos pequeños, esto es lo que más se nota.
/// </summary>
internal static unsafe partial class SmallFile
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint Delete = 0x00010000;
    private const uint CreateAlways = 2;
    private const uint FileFlagWriteThrough = 0x80000000;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const int FileBasicInfo = 0;
    private const int FileRenameInfoEx = 22;
    private const uint RenameReplaceIfExists = 0x1;
    private const uint RenamePosixSemantics = 0x2;
    private const uint RenameIgnoreReadOnly = 0x40;

    /// <summary>Crea (o vacía) el archivo con permiso para renombrarlo después desde el mismo identificador.</summary>
    public static SafeFileHandle Create(string path, bool writeThrough)
    {
        var handle = CreateFileW(Win32.LongPath(path), GenericRead | GenericWrite | Delete, (uint)(FileShare.ReadWrite | FileShare.Delete), null,
            CreateAlways, FileFlagSequentialScan | (writeThrough ? FileFlagWriteThrough : 0), 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Win32Error(error, path);
        }

        return handle;
    }

    /// <summary>Fechas y atributos de una vez (null = no tocarlos).</summary>
    public static void SetBasicInfo(SafeFileHandle file, DateTime? creationUtc, DateTime? lastWriteUtc, FileAttributes? attributes)
    {
        var info = new BasicInfo
        {
            CreationTime = creationUtc?.ToFileTimeUtc() ?? 0,
            LastWriteTime = lastWriteUtc?.ToFileTimeUtc() ?? 0,
            FileAttributes = attributes is { } a ? (uint)(a == 0 ? FileAttributes.Normal : a) : 0,
        };
        if (!SetFileInformationByHandle(file, FileBasicInfo, &info, (uint)sizeof(BasicInfo)))
        {
            throw Win32Error(Marshal.GetLastPInvokeError(), null);
        }
    }

    /// <summary>Renombra el archivo abierto (reemplazando el destino aunque sea de solo lectura). Atómico.</summary>
    public static void Rename(SafeFileHandle file, string newPath)
    {
        string full = Win32.LongPath(Path.GetFullPath(newPath));
        int size = 24 + (full.Length + 1) * sizeof(char);
        byte* buffer = stackalloc byte[size];
        new Span<byte>(buffer, size).Clear();
        *(uint*)buffer = RenameReplaceIfExists | RenamePosixSemantics | RenameIgnoreReadOnly;
        *(uint*)(buffer + 16) = (uint)(full.Length * sizeof(char));
        full.AsSpan().CopyTo(new Span<char>(buffer + 20, full.Length));
        if (!SetFileInformationByHandle(file, FileRenameInfoEx, buffer, (uint)size))
        {
            throw Win32Error(Marshal.GetLastPInvokeError(), newPath);
        }
    }

    private static IOException Win32Error(int error, string? path) =>
        new($"{Marshal.GetPInvokeErrorMessage(error)}{(path is null ? "" : $" ({path})")}", unchecked((int)0x80070000 | error));

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, void* security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, void* info, uint size);
}
