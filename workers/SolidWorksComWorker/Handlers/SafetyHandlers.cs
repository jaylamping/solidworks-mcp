using System.Collections.Concurrent;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private sealed record CheckpointCacheEntry(string CheckpointPath, DateTime Utc, string Method);

    private static readonly ConcurrentDictionary<string, CheckpointCacheEntry> RecentCheckpoints =
        new(StringComparer.OrdinalIgnoreCase);

    private static object CheckpointDocument(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string allowedPath = PathGuard.AssertAllowedPath(inputPath);
        bool force = BoolArg(args, "force", defaultValue: true);
        return CreateDocumentCheckpoint(allowedPath, reason: "explicit", force: force);
    }

    private static object ListCheckpoints(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string allowedPath = PathGuard.AssertAllowedPath(inputPath);
        string checkpointDir = CheckpointDirectoryFor(allowedPath);
        string fileName = Path.GetFileNameWithoutExtension(allowedPath);
        string ext = Path.GetExtension(allowedPath);

        if (!Directory.Exists(checkpointDir))
        {
            return new
            {
                sourcePath = allowedPath,
                checkpointDir,
                count = 0,
                checkpoints = Array.Empty<object>(),
            };
        }

        var checkpoints = Directory.EnumerateFiles(checkpointDir, $"{fileName}_*{ext}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Take(50)
            .Select(info => (object)new
            {
                checkpointPath = info.FullName,
                sizeBytes = info.Length,
                modifiedUtc = info.LastWriteTimeUtc.ToString("o"),
            })
            .ToList();

        return new
        {
            sourcePath = allowedPath,
            checkpointDir,
            count = checkpoints.Count,
            checkpoints,
        };
    }

    private static object RestoreFromCheckpoint(JsonElement? args)
    {
        string checkpointPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "checkpoint_path"));
        string? targetPathArg = StringArg(args, "path");
        bool closeOpen = BoolArg(args, "close_open_document", defaultValue: true);

        if (!File.Exists(checkpointPath))
        {
            throw WorkerException.Validation(
                "CHECKPOINT_NOT_FOUND",
                $"Checkpoint does not exist: {checkpointPath}",
                new Dictionary<string, object?> { ["checkpointPath"] = checkpointPath });
        }

        string targetPath = string.IsNullOrWhiteSpace(targetPathArg)
            ? InferRestoreTargetPath(checkpointPath)
            : PathGuard.AssertAllowedPath(targetPathArg);

        // Stage current target before overwrite so restore is itself reversible.
        object? preRestoreCheckpoint = null;
        if (File.Exists(targetPath))
        {
            preRestoreCheckpoint = CreateDocumentCheckpoint(targetPath, reason: "pre_restore", force: true);
        }

        ISldWorks? app = null;
        try
        {
            app = AttachSolidWorks(startIfMissing: false);
        }
        catch
        {
            // SolidWorks may not be running; file restore can still proceed.
        }

        string? openTitle = null;
        if (app is not null)
        {
            ModelDoc2? openDoc = FindOpenDocumentByPath(app, targetPath);
            if (openDoc is not null)
            {
                openTitle = Try(() => openDoc.GetTitle()) as string;
                if (closeOpen && !string.IsNullOrWhiteSpace(openTitle))
                {
                    TryVoid(() => app.CloseDoc(openTitle));
                }
                else if (!closeOpen)
                {
                    throw WorkerException.Validation(
                        "DOCUMENT_OPEN",
                        $"Cannot restore while document is open: {targetPath}. Re-run with close_open_document: true.",
                        new Dictionary<string, object?> { ["path"] = targetPath, ["title"] = openTitle });
                }
            }
        }

        File.Copy(checkpointPath, targetPath, overwrite: true);

        object? reopened = null;
        if (app is not null)
        {
            try
            {
                ModelDoc2 restored = OpenDocument(app, targetPath);
                reopened = DescribeDocument(restored);
            }
            catch (Exception ex)
            {
                reopened = new { error = ex.Message };
            }
        }

        return new
        {
            restored = true,
            checkpointPath,
            targetPath,
            closedTitle = openTitle,
            preRestoreCheckpoint,
            document = reopened,
        };
    }

    private static object? TryAutoCheckpointBeforeMutation(string command, JsonElement? args)
    {
        bool shouldCheckpoint = CommandSafety.ShouldAutoCheckpoint(command);
        if (!shouldCheckpoint
            && (command == "invoke" || command == "batch_invoke")
            && InvokeWriteEnabled())
        {
            shouldCheckpoint = true;
        }

        if (!shouldCheckpoint)
        {
            return null;
        }

        string? resolvedPath = ResolveCheckpointSourcePath(args);
        if (string.IsNullOrWhiteSpace(resolvedPath) && command == "batch_invoke")
        {
            resolvedPath = ResolveBatchInvokePath(args);
        }

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return new
            {
                skipped = true,
                reason = "no_document_path",
                command,
            };
        }

        try
        {
            string allowedPath = PathGuard.AssertAllowedPath(resolvedPath);
            return CreateDocumentCheckpoint(allowedPath, reason: $"auto:{command}", force: false);
        }
        catch (Exception ex)
        {
            // Never block a mutation solely because staging failed; surface the failure for the agent.
            return new
            {
                skipped = true,
                reason = "checkpoint_failed",
                command,
                error = ex.Message,
            };
        }
    }

    private static string? ResolveBatchInvokePath(JsonElement? args)
    {
        if (args is null
            || args.Value.ValueKind != JsonValueKind.Object
            || !args.Value.TryGetProperty("calls", out JsonElement calls)
            || calls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (call.TryGetProperty("path", out JsonElement pathValue)
                && pathValue.ValueKind == JsonValueKind.String)
            {
                string? path = pathValue.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static object CreateDocumentCheckpoint(string allowedPath, string reason, bool force)
    {
        if (!File.Exists(allowedPath))
        {
            throw WorkerException.Validation(
                "FILE_NOT_FOUND",
                $"CAD document does not exist: {allowedPath}",
                new Dictionary<string, object?> { ["path"] = allowedPath });
        }

        int debounceSec = CommandSafety.AutoCheckpointDebounceSeconds();
        if (!force
            && debounceSec > 0
            && RecentCheckpoints.TryGetValue(allowedPath, out CheckpointCacheEntry? recent)
            && (DateTime.UtcNow - recent.Utc).TotalSeconds < debounceSec)
        {
            return new
            {
                sourcePath = allowedPath,
                checkpointPath = recent.CheckpointPath,
                checkpointDir = Path.GetDirectoryName(recent.CheckpointPath),
                timestampUtc = recent.Utc.ToString("yyyyMMdd_HHmmss"),
                method = recent.Method,
                reason,
                reused = true,
                debounceSec,
            };
        }

        string checkpointDir = CheckpointDirectoryFor(allowedPath);
        Directory.CreateDirectory(checkpointDir);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string fileName = Path.GetFileNameWithoutExtension(allowedPath);
        string ext = Path.GetExtension(allowedPath);
        string checkpointPath = Path.Combine(checkpointDir, $"{fileName}_{stamp}{ext}");

        string method = "file_copy";
        bool staged = false;

        // Prefer a live SaveAs-copy when the document is open so unsaved session state is staged.
        try
        {
            ISldWorks app = AttachSolidWorks(startIfMissing: false);
            ModelDoc2? openDoc = FindOpenDocumentByPath(app, allowedPath);
            if (openDoc is not null)
            {
                int errors = 0;
                int warnings = 0;
                bool savedCopy = openDoc.Extension.SaveAs(
                    checkpointPath,
                    0,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent
                    | (int)swSaveAsOptions_e.swSaveAsOptions_Copy,
                    null,
                    ref errors,
                    ref warnings);

                if (savedCopy && errors == 0 && File.Exists(checkpointPath))
                {
                    method = "save_as_copy";
                    staged = true;
                }
            }
        }
        catch
        {
            // Fall through to on-disk copy.
        }

        if (!staged)
        {
            File.Copy(allowedPath, checkpointPath, overwrite: false);
            method = "file_copy";
        }

        var entry = new CheckpointCacheEntry(checkpointPath, DateTime.UtcNow, method);
        RecentCheckpoints[allowedPath] = entry;

        return new
        {
            sourcePath = allowedPath,
            checkpointPath,
            checkpointDir,
            timestampUtc = stamp,
            method,
            reason,
            reused = false,
            debounceSec,
        };
    }

    private static string CheckpointDirectoryFor(string allowedPath)
    {
        string root = Path.GetDirectoryName(allowedPath) ?? allowedPath;
        return Path.Combine(root, ".checkpoints");
    }

    private static string InferRestoreTargetPath(string checkpointPath)
    {
        string file = Path.GetFileNameWithoutExtension(checkpointPath);
        string ext = Path.GetExtension(checkpointPath);
        // Expect {name}_{yyyyMMdd}_{HHmmss}
        var match = System.Text.RegularExpressions.Regex.Match(
            file,
            @"^(?<name>.+)_(?<date>\d{8})_(?<time>\d{6})$");
        if (!match.Success)
        {
            throw WorkerException.Validation(
                "RESTORE_TARGET_UNKNOWN",
                "Checkpoint filename is not a staged snapshot; pass path explicitly.",
                new Dictionary<string, object?> { ["checkpointPath"] = checkpointPath });
        }

        string? checkpointDir = Path.GetDirectoryName(checkpointPath);
        string? parent = checkpointDir is null ? null : Directory.GetParent(checkpointDir)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw WorkerException.Validation(
                "RESTORE_TARGET_UNKNOWN",
                "Could not infer restore target path; pass path explicitly.",
                new Dictionary<string, object?> { ["checkpointPath"] = checkpointPath });
        }

        return PathGuard.AssertAllowedPath(Path.Combine(parent, match.Groups["name"].Value + ext));
    }

    private static string? ResolveCheckpointSourcePath(JsonElement? args)
    {
        foreach (string key in new[]
                 {
                     "path",
                     "assembly_path",
                     "model_path",
                     "part_path",
                     "source_part_path",
                     "component_path",
                 })
        {
            string? value = StringArg(args, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        try
        {
            ISldWorks app = AttachSolidWorks(startIfMissing: false);
            ModelDoc2? doc = app.ActiveDoc as ModelDoc2;
            string? activePath = Try(() => doc?.GetPathName()) as string;
            if (!string.IsNullOrWhiteSpace(activePath))
            {
                return activePath;
            }
        }
        catch
        {
            // No active document.
        }

        return null;
    }

    private static ModelDoc2? FindOpenDocumentByPath(ISldWorks app, string path)
    {
        string full = Path.GetFullPath(path);
        object? docsObj = Try(() => app.GetDocuments());
        if (docsObj is not object[] docs)
        {
            ModelDoc2? active = app.ActiveDoc as ModelDoc2;
            string? activePath = Try(() => active?.GetPathName()) as string;
            if (!string.IsNullOrWhiteSpace(activePath)
                && string.Equals(Path.GetFullPath(activePath), full, StringComparison.OrdinalIgnoreCase))
            {
                return active;
            }

            return null;
        }

        foreach (object entry in docs)
        {
            if (entry is not ModelDoc2 doc)
            {
                continue;
            }

            string? docPath = Try(() => doc.GetPathName()) as string;
            if (!string.IsNullOrWhiteSpace(docPath)
                && string.Equals(Path.GetFullPath(docPath), full, StringComparison.OrdinalIgnoreCase))
            {
                return doc;
            }
        }

        return null;
    }
}
