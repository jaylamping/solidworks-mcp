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
            .Select(NormalizeCadPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return _allowedRoots;
    }

    /// <summary>
    /// Normalize without turning UNC/WSL paths into drive-relative junk.
    /// </summary>
    public static string NormalizeCadPath(string path)
    {
        string trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        string unified = trimmed.Replace('/', '\\');
        if (unified.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return Path.GetFullPath(unified);
        }

        if (unified.Length >= 3
            && char.IsLetter(unified[0])
            && unified[1] == ':'
            && (unified[2] == '\\' || unified[2] == '/'))
        {
            return Path.GetFullPath(unified);
        }

        return Path.GetFullPath(trimmed);
    }

    public static string AssertAllowedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw WorkerException.Validation("PATH_REQUIRED", "A file path is required.", new Dictionary<string, object?>());
        }

        string fullPath = NormalizeCadPath(path);
        string normalized = fullPath.ToLowerInvariant();

        foreach (string root in AllowedRoots())
        {
            string normalizedRoot = NormalizeCadPath(root).ToLowerInvariant();
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
