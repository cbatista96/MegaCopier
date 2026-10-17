using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Modo copia de seguridad (como robocopy /B): con los privilegios de copia y restauración de Windows (los tienen los
/// administradores, pero hay que activarlos), abrir con FILE_FLAG_BACKUP_SEMANTICS permite leer y escribir cualquier
/// archivo o carpeta sin importar sus permisos. Solo funciona en un proceso con permisos de administrador.
/// </summary>
public static unsafe partial class BackupPrivileges
{
    private const uint FileFlagBackupSemantics = 0x02000000;

    /// <summary>Activa SeBackupPrivilege y SeRestorePrivilege (y SeSecurityPrivilege, para las auditorías). True si los dos primeros quedan activos.</summary>
    public static bool Enable() =>
        EnablePrivilege("SeBackupPrivilege") & EnablePrivilege("SeRestorePrivilege") & (EnablePrivilege("SeSecurityPrivilege") || true);

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY */, out nint token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValueW(null, name, out long luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            // Devuelve "correcto" aunque no se tenga el privilegio: entonces el último error es ERROR_NOT_ALL_ASSIGNED (1300).
            return AdjustTokenPrivileges(token, false, &privileges, 0, null, null) && Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>Abre un archivo saltándose sus permisos (hace falta haber llamado a <see cref="Enable"/>).</summary>
    public static SafeFileHandle Open(string path, FileMode mode, FileAccess access, FileShare share, FileOptions options)
    {
        uint desired = (access.HasFlag(FileAccess.Read) ? 0x80000000u : 0) | (access.HasFlag(FileAccess.Write) ? 0x40000000u : 0);
        uint disposition = mode switch
        {
            FileMode.CreateNew => 1,
            FileMode.Create => 2,
            FileMode.Open => 3,
            FileMode.OpenOrCreate => 4,
            FileMode.Truncate => 5,
            _ => 4,
        };
        uint flags = (uint)options | FileFlagBackupSemantics;
        string full = Path.GetFullPath(path);
        string longPath = full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full
            : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
        var handle = CreateFileW(longPath, desired, (uint)share, 0, disposition, flags, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw error == 5 ? new UnauthorizedAccessException(path) : new IOException($"{path}: {new System.ComponentModel.Win32Exception(error).Message}", unchecked((int)0x80070000) | error);
        }

        return handle;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TokenPrivileges* newState, uint length, void* previous, uint* returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
