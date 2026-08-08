using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static bool SelectComponentMateEntity(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        int faceIndex,
        bool append,
        int mark)
    {
        // When a face index is provided, select that face directly. Named-feature SelectByID2
        // mis-handles Stock/ICE features (falls through to COORDSYS) and can over-select.
        if (faceIndex >= 0
            && SelectComponentFeatureFace(assemblyDoc, component, referenceName, faceIndex, append, mark))
        {
            return true;
        }

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
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
        Face2? face = feature is null ? null : GetFeatureFaceByIndex(feature, faceIndex);
        if (face is null)
        {
            return false;
        }

        // Prefer the assembly-context entity; part-doc Face2 often won't select for mates.
        Entity? assemblyEntity = Try(() => component.GetCorrespondingEntity(face)) as Entity;
        SelectData? selectData = CreateSelectData(assemblyDoc, mark);
        if (assemblyEntity is not null && selectData is not null
            && (Try(() => assemblyEntity.Select4(append, selectData)) as bool? ?? false))
        {
            return true;
        }

        if (SelectFeatureFaceByRay(assemblyDoc, component, face, append, mark))
        {
            return true;
        }

        if (face is Entity entity && selectData is not null
            && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
        {
            return true;
        }

        TryVoid(() => component.Select4(append, null, false));
        return face is Entity fallback
            && (Try(() => fallback.Select2(append, mark)) as bool? ?? false);
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
        if (normal.Length < 3)
        {
            normal = [0, 0, 1];
        }

        double[] assemblyNormal = TransformDirectionManual(transform, normal[0], normal[1], normal[2]);
        double norm = Math.Sqrt(
            (assemblyNormal[0] * assemblyNormal[0])
            + (assemblyNormal[1] * assemblyNormal[1])
            + (assemblyNormal[2] * assemblyNormal[2]));
        if (norm < 1e-12)
        {
            return false;
        }

        assemblyNormal =
        [
            assemblyNormal[0] / norm,
            assemblyNormal[1] / norm,
            assemblyNormal[2] / norm,
        ];

        foreach (double sign in new[] { 1.0, -1.0 })
        {
            // SelectByRay(..., radius, TypeHit, Append, HitRadius, SelectionMark)
            if (assemblyDoc.Extension.SelectByRay(
                    assemblyPoint[0] - (assemblyNormal[0] * 0.002 * sign),
                    assemblyPoint[1] - (assemblyNormal[1] * 0.002 * sign),
                    assemblyPoint[2] - (assemblyNormal[2] * 0.002 * sign),
                    assemblyNormal[0] * sign,
                    assemblyNormal[1] * sign,
                    assemblyNormal[2] * sign,
                    0.002,
                    (int)swSelectType_e.swSelFACES,
                    append,
                    0,
                    mark))
            {
                return true;
            }
        }

        return false;
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

    private static int GetLargestPlanarFaceIndex(Component2 component, string featureName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
        object? facesObj = feature is null ? null : Try(() => feature.GetFaces());
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
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
        object? facesObj = feature is null ? null : Try(() => feature.GetFaces());
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
            double[]? box = area < 0.001 ? null : Try(() => face.GetBox()) as double[];
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

    private static List<object> ProbeFeatureFaceSummaries(Component2 component, string featureName)
    {
        var summaries = new List<object>();
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
        object? facesObj = feature is null ? null : Try(() => feature.GetFaces());
        if (facesObj is not object[] faces)
        {
            return summaries;
        }

        MathTransform? transform = Try(() => component.Transform2) as MathTransform;

        for (int i = 0; i < faces.Length; i++)
        {
            if (faces[i] is not Face2 face)
            {
                continue;
            }

            double[]? partBox = Try(() => face.GetBox()) as double[];
            double[]? partCenter = partBox is { Length: >= 6 }
                ?
                [
                    (partBox[0] + partBox[3]) / 2.0,
                    (partBox[1] + partBox[4]) / 2.0,
                    (partBox[2] + partBox[5]) / 2.0,
                ]
                : null;
            double[]? assemblyCenter = partCenter is null || transform is null
                ? null
                : TransformPointManual(transform, partCenter[0], partCenter[1], partCenter[2]);

            double[] partNormal = Try(() => face.Normal) as double[] ?? [];
            double[]? assemblyNormal = null;
            if (partNormal.Length >= 3 && transform is not null)
            {
                assemblyNormal = TransformDirectionManual(transform, partNormal[0], partNormal[1], partNormal[2]);
                double n = Math.Sqrt(
                    (assemblyNormal[0] * assemblyNormal[0])
                    + (assemblyNormal[1] * assemblyNormal[1])
                    + (assemblyNormal[2] * assemblyNormal[2]));
                if (n > 1e-12)
                {
                    assemblyNormal =
                    [
                        assemblyNormal[0] / n,
                        assemblyNormal[1] / n,
                        assemblyNormal[2] / n,
                    ];
                }
            }

            summaries.Add(new
            {
                index = i,
                area = Try(() => face.GetArea()),
                isPlanar = Try(() => (face.GetSurface() as Surface)?.IsPlane()),
                centerM = assemblyCenter,
                normal = assemblyNormal,
                partBox,
            });
        }

        return summaries;
    }

    private static double[]? GetFeatureBoundingBoxInAssembly(Component2 component, string featureName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
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
        return facesObj is object[] faces && faces.Length > 0
            ? AggregateFaceBoxesInAssembly(component, faces)
            : null;
    }

    private static double[]? GetComponentPlaneBoxInAssembly(Component2 component, string planeName)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        Feature? plane = componentDoc is null ? null : FindFeatureByName(componentDoc, planeName);
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
                    origin[0] - slab, origin[1] - slab, origin[2] - slab,
                    origin[0] + slab, origin[1] + slab, origin[2] + slab,
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
        Feature? plane = componentDoc is null ? null : FindFeatureByName(componentDoc, planeName);
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
                return componentTransform is null
                    ? [normalized[9], normalized[10], normalized[11]]
                    : TransformPointManual(componentTransform, normalized[9], normalized[10], normalized[11]);
            }
        }

        object? facesObj = Try(() => plane.GetFaces());
        if (facesObj is object[] faces && faces.Length > 0 && faces[0] is Face2 face)
        {
            double[]? partBox = Try(() => face.GetBox()) as double[];
            if (partBox is { Length: >= 6 })
            {
                MathTransform? componentTransform = Try(() => component.Transform2) as MathTransform;
                double[] center = BoxCenter(partBox);
                return componentTransform is null
                    ? center
                    : TransformPointManual(componentTransform, center[0], center[1], center[2]);
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
            if (entry is Face2 face && Try(() => face.GetBox()) is double[] partBox)
            {
                double[]? assemblyBox = TransformPartBoxToAssembly(component, partBox);
                merged = merged is null ? assemblyBox : MergeBoxes(merged, assemblyBox);
            }
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
            [partBox[0], partBox[1], partBox[2]], [partBox[3], partBox[1], partBox[2]],
            [partBox[0], partBox[4], partBox[2]], [partBox[3], partBox[4], partBox[2]],
            [partBox[0], partBox[1], partBox[5]], [partBox[3], partBox[1], partBox[5]],
            [partBox[0], partBox[4], partBox[5]], [partBox[3], partBox[4], partBox[5]],
        ];

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        foreach (double[] corner in corners)
        {
            double[] point = TransformPointManual(transform, corner[0], corner[1], corner[2]);
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
        double[] matrix = Try(() => transform.ArrayData) as double[]
            ?? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
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
            Math.Min(left[0], right[0]), Math.Min(left[1], right[1]), Math.Min(left[2], right[2]),
            Math.Max(left[3], right[3]), Math.Max(left[4], right[4]), Math.Max(left[5], right[5]),
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

    private static double[] MirrorXMatrix() =>
    [
        -1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    private static void ApplyComponentTransformMatrix(Component2 component, double[] matrix)
    {
        if (component.Transform2 is not MathTransform transform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        if (matrix.Length != 16)
        {
            throw new InvalidOperationException("Transform matrix must contain 16 numbers.");
        }

        transform.ArrayData = matrix;
        component.Transform2 = transform;
    }

    private static Feature? InsertOffsetPlaneFromReference(
        ModelDoc2 doc,
        string referencePlane,
        double offsetM,
        string name)
    {
        FeatureManager featMgr = doc.FeatureManager;
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2(referencePlane, "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            return null;
        }

        Feature? plane = Try(() => featMgr.InsertRefPlane(
            (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance,
            offsetM,
            0,
            0.0,
            0,
            0.0)) as Feature;
        doc.ClearSelection2(true);
        if (plane is null)
        {
            return null;
        }

        TryVoid(() => plane.Name = name);
        return plane;
    }

    private static void DeleteFeatureByName(ModelDoc2 doc, string featureName)
    {
        Feature? feature = FindFeatureByName(doc, featureName);
        if (feature is null)
        {
            return;
        }

        doc.ClearSelection2(true);
        if (Try(() => feature.Select2(false, -1)) as bool? == true)
        {
            TryVoid(() => doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Children));
        }
    }

    private static double[]? GetFeatureBoundingBoxInPart(ModelDoc2 doc, string featureName)
    {
        Feature? feature = FindFeatureByName(doc, featureName);
        if (feature is null)
        {
            return null;
        }

        object? bodyObj = Try(() => feature.GetBody());
        if (bodyObj is Body2 body)
        {
            return Try(() => body.GetBodyBox()) as double[];
        }

        object? facesObj = Try(() => feature.GetFaces());
        if (facesObj is not object[] faces || faces.Length == 0)
        {
            return null;
        }

        double[]? merged = null;
        foreach (object entry in faces)
        {
            if (entry is Face2 face && Try(() => face.GetBox()) is double[] partBox)
            {
                merged = merged is null ? partBox : MergeBoxes(merged, partBox);
            }
        }

        return merged;
    }

    private static IEnumerable<Component2> EnumerateAllComponents(IAssemblyDoc assembly)
    {
        object[]? roots = Try(() => assembly.GetComponents(true)) as object[];
        if (roots is null)
        {
            yield break;
        }

        foreach (object entry in roots)
        {
            if (entry is Component2 component)
            {
                yield return component;
            }
        }
    }
}
