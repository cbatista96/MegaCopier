using Windows.Management.Deployment;

namespace MegaCopier.App;

/// <summary>
/// "Pegar con MegaCopier" en el menú contextual corto de Windows 11. Ese menú solo admite apps con identidad de paquete,
/// así que el instalador registra un paquete disperso (MegaCopierShell.msix: solo el manifiesto y los logotipos) cuyo
/// contenido es la propia carpeta de instalación. Sin firma, Windows 11 lo acepta con AllowUnsigned porque el editor
/// lleva el OID de "paquete sin firmar". En Windows 10 no hace nada: allí ya está el menú clásico.
/// </summary>
internal static class ShellMenuPackage
{
    private const string PackageName = "MegaCopier.ShellMenu";
    private const string FileName = "MegaCopierShell.msix";

    private static bool Supported => Environment.OSVersion.Version.Build >= 22000;

    public static void Register()
    {
        string msix = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!Supported || !File.Exists(msix))
        {
            return;
        }

        try
        {
            var options = new AddPackageOptions
            {
                ExternalLocationUri = new Uri(AppContext.BaseDirectory),
                AllowUnsigned = true,
                ForceUpdateFromAnyVersion = true,
            };
            var result = new PackageManager().AddPackageByUriAsync(new Uri(msix), options).AsTask().GetAwaiter().GetResult();
            if (result.ExtendedErrorCode is { } error)
            {
                Console.Error.WriteLine($"{PackageName}: {result.ErrorText} (0x{error.HResult:X8})");
            }
        }
        catch (Exception ex)
        {
            // El menú clásico ("Mostrar más opciones") sigue funcionando aunque esto falle.
            Console.Error.WriteLine($"{PackageName}: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        if (!Supported)
        {
            return;
        }

        try
        {
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                if (package.Id.Name == PackageName)
                {
                    manager.RemovePackageAsync(package.Id.FullName).AsTask().GetAwaiter().GetResult();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{PackageName}: {ex.Message}");
        }
    }
}
