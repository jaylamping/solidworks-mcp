using System.Text.Json;
using System.Threading;

internal sealed record WorkerRequest(string Command, JsonElement? Args);

internal static partial class Program
{
    private const string ComLockName = @"Global\MarengoSolidWorksComWorker";
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
    private static int Main()
    {
        using Mutex comLock = new(false, ComLockName);
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
            return RunWorker();
        }
        finally
        {
            comLock.ReleaseMutex();
        }
    }

    private static int RunWorker()
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
            if (!CommandRegistry.TryGetValue(command, out Func<JsonElement?, object>? handler))
            {
                throw WorkerException.Validation(
                    "UNKNOWN_COMMAND",
                    $"Unknown worker command: {command}",
                    new Dictionary<string, object?> { ["command"] = command });
            }

            CommandSafety.RequireConfirmIfDestructive(command, request.Args);
            JsonElement? effectiveArgs = ApplyUseSelection(command, request.Args);
            object data = handler(effectiveArgs);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, WriteJson));
            return 0;
        }
        catch (WorkerException ex)
        {
            return WriteError(ex.Error);
        }
        catch (Exception ex)
        {
            var context = new Dictionary<string, object?> { ["command"] = command };
            return WriteError(SwErrorDecoder.FromException(ex, "Program.RunWorker", context));
        }
    }

    private static int WriteError(WorkerError error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error }, WriteJson));
        return 0;
    }

    private static int WriteError(string message) =>
        WriteError(new WorkerError("WORKER_ERROR", message, "worker"));
}
