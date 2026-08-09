using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static ModelDoc2 ResolveDocument(ISldWorks app, JsonElement? args, bool startIfMissing = true)
    {
        string? inputPath = StringArg(args, "path");
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            return OpenDocument(app, PathGuard.AssertAllowedPath(inputPath));
        }

        return Try(() => app.ActiveDoc) as ModelDoc2
            ?? throw WorkerException.Validation(
                "NO_ACTIVE_DOCUMENT",
                "No active document and no path provided.",
                new Dictionary<string, object?>());
    }

    private static object GetMassProperties(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string? componentName = StringArg(args, "component_name");

        MassProperty? massProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty;
        if (massProperty is null)
        {
            throw new InvalidOperationException("CreateMassProperty returned null.");
        }

        if (!string.IsNullOrWhiteSpace(componentName) && doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName);
            if (component is not null)
            {
                object? bodies = Try(() => component.GetBodies2((int)swBodyType_e.swSolidBody));
                if (bodies is object[] bodyArray && bodyArray.Length > 0)
                {
                    TryVoid(() => massProperty.AddBodies(bodyArray));
                }
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            componentName,
            mass = Normalize(Try(() => massProperty.Mass)),
            volume = Normalize(Try(() => massProperty.Volume)),
            surfaceArea = Normalize(Try(() => massProperty.SurfaceArea)),
            centerOfMass = Normalize(Try(() => massProperty.CenterOfMass)),
            momentsOfInertia = Normalize(Try(() => massProperty.GetMomentOfInertia(0))),
        };
    }

    private static object GetMaterial(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string? bodyName = StringArg(args, "body_name");

        PartDoc? part = doc as PartDoc;
        if (part is null)
        {
            throw new InvalidOperationException("get_material requires a part document.");
        }

        object? bodiesObj = Try(() => part.GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies || bodies.Length == 0)
        {
            return new { document = DescribeDocument(doc), materials = Array.Empty<object>() };
        }

        var materials = new List<object>();
        string docMaterial = "";
        TryVoid(() =>
        {
            docMaterial = ((PartDoc)doc).GetMaterialPropertyName2("", out string _);
        });

        foreach (object entry in bodies)
        {
            if (entry is not Body2 body)
            {
                continue;
            }

            string name = Try(() => body.Name) as string ?? "";
            if (!string.IsNullOrWhiteSpace(bodyName)
                && !name.Equals(bodyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            materials.Add(new { bodyName = name, material = docMaterial });
        }

        return new { document = DescribeDocument(doc), materials };
    }

    private static object ListBodies(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);

        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("list_bodies requires a part document.");
        }

        object? bodiesObj = Try(() => ((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        var bodies = new List<object>();
        if (bodiesObj is object[] bodyArray)
        {
            foreach (object entry in bodyArray)
            {
                if (entry is not Body2 body)
                {
                    continue;
                }

                bodies.Add(new
                {
                    name = Try(() => body.Name),
                    isSheetMetal = Try(() => body.IsSheetMetal()),
                });
            }
        }

        return new { document = DescribeDocument(doc), bodies };
    }

    private static object GetEquations(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        EquationMgr? eqMgr = Try(() => doc.GetEquationMgr()) as EquationMgr;
        if (eqMgr is null)
        {
            return new { document = DescribeDocument(doc), equations = Array.Empty<object>() };
        }

        int count = Try(() => eqMgr.GetCount()) as int? ?? 0;
        var equations = new List<object>();
        for (int i = 0; i < count; i++)
        {
            string? equation = Try(() => eqMgr.Equation[i]) as string;
            bool global = Try(() => eqMgr.GlobalVariable[i]) as bool? ?? false;
            equations.Add(new { index = i, equation, global });
        }

        return new { document = DescribeDocument(doc), equations };
    }

    private static object ListSketches(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        var sketches = new List<object>();

        Feature? feature = Try(() => doc.FirstFeature()) as Feature;
        int guard = 0;
        while (feature is not null && guard++ < 500)
        {
            string? typeName = Try(() => feature.GetTypeName2()) as string;
            if (typeName is not null && typeName.Contains("Sketch", StringComparison.OrdinalIgnoreCase))
            {
                sketches.Add(new
                {
                    name = Try(() => feature.Name),
                    type = typeName,
                });
            }

            feature = Try(() => feature.GetNextFeature()) as Feature;
        }

        return new { document = DescribeDocument(doc), sketches, truncated = guard >= 500 };
    }

    private static object GetSelection(JsonElement? args) => ResolveSelection(args);

    private static object ListDisplayStates(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        Configuration? config = Try(() => doc.GetActiveConfiguration()) as Configuration;
        string[]? states = config is null
            ? null
            : Try(() => config.GetDisplayStates()) as string[];

        return new
        {
            document = DescribeDocument(doc),
            displayStates = states ?? Array.Empty<string>(),
        };
    }

    private static object GetOpenDocuments(JsonElement? args)
    {
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: false);
        ISldWorks app = AttachSolidWorks(startIfMissing);
        object? docsObj = Try(() => app.GetDocuments());
        var documents = new List<object>();

        if (docsObj is object[] docs)
        {
            foreach (object entry in docs)
            {
                if (entry is ModelDoc2 modelDoc)
                {
                    documents.Add(DescribeDocument(modelDoc) ?? new { });
                }
            }
        }

        return new { count = documents.Count, documents };
    }

    private static object MeasureDistance(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double[] point1 = DoubleArrayArg(args, "point_1_m");
        double[] point2 = DoubleArrayArg(args, "point_2_m");
        if (point1.Length < 3 || point2.Length < 3)
        {
            throw new ArgumentException("point_1_m and point_2_m must each have 3 coordinates.");
        }

        double dx = point2[0] - point1[0];
        double dy = point2[1] - point1[1];
        double dz = point2[2] - point1[2];
        double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        return new
        {
            document = DescribeDocument(doc),
            point1M = point1,
            point2M = point2,
            distanceM = distance,
        };
    }

    private static object GetComponentReferences(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_component_references requires an assembly.");
        }

        string? componentName = StringArg(args, "component_name");
        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        IEnumerable<Component2> components = string.IsNullOrWhiteSpace(componentName)
            ? EnumerateAllComponents(assembly)
            : FindComponent(assembly, null, componentName) is Component2 one
                ? [one]
                : [];

        var references = new List<object>();
        foreach (Component2 component in components)
        {
            references.Add(new
            {
                component = Try(() => component.Name2),
                path = Try(() => component.GetPathName()),
                referencedConfiguration = Try(() => component.ReferencedConfiguration),
                isSuppressed = Try(() => component.IsSuppressed()),
            });
        }

        return new { document = DescribeDocument(doc), references };
    }

    private sealed record FeatureErrorEntry(
        string? Name,
        string? Type,
        int ErrorCode,
        bool Warning,
        bool? Suppressed,
        string? Parent);

    private static object ListBrokenReferences(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        var errors = CollectFeatureErrors(doc);
        return new
        {
            document = DescribeDocument(doc),
            brokenReferences = errors
                .Select(entry => $"{entry.Name} ({entry.Type}) errorCode={entry.ErrorCode}")
                .ToArray(),
            count = errors.Count,
            features = errors,
        };
    }

    private static object ListFeatureErrors(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        var errors = CollectFeatureErrors(doc);
        return new
        {
            document = DescribeDocument(doc),
            count = errors.Count,
            features = errors,
        };
    }

    private static List<FeatureErrorEntry> CollectFeatureErrors(ModelDoc2 doc)
    {
        var errors = new List<FeatureErrorEntry>();
        object? featureObj = Try(() => doc.FirstFeature());
        int guard = 0;
        while (featureObj is not null && guard++ < 2000)
        {
            if (featureObj is Feature feature)
            {
                AppendFeatureError(errors, feature, parent: null);
                object? sub = Try(() => feature.GetFirstSubFeature());
                int subGuard = 0;
                while (sub is not null && subGuard++ < 200)
                {
                    if (sub is Feature subFeature)
                    {
                        AppendFeatureError(errors, subFeature, parent: Try(() => feature.Name) as string);
                        sub = Try(() => subFeature.GetNextSubFeature());
                    }
                    else
                    {
                        break;
                    }
                }
            }

            featureObj = featureObj is Feature current
                ? Try(() => current.GetNextFeature())
                : null;
        }

        return errors;
    }

    private static void AppendFeatureError(List<FeatureErrorEntry> errors, Feature feature, string? parent)
    {
        int? errorCode = null;
        bool warning = false;
        try
        {
            errorCode = feature.GetErrorCode2(out warning);
        }
        catch
        {
            errorCode = Try(() => feature.GetErrorCode()) as int?;
        }

        if (errorCode is null or 0)
        {
            return;
        }

        errors.Add(new FeatureErrorEntry(
            Name: Try(() => feature.Name) as string,
            Type: Try(() => feature.GetTypeName2()) as string,
            ErrorCode: errorCode.Value,
            Warning: warning,
            Suppressed: Try(() => feature.IsSuppressed()) as bool?,
            Parent: parent));
    }

    private static object NewDocument(JsonElement? args)
    {
        string docType = StringArg(args, "doc_type") ?? "part";
        string? outputPath = StringArg(args, "output_path");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = PathGuard.AssertAllowedPath(outputPath);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        int swType = docType.ToLowerInvariant() switch
        {
            "assembly" => (int)swDocumentTypes_e.swDocASSEMBLY,
            "drawing" => (int)swDocumentTypes_e.swDocDRAWING,
            _ => (int)swDocumentTypes_e.swDocPART,
        };

        ModelDoc2? doc = Try(() => app.NewDocument("", swType, 0, 0)) as ModelDoc2;
        if (doc is null)
        {
            throw new InvalidOperationException($"Failed to create new {docType} document.");
        }

        bool saved = false;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            int errors = 0;
            int warnings = 0;
            saved = doc.Extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
        }

        return new { document = DescribeDocument(doc), docType, outputPath, saved };
    }

    private static object CloseDocument(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        bool save = BoolArg(args, "save", defaultValue: false);
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2? doc = FindOpenDocument(app, inputPath);
        if (doc is null)
        {
            return new { path = inputPath, closed = false, reason = "not_open" };
        }

        string? title = Try(() => doc.GetTitle()) as string;
        if (save)
        {
            int errors = 0;
            int warnings = 0;
            doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        bool closed = false;
        if (!string.IsNullOrWhiteSpace(title))
        {
            TryVoid(() => app.CloseDoc(title));
            closed = FindOpenDocument(app, inputPath) is null;
        }
        return new { path = inputPath, title, closed, saved = save };
    }

    private static object ActivateDocument(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        return new { document = DescribeDocument(doc), activated = true };
    }

    private static object CreateSketch(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string planeName = StringArg(args, "plane_name") ?? "Front Plane";

        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException($"Could not select plane: {planeName}");
        }

        SketchManager sketchMgr = doc.SketchManager;
        sketchMgr.InsertSketch(true);
        return new { document = DescribeDocument(doc), planeName, sketchActive = true };
    }

    private static object SketchRectangle(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double x1 = DoubleArg(args, "x1_m", -0.01);
        double y1 = DoubleArg(args, "y1_m", -0.01);
        double x2 = DoubleArg(args, "x2_m", 0.01);
        double y2 = DoubleArg(args, "y2_m", 0.01);

        SketchManager sketchMgr = doc.SketchManager;
        sketchMgr.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
        return new { document = DescribeDocument(doc), corner1M = new[] { x1, y1 }, corner2M = new[] { x2, y2 } };
    }

    private static object FeatureExtrudeBoss(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        double depthM = DoubleArg(args, "depth_m", 0.01);
        bool merge = BoolArg(args, "merge", defaultValue: true);
        bool flip = BoolArg(args, "flip", defaultValue: false);
        string? sketchName = StringArg(args, "sketch_name");
        string? mergeBodyName = StringArg(args, "merge_body_name");

        if (!string.IsNullOrWhiteSpace(sketchName))
        {
            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0))
            {
                throw new InvalidOperationException($"Could not select sketch: {sketchName}");
            }
        }

        // When merging into one body only, select that solid (append) so auto-select
        // does not consume unrelated multi-body solids (e.g. the ring being cropped).
        // Keep any active sketch selection; clearing it prevents the extrude.
        if (merge && !string.IsNullOrWhiteSpace(mergeBodyName))
        {
            if (!SelectSolidBody(doc, mergeBodyName, append: true, mark: 0))
            {
                throw new InvalidOperationException($"Could not select merge body: {mergeBodyName}");
            }
        }

        // When creating a separate body, feature-scope auto-select can prevent the extrude.
        bool useFeatScope = BoolArg(args, "use_feat_scope", defaultValue: merge);
        bool useAutoSelect = BoolArg(args, "use_auto_select", defaultValue: merge && string.IsNullOrWhiteSpace(mergeBodyName));

        Feature? extrude = Try(() => doc.FeatureManager.FeatureExtrusion2(
            true, false, flip, 0, 0, depthM, 0, false, false, false, false, 0, 0, false, false, false, false,
            merge, useFeatScope, useAutoSelect, 0, 0, false)) as Feature;

        doc.EditRebuild3();
        return new
        {
            document = DescribeDocument(doc),
            depthM,
            merge,
            flip,
            sketchName,
            featureName = Try(() => extrude?.Name),
            created = extrude is not null,
        };
    }

    private static object DeleteFeature(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string featureName = RequiredStringArg(args, "feature_name");
        DeleteFeatureByName(doc, featureName);
        doc.EditRebuild3();
        return new { document = DescribeDocument(doc), featureName, deleted = true };
    }

    private static object DissolveComponent(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string componentName = RequiredStringArg(args, "component_name");

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("dissolve_component requires an assembly.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        doc.ClearSelection2(true);
        component.Select4(false, null, false);
        bool dissolved = false;
        return new
        {
            document = DescribeDocument(doc),
            componentName,
            dissolved,
            stub = true,
            message = "dissolve_component stub — DissolveAssembly API not available in this interop build.",
        };
    }

    private static object MirrorComponent(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string componentName = RequiredStringArg(args, "component_name");
        string? mirrorPlane = StringArg(args, "mirror_plane") ?? "Right Plane";

        return new
        {
            stub = true,
            path = inputPath,
            componentName,
            mirrorPlane,
            message = "mirror_component stub — use mirror_part_file for file-level mirroring today.",
        };
    }

    private static object PackAndGo(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string outputDir = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_dir"));
        Directory.CreateDirectory(outputDir);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        ModelDocExtension extension = doc.Extension;

        object? packAndGo = Try(() => extension.GetPackAndGo());
        if (packAndGo is not PackAndGo pack)
        {
            throw WorkerException.Validation(
                "PACK_AND_GO_UNAVAILABLE",
                "PackAndGo API unavailable on this document.",
                new Dictionary<string, object?> { ["path"] = inputPath });
        }

        TryVoid(() => pack.IncludeDrawings = true);
        TryVoid(() => pack.IncludeSimulationResults = false);
        TryVoid(() => pack.FlattenToSingleFolder = true);
        TryVoid(() => pack.SetSaveToName(true, outputDir));

        bool ok = Try(() => extension.SavePackAndGo(pack)) as bool? ?? false;

        return new
        {
            document = DescribeDocument(doc),
            inputPath,
            outputDir,
            ok,
        };
    }
}
