using System.Text.Json;

internal static class CommandSafety
{
    public static bool IsDestructive(string command) =>
        CommandPolicyCatalog.TryGet(command, out CommandPolicy policy) && policy.Destructive;

    public static bool ShouldAutoCheckpoint(string command)
    {
        if (!AutoCheckpointEnabled())
        {
            return false;
        }

        return CommandPolicyCatalog.TryGet(command, out CommandPolicy policy) && policy.AutoCheckpoint;
    }

    public static bool AutoCheckpointEnabled()
    {
        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_AUTO_CHECKPOINT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return raw is not "0" and not "false" and not "False" and not "FALSE" and not "no" and not "off";
    }

    public static int AutoCheckpointDebounceSeconds()
    {
        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_CHECKPOINT_DEBOUNCE_SEC");
        if (int.TryParse(raw, out int seconds) && seconds >= 0)
        {
            return seconds;
        }

        return 45;
    }

    public static void RequireConfirmIfDestructive(string command, JsonElement? args)
    {
        if (!IsDestructive(command))
        {
            return;
        }

        bool confirmed = false;
        if (args is not null && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("confirm", out JsonElement confirmValue))
        {
            confirmed = confirmValue.ValueKind == JsonValueKind.True;
        }

        if (!confirmed)
        {
            throw WorkerException.Validation(
                "CONFIRM_REQUIRED",
                $"Destructive command '{command}' requires confirm: true in args.",
                new Dictionary<string, object?> { ["command"] = command },
                [
                    "Re-run with confirm: true only when the user explicitly requested this destructive operation.",
                    "A pre-change checkpoint is staged automatically for mutating commands when possible.",
                ]);
        }
    }
}
