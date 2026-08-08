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

        int mateAlign = ParseMateAlign(args);

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
            mateAlign);
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
            ParseMateAlign(args),
            "mate_coincident");
    }

    private static object MateParallel(JsonElement? args)
    {
        return AddMateFromComponentRefs(
            args,
            (int)swMateType_e.swMatePARALLEL,
            ParseMateAlign(args),
            "mate_parallel");
    }

    private static object MateTangent(JsonElement? args)
    {
        return AddMateFromComponentRefs(
            args,
            (int)swMateType_e.swMateTANGENT,
            ParseMateAlign(args),
            "mate_tangent");
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

    private static object MateLimitAngle(JsonElement? args)
    {
        // Angle mates require entities (marks 1/2) plus a reference axis (mark 67108864).
        const int AngleMateReferenceMark = 67108864;

        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");
        string axisRef = StringArg(args, "axis_ref") ?? "shoulder_roll_axis";
        string? axisComponentName = StringArg(args, "axis_component");
        double minDeg = DoubleArg(args, "min_angle_deg", -90);
        double maxDeg = DoubleArg(args, "max_angle_deg", 90);
        bool hasAngle = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("angle_deg", out _);
        double nominalDeg = hasAngle
            ? DoubleArg(args, "angle_deg", (minDeg + maxDeg) / 2.0)
            : (minDeg + maxDeg) / 2.0;
        int face1 = (int)DoubleArg(args, "face_index_1", 0);
        int face2 = (int)DoubleArg(args, "face_index_2", 0);

        if (minDeg > maxDeg)
        {
            (minDeg, maxDeg) = (maxDeg, minDeg);
        }

        nominalDeg = Math.Clamp(nominalDeg, Math.Min(minDeg, maxDeg), Math.Max(minDeg, maxDeg));

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

        Component2? axisComponent = null;
        if (!string.IsNullOrWhiteSpace(axisComponentName))
        {
            axisComponent = FindComponent(assembly, null, axisComponentName)
                ?? throw new InvalidOperationException($"Axis component not found: {axisComponentName}");
        }

        // Official CreateMate angle-mate flow: both entities Mark=1, reference axis Mark=67108864.
        // Use strict SelectByID2/Feature selects only — ray fallbacks can over-select and break CreateMate.
        doc.ClearSelection2(true);
        if (!SelectComponentPlaneOrAxisStrict(doc, first, ref1, append: false, mark: 1))
        {
            // Fall back to generic mate entity select only if strict plane/axis select fails.
            if (!SelectComponentMateEntity(doc, first, ref1, face1, append: false, mark: 1))
            {
                throw new InvalidOperationException($"Failed to select {ref1} on {component1}");
            }
        }

        if (!SelectComponentPlaneOrAxisStrict(doc, second, ref2, append: true, mark: 1))
        {
            if (!SelectComponentMateEntity(doc, second, ref2, face2, append: true, mark: 1))
            {
                throw new InvalidOperationException($"Failed to select {ref2} on {component2}");
            }
        }

        string? selectedAxisOwner = null;
        bool axisSelected = false;
        foreach ((Component2? owner, string ownerName) in new (Component2?, string)[]
                 {
                     (axisComponent, axisComponentName ?? string.Empty),
                     (first, component1),
                     (second, component2),
                     (null, "__assembly__"),
                 })
        {
            if (owner is not null)
            {
                if (SelectComponentPlaneOrAxisStrict(doc, owner, axisRef, append: true, mark: AngleMateReferenceMark)
                    || SelectComponentAxisFeature(doc, owner, axisRef, append: true, mark: AngleMateReferenceMark))
                {
                    axisSelected = true;
                    selectedAxisOwner = ownerName;
                    break;
                }
            }
            else if (SelectAssemblyReference(doc, axisRef, append: true, mark: AngleMateReferenceMark))
            {
                axisSelected = true;
                selectedAxisOwner = "__assembly__";
                break;
            }
        }

        if (!axisSelected)
        {
            throw new InvalidOperationException(
                $"Failed to select angle reference axis '{axisRef}'. Pass axis_component/axis_ref explicitly.");
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int selectedCount = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        // Axis SelectByID2 often also selects associated datum points with the same mark.
        // Pick entities by selection type, not by first index alone.
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
            }
        }

        object? entity1 = planeEntities.Count > 0 ? planeEntities[0] : null;
        object? entity2 = planeEntities.Count > 1 ? planeEntities[1] : null;
        // Fallback to mark-based fetch if type filtering missed planes (e.g. face selections).
        entity1 ??= Try(() => selectionMgr?.GetSelectedObject6(1, 1));
        entity2 ??= Try(() => selectionMgr?.GetSelectedObject6(2, 1));
        axisEntity ??= Try(() => selectionMgr?.GetSelectedObject6(1, AngleMateReferenceMark));

        // Capture COM entities, then clear selection so CreateMate uses only EntitiesToMate/ReferenceEntity.
        doc.ClearSelection2(true);

        int mateType = (int)swMateType_e.swMateANGLE;
        Feature? mateFeature = null;
        string method = "none";
        int mateError = 0;
        int? createMateErrorStatus = null;
        var attempts = new List<object>();
        HashSet<string> existingMateNames = CollectMateHealthEntries(doc)
            .Select(m => m.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void AttemptCreate(
            string attemptName,
            bool advanced,
            bool includeAxis,
            bool flip,
            int align)
        {
            if (mateFeature is not null || entity1 is null || entity2 is null)
            {
                return;
            }

            object? mateDataObj = Try(() => assembly.CreateMateData(mateType));
            if (mateDataObj is not IAngleMateFeatureData angleMate)
            {
                attempts.Add(new { attempt = attemptName, ok = false, error = "no_angle_mate_data" });
                return;
            }

            angleMate.IsAdvancedMate = advanced;
            angleMate.EntitiesToMate = new object[] { entity1, entity2 };
            if (includeAxis && axisEntity is not null)
            {
                angleMate.ReferenceEntity = axisEntity;
            }

            angleMate.Angle = nominalDeg * Math.PI / 180.0;
            if (advanced)
            {
                angleMate.MinimumAngle = minDeg * Math.PI / 180.0;
                angleMate.MaximumAngle = maxDeg * Math.PI / 180.0;
            }

            angleMate.FlipDimension = flip;
            angleMate.MateAlignment = align;
            Feature? created = Try(() => assembly.CreateMate(mateDataObj)) as Feature;
            int? status = mateDataObj is IMateFeatureData mateData
                ? Try(() => mateData.ErrorStatus) as int?
                : null;
            attempts.Add(new
            {
                attempt = attemptName,
                ok = created is not null,
                status,
                advanced,
                includeAxis,
                flip,
                align,
            });
            if (created is not null)
            {
                mateFeature = created;
                method = attemptName;
                createMateErrorStatus = status;
            }
            else if (status is int s)
            {
                createMateErrorStatus = s;
                mateError = s;
            }
        }

        // Prefer limit mate with axis; fall back through common angle-mate variants.
        AttemptCreate("CreateMateLimitAxisAligned", advanced: true, includeAxis: true, flip: false, align: (int)swMateAlign_e.swMateAlignALIGNED);
        AttemptCreate("CreateMateLimitAxisAnti", advanced: true, includeAxis: true, flip: false, align: (int)swMateAlign_e.swMateAlignANTI_ALIGNED);
        AttemptCreate("CreateMateLimitNoAxisAligned", advanced: true, includeAxis: false, flip: false, align: (int)swMateAlign_e.swMateAlignALIGNED);
        AttemptCreate("CreateMateLimitNoAxisAnti", advanced: true, includeAxis: false, flip: false, align: (int)swMateAlign_e.swMateAlignANTI_ALIGNED);
        AttemptCreate("CreateMateLimitFlip", advanced: true, includeAxis: true, flip: true, align: (int)swMateAlign_e.swMateAlignALIGNED);
        AttemptCreate("CreateMateStandardAngle", advanced: false, includeAxis: false, flip: false, align: (int)swMateAlign_e.swMateAlignALIGNED);

        bool mateCreated = mateFeature is not null;
        if (!mateCreated)
        {
            // Restore selections for AddMate3/AddMate5 fallbacks.
            SelectComponentPlaneOrAxisStrict(doc, first, ref1, append: false, mark: 1);
            SelectComponentPlaneOrAxisStrict(doc, second, ref2, append: true, mark: 1);
            if (axisComponent is not null)
            {
                SelectComponentPlaneOrAxisStrict(doc, axisComponent, axisRef, append: true, mark: AngleMateReferenceMark);
            }
            else
            {
                SelectAssemblyReference(doc, axisRef, append: true, mark: AngleMateReferenceMark);
            }

            // Older AddMate3 path used by many macros for angle mates.
            try
            {
                Mate2? mate3 = assembly.AddMate3(
                    mateType,
                    (int)swMateAlign_e.swMateAlignALIGNED,
                    false,
                    0,
                    0,
                    0,
                    0,
                    0,
                    nominalDeg * Math.PI / 180.0,
                    maxDeg * Math.PI / 180.0,
                    minDeg * Math.PI / 180.0,
                    false,
                    out int mate3Error) as Mate2;
                attempts.Add(new { attempt = "AddMate3", ok = mate3 is not null, status = mate3Error });
                if (mate3 is not null)
                {
                    method = "AddMate3";
                    mateCreated = true;
                    mateError = mate3Error;
                }
                else
                {
                    mateError = mate3Error;
                }
            }
            catch (Exception ex)
            {
                attempts.Add(new { attempt = "AddMate3", ok = false, error = ex.Message });
            }
        }

        if (!mateCreated)
        {
            SelectComponentPlaneOrAxisStrict(doc, first, ref1, append: false, mark: 1);
            SelectComponentPlaneOrAxisStrict(doc, second, ref2, append: true, mark: 1);
            if (axisComponent is not null)
            {
                SelectComponentPlaneOrAxisStrict(doc, axisComponent, axisRef, append: true, mark: AngleMateReferenceMark);
            }
            else
            {
                SelectAssemblyReference(doc, axisRef, append: true, mark: AngleMateReferenceMark);
            }

            Mate2? mate = assembly.AddMate5(
                mateType,
                (int)swMateAlign_e.swMateAlignALIGNED,
                false,
                0,
                0,
                0,
                0,
                0,
                nominalDeg * Math.PI / 180.0,
                maxDeg * Math.PI / 180.0,
                minDeg * Math.PI / 180.0,
                false,
                false,
                0,
                out mateError) as Mate2;
            if (mate is not null)
            {
                method = "AddMate5";
                mateCreated = true;
            }

            attempts.Add(new { attempt = "AddMate5", ok = mateCreated, status = mateError });
        }

        bool forceRebuildOk = ForceRebuildDocument(doc, out _);
        List<MateHealthEntry> postForceMates = CollectMateHealthEntries(doc);
        List<object> mateFailures = MateFailuresFrom(postForceMates);
        bool mateHealthy = mateFailures.Count == 0;
        string? mateName = Try(() => mateFeature?.Name) as string;
        MateHealthEntry? createdMateHealth = postForceMates
            .Where(m => m.Name is not null && !existingMateNames.Contains(m.Name))
            .LastOrDefault();
        mateName ??= createdMateHealth?.Name;
        MateHealthEntry? namedMateHealth = mateName is null
            ? null
            : postForceMates.FirstOrDefault(m =>
                string.Equals(m.Name, mateName, StringComparison.OrdinalIgnoreCase));
        int? postForceErrorCode = namedMateHealth?.ErrorCode ?? createdMateHealth?.ErrorCode;
        bool? isWarning = namedMateHealth?.IsWarning ?? createdMateHealth?.IsWarning;
        string? featureErrorName = postForceErrorCode is int featureErrorCode
            ? SwErrorDecoder.DecodeFeatureError(featureErrorCode).Name
            : namedMateHealth?.ErrorName ?? createdMateHealth?.ErrorName;
        bool ok = mateCreated && mateHealthy;

        (string errorName, string[] remediation) = SwErrorDecoder.DecodeMateError(mateError);

        return new
        {
            document = DescribeDocument(doc),
            ok,
            forceRebuildOk,
            mateHealthy,
            mateFailures,
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            axisRef,
            axisOwner = selectedAxisOwner,
            selectedCount,
            planeEntityCount = planeEntities.Count,
            hasEntity1 = entity1 is not null,
            hasEntity2 = entity2 is not null,
            hasAxisEntity = axisEntity is not null,
            minAngleDeg = minDeg,
            maxAngleDeg = maxDeg,
            isAdvancedMate = true,
            mateCreated,
            mateName,
            mateError,
            createMateErrorStatus,
            mateErrorName = errorName,
            remediation,
            mateMethod = method,
            attempts,
            mateCount = CountAssemblyMates(doc),
            forceOk = forceRebuildOk,
            errorCode = postForceErrorCode,
            postForceErrorCode,
            postForceErrorName = featureErrorName,
            featureErrorName,
            isWarning,
        };
    }

    private static object SetMateLimitAngle(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string mateName = RequiredStringArg(args, "mate_name");
        bool hasMin = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("min_angle_deg", out _);
        bool hasMax = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("max_angle_deg", out _);
        bool hasAngle = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("angle_deg", out _);
        bool hasFlip = args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("flip_dimension", out _);

        if (!hasMin && !hasMax && !hasAngle && !hasFlip)
        {
            throw new InvalidOperationException(
                "set_mate_limit_angle requires at least one of min_angle_deg, max_angle_deg, angle_deg, flip_dimension.");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_mate_limit_angle requires an assembly document.");
        }

        Feature mateFeature = FindFeatureByName(doc, mateName)
            ?? throw new InvalidOperationException($"Mate feature not found: {mateName}");
        if (Try(() => mateFeature.GetDefinition()) is not IAngleMateFeatureData editable)
        {
            throw new InvalidOperationException($"Mate '{mateName}' is not an angle/limit-angle mate.");
        }

        double beforeMinDeg = (Try(() => editable.MinimumAngle) as double? ?? 0) * 180.0 / Math.PI;
        double beforeMaxDeg = (Try(() => editable.MaximumAngle) as double? ?? 0) * 180.0 / Math.PI;
        double beforeAngleDeg = (Try(() => editable.Angle) as double? ?? 0) * 180.0 / Math.PI;
        bool beforeFlip = Try(() => editable.FlipDimension) as bool? ?? false;
        bool beforeAdvanced = Try(() => editable.IsAdvancedMate) as bool? ?? false;

        double minDeg = hasMin ? DoubleArg(args, "min_angle_deg", beforeMinDeg) : beforeMinDeg;
        double maxDeg = hasMax ? DoubleArg(args, "max_angle_deg", beforeMaxDeg) : beforeMaxDeg;
        if (minDeg > maxDeg)
        {
            (minDeg, maxDeg) = (maxDeg, minDeg);
        }

        double angleDeg = hasAngle
            ? DoubleArg(args, "angle_deg", beforeAngleDeg)
            : Math.Clamp(beforeAngleDeg, minDeg, maxDeg);
        bool flip = hasFlip ? BoolArg(args, "flip_dimension", beforeFlip) : beforeFlip;

        editable.IsAdvancedMate = true;
        editable.MinimumAngle = minDeg * Math.PI / 180.0;
        editable.MaximumAngle = maxDeg * Math.PI / 180.0;
        editable.Angle = angleDeg * Math.PI / 180.0;
        editable.FlipDimension = flip;

        bool modifyOk = Try(() => mateFeature.ModifyDefinition(editable, doc, null)) as bool? ?? false;
        bool forceRebuildOk = ForceRebuildDocument(doc, out _);
        List<MateHealthEntry> postForceMates = CollectMateHealthEntries(doc);
        List<object> mateFailures = MateFailuresFrom(postForceMates);
        bool mateHealthy = mateFailures.Count == 0;

        IAngleMateFeatureData? afterDef = Try(() => mateFeature.GetDefinition()) as IAngleMateFeatureData;
        MateHealthEntry? mateHealth = postForceMates.FirstOrDefault(m =>
            string.Equals(m.Name, mateName, StringComparison.OrdinalIgnoreCase));
        int? postForceErrorCode = mateHealth?.ErrorCode;
        bool? isWarning = mateHealth?.IsWarning;
        if (postForceErrorCode is null)
        {
            try
            {
                bool warning = false;
                postForceErrorCode = mateFeature.GetErrorCode2(out warning);
                isWarning = warning;
            }
            catch
            {
                postForceErrorCode = Try(() => mateFeature.GetErrorCode()) as int?;
            }
        }

        string? featureErrorName = postForceErrorCode is int featureErrorCode
            ? SwErrorDecoder.DecodeFeatureError(featureErrorCode).Name
            : mateHealth?.ErrorName;
        bool ok = modifyOk && mateHealthy;

        return new
        {
            document = DescribeDocument(doc),
            ok,
            forceRebuildOk,
            mateHealthy,
            mateFailures,
            mateName = Try(() => mateFeature.Name),
            modifyOk,
            errorCode = postForceErrorCode,
            forceOk = forceRebuildOk,
            postForceErrorCode,
            postForceErrorName = featureErrorName,
            featureErrorName,
            isWarning,
            before = new
            {
                isAdvancedMate = beforeAdvanced,
                minAngleDeg = beforeMinDeg,
                maxAngleDeg = beforeMaxDeg,
                angleDeg = beforeAngleDeg,
                flipDimension = beforeFlip,
            },
            after = new
            {
                isAdvancedMate = Try(() => afterDef?.IsAdvancedMate) as bool?,
                minAngleDeg = afterDef is null
                    ? (double?)null
                    : (Try(() => afterDef.MinimumAngle) as double? ?? 0) * 180.0 / Math.PI,
                maxAngleDeg = afterDef is null
                    ? (double?)null
                    : (Try(() => afterDef.MaximumAngle) as double? ?? 0) * 180.0 / Math.PI,
                angleDeg = afterDef is null
                    ? (double?)null
                    : (Try(() => afterDef.Angle) as double? ?? 0) * 180.0 / Math.PI,
                flipDimension = Try(() => afterDef?.FlipDimension) as bool?,
            },
        };
    }

    private static bool SelectComponentPlaneOrAxisStrict(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        bool append,
        int mark)
    {
        string? componentName = Try(() => component.Name2) as string;
        if (componentName is null)
        {
            return false;
        }

        string? assemblyName = Try(() =>
        {
            string? pathName = assemblyDoc.GetPathName();
            return string.IsNullOrWhiteSpace(pathName)
                ? assemblyDoc.GetTitle()
                : Path.GetFileNameWithoutExtension(pathName);
        }) as string;

        string[] selectIds =
        [
            $"{referenceName}@{componentName}",
            string.IsNullOrWhiteSpace(assemblyName)
                ? string.Empty
                : $"{referenceName}@{componentName}@{assemblyName}",
        ];

        foreach (string selectionType in new[] { "PLANE", "AXIS" })
        {
            foreach (string selectId in selectIds)
            {
                if (string.IsNullOrWhiteSpace(selectId))
                {
                    continue;
                }

                if (assemblyDoc.Extension.SelectByID2(
                        selectId,
                        selectionType,
                        0,
                        0,
                        0,
                        append,
                        mark,
                        null,
                        0))
                {
                    return true;
                }
            }
        }

        Feature? feature = Try(() => component.FeatureByName(referenceName)) as Feature;
        if (feature is null)
        {
            ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
            feature = componentDoc is null ? null : FindFeatureByName(componentDoc, referenceName);
        }

        if (feature is null)
        {
            return false;
        }

        SelectData? selectData = CreateSelectData(assemblyDoc, mark);
        if (feature is Entity entity && selectData is not null
            && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
        {
            return true;
        }

        return Try(() => feature.Select2(append, mark)) as bool? ?? false;
    }

    private static bool SelectComponentAxisFeature(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string axisName,
        bool append,
        int mark)
    {
        Feature? axisFeature = Try(() => component.FeatureByName(axisName)) as Feature;
        if (axisFeature is null)
        {
            ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
            axisFeature = componentDoc is null ? null : FindFeatureByName(componentDoc, axisName);
        }

        if (axisFeature is null)
        {
            return false;
        }

        SelectData? selectData = CreateSelectData(assemblyDoc, mark);
        if (axisFeature is Entity entity && selectData is not null
            && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
        {
            return true;
        }

        return Try(() => axisFeature.Select2(append, mark)) as bool? ?? false;
    }

    private static bool SelectAssemblyReference(ModelDoc2 assemblyDoc, string referenceName, bool append, int mark)
    {
        Feature? feature = FindFeatureByName(assemblyDoc, referenceName);
        if (feature is null)
        {
            return false;
        }

        string? refType = Try(() => feature.GetTypeName2()) as string;
        string selectionType = refType switch
        {
            "CoordSys" => "COORDSYS",
            "RefPlane" => "PLANE",
            "RefAxis" => "AXIS",
            _ => "AXIS",
        };

        if (assemblyDoc.Extension.SelectByID2(
                referenceName,
                selectionType,
                0,
                0,
                0,
                append,
                mark,
                null,
                0))
        {
            return true;
        }

        SelectData? selectData = CreateSelectData(assemblyDoc, mark);
        if (feature is Entity entity && selectData is not null
            && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
        {
            return true;
        }

        return Try(() => feature.Select2(append, mark)) as bool? ?? false;
    }

    /// <summary>
    /// Probe whether an angle-limit mate clamps travel: pose the moving component at requested
    /// angles about a reference-component axis, rebuild, and compare requested vs actual angle.
    /// </summary>
    private static object ProbeAngleTravel(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string referenceComponentName = RequiredStringArg(args, "reference_component");
        string axis = (StringArg(args, "axis") ?? "x").Trim().ToLowerInvariant();
        double minDeg = DoubleArg(args, "min_angle_deg", double.NaN);
        double maxDeg = DoubleArg(args, "max_angle_deg", double.NaN);
        double overshootDeg = DoubleArg(args, "overshoot_deg", 10);
        double toleranceDeg = DoubleArg(args, "tolerance_deg", 1.5);
        bool restore = BoolArg(args, "restore", defaultValue: true);

        List<double> angles = [];
        if (args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("angles_deg", out JsonElement anglesEl)
            && anglesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in anglesEl.EnumerateArray())
            {
                if (entry.TryGetDouble(out double value))
                {
                    angles.Add(value);
                }
            }
        }

        if (angles.Count == 0)
        {
            if (double.IsNaN(minDeg) || double.IsNaN(maxDeg))
            {
                throw new InvalidOperationException(
                    "probe_angle_travel requires angles_deg[] or both min_angle_deg and max_angle_deg.");
            }

            if (minDeg > maxDeg)
            {
                (minDeg, maxDeg) = (maxDeg, minDeg);
            }

            double mid = (minDeg + maxDeg) / 2.0;
            angles.AddRange(
            [
                minDeg - overshootDeg,
                minDeg,
                mid,
                maxDeg,
                maxDeg + overshootDeg,
            ]);
        }

        if (axis is not ("x" or "y" or "z"))
        {
            throw new InvalidOperationException("axis must be one of: x, y, z (reference component axes).");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("probe_angle_travel requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? moving = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");
        Component2? reference = FindComponent(assembly, null, referenceComponentName)
            ?? throw new InvalidOperationException($"Reference component not found: {referenceComponentName}");

        if (Try(() => moving.IsFixed()) as bool? == true)
        {
            moving.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        double[] original = ReadComponentTransformMatrix(moving);
        double[] referenceMatrix = ReadComponentTransformMatrix(reference);
        double startDeg = MeasureRelativeAngleAboutReferenceAxisDeg(referenceMatrix, original, axis);
        List<object> samples = [];
        bool allPassed = true;

        try
        {
            DragOperator? dragOp = Try(() => assembly.GetDragOperator()) as DragOperator;
            bool usedDragOperator = false;
            if (dragOp is not null)
            {
                TryVoid(() => dragOp.AddComponent(moving, false));
                TryVoid(() => { dragOp.IsRelaxationEval = true; });
                usedDragOperator = true;
            }

            foreach (double requestedDeg in angles)
            {
                // Pose from the live transform with a delta about the reference axis.
                double[] currentMatrix = ReadComponentTransformMatrix(moving);
                double currentDeg = MeasureRelativeAngleAboutReferenceAxisDeg(
                    referenceMatrix,
                    currentMatrix,
                    axis);
                double deltaDeg = ShortestAngleDeltaDeg(requestedDeg, currentDeg);
                double[] posed = RotateMovingBasisAboutReferenceAxis(
                    referenceMatrix,
                    currentMatrix,
                    axis,
                    deltaDeg * Math.PI / 180.0);

                string poseMethod = "set_transform";
                if (usedDragOperator && dragOp is not null)
                {
                    bool dragged = TryDragComponentToMatrix(doc, assembly, dragOp, moving, posed);
                    poseMethod = dragged ? "drag_operator" : "set_transform_fallback";
                    if (!dragged)
                    {
                        ApplyComponentTransformMatrix(moving, posed);
                        doc.EditRebuild3();
                    }
                }
                else
                {
                    ApplyComponentTransformMatrix(moving, posed);
                    doc.EditRebuild3();
                }

                double[] actualMatrix = ReadComponentTransformMatrix(moving);
                double actualDeg = MeasureRelativeAngleAboutReferenceAxisDeg(
                    referenceMatrix,
                    actualMatrix,
                    axis);
                double errorDeg = ShortestAngleDeltaDeg(actualDeg, requestedDeg);

                bool expectClamped = !double.IsNaN(minDeg)
                    && !double.IsNaN(maxDeg)
                    && (requestedDeg < minDeg - 1e-9 || requestedDeg > maxDeg + 1e-9);
                double expectedDeg = expectClamped
                    ? ClampAngleToRangeDeg(requestedDeg, minDeg, maxDeg)
                    : requestedDeg;
                double expectedError = Math.Abs(ShortestAngleDeltaDeg(actualDeg, expectedDeg));
                bool passed = expectedError <= toleranceDeg;
                if (!passed)
                {
                    allPassed = false;
                }

                samples.Add(new
                {
                    requestedDeg,
                    actualDeg,
                    errorDeg,
                    expectedDeg,
                    expectClamped,
                    passed,
                    toleranceDeg,
                    deltaAppliedDeg = deltaDeg,
                    poseMethod,
                });
            }
        }
        finally
        {
            if (restore)
            {
                ApplyComponentTransformMatrix(moving, original);
                doc.EditRebuild3();
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => moving.Name2),
            referenceComponent = Try(() => reference.Name2),
            axis,
            startAngleDeg = startDeg,
            minAngleDeg = double.IsNaN(minDeg) ? (double?)null : minDeg,
            maxAngleDeg = double.IsNaN(maxDeg) ? (double?)null : maxDeg,
            overshootDeg,
            toleranceDeg,
            restored = restore,
            passed = allPassed,
            samples,
            mateCount = CountAssemblyMates(doc),
        };
    }

    private static double ShortestAngleDeltaDeg(double toDeg, double fromDeg)
    {
        double delta = toDeg - fromDeg;
        while (delta > 180) delta -= 360;
        while (delta < -180) delta += 360;
        return delta;
    }

    private static double ClampAngleToRangeDeg(double valueDeg, double minDeg, double maxDeg)
    {
        if (minDeg <= maxDeg)
        {
            return Math.Clamp(valueDeg, minDeg, maxDeg);
        }

        // Wrapped range support (not expected for current shoulder limits).
        return valueDeg;
    }

    private static bool TryDragComponentToMatrix(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        DragOperator dragOp,
        Component2 component,
        double[] matrix)
    {
        try
        {
            MathTransform? xform = Try(() => component.Transform2) as MathTransform;
            if (xform is null)
            {
                return false;
            }

            xform.ArrayData = matrix;
            bool began = Try(() => dragOp.BeginDrag()) as bool? ?? false;
            if (!began)
            {
                // Some SW builds return void/false inconsistently; continue and attempt Drag.
            }

            bool dragged = false;
            try
            {
                dragged = Try(() => dragOp.Drag(xform)) as bool? ?? false;
                if (!dragged)
                {
                    // DragAsUI is closer to interactive mate-respecting motion.
                    dragged = Try(() => dragOp.DragAsUI(xform)) as bool? ?? false;
                }
            }
            finally
            {
                TryVoid(() => dragOp.EndDrag());
            }

            doc.EditRebuild3();
            return dragged;
        }
        catch
        {
            return false;
        }
    }

    private static double[] RotateMovingBasisAboutReferenceAxis(
        double[] referenceMatrix,
        double[] movingMatrix,
        string axis,
        double deltaRad)
    {
        double[] axisVec = axis switch
        {
            "x" => [referenceMatrix[0], referenceMatrix[1], referenceMatrix[2]],
            "y" => [referenceMatrix[3], referenceMatrix[4], referenceMatrix[5]],
            "z" => [referenceMatrix[6], referenceMatrix[7], referenceMatrix[8]],
            _ => throw new InvalidOperationException($"Unsupported axis: {axis}"),
        };

        double axisLen = Math.Sqrt((axisVec[0] * axisVec[0]) + (axisVec[1] * axisVec[1]) + (axisVec[2] * axisVec[2]));
        if (axisLen < 1e-12)
        {
            throw new InvalidOperationException("Reference axis has zero length.");
        }

        axisVec[0] /= axisLen;
        axisVec[1] /= axisLen;
        axisVec[2] /= axisLen;

        double cos = Math.Cos(deltaRad);
        double sin = Math.Sin(deltaRad);
        double[] Rotate(double x, double y, double z)
        {
            double dot = (axisVec[0] * x) + (axisVec[1] * y) + (axisVec[2] * z);
            double cx = (axisVec[1] * z) - (axisVec[2] * y);
            double cy = (axisVec[2] * x) - (axisVec[0] * z);
            double cz = (axisVec[0] * y) - (axisVec[1] * x);
            return
            [
                (x * cos) + (cx * sin) + (axisVec[0] * dot * (1 - cos)),
                (y * cos) + (cy * sin) + (axisVec[1] * dot * (1 - cos)),
                (z * cos) + (cz * sin) + (axisVec[2] * dot * (1 - cos)),
            ];
        }

        double[] movingX = Rotate(movingMatrix[0], movingMatrix[1], movingMatrix[2]);
        double[] movingY = Rotate(movingMatrix[3], movingMatrix[4], movingMatrix[5]);
        double[] movingZ = Rotate(movingMatrix[6], movingMatrix[7], movingMatrix[8]);
        return
        [
            movingX[0], movingX[1], movingX[2],
            movingY[0], movingY[1], movingY[2],
            movingZ[0], movingZ[1], movingZ[2],
            movingMatrix[9], movingMatrix[10], movingMatrix[11],
            movingMatrix.Length > 12 ? movingMatrix[12] : 1,
            0, 0, 0,
        ];
    }

    private static double MeasureRelativeAngleAboutReferenceAxisDeg(
        double[] referenceMatrix,
        double[] movingMatrix,
        string axis)
    {
        // Express moving Y in reference coordinates; for axis=x use atan2(Yz, Yy).
        double[] mx =
        [
            Dot3(referenceMatrix, 0, movingMatrix, 0),
            Dot3(referenceMatrix, 3, movingMatrix, 0),
            Dot3(referenceMatrix, 6, movingMatrix, 0),
        ];
        double[] my =
        [
            Dot3(referenceMatrix, 0, movingMatrix, 3),
            Dot3(referenceMatrix, 3, movingMatrix, 3),
            Dot3(referenceMatrix, 6, movingMatrix, 3),
        ];
        double[] mz =
        [
            Dot3(referenceMatrix, 0, movingMatrix, 6),
            Dot3(referenceMatrix, 3, movingMatrix, 6),
            Dot3(referenceMatrix, 6, movingMatrix, 6),
        ];

        return axis switch
        {
            "x" => Math.Atan2(my[2], my[1]) * 180.0 / Math.PI,
            "y" => Math.Atan2(-mx[2], mx[0]) * 180.0 / Math.PI,
            "z" => Math.Atan2(mx[1], mx[0]) * 180.0 / Math.PI,
            _ => throw new InvalidOperationException($"Unsupported axis: {axis}"),
        };
    }

    private static double Dot3(double[] a, int aOffset, double[] b, int bOffset) =>
        (a[aOffset] * b[bOffset])
        + (a[aOffset + 1] * b[bOffset + 1])
        + (a[aOffset + 2] * b[bOffset + 2]);

    private static object MateRecordMacro(JsonElement? args) =>
        new
        {
            stub = true,
            path = StringArg(args, "path"),
            message = "mate_record_macro stub — capture manual mate selections for replay.",
        };

    private static object MateReplaySequence(JsonElement? args) =>
        new
        {
            stub = true,
            path = StringArg(args, "path"),
            sequenceId = StringArg(args, "sequence_id"),
            message = "mate_replay_sequence stub — replay a recorded mate macro sequence.",
        };

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