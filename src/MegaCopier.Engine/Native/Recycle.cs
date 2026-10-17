using System.Runtime.InteropServices;

namespace MegaCopier.Engine.Native;

/// <summary>Enviar a la Papelera de reciclaje (como borrar en el Explorador), sin ventanas ni preguntas.</summary>
internal static unsafe partial class Recycle
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

    /// <summary>True si quedó en la Papelera. False si no se pudo (y no se ha borrado nada).</summary>
    public static bool ToRecycleBin(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.Length >= 260 || full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return false; // SHFileOperation no admite rutas largas: mejor la carpeta de borrados
        }

        fixed (char* from = full + "\0\0")
        {
            var operation = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = from,
                fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
            };
            return SHFileOperationW(&operation) == 0 && operation.fAnyOperationsAborted == 0 && !File.Exists(full) && !Directory.Exists(full);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public nint hwnd;
        public uint wFunc;
        public char* pFrom;
        public char* pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public char* lpszProgressTitle;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(ShFileOpStruct* operation);
}
