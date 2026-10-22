using MegaCopier.Engine;

namespace MegaCopier.Engine.Tests;

public sealed class NavigationHistoryTests
{
    [Fact]
    public void Conserva_la_ruta_completa_al_volver_y_avanzar()
    {
        var history = new NavigationHistory<string>("copias");
        history.Visit("actualizaciones"); history.Visit("historial"); history.Visit("version");
        Assert.True(history.Move(false, out var page)); Assert.Equal("historial", page);
        Assert.True(history.Move(false, out page)); Assert.Equal("actualizaciones", page);
        Assert.True(history.Move(true, out page)); Assert.Equal("historial", page);
        Assert.True(history.Move(true, out page)); Assert.Equal("version", page);
        Assert.False(history.Move(true, out page)); Assert.Equal("version", page);
    }

    [Fact]
    public void Una_ruta_nueva_descarta_avance_y_no_duplica_la_pantalla_actual()
    {
        var history = new NavigationHistory<string>("copias");
        history.Visit("ajustes"); history.Visit("historial"); history.Move(false, out _);
        history.Visit("ajustes"); Assert.True(history.CanForward);
        history.Visit("otra sección"); Assert.False(history.CanForward);
        Assert.True(history.Move(false, out var page)); Assert.Equal("ajustes", page);
        Assert.True(history.Move(false, out page)); Assert.Equal("copias", page);
        Assert.False(history.Move(false, out _));
    }

    [Fact]
    public void Acota_historial_sin_perder_la_posicion_de_avance()
    {
        var history = new NavigationHistory<int>(0);
        for (int i = 1; i <= 200; i++) history.Visit(i);
        int count = 0;
        while (history.Move(false, out _)) count++;
        Assert.Equal(127, count);
        Assert.Equal(73, history.Current);
        while (history.Move(true, out _)) { }
        Assert.Equal(200, history.Current);
    }
}
