using System.Text;

namespace MegaCopier.Engine.Network;

/// <summary>
/// Una petición HTTP/1.1 mínima, leída de una conexión: línea de petición, cabeceras y cuerpo (con Content-Length o
/// "chunked"). El cuerpo no se carga en memoria: se lee a trozos (para subir archivos grandes).
/// </summary>
internal sealed class HttpRequest
{
    private const int MaxHeaderBytes = 32 * 1024;

    private long _remaining;   // bytes del cuerpo que quedan (Content-Length) o del trozo actual (chunked)
    private bool _chunked;
    private bool _finished;

    private HttpRequest(string method, string target, Dictionary<string, string> headers, bool http10)
    {
        Method = method;
        int question = target.IndexOf('?');
        Path = question < 0 ? target : target[..question];
        QueryString = question < 0 ? "" : target[(question + 1)..];
        Headers = headers;
        Close = http10 || (headers.TryGetValue("connection", out string? connection) && connection.Equals("close", StringComparison.OrdinalIgnoreCase));
        _chunked = headers.TryGetValue("transfer-encoding", out string? encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        if (!_chunked)
        {
            _remaining = headers.TryGetValue("content-length", out string? length) && long.TryParse(length, out long value) && value >= 0 ? value : 0;
            _finished = _remaining == 0;
        }
    }

    public string Method { get; }

    public string Path { get; }

    public string QueryString { get; }

    public Dictionary<string, string> Headers { get; }

    /// <summary>El cliente cierra la conexión después de esta petición.</summary>
    public bool Close { get; }

    public string? Query(string name)
    {
        foreach (string pair in QueryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            if (key == name)
            {
                return equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    /// <summary>Lee la siguiente petición de la conexión; null si el cliente la cerró.</summary>
    public static async Task<HttpRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? requestLine;
        do
        {
            requestLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (requestLine is null)
            {
                return null;
            }
        }
        while (requestLine.Length == 0); // líneas vacías entre peticiones

        string[] parts = requestLine.Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Petición HTTP no válida.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = requestLine.Length;
        while (await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false) is { Length: > 0 } line)
        {
            total += line.Length;
            if (total > MaxHeaderBytes)
            {
                throw new InvalidDataException("Cabeceras demasiado grandes.");
            }

            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return new HttpRequest(parts[0], parts[1], headers, parts[2] == "HTTP/1.0");
    }

    /// <summary>Todo el cuerpo (para JSON pequeños); falla si pasa de <paramref name="max"/> bytes.</summary>
    public async Task<byte[]> ReadBodyAsync(Stream stream, int max, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await CopyBodyAsync(stream, chunk =>
        {
            if (buffer.Length + chunk.Length > max)
            {
                throw new InvalidDataException("Cuerpo demasiado grande.");
            }

            buffer.Write(chunk.Span);
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Pasa el cuerpo a trozos a <paramref name="sink"/>.</summary>
    public async Task CopyBodyAsync(Stream stream, Func<ReadOnlyMemory<byte>, Task> sink, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1 << 16];
        while (!_finished)
        {
            if (_chunked && _remaining == 0)
            {
                string? sizeLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                int semicolon = sizeLine?.IndexOf(';') ?? -1;
                string hex = semicolon >= 0 ? sizeLine![..semicolon] : sizeLine ?? "";
                if (!long.TryParse(hex.Trim(), System.Globalization.NumberStyles.HexNumber, null, out _remaining) || _remaining < 0)
                {
                    throw new InvalidDataException("Trozo HTTP no válido.");
                }

                if (_remaining == 0)
                {
                    // Último trozo: posibles cabeceras finales y una línea vacía.
                    while (await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false) is { Length: > 0 })
                    {
                    }

                    _finished = true;
                    return;
                }
            }

            int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("La conexión se cerró a mitad del envío.");
            }

            _remaining -= read;
            await sink(buffer.AsMemory(0, read)).ConfigureAwait(false);
            if (_remaining == 0)
            {
                if (_chunked)
                {
                    await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false); // el \r\n tras cada trozo
                }
                else
                {
                    _finished = true;
                }
            }
        }
    }

    /// <summary>Descarta lo que quede del cuerpo, para poder leer la siguiente petición de la misma conexión.</summary>
    public Task SkipRestAsync(Stream stream, CancellationToken cancellationToken) =>
        _finished ? Task.CompletedTask : CopyBodyAsync(stream, _ => Task.CompletedTask, cancellationToken);

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        byte[] one = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (one[0] == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (line.Length > MaxHeaderBytes)
            {
                throw new InvalidDataException("Línea HTTP demasiado larga.");
            }

            line.Append((char)one[0]);
        }
    }
}
