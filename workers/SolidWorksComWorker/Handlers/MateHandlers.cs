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

}