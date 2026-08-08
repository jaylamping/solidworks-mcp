using System.Text.Json;
using System.Threading;

internal static partial class Program
{
    private static readonly JsonSerializerOptions WriteJsonCompact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static bool IsSessionMode(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--session", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? mode = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_WORKER_MODE");
        return string.Equals(mode, "session", StringComparison.OrdinalIgnoreCase);
    }

    private static int RunSessionHost()
    {
        using Mutex comLock = new(false, WorkerConstants.ComMutexName);
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "ready",
            protocol = 1,
            pid = Environment.ProcessId,
        }, WriteJsonCompact));
        Console.Out.Flush();

        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string? requestId = null;
            string? command = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                requestId = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    WriteSessionError(null, new WorkerError(
                        "MISSING_REQUEST_ID",
                        "Session request is missing id.",
                        "worker"));
                    continue;
                }

                int protocol = root.TryGetProperty("protocol", out JsonElement protoEl) && protoEl.ValueKind == JsonValueKind.Number
                    ? protoEl.GetInt32()
                    : 0;
                if (protocol != 1)
                {
                    WriteSessionError(requestId, new WorkerError(
                        "PROTOCOL_MISMATCH",
                        $"Unsupported session protocol: {protocol}",
                        "worker"));
                    continue;
                }

                command = root.TryGetProperty("command", out JsonElement cmdEl) ? cmdEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(command))
                {
                    WriteSessionError(requestId, new WorkerError(
                        "MISSING_COMMAND",
                        "Missing worker command.",
                        "worker"));
                    continue;
                }

                if (string.Equals(command, "__shutdown__", StringComparison.Ordinal))
                {
                    WriteSessionOk(requestId, new { shutdown = true });
                    return 0;
                }

                JsonElement? args = root.TryGetProperty("args", out JsonElement argsEl) ? argsEl.Clone() : null;

                if (!comLock.WaitOne(ComLockTimeout))
                {
                    WriteSessionError(requestId, new WorkerError(
                        "COM_LOCK_TIMEOUT",
                        "Timed out waiting for SolidWorks COM lock. Another worker or script is using SolidWorks.",
                        "worker",
                        Remediation:
                        [
                            "Wait for other SolidWorks scripts to finish.",
                            "Close hung SLDWORKS.exe processes if no script is running.",
                        ]));
                    continue;
                }

                try
                {
                    object data = DispatchCommand(command, args);
                    WriteSessionOk(requestId, data);
                }
                finally
                {
                    comLock.ReleaseMutex();
                }
            }
            catch (WorkerException ex)
            {
                WriteSessionError(requestId, ex.Error);
            }
            catch (Exception ex)
            {
                var context = new Dictionary<string, object?> { ["command"] = command };
                WriteSessionError(requestId, SwErrorDecoder.FromException(ex, "Program.RunSessionHost", context));
            }
        }

        return 0;
    }

    private static object DispatchCommand(string command, JsonElement? args)
    {
        if (!CommandRegistry.TryGetValue(command, out Func<JsonElement?, object>? handler))
        {
            throw WorkerException.Validation(
                "UNKNOWN_COMMAND",
                $"Unknown worker command: {command}",
                new Dictionary<string, object?> { ["command"] = command });
        }

        CommandSafety.RequireConfirmIfDestructive(command, args);
        object? preCheckpoint = TryAutoCheckpointBeforeMutation(command, args);
        JsonElement? effectiveArgs = ApplyUseSelection(command, args);
        object data = handler(effectiveArgs);
        return AttachPreCheckpoint(data, preCheckpoint);
    }

    private static void WriteSessionOk(string id, object data)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "response",
            id,
            ok = true,
            data,
        }, WriteJsonCompact));
        Console.Out.Flush();
    }

    private static void WriteSessionError(string? id, WorkerError error)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "response",
            id = id ?? "",
            ok = false,
            error,
        }, WriteJsonCompact));
        Console.Out.Flush();
    }
}
