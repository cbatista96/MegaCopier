using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MegaCopier.Engine.Ipc;

/// <summary>
/// Petición al proceso residente. El formato es JSON simple a propósito: la extensión del Explorador
/// (C++, fase 3) también lo enviará.
/// </summary>
public sealed class IpcRequest
{
    /// <summary>"copy" para encolar una copia, "show" para abrir la ventana.</summary>
    public string Command { get; set; } = "show";

    public List<string> Sources { get; set; } = [];

    public string? Destination { get; set; }

    public bool? Verify { get; set; }

    public ConflictPolicy? Conflict { get; set; }

    /// <summary>Mover en lugar de copiar (pegar tras Ctrl+X).</summary>
    public bool? Move { get; set; }

    /// <summary>Copiar (por defecto), comprobar sumas o borrar.</summary>
    public JobOperation? Operation { get; set; }

    /// <summary>Al borrar: sobrescribir antes.</summary>
    public bool? SecureDelete { get; set; }

    /// <summary>Más destinos: la misma copia a cada uno (un trabajo por destino).</summary>
    public List<string>? ExtraDestinations { get; set; }

    /// <summary>Hora de inicio programada (UTC).</summary>
    public DateTime? StartAtUtc { get; set; }

    /// <summary>Copiar, sincronizar (espejo), solo actualizar o solo carpetas.</summary>
    public CopyMode? Mode { get; set; }

    /// <summary>Con la operación Sync: en dos sentidos o como espejo hacia un lado.</summary>
    public SyncDirection? SyncDirection { get; set; }

    /// <summary>Lo que no se toca (en Sync, rutas relativas desmarcadas en la vista de comparación).</summary>
    public List<string>? Excluded { get; set; }

    /// <summary>Simular: decir qué se haría sin cambiar nada.</summary>
    public bool? DryRun { get; set; }

    /// <summary>Al enviar a otro equipo: el código que muestra su MegaCopier.</summary>
    public string? PeerCode { get; set; }

    /// <summary>Copiar a un ZIP (cifrado con AES-256 si además va <see cref="Encrypt"/>).</summary>
    public bool? Zip { get; set; }

    public IO.ArchiveFormat? ArchiveFormat { get; set; }

    /// <summary>Copia cifrada (con <see cref="Password"/>).</summary>
    public bool? Encrypt { get; set; }

    /// <summary>Contraseña para cifrar o descifrar (solo viaja por la tubería local; nunca se guarda).</summary>
    public string? Password { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }

    public string? JobId { get; set; }

    public string? Error { get; set; }

    /// <summary>Respuesta a "status": estado de la app en texto clave=valor (diagnóstico y pruebas).</summary>
    public string? Status { get; set; }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
internal sealed partial class IpcJsonContext : JsonSerializerContext;

public static partial class IpcProtocol
{
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    /// <summary>Nombre único por usuario: dos usuarios en el mismo equipo tienen cada uno su proceso residente.</summary>
    public static string PipeName { get; } = "MegaCopier-" + UserId();

    /// <summary>Mutex que marca la instancia residente (ámbito de sesión).</summary>
    public static string MutexName { get; } = @"Local\MegaCopier-" + UserId();

    internal static async Task WriteAsync<T>(Stream stream, T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, type);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<T?> ReadAsync<T>(Stream stream, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes)
        {
            throw new InvalidDataException("Mensaje IPC con tamaño inválido.");
        }

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, type);
    }

    private static string UserId()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? Environment.UserName;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);
}

/// <summary>Cliente: envía una petición al proceso residente si está en marcha.</summary>
public static class IpcClient
{
    /// <summary>
    /// Comprobación inmediata de si el proceso residente existe (su mutex de instancia única), sin esperar a la tubería.
    /// </summary>
    public static bool IsAppRunning()
    {
        if (Mutex.TryOpenExisting(IpcProtocol.MutexName, out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }

    public static async Task<IpcResponse?> TrySendAsync(IpcRequest request, int connectTimeoutMs, CancellationToken cancellationToken = default, string? pipeName = null)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName ?? IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(connectTimeoutMs, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }

        // Permite que la ventana del proceso residente pase al frente (si no, Windows solo haría parpadear la barra de tareas).
        if (IpcProtocol.GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint serverPid))
        {
            IpcProtocol.AllowSetForegroundWindow(serverPid);
        }

        await IpcProtocol.WriteAsync(pipe, request, IpcJsonContext.Default.IpcRequest, cancellationToken).ConfigureAwait(false);
        return await IpcProtocol.ReadAsync(pipe, IpcJsonContext.Default.IpcResponse, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Servidor: atiende peticiones de cualquier número de clientes mientras el proceso residente viva.</summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly Func<IpcRequest, Task<IpcResponse>> _handler;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public IpcServer(Func<IpcRequest, Task<IpcResponse>> handler, string? pipeName = null)
    {
        _handler = handler;
        _pipeName = pipeName ?? IpcProtocol.PipeName;
    }

    public void Start() => _loop = Task.Run(() => AcceptLoopAsync(_stop.Token));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _ = Task.Run(() => ServeAsync(pipe, cancellationToken), cancellationToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            IpcResponse response;
            try
            {
                var request = await IpcProtocol.ReadAsync(pipe, IpcJsonContext.Default.IpcRequest, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Petición vacía.");
                response = await _handler(request).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                response = new IpcResponse { Ok = false, Error = ex.Message };
            }

            try
            {
                await IpcProtocol.WriteAsync(pipe, response, IpcJsonContext.Default.IpcResponse, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // El cliente se fue antes de leer la respuesta.
            }
        }
    }
}
