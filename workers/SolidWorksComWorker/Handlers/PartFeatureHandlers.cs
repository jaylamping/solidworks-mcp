using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static bool SelectPlane(ModelDoc2 doc, string planeName)
    {
        doc.ClearSelection2(true);
        return doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0);
    }

    private static SketchManager ActiveSketchManager(ModelDoc2 doc) => doc.SketchManager;

    private static Feature? ExtrudeCutThroughAll(ModelDoc2 doc)
    {
        Feature? cut = Try(() => doc.FeatureManager.FeatureCut4(
            true,
            false,
            false,
            (int)swEndConditions_e.swEndCondThroughAll,
            0,
            0.01,
            0.01,
            false,
            false,
            false,
            false,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            0,
            0.0,
            false,
            false)) as Feature;

        doc.EditRebuild3();
        return cut;
    }

    private static object SketchLine(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double x1 = DoubleArg(args, "x1_m", 0);
        double y1 = DoubleArg(args, "y1_m", 0);
        double x2 = DoubleArg(args, "x2_m", 0.01);
        double y2 = DoubleArg(args, "y2_m", 0.01);

        SketchManager sketchMgr = ActiveSketchManager(doc);
        object? line = Try(() => sketchMgr.CreateLine(x1, y1, 0, x2, y2, 0));
        return new
        {
            document = DescribeDocument(doc),
            startM = new[] { x1, y1 },
            endM = new[] { x2, y2 },
            created = line is not null,
        };
    }

    private static object SketchCircle(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double centerX = DoubleArg(args, "center_x_m", 0);
        double centerY = DoubleArg(args, "center_y_m", 0);
        double radiusM = DoubleArg(args, "radius_m", 0.005);

        SketchManager sketchMgr = ActiveSketchManager(doc);
        object? circle = Try(() => sketchMgr.CreateCircleByRadius(centerX, centerY, 0, radiusM));
        return new
        {
            document = DescribeDocument(doc),
            centerM = new[] { centerX, centerY },
            radiusM,
            created = circle is not null,
        };
    }

    private static object SketchExit(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        SketchManager sketchMgr = ActiveSketchManager(doc);
        TryVoid(() => sketchMgr.InsertSketch(true));
        return new { document = DescribeDocument(doc), sketchActive = false };
    }

    private static object FeatureExtrudeCut(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double depthM = DoubleArg(args, "depth_m", 0.01);
        bool throughAll = BoolArg(args, "through_all", defaultValue: true);

        Feature? cut;
        if (throughAll)
        {
            cut = ExtrudeCutThroughAll(doc);
        }
        else
        {
            cut = Try(() => doc.FeatureManager.FeatureCut4(
                true,
                false,
                false,
                (int)swEndConditions_e.swEndCondBlind,
                0,
                depthM,
                0,
                false,
                false,
                false,
                false,
                0,
                0,
                false,
                false,
                false,
                false,
                false,
                true,
                true,
                true,
                true,
                false,
                0,
                0.0,
                false,
                false)) as Feature;
            doc.EditRebuild3();
        }

        return new
        {
            document = DescribeDocument(doc),
            depthM,
            throughAll,
            featureName = Try(() => cut?.Name),
            created = cut is not null,
        };
    }

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

        // Prefer editing an existing diameter/radius parameter if one is already attached.
        Dimension? dimension = null;
        DisplayDimension? displayDimension = Try(() => doc.AddDiameterDimension2(0, 0, 0)) as DisplayDimension;
        if (displayDimension is not null)
        {
            dimension = Try(() => displayDimension.GetDimension2(0)) as Dimension;
        }

        if (dimension is null)
        {
            // Fallback: try common parameter names on this sketch.
            foreach (string candidate in new[]
                     {
                         $"D1@{sketchName}", $"D2@{sketchName}", $"D3@{sketchName}", $"D4@{sketchName}",
                         $"RD1@{sketchName}", $"RD2@{sketchName}",
                     })
            {
                dimension = Try(() => doc.Parameter(candidate)) as Dimension;
                if (dimension is not null)
                {
                    break;
                }
            }
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
        TryVoid(() => dimension.SystemValue = writeValue);

        TryVoid(() => doc.SketchManager.InsertSketch(true));
        Try(() => doc.ForceRebuild3(false));
        doc.EditRebuild3();

        // Re-read chosen arc radius after rebuild.
        double? rebuiltRadius = Try(() => chosen.Arc.GetRadius()) as double?;
        return new
        {
            document = DescribeDocument(doc),
            sketchName,
            oldDiameterM,
            newDiameterM,
            wroteRadiusNotDiameter = looksLikeRadius,
            dimensionName = Try(() => dimension.FullName),
            previousDimensionValueM = previousValue,
            rebuiltDiameterM = rebuiltRadius * 2.0,
            rebuiltDiameterMm = rebuiltRadius * 2000.0,
        };
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
        for (int i = 0; i < combineBodyNames.Count; i++)
        {
            string original = combineBodyNames[i];
            if (!keepNames.Contains(original))
            {
                continue;
            }

            string copyName = CopySolidBodyIdentity(doc, original);
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
                out operationType);
            synthesizedCommon = combine is not null;
        }

        if (combine is null)
        {
            Feature? tip = Try(() => doc.FeatureByPositionReverse(0)) as Feature;
            string? tipName = Try(() => tip?.Name) as string;
            if (!string.IsNullOrWhiteSpace(tipName)
                && tipName.StartsWith("Body-Move/Copy", StringComparison.OrdinalIgnoreCase))
            {
                TryVoid(() => DeleteFeatureByName(doc, tipName));
                Try(() => doc.ForceRebuild3(false));
            }

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
        out int operationType)
    {
        if (combineBodyNames.Count != 2)
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
            workCopy = CopySolidBodyIdentity(doc, target);
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
        if (outsideBodies.Count == 0)
        {
            throw new InvalidOperationException(
                $"Common synthesis produced no outside remnant. Bodies before={string.Join(", ", beforeOutside)}. Bodies after={string.Join(", ", afterOutside)}.");
        }

        string preservedTarget = originalTarget;
        if (FindSolidBodyByName(doc, preservedTarget) is null)
        {
            if (keepNames.Contains(originalTarget))
            {
                throw new InvalidOperationException(
                    $"Common synthesis lost preserved target body: {originalTarget}");
            }

            List<string> remainingCandidates = afterOutside
                .Where(name =>
                    !outsideBodies.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(name, tool, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, workCopy, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (remainingCandidates.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Common synthesis could not identify the preserved target. Candidates={string.Join(", ", remainingCandidates)}.");
            }

            preservedTarget = remainingCandidates[0];
        }

        return TryInsertCombineFeature(
            doc,
            "subtract",
            new[] { preservedTarget }.Concat(outsideBodies).ToArray(),
            GetCombineOperationTypes("subtract"),
            attempts,
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

    private static string CopySolidBodyIdentity(ModelDoc2 doc, string bodyName)
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
            $"Copied body '{bodyName}' but could not identify the new body name. Feature={Try(() => moveCopy.Name)}");
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

    private static object RoundSideArmsFromCircle(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string planeName = StringArg(args, "plane_name") ?? "Front Plane";
        bool dryRun = BoolArg(args, "dry_run", defaultValue: false);
        bool save = BoolArg(args, "save", defaultValue: true);
        double samples = DoubleArg(args, "samples", 24);

        (double centerX, double centerY, double radiusM) = ResolveCircleGuide(doc, args);
        if (radiusM <= 0)
        {
            throw new InvalidOperationException("Circle guide radius must be greater than zero.");
        }

        double[]? box = Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[];
        double margin = radiusM * 0.1;
        double topY = centerY + radiusM;
        double leftOuterX = centerX - radiusM;
        double rightOuterX = centerX + radiusM;

        if (box is { Length: >= 6 })
        {
            double boxWidth = box[3] - box[0];
            double boxHeight = box[4] - box[1];
            margin = Math.Max(margin, Math.Max(boxWidth, boxHeight) * 0.05);
            topY = Math.Max(topY, box[4]);
            leftOuterX = box[0] - margin;
            rightOuterX = box[3] + margin;
        }

        double cutTopY = topY + margin;
        int segmentCount = Math.Clamp((int)Math.Round(samples), 8, 96);

        if (dryRun)
        {
            return new
            {
                document = DescribeDocument(doc),
                planeName,
                centerM = new[] { centerX, centerY },
                radiusM,
                outerXM = new[] { leftOuterX, rightOuterX },
                cutTopY,
                segmentCount,
                partBoxM = box,
            };
        }

        Feature? leftCut = CutArmOutsideCircle(doc, planeName, centerX, centerY, radiusM, leftOuterX, cutTopY, segmentCount, left: true);
        Feature? rightCut = CutArmOutsideCircle(doc, planeName, centerX, centerY, radiusM, rightOuterX, cutTopY, segmentCount, left: false);

        bool rebuildOk = Try(() => doc.ForceRebuild3(false)) as bool? ?? false;
        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            document = DescribeDocument(doc),
            planeName,
            centerM = new[] { centerX, centerY },
            radiusM,
            outerXM = new[] { leftOuterX, rightOuterX },
            cutTopY,
            segmentCount,
            leftFeatureName = Try(() => leftCut?.Name),
            rightFeatureName = Try(() => rightCut?.Name),
            rebuildOk,
            saved,
            saveErrors = errors,
            saveWarnings = warnings,
        };
    }

    private static (double CenterX, double CenterY, double RadiusM) ResolveCircleGuide(ModelDoc2 doc, JsonElement? args)
    {
        if (args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("center_x_m", out _)
            && args.Value.TryGetProperty("center_y_m", out _)
            && args.Value.TryGetProperty("radius_m", out _))
        {
            return (
                DoubleArg(args, "center_x_m"),
                DoubleArg(args, "center_y_m"),
                DoubleArg(args, "radius_m"));
        }

        SelectionMgr selection = (SelectionMgr)doc.SelectionManager;
        object selected = selection.GetSelectedObject6(1, -1)
            ?? throw new InvalidOperationException("Select a circular sketch entity or pass center_x_m, center_y_m, and radius_m.");

        dynamic circle = selected;
        dynamic centerPoint = circle.GetCenterPoint2();
        return ((double)centerPoint.X, (double)centerPoint.Y, (double)circle.GetRadius());
    }

    private static Feature? CutArmOutsideCircle(
        ModelDoc2 doc,
        string planeName,
        double centerX,
        double centerY,
        double radiusM,
        double outerX,
        double topY,
        int segmentCount,
        bool left)
    {
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException($"Could not select sketch plane: {planeName}");
        }

        SketchManager sketchMgr = doc.SketchManager;
        sketchMgr.InsertSketch(true);

        var points = new List<(double X, double Y)>
        {
            (centerX + (left ? -radiusM : radiusM), centerY),
            (outerX, centerY),
            (outerX, topY),
            (centerX, topY),
        };

        for (int i = 0; i <= segmentCount; i++)
        {
            double theta = left
                ? (Math.PI / 2.0) + (Math.PI / 2.0) * i / segmentCount
                : (Math.PI / 2.0) - (Math.PI / 2.0) * i / segmentCount;
            points.Add((centerX + radiusM * Math.Cos(theta), centerY + radiusM * Math.Sin(theta)));
        }

        for (int i = 0; i < points.Count; i++)
        {
            (double x1, double y1) = points[i];
            (double x2, double y2) = points[(i + 1) % points.Count];
            sketchMgr.CreateLine(x1, y1, 0, x2, y2, 0);
        }

        sketchMgr.InsertSketch(true);
        Feature? sketchFeature = Try(() => doc.FeatureByPositionReverse(0)) as Feature;
        doc.ClearSelection2(true);
        if (sketchFeature is null || !(Try(() => sketchFeature.Select2(false, 0)) as bool? ?? false))
        {
            throw new InvalidOperationException("Could not select generated cut sketch.");
        }

        Feature? cut = ExtrudeCutThroughAll(doc);
        doc.EditRebuild3();
        return cut;
    }

    private static object FeatureFillet(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double radiusM = DoubleArg(args, "radius_m", DoubleArg(args, "radius_mm", 1.0) / 1000.0);

        Feature? fillet = Try(() => doc.FeatureManager.FeatureFillet3(
            0,
            radiusM,
            0,
            0,
            (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            radiusM,
            featureName = Try(() => fillet?.Name),
            created = fillet is not null,
            note = "Select edges before calling, or fillet may no-op.",
        };
    }

    private static object FeatureChamfer(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double distanceM = DoubleArg(args, "distance_m", DoubleArg(args, "distance_mm", 1.0) / 1000.0);

        Feature? chamfer = Try(() => doc.FeatureManager.InsertFeatureChamfer(
            0,
            (int)swChamferType_e.swChamferAngleDistance,
            distanceM,
            0.7853981633974483,
            0,
            0,
            0,
            0)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            distanceM,
            featureName = Try(() => chamfer?.Name),
            created = chamfer is not null,
            note = "Select edges before calling, or chamfer may no-op.",
        };
    }

    private static object FeatureMirror(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string featureName = RequiredStringArg(args, "feature_name");
        string planeName = StringArg(args, "plane_name") ?? "Right Plane";

        Feature? source = FindFeatureByName(doc, featureName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {featureName}",
                new Dictionary<string, object?> { ["feature_name"] = featureName });

        doc.ClearSelection2(true);
        TryVoid(() => source.Select2(false, 0));
        if (!SelectPlane(doc, planeName))
        {
            throw new InvalidOperationException($"Could not select mirror plane: {planeName}");
        }

        Feature? mirror = Try(() => doc.FeatureManager.InsertMirrorFeature2(true, false, true, false, 0)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            sourceFeature = featureName,
            planeName,
            featureName = Try(() => mirror?.Name),
            created = mirror is not null,
        };
    }

    private static object FeatureLinearPattern(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string featureName = RequiredStringArg(args, "feature_name");
        int count = IntArg(args, "count", 2);
        double spacingM = DoubleArg(args, "spacing_m", DoubleArg(args, "spacing_mm", 10.0) / 1000.0);
        string direction = StringArg(args, "direction") ?? "X";

        Feature? source = FindFeatureByName(doc, featureName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {featureName}",
                new Dictionary<string, object?> { ["feature_name"] = featureName });

        doc.ClearSelection2(true);
        TryVoid(() => source.Select2(false, 0));
        double dirX = direction.Equals("Y", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        double dirY = direction.Equals("Y", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        Feature? pattern = Try(() => doc.FeatureManager.FeatureLinearPattern4(
            count,
            spacingM,
            1,
            0.0,
            false,
            false,
            direction.Equals("Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "X",
            "Y",
            false,
            false,
            false,
            false,
            true,
            true,
            false,
            false,
            false,
            false,
            0.0,
            0.0)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            sourceFeature = featureName,
            count,
            spacingM,
            direction,
            featureName = Try(() => pattern?.Name),
            created = pattern is not null,
        };
    }

    private static object FeatureCircularPattern(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string featureName = RequiredStringArg(args, "feature_name");
        int count = IntArg(args, "count", 4);
        double angleDeg = DoubleArg(args, "angle_deg", 360.0);
        string axisName = StringArg(args, "axis_name") ?? "Z Axis";

        Feature? source = FindFeatureByName(doc, featureName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {featureName}",
                new Dictionary<string, object?> { ["feature_name"] = featureName });

        doc.ClearSelection2(true);
        TryVoid(() => source.Select2(false, 0));
        if (!doc.Extension.SelectByID2(axisName, "AXIS", 0, 0, 0, true, 0, null, 0))
        {
            if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, true, 0, null, 0))
            {
                throw new InvalidOperationException($"Could not select pattern axis: {axisName}");
            }
        }

        Feature? pattern = Try(() => doc.FeatureManager.FeatureCircularPattern4(
            count,
            angleDeg * Math.PI / 180.0,
            false,
            axisName,
            false,
            true,
            false)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            sourceFeature = featureName,
            count,
            angleDeg,
            axisName,
            featureName = Try(() => pattern?.Name),
            created = pattern is not null,
        };
    }

    private static object SetMaterial(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string material = RequiredStringArg(args, "material");
        string database = StringArg(args, "database") ?? "solidworks materials.sldmat";

        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("set_material requires a part document.");
        }

        PartDoc part = (PartDoc)doc;
        TryVoid(() => part.SetMaterialPropertyName2("", database, material));
        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            material,
            database,
            applied = true,
        };
    }

    private static object CreateSubassembly(JsonElement? args)
    {
        string assemblyPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_path"));
        string? componentPath = StringArg(args, "component_path");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        string template = Try(() => app.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly)) as string ?? "";
        ModelDoc2? doc = Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2;
        if (doc is null)
        {
            throw new InvalidOperationException("Failed to create assembly document.");
        }

        string? inserted = null;
        if (!string.IsNullOrWhiteSpace(componentPath))
        {
            string resolved = PathGuard.AssertAllowedPath(componentPath);
            OpenDocument(app, resolved);
            Component2? component = Try(() => ((AssemblyDoc)doc).AddComponent5(
                resolved,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                string.Empty,
                false,
                string.Empty,
                0,
                0,
                0)) as Component2;
            inserted = Try(() => component?.Name2) as string;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath) ?? ".");
        int errors = 0;
        int warnings = 0;
        bool saved = doc.Extension.SaveAs(
            assemblyPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref errors,
            ref warnings);
        return new
        {
            document = DescribeDocument(doc),
            outputPath = assemblyPath,
            insertedComponent = inserted,
            saved,
            errors,
            warnings,
        };
    }

    private static object ExplodeView(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("explode_view requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        bool created = Try(() => assembly.CreateExplodedView()) as bool? ?? false;
        doc.EditRebuild3();
        return new { document = DescribeDocument(doc), explodedViewCreated = created };
    }

    private static object CopyWithMates(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string componentName = RequiredStringArg(args, "component_name");
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("copy_with_mates requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw WorkerException.Validation(
                "COMPONENT_NOT_FOUND",
                $"Component not found: {componentName}",
                new Dictionary<string, object?> { ["component_name"] = componentName });

        doc.ClearSelection2(true);
        component.Select4(false, null, false);
        bool copied = Try(() => ((AssemblyDoc)doc).CopyWithMates2(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null)) as bool? ?? false;
        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            componentName,
            copied,
        };
    }

    private static object CreateDrawingFromModel(JsonElement? args)
    {
        string modelPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "model_path"));
        string outputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_path"));

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 model = OpenDocument(app, modelPath);
        string template = Try(() => app.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing)) as string ?? "";
        ModelDoc2? drawing = Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2;
        if (drawing is null)
        {
            throw new InvalidOperationException("Failed to create drawing document.");
        }

        DrawingDoc drawingDoc = (DrawingDoc)drawing;
        object? view = Try(() => drawingDoc.CreateDrawViewFromModelView3(
            modelPath,
            "*Front",
            0.1,
            0.1,
            0));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        int errors = 0;
        int warnings = 0;
        bool saved = drawing.Extension.SaveAs(
            outputPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref errors,
            ref warnings);
        return new
        {
            modelPath,
            outputPath,
            viewCreated = view is not null,
            saved,
            errors,
            warnings,
        };
    }

    private static object AddStandardViews(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string modelPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "model_path"));
        if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
        {
            throw new InvalidOperationException("add_standard_views requires a drawing document.");
        }

        DrawingDoc drawing = (DrawingDoc)doc;
        var views = new List<object>();
        foreach ((string name, double x, double y) in new[]
                 {
                     ("*Front", 0.1, 0.2),
                     ("*Top", 0.1, 0.05),
                     ("*Right", 0.25, 0.2),
                 })
        {
            object? view = Try(() => drawing.CreateDrawViewFromModelView3(modelPath, name, x, y, 0));
            views.Add(new { name, created = view is not null });
        }

        return new { document = DescribeDocument(doc), modelPath, views };
    }

    private static object ListSheetViews(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
        {
            throw new InvalidOperationException("list_sheet_views requires a drawing document.");
        }

        DrawingDoc drawing = (DrawingDoc)doc;
        var views = new List<object>();
        object[]? sheetNames = Try(() => drawing.GetSheetNames()) as object[];
        if (sheetNames is not null)
        {
            foreach (object sheetEntry in sheetNames)
            {
                string sheetName = sheetEntry as string ?? "";
                object[]? sheetViews = Try(() => drawing.GetViews()) as object[];
                if (sheetViews is null)
                {
                    continue;
                }

                foreach (object viewEntry in sheetViews)
                {
                    if (viewEntry is View view)
                    {
                        views.Add(new
                        {
                            sheet = sheetName,
                            name = Try(() => view.Name),
                            type = Try(() => view.Type),
                        });
                    }
                }
            }
        }

        return new { document = DescribeDocument(doc), views, count = views.Count };
    }

    private static object ImportStep(JsonElement? args)
    {
        string stepPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);
        if (!File.Exists(stepPath))
        {
            throw new FileNotFoundException("STEP file does not exist.", stepPath);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2 doc = OpenDocument(app, stepPath);
        return new
        {
            document = DescribeDocument(doc),
            imported = true,
            note = "STEP import uses the same OpenDocument path as solidworks_open.",
        };
    }

    private static object GetAssemblyDegreesOfFreedom(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_assembly_degrees_of_freedom requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        int fixedCount = 0;
        int movableCount = 0;
        object[]? components = Try(() => assembly.GetComponents(false)) as object[];
        if (components is not null)
        {
            foreach (object entry in components)
            {
                if (entry is not Component2 component)
                {
                    continue;
                }

                bool fixedComp = Try(() => component.IsFixed()) as bool? ?? false;
                if (fixedComp)
                {
                    fixedCount++;
                }
                else
                {
                    movableCount++;
                }
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            fixedComponents = fixedCount,
            movableComponents = movableCount,
            estimatedUnconstrainedComponents = movableCount,
            note = "SolidWorks interop does not expose GetRemainingDOF; use mate list + fixed count for assembly review.",
        };
    }

    private static int IntArg(JsonElement? args, string name, int defaultValue)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        if (!args.Value.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => defaultValue,
        };
    }
}
