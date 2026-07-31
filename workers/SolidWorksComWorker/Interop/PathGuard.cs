internal static class PathGuard
{
    private static string[]? _allowedRoots;

    public static IReadOnlyList<string> AllowedRoots()
    {
        if (_allowedRoots is not null)
        {
            return _allowedRoots;
        }

        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_ALLOWED_ROOTS");
        string[] roots = string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _allowedRoots = roots
            .Select(root => Path.GetFullPath(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return _allowedRoots;
    }

    public static string AssertAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw WorkerException.Validation("PATH_REQUIRED", "A file path is required.", new Dictionary<string, object?>());
        }

        string fullPath = Path.GetFullPath(path);
        string normalized = fullPath.ToLowerInvariant();

        foreach (string root in AllowedRoots())
        {
            string normalizedRoot = Path.GetFullPath(root).ToLowerInvariant();
            string prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

            if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }
        }

        throw WorkerException.Validation(
            "PATH_NOT_ALLOWED",
            $"Path is outside allowed CAD roots: {fullPath}. Set SOLIDWORKS_MCP_ALLOWED_ROOTS to allow it.",
            new Dictionary<string, object?> { ["path"] = fullPath, ["allowedRoots"] = AllowedRoots().ToArray() },
            ["Set SOLIDWORKS_MCP_ALLOWED_ROOTS to a semicolon-separated list of allowed directories."]);
    }
}
