using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MegaCopier.App.Services;

/// <summary>
/// Que un icono de MegaCopier se vea en la bandeja, junto al reloj, y no escondido tras la flecha «˄». Windows 11
/// esconde los iconos nuevos; lo que el usuario elige en Configuración (Personalización › Barra de tareas › Otros
/// iconos de la bandeja) se guarda por icono en HKCU\Control Panel\NotifyIconSettings, con IsPromoted = 1 para los que
/// se ven. MegaCopier lo pone a 1 en su icono principal la primera vez (después manda lo que elija el usuario) y en
/// los iconos de progreso de cada copia siempre: están para verse de un vistazo.
/// </summary>
internal static partial class TrayPromotion
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>
    /// Marca como visible el icono <paramref name="uid"/> de este programa. Windows crea su entrada al añadir el icono
    /// (a veces un instante después), así que se intenta enseguida y otra vez al poco.
    /// </summary>
    /// <param name="always">True: aunque el usuario lo hubiera escondido (iconos de progreso). False: solo si nunca se eligió.</param>
    /// <param name="changed">Se llama (en otro hilo) si se marcó: hay que volver a añadir el icono para que se vea.</param>
    public static void Promote(uint uid, bool always, Action? changed = null)
    {
        if (App.TestMode && Environment.GetEnvironmentVariable("MEGACOPIER_TEST_PROMOTE") != "1")
        {
            return; // las pruebas no tocan la configuración de la bandeja
        }

        foreach (int delay in new[] { 150, 1500 })
        {
            _ = Task.Delay(delay).ContinueWith(_ => TryPromote(uid, always, changed), TaskScheduler.Default);
        }
    }

    private static void TryPromote(uint uid, bool always, Action? changed)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
            if (exe is null || root is null)
            {
                return;
            }

            foreach (string name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name, writable: true);
                if (entry?.GetValue("UID") is not int id || (uint)id != uid
                    || !string.Equals(Resolve(entry.GetValue("ExecutablePath") as string), exe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object? current = entry.GetValue("IsPromoted");
                if ((always || current is null) && current is not 1)
                {
                    entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    changed?.Invoke();
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
    }

    /// <summary>Windows guarda algunas rutas con la carpeta conocida delante ("{GUID}\resto"): la ruta completa.</summary>
    private static string? Resolve(string? path)
    {
        if (path is null || path.Length < 40 || path[0] != '{' || path.IndexOf("}\\", StringComparison.Ordinal) is not (> 0 and var close))
        {
            return path;
        }

        if (!Guid.TryParse(path[..(close + 1)], out var folder) || SHGetKnownFolderPath(folder, 0, 0, out nint buffer) < 0)
        {
            return path;
        }

        try
        {
            return Path.Combine(Marshal.PtrToStringUni(buffer)!, path[(close + 2)..]);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
}
