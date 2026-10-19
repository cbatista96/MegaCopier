using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MegaCopier.App.Services;

/// <summary>
/// Si WinUI falla por dentro (un acceso a memoria inválido en sus bibliotecas, que cierra la aplicación de golpe sin
/// pasar por .NET), apunta la pila de llamadas (módulo + desplazamiento) en crashstack.log, en la carpeta de datos: con
/// los símbolos públicos de Microsoft se sabe qué lo provocó. No cambia nada más: el fallo sigue su curso.
/// </summary>
internal static unsafe partial class CrashProbe
{
    private static string _path = "";
    private static int _written;

    public static void Install(string folder)
    {
        _path = Path.Combine(folder, "crashstack.log");
        AddVectoredExceptionHandler(1, &Handler);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Handler(ExceptionPointers* info)
    {
        try
        {
            var record = info->ExceptionRecord;
            if (record->ExceptionCode != 0xC0000005 || _written > 2)
            {
                return 0;
            }

            string module = ModuleOf(record->ExceptionAddress, out _);
            if (!module.Contains("Microsoft.UI", StringComparison.OrdinalIgnoreCase) && !module.Contains("CoreMessaging", StringComparison.OrdinalIgnoreCase))
            {
                return 0; // los nulos de .NET también son accesos inválidos: solo interesan los de WinUI
            }

            _written++;
            var frames = stackalloc nint[96];
            int count = RtlCaptureStackBackTrace(0, 96, frames, null);
            var text = new StringBuilder($"{DateTime.Now:HH:mm:ss.fff} hilo {Environment.CurrentManagedThreadId} fallo en {module}\n");
            for (int i = 0; i < count; i++)
            {
                string name = ModuleOf(frames[i], out nint baseAddress);
                text.Append($"  {Path.GetFileName(name)}+0x{frames[i] - baseAddress:X}\n");
            }

            File.AppendAllText(_path, text.ToString());
        }
        catch
        {
            // Nunca empeorar un fallo.
        }

        return 0; // EXCEPTION_CONTINUE_SEARCH: el fallo sigue su curso
    }

    private static string ModuleOf(nint address, out nint baseAddress)
    {
        baseAddress = 0;
        if (!GetModuleHandleExW(0x4 | 0x2, address, out nint module)) // FROM_ADDRESS | UNCHANGED_REFCOUNT
        {
            return "?";
        }

        baseAddress = module;
        var buffer = stackalloc char[520];
        int length = GetModuleFileNameW(module, buffer, 520);
        return new string(buffer, 0, length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionRecord
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public ExceptionRecord* Next;
        public nint ExceptionAddress;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExceptionPointers
    {
        public ExceptionRecord* ExceptionRecord;
        public nint ContextRecord;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint AddVectoredExceptionHandler(uint first, delegate* unmanaged[Stdcall]<ExceptionPointers*, int> handler);

    [LibraryImport("kernel32.dll")]
    private static partial ushort RtlCaptureStackBackTrace(uint skip, uint count, nint* frames, uint* hash);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, out nint module);

    [LibraryImport("kernel32.dll")]
    private static partial int GetModuleFileNameW(nint module, char* name, int size);
}
