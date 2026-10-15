using Microsoft.Win32.SafeHandles;

namespace MegaCopier.Engine.IO;

internal static class IoHelpers
{
    public static int AlignUp(int value) => (value + AlignedBuffer.Alignment - 1) & ~(AlignedBuffer.Alignment - 1);

    /// <summary>
    /// Lee hasta <paramref name="count"/> bytes en <paramref name="offset"/>. En modo sin caché la petición
    /// se redondea al sector; el sistema devuelve solo los bytes reales al llegar al final del archivo.
    /// </summary>
    public static async Task<int> ReadFullAsync(SafeFileHandle handle, bool unbuffered, AlignedBuffer buffer, int count, long offset, CancellationToken cancellationToken)
    {
        int request = unbuffered ? AlignUp(count) : count;
        var memory = buffer.Memory[..request];
        int total = 0;
        while (total < count)
        {
            int read = await RandomAccess.ReadAsync(handle, memory[total..], offset + total, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return Math.Min(total, count);
    }

    /// <summary>
    /// Escribe <paramref name="count"/> bytes. En modo sin caché el último bloque se rellena con ceros hasta el
    /// sector; quien llama ajusta después la longitud exacta del archivo.
    /// </summary>
    public static ValueTask WriteFullAsync(SafeFileHandle handle, bool unbuffered, AlignedBuffer buffer, int count, long offset)
    {
        int length = count;
        if (unbuffered)
        {
            length = AlignUp(count);
            if (length > count)
            {
                buffer.Span[count..length].Clear();
            }
        }

        // Sin token de cancelación a propósito: una escritura ya lanzada se deja terminar.
        return RandomAccess.WriteAsync(handle, buffer.Memory[..length], offset);
    }

    /// <summary>La E/S sin caché se usa en discos locales; en red se usa el modo con caché + vaciado explícito.</summary>
    public static bool SupportsUnbuffered(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && !full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                full = full[4..];
            }

            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            return new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
