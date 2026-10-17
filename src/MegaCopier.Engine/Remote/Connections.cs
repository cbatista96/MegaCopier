using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Remote;

public enum ConnectionKind
{
    /// <summary>FTP sin cifrar (solo para redes de confianza).</summary>
    Ftp,

    /// <summary>FTP con TLS explícito (AUTH TLS).</summary>
    Ftps,

    /// <summary>WebDAV por HTTPS o HTTP: Nextcloud, ownCloud, muchos NAS, Box, pCloud...</summary>
    WebDav,

    /// <summary>Almacenamiento S3 y compatibles: AWS, Backblaze B2, Cloudflare R2, Wasabi, MinIO...</summary>
    S3,

    /// <summary>SFTP (SSH).</summary>
    Sftp,

    /// <summary>OneDrive (personal o de empresa), con inicio de sesión de Microsoft.</summary>
    OneDrive,

    /// <summary>Google Drive, con inicio de sesión de Google.</summary>
    GoogleDrive,

    /// <summary>Dropbox, con inicio de sesión de Dropbox.</summary>
    Dropbox,
}

/// <summary>Un servidor guardado. La contraseña (o la clave secreta de S3) no está aquí: va al Administrador de credenciales.</summary>
public sealed class Connection
{
    /// <summary>"conn-" + identificador; es el que llevan las rutas net:.</summary>
    public string Id { get; set; } = RemotePath.ConnectionIdPrefix + Guid.NewGuid().ToString("N")[..12];

    public string Name { get; set; } = "";

    public ConnectionKind Kind { get; set; }

    /// <summary>Servidor (FTP, SFTP), dirección completa (WebDAV: https://nube/remote.php/dav/files/ana) o punto de acceso (S3).</summary>
    public string Host { get; set; } = "";

    /// <summary>0 = el de siempre del protocolo.</summary>
    public int Port { get; set; }

    /// <summary>Usuario (en S3, la clave de acceso).</summary>
    public string User { get; set; } = "";

    /// <summary>Carpeta del servidor que hace de raíz ("" = la raíz). En S3, el bucket y, si se quiere, un prefijo: "bucket/carpeta".</summary>
    public string Root { get; set; } = "";

    /// <summary>Región de S3 ("us-east-1" si no se indica; "auto" en R2).</summary>
    public string Region { get; set; } = "";

    /// <summary>Aceptar certificados no válidos (NAS con certificado propio). Avisado en la interfaz.</summary>
    public bool AcceptAnyCertificate { get; set; }

    /// <summary>SFTP: huella SHA-256 de la clave del servidor, guardada la primera vez; si cambia, no se conecta.</summary>
    public string HostKey { get; set; } = "";

    /// <summary>Nubes: el identificador de la app registrada en Microsoft, Google o Dropbox (ver docs/Nube.md).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Google: el "secreto" de la app de escritorio (Google lo pide aunque en una app instalada no es secreto).</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Solo pruebas: otras direcciones de inicio de sesión (vacío = las del servicio).</summary>
    public string AuthUrl { get; set; } = "";

    public string TokenUrl { get; set; } = "";
}

/// <summary>Las conexiones guardadas (connections.json en la carpeta de datos) y sus contraseñas (Administrador de credenciales).</summary>
public static unsafe partial class Connections
{
    /// <summary>Solo pruebas: otra carpeta en lugar de la de datos, y contraseñas en memoria.</summary>
    internal static string? FolderOverride { get; set; }

    internal static Dictionary<string, string>? SecretsOverride { get; set; }

    /// <summary>Modo de prueba de la app: las contraseñas solo en memoria (nunca al Administrador de credenciales del usuario).</summary>
    public static void UseMemorySecrets() => SecretsOverride ??= [];

    private static string FilePath => Path.Combine(FolderOverride ?? DataFolder.Path, "connections.json");

    public static List<Connection> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath, Encoding.UTF8), ConnectionsJsonContext.Default.ListConnection) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<Connection> connections)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize([.. connections], ConnectionsJsonContext.Default.ListConnection), new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }

    public static Connection? Find(string id) => Load().FirstOrDefault(c => c.Id == id);

    /// <summary>Abre la conexión guardada con ese id.</summary>
    public static IRemoteStore Open(string id) =>
        Find(id) is { } connection ? Open(connection, GetSecret(id)) : throw new IOException(Texts.F("E_SourceMissing", id));

    /// <summary>Abre una conexión (también una sin guardar, para "Probar").</summary>
    public static IRemoteStore Open(Connection connection, string secret) => connection.Kind switch
    {
        ConnectionKind.Ftp or ConnectionKind.Ftps => new FtpStore(connection, secret),
        ConnectionKind.WebDav => new WebDavStore(connection, secret),
        ConnectionKind.S3 => new S3Store(connection, secret),
        ConnectionKind.Sftp => new SftpStore(connection, secret),
        ConnectionKind.OneDrive => new OneDriveStore(connection, secret),
        ConnectionKind.GoogleDrive => new GoogleDriveStore(connection, secret),
        ConnectionKind.Dropbox => new DropboxStore(connection, secret),
        _ => throw new NotSupportedException(connection.Kind.ToString()),
    };

    // --- Contraseñas en el Administrador de credenciales de Windows (genéricas, del usuario) ---

    private static string Target(string id) => "MegaCopier:" + id;

    public static void SetSecret(string id, string secret)
    {
        if (SecretsOverride is { } fake)
        {
            fake[id] = secret;
            return;
        }

        byte[] blob = Encoding.Unicode.GetBytes(secret);
        fixed (char* target = Target(id))
        fixed (char* user = "MegaCopier")
        fixed (byte* data = blob)
        {
            var credential = new Credential
            {
                Type = 1, // CRED_TYPE_GENERIC
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = data,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE: este usuario en este equipo
                UserName = user,
            };
            if (!CredWriteW(&credential, 0))
            {
                throw new IOException($"CredWrite: {Marshal.GetLastPInvokeError()}");
            }
        }
    }

    public static string GetSecret(string id)
    {
        if (SecretsOverride is { } fake)
        {
            return fake.GetValueOrDefault(id, "");
        }

        Credential* credential = null;
        fixed (char* target = Target(id))
        {
            if (!CredReadW(target, 1, 0, &credential))
            {
                return "";
            }
        }

        try
        {
            return credential->CredentialBlobSize == 0 ? "" : Encoding.Unicode.GetString(credential->CredentialBlob, (int)credential->CredentialBlobSize);
        }
        finally
        {
            CredFree(credential);
        }
    }

    public static void DeleteSecret(string id)
    {
        if (SecretsOverride is { } fake)
        {
            fake.Remove(id);
            return;
        }

        fixed (char* target = Target(id))
        {
            CredDeleteW(target, 1, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(char* target, uint type, uint flags, Credential** credential);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(char* target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(void* buffer);
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<Connection>))]
internal sealed partial class ConnectionsJsonContext : JsonSerializerContext;
