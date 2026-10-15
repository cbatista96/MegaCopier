using System.Runtime.InteropServices;

namespace MegaCopier.Shell;

public enum ClipboardOperation
{
    Copy,

    /// <summary>Los archivos se cortaron (Ctrl+X): pegar debe moverlos.</summary>
    Move,
}

public sealed record ClipboardFileList(IReadOnlyList<string> Paths, ClipboardOperation Operation);

/// <summary>Lee la lista de archivos que el Explorador dejó en el portapapeles (CF_HDROP + "Preferred DropEffect").</summary>
public static unsafe partial class ClipboardFiles
{
    private const uint CfHDrop = 15;
    private const uint DropEffectMove = 2;

    private static readonly uint PreferredDropEffect = RegisterClipboardFormatW("Preferred DropEffect");

    /// <summary>Comprobación instantánea, apta para el gancho de teclado: ¿hay archivos en el portapapeles?</summary>
    public static bool HasFiles() => IsClipboardFormatAvailable(CfHDrop);

    public static ClipboardFileList? TryRead()
    {
        // Otra aplicación puede tener el portapapeles abierto un instante: se reintenta brevemente.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(0))
            {
                try
                {
                    return ReadOpen();
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(15);
        }

        return null;
    }

    private static ClipboardFileList? ReadOpen()
    {
        nint drop = GetClipboardData(CfHDrop);
        if (drop == 0)
        {
            return null;
        }

        var paths = new List<string>();
        uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
        char* buffer = stackalloc char[32768];
        for (uint i = 0; i < count; i++)
        {
            uint length = DragQueryFileW(drop, i, buffer, 32768);
            if (length > 0)
            {
                paths.Add(new string(buffer, 0, (int)length));
            }
        }

        var operation = ClipboardOperation.Copy;
        nint effectHandle = GetClipboardData(PreferredDropEffect);
        if (effectHandle != 0)
        {
            uint* effect = (uint*)GlobalLock(effectHandle);
            if (effect != null)
            {
                if ((*effect & DropEffectMove) != 0)
                {
                    operation = ClipboardOperation.Move;
                }

                GlobalUnlock(effectHandle);
            }
        }

        return paths.Count > 0 ? new ClipboardFileList(paths, operation) : null;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("kernel32.dll")]
    private static partial void* GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
