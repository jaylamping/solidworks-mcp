using System.Text.Json;
using SolidWorks.Interop.sldworks;

internal static partial class Program
{
    private static object CheckpointDocument(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string allowedPath = PathGuard.AssertAllowedPath(inputPath);

        if (!File.Exists(allowedPath))
        {
            throw WorkerException.Validation(
                "FILE_NOT_FOUND",
                $"CAD document does not exist: {allowedPath}",
                new Dictionary<string, object?> { ["path"] = allowedPath });
        }

        string root = Path.GetDirectoryName(allowedPath) ?? allowedPath;
        string checkpointDir = Path.Combine(root, ".checkpoints");
        Directory.CreateDirectory(checkpointDir);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string fileName = Path.GetFileNameWithoutExtension(allowedPath);
        string ext = Path.GetExtension(allowedPath);
        string checkpointPath = Path.Combine(checkpointDir, $"{fileName}_{stamp}{ext}");

        File.Copy(allowedPath, checkpointPath, overwrite: false);

        return new
        {
            sourcePath = allowedPath,
            checkpointPath,
            checkpointDir,
            timestampUtc = stamp,
        };
    }
}
