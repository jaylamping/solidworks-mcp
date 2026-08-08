using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    /// <summary>
    /// Strict reusable hinge builder: suppress caller-listed grounding mates, create coaxial +
    /// mount plane mates, create an advanced axis-backed LimitAngle on the requested branch,
    /// force-rebuild, probe motion/clearance, and restore the pre-change checkpoint on failure.
    /// Never accepts no-axis or standard-angle fallbacks. Does not save/lock-in.
    /// </summary>
    private static object CreateHingeLimit(JsonElement? args)
    {
        const int AngleMateReferenceMark = 67108864;

        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string fixedComponentName = RequiredStringArg(args, "fixed_component");
        string movingComponentName = RequiredStringArg(args, "moving_component");
        string fixedAxisRef = RequiredStringArg(args, "fixed_axis_ref");
        string movingAxisRef = RequiredStringArg(args, "moving_axis_ref");
        string fixedMountPlaneRef = RequiredStringArg(args, "fixed_mount_plane_ref");
        string movingMountPlaneRef = RequiredStringArg(args, "moving_mount_plane_ref");
        string fixedAnglePlaneRef = RequiredStringArg(args, "fixed_angle_plane_ref");
        string movingAnglePlaneRef = RequiredStringArg(args, "moving_angle_plane_ref");
        double minDeg = DoubleArg(args, "min_angle_deg", double.NaN);
        double maxDeg = DoubleArg(args, "max_angle_deg", double.NaN);
        double parkDeg = DoubleArg(args, "park_angle_deg", double.NaN);
        string alignText = RequiredStringArg(args, "align");
        bool flipDimension = BoolArg(args, "flip_dimension", defaultValue: false);
        string[] releaseMates = StringArrayArg(args, "release_mates", required: true);
        int axisAlign = ParseAlignText(StringArg(args, "axis_align") ?? "aligned");
        int mountAlign = ParseAlignText(StringArg(args, "mount_align") ?? "aligned");
        int limitAlign = ParseAlignText(alignText);
        double[] probeAngles = OptionalDoubleArrayArg(args, "probe_angles_deg");
        string? clearanceComponentName = StringArg(args, "clearance_component");
        string clearanceAxis = (StringArg(args, "clearance_axis") ?? "y").Trim().ToLowerInvariant();
        bool hasClearanceMax = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("clearance_max", out _);
        bool hasClearanceMin = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("clearance_min", out _);
        double clearanceMax = hasClearanceMax ? DoubleArg(args, "clearance_max", 0) : double.NaN;
        double clearanceMin = hasClearanceMin ? DoubleArg(args, "clearance_min", 0) : double.NaN;

        if (double.IsNaN(minDeg) || double.IsNaN(maxDeg) || double.IsNaN(parkDeg))
        {
            throw new ArgumentException("min_angle_deg, max_angle_deg, and park_angle_deg are required.");
        }

        if (minDeg > maxDeg)
        {
            (minDeg, maxDeg) = (maxDeg, minDeg);
        }

        parkDeg = Math.Clamp(parkDeg, minDeg, maxDeg);
        if (clearanceAxis is not ("x" or "y" or "z"))
        {
            throw new ArgumentException("clearance_axis must be one of: x, y, z.");
        }

        if (!string.IsNullOrWhiteSpace(clearanceComponentName)
            && !hasClearanceMax
            && !hasClearanceMin)
        {
            throw new ArgumentException(
                "When clearance_component is set, provide clearance_max and/or clearance_min.");
        }

        if (probeAngles.Length == 0)
        {
            probeAngles =
            [
                parkDeg,
                minDeg,
                (minDeg + maxDeg) / 2.0,
                maxDeg,
            ];
        }

        object checkpoint = CreateDocumentCheckpoint(inputPath, reason: "create_hinge_limit", force: true);
        string? checkpointPath = checkpoint.GetType().GetProperty("checkpointPath")?.GetValue(checkpoint) as string;
        if (string.IsNullOrWhiteSpace(checkpointPath))
        {
            throw new InvalidOperationException("Failed to stage pre-change checkpoint for create_hinge_limit.");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("create_hinge_limit requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        var stages = new List<object>();
        string? failureStage = null;
        string? failureReason = null;
        bool restored = false;
        object? restoreResult = null;

        try
        {
            Component2? fixedComponent = FindComponent(assembly, null, fixedComponentName)
                ?? throw new InvalidOperationException($"Fixed component not found: {fixedComponentName}");
            Component2? movingComponent = FindComponent(assembly, null, movingComponentName)
                ?? throw new InvalidOperationException($"Moving component not found: {movingComponentName}");

            if (Try(() => movingComponent.IsFixed()) as bool? == true)
            {
                throw new InvalidOperationException(
                    $"Moving component is Fixed and cannot hinge: {movingComponentName}");
            }

            stages.Add(new
            {
                stage = "validate_components",
                ok = true,
                fixedComponent = Try(() => fixedComponent.Name2),
                movingComponent = Try(() => movingComponent.Name2),
                movingFixed = false,
            });

            List<object> released = [];
            foreach (string mateName in releaseMates)
            {
                Feature? mateFeature = FindFeatureByName(doc, mateName);
                if (mateFeature is null)
                {
                    released.Add(new { mateName, ok = false, error = "not_found" });
                    continue;
                }

                bool alreadySuppressed = Try(() => mateFeature.IsSuppressed()) as bool? ?? false;
                if (alreadySuppressed)
                {
                    released.Add(new { mateName, ok = true, suppressed = true, alreadySuppressed = true });
                    continue;
                }

                bool ok = Try(() => mateFeature.SetSuppression2(
                    (int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swAllConfiguration,
                    null)) as bool? ?? false;
                released.Add(new
                {
                    mateName,
                    ok,
                    suppressed = Try(() => mateFeature.IsSuppressed()) as bool?,
                    alreadySuppressed = false,
                });
                if (!ok)
                {
                    throw new InvalidOperationException($"Failed to suppress release mate: {mateName}");
                }
            }

            doc.EditRebuild3();
            stages.Add(new { stage = "release_mates", ok = true, released });

            HashSet<string> matesBefore = CollectMateHealthEntries(doc)
                .Select(m => m.Name)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            (Feature? axisMate, object axisDiag) = CreateHingeCoincidentMate(
                doc,
                assembly,
                fixedComponent,
                movingComponent,
                fixedAxisRef,
                movingAxisRef,
                axisAlign,
                preferAxis: true);
            if (axisMate is null)
            {
                stages.Add(new { stage = "coaxial_mate", ok = false, diagnostics = axisDiag });
                throw new InvalidOperationException(
                    $"Failed to create coaxial axis mate ({fixedAxisRef} ↔ {movingAxisRef}): {JsonSerializer.Serialize(axisDiag)}");
            }

            stages.Add(new
            {
                stage = "coaxial_mate",
                ok = true,
                mateName = Try(() => axisMate.Name),
                fixedAxisRef,
                movingAxisRef,
                align = AlignText(axisAlign),
                diagnostics = axisDiag,
            });

            (Feature? mountMate, object mountDiag) = CreateHingeCoincidentMate(
                doc,
                assembly,
                fixedComponent,
                movingComponent,
                fixedMountPlaneRef,
                movingMountPlaneRef,
                mountAlign,
                preferAxis: false);
            if (mountMate is null)
            {
                stages.Add(new { stage = "mount_mate", ok = false, diagnostics = mountDiag });
                throw new InvalidOperationException(
                    $"Failed to create mounting-plane mate ({fixedMountPlaneRef} ↔ {movingMountPlaneRef}): {JsonSerializer.Serialize(mountDiag)}");
            }

            stages.Add(new
            {
                stage = "mount_mate",
                ok = true,
                mateName = Try(() => mountMate.Name),
                fixedMountPlaneRef,
                movingMountPlaneRef,
                align = AlignText(mountAlign),
                diagnostics = mountDiag,
            });

            doc.ClearSelection2(true);
            if (!SelectComponentPlaneOrAxisStrict(doc, fixedComponent, fixedAnglePlaneRef, append: false, mark: 1)
                && !SelectComponentMateEntity(doc, fixedComponent, fixedAnglePlaneRef, 0, append: false, mark: 1))
            {
                throw new InvalidOperationException(
                    $"Failed to select fixed angle plane '{fixedAnglePlaneRef}' on {fixedComponentName}.");
            }

            if (!SelectComponentPlaneOrAxisStrict(doc, movingComponent, movingAnglePlaneRef, append: true, mark: 1)
                && !SelectComponentMateEntity(doc, movingComponent, movingAnglePlaneRef, 0, append: true, mark: 1))
            {
                throw new InvalidOperationException(
                    $"Failed to select moving angle plane '{movingAnglePlaneRef}' on {movingComponentName}.");
            }

            bool axisSelected =
                SelectComponentPlaneOrAxisStrict(
                    doc,
                    fixedComponent,
                    fixedAxisRef,
                    append: true,
                    mark: AngleMateReferenceMark)
                || SelectComponentAxisFeature(
                    doc,
                    fixedComponent,
                    fixedAxisRef,
                    append: true,
                    mark: AngleMateReferenceMark)
                || SelectComponentPlaneOrAxisStrict(
                    doc,
                    movingComponent,
                    movingAxisRef,
                    append: true,
                    mark: AngleMateReferenceMark)
                || SelectComponentAxisFeature(
                    doc,
                    movingComponent,
                    movingAxisRef,
                    append: true,
                    mark: AngleMateReferenceMark);

            if (!axisSelected)
            {
                throw new InvalidOperationException(
                    "Failed to select a reference axis for LimitAngle. Axis-backed CreateMate is required.");
            }

            SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
            int selectedCount = selectionMgr is null
                ? 0
                : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

            List<object> planeEntities = [];
            object? axisEntity = null;
            if (selectionMgr is not null)
            {
                for (int i = 1; i <= Math.Max(selectedCount, 1); i++)
                {
                    int? selType = Try(() => selectionMgr.GetSelectedObjectType3(i, -1)) as int?;
                    object? selObj = Try(() => selectionMgr.GetSelectedObject6(i, -1));
                    if (selObj is null || selType is null)
                    {
                        continue;
                    }

                    if (selType == (int)swSelectType_e.swSelDATUMPLANES && planeEntities.Count < 2)
                    {
                        planeEntities.Add(selObj);
                    }
                    else if (selType == (int)swSelectType_e.swSelDATUMAXES && axisEntity is null)
                    {
                        axisEntity = selObj;
                    }
                    else if (selType is (int)swSelectType_e.swSelFACES
                        or (int)swSelectType_e.swSelDATUMPLANES)
                    {
                        if (planeEntities.Count < 2)
                        {
                            planeEntities.Add(selObj);
                        }
                    }
                }
            }

            object? entity1 = planeEntities.Count > 0 ? planeEntities[0] : null;
            object? entity2 = planeEntities.Count > 1 ? planeEntities[1] : null;
            entity1 ??= Try(() => selectionMgr?.GetSelectedObject6(1, 1));
            entity2 ??= Try(() => selectionMgr?.GetSelectedObject6(2, 1));
            axisEntity ??= Try(() => selectionMgr?.GetSelectedObject6(1, AngleMateReferenceMark));
            doc.ClearSelection2(true);

            if (entity1 is null || entity2 is null)
            {
                throw new InvalidOperationException("LimitAngle plane entities were not captured from selection.");
            }

            if (axisEntity is null)
            {
                throw new InvalidOperationException(
                    "LimitAngle axis entity missing. Refusing no-axis / standard-angle fallbacks.");
            }

            object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateANGLE));
            if (mateDataObj is not IAngleMateFeatureData angleMate)
            {
                throw new InvalidOperationException("CreateMateData(swMateANGLE) failed.");
            }

            angleMate.IsAdvancedMate = true;
            angleMate.EntitiesToMate = new object[] { entity1, entity2 };
            angleMate.ReferenceEntity = axisEntity;
            angleMate.Angle = parkDeg * Math.PI / 180.0;
            angleMate.MinimumAngle = minDeg * Math.PI / 180.0;
            angleMate.MaximumAngle = maxDeg * Math.PI / 180.0;
            angleMate.FlipDimension = flipDimension;
            angleMate.MateAlignment = limitAlign;

            Feature? limitMate = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
            int? createStatus = mateDataObj is IMateFeatureData mateData
                ? Try(() => mateData.ErrorStatus) as int?
                : null;
            if (limitMate is null)
            {
                throw new InvalidOperationException(
                    $"Strict axis-backed LimitAngle CreateMate failed (status={createStatus}). "
                    + "No NoAxis/standard-angle fallbacks are attempted.");
            }

            string? limitMateName = Try(() => limitMate.Name) as string;
            stages.Add(new
            {
                stage = "limit_angle",
                ok = true,
                mateName = limitMateName,
                method = "CreateMateLimitAxisStrict",
                createStatus,
                align = AlignText(limitAlign),
                flipDimension,
                minAngleDeg = minDeg,
                maxAngleDeg = maxDeg,
                parkAngleDeg = parkDeg,
                hasAxisEntity = true,
            });

            bool forceRebuildOk = ForceRebuildDocument(doc, out _);
            List<MateHealthEntry> postForceMates = CollectMateHealthEntries(doc);
            List<object> mateFailures = MateFailuresFrom(postForceMates);
            if (!forceRebuildOk || mateFailures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Post-construction force rebuild failed or mates unhealthy "
                    + $"(forceRebuildOk={forceRebuildOk}, failures={mateFailures.Count}).");
            }

            stages.Add(new
            {
                stage = "force_rebuild_health",
                ok = true,
                forceRebuildOk,
                mateFailures,
            });

            if (string.IsNullOrWhiteSpace(limitMateName))
            {
                limitMateName = postForceMates
                    .Where(m => m.Name is not null && !matesBefore.Contains(m.Name))
                    .Select(m => m.Name)
                    .LastOrDefault(name =>
                        name is not null
                        && name.StartsWith("LimitAngle", StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(limitMateName))
            {
                throw new InvalidOperationException("LimitAngle was created but its feature name could not be resolved.");
            }

            Feature? limitFeature = FindFeatureByName(doc, limitMateName)
                ?? throw new InvalidOperationException($"LimitAngle feature missing after create: {limitMateName}");

            List<object> samples = [];
            foreach (double requestedDeg in probeAngles)
            {
                IAngleMateFeatureData? editable = Try(() => limitFeature.GetDefinition()) as IAngleMateFeatureData
                    ?? throw new InvalidOperationException($"Unable to edit LimitAngle definition: {limitMateName}");

                double clamped = Math.Clamp(requestedDeg, minDeg, maxDeg);
                editable.IsAdvancedMate = true;
                editable.MinimumAngle = minDeg * Math.PI / 180.0;
                editable.MaximumAngle = maxDeg * Math.PI / 180.0;
                editable.Angle = clamped * Math.PI / 180.0;
                editable.FlipDimension = flipDimension;

                bool modifyOk = Try(() => limitFeature.ModifyDefinition(editable, doc, null)) as bool? ?? false;
                bool sampleForceOk = ForceRebuildDocument(doc, out _);
                List<MateHealthEntry> sampleMates = CollectMateHealthEntries(doc);
                List<object> sampleFailures = MateFailuresFrom(sampleMates);
                MateHealthEntry? limitHealth = sampleMates.FirstOrDefault(m =>
                    string.Equals(m.Name, limitMateName, StringComparison.OrdinalIgnoreCase));

                double? clearanceCenter = null;
                bool clearanceOk = true;
                string? clearanceError = null;
                object? clearanceBox = null;
                if (!string.IsNullOrWhiteSpace(clearanceComponentName))
                {
                    Component2? clearanceComponent = FindComponent(assembly, null, clearanceComponentName)
                        ?? throw new InvalidOperationException(
                            $"Clearance component not found: {clearanceComponentName}");
                    object? boxObj = Try(() => clearanceComponent.GetBox(false, false));
                    clearanceBox = Normalize(boxObj);
                    if (boxObj is double[] box && box.Length >= 6)
                    {
                        clearanceCenter = clearanceAxis switch
                        {
                            "x" => (box[0] + box[3]) / 2.0,
                            "y" => (box[1] + box[4]) / 2.0,
                            _ => (box[2] + box[5]) / 2.0,
                        };

                        if (hasClearanceMax && clearanceCenter > clearanceMax)
                        {
                            clearanceOk = false;
                            clearanceError =
                                $"clearance center {clearanceAxis}={clearanceCenter} > max {clearanceMax}";
                        }
                        else if (hasClearanceMin && clearanceCenter < clearanceMin)
                        {
                            clearanceOk = false;
                            clearanceError =
                                $"clearance center {clearanceAxis}={clearanceCenter} < min {clearanceMin}";
                        }
                    }
                    else
                    {
                        clearanceOk = false;
                        clearanceError = "clearance bounding box unavailable";
                    }
                }

                bool sampleOk = modifyOk
                    && sampleForceOk
                    && sampleFailures.Count == 0
                    && (limitHealth?.ErrorCode ?? -1) == 0
                    && clearanceOk;

                samples.Add(new
                {
                    requestedDeg,
                    drivenDeg = clamped,
                    modifyOk,
                    forceRebuildOk = sampleForceOk,
                    mateFailures = sampleFailures,
                    limitErrorCode = limitHealth?.ErrorCode,
                    clearanceComponent = clearanceComponentName,
                    clearanceAxis,
                    clearanceCenter,
                    clearanceBox,
                    clearanceOk,
                    clearanceError,
                    ok = sampleOk,
                });

                if (!sampleOk)
                {
                    throw new InvalidOperationException(
                        $"Probe sample failed at {requestedDeg}°"
                        + (clearanceError is null ? "" : $": {clearanceError}")
                        + (sampleFailures.Count == 0
                            ? ""
                            : $"; mateFailures={sampleFailures.Count}"));
                }
            }

            // Park after successful sweep.
            IAngleMateFeatureData? parkEditable = Try(() => limitFeature.GetDefinition()) as IAngleMateFeatureData;
            if (parkEditable is not null)
            {
                parkEditable.IsAdvancedMate = true;
                parkEditable.MinimumAngle = minDeg * Math.PI / 180.0;
                parkEditable.MaximumAngle = maxDeg * Math.PI / 180.0;
                parkEditable.Angle = parkDeg * Math.PI / 180.0;
                parkEditable.FlipDimension = flipDimension;
                Try(() => limitFeature.ModifyDefinition(parkEditable, doc, null));
                ForceRebuildDocument(doc, out _);
            }

            List<MateHealthEntry> finalMates = CollectMateHealthEntries(doc);
            List<object> finalFailures = MateFailuresFrom(finalMates);
            stages.Add(new { stage = "motion_clearance_probe", ok = true, samples });

            return new
            {
                document = DescribeDocument(doc),
                ok = true,
                restored = false,
                checkpoint,
                checkpointPath,
                fixedComponent = Try(() => fixedComponent.Name2),
                movingComponent = Try(() => movingComponent.Name2),
                axisMateName = Try(() => axisMate.Name),
                mountMateName = Try(() => mountMate.Name),
                limitMateName,
                align = AlignText(limitAlign),
                flipDimension,
                minAngleDeg = minDeg,
                maxAngleDeg = maxDeg,
                parkAngleDeg = parkDeg,
                releaseMates = released,
                stages,
                samples,
                mateFailures = finalFailures,
                mateHealthy = finalFailures.Count == 0,
                mateCount = CountAssemblyMates(doc),
                note = "Construction/motion validated. Call confirm_and_save after visual approval; this command does not save.",
            };
        }
        catch (Exception ex)
        {
            failureStage ??= "create_hinge_limit";
            failureReason = ex.Message;
            stages.Add(new { stage = failureStage, ok = false, error = failureReason });

            try
            {
                restoreResult = RestoreFromCheckpoint(JsonSerializer.SerializeToElement(new
                {
                    checkpoint_path = checkpointPath,
                    path = inputPath,
                    close_open_document = true,
                }));
                restored = true;
            }
            catch (Exception restoreEx)
            {
                restoreResult = new { ok = false, error = restoreEx.Message };
                restored = false;
            }

            return new
            {
                document = new { path = inputPath },
                ok = false,
                restored,
                checkpoint,
                checkpointPath,
                failureStage,
                failureReason,
                stages,
                restoreResult,
                note = "Failed closed: pre-change checkpoint restore attempted. Inspect stages/restoreResult before retrying.",
            };
        }
    }

    private static bool SelectHingeMateEntity(
        ModelDoc2 doc,
        Component2 component,
        string referenceName,
        bool append,
        int mark,
        bool preferAxis)
    {
        if (preferAxis)
        {
            return SelectComponentPlaneOrAxisStrict(doc, component, referenceName, append, mark)
                || SelectComponentAxisFeature(doc, component, referenceName, append, mark)
                || SelectComponentReference(doc, component, referenceName, append, mark)
                || SelectComponentMateEntity(doc, component, referenceName, 0, append, mark);
        }

        return SelectComponentPlaneOrAxisStrict(doc, component, referenceName, append, mark)
            || SelectComponentReference(doc, component, referenceName, append, mark)
            || SelectComponentMateEntity(doc, component, referenceName, 0, append, mark);
    }

    private static (object? Entity1, object? Entity2, int SelectedCount, int? Type1, int? Type2) CaptureTwoMateEntities(
        ModelDoc2 doc,
        bool preferAxis)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int selectedCount = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        List<object> preferred = [];
        List<object> fallback = [];
        int? type1 = null;
        int? type2 = null;
        if (selectionMgr is not null)
        {
            for (int i = 1; i <= Math.Max(selectedCount, 1); i++)
            {
                int? selType = Try(() => selectionMgr.GetSelectedObjectType3(i, -1)) as int?;
                object? selObj = Try(() => selectionMgr.GetSelectedObject6(i, -1));
                if (selObj is null || selType is null)
                {
                    continue;
                }

                type1 ??= selType;
                if (preferred.Count + fallback.Count == 1)
                {
                    type2 ??= selType;
                }

                bool isAxis = selType == (int)swSelectType_e.swSelDATUMAXES;
                bool isPlane = selType is (int)swSelectType_e.swSelDATUMPLANES
                    or (int)swSelectType_e.swSelFACES;
                if (preferAxis ? isAxis : isPlane)
                {
                    preferred.Add(selObj);
                }
                else
                {
                    fallback.Add(selObj);
                }
            }
        }

        List<object> chosen = preferred.Count >= 2
            ? preferred
            : preferred.Concat(fallback).ToList();
        object? entity1 = chosen.Count > 0 ? chosen[0] : Try(() => selectionMgr?.GetSelectedObject6(1, 1));
        object? entity2 = chosen.Count > 1 ? chosen[1] : Try(() => selectionMgr?.GetSelectedObject6(1, 2));
        return (entity1, entity2, selectedCount, type1, type2);
    }

    private static Feature? CreateMateFromCapturedEntities(
        IAssemblyDoc assembly,
        int mateType,
        int mateAlign,
        object entity1,
        object entity2)
    {
        object? mateDataObj = Try(() => assembly.CreateMateData(mateType));
        if (mateDataObj is null)
        {
            return null;
        }

        object[] entities = [entity1, entity2];
        switch (mateDataObj)
        {
            case ICoincidentMateFeatureData coincident:
                coincident.EntitiesToMate = entities;
                coincident.MateAlignment = mateAlign;
                break;
            case IParallelMateFeatureData parallel:
                parallel.EntitiesToMate = entities;
                parallel.MateAlignment = mateAlign;
                break;
            case IConcentricMateFeatureData concentric:
                concentric.EntitiesToMate = entities;
                concentric.MateAlignment = mateAlign;
                break;
            default:
                return null;
        }

        return Try(() => assembly.CreateMate(mateDataObj)) as Feature;
    }

    private static (Feature? Mate, object Diagnostics) CreateHingeCoincidentMate(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        Component2 second,
        string ref1,
        string ref2,
        int mateAlign,
        bool preferAxis)
    {
        doc.ClearSelection2(true);
        bool selected1 = SelectHingeMateEntity(doc, first, ref1, append: false, mark: 1, preferAxis);
        bool selected2 = SelectHingeMateEntity(doc, second, ref2, append: true, mark: 2, preferAxis);
        (object? entity1, object? entity2, int selectedCount, int? type1, int? type2) =
            CaptureTwoMateEntities(doc, preferAxis);
        doc.ClearSelection2(true);

        if (!selected1 || !selected2 || entity1 is null || entity2 is null)
        {
            return (null, new
            {
                error = "selection_failed",
                selected1,
                selected2,
                selectedCount,
                type1,
                type2,
                hasEntity1 = entity1 is not null,
                hasEntity2 = entity2 is not null,
                ref1,
                ref2,
            });
        }

        HashSet<string> before = CollectMateHealthEntries(doc)
            .Select(m => m.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var attempts = new List<object>();
        int[] mateTypes = preferAxis
            ?
            [
                (int)swMateType_e.swMateCOINCIDENT,
                (int)swMateType_e.swMateCONCENTRIC,
            ]
            :
            [
                (int)swMateType_e.swMateCOINCIDENT,
                (int)swMateType_e.swMatePARALLEL,
            ];
        int[] aligns = mateAlign == (int)swMateAlign_e.swMateAlignALIGNED
            ?
            [
                (int)swMateAlign_e.swMateAlignALIGNED,
                (int)swMateAlign_e.swMateAlignANTI_ALIGNED,
            ]
            :
            [
                (int)swMateAlign_e.swMateAlignANTI_ALIGNED,
                (int)swMateAlign_e.swMateAlignALIGNED,
            ];

        foreach (int mateType in mateTypes)
        {
            foreach (int align in aligns)
            {
                Feature? viaCaptured = CreateMateFromCapturedEntities(
                    assembly,
                    mateType,
                    align,
                    entity1,
                    entity2);
                if (viaCaptured is not null)
                {
                    doc.EditRebuild3();
                    attempts.Add(new
                    {
                        mateType,
                        align = AlignText(align),
                        method = "CreateMateCaptured",
                        ok = true,
                        mateName = Try(() => viaCaptured.Name),
                    });
                    return (viaCaptured, new { attempts, selectedCount, type1, type2 });
                }

                // Fallback: clean two-entity selection + AddMate5.
                doc.ClearSelection2(true);
                SelectHingeMateEntity(doc, first, ref1, append: false, mark: 1, preferAxis);
                SelectHingeMateEntity(doc, second, ref2, append: true, mark: 2, preferAxis);
                (bool created, string method, int mateError, bool alreadyConstrained) = TryCreateMate(
                    assembly,
                    doc,
                    mateType,
                    align);
                doc.EditRebuild3();
                Feature? createdFeature = CollectMateHealthEntries(doc)
                    .Where(m => m.Name is not null && !before.Contains(m.Name))
                    .Select(m => FindFeatureByName(doc, m.Name!))
                    .FirstOrDefault(f => f is not null);

                attempts.Add(new
                {
                    mateType,
                    align = AlignText(align),
                    method,
                    created,
                    alreadyConstrained,
                    mateError,
                    mateName = Try(() => createdFeature?.Name),
                });

                if (createdFeature is not null)
                {
                    return (createdFeature, new { attempts, selectedCount, type1, type2 });
                }

                if (alreadyConstrained || mateError == 4)
                {
                    Feature? existing = before
                        .Select(name => FindFeatureByName(doc, name))
                        .FirstOrDefault(feature => feature is not null);
                    if (existing is not null)
                    {
                        return (existing, new
                        {
                            attempts,
                            selectedCount,
                            type1,
                            type2,
                            alreadyConstrained = true,
                        });
                    }
                }
            }
        }

        return (null, new
        {
            error = "create_failed",
            attempts,
            selectedCount,
            type1,
            type2,
            hasEntity1 = true,
            hasEntity2 = true,
            ref1,
            ref2,
        });
    }

    private static int ParseAlignText(string align) =>
        align.Equals("anti_aligned", StringComparison.OrdinalIgnoreCase)
            ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED
            : (int)swMateAlign_e.swMateAlignALIGNED;

    private static string AlignText(int align) =>
        align == (int)swMateAlign_e.swMateAlignANTI_ALIGNED ? "anti_aligned" : "aligned";
}
