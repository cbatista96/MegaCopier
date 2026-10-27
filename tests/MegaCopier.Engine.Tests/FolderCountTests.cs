namespace MegaCopier.Engine.Tests;

/// <summary>La pestaña Estado cuenta las carpetas aparte de los archivos: el motor deja ese número en la lista.</summary>
public class FolderCountTests
{
    [Fact]
    public void CuentaLasCarpetasQueCreaEnElDestino()
    {
        using var temp = new TempDir();
        string fotos = temp.Combine("origen", "Fotos");
        Directory.CreateDirectory(Path.Combine(fotos, "2024", "Verano"));
        Directory.CreateDirectory(Path.Combine(fotos, "Vacía"));
        File.WriteAllText(Path.Combine(fotos, "a.jpg"), "a");
        File.WriteAllText(Path.Combine(fotos, "2024", "Verano", "b.jpg"), "b");
        string suelto = temp.Combine("origen", "nota.txt");
        File.WriteAllText(suelto, "c");

        var engine = new CopyEngine(new CopyOptions());
        Assert.Null(engine.Files.FolderCount); // todavía sin recorrer los orígenes
        engine.Run(new CopyJobRequest([fotos, suelto], temp.Combine("destino")));

        // Fotos, 2024, Verano y la carpeta vacía; el archivo suelto no suma ninguna.
        Assert.Equal(4, engine.Files.FolderCount);
        Assert.Equal(3, engine.Files.Count);
    }
}
