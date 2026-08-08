using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

internal sealed record WorkerRequest(string Command, JsonElement? Args);

internal static partial class Program
{
    private static readonly TimeSpan ComLockTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly Dictionary<string, Func<JsonElement?, object>> CommandRegistry = BuildCommandRegistry();

    [STAThread]
    private static int Main(string[] args)
    {
        if (IsSessionMode(args))
        {
            return RunSessionHost();
        }

        using Mutex comLock = new(false, WorkerConstants.ComMutexName);
        if (!comLock.WaitOne(ComLockTimeout))
        {
            return WriteError(new WorkerError(
                "COM_LOCK_TIMEOUT",
                "Timed out waiting for SolidWorks COM lock. Another worker or script is using SolidWorks.",
                "worker",
                Remediation:
                [
                    "Wait for other SolidWorks scripts to finish.",
                    "Close hung SLDWORKS.exe processes if no script is running.",
                ]));
        }

        try
        {
            return RunOneshotWorker();
        }
        finally
        {
            comLock.ReleaseMutex();
        }
    }

    private static int RunOneshotWorker()
    {
        string? command = null;
        try
        {
            string input = Console.In.ReadToEnd();
            WorkerRequest? request = JsonSerializer.Deserialize<WorkerRequest>(input, ReadJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return WriteError(WorkerException.Validation(
                    "MISSING_COMMAND",
                    "Missing worker command.",
                    new Dictionary<string, object?>()).Error);
            }

            command = request.Command;
            object responseData = DispatchCommand(command, request.Args);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = responseData }, WriteJson));
            return 0;
        }
        catch (WorkerException ex)
        {
            return WriteError(ex.Error);
        }
        catch (Exception ex)
        {
            var context = new Dictionary<string, object?> { ["command"] = command };
            return WriteError(SwErrorDecoder.FromException(ex, "Program.RunOneshotWorker", context));
        }
    }

    private static object AttachPreCheckpoint(object data, object? preCheckpoint)
    {
        if (preCheckpoint is null)
        {
            return data;
        }

        JsonNode? node = JsonSerializer.SerializeToNode(data, WriteJson);
        if (node is JsonObject obj)
        {
            obj["preCheckpoint"] = JsonSerializer.SerializeToNode(preCheckpoint, WriteJson);
            return obj;
        }

        return new
        {
            value = data,
            preCheckpoint,
        };
    }

    private static int WriteError(WorkerError error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error }, WriteJson));
        return 0;
    }

    private static int WriteError(string message) =>
        WriteError(new WorkerError("WORKER_ERROR", message, "worker"));
}
