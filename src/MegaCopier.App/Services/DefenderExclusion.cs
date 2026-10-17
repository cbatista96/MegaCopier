using System.Diagnostics;
using System.Text;

namespace MegaCopier.App.Services;

/// <summary>
/// Exclusión de Windows Defender para MegaCopier.exe y mcopy.exe (Add-MpPreference -ExclusionProcess): Defender deja
/// de analizar cada archivo que escriben, y las copias de muchos archivos van bastante más rápido. Solo se hace si lo
/// pides en Ajustes, con el aviso de control de cuentas de Windows. Lo que copies no se analiza al escribirlo (sí al
/// abrirlo con otro programa). Sin permisos de administrador no se puede leer la lista de exclusiones, así que se
/// recuerda en los ajustes si se añadió.
/// </summary>
internal static class DefenderExclusion
{
    public static IReadOnlyList<string> Processes =>
    [
        Path.Combine(AppContext.BaseDirectory, "MegaCopier.exe"),
        Path.Combine(AppContext.BaseDirectory, "mcopy.exe"),
    ];

    /// <summary>La orden de PowerShell que añade o quita la exclusión.</summary>
    public static string Command(bool add) =>
        $"{(add ? "Add" : "Remove")}-MpPreference -ExclusionProcess {string.Join(',', Processes.Select(p => "'" + p.Replace("'", "''") + "'"))}";

    /// <summary>
    /// Añade o quita la exclusión con permisos de administrador. Null si no se dio permiso; false si Defender no la
    /// aceptó (por ejemplo, otro antivirus lo sustituye o una directiva de la empresa lo impide).
    /// </summary>
    public static async Task<bool?> SetAsync(bool add)
    {
        if (App.TestMode)
        {
            App.TestLog("defender.log", Command(add)); // en las pruebas nunca se toca Defender
            return true;
        }

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; " + Command(add)));
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = true,
            Verb = "runas", // aviso de control de cuentas de Windows
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(info)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // no se dio permiso
        }
    }
}
