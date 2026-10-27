using System.Diagnostics;

namespace MegaCopier.Engine.Tests;

/// <summary>Copiar una unidad entera: va a una carpeta con su nombre, sin lo de Windows de la raíz; mover y borrar, nunca.</summary>
public sealed class WholeDriveTests
{
    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:", true)]
    [InlineData(@"\\servidor\recurso", true)]
    [InlineData(@"\\servidor\recurso\", true)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"\\servidor\recurso\carpeta", false)]
    public void RecognizesDriveRoots(string path, bool root) =>
        Assert.Equal(root, CopyPlanner.IsDriveRoot(path.Length == 2 ? path + Path.DirectorySeparatorChar : path));

    [Fact]
    public void DeletingOrMovingAWholeDriveIsRefusedBeforeReadingIt()
    {
        using var temp = new TempDir();
        var error = Assert.Throws<CopyPlanException>(() => CopyPlanner.Build([@"C:\"], temp.Path, allowDriveRoot: false));
        Assert.Contains(@"C:\", error.Message);

        var move = new CopyEngine(new CopyOptions { Move = true, RetryCount = 0 });
        Assert.Throws<CopyPlanException>(() => move.Run(new CopyJobRequest([@"C:\"], temp.Path)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public void CopiesAWholeDriveIntoAFolderNamedAfterIt()
    {
        using var disk = new TempDir();
        using var destination = new TempDir();
        Directory.CreateDirectory(disk.Combine("Fotos", "2024"));
        File.WriteAllText(disk.Combine("Fotos", "2024", "playa.jpg"), "foto");
        File.WriteAllText(disk.Combine("léeme.txt"), "hola");
        // Lo que Windows deja en la raíz de un disco: no se copia.
        Directory.CreateDirectory(disk.Combine("System Volume Information"));
        File.WriteAllText(disk.Combine("System Volume Information", "tracking.log"), "x");
        Directory.CreateDirectory(disk.Combine("$Recycle.Bin"));
        File.WriteAllText(disk.Combine("pagefile.sys"), "x");

        if (Subst.Create(disk.Path) is not { } letter)
        {
            return; // sin letras libres o sin subst: no se puede simular un disco
        }

        try
        {
            string root = letter + @":\";
            string name = SourceNames.TargetName(root);
            Assert.True(name.EndsWith($"({letter})", StringComparison.Ordinal) || name == Texts.F("E_DriveFolder", letter), name);
            string folder = Path.Combine(destination.Path, name);

            var engine = new CopyEngine(new CopyOptions { Verify = true, RetryCount = 0 });
            var result = engine.Run(new CopyJobRequest([root], destination.Path));

            Assert.True(result.Success, string.Join("\n", result.Failures.Select(f => f.Source + ": " + f.Message)));
            Assert.Equal("foto", File.ReadAllText(Path.Combine(folder, "Fotos", "2024", "playa.jpg")));
            Assert.Equal("hola", File.ReadAllText(Path.Combine(folder, "léeme.txt")));
            Assert.False(Directory.Exists(Path.Combine(folder, "System Volume Information")));
            Assert.False(Directory.Exists(Path.Combine(folder, "$Recycle.Bin")));
            Assert.False(File.Exists(Path.Combine(folder, "pagefile.sys")));
            // La raíz de un disco es oculta y de sistema: su carpeta en el destino no.
            var attributes = File.GetAttributes(folder);
            Assert.False(attributes.HasFlag(FileAttributes.Hidden));
            Assert.False(attributes.HasFlag(FileAttributes.System));
        }
        finally
        {
            Subst.Remove(letter);
        }
    }

    [Fact]
    public void CopyingADriveIntoItselfIsRefused()
    {
        using var disk = new TempDir();
        File.WriteAllText(disk.Combine("a.txt"), "a");
        if (Subst.Create(disk.Path) is not { } letter)
        {
            return;
        }

        try
        {
            var error = Assert.Throws<CopyPlanException>(() => CopyPlanner.Build([letter + @":\"], letter + @":\copia"));
            Assert.Contains(letter + @":\", error.Message);
        }
        finally
        {
            Subst.Remove(letter);
        }
    }

    /// <summary>Una unidad virtual (subst) sobre una carpeta, para probar una unidad entera sin tocar ningún disco.</summary>
    private static class Subst
    {
        public static char? Create(string folder)
        {
            var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
            for (char letter = 'Z'; letter >= 'M'; letter--)
            {
                if (used.Contains(letter))
                {
                    continue;
                }

                if (Run($"{letter}: \"{folder}\"") && Directory.Exists(letter + @":\"))
                {
                    return letter;
                }
            }

            return null;
        }

        public static void Remove(char letter) => Run($"{letter}: /d");

        private static bool Run(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "subst.exe"), arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                })!;
                process.WaitForExit(10_000);
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
