using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object MatePlanes(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_planes requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");

        doc.ClearSelection2(true);
        if (!SelectComponentReference(doc, first, ref1, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {ref1} on {component1}");
        }

        if (!SelectComponentReference(doc, second, ref2, append: true, mark: 2))
        {
            throw new InvalidOperationException($"Failed to select {ref2} on {component2}");
        }

        return CreateMateFromSelection(
            doc,
            assembly,
            first,
            second,
            ref1,
            ref2,
            (int)swMateType_e.swMateCOINCIDENT,
            (int)swMateAlign_e.swMateAlignALIGNED);
    }

    private static object CreateMateFromSelection(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        Component2 second,
        string ref1,
        string ref2,
        int mateType,
        int mateAlign)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int selectedCount = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        (bool created, string method, int mateError, bool alreadyConstrained) = TryCreateMate(
            assembly,
            doc,
            mateType,
            mateAlign);

        if (!created && mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"Mate creation failed with error code {mateError}.");
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            selectedCount,
            mateError,
            mateCreated = created,
            alreadyConstrained,
            mateMethod = method,
        };
    }

    private static (bool Created, string Method, int MateError, bool AlreadyConstrained) TryCreateMate(
        IAssemblyDoc assembly,
        ModelDoc2 doc,
        int mateType,
        int mateAlign)
    {
        Feature? mateFeature = CreateMateViaFeatureData(assembly, doc, mateType, mateAlign);
        if (mateFeature is not null)
        {
            return (true, "CreateMate", 0, false);
        }

        Mate2? mate = assembly.AddMate5(
            mateType,
            mateAlign,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            0,
            out int mateError) as Mate2;

        if (mate is not null)
        {
            return (true, "AddMate5", mateError, mateError == 4);
        }

        return (false, "none", mateError, mateError == 4);
    }

    private static object DebugMateEntities(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");

        doc.ClearSelection2(true);
        bool sel1 = SelectComponentReference(doc, first, ref1, append: false, mark: 1);
        bool sel2 = SelectComponentReference(doc, second, ref2, append: true, mark: 2);
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        object? entity1 = Try(() => selectionMgr?.GetSelectedObject6(1, 1))
            ?? Try(() => selectionMgr?.GetSelectedObject6(1, -1));
        object? entity2 = Try(() => selectionMgr?.GetSelectedObject6(2, 2))
            ?? Try(() => selectionMgr?.GetSelectedObject6(2, -1));

        object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateCOINCIDENT));
        string? mateDataType = mateDataObj?.GetType().FullName;

        Feature? mateFeature = null;
        string? createMateError = null;
        int errorStatus = -1;
        try
        {
            if (mateDataObj is ICoincidentMateFeatureData coincident && entity1 is not null && entity2 is not null)
            {
                coincident.EntitiesToMate = new object[] { entity1, entity2 };
                coincident.MateAlignment = (int)swMateAlign_e.swMateAlignALIGNED;
            }

            mateFeature = assembly.CreateMate(mateDataObj) as Feature;
            if (mateDataObj is IMateFeatureData mateData)
            {
                errorStatus = Try(() => mateData.ErrorStatus) as int? ?? -1;
            }
        }
        catch (Exception ex)
        {
            createMateError = ex.Message;
        }

        doc.EditRebuild3();

        int? type1 = Try(() => selectionMgr?.GetSelectedObjectType3(1, 1)) as int?;
        int? type2 = Try(() => selectionMgr?.GetSelectedObjectType3(2, 2)) as int?;

        return new
        {
            document = DescribeDocument(doc),
            sel1,
            sel2,
            entity1Type = entity1?.GetType().FullName,
            entity2Type = entity2?.GetType().FullName,
            entity1IsFace = entity1 is Face2,
            entity2IsFace = entity2 is Face2,
            selectionType1 = type1,
            selectionType2 = type2,
            mateDataType,
            mateFeatureName = Try(() => mateFeature?.Name),
            createMateError,
            errorStatus,
            mateCount = CountAssemblyMates(doc),
        };
    }

    private static Feature? CreateMateViaFeatureData(
        IAssemblyDoc assembly,
        ModelDoc2 doc,
        int mateType,
        int mateAlign)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        if (selectionMgr is null)
        {
            return null;
        }

        object? entity1 = Try(() => selectionMgr.GetSelectedObject6(1, -1));
        object? entity2 = Try(() => selectionMgr.GetSelectedObject6(2, -1));
        if (entity1 is null || entity2 is null)
        {
            return null;
        }

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
            case IDistanceMateFeatureData distance:
                distance.EntitiesToMate = entities;
                distance.MateAlignment = mateAlign;
                break;
            case IPerpendicularMateFeatureData perpendicular:
                perpendicular.EntitiesToMate = entities;
                break;
            default:
                return null;
        }

        return Try(() => assembly.CreateMate(mateDataObj)) as Feature;
    }

    private static object GetFeatureBox(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string featureName = RequiredStringArg(args, "feature_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_feature_box requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        double[]? box = GetFeatureBoundingBoxInAssembly(component, featureName)
            ?? throw new InvalidOperationException($"Feature box unavailable: {featureName} on {componentName}");

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            feature = featureName,
            boundingBox = Normalize(box),
            center = BoxCenter(box),
        };
    }

    private static object ProbeFeatureFaces(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string featureName = RequiredStringArg(args, "feature_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("probe_feature_faces requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        var faces = ProbeFeatureFaceSummaries(component, featureName);
        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            feature = featureName,
            faces,
            faceCount = faces.Count,
        };
    }

    private static object AlignComponentToFeature(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string layoutComponent = RequiredStringArg(args, "layout_component");
        string layoutFeature = RequiredStringArg(args, "layout_feature");
        string targetComponent = RequiredStringArg(args, "target_component");
        string targetPlane = StringArg(args, "target_plane") ?? "Plane1";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("align_component_to_feature requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? layout = FindComponent(assembly, null, layoutComponent)
            ?? throw new InvalidOperationException($"Layout component not found: {layoutComponent}");
        Component2? target = FindComponent(assembly, null, targetComponent)
            ?? throw new InvalidOperationException($"Target component not found: {targetComponent}");

        double[]? iceBox = GetFeatureBoundingBoxInAssembly(layout, layoutFeature);
        double[]? targetBox = GetComponentPlaneBoxInAssembly(target, targetPlane)
            ?? GetFeatureBoundingBoxInAssembly(target, targetPlane)
            ?? Try(() => target.GetBox(false, false)) as double[];

        if (iceBox is null || targetBox is null)
        {
            throw new InvalidOperationException("Could not resolve layout or target bounding boxes.");
        }

        double[] iceCenter = BoxCenter(iceBox);
        double[] targetCenter = BoxCenter(targetBox);
        double tx = iceCenter[0] - targetCenter[0];
        double ty = iceCenter[1] - targetCenter[1];
        double tz = iceCenter[2] - targetCenter[2];

        ApplyComponentTranslation(target, tx, ty, tz);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            layoutComponent = Try(() => layout.Name2),
            layoutFeature,
            targetComponent = Try(() => target.Name2),
            targetPlane,
            translation = new[] { tx, ty, tz },
        };
    }

    private static object MateCoincident(JsonElement? args)
    {
        return AddMateFromComponentRefs(
            args,
            (int)swMateType_e.swMateCOINCIDENT,
            (int)swMateAlign_e.swMateAlignALIGNED,
            "mate_coincident");
    }

    private static object MateParallel(JsonElement? args)
    {
        return AddMateFromComponentRefs(
            args,
            (int)swMateType_e.swMatePARALLEL,
            (int)swMateAlign_e.swMateAlignALIGNED,
            "mate_parallel");
    }

    private static object MateTryCoincident(JsonElement? args) =>
        TryMateFromComponentRefs(args, (int)swMateType_e.swMateCOINCIDENT, "mate_try_coincident");

    private static object MateTryParallel(JsonElement? args) =>
        TryMateFromComponentRefs(args, (int)swMateType_e.swMatePARALLEL, "mate_try_parallel");

    private static object MateTryDistance(JsonElement? args) =>
        TryMateFromComponentRefs(args, (int)swMateType_e.swMateDISTANCE, "mate_try_distance");

    private static object MateTryPerpendicular(JsonElement? args) =>
        TryMateFromComponentRefs(args, (int)swMateType_e.swMatePERPENDICULAR, "mate_try_perpendicular");

    private static object MateTryWidth(JsonElement? args) =>
        TryMateFromComponentRefs(args, (int)swMateType_e.swMateWIDTH, "mate_try_width");

    private static object MateDistance(JsonElement? args) =>
        AddMateFromComponentRefs(args, (int)swMateType_e.swMateDISTANCE, ParseMateAlign(args), "mate_distance");

    private static object MatePerpendicular(JsonElement? args) =>
        AddMateFromComponentRefs(args, (int)swMateType_e.swMatePERPENDICULAR, ParseMateAlign(args), "mate_perpendicular");

    private static object MateWidth(JsonElement? args) =>
        AddMateFromComponentRefs(args, (int)swMateType_e.swMateWIDTH, ParseMateAlign(args), "mate_width");

    private static object MateProbe(JsonElement? args) => DebugMateEntities(args);

    private static int ParseMateAlign(JsonElement? args)
    {
        string? align = StringArg(args, "align");
        return align?.Equals("anti_aligned", StringComparison.OrdinalIgnoreCase) == true
            ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED
            : (int)swMateAlign_e.swMateAlignALIGNED;
    }

    private static object TryMateFromComponentRefs(JsonElement? args, int mateType, string commandName)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        int face1 = (int)DoubleArg(args, "face_index_1", 0);
        int face2 = (int)DoubleArg(args, "face_index_2", 0);
        int mateAlign = ParseMateAlign(args);
        bool rebuild = BoolArg(args, "rebuild", defaultValue: true);
        double distanceM = DoubleArg(args, "distance_m", 0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return new { ok = false, error = $"{commandName} requires an assembly document." };
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1);
        Component2? second = FindComponent(assembly, null, component2);
        if (first is null || second is null)
        {
            return new { ok = false, error = "component_missing", component1, component2 };
        }

        try
        {
            doc.ClearSelection2(true);
            if (!SelectComponentMateEntity(doc, first, ref1, face1, append: false, mark: 1)
                || !SelectComponentMateEntity(doc, second, ref2, face2, append: true, mark: 2))
            {
                return new { ok = false, error = "selection_failed", ref1, ref2 };
            }

            if (mateType == (int)swMateType_e.swMateDISTANCE && distanceM != 0)
            {
                object? mateDataObj = Try(() => assembly.CreateMateData(mateType));
                if (mateDataObj is IDistanceMateFeatureData distanceMate)
                {
                    SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
                    object? entity1 = Try(() => selectionMgr?.GetSelectedObject6(1, -1));
                    object? entity2 = Try(() => selectionMgr?.GetSelectedObject6(2, -1));
                    if (entity1 is not null && entity2 is not null)
                    {
                        distanceMate.EntitiesToMate = new object[] { entity1, entity2 };
                        distanceMate.Distance = distanceM;
                        distanceMate.MateAlignment = mateAlign;
                        Feature? mateFeature = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
                        if (rebuild)
                        {
                            doc.EditRebuild3();
                        }

                        return new
                        {
                            ok = mateFeature is not null,
                            mateCreated = mateFeature is not null,
                            ref1,
                            ref2,
                            distanceM,
                            mateMethod = "CreateMate",
                        };
                    }
                }
            }

            object mateResult = CreateMateFromSelectionWithAlign(
                doc,
                assembly,
                first,
                second,
                ref1,
                ref2,
                mateType,
                mateAlign,
                rebuild);
            bool created = mateResult.GetType().GetProperty("mateCreated")?.GetValue(mateResult) as bool? == true
                || mateResult.GetType().GetProperty("alreadyConstrained")?.GetValue(mateResult) as bool? == true;
            return new { ok = created, mateResult };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message, ref1, ref2 };
        }
    }

    private static object CreateMateFromSelectionWithAlign(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        Component2 second,
        string ref1,
        string ref2,
        int mateType,
        int mateAlign,
        bool rebuild = true)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int selectedCount = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        (bool created, string method, int mateError, bool alreadyConstrained) = TryCreateMate(
            assembly,
            doc,
            mateType,
            mateAlign);

        if (rebuild)
        {
            doc.EditRebuild3();
        }

        return new
        {
            document = DescribeDocument(doc),
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            selectedCount,
            mateError,
            mateCreated = created,
            alreadyConstrained,
            mateAlign,
            mateMethod = method,
            ok = created || alreadyConstrained,
        };
    }

    private static object AddMateFromComponentRefs(
        JsonElement? args,
        int mateType,
        int mateAlign,
        string commandName)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        int face1 = (int)DoubleArg(args, "face_index_1", 0);
        int face2 = (int)DoubleArg(args, "face_index_2", 0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException($"{commandName} requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");

        doc.ClearSelection2(true);
        if (!SelectComponentMateEntity(doc, first, ref1, face1, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {ref1} on {component1}");
        }

        if (!SelectComponentMateEntity(doc, second, ref2, face2, append: true, mark: 2))
        {
            throw new InvalidOperationException($"Failed to select {ref2} on {component2}");
        }

        return CreateMateFromSelection(doc, assembly, first, second, ref1, ref2, mateType, mateAlign);
    }

    private static object ProbePitchAxis(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string refAxis = StringArg(args, "ref_axis") ?? "shaft_axis";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");
        ModelDoc2? cdoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (cdoc is null)
        {
            throw new InvalidOperationException("No part doc.");
        }

        double[]? axisDir = null;
        double[]? axisOrigin = null;
        Feature? axisFeature = FindFeatureByName(cdoc, refAxis);
        if (axisFeature is not null && Try(() => axisFeature.GetSpecificFeature2()) is RefAxis ra)
        {
            double[]? p = Try(() => ra.GetRefAxisParams()) as double[];
            if (p is { Length: >= 6 })
            {
                axisOrigin = [p[0], p[1], p[2]];
                axisDir = NormalizeVector(p[3] - p[0], p[4] - p[1], p[5] - p[2]);
            }
        }

        var planes = new List<object>();
        foreach (string pn in new[] { "Front Plane", "Top Plane", "Right Plane", "output_face", "pitch_limit_ref" })
        {
            Feature? pf = FindFeatureByName(cdoc, pn);
            if (pf is null || Try(() => pf.GetSpecificFeature2()) is not RefPlane rp)
            {
                planes.Add(new { name = pn, found = false });
                continue;
            }

            MathTransform? t = Try(() => rp.Transform) as MathTransform;
            double[]? m = t?.ArrayData as double[];
            double[]? normal = m is { Length: >= 12 } ? NormalizeVector(m[6], m[7], m[8]) : null;
            double? dotAxis = (normal is not null && axisDir is not null)
                ? Math.Abs(Dot(normal, axisDir))
                : (double?)null;
            planes.Add(new { name = pn, found = true, normal, dotWithAxis = dotAxis });
        }

        // Find largest planar faces whose normal is perpendicular to the axis (good for angle-about-axis).
        var goodFaces = new List<object>();
        if (cdoc is IPartDoc partDoc && axisDir is not null)
        {
            object? bodiesObj = Try(() => partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true));
            if (bodiesObj is object[] bodies)
            {
                foreach (object bo in bodies)
                {
                    if (bo is not Body2 body)
                    {
                        continue;
                    }

                    if (Try(() => body.GetFaces()) is not object[] faces)
                    {
                        continue;
                    }

                    foreach (object fo in faces)
                    {
                        if (fo is not Face2 face
                            || Try(() => face.GetSurface()) is not Surface s
                            || (Try(() => s.IsPlane()) as bool? ?? false) != true)
                        {
                            continue;
                        }

                        double[]? pp = Try(() => s.PlaneParams) as double[];
                        if (pp is not { Length: >= 4 })
                        {
                            continue;
                        }

                        double[] nrm = NormalizeVector(pp[0], pp[1], pp[2]);
                        double dot = Math.Abs(Dot(nrm, axisDir));
                        double area = Try(() => face.GetArea()) as double? ?? 0.0;
                        if (dot < 0.2 && area > 0.0005)
                        {
                            goodFaces.Add(new { area, normal = nrm });
                        }
                    }
                }
            }
        }

        var topFaces = goodFaces
            .OrderByDescending(f => (double)(f.GetType().GetProperty("area")!.GetValue(f) ?? 0.0))
            .Take(5)
            .ToList();

        return new
        {
            component = Try(() => component.Name2),
            axisDir,
            axisOrigin,
            planes,
            perpFaceCount = goodFaces.Count,
            topPerpFaces = topFaces,
        };
    }

    private static object MatePitchLimit(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        string componentAxis = StringArg(args, "component_axis") ?? component1;
        string refAxis = StringArg(args, "ref_axis") ?? "shaft_axis";
        double minDeg = DoubleArg(args, "min_deg", -50.0);
        double maxDeg = DoubleArg(args, "max_deg", 180.0);
        bool save = BoolArg(args, "save", defaultValue: true);
        bool flip = BoolArg(args, "flip", defaultValue: false);
        bool dryRun = BoolArg(args, "dry_run", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_pitch_limit requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");
        Component2? axisComponent = FindComponent(assembly, null, componentAxis)
            ?? throw new InvalidOperationException($"Component not found: {componentAxis}");

        EnsureComponentResolved(first);
        EnsureComponentResolved(second);
        EnsureComponentResolved(axisComponent);

        const int axisMark = 4;
        const int expectedAxisType = (int)swSelectType_e.swSelDATUMAXES;

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;

        // Capture each entity COM object immediately after its own selection step,
        // because selecting the axis on the motor component clobbers the motor
        // plane's selection mark (SW reuses marks per component).
        doc.ClearSelection2(true);
        bool sel1 = SelectComponentReference(doc, first, ref1, append: false, mark: 1);
        object? entity1 = ResolveSelectedMateEntity(selectionMgr, 1);
        int? type1 = Try(() => selectionMgr?.GetSelectedObjectType3(1, 1)) as int?;

        doc.ClearSelection2(true);
        bool sel2 = SelectComponentReference(doc, second, ref2, append: false, mark: 1);
        object? entity2 = ResolveSelectedMateEntity(selectionMgr, 1);
        int? type2 = Try(() => selectionMgr?.GetSelectedObjectType3(1, 1)) as int?;

        doc.ClearSelection2(true);
        bool selAxis = SelectMateReferenceByName(doc, axisComponent, refAxis, append: false, mark: 1, expectedAxisType)
            || SelectComponentReference(doc, axisComponent, refAxis, append: false, mark: 1);
        object? axisEntity = ResolveSelectedMateEntity(selectionMgr, 1);
        int? typeAxis = Try(() => selectionMgr?.GetSelectedObjectType3(1, 1)) as int?;

        doc.ClearSelection2(true);

        bool haveEntities = entity1 is not null && entity2 is not null && axisEntity is not null;

        if (dryRun || !haveEntities)
        {
            return new
            {
                ok = false,
                error = dryRun ? "dry_run" : "selection_failed",
                sel1,
                sel2,
                selAxis,
                selectionType1 = type1,
                selectionType2 = type2,
                selectionTypeAxis = typeAxis,
                entity1Type = entity1?.GetType().FullName,
                entity2Type = entity2?.GetType().FullName,
                axisEntityType = axisEntity?.GetType().FullName,
                haveEntities,
            };
        }

        double minRad = minDeg * Math.PI / 180.0;
        double maxRad = maxDeg * Math.PI / 180.0;

        int mateError = -1;
        Feature? mateFeature = null;
        string mateMethod = "none";

        var attempts = new List<object>();
        // (useAxis, useLimit, angle0)
        (bool useAxis, bool useLimit, double angleDeg, string label)[] variants =
        [
            (true, true, 0.0, "axis+limit"),
            (false, true, 0.0, "noaxis+limit"),
            (true, false, 0.0, "axis+fixed0"),
            (false, false, 0.0, "noaxis+fixed0"),
            (true, false, 90.0, "axis+fixed90"),
        ];

        foreach (var v in variants)
        {
            if (mateFeature is not null)
            {
                break;
            }

            object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateANGLE));
            if (mateDataObj is not IAngleMateFeatureData angleMate)
            {
                continue;
            }

            angleMate.EntitiesToMate = new object[] { entity1!, entity2! };
            if (v.useAxis)
            {
                TryVoid(() => angleMate.ReferenceEntity = axisEntity);
            }

            TryVoid(() => angleMate.FlipDimension = flip);
            angleMate.Angle = v.angleDeg * Math.PI / 180.0;
            if (v.useLimit)
            {
                angleMate.MinimumAngle = minRad;
                angleMate.MaximumAngle = maxRad;
                TryVoid(() => angleMate.IsAdvancedMate = true);
            }

            angleMate.MateAlignment = (int)swMateAlign_e.swMateAlignALIGNED;

            Feature? f = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
            int err = mateDataObj is IMateFeatureData mfd
                ? Try(() => mfd.ErrorStatus) as int? ?? -1
                : -1;
            attempts.Add(new { v.label, created = f is not null, err });

            if (f is not null)
            {
                mateFeature = f;
                mateError = err;
                mateMethod = $"CreateMate({v.label})";
            }
        }

        bool created = mateFeature is not null;

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save && created)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            ok = created,
            mateFeature = Try(() => mateFeature?.Name),
            mateMethod,
            attempts,
            mateError,
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            componentAxis = Try(() => axisComponent.Name2),
            refAxis,
            minDeg,
            maxDeg,
            selectionType1 = type1,
            selectionType2 = type2,
            selectionTypeAxis = typeAxis,
            mateCount = CountAssemblyMates(doc),
            saved,
            errors,
            warnings,
        };
    }

    private static object MateLimitAngle(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        string componentAxis = StringArg(args, "component_axis") ?? component1;
        string refAxis = StringArg(args, "ref_axis") ?? "shaft_axis";
        double minDeg = DoubleArg(args, "min_deg", -50.0);
        double maxDeg = DoubleArg(args, "max_deg", 180.0);
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_limit_angle requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");
        Component2? axisComponent = FindComponent(assembly, null, componentAxis)
            ?? throw new InvalidOperationException($"Component not found: {componentAxis}");

        EnsureComponentResolved(first);
        EnsureComponentResolved(second);
        EnsureComponentResolved(axisComponent);

        const int axisMark = 4;
        const int expectedAxisType = (int)swSelectType_e.swSelDATUMAXES;

        doc.ClearSelection2(true);
        if (!SelectComponentReferenceOrBodyPlane(doc, first, ref1, refAxis, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {ref1} on {component1}");
        }

        if (!SelectComponentReferenceOrBodyPlane(doc, second, ref2, refAxis, append: true, mark: 2))
        {
            throw new InvalidOperationException($"Failed to select {ref2} on {component2}");
        }

        bool axisSelected = SelectMateReferenceByName(doc, axisComponent, refAxis, append: true, mark: axisMark, expectedAxisType)
            || SelectComponentReference(doc, axisComponent, refAxis, append: true, mark: axisMark);
        if (!axisSelected || !HasMarkedSelection(doc, axisMark, expectedAxisType))
        {
            DeselectMarkedObject(doc, axisMark);
            axisSelected = false;
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        object? entity1 = ResolveSelectedMateEntity(selectionMgr, 1);
        object? entity2 = ResolveSelectedMateEntity(selectionMgr, 2);
        object? axisEntity = axisSelected ? ResolveSelectedMateEntity(selectionMgr, axisMark) : null;

        if (entity1 is null || entity2 is null)
        {
            throw new InvalidOperationException(
                $"Limit angle mate entity resolution failed (entity1={entity1 is not null}, entity2={entity2 is not null}).");
        }

        if (axisEntity is null)
        {
            axisEntity = FindCylindricalFaceNearRefAxis(axisComponent, refAxis)
                ?? FindCylindricalFaceNearRefAxis(first, refAxis)
                ?? FindCylindricalFaceNearRefAxis(second, "joint_axis");
        }

        Feature? mateFeature = null;
        int errorStatus = -1;

        if (axisSelected)
        {
            mateFeature = TryCreateAngleLimitViaAddMate5(assembly, minDeg, maxDeg, out errorStatus);
        }

        if (mateFeature is null)
        {
            mateFeature = TryCreateHingeLimitMate(assembly, doc, first, second, entity1, entity2, minDeg, maxDeg);
            if (mateFeature is not null)
            {
                errorStatus = 0;
            }
        }

        if (mateFeature is null && axisEntity is not null)
        {
            mateFeature = TryCreateAngleLimitMate(assembly, entity1, entity2, axisEntity, minDeg, maxDeg, out errorStatus);
            if (mateFeature is null && axisSelected)
            {
                mateFeature = TryCreateAngleLimitViaAddMate5(assembly, minDeg, maxDeg, out int addMateError);
                if (mateFeature is null && addMateError != 0)
                {
                    errorStatus = addMateError;
                }
            }
        }

        if (mateFeature is null)
        {
            mateFeature = TryCreateHingeLimitMate(
                assembly,
                doc,
                first,
                second,
                axisComponent,
                ref1,
                ref2,
                refAxis,
                minDeg,
                maxDeg);
            if (mateFeature is not null)
            {
                errorStatus = 0;
            }
        }

        string usedRef1 = ref1;
        string usedRef2 = ref2;
        if (mateFeature is null)
        {
            string[][] planePairs =
            [
                ["Right Plane", "Front Plane"],
                ["Right Plane", "Top Plane"],
                ["Top Plane", "Front Plane"],
                ["Top Plane", "Right Plane-RS03"],
            ];

            foreach (string[] pair in planePairs)
            {
                if (pair[0].Equals(usedRef1, StringComparison.OrdinalIgnoreCase)
                    && pair[1].Equals(usedRef2, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Feature? candidateMate = TryLimitAngleWithPlanePair(
                    assembly,
                    doc,
                    first,
                    second,
                    axisComponent,
                    pair[0],
                    pair[1],
                    refAxis,
                    minDeg,
                    maxDeg,
                    out int candidateError);
                if (candidateMate is not null)
                {
                    mateFeature = candidateMate;
                    usedRef1 = pair[0];
                    usedRef2 = pair[1];
                    errorStatus = candidateError;
                    break;
                }
            }
        }

        if (mateFeature is null)
        {
            throw new InvalidOperationException(
                $"CreateMate(limit angle) returned null (errorStatus={errorStatus}). "
                + "Use non-coincident planes that rotate about the axis (not mount faces).");
        }

        doc.ClearSelection2(true);
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
            mateFeature = Try(() => mateFeature.Name),
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1 = usedRef1,
            ref2 = usedRef2,
            componentAxis = Try(() => axisComponent.Name2),
            refAxis,
            minDeg,
            maxDeg,
            mateCount = CountAssemblyMates(doc),
            saved,
            errors,
            warnings,
            errorStatus,
        };
    }

    private static void EnsureComponentResolved(Component2 component)
    {
        TryVoid(() => component.SetSuppression2((int)swComponentSuppressionState_e.swComponentResolved));
        Component2? parent = Try(() => component.GetParent()) as Component2;
        if (parent is not null)
        {
            EnsureComponentResolved(parent);
        }
    }

    private static object? GetComponentReferenceEntity(Component2 component, string referenceName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return null;
        }

        Feature? feature = FindFeatureByName(componentDoc, referenceName);
        if (feature is null)
        {
            return null;
        }

        string? refType = Try(() => feature.GetTypeName2()) as string;
        object? specific = Try(() => feature.GetSpecificFeature2());
        if (specific is not null)
        {
            object? corresponding = Try(() => component.GetCorrespondingEntity(specific));
            if (corresponding is not null)
            {
                return corresponding;
            }
        }

        if (refType == "RefAxis" && specific is RefAxis refAxis)
        {
            object? correspondingAxis = Try(() => component.GetCorrespondingEntity(refAxis));
            if (correspondingAxis is not null)
            {
                return correspondingAxis;
            }
        }

        if (refType == "RefPlane")
        {
            object? facesObj = Try(() => feature.GetFaces());
            if (facesObj is object[] planeFaces && planeFaces.Length > 0)
            {
                object? asmFace = Try(() => component.GetCorrespondingEntity(planeFaces[0]));
                if (asmFace is not null)
                {
                    return asmFace;
                }
            }
        }

        return null;
    }

    private static bool SelectMateReferenceByName(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        bool append,
        int mark,
        int expectedType)
    {
        string? componentName = Try(() => component.Name2) as string;
        if (componentName is null)
        {
            return false;
        }

        string selectName = $"{referenceName}@{componentName}";
        if (componentName.Contains('/'))
        {
            string[] parts = componentName.Split('/');
            if (parts.Length == 2)
            {
                selectName = $"{referenceName}@{parts[1]}@{parts[0]}";
            }
        }
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        string? refType = componentDoc is null
            ? null
            : Try(() => FindFeatureByName(componentDoc, referenceName)?.GetTypeName2()) as string;

        string[] selectionTypes = refType switch
        {
            "RefPlane" => ["PLANE", "EXTREFPLANE", "REFPLANE"],
            "RefAxis" => ["EXTREFAXIS", "AXIS", "REFAXIS"],
            _ => ["PLANE", "EXTREFPLANE", "EXTREFAXIS", "AXIS"],
        };

        foreach (string selectionType in selectionTypes)
        {
            if (assemblyDoc.Extension.SelectByID2(
                    selectName,
                    selectionType,
                    0,
                    0,
                    0,
                    append,
                    mark,
                    null,
                    0)
                && HasMarkedSelection(assemblyDoc, mark, expectedType))
            {
                return true;
            }
        }

        if (componentName.Contains('/'))
        {
            string[] parts = componentName.Split('/');
            if (parts.Length == 2)
            {
                string[] nestedNames =
                [
                    $"{referenceName}@{parts[1]}@{parts[0]}",
                    $"{referenceName}@{parts[0]}/{parts[1]}",
                ];
                foreach (string nestedName in nestedNames)
                {
                    foreach (string selectionType in selectionTypes)
                    {
                        if (assemblyDoc.Extension.SelectByID2(
                                nestedName,
                                selectionType,
                                0,
                                0,
                                0,
                                append,
                                mark,
                                null,
                                0)
                            && HasMarkedSelection(assemblyDoc, mark, expectedType))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        if (SelectComponentReference(assemblyDoc, component, referenceName, append, mark)
            && HasMarkedSelection(assemblyDoc, mark, expectedType))
        {
            return true;
        }

        return false;
    }

    private static bool HasMarkedSelection(ModelDoc2 assemblyDoc, int mark, int expectedType)
    {
        SelectionMgr? selectionMgr = Try(() => assemblyDoc.SelectionManager) as SelectionMgr;
        if (selectionMgr is null)
        {
            return false;
        }

        int? selectionType = Try(() => selectionMgr.GetSelectedObjectType3(1, mark)) as int?;
        return selectionType == expectedType;
    }

    private static object? ResolveSelectedMateEntity(SelectionMgr? selectionMgr, int mark)
    {
        if (selectionMgr is null)
        {
            return null;
        }

        object? entity = Try(() => selectionMgr.GetSelectedObject6(1, mark));
        if (entity is not null)
        {
            return entity;
        }

        return Try(() => selectionMgr.GetSelectedObject6(1, -1));
    }

    private static Feature? TryLimitAngleWithPlanePair(
        IAssemblyDoc assembly,
        ModelDoc2 doc,
        Component2 first,
        Component2 second,
        Component2 axisComponent,
        string ref1,
        string ref2,
        string refAxis,
        double minDeg,
        double maxDeg,
        out int errorStatus)
    {
        errorStatus = -1;
        const int axisMark = 4;
        const int expectedAxisType = (int)swSelectType_e.swSelDATUMAXES;

        doc.ClearSelection2(true);
        if (!SelectComponentReferenceOrBodyPlane(doc, first, ref1, refAxis, append: false, mark: 1)
            || !SelectComponentReferenceOrBodyPlane(doc, second, ref2, refAxis, append: true, mark: 2))
        {
            return null;
        }

        bool axisSelected = SelectMateReferenceByName(doc, axisComponent, refAxis, append: true, mark: axisMark, expectedAxisType)
            || SelectComponentReference(doc, axisComponent, refAxis, append: true, mark: axisMark);
        if (!axisSelected || !HasMarkedSelection(doc, axisMark, expectedAxisType))
        {
            DeselectMarkedObject(doc, axisMark);
            return null;
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        object? entity1 = ResolveSelectedMateEntity(selectionMgr, 1);
        object? entity2 = ResolveSelectedMateEntity(selectionMgr, 2);
        object? axisEntity = ResolveSelectedMateEntity(selectionMgr, axisMark);
        if (entity1 is null || entity2 is null || axisEntity is null)
        {
            return null;
        }

        Feature? mateFeature = TryCreateAngleLimitViaAddMate5(assembly, minDeg, maxDeg, out errorStatus);
        if (mateFeature is not null)
        {
            return mateFeature;
        }

        mateFeature = TryCreateAngleLimitMate(assembly, entity1, entity2, axisEntity, minDeg, maxDeg, out errorStatus);
        if (mateFeature is not null)
        {
            return mateFeature;
        }

        return TryCreateHingeLimitMate(assembly, doc, first, second, entity1, entity2, minDeg, maxDeg);
    }

    private static Feature? TryCreateAngleLimitMate(
        IAssemblyDoc assembly,
        object planeEntity1,
        object planeEntity2,
        object axisEntity,
        double minDeg,
        double maxDeg,
        out int errorStatus)
    {
        errorStatus = -1;
        object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateANGLE));
        if (mateDataObj is not IAngleMateFeatureData angleMate)
        {
            return null;
        }

        foreach ((double minVal, double maxVal) in new[] { (minDeg, maxDeg), (minDeg * Math.PI / 180.0, maxDeg * Math.PI / 180.0) })
        {
            angleMate.EntitiesToMate = new object[] { planeEntity1, planeEntity2 };
            angleMate.ReferenceEntity = axisEntity;
            angleMate.Angle = 0.0;
            angleMate.MinimumAngle = minVal;
            angleMate.MaximumAngle = maxVal;
            angleMate.MateAlignment = (int)swMateAlign_e.swMateAlignALIGNED;
            TryVoid(() => angleMate.IsAdvancedMate = true);

            Feature? mateFeature = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
            errorStatus = mateDataObj is IMateFeatureData mateData
                ? Try(() => mateData.ErrorStatus) as int? ?? -1
                : -1;
            if (mateFeature is not null)
            {
                return mateFeature;
            }
        }

        return null;
    }

    private static Feature? TryCreateAngleLimitViaAddMate5(
        IAssemblyDoc assembly,
        double minDeg,
        double maxDeg,
        out int mateError)
    {
        mateError = -1;
        double minRad = minDeg * Math.PI / 180.0;
        double maxRad = maxDeg * Math.PI / 180.0;

        foreach ((double minVal, double maxVal) in new[] { (minDeg, maxDeg), (minRad, maxRad) })
        {
            Mate2? mate = assembly.AddMate5(
                (int)swMateType_e.swMateANGLE,
                (int)swMateAlign_e.swMateAlignALIGNED,
                false,
                0,
                0,
                0,
                0,
                0,
                0.0,
                maxVal,
                minVal,
                false,
                false,
                0,
                out mateError) as Mate2;

            if (mate is not null)
            {
                return mate as Feature;
            }
        }

        return null;
    }

    private static Feature? TryCreateHingeLimitMate(
        IAssemblyDoc assembly,
        ModelDoc2 doc,
        Component2 first,
        Component2 second,
        object planeEntity1,
        object planeEntity2,
        double minDeg,
        double maxDeg)
    {
        object? cylEntity1 = FindCylindricalFaceNearRefAxis(first, "shaft_axis")
            ?? FindCylindricalFaceNearRefAxis(first, null);
        object? cylEntity2 = FindCylindricalFaceNearRefAxis(second, "joint_axis")
            ?? FindCylindricalFaceNearRefAxis(second, null);
        if (cylEntity1 is null || cylEntity2 is null)
        {
            return null;
        }

        object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateHINGE));
        if (mateDataObj is not IHingeMateFeatureData hingeMate)
        {
            return null;
        }

        hingeMate.EntitiesToMate[0] = planeEntity1;
        hingeMate.EntitiesToMate[1] = planeEntity2;
        hingeMate.EntitiesToMate[2] = cylEntity1;
        hingeMate.EntitiesToMate[3] = cylEntity2;
        hingeMate.AngleSelection = true;
        hingeMate.Angle = 0.0;
        hingeMate.MinVal = minDeg;
        hingeMate.MaxVal = maxDeg;
        hingeMate.MateAlignment = (int)swMateAlign_e.swMateAlignALIGNED;

        Feature? mateFeature = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
        if (mateFeature is not null)
        {
            return mateFeature;
        }

        hingeMate.MinVal = minDeg * Math.PI / 180.0;
        hingeMate.MaxVal = maxDeg * Math.PI / 180.0;
        return Try(() => assembly.CreateMate(mateDataObj)) as Feature;
    }

    private static Feature? TryCreateHingeLimitMate(
        IAssemblyDoc assembly,
        ModelDoc2 doc,
        Component2 first,
        Component2 second,
        Component2 axisComponent,
        string ref1,
        string ref2,
        string refAxis,
        double minDeg,
        double maxDeg)
    {
        doc.ClearSelection2(true);
        if (!SelectComponentReference(doc, first, ref1, append: false, mark: 1)
            || !SelectComponentReference(doc, second, ref2, append: true, mark: 2))
        {
            return null;
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        object? entity1 = ResolveSelectedMateEntity(selectionMgr, 1);
        object? entity2 = ResolveSelectedMateEntity(selectionMgr, 2);
        if (entity1 is null || entity2 is null)
        {
            return null;
        }

        return TryCreateHingeLimitMate(assembly, doc, first, second, entity1, entity2, minDeg, maxDeg);
    }

    private static object? FindSmallestCylindricalFaceEntity(Component2 component)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is not IPartDoc partDoc)
        {
            return null;
        }

        object? bodiesObj = Try(() => partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        Face2? bestFace = null;
        double bestArea = double.MaxValue;
        foreach (object bodyObj in bodies)
        {
            if (bodyObj is not Body2 body)
            {
                continue;
            }

            object? facesObj = Try(() => body.GetFaces());
            if (facesObj is not object[] faces)
            {
                continue;
            }

            foreach (object faceObj in faces)
            {
                if (faceObj is not Face2 face)
                {
                    continue;
                }

                if (Try(() => face.GetSurface()) is not Surface surface
                    || (Try(() => surface.IsCylinder()) as bool? ?? false) != true)
                {
                    continue;
                }

                double area = Try(() => face.GetArea()) as double? ?? 0.0;
                if (area <= 0.0 || area >= bestArea)
                {
                    continue;
                }

                bestArea = area;
                bestFace = face;
            }
        }

        if (bestFace is null)
        {
            return null;
        }

        return Try(() => component.GetCorrespondingEntity(bestFace));
    }

    private static object? FindCylindricalFaceNearRefAxis(Component2 component, string? refAxisName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is not IPartDoc partDoc)
        {
            return null;
        }

        double[]? refOrigin = null;
        double[]? refDirection = null;
        if (!string.IsNullOrWhiteSpace(refAxisName))
        {
            Feature? axisFeature = FindFeatureByName(componentDoc, refAxisName);
            if (axisFeature is not null
                && Try(() => axisFeature.GetSpecificFeature2()) is RefAxis refAxis)
            {
                double[]? axisParams = Try(() => refAxis.GetRefAxisParams()) as double[];
                if (axisParams is { Length: >= 6 })
                {
                    refOrigin = [axisParams[0], axisParams[1], axisParams[2]];
                    refDirection = NormalizeVector(axisParams[3], axisParams[4], axisParams[5]);
                }
            }
        }

        object? bodiesObj = Try(() => partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        Face2? bestFace = null;
        double bestScore = double.MaxValue;
        foreach (object bodyObj in bodies)
        {
            if (bodyObj is not Body2 body)
            {
                continue;
            }

            object? facesObj = Try(() => body.GetFaces());
            if (facesObj is not object[] faces)
            {
                continue;
            }

            foreach (object faceObj in faces)
            {
                if (faceObj is not Face2 face)
                {
                    continue;
                }

                if (Try(() => face.GetSurface()) is not Surface surface
                    || (Try(() => surface.IsCylinder()) as bool? ?? false) != true)
                {
                    continue;
                }

                double radius = 0.0;
                if (Try(() => surface.CylinderParams) is double[] cylinderParams && cylinderParams.Length >= 7)
                {
                    radius = cylinderParams[6];
                }

                if (radius <= 0.0)
                {
                    continue;
                }

                double area = Try(() => face.GetArea()) as double? ?? 0.0;
                double score = area;
                if (refOrigin is not null && refDirection is not null)
                {
                    double[]? cylParams = Try(() => surface.CylinderParams) as double[];
                    if (cylParams is { Length: >= 7 })
                    {
                        double[] cylOrigin = [cylParams[0], cylParams[1], cylParams[2]];
                        double[] cylDirection = NormalizeVector(cylParams[3], cylParams[4], cylParams[5]);
                        double alignment = Math.Abs(Dot(cylDirection, refDirection));
                        if (alignment < 0.95)
                        {
                            continue;
                        }

                        double axisDistance = DistanceBetweenLines(refOrigin, refDirection, cylOrigin, cylDirection);
                        if (axisDistance > 0.005)
                        {
                            continue;
                        }

                        score = radius;
                    }
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestFace = face;
                }
            }
        }

        if (bestFace is null)
        {
            return FindSmallestCylindricalFaceEntity(component);
        }

        return Try(() => component.GetCorrespondingEntity(bestFace));
    }

    private static object? FindLargestCylindricalFaceEntity(Component2 component)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is not IPartDoc partDoc)
        {
            return null;
        }

        object? bodiesObj = Try(() => partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        Face2? bestFace = null;
        double bestArea = 0.0;
        foreach (object bodyObj in bodies)
        {
            if (bodyObj is not Body2 body)
            {
                continue;
            }

            object? facesObj = Try(() => body.GetFaces());
            if (facesObj is not object[] faces)
            {
                continue;
            }

            foreach (object faceObj in faces)
            {
                if (faceObj is not Face2 face)
                {
                    continue;
                }

                if (Try(() => face.GetSurface()) is not Surface surface
                    || (Try(() => surface.IsCylinder()) as bool? ?? false) != true)
                {
                    continue;
                }

                double area = Try(() => face.GetArea()) as double? ?? 0.0;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestFace = face;
                }
            }
        }

        if (bestFace is null)
        {
            return null;
        }

        return Try(() => component.GetCorrespondingEntity(bestFace));
    }

    private static object DebugLimitAngle(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        string componentAxis = StringArg(args, "component_axis") ?? component1;
        string refAxis = StringArg(args, "ref_axis") ?? "shaft_axis";
        double minDeg = DoubleArg(args, "min_deg", -50.0);
        double maxDeg = DoubleArg(args, "max_deg", 180.0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");
        Component2? axisComponent = FindComponent(assembly, null, componentAxis)
            ?? throw new InvalidOperationException($"Component not found: {componentAxis}");

        ModelDoc2? axisComponentDoc = Try(() => axisComponent.GetModelDoc2()) as ModelDoc2;
        Feature? axisRefFeature = axisComponentDoc is null
            ? null
            : FindFeatureByName(axisComponentDoc, refAxis);
        string? axisFeatureType = axisRefFeature is null
            ? null
            : Try(() => axisRefFeature.GetTypeName2()) as string;

        doc.ClearSelection2(true);
        object? directEntity1 = GetComponentReferenceEntity(first, ref1);
        object? directEntity2 = GetComponentReferenceEntity(second, ref2);
        object? directAxisEntity = GetComponentReferenceEntity(axisComponent, refAxis);

        bool sel1 = SelectComponentReference(doc, first, ref1, append: false, mark: 1);
        bool sel2 = SelectComponentReference(doc, second, ref2, append: true, mark: 2);
        const int axisMark = 4;
        const int expectedAxisType = (int)swSelectType_e.swSelDATUMAXES;
        bool selAxis = SelectMateReferenceByName(doc, axisComponent, refAxis, append: true, mark: axisMark, expectedAxisType)
            || SelectComponentReference(doc, axisComponent, refAxis, append: true, mark: axisMark);
        if (selAxis && !HasMarkedSelection(doc, axisMark, expectedAxisType))
        {
            DeselectMarkedObject(doc, axisMark);
            selAxis = false;
        }

        var axisSelectionAttempts = new List<object>();
        if (!selAxis)
        {
            doc.ClearSelection2(true);
            sel1 = SelectComponentReference(doc, first, ref1, append: false, mark: 1);
            sel2 = SelectComponentReference(doc, second, ref2, append: true, mark: 2);
            string? axisComponentName = Try(() => axisComponent.Name2) as string;
            string? assemblyTitle = Try(() => doc.GetTitle()) as string;
            if (axisRefFeature is not null && axisComponentName is not null)
            {
                string axisFeatureName = Try(() => axisRefFeature.Name) as string ?? refAxis;
                List<string> selectNames = [$"{axisFeatureName}@{axisComponentName}"];
                if (assemblyTitle is not null)
                {
                    selectNames.Add($"{axisFeatureName}@{axisComponentName}@{assemblyTitle}");
                    string titleNoExt = System.IO.Path.GetFileNameWithoutExtension(assemblyTitle);
                    if (titleNoExt != assemblyTitle)
                    {
                        selectNames.Add($"{axisFeatureName}@{axisComponentName}@{titleNoExt}");
                    }
                }

                if (axisComponentName.Contains('/'))
                {
                    string[] parts = axisComponentName.Split('/');
                    if (parts.Length == 2)
                    {
                        selectNames.Add($"{axisFeatureName}@{parts[1]}@{parts[0]}");
                        if (assemblyTitle is not null)
                        {
                            selectNames.Add($"{axisFeatureName}@{parts[1]}@{parts[0]}@{assemblyTitle}");
                            string titleNoExt = System.IO.Path.GetFileNameWithoutExtension(assemblyTitle);
                            if (titleNoExt != assemblyTitle)
                            {
                                selectNames.Add($"{axisFeatureName}@{parts[1]}@{parts[0]}@{titleNoExt}");
                            }
                        }
                    }
                }

                foreach (string selectName in selectNames.Distinct())
                {
                    foreach (string selectionType in new[] { "EXTREFAXIS", "AXIS", "REFAXIS" })
                    {
                        bool selected = doc.Extension.SelectByID2(
                            selectName,
                            selectionType,
                            0,
                            0,
                            0,
                            true,
                            axisMark,
                            null,
                            0);
                        int? selectedType = Try(() => (doc.SelectionManager as SelectionMgr)?.GetSelectedObjectType3(1, axisMark)) as int?;
                        axisSelectionAttempts.Add(new
                        {
                            selectName,
                            selectionType,
                            selected,
                            selectedType,
                            validAxis = selectedType == expectedAxisType,
                        });
                        if (selected && selectedType != expectedAxisType)
                        {
                            DeselectMarkedObject(doc, axisMark);
                        }
                    }
                }
            }

            selAxis = SelectComponentReference(doc, axisComponent, refAxis, append: true, mark: axisMark)
                && HasMarkedSelection(doc, axisMark, expectedAxisType);
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int selectedCount = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;
        object? entity1 = directEntity1 ?? ResolveSelectedMateEntity(selectionMgr, 1);
        object? entity2 = directEntity2 ?? ResolveSelectedMateEntity(selectionMgr, 2);
        object? axisEntity = directAxisEntity;
        if (selAxis && HasMarkedSelection(doc, axisMark, expectedAxisType))
        {
            axisEntity ??= ResolveSelectedMateEntity(selectionMgr, axisMark);
        }

        int? type1 = Try(() => selectionMgr?.GetSelectedObjectType3(1, 1)) as int?;
        int? type2 = Try(() => selectionMgr?.GetSelectedObjectType3(1, 2)) as int?;
        int? typeAxis = Try(() => selectionMgr?.GetSelectedObjectType3(1, axisMark)) as int?;

        Feature? angleFeature = null;
        int angleErrorStatus = -1;
        object? mateDataObj = Try(() => assembly.CreateMateData((int)swMateType_e.swMateANGLE));
        if (mateDataObj is IAngleMateFeatureData angleMate && entity1 is not null && entity2 is not null && axisEntity is not null)
        {
            angleMate.EntitiesToMate = new object[] { entity1, entity2 };
            angleMate.ReferenceEntity = axisEntity;
            angleMate.Angle = 0.0;
            angleMate.MinimumAngle = minDeg;
            angleMate.MaximumAngle = maxDeg;
            angleMate.MateAlignment = (int)swMateAlign_e.swMateAlignALIGNED;
            TryVoid(() => angleMate.IsAdvancedMate = true);
            angleFeature = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
            angleErrorStatus = mateDataObj is IMateFeatureData mateData
                ? Try(() => mateData.ErrorStatus) as int? ?? -1
                : -1;
            if (angleFeature is null)
            {
                angleMate.MinimumAngle = minDeg * Math.PI / 180.0;
                angleMate.MaximumAngle = maxDeg * Math.PI / 180.0;
                angleFeature = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
                angleErrorStatus = mateDataObj is IMateFeatureData mateDataRetry
                    ? Try(() => mateDataRetry.ErrorStatus) as int? ?? angleErrorStatus
                    : angleErrorStatus;
            }
        }

        Feature? hingeFeature = entity1 is not null && entity2 is not null
            ? TryCreateHingeLimitMate(assembly, doc, first, second, entity1, entity2, minDeg, maxDeg)
            : null;

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        return new
        {
            directEntity1 = directEntity1 is not null,
            directEntity2 = directEntity2 is not null,
            directAxisEntity = directAxisEntity is not null,
            hasCylFace1 = FindCylindricalFaceNearRefAxis(first, "shaft_axis") is not null,
            hasCylFace2 = FindCylindricalFaceNearRefAxis(second, "joint_axis") is not null,
            axisSelectionAttempts,
            hingeFeature = Try(() => hingeFeature?.Name),
            sel1,
            sel2,
            selAxis,
            selectedCount,
            entity1Type = entity1?.GetType().FullName,
            entity2Type = entity2?.GetType().FullName,
            axisEntityType = axisEntity?.GetType().FullName,
            selectionType1 = type1,
            selectionType2 = type2,
            selectionTypeAxis = typeAxis,
            component1Name = Try(() => first.Name2),
            component2Name = Try(() => second.Name2),
            axisFeatureType,
            angleFeature = Try(() => angleFeature?.Name),
            angleErrorStatus,
            mateCount = CountAssemblyMates(doc),
        };
    }

}