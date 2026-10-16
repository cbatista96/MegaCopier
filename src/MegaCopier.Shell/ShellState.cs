using Microsoft.Win32;

namespace MegaCopier.Shell;

/// <summary>
/// Estado que comparten la app y la extensión del Explorador (HKCU\Software\MegaCopier).
/// La extensión lo lee en cada menú: desactivar MegaCopier hace desaparecer sus opciones al instante.
/// </summary>
public static class ShellState
{
    private const string Key = @"Software\MegaCopier";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
