using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

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

    private static object ConfirmAndSave(JsonElement? args)
    {
        bool looksGood = BoolArg(args, "looks_good", defaultValue: false);
        bool confirm = BoolArg(args, "confirm", defaultValue: false);
        if (!looksGood || !confirm)
        {
            throw WorkerException.Validation(
                "LOOKS_GOOD_REQUIRED",
                "confirm_and_save requires looks_good: true and confirm: true after the user visually approved the viewport.",
                new Dictionary<string, object?>
                {
                    ["looks_good"] = looksGood,
                    ["confirm"] = confirm,
                    ["ask_user"] = "Does this look good? If yes, call confirm_and_save with looks_good:true and confirm:true.",
                });
        }

        string? inputPath = StringArg(args, "path");
        string? previewPath = StringArg(args, "preview_path");
        double poseTolerance = DoubleArg(args, "pose_tolerance", 0.05);
        int maxHealAttempts = Math.Clamp((int)DoubleArg(args, "max_heal_attempts", 3), 1, 5);
        bool checkpoint = BoolArg(args, "checkpoint", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath)
            ? app.ActiveDoc as ModelDoc2
            : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to confirm_and_save.");
        }

        string? docPath = Try(() => doc.GetPathName()) as string;
        object? checkpointResult = null;
        if (checkpoint && !string.IsNullOrWhiteSpace(docPath))
        {
            checkpointResult = CheckpointDocument(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["path"] = docPath,
            }));
        }

        object? preview = null;
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            preview = Export(JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["path"] = docPath ?? inputPath,
                ["output_path"] = previewPath,
                ["keep_view"] = true,
            }));
        }

        var tracked = SnapshotFloatComponentTransforms(doc);
        var matesBefore = SnapshotMateHealth(doc);
        var healLog = new List<object>();
        var attempts = new List<object>();

        HealWarningLimitAngleMates(doc, healLog);

        bool poseStable = false;
        bool saved = false;
        int saveErrors = 0;
        int saveWarnings = 0;
        var matesAfter = matesBefore;
        List<object> jumped = [];

        for (int attempt = 1; attempt <= maxHealAttempts; attempt++)
        {
            // Lock the approved pose so Save/rebuild cannot branch-flip float components.
            var fixedNames = new List<string>();
            if (doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                IAssemblyDoc assembly = (IAssemblyDoc)doc;
                foreach (var entry in tracked)
                {
                    Component2? component = FindComponent(assembly, null, entry.Name);
                    if (component is null)
                    {
                        continue;
                    }

                    ApplyComponentTransformMatrix(component, entry.Matrix);
                    if (Try(() => component.Select4(false, null, false)) as bool? == true)
                    {
                        TryVoid(() => assembly.FixComponent());
                        fixedNames.Add(entry.Name);
                    }

                    doc.ClearSelection2(true);
                }
            }

            doc.EditRebuild3();
            HealWarningLimitAngleMates(doc, healLog);
            doc.EditRebuild3();

            saveErrors = 0;
            saveWarnings = 0;
            saved = doc.Save3(
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ref saveErrors,
                ref saveWarnings);

            // Restore float state after save.
            if (doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                IAssemblyDoc assembly = (IAssemblyDoc)doc;
                foreach (string name in fixedNames)
                {
                    Component2? component = FindComponent(assembly, null, name);
                    if (component is null)
                    {
                        continue;
                    }

                    if (Try(() => component.Select4(false, null, false)) as bool? == true)
                    {
                        TryVoid(() => assembly.UnfixComponent());
                    }

                    doc.ClearSelection2(true);
                }
            }

            doc.EditRebuild3();
            jumped = DiffComponentTransforms(doc, tracked, poseTolerance);
            matesAfter = SnapshotMateHealth(doc);
            int mateWarningCount = matesAfter.Count(m => m.ErrorCode is int code && code != 0);
            int limitWarningCount = matesAfter.Count(m =>
                m.Type is not null
                && m.Type.Contains("Limit", StringComparison.OrdinalIgnoreCase)
                && m.ErrorCode is int code
                && code != 0);
            if (limitWarningCount > 0 && jumped.Count == 0)
            {
                HealWarningLimitAngleMates(doc, healLog);
                doc.EditRebuild3();
                jumped = DiffComponentTransforms(doc, tracked, poseTolerance);
                matesAfter = SnapshotMateHealth(doc);
                mateWarningCount = matesAfter.Count(m => m.ErrorCode is int code && code != 0);
                limitWarningCount = matesAfter.Count(m =>
                    m.Type is not null
                    && m.Type.Contains("Limit", StringComparison.OrdinalIgnoreCase)
                    && m.ErrorCode is int code
                    && code != 0);
            }

            poseStable = jumped.Count == 0;
            bool limitsHealthy = limitWarningCount == 0;
            attempts.Add(new
            {
                attempt,
                saved,
                saveErrors,
                saveWarnings,
                poseStable,
                limitsHealthy,
                jumpedCount = jumped.Count,
                mateWarnings = mateWarningCount,
                limitWarnings = limitWarningCount,
                fixedDuringSave = fixedNames,
            });

            if (saved && saveErrors == 0 && poseStable && limitsHealthy)
            {
                break;
            }

            // Pose jumped, limit mates still warn, or save failed — restore approved transforms and re-seed mates.
            if (doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                IAssemblyDoc assembly = (IAssemblyDoc)doc;
                foreach (var entry in tracked)
                {
                    Component2? component = FindComponent(assembly, null, entry.Name);
                    if (component is null)
                    {
                        continue;
                    }

                    ApplyComponentTransformMatrix(component, entry.Matrix);
                }

                doc.EditRebuild3();
                HealWarningLimitAngleMates(doc, healLog);
            }
        }

        matesAfter = SnapshotMateHealth(doc);
        jumped = DiffComponentTransforms(doc, tracked, poseTolerance);
        poseStable = jumped.Count == 0;
        bool finalLimitsHealthy = matesAfter
            .Where(m => m.Type is not null && m.Type.Contains("Limit", StringComparison.OrdinalIgnoreCase))
            .All(m => m.ErrorCode is null or 0);
        var otherMateWarnings = matesAfter
            .Where(m =>
                (m.Type is null || !m.Type.Contains("Limit", StringComparison.OrdinalIgnoreCase))
                && m.ErrorCode is int code
                && code != 0)
            .Select(DescribeMateHealth)
            .ToArray();
        bool ok = saved && saveErrors == 0 && poseStable && finalLimitsHealthy;
        return new
        {
            document = DescribeDocument(doc),
            ok,
            saved,
            saveErrors,
            saveWarnings,
            poseStable,
            limitsHealthy = finalLimitsHealthy,
            looksGood = true,
            askUserNext = ok
                ? (otherMateWarnings.Length > 0
                    ? "Pose saved and limit mates healthy, but other mate warnings remain — ask the user if those yellow marks are acceptable."
                    : null)
                : "Save/pose/limit-mate health still unstable. Ask the user to re-approve the viewport, then retry confirm_and_save.",
            checkpoint = checkpointResult,
            preview,
            attempts,
            jumped,
            healLog,
            otherMateWarnings,
            matesBefore = matesBefore.Select(DescribeMateHealth).ToArray(),
            matesAfter = matesAfter.Select(DescribeMateHealth).ToArray(),
            trackedComponents = tracked.Select(t => t.Name).ToArray(),
        };
    }

    private sealed record TrackedTransform(string Name, double[] Matrix);

    private sealed record MateHealth(
        string Name,
        string? Type,
        int? ErrorCode,
        bool ErrorIsWarning,
        double? AngleDeg,
        double? MinAngleDeg,
        double? MaxAngleDeg,
        bool? FlipDimension);

    private static object DescribeMateHealth(MateHealth mate) => new
    {
        name = mate.Name,
        type = mate.Type,
        errorCode = mate.ErrorCode,
        errorIsWarning = mate.ErrorIsWarning,
        angleDeg = mate.AngleDeg,
        minAngleDeg = mate.MinAngleDeg,
        maxAngleDeg = mate.MaxAngleDeg,
        flipDimension = mate.FlipDimension,
    };

    private static List<TrackedTransform> SnapshotFloatComponentTransforms(ModelDoc2 doc)
    {
        var tracked = new List<TrackedTransform>();
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return tracked;
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        object? componentsObj = Try(() => assembly.GetComponents(false));
        if (componentsObj is not object[] components)
        {
            return tracked;
        }

        foreach (object entry in components)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            string? name = Try(() => component.Name2) as string;
            bool suppressed = Try(() => component.IsSuppressed()) as bool? ?? false;
            bool isFixed = Try(() => component.IsFixed()) as bool? ?? false;
            if (string.IsNullOrWhiteSpace(name) || suppressed || isFixed)
            {
                continue;
            }

            try
            {
                tracked.Add(new TrackedTransform(name!, ReadComponentTransformMatrix(component)));
            }
            catch
            {
                // Skip components without readable transforms.
            }
        }

        return tracked;
    }

    private static List<MateHealth> SnapshotMateHealth(ModelDoc2 doc)
    {
        var mates = new List<MateHealth>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is null)
        {
            return mates;
        }

        object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
        int guard = 0;
        while (subFeature is Feature current && guard++ < 500)
        {
            string? name = Try(() => current.Name) as string;
            string? type = Try(() => current.GetTypeName2()) as string;
            bool errorIsWarning = false;
            int? errorCode = Try(() => current.GetErrorCode2(out errorIsWarning)) as int?;
            double? angleDeg = null;
            double? minDeg = null;
            double? maxDeg = null;
            bool? flip = null;
            if (type is not null && type.Contains("Limit", StringComparison.OrdinalIgnoreCase)
                && Try(() => current.GetDefinition()) is IAngleMateFeatureData angleMate)
            {
                angleDeg = angleMate.Angle * 180.0 / Math.PI;
                minDeg = angleMate.MinimumAngle * 180.0 / Math.PI;
                maxDeg = angleMate.MaximumAngle * 180.0 / Math.PI;
                flip = angleMate.FlipDimension;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                mates.Add(new MateHealth(
                    name!,
                    type,
                    errorCode,
                    errorIsWarning,
                    angleDeg,
                    minDeg,
                    maxDeg,
                    flip));
            }

            subFeature = Try(() => current.GetNextSubFeature());
        }

        return mates;
    }

    private static void HealWarningLimitAngleMates(ModelDoc2 doc, List<object> healLog)
    {
        foreach (MateHealth mate in SnapshotMateHealth(doc))
        {
            if (mate.Type is null
                || !mate.Type.Contains("Limit", StringComparison.OrdinalIgnoreCase)
                || mate.ErrorCode is null
                || mate.ErrorCode == 0
                || mate.MinAngleDeg is null
                || mate.MaxAngleDeg is null
                || mate.AngleDeg is null)
            {
                continue;
            }

            Feature? feature = FindFeatureByName(doc, mate.Name);
            if (feature is null)
            {
                continue;
            }

            double angle = mate.AngleDeg.Value;
            if (angle < mate.MinAngleDeg.Value)
            {
                angle = mate.MinAngleDeg.Value;
            }
            else if (angle > mate.MaxAngleDeg.Value)
            {
                angle = mate.MaxAngleDeg.Value;
            }

            bool ok = TryUpdateLimitAngleMate(
                feature,
                doc,
                mate.MinAngleDeg.Value,
                mate.MaxAngleDeg.Value,
                angle,
                mate.FlipDimension);
            doc.EditRebuild3();
            bool warn = false;
            int? after = Try(() => feature.GetErrorCode2(out warn)) as int?;
            healLog.Add(new
            {
                mate = mate.Name,
                healed = ok,
                angleDeg = angle,
                errorBefore = mate.ErrorCode,
                errorAfter = after,
                errorIsWarningAfter = warn,
            });
        }
    }

    private static List<object> DiffComponentTransforms(
        ModelDoc2 doc,
        List<TrackedTransform> expected,
        double tolerance)
    {
        var jumped = new List<object>();
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return jumped;
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        foreach (TrackedTransform entry in expected)
        {
            Component2? component = FindComponent(assembly, null, entry.Name);
            if (component is null)
            {
                jumped.Add(new { component = entry.Name, reason = "missing" });
                continue;
            }

            double[] actual;
            try
            {
                actual = ReadComponentTransformMatrix(component);
            }
            catch
            {
                jumped.Add(new { component = entry.Name, reason = "unreadable" });
                continue;
            }

            double rotDelta = 0;
            for (int i = 0; i < 9 && i < entry.Matrix.Length && i < actual.Length; i++)
            {
                rotDelta += Math.Abs(entry.Matrix[i] - actual[i]);
            }

            double transDelta = 0;
            for (int i = 9; i < 12 && i < entry.Matrix.Length && i < actual.Length; i++)
            {
                transDelta += Math.Abs(entry.Matrix[i] - actual[i]);
            }

            if (rotDelta > tolerance || transDelta > tolerance)
            {
                jumped.Add(new
                {
                    component = entry.Name,
                    reason = "pose_changed",
                    rotationDelta = rotDelta,
                    translationDelta = transDelta,
                });
            }
        }

        return jumped;
    }
}
