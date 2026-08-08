using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object ProbePartFeatureGeometry(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("probe_part_feature_geometry requires a part document.");
        }

        string featureName = RequiredStringArg(args, "feature_name");
        Feature feature = FindFeatureByName(doc, featureName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {featureName}",
                new Dictionary<string, object?> { ["feature_name"] = featureName });

        var displayDimensions = new List<object>();
        object? displayDimensionObj = Try(() => feature.GetFirstDisplayDimension());
        int dimGuard = 0;
        while (displayDimensionObj is not null && dimGuard++ < 100)
        {
            if (displayDimensionObj is not DisplayDimension displayDimension)
            {
                break;
            }

            Dimension? dimension = Try(() => displayDimension.GetDimension2(0)) as Dimension;
            if (dimension is not null)
            {
                displayDimensions.Add(new
                {
                    name = Try(() => dimension.FullName),
                    shortName = Try(() => dimension.Name),
                    systemValueM = Try(() => dimension.SystemValue),
                });
            }

            displayDimensionObj = Try(() => feature.GetNextDisplayDimension(displayDimension));
        }

        var parameters = new List<object>();
        foreach (string candidate in new[]
                 {
                     $"D1@{featureName}", $"D2@{featureName}", $"D3@{featureName}", $"D4@{featureName}",
                     $"D5@{featureName}", $"D6@{featureName}", $"D7@{featureName}", $"D8@{featureName}",
                 })
        {
            Dimension? parameter = Try(() => doc.Parameter(candidate)) as Dimension;
            if (parameter is null)
            {
                continue;
            }

            parameters.Add(new
            {
                name = candidate,
                fullName = Try(() => parameter.FullName),
                systemValueM = Try(() => parameter.SystemValue),
            });
        }

        var cylinders = new List<object>();
        var faceSummaries = new List<object>();
        object? facesObj = Try(() => feature.GetFaces());
        int faceCount = facesObj is object[] faceArr ? faceArr.Length : 0;
        if (facesObj is object[] faces)
        {
            int index = 0;
            foreach (object entry in faces)
            {
                index++;
                Face2? face = entry as Face2 ?? Try(() => (Face2)entry) as Face2;
                if (face is null)
                {
                    faceSummaries.Add(new { faceIndex = index, typeName = entry?.GetType().FullName });
                    continue;
                }

                Surface? surface = Try(() => face.GetSurface()) as Surface;
                bool isCylinder = surface is not null && (Try(() => surface.IsCylinder()) as bool? ?? false);
                bool isPlane = surface is not null && (Try(() => surface.IsPlane()) as bool? ?? false);
                Body2? owningBody = Try(() => face.GetBody()) as Body2;
                faceSummaries.Add(new
                {
                    faceIndex = index,
                    isCylinder,
                    isPlane,
                    bodyName = Try(() => owningBody?.Name),
                    areaM2 = Try(() => face.GetArea()),
                });

                if (!isCylinder || surface is null)
                {
                    continue;
                }

                double[]? cyl = Try(() => surface.CylinderParams) as double[];
                double? radiusM = cyl is { Length: >= 7 } ? cyl[6] : null;
                cylinders.Add(new
                {
                    faceIndex = index,
                    radiusM,
                    originM = cyl is { Length: >= 3 } ? new[] { cyl[0], cyl[1], cyl[2] } : null,
                    axis = cyl is { Length: >= 6 } ? new[] { cyl[3], cyl[4], cyl[5] } : null,
                    areaM2 = Try(() => face.GetArea()),
                });
            }
        }

        var sketchInfo = new List<object>();
        Sketch? sketch = Try(() => feature.GetSpecificFeature2()) as Sketch;
        if (sketch is not null)
        {
            object? segsObj = Try(() => sketch.GetSketchSegments());
            if (segsObj is object[] segs)
            {
                int segIndex = 0;
                foreach (object segEntry in segs)
                {
                    segIndex++;
                    SketchSegment? seg = segEntry as SketchSegment ?? Try(() => (SketchSegment)segEntry) as SketchSegment;
                    if (seg is null)
                    {
                        continue;
                    }

                    int segType = Try(() => seg.GetType()) as int? ?? -1;
                    double? radiusM = null;
                    SketchArc? arc = seg as SketchArc ?? Try(() => (SketchArc)(object)seg) as SketchArc;
                    if (arc is not null)
                    {
                        radiusM = Try(() => arc.GetRadius()) as double?;
                    }

                    double? diameterMm = radiusM is null ? null : radiusM.Value * 2000.0;
                    sketchInfo.Add(new
                    {
                        segIndex,
                        segType,
                        construction = Try(() => seg.ConstructionGeometry),
                        radiusM,
                        diameterMm,
                    });
                }
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            featureName,
            featureType = Try(() => feature.GetTypeName2()),
            faceCount,
            displayDimensions,
            parameters,
            faceSummaries,
            cylinders,
            sketchSegments = sketchInfo,
        };
    }

    private static object SetSketchCircleDiameter(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("set_sketch_circle_diameter requires a part document.");
        }

        string sketchName = RequiredStringArg(args, "sketch_name");
        double? diameterM = args is not null
            && args.Value.TryGetProperty("diameter_m", out JsonElement diameterEl)
            && diameterEl.ValueKind == JsonValueKind.Number
            ? diameterEl.GetDouble()
            : null;
        double? diameterMm = args is not null
            && args.Value.TryGetProperty("diameter_mm", out JsonElement diameterMmEl)
            && diameterMmEl.ValueKind == JsonValueKind.Number
            ? diameterMmEl.GetDouble()
            : null;
        double? deltaM = args is not null
            && args.Value.TryGetProperty("delta_m", out JsonElement deltaEl)
            && deltaEl.ValueKind == JsonValueKind.Number
            ? deltaEl.GetDouble()
            : null;
        double? deltaMm = args is not null
            && args.Value.TryGetProperty("delta_mm", out JsonElement deltaMmEl)
            && deltaMmEl.ValueKind == JsonValueKind.Number
            ? deltaMmEl.GetDouble()
            : null;
        double? matchDiameterM = args is not null
            && args.Value.TryGetProperty("match_diameter_m", out JsonElement matchEl)
            && matchEl.ValueKind == JsonValueKind.Number
            ? matchEl.GetDouble()
            : null;
        double? matchDiameterMm = args is not null
            && args.Value.TryGetProperty("match_diameter_mm", out JsonElement matchMmEl)
            && matchMmEl.ValueKind == JsonValueKind.Number
            ? matchMmEl.GetDouble()
            : null;
        bool preferInner = BoolArg(args, "prefer_inner", defaultValue: true);

        if (diameterM is null && diameterMm is not null)
        {
            diameterM = diameterMm.Value / 1000.0;
        }

        if (deltaM is null && deltaMm is not null)
        {
            deltaM = deltaMm.Value / 1000.0;
        }

        if (matchDiameterM is null && matchDiameterMm is not null)
        {
            matchDiameterM = matchDiameterMm.Value / 1000.0;
        }

        if (diameterM is null && deltaM is null)
        {
            throw WorkerException.Validation(
                "MISSING_TARGET",
                "Provide diameter_m/diameter_mm or delta_m/delta_mm.",
                new Dictionary<string, object?>());
        }

        Feature sketchFeature = FindFeatureByName(doc, sketchName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Sketch not found: {sketchName}",
                new Dictionary<string, object?> { ["sketch_name"] = sketchName });

        Sketch sketch = Try(() => sketchFeature.GetSpecificFeature2()) as Sketch
            ?? throw new InvalidOperationException($"Feature '{sketchName}' is not a sketch.");

        object? segsObj = Try(() => sketch.GetSketchSegments())
            ?? throw new InvalidOperationException($"Sketch '{sketchName}' has no segments.");
        if (segsObj is not object[] segs || segs.Length == 0)
        {
            throw new InvalidOperationException($"Sketch '{sketchName}' has no segments.");
        }

        var arcs = new List<(SketchArc Arc, SketchSegment Segment, double RadiusM)>();
        foreach (object entry in segs)
        {
            SketchSegment? segment = entry as SketchSegment ?? Try(() => (SketchSegment)entry) as SketchSegment;
            if (segment is null)
            {
                continue;
            }

            if (Try(() => segment.ConstructionGeometry) as bool? == true)
            {
                continue;
            }

            SketchArc? arc = segment as SketchArc ?? Try(() => (SketchArc)(object)segment) as SketchArc;
            double? radius = arc is null ? null : Try(() => arc.GetRadius()) as double?;
            if (arc is null || radius is null || radius.Value <= 0)
            {
                continue;
            }

            arcs.Add((arc, segment, radius.Value));
        }

        if (arcs.Count == 0)
        {
            throw new InvalidOperationException($"Sketch '{sketchName}' has no circular arcs.");
        }

        (SketchArc Arc, SketchSegment Segment, double RadiusM) chosen;
        if (matchDiameterM is not null)
        {
            double targetRadius = matchDiameterM.Value / 2.0;
            chosen = arcs.OrderBy(a => Math.Abs(a.RadiusM - targetRadius)).First();
        }
        else if (preferInner)
        {
            chosen = arcs.OrderBy(a => a.RadiusM).First();
        }
        else
        {
            chosen = arcs.OrderByDescending(a => a.RadiusM).First();
        }

        double oldDiameterM = chosen.RadiusM * 2.0;
        double newDiameterM = diameterM ?? (oldDiameterM + deltaM!.Value);
        if (newDiameterM <= 0)
        {
            throw WorkerException.Validation(
                "INVALID_DIAMETER",
                "Resulting diameter must be positive.",
                new Dictionary<string, object?> { ["newDiameterM"] = newDiameterM });
        }

        // Resolve existing driving/display dimensions before entering the sketch.
        Dimension? dimension = ResolveExistingSketchDimension(doc, sketchFeature, sketchName);

        doc.ClearSelection2(true);
        bool selectedSketch = doc.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0)
            || (Try(() => sketchFeature.Select2(false, 0)) as bool? ?? false);
        if (!selectedSketch)
        {
            throw new InvalidOperationException($"Could not select sketch: {sketchName}");
        }

        TryVoid(() => doc.EditSketch());

        doc.ClearSelection2(true);
        SelectData? selData = CreateSelectData(doc, 0);
        bool selectedArc = selData is not null
            && (Try(() => chosen.Segment.Select4(false, selData)) as bool? ?? false);
        if (!selectedArc)
        {
            SketchPoint? center = Try(() => chosen.Arc.GetCenterPoint2()) as SketchPoint;
            double x = Try(() => center?.X) as double? ?? 0;
            double y = Try(() => center?.Y) as double? ?? 0;
            double z = Try(() => center?.Z) as double? ?? 0;
            selectedArc = doc.Extension.SelectByID2(
                "",
                "SKETCHSEGMENT",
                x + chosen.RadiusM,
                y,
                z,
                false,
                0,
                null,
                0);
        }

        if (!selectedArc)
        {
            TryVoid(() => doc.SketchManager.InsertSketch(true));
            throw new InvalidOperationException("Could not select target sketch circle.");
        }

        if (dimension is null)
        {
            DisplayDimension? displayDimension = Try(() => doc.AddDiameterDimension2(0, 0, 0)) as DisplayDimension;
            dimension = displayDimension is null
                ? null
                : Try(() => displayDimension.GetDimension2(0)) as Dimension;
        }

        if (dimension is null)
        {
            TryVoid(() => doc.SketchManager.InsertSketch(true));
            throw new InvalidOperationException(
                "Could not create or resolve a diameter dimension for the selected circle.");
        }

        double previousValue = Try(() => dimension.SystemValue) as double? ?? oldDiameterM;
        // Diameter dimensions use diameter in meters; radius dims use radius.
        bool looksLikeRadius = previousValue > 0 && Math.Abs(previousValue - chosen.RadiusM) < Math.Abs(previousValue - oldDiameterM);
        double writeValue = looksLikeRadius ? newDiameterM / 2.0 : newDiameterM;
        dimension.SystemValue = writeValue;

        TryVoid(() => doc.SketchManager.InsertSketch(true));
        Try(() => doc.ForceRebuild3(false));
        doc.EditRebuild3();

        double rebuiltRadius = Try(() => chosen.Arc.GetRadius()) as double?
            ?? throw new InvalidOperationException("Could not read the sketch circle after rebuild.");
        double rebuiltDiameterM = rebuiltRadius * 2.0;
        double toleranceM = Math.Max(1e-8, Math.Abs(newDiameterM) * 1e-6);
        if (Math.Abs(rebuiltDiameterM - newDiameterM) > toleranceM)
        {
            throw new InvalidOperationException(
                $"Sketch circle diameter did not reach target. Target={newDiameterM:R} m, actual={rebuiltDiameterM:R} m, tolerance={toleranceM:R} m.");
        }

        return new
        {
            document = DescribeDocument(doc),
            sketchName,
            oldDiameterM,
            newDiameterM,
            wroteRadiusNotDiameter = looksLikeRadius,
            dimensionName = Try(() => dimension.FullName),
            previousDimensionValueM = previousValue,
            rebuiltDiameterM,
            rebuiltDiameterMm = rebuiltDiameterM * 1000.0,
        };
    }

    private static Dimension? ResolveExistingSketchDimension(ModelDoc2 doc, Feature sketchFeature, string sketchName)
    {
        foreach (string candidate in new[]
                 {
                     $"D1@{sketchName}", $"D2@{sketchName}", $"D3@{sketchName}", $"D4@{sketchName}",
                     $"D5@{sketchName}", $"D6@{sketchName}", $"D7@{sketchName}", $"D8@{sketchName}",
                     $"RD1@{sketchName}", $"RD2@{sketchName}", $"RD3@{sketchName}", $"RD4@{sketchName}",
                 })
        {
            Dimension? parameter = Try(() => doc.Parameter(candidate)) as Dimension;
            if (parameter is not null)
            {
                return parameter;
            }
        }

        object? displayDimensionObj = Try(() => sketchFeature.GetFirstDisplayDimension());
        int dimGuard = 0;
        while (displayDimensionObj is not null && dimGuard++ < 100)
        {
            if (displayDimensionObj is not DisplayDimension displayDimension)
            {
                break;
            }

            Dimension? dimension = Try(() => displayDimension.GetDimension2(0)) as Dimension;
            if (dimension is not null)
            {
                return dimension;
            }

            displayDimensionObj = Try(() => sketchFeature.GetNextDisplayDimension(displayDimension));
        }

        return null;
    }

    private static object CombineBodies(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("combine_bodies requires a part document.");
        }

        string operation = (RequiredStringArg(args, "operation") ?? "").Trim().ToLowerInvariant();
        IReadOnlyList<int> operationTypes = GetCombineOperationTypes(operation);

        var bodyNames = new List<string>();
        string? primaryBody = StringArg(args, "body_name");
        if (!string.IsNullOrWhiteSpace(primaryBody))
        {
            bodyNames.Add(primaryBody.Trim());
        }

        string? toolBody = StringArg(args, "tool_body_name")?.Trim();
        if (!string.IsNullOrWhiteSpace(toolBody)
            && !bodyNames.Contains(toolBody, StringComparer.OrdinalIgnoreCase))
        {
            bodyNames.Add(toolBody.Trim());
        }

        string[]? extraBodies = StringArrayArg(args, "body_names");
        if (extraBodies is not null)
        {
            foreach (string name in extraBodies)
            {
                if (!bodyNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    bodyNames.Add(name);
                }
            }
        }

        string[]? featureNames = StringArrayArg(args, "feature_names");
        var resolvedFromFeatures = new List<object>();
        if (featureNames is not null)
        {
            foreach (string featureName in featureNames)
            {
                string resolved = ResolveSolidBodyNameFromFeature(doc, featureName);
                resolvedFromFeatures.Add(new { featureName, bodyName = resolved });
                if (!bodyNames.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    bodyNames.Add(resolved);
                }
            }
        }

        if (bodyNames.Count < 2)
        {
            throw WorkerException.Validation(
                "TOO_FEW_BODIES",
                "combine_bodies needs at least two solid bodies after resolving names/features/selection.",
                new Dictionary<string, object?>
                {
                    ["bodyNames"] = bodyNames,
                    ["resolvedFromFeatures"] = resolvedFromFeatures,
                });
        }

        if (operation == "common" && bodyNames.Count != 2)
        {
            throw WorkerException.Validation(
                "COMMON_REQUIRES_TWO_BODIES",
                "combine_bodies operation 'common' currently supports exactly two resolved bodies (native or synthesized).",
                new Dictionary<string, object?>
                {
                    ["bodyNames"] = bodyNames,
                    ["resolvedFromFeatures"] = resolvedFromFeatures,
                });
        }

        var keepNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[]? keepBodies = StringArrayArg(args, "keep_body_names");
        if (keepBodies is not null)
        {
            foreach (string name in keepBodies)
            {
                keepNames.Add(name);
            }
        }

        string[]? keepFeatures = StringArrayArg(args, "keep_feature_names");
        if (keepFeatures is not null)
        {
            foreach (string featureName in keepFeatures)
            {
                keepNames.Add(ResolveSolidBodyNameFromFeature(doc, featureName));
            }
        }

        var copiedBodies = new List<object>();
        var combineBodyNames = new List<string>(bodyNames);
        var createdFeatures = new List<string>();

        try
        {
            for (int i = 0; i < combineBodyNames.Count; i++)
            {
                string original = combineBodyNames[i];
                if (!keepNames.Contains(original))
                {
                    continue;
                }

                string copyName = CopySolidBodyIdentity(doc, original, createdFeatures);
                copiedBodies.Add(new { originalBodyName = original, copyBodyName = copyName });
                combineBodyNames[i] = copyName;
            }

            List<string> beforeBodies = ListSolidBodyNames(doc);
            var attempts = new List<string>();
            int operationType;
            Feature? combine = TryInsertCombineFeature(
                doc,
                operation,
                combineBodyNames,
                operationTypes,
                attempts,
                createdFeatures,
                out operationType);
            bool synthesizedCommon = false;

            if (combine is null && operation == "common")
            {
                // Synthesize A ∩ B as A - (A - B) when the host rejects Common.
                combine = SynthesizeCommon(
                    doc,
                    bodyNames,
                    combineBodyNames,
                    keepNames,
                    copiedBodies,
                    attempts,
                    createdFeatures,
                    out operationType);
                synthesizedCommon = combine is not null;
            }

            if (combine is null)
            {
                throw new InvalidOperationException(
                    $"InsertCombineFeature failed for operation={operation}. Bodies={string.Join(", ", combineBodyNames)}. Attempts={string.Join(" | ", attempts)}");
            }

            Try(() => doc.ForceRebuild3(false));
            doc.EditRebuild3();

            return new
            {
                document = DescribeDocument(doc),
                operation,
                operationType,
                inputBodyNames = bodyNames,
                combineBodyNames,
                keepBodyNames = keepNames.ToArray(),
                copiedBodies,
                resolvedFromFeatures,
                synthesizedCommon,
                attempts,
                featureName = Try(() => combine.Name),
                created = true,
                bodiesBefore = beforeBodies,
                bodiesAfter = ListSolidBodyNames(doc),
            };
        }
        catch
        {
            CleanupCreatedFeatures(doc, createdFeatures);
            throw;
        }
    }

    private static IReadOnlyList<int> GetCombineOperationTypes(string operation) =>
        operation switch
        {
            // 15901 (SWBODYINTERSECT) behaves as cut on this host, so Common stays at enum value 2.
            "common" => [
                (int)swCombineBodiesOperationType_e.swCombineBodiesOperationCommon,
            ],
            "add" => [
                (int)swBodyOperationType_e.SWBODYADD,
                (int)swCombineBodiesOperationType_e.swCombineBodiesOperationAdd,
            ],
            "subtract" => [
                (int)swBodyOperationType_e.SWBODYCUT,
                (int)swCombineBodiesOperationType_e.swCombineBodiesOperationSubtract,
            ],
            _ => throw WorkerException.Validation(
                "INVALID_OPERATION",
                "operation must be one of: common, add, subtract.",
                new Dictionary<string, object?> { ["operation"] = operation }),
        };

    private static Feature? TryInsertCombineFeature(
        ModelDoc2 doc,
        string operation,
        IReadOnlyList<string> bodyNames,
        IReadOnlyList<int> operationTypes,
        List<string> attempts,
        List<string> createdFeatures,
        out int operationType)
    {
        operationType = operationTypes[0];
        Body2[] bodyObjs = ResolveSolidBodyArray(doc, bodyNames);
        Feature? combine = null;

        foreach (int opType in operationTypes)
        {
            operationType = opType;

            if (combine is null && operation != "subtract")
            {
                try
                {
                    combine = Try(() => doc.FeatureManager.InsertCombineFeature(opType, null, bodyObjs)) as Feature;
                    attempts.Add(combine is null ? $"bodyArray({opType}):null" : $"bodyArray({opType}):ok");
                }
                catch (Exception ex)
                {
                    attempts.Add($"bodyArray({opType}):ex:{ex.GetType().Name}:{ex.Message}");
                }
            }

            if (combine is null)
            {
                doc.ClearSelection2(true);
                if (operation == "subtract")
                {
                    if (!SelectSolidBody(doc, bodyNames[0], append: false, mark: 1))
                    {
                        throw new InvalidOperationException($"Could not select main body: {bodyNames[0]}");
                    }

                    for (int i = 1; i < bodyNames.Count; i++)
                    {
                        if (!SelectSolidBody(doc, bodyNames[i], append: true, mark: 2))
                        {
                            throw new InvalidOperationException($"Could not select tool body: {bodyNames[i]}");
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < bodyNames.Count; i++)
                    {
                        if (!SelectSolidBody(doc, bodyNames[i], append: i > 0, mark: 1))
                        {
                            throw new InvalidOperationException($"Could not select body: {bodyNames[i]}");
                        }
                    }
                }

                try
                {
                    combine = Try(() => doc.FeatureManager.InsertCombineFeature(opType, null, null)) as Feature;
                    attempts.Add(combine is null ? $"selection({opType}):null" : $"selection({opType}):ok");
                }
                catch (Exception ex)
                {
                    attempts.Add($"selection({opType}):ex:{ex.GetType().Name}:{ex.Message}");
                }
            }

            if (combine is not null)
            {
                string? featureName = Try(() => combine.Name) as string;
                if (!string.IsNullOrWhiteSpace(featureName))
                {
                    createdFeatures.Add(featureName);
                }

                break;
            }
        }

        return combine;
    }

    private static Feature? SynthesizeCommon(
        ModelDoc2 doc,
        IReadOnlyList<string> originalBodyNames,
        IReadOnlyList<string> combineBodyNames,
        HashSet<string> keepNames,
        List<object> copiedBodies,
        List<string> attempts,
        List<string> createdFeatures,
        out int operationType)
    {
        if (combineBodyNames.Count != 2 || originalBodyNames.Count != 2)
        {
            throw new InvalidOperationException("Common synthesis requires exactly two solid bodies.");
        }

        string originalTarget = originalBodyNames[0];
        string target = combineBodyNames[0];
        string tool = combineBodyNames[1];
        bool targetWasKeepCopy = !string.Equals(target, originalTarget, StringComparison.OrdinalIgnoreCase);
        string workCopy = target;
        if (!targetWasKeepCopy)
        {
            workCopy = CopySolidBodyIdentity(doc, target, createdFeatures);
            copiedBodies.Add(new { originalBodyName = target, copyBodyName = workCopy });
        }

        attempts.Add($"common:synthesize({workCopy},{tool})");
        List<string> beforeOutside = ListSolidBodyNames(doc);
        Feature? outside = TryInsertCombineFeature(
            doc,
            "subtract",
            [workCopy, tool],
            GetCombineOperationTypes("subtract"),
            attempts,
            createdFeatures,
            out _);
        if (outside is null)
        {
            throw new InvalidOperationException(
                $"Common synthesis outside subtract failed. Bodies={workCopy}, {tool}. Attempts={string.Join(" | ", attempts)}");
        }

        HashSet<string> beforeOutsideSet = new(beforeOutside, StringComparer.OrdinalIgnoreCase);
        List<string> afterOutside = ListSolidBodyNames(doc);
        List<string> outsideBodies = afterOutside
            .Where(name =>
                !beforeOutsideSet.Contains(name)
                && !string.Equals(name, originalTarget, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, tool, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, workCopy, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (outsideBodies.Count == 0 && FindSolidBodyByName(doc, workCopy) is not null)
        {
            outsideBodies.Add(workCopy);
            attempts.Add($"common:outside-remnant=in-place({workCopy})");
        }

        if (outsideBodies.Count == 0)
        {
            // A - B is empty when A is contained by B. The intersection is A,
            // which is still available as originalTarget because workCopy was
            // always disposable. Copy it as the synthesized common result.
            if (FindSolidBodyByName(doc, originalTarget) is null)
            {
                throw new InvalidOperationException(
                    $"Common synthesis produced no outside remnant and lost contained target body. Bodies before={string.Join(", ", beforeOutside)}. Bodies after={string.Join(", ", afterOutside)}.");
            }

            string containedBody = CopySolidBodyIdentity(doc, originalTarget, createdFeatures);
            string containedFeatureName = createdFeatures.LastOrDefault()
                ?? throw new InvalidOperationException("Contained common copy did not create a feature.");
            Feature containedResult = FindFeatureByName(doc, containedFeatureName)
                ?? throw new InvalidOperationException(
                    $"Contained common copy feature was not found: {containedFeatureName}");
            attempts.Add($"common:contained-intersection=copy({containedBody})");
            operationType = (int)swCombineBodiesOperationType_e.swCombineBodiesOperationCommon;
            return containedResult;
        }

        // Never use a body requested in keep_body_names as the subtract main.
        // If the original target was kept, make a second disposable work copy
        // for the final A - (A - B) step.
        string finalTarget = originalTarget;
        if (keepNames.Contains(originalTarget))
        {
            finalTarget = CopySolidBodyIdentity(doc, originalTarget, createdFeatures);
            copiedBodies.Add(new { originalBodyName = originalTarget, copyBodyName = finalTarget });
            attempts.Add($"common:final-target-copy({finalTarget})");
        }

        return TryInsertCombineFeature(
            doc,
            "subtract",
            new[] { finalTarget }.Concat(outsideBodies).ToArray(),
            GetCombineOperationTypes("subtract"),
            attempts,
            createdFeatures,
            out operationType);
    }

    private static string ResolveSolidBodyNameFromFeature(ModelDoc2 doc, string featureName)
    {
        Feature feature = FindFeatureByName(doc, featureName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {featureName}",
                new Dictionary<string, object?> { ["feature_name"] = featureName });

        // Early features (lofts/shells) can report faces that later get owned by unrelated
        // tip bodies after multi-body edits. Majority-vote by face area, and prefer a body
        // whose name still contains the feature token when the vote is ambiguous.
        var areaByBody = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is object[] faces)
        {
            foreach (object entry in faces)
            {
                Face2? face = entry as Face2 ?? Try(() => (Face2)entry) as Face2;
                if (face is null)
                {
                    continue;
                }

                Body2? body = Try(() => face.GetBody()) as Body2;
                string? bodyName = Try(() => body?.Name) as string;
                if (string.IsNullOrWhiteSpace(bodyName))
                {
                    continue;
                }

                double area = Try(() => face.GetArea()) as double? ?? 0;
                areaByBody[bodyName] = areaByBody.TryGetValue(bodyName, out double existing)
                    ? existing + Math.Max(area, 0)
                    : Math.Max(area, 0);
            }
        }

        if (areaByBody.Count > 0)
        {
            string? exact = areaByBody.Keys.FirstOrDefault(name =>
                string.Equals(name, featureName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(featureName + "[", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }

            return areaByBody.OrderByDescending(pair => pair.Value).First().Key;
        }

        // Some features expose the body under the same name.
        if (FindSolidBodyByName(doc, featureName) is not null)
        {
            return featureName;
        }

        string? prefixed = ListSolidBodyNames(doc).FirstOrDefault(name =>
            name.StartsWith(featureName + "[", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, featureName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(prefixed))
        {
            return prefixed;
        }

        throw WorkerException.Validation(
            "BODY_NOT_FOUND_FOR_FEATURE",
            $"Could not resolve a solid body from feature '{featureName}'. Pass body_names explicitly.",
            new Dictionary<string, object?>
            {
                ["feature_name"] = featureName,
                ["bodies"] = ListSolidBodyNames(doc),
                ["faceBodyAreas"] = areaByBody,
            });
    }

    private static string CopySolidBodyIdentity(ModelDoc2 doc, string bodyName, List<string> createdFeatures)
    {
        HashSet<string> before = new(ListSolidBodyNames(doc), StringComparer.OrdinalIgnoreCase);
        doc.ClearSelection2(true);
        if (!SelectSolidBody(doc, bodyName, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Could not select body to copy: {bodyName}");
        }

        Feature? moveCopy = Try(() => doc.FeatureManager.InsertMoveCopyBody2(
            0, 0, 0,
            0, 0, 0,
            0, 0, 0,
            0,
            true,
            1)) as Feature;

        if (moveCopy is null)
        {
            throw new InvalidOperationException($"InsertMoveCopyBody2 failed for body: {bodyName}");
        }

        string featureName = Try(() => moveCopy.Name) as string
            ?? throw new InvalidOperationException($"Move/copy feature has no name for body: {bodyName}");
        createdFeatures.Add(featureName);

        Try(() => doc.ForceRebuild3(false));
        doc.EditRebuild3();

        foreach (string name in ListSolidBodyNames(doc))
        {
            if (!before.Contains(name))
            {
                return name;
            }
        }

        throw new InvalidOperationException(
            $"Copied body '{bodyName}' but could not identify the new body name. Feature={featureName}");
    }

    private static void CleanupCreatedFeatures(ModelDoc2 doc, IReadOnlyList<string> createdFeatures)
    {
        for (int i = createdFeatures.Count - 1; i >= 0; i--)
        {
            try
            {
                DeleteFeatureByName(doc, createdFeatures[i]);
            }
            catch
            {
                // Preserve the original synthesis failure while making a best-effort rollback.
            }
        }

        TryVoid(() => doc.ForceRebuild3(false));
        TryVoid(() => doc.EditRebuild3());
    }

    private static bool SelectSolidBody(ModelDoc2 doc, string bodyName, bool append, int mark)
    {
        Body2? body = FindSolidBodyByName(doc, bodyName);
        if (body is not null)
        {
            SelectData? selData = CreateSelectData(doc, mark);
            if (selData is not null)
            {
                bool selected = Try(() => body.Select2(append, selData)) as bool? ?? false;
                if (selected)
                {
                    return true;
                }
            }
        }

        return doc.Extension.SelectByID2(
            bodyName,
            "SOLIDBODY",
            0,
            0,
            0,
            append,
            mark,
            null,
            0);
    }

    private static Body2? FindSolidBodyByName(ModelDoc2 doc, string bodyName)
    {
        object? bodiesObj = Try(() => ((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        foreach (object entry in bodies)
        {
            if (entry is Body2 body
                && string.Equals(Try(() => body.Name) as string, bodyName, StringComparison.OrdinalIgnoreCase))
            {
                return body;
            }
        }

        return null;
    }

    private static Body2[] ResolveSolidBodyArray(ModelDoc2 doc, IReadOnlyList<string> bodyNames)
    {
        var bodies = new List<Body2>();
        foreach (string name in bodyNames)
        {
            Body2 body = FindSolidBodyByName(doc, name)
                ?? throw new InvalidOperationException($"Solid body not found: {name}");
            bodies.Add(body);
        }

        if (bodies.Count == 0)
        {
            throw new InvalidOperationException("No solid bodies resolved for combine.");
        }

        return bodies.ToArray();
    }

    private static List<string> ListSolidBodyNames(ModelDoc2 doc)
    {
        var names = new List<string>();
        object? bodiesObj = Try(() => ((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return names;
        }

        foreach (object entry in bodies)
        {
            if (entry is Body2 body)
            {
                string? name = Try(() => body.Name) as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }
}
