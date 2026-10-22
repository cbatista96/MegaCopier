namespace MegaCopier.Engine;

/// <summary>
/// Dónde guarda MegaCopier sus datos (ajustes, trabajos pendientes, historial, temas, registros...): en
/// %LOCALAPPDATA%\MegaCopier, o, en modo portátil, en la carpeta "Data" junto al programa. El modo portátil se activa
/// poniendo un archivo "portable.txt" junto a MegaCopier.exe (por ejemplo, en un pendrive).
/// </summary>
public static class DataFolder
{
    public const string PortableMarker = "portable.txt";

    public static bool IsPortable { get; } = File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, PortableMarker));

    // Solo las compilaciones ejecutadas explícitamente en modo de prueba admiten datos aislados.
    public static string Path { get; } = Environment.GetEnvironmentVariable("MEGACOPIER_TEST") == "1"
        && Environment.GetEnvironmentVariable("MEGACOPIER_TEST_DATA_DIRECTORY") is { Length: > 0 } testPath
        && System.IO.Path.IsPathFullyQualified(testPath) ? testPath : IsPortable
        ? System.IO.Path.Combine(AppContext.BaseDirectory, "Data")
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaCopier");

    public static string Combine(string name) => System.IO.Path.Combine(Path, name);
}
