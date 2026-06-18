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
