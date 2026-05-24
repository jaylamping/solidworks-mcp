using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object BuildTorsoComputeShelf(JsonElement? args)
    {
        string assemblyPath = RequiredStringArg(args, "path");
        string partPath = StringArg(args, "part_path")
            ?? Path.Combine(
                Path.GetDirectoryName(assemblyPath) ?? ".",
                "..",
                "parts",
                "marengo_torso_compute_shelf_upper_revA.SLDPRT");
        partPath = Path.GetFullPath(partPath);
        string shelfComponent = StringArg(args, "shelf_component") ?? "marengo_torso_compute_shelf_upper_revA";
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout_revA";
        string leftActuator = StringArg(args, "left_actuator") ?? "actuator_rs03_left_shoulder_roll";
        string rightActuator = StringArg(args, "right_actuator") ?? "actuator_rs03_right_shoulder_roll";
        double thicknessM = DoubleArg(args, "thickness_mm", 4.0) / 1000.0;
        double clearanceM = DoubleArg(args, "clearance_mm", 1.0) / 1000.0;
        bool mateInAssembly = BoolArg(args, "mate_in_assembly", defaultValue: true);
        bool save = BoolArg(args, "save", defaultValue: true);
        string owner = StringArg(args, "owner") ?? "Joey Lamping";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("build_torso_compute_shelf requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2? layout = ResolveTorsoLayoutComponent(assembly)
            ?? FindComponent(assembly, null, layoutComponent)
            ?? throw new InvalidOperationException($"Layout component not found: {layoutComponent}");
        Component2? leftMotor = FindComponent(assembly, null, leftActuator)
            ?? throw new InvalidOperationException($"Left actuator not found: {leftActuator}");
        Component2? rightMotor = FindComponent(assembly, null, rightActuator)
            ?? throw new InvalidOperationException($"Right actuator not found: {rightActuator}");

        double[]? innerAsm = GetFeatureBoundingBoxInAssembly(layout, "torso_inner_clear")
            ?? throw new InvalidOperationException("Layout torso_inner_clear unavailable in assembly.");
        double[]? leftBox = Try(() => leftMotor.GetBox(false, false)) as double[];
        double[]? rightBox = Try(() => rightMotor.GetBox(false, false)) as double[];
        if (leftBox is null || rightBox is null || leftBox.Length < 6 || rightBox.Length < 6)
        {
            throw new InvalidOperationException("Shoulder actuator bounding boxes unavailable.");
        }

        double shelfSeatAssemblyY = Math.Min(leftBox[1], rightBox[1]);
        double halfWidthX = (innerAsm[3] - innerAsm[0]) / 2.0;
        double halfDepthZ = (innerAsm[5] - innerAsm[2]) / 2.0;
        double centerX = (innerAsm[0] + innerAsm[3]) / 2.0;
        double centerZ = (innerAsm[2] + innerAsm[5]) / 2.0;

        var notchRects = new List<(double X0, double X1, double Z0, double Z1)>();
        foreach (double[] motorBox in new[] { leftBox, rightBox })
        {
            (double X0, double X1, double Z0, double Z1)? notch = ClipActuatorNotchOnShelf(
                motorBox,
                centerX,
                centerZ,
                halfWidthX,
                halfDepthZ,
                clearanceM);
            if (notch is not null)
            {
                notchRects.Add(notch.Value);
            }
        }

        string layoutPartPath = Try(() => layout.GetPathName()) as string
            ?? throw new InvalidOperationException("Layout component path unavailable.");
        layoutPartPath = Path.GetFullPath(layoutPartPath);

        double[]? innerPart = null;
        ModelDoc2 layoutDoc = OpenDocument(app, layoutPartPath);
        innerPart = GetFeatureBoundingBoxInPart(layoutDoc, "torso_inner_clear");
        double shelfSeatLayoutY = shelfSeatAssemblyY;
        if (innerPart is not null && innerPart.Length >= 6)
        {
            shelfSeatLayoutY = innerPart[1] + (shelfSeatAssemblyY - innerAsm[1]);
        }

        object layoutPlaneResult = EnsureLayoutComputeShelfSeat(layoutDoc, shelfSeatLayoutY, save: false);
        object partBuildResult = RebuildComputeShelfPartFile(
            app,
            partPath,
            halfWidthX,
            halfDepthZ,
            thicknessM,
            notchRects,
            owner,
            save: false);

        if (save)
        {
            int layoutErrors = 0;
            int layoutWarnings = 0;
            bool layoutSaved = layoutDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref layoutErrors, ref layoutWarnings);
            if (!layoutSaved || layoutErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Layout save failed after compute_shelf_seat. errors={layoutErrors}, warnings={layoutWarnings}");
            }
        }

        asmDoc = OpenDocument(app, assemblyPath);
        assembly = (IAssemblyDoc)asmDoc;
        asmDoc.EditRebuild3();

        Component2? shelf = FindComponent(assembly, null, shelfComponent);
        object? placementResult = null;
        object? mateResult = null;
        if (shelf is not null)
        {
            layout = ResolveTorsoLayoutComponent(assembly)
                ?? FindComponent(assembly, null, layoutComponent);
            if (layout is not null)
            {
                placementResult = PlaceComputeShelfComponent(
                    asmDoc,
                    assembly,
                    shelf,
                    layout,
                    centerX,
                    centerZ,
                    shelfSeatAssemblyY);
            }

            if (mateInAssembly && layout is not null)
            {
                mateResult = MateComputeShelfToLayout(asmDoc, assembly, shelf, layout);
            }
        }

        bool asmSaved = false;
        int asmErrors = 0;
        int asmWarnings = 0;
        if (save)
        {
            asmSaved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
            if (!asmSaved || asmErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Assembly save failed after compute shelf build. errors={asmErrors}, warnings={asmWarnings}");
            }
        }

        return new
        {
            document = DescribeDocument(asmDoc),
            partPath,
            shelfComponent = Try(() => shelf?.Name2),
            shelfSeatAssemblyYM = shelfSeatAssemblyY,
            shelfSeatLayoutYM = shelfSeatLayoutY,
            innerClearAssemblyM = Normalize(innerAsm),
            halfWidthXM = halfWidthX,
            halfDepthZM = halfDepthZ,
            notchCount = notchRects.Count,
            notches = notchRects.Select(
                notch => new
                {
                    xMinM = notch.X0,
                    xMaxM = notch.X1,
                    zMinM = notch.Z0,
                    zMaxM = notch.Z1,
                }).ToArray(),
            layoutPlaneResult,
            partBuildResult,
            placementResult,
            mateResult,
            saved = asmSaved,
            asmErrors,
            asmWarnings,
        };
    }

    private static (double X0, double X1, double Z0, double Z1)? ClipActuatorNotchOnShelf(
        double[] motorBox,
        double centerX,
        double centerZ,
        double halfWidthX,
        double halfDepthZ,
        double clearanceM)
    {
        double x0 = Math.Max(motorBox[0] - clearanceM - centerX, -halfWidthX);
        double x1 = Math.Min(motorBox[3] + clearanceM - centerX, halfWidthX);
        double z0 = Math.Max(motorBox[2] - clearanceM - centerZ, -halfDepthZ);
        double z1 = Math.Min(motorBox[5] + clearanceM - centerZ, halfDepthZ);
        if (x1 <= x0 + 0.0005 || z1 <= z0 + 0.0005)
        {
            return null;
        }

        return (x0, x1, z0, z1);
    }

    private static object EnsureLayoutComputeShelfSeat(ModelDoc2 layoutDoc, double shelfSeatLayoutY, bool save)
    {
        if (layoutDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("ensure_layout_compute_shelf_seat requires a part document.");
        }

        DeleteFeatureByName(layoutDoc, "compute_shelf_seat");
        Feature? plane = InsertOffsetPlaneFromTop(layoutDoc, shelfSeatLayoutY, "compute_shelf_seat");
        if (plane is null)
        {
            throw new InvalidOperationException("Failed to create compute_shelf_seat on layout part.");
        }

        layoutDoc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = layoutDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException(
                    $"Layout save failed after compute_shelf_seat. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(layoutDoc),
            shelfSeatLayoutYM = shelfSeatLayoutY,
            saved,
            errors,
            warnings,
        };
    }

    private static object RebuildComputeShelfPartFile(
        ISldWorks app,
        string partPath,
        double halfWidthX,
        double halfDepthZ,
        double thicknessM,
        IReadOnlyList<(double X0, double X1, double Z0, double Z1)> notchRects,
        string owner,
        bool save)
    {
        CloseDocumentIfOpen(app, partPath);

        string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
            ?? string.Empty;
        ModelDoc2? doc = !string.IsNullOrWhiteSpace(template) && File.Exists(template)
            ? Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2
            : null;
        doc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
        if (doc is null)
        {
            throw new InvalidOperationException("Failed to create blank part for compute shelf rebuild.");
        }

        CreateComputeShelfSolid(doc, halfWidthX, halfDepthZ, thicknessM, notchRects);

        DeleteFeatureByName(doc, "mount_face");
        Feature? mountFace = InsertOffsetPlaneFromTop(doc, 0.0, "mount_face");
        if (mountFace is null)
        {
            throw new InvalidOperationException("Failed to create mount_face on compute shelf part.");
        }

        doc.EditRebuild3();

        Directory.CreateDirectory(Path.GetDirectoryName(partPath) ?? ".");
        CloseDocumentIfOpen(app, partPath);

        int saveErrors = 0;
        int saveWarnings = 0;
        bool partSaved = doc.Extension.SaveAs(
            partPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref saveErrors,
            ref saveWarnings);
        if (!partSaved || saveErrors != 0)
        {
            throw new InvalidOperationException(
                $"Compute shelf part save failed. errors={saveErrors} ({DecodeSaveErrors(saveErrors)}), warnings={saveWarnings}");
        }

        object propsResult = SetCustomPropertiesOnOpenDocument(
            app,
            partPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["process"] = "print",
                ["material"] = "PETG",
                ["revision"] = "A",
                ["owner"] = owner,
            },
            save: false);

        if (save)
        {
            ModelDoc2 savedDoc = OpenDocument(app, partPath);
            int errors = 0;
            int warnings = 0;
            bool saved = savedDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException(
                    $"Compute shelf part final save failed. errors={errors}, warnings={warnings}");
            }
        }

        double[]? box = Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[];
        return new
        {
            partPath,
            boundingBoxM = Normalize(box),
            thicknessM,
            halfWidthXM = halfWidthX,
            halfDepthZM = halfDepthZ,
            notchCount = notchRects.Count,
            propsResult,
            saved = partSaved,
            saveErrors,
            saveWarnings,
        };
    }

    private static object SetCustomPropertiesOnOpenDocument(
        ISldWorks app,
        string path,
        IReadOnlyDictionary<string, string> properties,
        bool save)
    {
        ModelDoc2 doc = OpenDocument(app, path);
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            throw new InvalidOperationException("CustomPropertyManager is unavailable on this document.");
        }

        var applied = new List<object>();
        foreach (KeyValuePair<string, string> entry in properties)
        {
            TryVoid(() =>
            {
                manager.Add3(
                    entry.Key,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    entry.Value ?? "",
                    1);
            });
            applied.Add(new { name = entry.Key, value = entry.Value });
        }

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new { applied, saved, errors, warnings };
    }

    private static void CreateComputeShelfSolid(
        ModelDoc2 doc,
        double halfWidthX,
        double halfDepthZ,
        double thicknessM,
        IReadOnlyList<(double X0, double X1, double Z0, double Z1)> notchRects)
    {
        SketchManager skMgr = doc.SketchManager;
        FeatureManager featMgr = doc.FeatureManager;

        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("compute shelf: could not select Top Plane.");
        }

        skMgr.InsertSketch(true);
        object? rectangle = Try(() => skMgr.CreateCornerRectangle(
            -halfWidthX,
            -halfDepthZ,
            0.0,
            halfWidthX,
            halfDepthZ,
            0.0));
        if (rectangle is null)
        {
            throw new InvalidOperationException("compute shelf: plate sketch rectangle failed.");
        }

        skMgr.InsertSketch(true);
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("compute shelf: could not select Sketch1 for plate extrusion.");
        }

        Feature? boss = ExtrudeActiveSketch(featMgr, thicknessM, flip: true);
        if (boss is null)
        {
            throw new InvalidOperationException("compute shelf: plate extrusion failed.");
        }

        TryVoid(() => boss.Name = "Boss-Extrude1");
    }

    private static Feature? ExtrudeActiveSketch(FeatureManager featMgr, double depthM, bool flip)
    {
        return Try(() => featMgr.FeatureExtrusion2(
            true,
            flip,
            false,
            (int)swEndConditions_e.swEndCondBlind,
            0,
            depthM,
            0.0,
            false,
            false,
            false,
            false,
            0.0,
            0.0,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            0,
            0.0,
            false)) as Feature;
    }

    private static object ResetComputeShelfComponent(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 shelf)
    {
        if (Try(() => shelf.IsFixed()) as bool? == true)
        {
            shelf.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        ApplyComponentTransformMatrix(shelf, IdentityTransformMatrix());
        doc.EditRebuild3();

        return new
        {
            component = Try(() => shelf.Name2),
            reset = true,
            boundingBoxM = Normalize(Try(() => shelf.GetBox(false, false))),
        };
    }

    private static object PlaceComputeShelfComponent(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 shelf,
        Component2 layout,
        double centerX,
        double centerZ,
        double shelfSeatAssemblyY)
    {
        if (Try(() => shelf.IsFixed()) as bool? == true)
        {
            shelf.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        ApplyComponentTransformMatrix(shelf, IdentityTransformMatrix());

        double[]? seatOrigin = GetComponentRefPlaneOriginInAssembly(layout, "compute_shelf_seat");
        double[]? shelfTopOrigin = GetComponentRefPlaneOriginInAssembly(shelf, "Top Plane");
        double[]? shelfBox = Try(() => shelf.GetBox(false, false)) as double[];

        double tx;
        double ty;
        double tz;

        if (seatOrigin is { Length: >= 3 } && shelfTopOrigin is { Length: >= 3 })
        {
            tx = seatOrigin[0] - shelfTopOrigin[0];
            ty = seatOrigin[1] - shelfTopOrigin[1];
            tz = seatOrigin[2] - shelfTopOrigin[2];
        }
        else if (shelfBox is { Length: >= 6 })
        {
            tx = centerX - ((shelfBox[0] + shelfBox[3]) / 2.0);
            ty = shelfSeatAssemblyY - shelfBox[4];
            tz = centerZ - ((shelfBox[2] + shelfBox[5]) / 2.0);
        }
        else
        {
            throw new InvalidOperationException("Compute shelf bounding box unavailable.");
        }

        ApplyComponentTranslation(shelf, tx, ty, tz);
        doc.EditRebuild3();

        shelfBox = Try(() => shelf.GetBox(false, false)) as double[];
        double[]? shelfTopBox = GetComponentPlaneBoxInAssembly(shelf, "Top Plane");
        double[]? seatBox = GetComponentPlaneBoxInAssembly(layout, "compute_shelf_seat");
        double[]? shelfTopOriginAfter = GetComponentRefPlaneOriginInAssembly(shelf, "Top Plane");
        return new
        {
            component = Try(() => shelf.Name2),
            translationM = new[] { tx, ty, tz },
            boundingBoxM = Normalize(shelfBox),
            shelfTopBoxM = Normalize(shelfTopBox),
            shelfTopOriginM = Normalize(shelfTopOriginAfter),
            seatOriginM = Normalize(seatOrigin),
            seatBoxM = Normalize(seatBox),
        };
    }

    private static string ResolveShelfMountReference(Component2 shelf)
    {
        ModelDoc2? shelfDoc = Try(() => shelf.GetModelDoc2()) as ModelDoc2;
        if (shelfDoc is not null && FindFeatureByName(shelfDoc, "mount_face") is not null)
        {
            return "mount_face";
        }

        return "Top Plane";
    }

    private static object MateComputeShelfToLayout(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 shelf,
        Component2 layout)
    {
        string shelfMountRef = ResolveShelfMountReference(shelf);
        int shelfFaceIndex = GetTopPlanarFaceIndex(shelf, "Boss-Extrude1");
        var mates = new List<object>();
        bool xzOk = true;

        object frontMate = TryMateComponentReferencesWithAlign(
            doc,
            assembly,
            shelf,
            "Front Plane",
            layout,
            "Front Plane",
            (int)swMateType_e.swMateCOINCIDENT,
            (int)swMateAlign_e.swMateAlignALIGNED,
            rebuild: false);
        mates.Add(new { kind = "front", result = frontMate });
        xzOk &= MateResultOk(frontMate);

        object rightMate = TryMateComponentReferencesWithAlign(
            doc,
            assembly,
            shelf,
            "Right Plane",
            layout,
            "Right Plane",
            (int)swMateType_e.swMateCOINCIDENT,
            (int)swMateAlign_e.swMateAlignALIGNED,
            rebuild: false);
        mates.Add(new { kind = "right", result = rightMate });
        xzOk &= MateResultOk(rightMate);

        bool heightOk = false;
        foreach (int align in new[] { (int)swMateAlign_e.swMateAlignALIGNED, (int)swMateAlign_e.swMateAlignANTI_ALIGNED })
        {
            object faceMate = TryMateComponentEntitiesWithAlign(
                doc,
                assembly,
                shelf,
                "Boss-Extrude1",
                shelfFaceIndex,
                layout,
                "compute_shelf_seat",
                (int)swMateType_e.swMateCOINCIDENT,
                align,
                rebuild: false);
            mates.Add(new
            {
                kind = align == (int)swMateAlign_e.swMateAlignALIGNED ? "face_height_aligned" : "face_height_anti_aligned",
                shelfMountRef = "Boss-Extrude1",
                shelfFaceIndex,
                layoutRef = "compute_shelf_seat",
                result = faceMate,
            });
            if (MateResultOk(faceMate))
            {
                heightOk = true;
                break;
            }
        }

        if (!heightOk)
        {
            foreach (int align in new[] { (int)swMateAlign_e.swMateAlignALIGNED, (int)swMateAlign_e.swMateAlignANTI_ALIGNED })
            {
                object heightMate = TryMateComponentReferencesWithAlign(
                    doc,
                    assembly,
                    shelf,
                    shelfMountRef,
                    layout,
                    "compute_shelf_seat",
                    (int)swMateType_e.swMateCOINCIDENT,
                    align,
                    rebuild: false);
                mates.Add(new
                {
                    kind = align == (int)swMateAlign_e.swMateAlignALIGNED ? "height_aligned" : "height_anti_aligned",
                    shelfMountRef,
                    layoutRef = "compute_shelf_seat",
                    result = heightMate,
                });
                if (MateResultOk(heightMate))
                {
                    heightOk = true;
                    break;
                }
            }
        }

        if (heightOk)
        {
            // front/right already attempted above
        }

        doc.EditRebuild3();
        return new { shelfMountRef, mates, heightOk, xzOk };
    }

    private static object TryMateComponentReferencesWithAlign(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        string ref1,
        Component2 second,
        string ref2,
        int mateType,
        int mateAlign,
        bool rebuild = true)
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

            return CreateMateFromSelectionWithAlign(
                doc,
                assembly,
                first,
                second,
                ref1,
                ref2,
                mateType,
                mateAlign,
                rebuild);
        }
        catch (Exception ex)
        {
            return new { ref1, ref2, ok = false, error = ex.Message };
        }
    }

    private static object TryMateComponentEntitiesWithAlign(
        ModelDoc2 doc,
        IAssemblyDoc assembly,
        Component2 first,
        string ref1,
        int face1,
        Component2 second,
        string ref2,
        int mateType,
        int mateAlign,
        bool rebuild = true)
    {
        try
        {
            doc.ClearSelection2(true);
            if (!SelectComponentMateEntity(doc, first, ref1, face1, append: false, mark: 1))
            {
                return new { ref1, ref2, ok = false, error = $"selection_failed:{ref1}" };
            }

            if (!SelectComponentMateEntity(doc, second, ref2, 0, append: true, mark: 2))
            {
                return new { ref1, ref2, ok = false, error = $"selection_failed:{ref2}" };
            }

            return CreateMateFromSelectionWithAlign(
                doc,
                assembly,
                first,
                second,
                ref1,
                ref2,
                mateType,
                mateAlign,
                rebuild);
        }
        catch (Exception ex)
        {
            return new { ref1, ref2, ok = false, error = ex.Message };
        }
    }

    private static bool MateResultOk(object mateResult)
    {
        if (mateResult.GetType().GetProperty("mateCreated")?.GetValue(mateResult) as bool? == true)
        {
            return true;
        }

        if (mateResult.GetType().GetProperty("alreadyConstrained")?.GetValue(mateResult) as bool? == true)
        {
            return true;
        }

        return mateResult.GetType().GetProperty("ok")?.GetValue(mateResult) as bool? == true;
    }

    private static object VendorAddRs03UrdfFrame(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool save = BoolArg(args, "save", defaultValue: true);
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("vendor_add_rs03_urdf_frame requires a part document.");
        }

        if (replaceExisting)
        {
            DeleteFeatureByName(doc, "urdf_link_frame");
            DeleteFeatureByName(doc, "joint_axis");
        }
        else if (FindFeatureByName(doc, "urdf_link_frame") is not null)
        {
            return new
            {
                document = DescribeDocument(doc),
                skipped = true,
                reason = "urdf_link_frame already exists",
            };
        }

        double[]? box = Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[];
        if (box is null || box.Length < 6)
        {
            throw new InvalidOperationException("RS03 vendor part bounding box unavailable.");
        }

        double originX = (box[0] + box[3]) / 2.0;
        double originY = (box[1] + box[4]) / 2.0;
        double originZ = box[5] - 0.005;

        Feature? coordFeature = CreateRs03UrdfLinkFrame(doc, originX, originY, originZ);
        if (coordFeature is null)
        {
            throw new InvalidOperationException("Failed to create urdf_link_frame on RS03 vendor part.");
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
                throw new InvalidOperationException($"Save failed after RS03 urdf frame. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            originM = new[] { originX, originY, originZ },
            saved,
            errors,
            warnings,
        };
    }

    private static Feature? CreateRs03UrdfLinkFrame(
        ModelDoc2 doc,
        double originX,
        double originY,
        double originZ)
    {
        FeatureManager featMgr = doc.FeatureManager;
        SketchManager skMgr = doc.SketchManager;

        skMgr.Insert3DSketch(true);
        SketchPoint? sketchPoint = Try(() => skMgr.CreatePoint(originX, originY, originZ)) as SketchPoint;
        skMgr.Insert3DSketch(true);
        if (sketchPoint is null)
        {
            throw new InvalidOperationException("urdf_link_frame: 3D sketch point creation failed.");
        }

        doc.ClearSelection2(true);
        bool originSelected = doc.Extension.SelectByID2(
            string.Empty,
            "EXTSKETCHPOINT",
            originX,
            originY,
            originZ,
            false,
            1,
            null,
            0);
        if (!originSelected)
        {
            SelectData? originMark = CreateSelectData(doc, 1);
            originSelected = originMark is not null
                && (Try(() => sketchPoint.Select4(false, originMark)) as bool? ?? false);
        }

        if (!originSelected)
        {
            throw new InvalidOperationException("urdf_link_frame: could not select origin sketch point.");
        }

        // RS03 output axis is -Z in vendor coords; roll joint +X mates to layout shoulder_mount.
        bool xSelected = doc.Extension.SelectByID2(
            "Front Plane",
            "PLANE",
            0,
            0,
            0,
            true,
            2,
            null,
            0);
        if (!xSelected)
        {
            throw new InvalidOperationException("urdf_link_frame: could not select Front Plane for output axis.");
        }

        bool ySelected = doc.Extension.SelectByID2(
            "Top Plane",
            "PLANE",
            0,
            0,
            0,
            true,
            4,
            null,
            0);
        if (!ySelected)
        {
            throw new InvalidOperationException("urdf_link_frame: could not select Top Plane for +Y.");
        }

        Feature? coordFeature = Try(() => featMgr.InsertCoordinateSystem(true, false, false)) as Feature;
        doc.ClearSelection2(true);
        if (coordFeature is null)
        {
            throw new InvalidOperationException("urdf_link_frame: InsertCoordinateSystem returned null.");
        }

        TryVoid(() => coordFeature.Name = "urdf_link_frame");
        return coordFeature;
    }

    private static Feature? CreateShoulderMountCoordSys(
        ModelDoc2 doc,
        string name,
        double originX,
        double originY,
        double originZ)
    {
        FeatureManager featMgr = doc.FeatureManager;
        SketchManager skMgr = doc.SketchManager;

        skMgr.Insert3DSketch(true);
        SketchPoint? sketchPoint = Try(() => skMgr.CreatePoint(originX, originY, originZ)) as SketchPoint;
        skMgr.Insert3DSketch(true);
        if (sketchPoint is null)
        {
            throw new InvalidOperationException($"{name}: 3D sketch point creation failed.");
        }

        doc.ClearSelection2(true);
        bool originSelected = doc.Extension.SelectByID2(
            string.Empty,
            "EXTSKETCHPOINT",
            originX,
            originY,
            originZ,
            false,
            1,
            null,
            0);
        if (!originSelected)
        {
            SelectData? originMark = CreateSelectData(doc, 1);
            originSelected = originMark is not null
                && (Try(() => sketchPoint.Select4(false, originMark)) as bool? ?? false);
        }

        if (!originSelected)
        {
            throw new InvalidOperationException($"{name}: could not select origin sketch point.");
        }

        bool xSelected = doc.Extension.SelectByID2(
            "Right Plane",
            "PLANE",
            0,
            0,
            0,
            true,
            2,
            null,
            0);
        if (!xSelected)
        {
            throw new InvalidOperationException($"{name}: could not select Right Plane for roll axis (+X).");
        }

        bool ySelected = doc.Extension.SelectByID2(
            "Top Plane",
            "PLANE",
            0,
            0,
            0,
            true,
            4,
            null,
            0);
        if (!ySelected)
        {
            throw new InvalidOperationException($"{name}: could not select Top Plane for +Y up.");
        }

        Feature? coordFeature = Try(() => featMgr.InsertCoordinateSystem(false, false, false)) as Feature;
        doc.ClearSelection2(true);
        if (coordFeature is null)
        {
            throw new InvalidOperationException($"{name}: InsertCoordinateSystem returned null.");
        }

        TryVoid(() => coordFeature.Name = name);
        return coordFeature;
    }

    private static void DeleteFeatureByName(ModelDoc2 doc, string featureName)
    {
        Feature? feature = FindFeatureByName(doc, featureName);
        if (feature is null)
        {
            return;
        }

        doc.ClearSelection2(true);
        if (Try(() => feature.Select2(false, -1)) as bool? != true)
        {
            return;
        }

        TryVoid(() => doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Children));
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
        if (facesObj is object[] faces && faces.Length > 0)
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

                merged = merged is null ? partBox : MergeBoxes(merged, partBox);
            }

            return merged;
        }

        return null;
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