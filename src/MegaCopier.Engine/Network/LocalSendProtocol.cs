using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Network;

/// <summary>
/// Protocolo de LocalSend v2 (https://github.com/localsend/protocol), la app gratuita para enviar archivos entre
/// móviles y ordenadores de la misma red. MegaCopier hace de receptor: anuncia el PC por multidifusión UDP y acepta
/// envíos por HTTP en el puerto 53317. Se anuncia con "protocol": "http" (permitido por el protocolo): así no hace
/// falta un certificado y cualquier versión de LocalSend puede enviarle.
/// </summary>
public static class LocalSendProtocol
{
    public const int DefaultPort = 53317;
    public const string MulticastAddress = "224.0.0.167";
    public const string Version = "2.1";
    public const string ApiPrefix = "/api/localsend/v2/";

    /// <summary>Un nombre de archivo de un móvil hecho seguro: sin "..", sin raíces y sin caracteres que Windows no admite.</summary>
    public static string? SafeRelativePath(string fileName)
    {
        var parts = new List<string>();
        foreach (string raw in fileName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            if (part is "." or ".." || part.Length == 0)
            {
                continue;
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                part = part.Replace(invalid, '_');
            }

            part = part.TrimEnd('.', ' ');
            if (part.Length > 0)
            {
                // CON, NUL, COM1... no pueden ser nombres de archivo en Windows.
                string stem = Path.GetFileNameWithoutExtension(part).ToUpperInvariant();
                bool reserved = stem is "CON" or "PRN" or "AUX" or "NUL"
                    || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsAsciiDigit(stem[3]));
                parts.Add(reserved ? "_" + part : part);
            }
        }

        return parts.Count == 0 ? null : Path.Combine([.. parts]);
    }
}

/// <summary>Quién es cada equipo (anuncios, registro e información).</summary>
public sealed class LocalSendInfo
{
    public string Alias { get; set; } = "";

    public string Version { get; set; } = LocalSendProtocol.Version;

    public string? DeviceModel { get; set; }

    public string? DeviceType { get; set; }

    public string Fingerprint { get; set; } = "";

    public int? Port { get; set; }

    public string? Protocol { get; set; }

    public bool? Download { get; set; }

    /// <summary>Solo en los anuncios UDP: true = "estoy aquí", false = respuesta a un anuncio.</summary>
    public bool? Announce { get; set; }
}

public sealed class LocalSendFile
{
    public string Id { get; set; } = "";

    public string FileName { get; set; } = "";

    public long Size { get; set; }

    public string? FileType { get; set; }

    public string? Sha256 { get; set; }

    public string? Preview { get; set; }

    public LocalSendMetadata? Metadata { get; set; }
}

public sealed class LocalSendMetadata
{
    public DateTimeOffset? Modified { get; set; }

    public DateTimeOffset? Accessed { get; set; }
}

public sealed class LocalSendPrepareRequest
{
    public LocalSendInfo Info { get; set; } = new();

    public Dictionary<string, LocalSendFile> Files { get; set; } = [];
}

public sealed class LocalSendPrepareResponse
{
    public string SessionId { get; set; } = "";

    public Dictionary<string, string> Files { get; set; } = [];
}

/// <summary>Lo que se pregunta al usuario antes de aceptar un envío.</summary>
public sealed record LocalSendOffer(string Sender, string? DeviceModel, int Files, long Bytes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LocalSendInfo))]
[JsonSerializable(typeof(LocalSendPrepareRequest))]
[JsonSerializable(typeof(LocalSendPrepareResponse))]
internal sealed partial class LocalSendJsonContext : JsonSerializerContext;
