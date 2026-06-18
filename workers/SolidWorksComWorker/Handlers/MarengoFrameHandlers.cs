using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object TorsoFrameBuildMates(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        bool includeBrackets = BoolArg(args, "include_brackets", defaultValue: true);
        bool rebuildConfigs = BoolArg(args, "rebuild_configs", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("torso_frame_build_mates requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        var steps = new List<object>();

        const string layoutPrefix = "marengo_torso_layout";
        Component2? layout = FindComponent(assembly, null, layoutPrefix)
            ?? throw new InvalidOperationException($"Layout jig not found: {layoutPrefix}");

        TryVoid(() => layout.Visible = (int)swComponentVisibilityState_e.swComponentVisible);
        layout.Select4(false, null, false);
        assembly.FixComponent();
        doc.ClearSelection2(true);

        if (rebuildConfigs)
        {
            steps.Add(EnsureDepthRailConfigs(doc, inputPath));
        }

        (string targetPrefix, string layoutIce, string endPlane)[] railPlan =
        [
            ("frame_2020_vertical_back_left_340", "bottom_rail_back", "Plane1"),
            ("frame_2020_vertical_back_right_340", "bottom_rail_back", "Plane1"),
            ("frame_2020_vertical_front_left_340", "bottom_rail_front", "Plane1"),
            ("frame_2020_vertical_front_right_340", "bottom_rail_front", "Plane1"),
            ("frame_2020_bottom_left_100", "bottom_rail_left", "Plane1"),
            ("frame_2020_bottom_right_100", "bottom_rail_right", "Plane1"),
            ("frame_2020_bottom_front_110", "bottom_rail_front", "Plane1"),
            ("frame_2020_bottom_rear_110", "bottom_rail_back", "Plane1"),
            ("frame_2020_top_left_100", "top_rail_left", "Plane1"),
            ("frame_2020_top_right_100", "top_rail_right", "Plane1"),
            ("frame_2020_top_front_110", "top_rail_front", "Plane1"),
            ("frame_2020_top_rear_110", "top_rail_back", "Plane1"),
        ];

        foreach ((string targetPrefix, string layoutIce, string endPlane) in railPlan)
        {
            steps.Add(AlignAndMateRail(doc, assembly, layout, layoutPrefix, targetPrefix, layoutIce, endPlane));
        }

        if (includeBrackets)
        {
            steps.AddRange(SnapBracketsToCorners(doc, assembly, layoutPrefix));
        }

        TryVoid(() => layout.Visible = (int)swComponentVisibilityState_e.swComponentHidden);
        doc.EditRebuild3();

        int mateCount = CountAssemblyMates(doc);
        int fixedComponents = 0;
        object[]? roots = Try(() => assembly.GetComponents(false)) as object[];
        if (roots is not null)
        {
            foreach (object entry in roots)
            {
                if (entry is Component2 component
                    && Try(() => component.IsFixed()) as bool? == true
                    && (Try(() => component.Name2) as string)?.StartsWith("marengo_torso_layout", StringComparison.OrdinalIgnoreCase) == false)
                {
                    fixedComponents++;
                }
            }
        }

        int errors = 0;
        int warnings = 0;
        bool saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);

        return new
        {
            document = DescribeDocument(doc),
            steps,
            mateCount,
            fixedComponents,
            constraintMode = mateCount > 0 ? "mates" : "fixed",
            saved,
            errors,
            warnings,
        };
    }

    private static object EnsureDepthRailConfigs(ModelDoc2 frameDoc, string framePath)
    {
        const string vendorPath = "C:/code/marengo/cad/vendor/vendor_2020_black_extrusion.SLDPRT";
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 vendorDoc = OpenDocument(app, vendorPath);

        Configuration? existing = Try(() => vendorDoc.GetConfigurationByName("L100")) as Configuration;
        if (existing is null)
        {
            ConfigurationManager? manager = Try(() => vendorDoc.ConfigurationManager) as ConfigurationManager;
            TryVoid(() => manager?.AddConfiguration("L100", "", "", 0, "L085", ""));
        }

        vendorDoc.ShowConfiguration2("L100");
        Dimension? dimension = Try(() => vendorDoc.Parameter("D1@Boss-Extrude1")) as Dimension;
        if (dimension is not null)
        {
            dimension.SystemValue = 0.1;
        }

        vendorDoc.EditRebuild3();
        int vendorErrors = 0;
        int vendorWarnings = 0;
        vendorDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref vendorErrors, ref vendorWarnings);

        string[] depthRails =
        [
            "frame_2020_bottom_left_100",
            "frame_2020_bottom_right_100",
            "frame_2020_top_left_100",
            "frame_2020_top_right_100",
        ];

        foreach (string rail in depthRails)
        {
            Component2? component = FindComponent((IAssemblyDoc)frameDoc, null, rail);
            if (component is not null)
            {
                component.ReferencedConfiguration = "L100";
            }
        }

        frameDoc.EditRebuild3();
        return new { step = "ensure_depth_rail_configs", depthRails };
    }

    private static object AlignAndMateRail(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 layout,
        string layoutPrefix,
        string targetPrefix,
        string layoutIce,
        string endPlane)
    {
        Component2? target = FindComponent(assembly, null, targetPrefix);
        if (target is null)
        {
            return new { targetPrefix, layoutIce, ok = false, error = "target_not_found" };
        }

        if (Try(() => target.IsFixed()) as bool? == true)
        {
            target.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        double[]? iceBox = GetFeatureBoundingBoxInAssembly(layout, layoutIce);
        double[]? targetBox = GetComponentPlaneBoxInAssembly(target, endPlane)
            ?? Try(() => target.GetBox(false, false)) as double[];

        if (iceBox is null || targetBox is null)
        {
            return new { targetPrefix, layoutIce, ok = false, error = "bbox_unavailable" };
        }

        double[] iceCenter = BoxCenter(iceBox);
        double[] targetCenter = BoxCenter(targetBox);
        ApplyComponentTranslation(
            target,
            iceCenter[0] - targetCenter[0],
            iceCenter[1] - targetCenter[1],
            iceCenter[2] - targetCenter[2]);

        doc.EditRebuild3();

        string layoutPlane = layoutIce.StartsWith("top_rail", StringComparison.OrdinalIgnoreCase)
            ? "shoulder_plane"
            : "waist_bay_top";

        var mates = new List<object>();
        mates.Add(TryMate(
            doc,
            assembly,
            layoutPrefix,
            layoutPlane,
            0,
            targetPrefix,
            endPlane,
            0,
            (int)swMateType_e.swMateCOINCIDENT));
        mates.Add(TryMate(
            doc,
            assembly,
            layoutPrefix,
            "Right Plane",
            0,
            targetPrefix,
            "Front Plane",
            0,
            (int)swMateType_e.swMatePARALLEL));

        bool matesOk = mates.All(m => m is not null && TryGetOk(m));
        if (!matesOk)
        {
            target.Select4(false, null, false);
            assembly.FixComponent();
            doc.ClearSelection2(true);
            doc.EditRebuild3();
        }

        return new
        {
            targetPrefix,
            layoutIce,
            layoutPlane,
            endPlane,
            ok = true,
            constraint = matesOk ? "mates" : "fixed",
            mates,
        };
    }

    private static bool TryGetOk(object mateResult)
    {
        if (mateResult is null)
        {
            return false;
        }

        object? ok = mateResult.GetType().GetProperty("ok")?.GetValue(mateResult);
        return ok is bool value && value;
    }

    private static object TryMate(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        string component1,
        string ref1,
        int face1,
        string component2,
        string ref2,
        int face2,
        int mateType)
    {
        try
        {
            Component2? first = FindComponent(assembly, null, component1);
            Component2? second = FindComponent(assembly, null, component2);
            if (first is null || second is null)
            {
                return new { ref1, ref2, ok = false, error = "component_missing" };
            }

            doc.ClearSelection2(true);
            if (!SelectComponentMateEntity(doc, first, ref1, face1, append: false, mark: 1)
                || !SelectComponentMateEntity(doc, second, ref2, face2, append: true, mark: 2))
            {
                return new { ref1, ref2, ok = false, error = "selection_failed" };
            }

            object mateResult = CreateMateFromSelection(
                doc,
                assembly,
                first,
                second,
                ref1,
                ref2,
                mateType,
                (int)swMateAlign_e.swMateAlignALIGNED);
            bool created = mateResult.GetType().GetProperty("mateCreated")?.GetValue(mateResult) as bool? == true
                || mateResult.GetType().GetProperty("alreadyConstrained")?.GetValue(mateResult) as bool? == true;
            return new
            {
                ref1,
                ref2,
                ok = created,
                mateResult,
            };
        }
        catch (Exception ex)
        {
            return new { ref1, ref2, ok = false, error = ex.Message };
        }
    }

    private static List<object> SnapBracketsToCorners(ModelDoc2 doc, IAssemblyDoc assembly, string layoutPrefix)
    {
        var results = new List<object>();
        Component2? layout = FindComponent(assembly, null, layoutPrefix);
        if (layout is null)
        {
            results.Add(new { ok = false, error = "layout_missing" });
            return results;
        }

        double[]? envelope = GetFeatureBoundingBoxInAssembly(layout, "torso_outer_envelope");
        if (envelope is null)
        {
            results.Add(new { ok = false, error = "envelope_missing" });
            return results;
        }

        object[]? roots = Try(() => assembly.GetComponents(false)) as object[];
        if (roots is null)
        {
            return results;
        }

        int bracketIndex = 0;
        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            string? name = Try(() => component.Name2) as string;
            if (name is null || !name.StartsWith("bracket_2028_corner", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double[]? bracketBox = Try(() => component.GetBox(false, false)) as double[];
            if (bracketBox is null)
            {
                continue;
            }

            double[] bracketCenter = BoxCenter(bracketBox);
            double cornerX = bracketCenter[0] < BoxCenter(envelope)[0] ? envelope[0] : envelope[3];
            double cornerY = bracketCenter[1] < BoxCenter(envelope)[1] ? envelope[1] : envelope[4];
            double cornerZ = bracketCenter[2] < BoxCenter(envelope)[2] ? envelope[2] : envelope[5];

            ApplyComponentTranslation(
                component,
                cornerX - bracketCenter[0],
                cornerY - bracketCenter[1],
                cornerZ - bracketCenter[2]);

            component.Select4(false, null, false);
            assembly.FixComponent();
            doc.ClearSelection2(true);

            int index = bracketIndex;
            bracketIndex += 1;
            results.Add(new
            {
                component = name,
                bracketIndex = index,
                ok = true,
                constraint = "fixed",
                snappedTo = new[] { cornerX, cornerY, cornerZ },
            });
        }

        doc.EditRebuild3();
        return results;
    }

    private static bool SelectComponentMateEntity(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        int faceIndex,
        bool append,
        int mark)
    {
        if (SelectComponentReference(assemblyDoc, component, referenceName, append, mark))
        {
            return true;
        }

        return SelectComponentFeatureFace(assemblyDoc, component, referenceName, faceIndex, append, mark);
    }

    private static bool SelectComponentFeatureFace(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string featureName,
        int faceIndex,
        bool append,
        int mark)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return false;
        }

        Feature? feature = FindFeatureByName(componentDoc, featureName);
        if (feature is null)
        {
            return false;
        }

        Face2? face = GetFeatureFaceByIndex(feature, faceIndex);
        if (face is null)
        {
            return false;
        }

        Entity? entity = face as Entity;
        if (entity is null)
        {
            return false;
        }

        if (SelectFeatureFaceByRay(assemblyDoc, component, face, append, mark))
        {
            return true;
        }

        assemblyDoc.ClearSelection2(append);
        TryVoid(() => component.Select4(append, null, false));
        return Try(() => entity.Select2(append, mark)) as bool? ?? false;
    }

    private static bool SelectFeatureFaceByRay(
        ModelDoc2 assemblyDoc,
        Component2 component,
        Face2 face,
        bool append,
        int mark)
    {
        double[]? partBox = Try(() => face.GetBox()) as double[];
        double[] partPoint = partBox is { Length: >= 6 }
            ?
            [
                (partBox[0] + partBox[3]) / 2.0,
                (partBox[1] + partBox[4]) / 2.0,
                (partBox[2] + partBox[5]) / 2.0,
            ]
            : Try(() => face.GetClosestPointOn(0, 0, 0)) as double[] ?? [];
        if (partPoint.Length < 3)
        {
            return false;
        }

        MathTransform? transform = Try(() => component.Transform2) as MathTransform;
        if (transform is null)
        {
            return false;
        }

        double[] assemblyPoint = TransformPointManual(transform, partPoint[0], partPoint[1], partPoint[2]);
        double[] normal = Try(() => face.Normal) as double[] ?? [0, 0, 1];
        double[] assemblyNormal = TransformPointManual(transform, normal[0], normal[1], normal[2]);

        return assemblyDoc.Extension.SelectByRay(
            assemblyPoint[0],
            assemblyPoint[1],
            assemblyPoint[2],
            assemblyNormal[0],
            assemblyNormal[1],
            assemblyNormal[2],
            0.001,
            mark,
            append,
            0,
            0);
    }

    private static int GetLargestPlanarFaceIndex(Component2 component, string featureName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return 0;
        }

        Feature? feature = FindFeatureByName(componentDoc, featureName);
        if (feature is null)
        {
            return 0;
        }

        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is not object[] faces || faces.Length == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        double bestArea = 0;
        for (int i = 0; i < faces.Length; i++)
        {
            if (faces[i] is not Face2 face)
            {
                continue;
            }

            double area = Try(() => face.GetArea()) as double? ?? 0;
            if (area > bestArea)
            {
                bestArea = area;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int GetTopPlanarFaceIndex(Component2 component, string featureName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return 0;
        }

        Feature? feature = FindFeatureByName(componentDoc, featureName);
        if (feature is null)
        {
            return 0;
        }

        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is not object[] faces || faces.Length == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        double bestY = double.NegativeInfinity;
        for (int i = 0; i < faces.Length; i++)
        {
            if (faces[i] is not Face2 face)
            {
                continue;
            }

            double area = Try(() => face.GetArea()) as double? ?? 0;
            if (area < 0.001)
            {
                continue;
            }

            double[]? box = Try(() => face.GetBox()) as double[];
            if (box is not { Length: >= 6 })
            {
                continue;
            }

            double centerY = (box[1] + box[4]) / 2.0;
            if (centerY > bestY)
            {
                bestY = centerY;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Face2? GetFeatureFaceByIndex(Feature feature, int faceIndex)
    {
        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is not object[] faces || faces.Length == 0)
        {
            return null;
        }

        if (faceIndex >= 0 && faceIndex < faces.Length)
        {
            return faces[faceIndex] as Face2;
        }

        Face2? largest = null;
        double largestArea = 0;
        foreach (object entry in faces)
        {
            if (entry is not Face2 face)
            {
                continue;
            }

            double area = Try(() => face.GetArea()) as double? ?? 0;
            if (area > largestArea)
            {
                largestArea = area;
                largest = face;
            }
        }

        return largest;
    }

    private static List<object> ProbeFeatureFaceSummaries(Component2 component, string featureName)
    {
        var summaries = new List<object>();
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return summaries;
        }

        Feature? feature = FindFeatureByName(componentDoc, featureName);
        if (feature is null)
        {
            return summaries;
        }

        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is not object[] faces)
        {
            return summaries;
        }

        for (int i = 0; i < faces.Length; i++)
        {
            if (faces[i] is not Face2 face)
            {
                continue;
            }

            summaries.Add(new
            {
                index = i,
                area = Try(() => face.GetArea()),
                isPlanar = Try(() => (face.GetSurface() as Surface)?.IsPlane()),
            });
        }

        return summaries;
    }

    private static double[]? GetFeatureBoundingBoxInAssembly(Component2 component, string featureName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return null;
        }

        Feature? feature = FindFeatureByName(componentDoc, featureName);
        if (feature is null)
        {
            return null;
        }

        object? bodyObj = Try(() => feature.GetBody());
        if (bodyObj is Body2 body)
        {
            double[]? partBox = Try(() => body.GetBodyBox()) as double[];
            return partBox is null ? null : TransformPartBoxToAssembly(component, partBox);
        }

        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is object[] faces && faces.Length > 0)
        {
            return AggregateFaceBoxesInAssembly(component, faces);
        }

        return null;
    }

    private static double[]? GetComponentPlaneBoxInAssembly(Component2 component, string planeName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return null;
        }

        Feature? plane = FindFeatureByName(componentDoc, planeName);
        if (plane is null)
        {
            return null;
        }

        object? facesObj = Try(() => plane.GetFaces());
        if (facesObj is object[] faces && faces.Length > 0)
        {
            return AggregateFaceBoxesInAssembly(component, faces);
        }

        if (componentDoc is IPartDoc partDoc)
        {
            double[]? origin = GetComponentRefPlaneOriginInAssembly(component, planeName);
            if (origin is { Length: >= 3 })
            {
                const double slab = 0.0001;
                return
                [
                    origin[0] - slab,
                    origin[1] - slab,
                    origin[2] - slab,
                    origin[0] + slab,
                    origin[1] + slab,
                    origin[2] + slab,
                ];
            }

            double[]? partBox = Try(() => partDoc.GetPartBox(true)) as double[];
            return partBox is null ? null : TransformPartBoxToAssembly(component, partBox);
        }

        return null;
    }

    private static double[]? GetComponentRefPlaneOriginInAssembly(Component2 component, string planeName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return null;
        }

        Feature? plane = FindFeatureByName(componentDoc, planeName);
        if (plane is null)
        {
            return null;
        }

        if (Try(() => plane.GetSpecificFeature2()) is RefPlane refPlane)
        {
            MathTransform? planeTransform = Try(() => refPlane.Transform) as MathTransform;
            if (planeTransform?.ArrayData is double[] planeMatrix)
            {
                double[] normalized = NormalizeTransformMatrix(planeMatrix);
                MathTransform? componentTransform = Try(() => component.Transform2) as MathTransform;
                if (componentTransform is null)
                {
                    return [normalized[9], normalized[10], normalized[11]];
                }

                return TransformPointManual(
                    componentTransform,
                    normalized[9],
                    normalized[10],
                    normalized[11]);
            }
        }

        object? facesObj = Try(() => plane.GetFaces());
        if (facesObj is object[] faces && faces.Length > 0 && faces[0] is Face2 face)
        {
            double[]? partBox = Try(() => face.GetBox()) as double[];
            if (partBox is { Length: >= 6 })
            {
                MathTransform? componentTransform = Try(() => component.Transform2) as MathTransform;
                if (componentTransform is null)
                {
                    return BoxCenter(partBox);
                }

                return TransformPointManual(
                    componentTransform,
                    (partBox[0] + partBox[3]) / 2.0,
                    (partBox[1] + partBox[4]) / 2.0,
                    (partBox[2] + partBox[5]) / 2.0);
            }
        }

        return null;
    }

    private static double[] IdentityTransformMatrix() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    private static double[]? AggregateFaceBoxesInAssembly(Component2 component, object[] faces)
    {
        double[]? merged = null;
        foreach (object entry in faces)
        {
            if (entry is not Face2 face)
            {
                continue;
            }

            double[]? partBox = Try(() => face.GetBox()) as double[];
            if (partBox is null)
            {
                continue;
            }

            double[]? assemblyBox = TransformPartBoxToAssembly(component, partBox);
            merged = merged is null ? assemblyBox : MergeBoxes(merged, assemblyBox);
        }

        return merged;
    }

    private static double[]? TransformPartBoxToAssembly(Component2 component, double[] partBox)
    {
        if (partBox.Length < 6)
        {
            return null;
        }

        MathTransform? transform = Try(() => component.Transform2) as MathTransform;
        if (transform is null)
        {
            return partBox;
        }

        double[][] corners =
        [
            [partBox[0], partBox[1], partBox[2]],
            [partBox[3], partBox[1], partBox[2]],
            [partBox[0], partBox[4], partBox[2]],
            [partBox[3], partBox[4], partBox[2]],
            [partBox[0], partBox[1], partBox[5]],
            [partBox[3], partBox[1], partBox[5]],
            [partBox[0], partBox[4], partBox[5]],
            [partBox[3], partBox[4], partBox[5]],
        ];

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        foreach (double[] corner in corners)
        {
            double[] point = TransformPointManual(transform, corner[0], corner[1], corner[2]);
            if (point is null || point.Length < 3)
            {
                continue;
            }

            minX = Math.Min(minX, point[0]);
            minY = Math.Min(minY, point[1]);
            minZ = Math.Min(minZ, point[2]);
            maxX = Math.Max(maxX, point[0]);
            maxY = Math.Max(maxY, point[1]);
            maxZ = Math.Max(maxZ, point[2]);
        }

        return [minX, minY, minZ, maxX, maxY, maxZ];
    }

    private static double[] TransformPointManual(MathTransform transform, double x, double y, double z)
    {
        double[] matrix = Try(() => transform.ArrayData) as double[] ?? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        return
        [
            matrix[0] * x + matrix[1] * y + matrix[2] * z + matrix[9],
            matrix[3] * x + matrix[4] * y + matrix[5] * z + matrix[10],
            matrix[6] * x + matrix[7] * y + matrix[8] * z + matrix[11],
        ];
    }

    private static double[] MergeBoxes(double[] left, double[]? right)
    {
        if (right is null || right.Length < 6)
        {
            return left;
        }

        return
        [
            Math.Min(left[0], right[0]),
            Math.Min(left[1], right[1]),
            Math.Min(left[2], right[2]),
            Math.Max(left[3], right[3]),
            Math.Max(left[4], right[4]),
            Math.Max(left[5], right[5]),
        ];
    }

    private static double[] BoxCenter(double[] box) =>
        [(box[0] + box[3]) / 2.0, (box[1] + box[4]) / 2.0, (box[2] + box[5]) / 2.0];

    private static void ApplyComponentTranslation(Component2 component, double tx, double ty, double tz)
    {
        if (component.Transform2 is not MathTransform mathTransform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        double[] data = Try(() => mathTransform.ArrayData) as double[]
            ?? throw new InvalidOperationException("Transform data unavailable.");
        if (data.Length < 16)
        {
            throw new InvalidOperationException("Unexpected transform matrix size.");
        }

        data[9] += tx;
        data[10] += ty;
        data[11] += tz;
        mathTransform.ArrayData = data;
        component.Transform2 = mathTransform;
    }

    private static object SaveDocument(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to save.");
        }

        int errors = 0;
        int warnings = 0;
        bool saved = doc.Save3(
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ref errors,
            ref warnings);

        if (!saved || errors != 0)
        {
            throw new InvalidOperationException($"Save failed. errors={errors}, warnings={warnings}");
        }

        return new
        {
            document = DescribeDocument(doc),
            saved = true,
            errors,
            warnings,
        };
    }

    private static object CloseAllDocuments(JsonElement? args)
    {
        bool saveFirst = BoolArg(args, "save_first", defaultValue: false);
        ISldWorks app = AttachSolidWorks(startIfMissing: false);

        var closed = new List<object>();
        object? docsObj = Try(() => app.GetDocuments()) as object;
        if (docsObj is object[] docs)
        {
            foreach (object entry in docs.ToArray())
            {
                if (entry is not ModelDoc2 doc)
                {
                    continue;
                }

                string? title = Try(() => doc.GetTitle()) as string;
                string? path = Try(() => doc.GetPathName()) as string;
                if (saveFirst && !string.IsNullOrWhiteSpace(path))
                {
                    int saveErrors = 0;
                    int saveWarnings = 0;
                    TryVoid(() => doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings));
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    TryVoid(() => app.CloseDoc(title));
                }

                closed.Add(new { title, path });
            }
        }

        return new
        {
            closedCount = closed.Count,
            closed,
        };
    }

    private static object DiagnosePartSave(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("diagnose_part_save requires a part document.");
        }

        var featureFindings = new List<object>();
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 500)
        {
            dynamic current = feature;
            string? typeName = Try(() => (string)current.GetTypeName2()) as string;
            if (string.Equals(typeName, "ICE", StringComparison.OrdinalIgnoreCase))
            {
                featureFindings.Add(new
                {
                    name = Try(() => (string)current.Name),
                    type = typeName,
                    note = "in_context_feature",
                });
            }

            feature = Try(() => current.GetNextFeature());
        }

        var externalRefs = new List<object>();
        bool listedRefs = false;
        try
        {
            dynamic extension = doc.Extension;
            object? refsObj = extension.ListExternalFileReferences();
            if (refsObj is object[] refsArray)
            {
                listedRefs = true;
                foreach (object entry in refsArray)
                {
                    externalRefs.Add(new { entry });
                }
            }
        }
        catch
        {
            // External reference listing varies by SolidWorks version.
        }

        bool rebuildOk = Try(() => doc.ForceRebuild3(false)) as bool? ?? false;
        doc.EditRebuild3();

        int saveErrors = 0;
        int saveWarnings = 0;
        bool saveOk = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings);

        return new
        {
            document = DescribeDocument(doc),
            readOnly = Try(() => doc.IsOpenedReadOnly()),
            hasExternalRefs = Try(() => ((dynamic)doc.Extension).HasExternalReferences()),
            rebuildOk,
            saveOk,
            saveErrors,
            saveWarnings,
            saveErrorMeaning = DecodeSaveErrors(saveErrors),
            externalRefCount = externalRefs.Count,
            externalRefs,
            failedFeatureCount = featureFindings.Count,
            inContextFeatures = featureFindings,
        };
    }

    private static string DecodeSaveErrors(int errors) => errors switch
    {
        0 => "none",
        1 => "generic",
        2 => "read_only",
        3 => "need_rebuild",
        4 => "need_rebuild_blocking",
        5 => "file_not_found",
        6 => "file_with_same_title_open",
        7 => "custom_property_error",
        _ => $"unknown_{errors}",
    };

    private static object CloneSolidBodyPart(JsonElement? args)
    {
        string sourcePath = RequiredStringArg(args, "source_part_path");
        string outputPath = RequiredStringArg(args, "output_part_path");
        string? assemblyPath = StringArg(args, "assembly_path");
        string? componentName = StringArg(args, "component_name");
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        CloseDocumentIfOpen(app, outputPath);
        CloseDocumentIfOpen(app, sourcePath);
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            CloseDocumentIfOpen(app, assemblyPath);
        }

        double[]? componentTransform = null;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(componentName))
        {
            ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
            Component2? component = FindComponent((IAssemblyDoc)asmDoc, null, componentName);
            if (component is not null)
            {
                componentTransform = NormalizeTransformMatrix(ReadComponentTransformMatrix(component));
            }
        }

        Body2? copiedBody = CopyPrimarySolidBody(app, sourcePath)
            ?? throw new InvalidOperationException($"No solid body found in source part: {sourcePath}");

        string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string ?? string.Empty;
        }

        ModelDoc2? newDoc = null;
        if (!string.IsNullOrWhiteSpace(template) && File.Exists(template))
        {
            newDoc = Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2;
        }

        newDoc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
        if (newDoc is null)
        {
            throw new InvalidOperationException("Failed to create a blank part for solid-body clone.");
        }

        Feature? imported = Try(() => ((dynamic)newDoc).CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
        if (imported is null)
        {
            dynamic part = (IPartDoc)newDoc;
            object[] bodyArray = [copiedBody];
            bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, null, 0)) as bool? == true
                || Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
            if (!inserted)
            {
                throw new InvalidOperationException("CreateFeatureFromBody3 and InsertBodies both failed.");
            }
        }
        else
        {
            TryVoid(() => imported.Name = "Imported-Solid-Body1");
        }
        newDoc.EditRebuild3();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        int saveErrors = 0;
        int saveWarnings = 0;
        bool saved = newDoc.Extension.SaveAs(
            outputPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref saveErrors,
            ref saveWarnings);
        if (!saved || saveErrors != 0)
        {
            throw new InvalidOperationException(
                $"Clone save failed. errors={saveErrors} ({DecodeSaveErrors(saveErrors)}), warnings={saveWarnings}");
        }

        CloseDocumentIfOpen(app, Path.GetFullPath(outputPath));

        object? replaceResult = null;
        object? saveAssemblyResult = null;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(componentName))
        {
            replaceResult = ReplaceComponentsByPathInternal(
                app,
                assemblyPath,
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(outputPath),
                "Default",
                saveAssembly: false);

            if (componentTransform is not null)
            {
                ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
                Component2? component = FindComponent((IAssemblyDoc)asmDoc, null, componentName);
                if (component is not null)
                {
                    MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
                    MathTransform? transform = mathUtil is null
                        ? null
                        : Try(() => mathUtil.CreateTransform(componentTransform)) as MathTransform;
                    if (transform is not null)
                    {
                        TryVoid(() => component.Transform2 = transform);
                    }
                }
            }

            if (save)
            {
                ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
                int asmErrors = 0;
                int asmWarnings = 0;
                bool asmSaved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
                saveAssemblyResult = new { asmSaved, asmErrors, asmWarnings };
            }
        }

        // Verify clone saves cleanly.
        CloseDocumentIfOpen(app, outputPath);
        ModelDoc2 verifyDoc = OpenDocument(app, outputPath);
        int verifyErrors = 0;
        int verifyWarnings = 0;
        bool verifySaved = verifyDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref verifyErrors, ref verifyWarnings);

        return new
        {
            sourcePath,
            outputPath,
            assemblyPath,
            componentName,
            importedFeature = imported is not null ? Try(() => imported.Name) : "InsertBodies",
            saved,
            saveErrors,
            verifySaved,
            verifyErrors,
            replaceResult,
            saveAssemblyResult,
        };
    }

    private static object MirrorPartFile(JsonElement? args)
    {
        string sourcePath = RequiredStringArg(args, "source_part_path");
        string outputPath = RequiredStringArg(args, "output_part_path");
        string mirrorPlane = StringArg(args, "mirror_plane") ?? "Right Plane";
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        CloseDocumentIfOpen(app, outputPath);
        CloseDocumentIfOpen(app, sourcePath);

        ModelDoc2 sourceDoc = OpenDocument(app, sourcePath);
        if (sourceDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("mirror_part_file requires a part document.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        string mode = "insert_mirror_part";
        bool mirrored = false;
        dynamic extension = sourceDoc.Extension;
        mirrored = Try(() => (bool)extension.InsertMirrorPart2(outputPath, true, true, 0)) as bool? ?? false;
        if (!mirrored)
        {
            mirrored = Try(() => (bool)extension.InsertMirrorPart(outputPath, true, true)) as bool? ?? false;
        }

        if (!mirrored)
        {
            mode = "copy_body_mirror_transform";
            Body2? sourceBody = GetPrimarySolidBody(sourceDoc)
                ?? throw new InvalidOperationException("Source part has no solid body to mirror.");

            Body2? copiedBody = Try(() => sourceBody.Copy()) as Body2
                ?? throw new InvalidOperationException("Failed to copy source solid body.");

            MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
            MathTransform? mirrorTransform = mathUtil is null
                ? null
                : Try(() => mathUtil.CreateTransform(MirrorXMatrix())) as MathTransform;
            if (mirrorTransform is not null)
            {
                TryVoid(() => copiedBody.ApplyTransform(mirrorTransform));
            }

            string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
                ?? string.Empty;
            ModelDoc2? newDoc = !string.IsNullOrWhiteSpace(template) && File.Exists(template)
                ? Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2
                : null;
            newDoc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
            if (newDoc is null)
            {
                throw new InvalidOperationException("Failed to create blank part for mirrored body.");
            }

            Feature? imported = Try(() => ((dynamic)newDoc).CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
            if (imported is null)
            {
                dynamic part = (IPartDoc)newDoc;
                object[] bodyArray = [copiedBody];
                bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, null, 0)) as bool? == true
                    || Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
                if (!inserted)
                {
                    throw new InvalidOperationException("Failed to import mirrored body into new part.");
                }
            }
            else
            {
                TryVoid(() => imported.Name = "Mirror-Left-Body1");
            }

            newDoc.EditRebuild3();
            int saveErrors = 0;
            int saveWarnings = 0;
            mirrored = newDoc.Extension.SaveAs(
                outputPath,
                0,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);
            if (!mirrored || saveErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Mirrored body save failed. errors={saveErrors}, warnings={saveWarnings}");
            }

            CloseDocumentIfOpen(app, Path.GetFullPath(outputPath));
        }
        else
        {
            CloseDocumentIfOpen(app, outputPath);
        }

        if (!mirrored)
        {
            mode = "mirror_feature";
            Body2? sourceBody = GetPrimarySolidBody(sourceDoc)
                ?? throw new InvalidOperationException("Source part has no solid body to mirror.");

            sourceDoc.ClearSelection2(true);
            if (!sourceDoc.Extension.SelectByID2(mirrorPlane, "PLANE", 0, 0, 0, false, 1, null, 0))
            {
                throw new InvalidOperationException($"Could not select mirror plane: {mirrorPlane}");
            }

            Entity? bodyEntity = sourceBody as Entity;
            if (bodyEntity is null || !bodyEntity.Select4(false, null))
            {
                throw new InvalidOperationException("Could not select source solid body for mirror.");
            }

            dynamic featMgr = sourceDoc.FeatureManager;
            Feature? mirrorFeature = Try(() => featMgr.InsertMirrorFeature2(
                true,
                false,
                false,
                false,
                1,
                1)) as Feature;
            if (mirrorFeature is null)
            {
                throw new InvalidOperationException("InsertMirrorFeature2 returned null.");
            }

            TryVoid(() => mirrorFeature.Name = "Mirror-Left1");
            sourceDoc.EditRebuild3();

            int saveErrors = 0;
            int saveWarnings = 0;
            mirrored = sourceDoc.Extension.SaveAs(
                outputPath,
                0,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);
            if (!mirrored || saveErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Mirror feature save failed. errors={saveErrors}, warnings={saveWarnings}");
            }

            CloseDocumentIfOpen(app, outputPath);
        }

        ModelDoc2? mirroredDoc = File.Exists(outputPath) ? OpenDocument(app, outputPath) : null;
        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (mirroredDoc is not null && save)
        {
            saved = mirroredDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            sourcePath,
            outputPath,
            mode,
            mirrored,
            saved,
            errors,
            warnings,
            document = mirroredDoc is not null ? DescribeDocument(mirroredDoc) : null,
        };
    }

    private static object MakeComponentIndependent(JsonElement? args)
    {
        string assemblyPath = RequiredStringArg(args, "assembly_path");
        string componentName = RequiredStringArg(args, "component_name");
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("make_component_independent requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? partPath = Try(() => component.GetPathName()) as string;
        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, component, append: false))
        {
            throw new InvalidOperationException($"Could not select component: {componentName}");
        }

        bool madeIndependent = false;
        int option = 1;
        dynamic dynComponent = component;
        madeIndependent = Try(() => (bool)dynComponent.MakeIndependent(option)) as bool? ?? false;
        if (!madeIndependent)
        {
            madeIndependent = Try(() => (bool)dynComponent.MakeIndependent(0)) as bool? ?? false;
        }

        asmDoc.EditRebuild3();

        bool asmSaved = false;
        int asmErrors = 0;
        int asmWarnings = 0;
        if (save)
        {
            asmSaved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
        }

        bool partSaved = false;
        int partErrors = 0;
        int partWarnings = 0;
        bool partRebuildOk = false;
        if (!string.IsNullOrWhiteSpace(partPath) && File.Exists(partPath))
        {
            CloseDocumentIfOpen(app, partPath);
            ModelDoc2 partDoc = OpenDocument(app, partPath);
            partRebuildOk = Try(() => partDoc.ForceRebuild3(false)) as bool? ?? false;
            partDoc.EditRebuild3();
            partSaved = partDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref partErrors, ref partWarnings);
        }

        return new
        {
            assembly = DescribeDocument(asmDoc),
            component = Try(() => component.Name2),
            partPath,
            madeIndependent,
            asmSaved,
            asmErrors,
            asmWarnings,
            partSaved,
            partErrors,
            partWarnings,
            partRebuildOk,
        };
    }

    private static object SetCustomProperties(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool save = BoolArg(args, "save", defaultValue: true);
        IReadOnlyDictionary<string, string> properties = PropertiesArg(args);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CAD document does not exist.", path);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            throw new InvalidOperationException("CustomPropertyManager is unavailable on this document.");
        }

        var applied = new List<object>();
        foreach (KeyValuePair<string, string> entry in properties)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            string value = entry.Value ?? "";
            TryVoid(() =>
            {
                manager.Add3(
                    entry.Key,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    value,
                    1);
            });

            applied.Add(new { name = entry.Key, value });
        }

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after setting properties. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            applied,
            saved,
            errors,
            warnings,
            customProperties = ListCustomProperties(doc),
        };
    }

    private static object ReplaceComponentsByPath(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string fromPartPath = Path.GetFullPath(RequiredStringArg(args, "from_part_path"));
        string toPartPath = Path.GetFullPath(RequiredStringArg(args, "to_part_path"));
        string configName = StringArg(args, "configuration") ?? "Default";
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        return ReplaceComponentsByPathInternal(app, inputPath, fromPartPath, toPartPath, configName, save);
    }

    private static object ReplaceComponentPath(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string toPartPath = Path.GetFullPath(RequiredStringArg(args, "to_part_path"));
        string configName = StringArg(args, "configuration") ?? "Default";
        bool save = BoolArg(args, "save", defaultValue: true);

        if (!File.Exists(toPartPath))
        {
            throw new FileNotFoundException("Replacement part does not exist.", toPartPath);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("replace_component_path requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? previousPath = Try(() => component.GetPathName()) as string;
        string? previousName = Try(() => component.Name2) as string;
        doc.ClearSelection2(true);
        if (!SelectAssemblyComponent(doc, component, append: false))
        {
            throw new InvalidOperationException($"Could not select component: {componentName}");
        }

        bool ok = false;
        TryVoid(() =>
        {
            ok = assembly.ReplaceComponents2(
                toPartPath,
                configName,
                true,
                0,
                true);
        });

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
            component = previousName,
            fromPath = previousPath,
            toPath = toPartPath,
            ok,
            saved,
            errors,
            warnings,
        };
    }

    private static object ReplaceComponentsByPathInternal(
        ISldWorks app,
        string inputPath,
        string fromPartPath,
        string toPartPath,
        string configName,
        bool saveAssembly)
    {
        if (!File.Exists(toPartPath))
        {
            throw new FileNotFoundException("Replacement part does not exist.", toPartPath);
        }

        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("replace_components_by_path requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        var replaced = new List<object>();

        foreach (Component2 component in EnumerateAllComponents(assembly))
        {
            string? componentPath = Try(() => component.GetPathName()) as string;
            if (string.IsNullOrWhiteSpace(componentPath))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(componentPath);
            if (!fullPath.Equals(fromPartPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? previousName = Try(() => component.Name2) as string;
            doc.ClearSelection2(true);
            bool selected = Try(() => component.Select4(false, null, false)) as bool? ?? false;
            if (!selected)
            {
                throw new InvalidOperationException($"Could not select component for replace: {previousName}");
            }

            bool ok = false;
            TryVoid(() =>
            {
                ok = assembly.ReplaceComponents2(
                    toPartPath,
                    configName,
                    true,
                    0,
                    true);
            });

            replaced.Add(new
            {
                name = previousName,
                fromPath = componentPath,
                toPath = toPartPath,
                ok,
            });
        }

        if (replaced.Count == 0)
        {
            throw new InvalidOperationException(
                $"No components reference part path: {fromPartPath}");
        }

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (saveAssembly)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after replace. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            replacedCount = replaced.Count,
            replaced,
            saved,
            errors,
            warnings,
        };
    }

}