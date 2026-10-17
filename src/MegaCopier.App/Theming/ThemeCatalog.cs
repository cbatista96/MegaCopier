using MegaCopier.App.Services;

namespace MegaCopier.App.Theming;

/// <summary>
/// Temas adicionales. Muchos se inspiran en las paletas de los temas clásicos de SuperCopier (Afterburner, Air, Metro,
/// Matrix, Ubuntu, Vista, XP...), rehechos con el estilo de Windows 11: degradados, texturas y controles coloreados.
/// </summary>
internal static class ThemeCatalog
{
    public static IEnumerable<ThemeDefinition> Extra()
    {
        // --- Oscuros -------------------------------------------------------------------------------------------------
        yield return Dark("combustion", "Combustión", "#FF1A0F0B", "#FF2A120A", "#FFFA7A1E", "#FFFFC21D", "#FFFA5800", "#FFFF7709", "#FFFFC21D",
            texture: "brasas", textureOpacity: 0.55, radius: 10, barHeight: 10);
        yield return Dark("caja-negra", "Caja negra", "#FF161616", "#FF0E0E0E", "#FFEF6307", "#FFF7890A", "#FFE42702", "#FFEF6307", "#FFF7890A",
            texture: "carbono", textureOpacity: 0.7, radius: 4);
        yield return Dark("metro-oscuro", "Metro oscuro", "#FF232323", "", "#FF1BA1E2", "#FF1BA1E2", "#FF6ED1FE", "#FFFF8000", "#FFFFBE3F",
            surface: "#FF2E2E2E", strong: "#FF3A3A3A", border: "#FF444444", radius: 0, barHeight: 8);
        yield return Dark("robot", "Robot verde", "#FF262626", "#FF333333", "#FFA4C639", "#FFA0D942", "#FF4FB04A", "#FFFF9001", "#FFFFCA84",
            accentText: "#FF102000", radius: 6);
        yield return Dark("cielo-otono", "Cielo de otoño", "#FF2B3E4F", "#FF48647D", "#FFBCE7FF", "#FFFFF061", "#FFF0B33D", "#FF7FB6D9", "#FFBCE7FF",
            text: "#FFE8F6FB", secondary: "#B3D2EDF6", accentText: "#FF14252F", radius: 10);
        yield return Dark("ambar", "Ámbar", "#FF2E2E2E", "#FF404040", "#FFD9A441", "#FFF2CA72", "#FFA2730D", "#FFD9A441", "#FFF2CA72",
            accentText: "#FF2A1C00", radius: 6);
        yield return Dark("granate", "Granate", "#FF2D2525", "#FF1A1414", "#FFE05A5A", "#FF8E2C3A", "#FFE05A5A", "#FFB23A48", "#FFFF8A80",
            text: "#FFF2E4E4", secondary: "#B3E0C8C8", radius: 8);
        yield return Dark("codigo-verde", "Código verde", "#FF000000", "", "#FF19D919", "#FF24AF24", "#FF99FA9B", "#FF019F01", "#FF5BE85B",
            text: "#FF3DFF5C", secondary: "#FF1FA82F", surface: "#CC020A02", strong: "#E6031203", border: "#FF0B4A0B", track: "#FF0A2A0A",
            accentText: "#FF001A00", texture: "lluvia", textureOpacity: 0.28, radius: 2, barHeight: 10);
        yield return Dark("vapor", "Vapor", "#FF2A2827", "#FF1B1A19", "#FF66C0F4", "#FF4C6B8A", "#FF66C0F4", "#FF3D5A73", "#FF8CCFF5",
            text: "#FFE0DDDA", secondary: "#B3B8B6B3", accentText: "#FF0B1B26", radius: 4);
        yield return Dark("metro-gris", "Metro gris", "#FF5A5A5A", "#FF3E3E3E", "#FF5FA4F3", "#FF5FA4F3", "#FF3769AF", "#FF7FB8F7", "#FF5FA4F3",
            surface: "#26FFFFFF", strong: "#38FFFFFF", radius: 6);
        yield return Dark("television", "Televisión", "#FF2F2F2F", "#FF1A1A1A", "#FF8FB8C8", "#FF313D45", "#FF346683", "#FF4A6F82", "#FF83969B",
            text: "#FFD6D6D6", secondary: "#B39D9D9D", accentText: "#FF102027", radius: 3);
        yield return Dark("reproductor", "Reproductor", "#FF181A21", "#FF0B0C10", "#FF3E8DDD", "#FF20365A", "#FF0660BA", "#FF516AB8", "#FF60B9C8",
            text: "#FFEDF2F9", secondary: "#B3AAB8CC", radius: 10, barHeight: 10);
        yield return Dark("humo", "Humo", "#FF5370A8", "#FF34497A", "#FFE4DD04", "#FFF3E59E", "#FFE4DD04", "#FFB2D1FF", "#FFE5FFFF",
            text: "#FFF4FCFF", secondary: "#CCC6DDEC", surface: "#26FFFFFF", strong: "#3DFFFFFF", accentText: "#FF2B2A00", radius: 12);
        yield return Dark("berenjena", "Berenjena", "#FF2C001E", "#FF4A1238", "#FFE95420", "#FFE95420", "#FFF7A35C", "#FFC34113", "#FFFFAC89",
            text: "#FFFFF5F0", secondary: "#B3E8CCD8", radius: 10, barHeight: 10);
        yield return Dark("sc3-azul", "SuperCopier 3 azul", "#FF333333", "#FF492421", "#FF7CCFFF", "#FF8ECEF3", "#FF4FB6F0", "#FFA4DDFD", "#FFE2F5FF",
            accentText: "#FF06263A", radius: 6, barHeight: 12);
        yield return Dark("sc3-verde", "SuperCopier 3 verde", "#FF333333", "#FF2B3A22", "#FF8CDE93", "#FF8CDE93", "#FF2FA84F", "#FF00FF80", "#FFFFFF00",
            accentText: "#FF06260C", radius: 6, barHeight: 12);
        yield return Dark("sc3-naranja", "SuperCopier 3 naranja", "#FF333333", "#FF492421", "#FFFA5900", "#FFFFC21D", "#FFFA5800", "#FFFF7709", "#FFFFC21D",
            radius: 6, barHeight: 12);
        yield return Dark("aurora", "Aurora", "#FF061026", "#FF0E1A34", "#FF3EE6B0", "#FF28E6A0", "#FF3C9CFF", "#FFAA5AFF", "#FF3C9CFF",
            accentText: "#FF002418", texture: "aurora", textureOpacity: 0.9, surface: "#33FFFFFF", strong: "#45FFFFFF", radius: 14, barHeight: 10);
        yield return Dark("galaxia", "Galaxia", "#FF05040E", "#FF140A2A", "#FFB28CFF", "#FF7A5CFF", "#FFE07AFF", "#FF46A6FF", "#FFB28CFF",
            accentText: "#FF14002E", texture: "galaxia", textureOpacity: 0.95, surface: "#2EFFFFFF", strong: "#40FFFFFF", radius: 14, barHeight: 10);
        yield return Dark("neon", "Neón", "#FF0A0420", "#FF1E0636", "#FFFF3DC8", "#FFFF3DC8", "#FF28DCFF", "#FF28DCFF", "#FFA05CFF",
            text: "#FFFFF0FB", secondary: "#CCD6B8FF", accentText: "#FF2A0020", texture: "rejilla", textureOpacity: 0.85,
            surface: "#2EFFFFFF", strong: "#40FFFFFF", border: "#66FF3DC8", radius: 12, barHeight: 10);

        // --- Claros --------------------------------------------------------------------------------------------------
        yield return Light("aire", "Aire", "#FFE7F1FE", "#FFD6E6FA", "#FFC9A800", "#FFF2D94E", "#FFDECC02", "#FF8CC8F0", "#FF3D8BEB",
            text: "#FF212121", radius: 12, barHeight: 10);
        yield return Light("metro-claro", "Metro claro", "#FFF1F6FB", "", "#FF1BA1E2", "#FF1BA1E2", "#FF6ED1FE", "#FFFFBE3F", "#FFFF8000",
            surface: "#FFFFFFFF", strong: "#FFE8F2F7", border: "#FFB7C6CC", radius: 0);
        yield return Light("pez-azul", "Pez azul", "#FFFFFFFF", "#FFE3F0FD", "#FF0080FF", "#FF0080FF", "#FF33B5FF", "#FF3399FF", "#FF99CCFF",
            border: "#FF8CC2F5", radius: 6);
        yield return Light("linea-plana", "Línea plana", "#FFD4D4CD", "#FFC6C6BE", "#FF2E92A9", "#FF2E92A9", "#FF026078", "#FF277789", "#FF5FB4C8",
            surface: "#FFE4E4E4", strong: "#FFD9D9D2", border: "#FFA8A8A0", radius: 2);
        yield return Light("plus", "Plus", "#FFF1F1F1", "#FFFFFFFF", "#FF427FED", "#FF8BBCFF", "#FF427FED", "#FF427FED", "#FF8BBCFF",
            surface: "#FFFFFFFF", strong: "#FFF7F7F7", border: "#FFDADADA", radius: 8);
        yield return Light("cristal", "Cristal", "#FFC5CCD4", "#FFB0B9C4", "#FF3F70B6", "#FF82BCE8", "#FF3F70B6", "#FF5791D2", "#FF91C6F1",
            texture: "metal", textureOpacity: 0.55, surface: "#CCF6F8FA", strong: "#E6FFFFFF", border: "#FF8C96A2", radius: 10, barHeight: 10);
        yield return Light("cielo", "Cielo", "#FFE8E8E8", "#FFF4FAFE", "#FF2A9AD8", "#FF4FB6F0", "#FF8ECEF3", "#FF99D3F5", "#FFE2F5FF",
            radius: 10);
        yield return Light("menta", "Menta", "#FFEBE9EB", "#FFE3F6E5", "#FF2FA84F", "#FF8CDE93", "#FF2FA84F", "#FF82C277", "#FFCDFAD1",
            radius: 10);
        yield return Light("cuaderno", "Cuaderno", "#FFF8F3E1", "", "#FFC98A1E", "#FFE6B136", "#FFFFCBCB", "#FFE6B136", "#FFFFE08A",
            text: "#FF2A2418", texture: "papel", textureOpacity: 1, surface: "#B3FFFFFF", strong: "#D9FFFFFF", border: "#FFD8CFAE", radius: 4);
        yield return Light("madera", "Madera", "#FFF0E4D0", "", "#FF8A5A2B", "#FFC29661", "#FF685C4B", "#FF9D7A4E", "#FFC29661",
            text: "#FF2E1F12", secondary: "#FF5C4530", texture: "madera", textureOpacity: 0.45, surface: "#D9F8F3E1", strong: "#F2F8F3E1",
            border: "#FF9A866E", track: "#66685C4B", radius: 6, barHeight: 10);
        yield return Light("brillo", "Brillo", "#FFCDF0F7", "#FFE9FBFE", "#FF1AA6C4", "#FF76ACD2", "#FF54FFFC", "#FF72DDEC", "#FFA6CDE2",
            radius: 12);
        yield return Light("nieve", "Nieve", "#FFE8E8E8", "#FFDCE3EC", "#FF3A7CDC", "#FF47BCFF", "#FF205AA5", "#FF5279A3", "#FFC4E1FF",
            radius: 10);
        yield return Dark("burbuja-azul", "Burbuja azul", "#FF2CADDD", "#FF127DA2", "#FFFFFFFF", "#FFABF4FF", "#FF0396C2", "#FF02B2DE", "#FFABF4FF",
            text: "#FFFFFFFF", secondary: "#E6E8F9FF", surface: "#33FFFFFF", strong: "#4DFFFFFF", accentText: "#FF0A5A78",
            texture: "ondas", textureOpacity: 0.6, radius: 14, barHeight: 10);
        yield return Light("burbuja-lima", "Burbuja lima", "#FFC5E23A", "#FFAFD30F", "#FF4D6B00", "#FF789000", "#FF0BAACA", "#FF02B2DE", "#FFABF4FF",
            text: "#FF1E2600", secondary: "#FF4A5A10", surface: "#59FFFFFF", strong: "#80FFFFFF", border: "#FF8FAA1E", radius: 14, barHeight: 10);
        yield return Light("toxico", "Tóxico", "#FFB2BBC4", "#FFC6CED6", "#FF5E9E1A", "#FF96D535", "#FFDAF2B6", "#FF9AD74B", "#FFC9E997",
            border: "#FF717479", radius: 6);
        yield return Light("naranja-tierra", "Naranja tierra", "#FFEFEBE7", "#FFF7F3EF", "#FFE95420", "#FFE57949", "#FFFFAC89", "#FFE95420", "#FFFFBC9F",
            border: "#FFC9BFB6", radius: 8);
        yield return Light("aero", "Aero", "#FFDCE9F5", "#FFF0F0F0", "#FF2A7DE1", "#FF37A42C", "#FF6DD15C", "#FF37A42C", "#FF8FE07F",
            backdrop: AppBackdrop.Acrylic, surface: "#B3FFFFFF", strong: "#D9FFFFFF", border: "#FFA9C2DA", radius: 6, barHeight: 14);
        yield return Light("moderno-8", "Moderno 8", "#FFF0F0F0", "", "#FF2E4A5D", "#FF2E4A5D", "#FF4E7A99", "#FF9DCEFF", "#FFC1E0FF",
            surface: "#FFFFFFFF", strong: "#FFE6E6E6", border: "#FFB5BFC6", radius: 0);
        yield return Light("mensajero", "Mensajero", "#FFECF3F6", "#FFD9E8ED", "#FF39688F", "#FF8CC4E8", "#FF39688F", "#FF608BA6", "#FFCEE0E7",
            text: "#FF2B354D", radius: 8);
        yield return Light("luna", "Luna", "#FFECE9D8", "#FFF2EFDF", "#FF316AC5", "#FF37B337", "#FF2D8F2D", "#FF37B337", "#FF7BD88F",
            border: "#FF7587DD", radius: 4, barHeight: 14);
        yield return Light("plata", "Plata", "#FFD2D5DD", "#FFE4E6EC", "#FF628CFF", "#FF37B337", "#FF2D8F2D", "#FF628CFF", "#FFA9C1FF",
            border: "#FFA7A7BF", radius: 4, barHeight: 14);
    }

    private static ThemeDefinition Dark(
        string id, string name, string background, string backgroundEnd, string accent,
        string totalStart, string totalEnd, string fileStart, string fileEnd,
        string text = "#FFFFFFFF", string secondary = "#B3FFFFFF", string surface = "#1AFFFFFF", string strong = "#29FFFFFF",
        string border = "#24FFFFFF", string track = "#30FFFFFF", string accentText = "#FFFFFFFF", string texture = "",
        double textureOpacity = 0.35, AppBackdrop backdrop = AppBackdrop.Solid, double radius = 8, double barHeight = 8) => new()
    {
        Id = id, Name = name, Dark = true, BuiltIn = true, Backdrop = backdrop,
        Background = background, BackgroundEnd = backgroundEnd, Surface = surface, SurfaceStrong = strong, Border = border,
        Text = text, TextSecondary = secondary, Accent = accent, AccentText = accentText,
        TotalStart = totalStart, TotalEnd = totalEnd, FileStart = fileStart, FileEnd = fileEnd, Track = track,
        Texture = texture, TextureOpacity = textureOpacity, CornerRadius = radius, BarHeight = barHeight,
    };

    private static ThemeDefinition Light(
        string id, string name, string background, string backgroundEnd, string accent,
        string totalStart, string totalEnd, string fileStart, string fileEnd,
        string text = "#FF1A1A1A", string secondary = "#FF5C5C5C", string surface = "#B3FFFFFF", string strong = "#E6FFFFFF",
        string border = "#1F000000", string track = "#1F000000", string accentText = "#FFFFFFFF", string texture = "",
        double textureOpacity = 0.35, AppBackdrop backdrop = AppBackdrop.Solid, double radius = 8, double barHeight = 8) => new()
    {
        Id = id, Name = name, Dark = false, BuiltIn = true, Backdrop = backdrop,
        Background = background, BackgroundEnd = backgroundEnd, Surface = surface, SurfaceStrong = strong, Border = border,
        Text = text, TextSecondary = secondary, Accent = accent, AccentText = accentText,
        TotalStart = totalStart, TotalEnd = totalEnd, FileStart = fileStart, FileEnd = fileEnd, Track = track,
        Texture = texture, TextureOpacity = textureOpacity, CornerRadius = radius, BarHeight = barHeight,
        Success = "#FF1F9D55", Warning = "#FFC27C0E", Error = "#FFD13438",
    };
}
