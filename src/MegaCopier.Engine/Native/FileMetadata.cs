using System.Runtime.InteropServices;

namespace MegaCopier.Engine.Native;

/// <summary>Qué partes de la seguridad NTFS se copian.</summary>
[Flags]
public enum SecurityParts
{
    None = 0,

    /// <summary>Permisos (DACL).</summary>
    Acl = 1,

    /// <summary>Propietario (necesita privilegios si no eres tú).</summary>
    Owner = 2,

    /// <summary>Auditoría (SACL, necesita privilegios de administrador).</summary>
    Audit = 4,
}

/// <summary>Streams alternativos y seguridad NTFS: lo que el Explorador copia y una copia "solo de datos" pierde.</summary>
internal static unsafe partial class FileMetadata
{
    /// <summary>
    /// Copia los streams alternativos (por ejemplo Zone.Identifier, la marca de "descargado de Internet").
    /// Devuelve cuántos copió.
    /// </summary>
    public static int CopyAlternateStreams(string source, string destination)
    {
        int copied = 0;
        var data = new Win32FindStreamData();
        nint find = FindFirstStreamW(Win32.LongPath(source), 0, &data, 0);
        if (find == -1)
        {
            return 0; // sin streams (o el volumen no los admite)
        }

        try
        {
            do
            {
                string name = new(data.cStreamName);
                // "::$DATA" es el contenido principal; los demás son ":nombre:$DATA".
                if (name.StartsWith("::", StringComparison.Ordinal) || !name.EndsWith(":$DATA", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string stream = name[..^":$DATA".Length];
                using var input = new FileStream(source + stream, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var output = new FileStream(destination + stream, FileMode.Create, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
                copied++;
            }
            while (FindNextStreamW(find, &data));
        }
        finally
        {
            FindClose(find);
        }

        return copied;
    }

    /// <summary>Copia la seguridad elegida. Lanza <see cref="UnauthorizedAccessException"/> si faltan privilegios.</summary>
    public static void CopySecurity(string source, string destination, SecurityParts parts)
    {
        if (parts == SecurityParts.None)
        {
            return;
        }

        uint info = 0;
        if (parts.HasFlag(SecurityParts.Acl))
        {
            info |= DaclSecurityInformation;
        }

        if (parts.HasFlag(SecurityParts.Owner))
        {
            info |= OwnerSecurityInformation | GroupSecurityInformation;
        }

        if (parts.HasFlag(SecurityParts.Audit))
        {
            info |= SaclSecurityInformation;
        }

        nint owner, group, dacl, sacl, descriptor;
        uint error = GetNamedSecurityInfoW(Win32.LongPath(source), SeFileObject, info, &owner, &group, &dacl, &sacl, &descriptor);
        if (error != 0)
        {
            throw Failure(error, "leer la seguridad del origen");
        }

        try
        {
            if (parts.HasFlag(SecurityParts.Acl) && GetSecurityDescriptorControl(descriptor, out ushort control, out _))
            {
                // Conserva si los permisos estaban protegidos (sin herencia) o heredaban de la carpeta.
                info |= (control & SeDaclProtected) != 0 ? ProtectedDaclSecurityInformation : UnprotectedDaclSecurityInformation;
            }

            error = SetNamedSecurityInfoW(Win32.LongPath(destination), SeFileObject, info, owner, group, dacl, sacl);
            if (error != 0)
            {
                throw Failure(error, "aplicar la seguridad en el destino");
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static Exception Failure(uint error, string what) => error is 5 or 1314
        ? new UnauthorizedAccessException($"No se pudo {what}: hacen falta privilegios de administrador.")
        : new IOException($"No se pudo {what} (error {error}).");

    private const uint SeFileObject = 1;
    private const uint OwnerSecurityInformation = 0x1, GroupSecurityInformation = 0x2, DaclSecurityInformation = 0x4, SaclSecurityInformation = 0x8;
    private const uint ProtectedDaclSecurityInformation = 0x80000000, UnprotectedDaclSecurityInformation = 0x20000000;
    private const ushort SeDaclProtected = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32FindStreamData
    {
        public long StreamSize;
        public fixed char cStreamName[296];
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstStreamW(string fileName, int infoLevel, Win32FindStreamData* data, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextStreamW(nint find, Win32FindStreamData* data);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint find);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetNamedSecurityInfoW(string name, uint type, uint info, nint* owner, nint* group, nint* dacl, nint* sacl, nint* descriptor);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint SetNamedSecurityInfoW(string name, uint type, uint info, nint owner, nint group, nint dacl, nint sacl);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSecurityDescriptorControl(nint descriptor, out ushort control, out uint revision);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
