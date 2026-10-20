namespace MegaCopier.Engine.Tests;

public sealed class SingleFileConflictTests : IDisposable
{
    private readonly TempDir _temp = new();
    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(ConflictDecision.Skip)]
    [InlineData(ConflictDecision.Replace)]
    [InlineData(ConflictDecision.KeepBoth)]
    public void Un_archivo_repetido_pregunta_aunque_tenga_igual_tamano_y_fecha(ConflictDecision decision)
    {
        string source = _temp.Combine("origen.pdf"), destination = _temp.Combine("destino");
        Directory.CreateDirectory(destination);
        string existing = Path.Combine(destination, "origen.pdf");
        File.WriteAllText(source, "nuevo");
        File.WriteAllText(existing, "viejo");
        File.SetLastWriteTimeUtc(existing, File.GetLastWriteTimeUtc(source));
        int asked = 0;
        var engine = new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Ask })
        {
            ConflictResolver = (info, _) =>
            {
                asked++;
                Assert.Equal(existing, info.Destination);
                return new ConflictAnswer(decision, ApplyToAll: false);
            },
        };

        var result = engine.Run(new CopyJobRequest([source], destination));

        Assert.True(result.Success);
        Assert.Equal(1, asked);
        Assert.Equal(decision == ConflictDecision.Replace ? "nuevo" : "viejo", File.ReadAllText(existing));
        if (decision == ConflictDecision.KeepBoth)
        {
            Assert.Equal("nuevo", File.ReadAllText(Path.Combine(destination, "origen (2).pdf")));
        }
        Assert.Equal(decision == ConflictDecision.Skip ? 1 : 0, result.FilesSkipped);
    }

    [Fact]
    public void Reanudar_un_archivo_individual_no_vuelve_a_preguntar_por_el_terminado()
    {
        string source = _temp.Combine("origen.pdf"), destination = _temp.Combine("destino");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "igual");
        File.Copy(source, Path.Combine(destination, "origen.pdf"));
        var engine = new CopyEngine(new CopyOptions { Conflict = ConflictPolicy.Ask })
        {
            ConflictResolver = (_, _) => throw new InvalidOperationException("No debe preguntar al reanudar lo ya terminado."),
        };

        var result = engine.Run(new CopyJobRequest([source], destination) { Resumed = true });

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesSkipped);
    }
}
