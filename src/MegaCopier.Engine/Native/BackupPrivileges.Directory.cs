using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

public static unsafe partial class BackupPrivileges
{
    /// <summary>Recorre una carpeta protegida usando el identificador abierto con privilegios de copia.</summary>
    public static IReadOnlyList<FileSystemInfo> EnumerateDirectory(string path)
    {
        using var handle = Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.None);
        const int capacity = 64 * 1024;
        byte* buffer = stackalloc byte[capacity];
        var entries = new List<FileSystemInfo>();
        bool first = true;
        while (true)
        {
            // FILE_ID_BOTH_DIR_INFO: RestartInfo inicia el recorrido; DirectoryInfo lo continúa.
            if (!GetFileInformationByHandleEx(handle, first ? 11 : 10, buffer, capacity))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 18 /* ERROR_NO_MORE_FILES */) return entries;
                if (error == 5) throw new UnauthorizedAccessException(path);
                throw new IOException(Marshal.GetPInvokeErrorMessage(error), unchecked((int)0x80070000) | error);
            }
            first = false;
            int offset = 0;
            while (true)
            {
                if (offset > capacity - 104) throw new IOException("Invalid directory entry.");
                byte* entry = buffer + offset;
                uint next = *(uint*)entry;
                uint attributes = *(uint*)(entry + 56);
                uint nameBytes = *(uint*)(entry + 60);
                if ((nameBytes & 1) != 0 || nameBytes > capacity - offset - 104) throw new IOException("Invalid directory name.");
                string name = new((char*)(entry + 104), 0, (int)nameBytes / 2);
                if (name is not ("." or ".."))
                {
                    string child = Path.Combine(path, name);
                    entries.Add((attributes & (uint)FileAttributes.Directory) != 0 ? new DirectoryInfo(child) : new FileInfo(child));
                }
                if (next == 0) break;
                if (next < 104 || next > capacity - offset - 104) throw new IOException("Invalid directory offset.");
                offset += (int)next;
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, void* buffer, uint size);
}
