using System.Buffers.Binary;
using System.IO.Hashing;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MegaCopier.Engine.Network;

/// <summary>
/// Envío de archivos entre dos equipos con MegaCopier, sin carpetas compartidas: TCP con TLS y un código de
/// emparejamiento que muestra el receptor. El código no viaja: cada lado demuestra que lo conoce con un HMAC ligado al
/// certificado TLS de esa conexión, así que alguien en medio (con otro certificado) no puede hacerse pasar por ninguno.
/// Cada archivo llega a un .mcpart, se comprueba con xxHash3 y se renombra; si la conexión se corta, al reenviar se
/// sigue desde lo que ya había llegado.
/// </summary>
public static class PeerProtocol
{
    public const int DefaultPort = 47801;
    public const int DiscoveryPort = 47802;
    internal const string Magic = "MCP1";
    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // base32 de Crockford: sin I, L, O, U

    /// <summary>Código nuevo de 12 caracteres (60 bits), en grupos: "K7QX-2M9D-4FJA".</summary>
    public static string NewCode()
    {
        Span<byte> random = stackalloc byte[12];
        RandomNumberGenerator.Fill(random);
        var code = new StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            if (i is 4 or 8)
            {
                code.Append('-');
            }

            code.Append(CodeAlphabet[random[i] % 32]);
        }

        return code.ToString();
    }

    /// <summary>El código tal como se compara: sin guiones ni espacios, en mayúsculas, O→0 e I/L→1.</summary>
    public static string Normalize(string code) =>
        new([.. code.ToUpperInvariant().Where(char.IsLetterOrDigit).Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })]);

    /// <summary>Certificado propio (autofirmado) del receptor; la confianza la da el código, no una autoridad.</summary>
    public static X509Certificate2 CreateCertificate(string name) => X509CertificateLoader.LoadPkcs12(CreateCertificatePfx(name), null);

    /// <summary>El mismo certificado como archivo .pfx (para guardarlo y usar siempre el mismo).</summary>
    public static byte[] CreateCertificatePfx(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=MegaCopier {name}", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return certificate.Export(X509ContentType.Pkcs12);
    }

    /// <summary>Prueba de que se conoce el código, ligada a esta conexión (reto + certificado) y a un lado.</summary>
    internal static byte[] Proof(string code, ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> certificateHash, string side)
    {
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("MegaCopier/" + Normalize(code)));
        var message = new byte[challenge.Length + certificateHash.Length + side.Length];
        challenge.CopyTo(message);
        certificateHash.CopyTo(message.AsSpan(challenge.Length));
        Encoding.ASCII.GetBytes(side).CopyTo(message, challenge.Length + certificateHash.Length);
        return HMACSHA256.HashData(key, message);
    }

    internal static async Task WriteMessageAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is < 0 or > 1 << 20)
        {
            throw new IOException("Mensaje no válido del otro equipo.");
        }

        byte[] data = new byte[size];
        await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(data);
    }

    internal static async Task WriteBytesAsync(Stream stream, byte[] data, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(stream, Convert.ToBase64String(data), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken cancellationToken) =>
        Convert.FromBase64String(await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false));

    internal static string Hash(XxHash3 hash) => hash.GetCurrentHashAsUInt64().ToString("x16");

    /// <summary>Ruta relativa segura dentro de la carpeta de recepción (sin "..", sin raíz, sin unidades).</summary>
    internal static string? SafeRelative(string relative)
    {
        string normalized = relative.Replace('/', '\\');
        if (normalized.Length == 0 || Path.IsPathRooted(normalized) || normalized.Contains(':')
            || normalized.Split('\\').Any(part => part is ".." or "." || part.Length == 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return null;
        }

        return normalized;
    }
}
