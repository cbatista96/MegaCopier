using MegaCopier.Engine.Jobs;

namespace MegaCopier.Engine.Tests;

/// <summary>Reconocer los discos de una tarea por su número de serie, aunque Windows les cambie la letra.</summary>
public sealed class VolumeTests
{
    [Fact]
    public void IdentifiesTheSystemDriveBySerial()
    {
        string system = Path.GetPathRoot(Environment.SystemDirectory)!;
        var volume = Volumes.Identify(Path.Combine(system, "Windows"));
        Assert.NotNull(volume);
        Assert.Equal(system, volume.Root, ignoreCase: true);
        Assert.Matches("^[0-9A-F]{8}$", volume.Serial);
        Assert.Equal(volume.Serial, Volumes.SerialOf(system));
        Assert.Null(Volumes.Identify(@"\\servidor\recurso\carpeta"));
    }

    [Fact]
    public void RemapsPathsWhenTheDriveComesBackWithAnotherLetter()
    {
        var record = new CopyJobRecord
        {
            Sources = [@"C:\Fotos", @"C:\Documentos\informe.pdf"],
            Destination = @"F:\Copias",
            Excluded = [@"C:\Fotos\borrar.jpg"],
            FileOrder = [@"C:\Fotos\a.jpg"],
            Volumes = [new VolumeRef(@"C:\", "11111111", "Windows"), new VolumeRef(@"F:\", "ABCD1234", "Copias")],
        };

        // Ahora el USB «Copias» es G: y en F: hay otro disco (o ninguno).
        var serials = new Dictionary<string, string> { [@"C:\"] = "11111111", [@"F:\"] = "99999999", [@"G:\"] = "ABCD1234" };
        int moved = Volumes.Remap(
            record,
            root => serials.GetValueOrDefault(root),
            serial => serials.FirstOrDefault(kv => kv.Value == serial).Key);

        Assert.Equal(1, moved);
        Assert.Equal(@"G:\Copias", record.Destination);
        Assert.Equal([@"C:\Fotos", @"C:\Documentos\informe.pdf"], record.Sources);
        Assert.Equal(@"G:\", record.Volumes[1].Root);
        Assert.Equal([@"C:\Fotos\borrar.jpg"], record.Excluded);
    }

    [Fact]
    public void LeavesPathsAloneWhenTheDriveIsMissingOrInPlace()
    {
        var record = new CopyJobRecord
        {
            Sources = [@"E:\Cámara"],
            Destination = @"D:\Fotos",
            Volumes = [new VolumeRef(@"E:\", "CAFEBABE", "SD"), new VolumeRef(@"D:\", "12345678", "Datos")],
        };

        int moved = Volumes.Remap(record, root => root == @"D:\" ? "12345678" : null, _ => null);

        Assert.Equal(0, moved);
        Assert.Equal([@"E:\Cámara"], record.Sources);
        Assert.Equal(@"D:\Fotos", record.Destination);
    }

    [Fact]
    public void TheVolumesTravelWithTheSavedList()
    {
        var record = new CopyJobRecord { Sources = [@"C:\a"], Destination = @"F:\b", StartOnVolume = "ABCD1234", Volumes = [new VolumeRef(@"F:\", "ABCD1234", "Copias")] };
        var loaded = CopyJobRecord.FromJson(record.ToJson())!;
        Assert.Equal("ABCD1234", loaded.StartOnVolume);
        Assert.Equal(record.Volumes, loaded.Volumes);
    }
}
