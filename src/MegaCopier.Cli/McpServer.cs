using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCopier.Engine;
using MegaCopier.Engine.Backup;
using MegaCopier.Engine.Ipc;

/// <summary>
/// "mcopy mcp": servidor MCP (Model Context Protocol) por la entrada y salida estándar, para que un asistente de IA
/// (Claude, Copilot, otros) pueda pedir copias a MegaCopier: "haz copia de Documentos al NAS esta noche y verifícala".
/// Las copias se entregan a la app (se ven en su ventana, con su progreso y su reanudación), igual que "mcopy send".
/// A propósito no hay herramientas para borrar: lo más destructivo que puede pedir un asistente es una copia espejo.
/// </summary>
internal static partial class Cli
{
    private const string McpProtocolVersion = "2025-06-18";

    private static int McpServe()
    {
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        while (input.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            JsonNode? reply;
            try
            {
                reply = HandleMcp(JsonNode.Parse(line)!.AsObject());
            }
            catch (JsonException ex)
            {
                reply = McpError(null, -32700, ex.Message);
            }

            if (reply is not null)
            {
                output.WriteLine(reply.ToJsonString());
            }
        }

        return ExitOk;
    }

    /// <summary>Atiende un mensaje JSON-RPC; null si era una notificación (sin respuesta).</summary>
    internal static JsonNode? HandleMcp(JsonObject message)
    {
        JsonNode? id = message["id"]?.DeepClone();
        string method = message["method"]?.GetValue<string>() ?? "";
        if (id is null)
        {
            return null; // notificaciones ("notifications/initialized"...)
        }

        try
        {
            return method switch
            {
                "initialize" => McpResult(id, new JsonObject
                {
                    ["protocolVersion"] = McpProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "MegaCopier", ["version"] = typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
                    ["instructions"] = "MegaCopier copia archivos en Windows con verificación y reanudación tras cortes. Usa rutas completas de Windows.",
                }),
                "ping" => McpResult(id, new JsonObject()),
                "tools/list" => McpResult(id, new JsonObject { ["tools"] = McpTools() }),
                "tools/call" => McpResult(id, McpCall(message["params"]?.AsObject() ?? [])),
                _ => McpError(id, -32601, $"Método desconocido: {method}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or CopyPlanException)
        {
            return McpResult(id, McpText(ex.Message, isError: true));
        }
    }

    private static JsonArray McpTools()
    {
        static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. required.Select(r => (JsonNode)r)]),
            },
        };

        static JsonObject Paths(string description) => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };
        static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
        static JsonObject Flag(string description) => new() { ["type"] = "boolean", ["description"] = description };

        return
        [
            Tool("copy", "Copia (o mueve) archivos y carpetas a una carpeta de destino con MegaCopier. La copia aparece en la ventana de MegaCopier, se puede pausar y sigue tras un corte de luz.",
                new JsonObject
                {
                    ["sources"] = Paths("Rutas completas de los archivos o carpetas a copiar."),
                    ["destination"] = Text("Carpeta de destino (ruta completa)."),
                    ["move"] = Flag("Mover en lugar de copiar."),
                    ["verify"] = Flag("Releer el destino y comparar sumas al terminar."),
                    ["mirror"] = Flag("Espejo: quitar del destino lo que ya no está en el origen (lo quitado va a la papelera o a la carpeta de versiones)."),
                    ["at"] = Text("Hora de inicio opcional, HH:mm o fecha y hora ISO 8601 (por ejemplo 23:30)."),
                },
                "sources", "destination"),
            Tool("verify_checksums", "Comprueba archivos de sumas (.sha256, .md5, .sfv...) o los hashes guardados dentro de los archivos de una carpeta.",
                new JsonObject { ["paths"] = Paths("Archivos de sumas o carpetas.") }, "paths"),
            Tool("create_checksums", "Crea un archivo de sumas de una carpeta o de unos archivos.",
                new JsonObject { ["paths"] = Paths("Carpeta o archivos.") }, "paths"),
            Tool("backup", "Copia de seguridad sin duplicados: guarda una instantánea de las carpetas en un repositorio (solo se guarda lo nuevo).",
                new JsonObject { ["sources"] = Paths("Carpetas a guardar."), ["repository"] = Text("Carpeta del repositorio de copias.") }, "sources", "repository"),
            Tool("list_backups", "Lista las copias de seguridad (instantáneas) de un repositorio.",
                new JsonObject { ["repository"] = Text("Carpeta del repositorio de copias.") }, "repository"),
            Tool("restore", "Restaura una copia de seguridad en una carpeta.",
                new JsonObject
                {
                    ["repository"] = Text("Carpeta del repositorio de copias."),
                    ["snapshot"] = Text("Identificador de la copia (de list_backups); vacío o 'latest' = la última."),
                    ["destination"] = Text("Carpeta donde restaurar."),
                },
                "repository", "destination"),
            Tool("status", "Estado de MegaCopier: si está abierto y qué copias hay en marcha o en cola.", []),
        ];
    }

    private static JsonObject McpCall(JsonObject parameters)
    {
        string name = parameters["name"]?.GetValue<string>() ?? "";
        var arguments = parameters["arguments"]?.AsObject() ?? [];
        List<string> PathList(string key) =>
            [.. (arguments[key]?.AsArray() ?? throw new ArgumentException($"Falta '{key}'.")).Select(n => Path.GetFullPath(n!.GetValue<string>()))];
        string Required(string key) => arguments[key]?.GetValue<string>() is { Length: > 0 } value ? value : throw new ArgumentException($"Falta '{key}'.");
        bool Flag(string key) => arguments[key]?.GetValue<bool>() ?? false;

        switch (name)
        {
            case "copy":
            {
                var request = new IpcRequest
                {
                    Command = "copy",
                    Sources = PathList("sources"),
                    Destination = Path.GetFullPath(Required("destination")),
                    Move = Flag("move") ? true : null,
                    Verify = Flag("verify") ? true : null,
                    Mode = Flag("mirror") ? CopyMode.Mirror : null,
                    StartAtUtc = arguments["at"]?.GetValue<string>() is { Length: > 0 } at ? ParseTime(at).ToUniversalTime() : null,
                };
                return Deliver(request);
            }

            case "verify_checksums":
                return Deliver(new IpcRequest { Command = "copy", Operation = JobOperation.Test, Sources = PathList("paths") });
            case "create_checksums":
                return Deliver(new IpcRequest { Command = "copy", Operation = JobOperation.Hash, Sources = PathList("paths") });
            case "backup":
                return Deliver(new IpcRequest { Command = "copy", Operation = JobOperation.Snapshot, Sources = PathList("sources"), Destination = Path.GetFullPath(Required("repository")) });
            case "restore":
                return Deliver(new IpcRequest
                {
                    Command = "copy",
                    Operation = JobOperation.Restore,
                    Sources = [Path.GetFullPath(Required("repository"))],
                    Destination = Path.GetFullPath(Required("destination")),
                    SnapshotId = arguments["snapshot"]?.GetValue<string>() is { } snapshot && snapshot != "latest" ? snapshot : "",
                });
            case "list_backups":
            {
                var repository = new BackupRepository(Required("repository"));
                var lines = repository.Snapshots().Select(s => $"{s.Id}\t{s.Files.Count} archivos\t{Format.Bytes(s.TotalBytes)}\t{string.Join("; ", s.Sources)}").ToList();
                return McpText(lines.Count == 0 ? $"No hay copias de seguridad en {repository.Root}." : string.Join('\n', lines));
            }

            case "status":
            {
                if (!IpcClient.IsAppRunning())
                {
                    return McpText("MegaCopier no está abierto (no hay copias en marcha).");
                }

                var response = IpcClient.TrySendAsync(new IpcRequest { Command = "status" }, connectTimeoutMs: 3000).GetAwaiter().GetResult();
                return McpText(response?.Status ?? "Sin respuesta de MegaCopier.");
            }

            default:
                throw new ArgumentException($"Herramienta desconocida: {name}");
        }
    }

    /// <summary>Entrega la petición a la app (arrancándola en la bandeja si hace falta).</summary>
    private static JsonObject Deliver(IpcRequest request)
    {
        var response = IpcClient.IsAppRunning() ? IpcClient.TrySendAsync(request, connectTimeoutMs: 3000).GetAwaiter().GetResult() : null;
        if (response is null && FindApp() is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = true });
            for (int attempt = 0; attempt < 50 && response is null; attempt++)
            {
                Thread.Sleep(100);
                response = IpcClient.IsAppRunning() ? IpcClient.TrySendAsync(request, connectTimeoutMs: 1000).GetAwaiter().GetResult() : null;
            }
        }

        return response switch
        {
            null => McpText("No se pudo abrir MegaCopier.", isError: true),
            { Ok: true } => McpText($"Hecho: trabajo {response.JobId} en MegaCopier (se ve en su ventana; pide 'status' para el progreso)."),
            _ => McpText(response.Error ?? "MegaCopier rechazó la petición.", isError: true),
        };
    }

    private static JsonObject McpText(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject McpResult(JsonNode id, JsonNode result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject McpError(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
}
