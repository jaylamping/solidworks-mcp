using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object PlaceShoulderRollMotors(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout-1";
        string leftComponent = StringArg(args, "left_component") ?? "actuator_rs03_left_shoulder_roll";
        string rightComponent = StringArg(args, "right_component") ?? "actuator_rs03_right_shoulder_roll";
        string side = StringArg(args, "side") ?? "both";
        bool save = BoolArg(args, "save", defaultValue: true);
        bool useGolden = BoolArg(args, "use_golden", defaultValue: false);
        double innerRailM = DoubleArg(args, "inner_rail_mm", 55.0) / 1000.0;
        double innerFlangeM = DoubleArg(args, "inner_flange_mm", 40.3975) / 1000.0;

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("place_shoulder_roll_motors requires an assembly document.");
        }

        if (useGolden)
        {
            return ApplyShoulderRollGolden(args);
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? layout = ResolveTorsoLayoutComponent(assembly)
            ?? throw new InvalidOperationException($"Layout component not found: {layoutComponent}");

        double[]? outerBox = GetFeatureBoundingBoxInAssembly(layout, "torso_outer_envelope")
            ?? throw new InvalidOperationException("Layout torso_outer_envelope unavailable.");
        double[]? innerBox = GetFeatureBoundingBoxInAssembly(layout, "torso_inner_clear")
            ?? throw new InvalidOperationException("Layout torso_inner_clear unavailable.");
        double shoulderY = outerBox[4];

        var placed = new List<object>();
        if (side is "left" or "both")
        {
            placed.Add(PlaceShoulderRollMotorSide(
                doc,
                assembly,
                app,
                leftComponent,
                innerFlangeM,
                innerBox,
                shoulderY,
                leftSide: true));
        }

        if (side is "right" or "both")
        {
            placed.Add(PlaceShoulderRollMotorSide(
                doc,
                assembly,
                app,
                rightComponent,
                innerFlangeM,
                innerBox,
                shoulderY,
                leftSide: false));
        }

        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after motor placement. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            shoulderYM = shoulderY,
            innerRailM,
            innerFlangeM,
            innerClearBoxM = innerBox,
            placed,
            saved,
            errors,
            warnings,
        };
    }

    private static object PlaceShoulderRollMotorSide(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        ISldWorks app,
        string componentName,
        double innerFlangeM,
        double[] innerBox,
        double shoulderY,
        bool leftSide,
        bool fixAtEnd = true)
    {
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (Try(() => component.IsFixed()) as bool? == true)
        {
            component.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        EnsureShoulderMotorOrientation(app, component, leftSide);
        TryVoid(() => doc.EditRebuild3());

        double[]? box = Try(() => component.GetBox(false, false)) as double[];
        if (box is null || box.Length < 6)
        {
            throw new InvalidOperationException($"Bounding box unavailable for {componentName}");
        }

        double tx;
        double ty = shoulderY - box[4];
        double tz = -((box[2] + box[5]) / 2.0);

        // User golden: left on -X (inner flange max-X ~ -40.4 mm), right on +X (min-X ~ +40.4 mm).
        if (leftSide)
        {
            tx = -innerFlangeM - box[3];
        }
        else
        {
            tx = innerFlangeM - box[0];
        }

        ApplyComponentTranslation(component, tx, ty, tz);

        box = Try(() => component.GetBox(false, false)) as double[];
        if (fixAtEnd)
        {
            component.Select4(false, null, false);
            assembly.FixComponent();
            doc.ClearSelection2(true);
        }

        return new
        {
            component = Try(() => component.Name2),
            leftSide,
            translationM = new[] { tx, ty, tz },
            boundingBoxM = box,
            innerFlangeTargetM = leftSide ? -innerFlangeM : innerFlangeM,
            insideInnerClear =
                box is { Length: >= 6 } &&
                box[0] >= innerBox[0] - 0.001 &&
                box[3] <= innerBox[3] + 0.001 &&
                box[2] >= innerBox[2] - 0.001 &&
                box[5] <= innerBox[5] + 0.001,
            fixedAtEnd = fixAtEnd,
        };
    }

    private static object MateShoulderRollMotor(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string side = StringArg(args, "side") ?? "left";
        bool leftSide = !side.Equals("right", StringComparison.OrdinalIgnoreCase);
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout";
        string motorComponent = StringArg(args, "motor_component")
            ?? (leftSide ? "actuator_rs03_left_shoulder_roll" : "actuator_rs03_right_shoulder_roll");
        bool save = BoolArg(args, "save", defaultValue: true);
        bool preplace = BoolArg(args, "preplace", defaultValue: true);
        double innerFlangeM = DoubleArg(args, "inner_flange_mm", 40.3975) / 1000.0;

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_shoulder_roll_motor requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? layout = ResolveTorsoLayoutComponent(assembly)
            ?? throw new InvalidOperationException($"Layout component not found: {layoutComponent}");
        Component2? motor = FindComponent(assembly, null, motorComponent)
            ?? throw new InvalidOperationException($"Motor component not found: {motorComponent}");

        TryVoid(() => layout.Visible = (int)swComponentVisibilityState_e.swComponentVisible);

        if (Try(() => motor.IsFixed()) as bool? == true)
        {
            motor.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        object? placement = null;
        if (preplace)
        {
            double[]? outerBox = GetFeatureBoundingBoxInAssembly(layout, "torso_outer_envelope")
                ?? throw new InvalidOperationException("Layout torso_outer_envelope unavailable.");
            double[]? innerBox = GetFeatureBoundingBoxInAssembly(layout, "torso_inner_clear")
                ?? throw new InvalidOperationException("Layout torso_inner_clear unavailable.");
            placement = PlaceShoulderRollMotorSide(
                doc,
                assembly,
                app,
                motorComponent,
                innerFlangeM,
                innerBox,
                outerBox[4],
                leftSide,
                fixAtEnd: false);
        }
        else
        {
            EnsureShoulderMotorOrientation(app, motor, leftSide);
            TryVoid(() => doc.EditRebuild3());
        }

        string railRef = leftSide ? "shoulder_rail_inner_left" : "shoulder_rail_inner_right";
        string mountRef = leftSide ? "shoulder_mount_left" : "shoulder_mount_right";
        var mates = new List<object>();

        object coordMate = TryMateComponentReferences(
            doc,
            assembly,
            layout,
            mountRef,
            motor,
            "urdf_link_frame",
            (int)swMateType_e.swMateCOORDINATE,
            (int)swMateAlign_e.swMateAlignALIGNED);
        mates.Add(new { kind = "coord_sys", mountRef, motorRef = "urdf_link_frame", result = coordMate });

        bool coordOk = coordMate.GetType().GetProperty("mateCreated")?.GetValue(coordMate) as bool? == true
            || coordMate.GetType().GetProperty("alreadyConstrained")?.GetValue(coordMate) as bool? == true;

        if (!coordOk)
        {
            mates.Add(new
            {
                kind = "coincident_fallback",
                result = TryMateComponentReferences(
                    doc,
                    assembly,
                    layout,
                    railRef,
                    motor,
                    "Front Plane",
                    (int)swMateType_e.swMateCOINCIDENT,
                    leftSide
                        ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED
                        : (int)swMateAlign_e.swMateAlignALIGNED),
            });

            mates.Add(new
            {
                kind = "coincident_fallback",
                result = TryMateComponentReferences(
                    doc,
                    assembly,
                    layout,
                    "shoulder_plane",
                    motor,
                    "Top Plane",
                    (int)swMateType_e.swMateCOINCIDENT,
                    (int)swMateAlign_e.swMateAlignALIGNED),
            });

            mates.Add(new
            {
                kind = "coincident_fallback",
                result = TryMateComponentReferences(
                    doc,
                    assembly,
                    layout,
                    "Front Plane",
                    motor,
                    "Right Plane",
                    (int)swMateType_e.swMateCOINCIDENT,
                    (int)swMateAlign_e.swMateAlignALIGNED),
            });
        }

        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after shoulder motor mate. errors={errors}, warnings={warnings}");
            }
        }

        double[]? motorBox = Try(() => motor.GetBox(false, false)) as double[];

        return new
        {
            document = DescribeDocument(doc),
            side = leftSide ? "left" : "right",
            motor = Try(() => motor.Name2),
            layout = Try(() => layout.Name2),
            placement,
            mates,
            motorBoundingBoxM = motorBox,
            motorFixed = Try(() => motor.IsFixed()),
            saved,
            errors,
            warnings,
        };
    }

    private static object TryMateComponentReferences(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        string ref1,
        Component2 second,
        string ref2,
        int mateType,
        int mateAlign)
    {
        try
        {
            doc.ClearSelection2(true);
            if (!SelectComponentReference(doc, first, ref1, append: false, mark: 1))
            {
                return new { ref1, ref2, ok = false, error = $"selection_failed:{ref1}" };
            }

            if (!SelectComponentReference(doc, second, ref2, append: true, mark: 2))
            {
                return new { ref1, ref2, ok = false, error = $"selection_failed:{ref2}" };
            }

            return CreateMateFromSelection(doc, assembly, first, second, ref1, ref2, mateType, mateAlign);
        }
        catch (Exception ex)
        {
            return new { ref1, ref2, ok = false, error = ex.Message };
        }
    }

    private static void EnsureShoulderMotorOrientation(ISldWorks app, Component2 component, bool leftSide)
    {
        if (component.Transform2 is null)
        {
            return;
        }

        // Pack roll on +X; vendor output on -Z → -X after rotY; flipY sends bolt face to +X.
        double[] orientation = MultiplyTransformMatrix(FlipY180Matrix(), RotY90Matrix());
        if (!leftSide)
        {
            orientation = MultiplyTransformMatrix(MirrorXMatrix(), orientation);
        }

        ApplyComponentTransformMatrix(component, orientation);
    }

    private static double[] FlipY180Matrix() =>
    [
        -1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, -1, 0,
        0, 0, 0, 1,
    ];

    private static double[] RotY90Matrix() =>
    [
        0, 0, 1, 0,
        0, 1, 0, 0,
        -1, 0, 0, 0,
        0, 0, 0, 1,
    ];

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

    private static double[] MultiplyTransformMatrix(double[] left, double[] right)
    {
        if (left.Length != 16 || right.Length != 16)
        {
            throw new InvalidOperationException("Transform matrix must contain 16 numbers.");
        }

        double[] result = new double[16];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += left[(row * 4) + k] * right[(k * 4) + col];
                }

                result[(row * 4) + col] = sum;
            }
        }

        return result;
    }

    private static Feature? InsertOffsetPlaneFromRight(ModelDoc2 doc, double offsetM, string name)
    {
        return InsertOffsetPlaneFromReference(doc, "Right Plane", offsetM, name);
    }

    private static Feature? InsertOffsetPlaneFromTop(ModelDoc2 doc, double offsetM, string name)
    {
        return InsertOffsetPlaneFromReference(doc, "Top Plane", offsetM, name);
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

}