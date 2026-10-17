using Renci.SshNet;
using Renci.SshNet.Common;

namespace MegaCopier.Engine.Remote;

/// <summary>
/// SFTP (SSH) con SSH.NET. Contraseña o clave privada (si el secreto guardado es una clave "-----BEGIN ... KEY-----").
/// La identidad del servidor se comprueba "al primer uso": la primera vez se guarda la huella de su clave y, si
/// después cambia, no se conecta (podría ser otro equipo haciéndose pasar por él).
/// Los ids son rutas absolutas del servidor; las carpetas terminan en "/".
/// </summary>
internal sealed class SftpStore : IRemoteStore
{
    private readonly SftpClient _client;
    private readonly Lock _gate = new();
    private readonly string _root;

    public SftpStore(Connection connection, string secret)
    {
        string user = connection.User.Length > 0 ? connection.User : Environment.UserName;
        AuthenticationMethod auth = secret.TrimStart().StartsWith("-----BEGIN", StringComparison.Ordinal)
            ? new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(secret))))
            : new PasswordAuthenticationMethod(user, secret);
        var info = new ConnectionInfo(connection.Host.Trim(), connection.Port > 0 ? connection.Port : 22, user, auth) { Timeout = TimeSpan.FromSeconds(30) };
        _client = new SftpClient(info);
        string? changed = null;
        _client.HostKeyReceived += (_, e) =>
        {
            string fingerprint = e.FingerPrintSHA256;
            if (connection.HostKey.Length == 0)
            {
                connection.HostKey = fingerprint; // primer uso: se recuerda
                Remember(connection);
                e.CanTrust = true;
            }
            else
            {
                e.CanTrust = string.Equals(connection.HostKey, fingerprint, StringComparison.Ordinal);
                if (!e.CanTrust)
                {
                    changed = fingerprint;
                }
            }
        };

        try
        {
            _client.Connect();
        }
        catch (SshAuthenticationException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
        catch (SshConnectionException ex) when (changed is not null)
        {
            throw new UnauthorizedAccessException(Texts.F("E_HostKeyChanged", connection.Host, changed), ex);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException)
        {
            throw new IOException(ex.Message, ex);
        }

        string root = connection.Root.Trim().Replace('\\', '/');
        _root = Folder(root.StartsWith('/') ? root : Combine(_client.WorkingDirectory, root));
    }

    /// <summary>Guarda la huella aprendida en la conexión guardada (si lo está).</summary>
    private static void Remember(Connection connection)
    {
        var all = Connections.Load();
        if (all.FirstOrDefault(c => c.Id == connection.Id) is { } saved)
        {
            saved.HostKey = connection.HostKey;
            Connections.Save(all);
        }
    }

    public string RootId => _root;

    private static string Folder(string path) => (path.StartsWith('/') ? path : "/" + path).TrimEnd('/') + "/";

    private static string Combine(string folder, string name) => folder.TrimEnd('/') + "/" + name;

    public IReadOnlyList<RemoteItem> Children(string parentId)
    {
        lock (_gate)
        {
            string folder = Folder(parentId);
            return
            [
                .. Wrap(() => _client.ListDirectory(folder))
                    .Where(f => f.Name is not ("." or ".."))
                    .Select(f => new RemoteItem(Combine(folder, f.Name) + (f.IsDirectory ? "/" : ""), f.Name, f.IsDirectory, f.IsDirectory ? 0 : f.Length, f.LastWriteTimeUtc)),
            ];
        }
    }

    public Stream OpenRead(string objectId)
    {
        lock (_gate)
        {
            return Wrap(() => _client.OpenRead(objectId));
        }
    }

    public string Upload(string parentId, string name, Stream data, long length, CancellationToken cancellationToken)
    {
        string id = Combine(Folder(parentId), name);
        lock (_gate)
        {
            Wrap(() =>
            {
                _client.UploadFile(data, id, canOverride: true, _ => cancellationToken.ThrowIfCancellationRequested());
                return 0;
            });
        }

        return id;
    }

    public string CreateFolder(string parentId, string name)
    {
        string id = Combine(Folder(parentId), name);
        lock (_gate)
        {
            if (!_client.Exists(id))
            {
                Wrap(() =>
                {
                    _client.CreateDirectory(id);
                    return 0;
                });
            }
        }

        return id + "/";
    }

    public void Delete(string objectId, bool recursive)
    {
        if (objectId.EndsWith('/'))
        {
            if (recursive)
            {
                foreach (var child in Children(objectId))
                {
                    Delete(child.Id, recursive: true);
                }
            }

            lock (_gate)
            {
                Wrap(() =>
                {
                    _client.DeleteDirectory(objectId.TrimEnd('/'));
                    return 0;
                });
            }
        }
        else
        {
            lock (_gate)
            {
                Wrap(() =>
                {
                    _client.DeleteFile(objectId);
                    return 0;
                });
            }
        }
    }

    /// <summary>Los errores de SSH.NET como los de siempre (IOException / acceso denegado) para el motor.</summary>
    private static T Wrap<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (SftpPermissionDeniedException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
        catch (SftpPathNotFoundException ex)
        {
            throw new FileNotFoundException(ex.Message, ex);
        }
        catch (SshException ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    public void Dispose()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        _client.Dispose();
    }
}
