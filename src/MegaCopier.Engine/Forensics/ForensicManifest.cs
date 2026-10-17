using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Forensics;

/// <summary>Un archivo del manifiesto: su ruta relativa al destino, tamaño, fecha y SHA-256 del origen y de la copia.</summary>
public sealed class ManifestEntry
{
    public string Path { get; set; } = "";

    public string Source { get; set; } = "";

    public long Size { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public string Sha256Source { get; set; } = "";

    public string Sha256Copy { get; set; } = "";
}

/// <summary>Lo que se firma: quién, cuándo, desde dónde y la huella de cada archivo.</summary>
public sealed class Manifest
{
    public string Tool { get; set; } = "MegaCopier";

    public string ToolVersion { get; set; } = "";

    public string Machine { get; set; } = "";

    public string User { get; set; } = "";

    public DateTime StartedUtc { get; set; }

    public DateTime FinishedUtc { get; set; }

    public List<string> Sources { get; set; } = [];

    public string Destination { get; set; } = "";

    public List<ManifestEntry> Files { get; set; } = [];

    /// <summary>Clave pública (SubjectPublicKeyInfo, Base64) con la que se comprueba la firma.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Huella corta de la clave (para compararla con la que muestra MegaCopier en el equipo que copió).</summary>
    public string KeyFingerprint { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Manifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;

/// <summary>Resultado de comprobar un manifiesto.</summary>
public sealed record ManifestCheck(bool SignatureValid, string KeyFingerprint, int Files, List<string> Changed, List<string> Missing)
{
    public bool AllGood => SignatureValid && Changed.Count == 0 && Missing.Count == 0;
}

/// <summary>
/// Modo forense o con valor de prueba: al terminar una copia se escribe un manifiesto (JSON) con la SHA-256 de cada
/// archivo en el origen y en la copia releída, quién y cuándo, firmado con ECDSA P-256 (archivo .sig al lado). La clave
/// es de esta instalación y se guarda protegida con DPAPI (solo este usuario de Windows puede usarla). Con
/// <see cref="Verify"/> cualquiera comprueba después que la firma es buena y que ningún archivo ha cambiado.
/// </summary>
public static partial class ForensicManifest
{
    public const string SignatureExtension = ".sig";

    /// <summary>Así empieza el nombre de los manifiestos (el espejo no los quita del destino).</summary>
    public const string FilePrefix = "MegaCopier-manifiesto ";

    /// <summary>Carpeta de la clave (las pruebas usan una temporal).</summary>
    public static string KeyFolder { get; set; } = DataFolder.Path;

    private static string KeyPath => System.IO.Path.Combine(KeyFolder, "forensic-key.bin");

    /// <summary>La clave de firma de esta instalación (se crea la primera vez).</summary>
    public static ECDsa LoadOrCreateKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (File.Exists(KeyPath))
        {
            key.ImportPkcs8PrivateKey(Unprotect(File.ReadAllBytes(KeyPath)), out _);
            return key;
        }

        Directory.CreateDirectory(KeyFolder);
        File.WriteAllBytes(KeyPath, Protect(key.ExportPkcs8PrivateKey()));
        return key;
    }

    public static string Fingerprint(byte[] publicKey) => Convert.ToHexString(SHA256.HashData(publicKey))[..16];

    /// <summary>Escribe el manifiesto y su firma; devuelve la ruta del manifiesto.</summary>
    public static string Write(string path, Manifest manifest)
    {
        using var key = LoadOrCreateKey();
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        manifest.PublicKey = Convert.ToBase64String(publicKey);
        manifest.KeyFingerprint = Fingerprint(publicKey);
        manifest.ToolVersion = typeof(ForensicManifest).Assembly.GetName().Version?.ToString(3) ?? "";
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonContext.Default.Manifest);
        File.WriteAllBytes(path, json);
        File.WriteAllText(path + SignatureExtension, Convert.ToBase64String(key.SignData(json, HashAlgorithmName.SHA256)));
        return path;
    }

    /// <summary>Comprueba la firma y vuelve a calcular la SHA-256 de cada archivo de la copia.</summary>
    public static ManifestCheck Verify(string path, Action<long>? onRead = null, CancellationToken cancellationToken = default)
    {
        byte[] json = File.ReadAllBytes(path);
        var manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.Manifest) ?? throw new InvalidDataException(path);
        bool signatureValid = false;
        string signaturePath = path + SignatureExtension;
        if (File.Exists(signaturePath))
        {
            using var key = ECDsa.Create();
            byte[] publicKey = Convert.FromBase64String(manifest.PublicKey);
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            signatureValid = key.VerifyData(json, Convert.FromBase64String(File.ReadAllText(signaturePath).Trim()), HashAlgorithmName.SHA256)
                && Fingerprint(publicKey) == manifest.KeyFingerprint;
        }

        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var changed = new List<string>();
        var missing = new List<string>();
        byte[] buffer = new byte[1 << 20];
        foreach (var entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = System.IO.Path.Combine(root, entry.Path);
            if (!File.Exists(file))
            {
                missing.Add(entry.Path);
                continue;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            {
                int read;
                while ((read = stream.Read(buffer)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    onRead?.Invoke(read);
                }
            }

            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), entry.Sha256Copy, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(entry.Path);
            }
        }

        return new ManifestCheck(signatureValid, manifest.KeyFingerprint, manifest.Files.Count, changed, missing);
    }

    // DPAPI (CryptProtectData) para guardar la clave privada: solo este usuario de Windows en este equipo puede abrirla.

    private static byte[] Protect(byte[] data) => Dpapi(data, protect: true);

    private static byte[] Unprotect(byte[] data) => Dpapi(data, protect: false);

    private static unsafe byte[] Dpapi(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        {
            var blobIn = new DataBlob { Size = data.Length, Data = (nint)input };
            bool ok = protect
                ? CryptProtectData(ref blobIn, "MegaCopier", 0, 0, 0, 0, out var blobOut)
                : CryptUnprotectData(ref blobIn, 0, 0, 0, 0, 0, out blobOut);
            if (!ok)
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            try
            {
                return new ReadOnlySpan<byte>((void*)blobOut.Data, blobOut.Size).ToArray();
            }
            finally
            {
                LocalFree(blobOut.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob input, string description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
