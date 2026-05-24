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
            return WriteError(
                "Timed out waiting for SolidWorks COM lock. Another worker or script is using SolidWorks.");
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
        try
        {
            string input = Console.In.ReadToEnd();
            WorkerRequest? request = JsonSerializer.Deserialize<WorkerRequest>(input, ReadJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return WriteError("Missing worker command.");
            }

            if (!CommandRegistry.TryGetValue(request.Command, out Func<JsonElement?, object>? handler))
            {
                throw new InvalidOperationException($"Unknown worker command: {request.Command}");
            }

            object data = handler(request.Args);
            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, WriteJson));
            return 0;
        }
        catch (Exception ex)
        {
            return WriteError(ex.Message);
        }
    }

    private static int WriteError(string error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error }, WriteJson));
        return 0;
    }
}
