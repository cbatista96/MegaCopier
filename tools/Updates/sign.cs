// Herramienta local: genera una pareja ECDSA o firma los instaladores de una publicación. Nunca imprime la clave.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 3 && args[0] == "create-key")
{
    string privatePath = Path.GetFullPath(args[1]), publicPath = Path.GetFullPath(args[2]);
    if (File.Exists(privatePath) || File.Exists(publicPath)) throw new IOException("Las claves existentes no se sustituyen.");
    using var pair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    File.WriteAllText(privatePath, pair.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, pair.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Claves creadas. Guarda la privada fuera del repositorio y conserva una copia segura.");
    return;
}
if (args.Length != 5 || args[0] != "sign") throw new ArgumentException("Uso: create-key privada.pem publica.pem | sign version carpeta privada.pem publica.pem");
string version = args[1], directory = Path.GetFullPath(args[2]);
if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Versión inválida.");
using var key = ECDsa.Create();
key.ImportFromPem(File.ReadAllText(args[3]));
if (key.KeySize != 256) throw new ArgumentException("Se necesita ECDSA P-256.");
using var publicKey = ECDsa.Create();
publicKey.ImportFromPem(File.ReadAllText(args[4]));
if (!key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo()))
    throw new CryptographicException("La privada no corresponde a la pública incrustada. No se publica una actualización incompatible.");
var manifest = new Manifest { Version = version };
foreach (string name in new[] { $"MegaCopier-Setup-{version}.exe", $"MegaCopier-{version}-portable.zip" })
{
    string path = Path.Combine(directory, name);
    using var file = File.OpenRead(path);
    manifest.Assets.Add(new() { Name = name, Size = file.Length, Sha256 = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() });
}
byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, SigningJson.Default.Manifest);
File.WriteAllBytes(Path.Combine(directory, "update.json"), bytes);
File.WriteAllBytes(Path.Combine(directory, "update.sig"), key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
Console.WriteLine("Manifiesto firmado; la clave privada no se incluye en la publicación.");

public sealed class Manifest
{
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "win-x64";
    public List<Asset> Assets { get; set; } = [];
}
public sealed class Asset
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Manifest))]
public partial class SigningJson : JsonSerializerContext;
