using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.Native;

/// <summary>
/// Clonado de bloques (ReFS y Dev Drive): dentro del mismo volumen, la copia comparte los bloques del original en lugar
/// de duplicarlos. Es instantánea sea cual sea el tamaño y no ocupa espacio hasta que una de las dos se modifica.
/// Es lo que hace el Explorador de Windows 11 en un Dev Drive (FSCTL_DUPLICATE_EXTENTS_TO_FILE).
/// </summary>
internal static unsafe partial class BlockClone
{
    private const uint FileSupportsBlockRefcounting = 0x08000000;
    private const uint FsctlDuplicateExtentsToFile = 0x00098344;
    private const uint FsctlGetIntegrityInformation = 0x0009027C;
    private const uint FsctlSetIntegrityInformation = 0x0009C280;
    private const uint FsctlSetSparse = 0x000900C4;

    /// <summary>Máximo por llamada: menos de 4 GB y múltiplo de cualquier tamaño de clúster.</summary>
    private const long MaxChunk = 1L << 30;

    /// <summary>El volumen del archivo sabe clonar bloques (ReFS, Dev Drive).</summary>
    public static bool SupportsCloning(SafeFileHandle file)
    {
        uint flags;
        return GetVolumeInformationByHandleW(file, null, 0, null, null, &flags, null, 0) && (flags & FileSupportsBlockRefcounting) != 0;
    }

    /// <summary>Origen y carpeta de destino en el mismo volumen, y ese volumen sabe clonar bloques.</summary>
    public static bool CanClone(SafeFileHandle source, string destinationFolder)
    {
        uint sourceSerial, flags;
        if (!GetVolumeInformationByHandleW(source, null, 0, &sourceSerial, null, &flags, null, 0)
            || (flags & FileSupportsBlockRefcounting) == 0)
        {
            return false;
        }

        // FILE_FLAG_BACKUP_SEMANTICS para abrir la carpeta; solo se consulta su volumen.
        using var folder = File.OpenHandle(destinationFolder, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, (FileOptions)0x02000000);
        uint destinationSerial;
        return GetVolumeInformationByHandleW(folder, null, 0, &destinationSerial, null, null, null, 0) && destinationSerial == sourceSerial;
    }

    /// <summary>
    /// Clona el contenido de <paramref name="source"/> en <paramref name="destination"/> (recién creado, vacío).
    /// Lanza IOException si el sistema de archivos lo rechaza: quien llama copia entonces de la forma normal.
    /// </summary>
    public static void Clone(SafeFileHandle source, SafeFileHandle destination, string volumePath, long length, bool sparse)
    {
        if (sparse && !DeviceIoControl(destination, FsctlSetSparse, null, 0, null, 0, out _, null))
        {
            throw Error("FSCTL_SET_SPARSE");
        }

        // Origen y destino deben tener la misma configuración de flujos de integridad. La respuesta trae además el tamaño
        // de clúster, como en el ejemplo de Microsoft (más fiable que preguntarlo por la ruta del volumen).
        var integrity = stackalloc byte[16];
        long cluster = 0;
        if (DeviceIoControl(source, FsctlGetIntegrityInformation, null, 0, integrity, 16, out _, null))
        {
            var set = stackalloc byte[8];
            *(ushort*)set = *(ushort*)integrity; // ChecksumAlgorithm
            *(uint*)(set + 4) = *(uint*)(integrity + 4); // Flags
            DeviceIoControl(destination, FsctlSetIntegrityInformation, set, 8, null, 0, out _, null);
            cluster = *(uint*)(integrity + 12); // ClusterSizeInBytes
        }

        RandomAccess.SetLength(destination, length);
        if (length == 0)
        {
            return;
        }

        if (cluster <= 0 || (cluster & (cluster - 1)) != 0)
        {
            cluster = ClusterSize(volumePath);
        }

        long rounded = (length + cluster - 1) / cluster * cluster; // la última región puede pasar del final, como en el ejemplo de Microsoft
        var data = new DuplicateExtentsData { FileHandle = source.DangerousGetHandle() };
        for (long offset = 0; offset < rounded; offset += MaxChunk)
        {
            data.SourceFileOffset = offset;
            data.TargetFileOffset = offset;
            data.ByteCount = Math.Min(MaxChunk, rounded - offset);
            if (!DeviceIoControl(destination, FsctlDuplicateExtentsToFile, &data, sizeof(DuplicateExtentsData), null, 0, out _, null))
            {
                throw Error("FSCTL_DUPLICATE_EXTENTS_TO_FILE");
            }
        }
    }

    private static long ClusterSize(string volumePath)
    {
        uint sectorsPerCluster, bytesPerSector;
        return GetDiskFreeSpaceW(volumePath, &sectorsPerCluster, &bytesPerSector, null, null)
            ? (long)sectorsPerCluster * bytesPerSector
            : 64 * 1024; // el mayor clúster de ReFS: múltiplo de cualquiera menor
    }

    private static IOException Error(string what) =>
        new($"{what}: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}", Marshal.GetHRForLastWin32Error());

    [StructLayout(LayoutKind.Sequential)]
    private struct DuplicateExtentsData
    {
        public nint FileHandle;
        public long SourceFileOffset;
        public long TargetFileOffset;
        public long ByteCount;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandleW(
        SafeFileHandle file, char* name, uint nameSize, uint* serial, uint* maxComponent, uint* flags, char* fsName, uint fsNameSize);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string root, uint* sectorsPerCluster, uint* bytesPerSector, uint* freeClusters, uint* totalClusters);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, int inputSize, void* output, int outputSize, out int returned, void* overlapped);
}
