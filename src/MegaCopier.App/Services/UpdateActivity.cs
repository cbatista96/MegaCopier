using System.Runtime.InteropServices;

namespace MegaCopier.App.Services;

/// <summary>Evita empezar una instalación desatendida mientras se arrastra algo con el ratón.</summary>
internal static partial class UpdateActivity
{
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
    internal static bool PointerDown => (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
}
