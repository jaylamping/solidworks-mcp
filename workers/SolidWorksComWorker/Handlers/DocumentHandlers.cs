using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object Status(JsonElement? args)
    {
        bool startIfMissing = BoolArg(args, "start_if_missing");
        ISldWorks app = AttachSolidWorks(startIfMissing);
        object? version = Try(() => app.RevisionNumber());
        if (version is null)
        {
            throw new InvalidOperationException(
                "SolidWorks COM object was found, but API calls are not responding. Check SolidWorks launch state and COM/type-library registration.");
        }

        object? doc = Try(() => app.ActiveDoc);

        return new
        {
            running = true,
            version,
            activeDocument = DescribeDocument(doc),
        };
    }

    private static object Open(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CAD document does not exist.", path);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2 doc = OpenDocument(app, path);
        return DescribeDocument(doc) ?? new { path };
    }

    private static object Export(JsonElement? args)
    {
        string outputPath = RequiredStringArg(args, "output_path");
        string? inputPath = StringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to export.");
        }

        int errors = 0;
        int warnings = 0;
        if (IsPreviewExport(outputPath))
        {
            PreparePreview(app, doc);
        }

        ModelDocExtension extension = doc.Extension;
        bool ok = extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);

        return new
        {
            ok,
            outputPath,
            errors,
            warnings,
            exists = File.Exists(outputPath),
        };
    }

    private static bool IsPreviewExport(string outputPath)
    {
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }

    private static void PreparePreview(ISldWorks app, ModelDoc2 doc)
    {
        HideReferenceGeometryForPreview(app);
        TryVoid(() => doc.ClearSelection2(true));
        SelectReferenceFeatures(doc);
        TryVoid(() => doc.BlankRefGeom());
        TryVoid(() => doc.ClearSelection2(true));
        TryVoid(() => doc.BlankSketch());
        TryVoid(() => doc.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView));
        TryVoid(() => doc.ViewZoomtofit2());

        ModelView? view = Try(() => doc.ActiveView) as ModelView;
        if (view is not null)
        {
            TryVoid(() => view.FrameState = (int)swWindowState_e.swWindowMaximized);
            Try(() => view.EnableGraphicsUpdate = true);
        }

        TryVoid(() => doc.GraphicsRedraw2());
    }

    private static void SelectReferenceFeatures(ModelDoc2 doc)
    {
        object? feature = Try(() => doc.FirstFeature());
        bool append = false;
        int guard = 0;
        while (feature is not null && guard++ < 1000)
        {
            dynamic current = feature;
            string? type = Try(() => current.GetTypeName2()) as string;
            if (type is "RefPlane" or "RefAxis" or "RefPoint" or "CoordSys")
            {
                bool selected = Try(() => current.Select2(append, 0)) as bool? ?? false;
                append = append || selected;
            }

            feature = Try(() => current.GetNextFeature());
        }
    }

    private static void HideReferenceGeometryForPreview(ISldWorks app)
    {
        swUserPreferenceToggle_e[] toggles =
        [
            swUserPreferenceToggle_e.swDisplayPlanes,
            swUserPreferenceToggle_e.swDisplayAxes,
            swUserPreferenceToggle_e.swDisplayTemporaryAxes,
            swUserPreferenceToggle_e.swDisplayCoordSystems,
            swUserPreferenceToggle_e.swDisplayOrigins,
            swUserPreferenceToggle_e.swDisplaySketches,
            swUserPreferenceToggle_e.swDisplaySketchPlanes,
        ];

        foreach (swUserPreferenceToggle_e toggle in toggles)
        {
            TryVoid(() => app.SetUserPreferenceToggle((int)toggle, false));
        }
    }

    private static object Measure(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to measure.");
        }

        MassProperty? massProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty;
        object? boundingBox = BoundingBox(doc);

        return new
        {
            document = DescribeDocument(doc),
            boundingBox = Normalize(boundingBox),
            mass = Normalize(Try(() => massProperty?.Mass)),
            centerOfMass = Normalize(Try(() => massProperty?.CenterOfMass)),
            momentsOfInertia = Normalize(Try(() => massProperty?.GetMomentOfInertia(0))),
        };
    }

    private static object? BoundingBox(ModelDoc2 doc)
    {
        return doc.GetType() switch
        {
            (int)swDocumentTypes_e.swDocPART => Try(() => ((IPartDoc)doc).GetPartBox(true)),
            (int)swDocumentTypes_e.swDocASSEMBLY => Try(() => ((IAssemblyDoc)doc).GetBox(1)),
            _ => null,
        };
    }

    private static object ListFeatures(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        var features = new List<object>();
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 500)
        {
            dynamic current = feature;
            features.Add(new
            {
                name = Try(() => current.Name),
                type = Try(() => current.GetTypeName2()),
            });
            feature = Try(() => current.GetNextFeature());
        }

        return new
        {
            document = DescribeDocument(doc),
            features,
            truncated = guard >= 500,
        };
    }

    private static object InspectDocument(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        string? pathName = Try(() => doc.GetPathName()) as string;
        bool saved = !string.IsNullOrWhiteSpace(pathName);

        return new
        {
            document = DescribeDocument(doc),
            saved,
            units = DescribeUnits(app, doc),
            customProperties = ListCustomProperties(doc),
        };
    }

    private static object ListComponents(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("list_components requires an assembly document.");
        }

        var tree = new List<object>();
        CollectComponentTree((IAssemblyDoc)doc, null, tree, 0);
        return new
        {
            document = DescribeDocument(doc),
            components = tree,
        };
    }

    private static object ListReferenceGeometry(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        var items = new List<object>();
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 1000)
        {
            dynamic current = feature;
            string? type = Try(() => current.GetTypeName2()) as string;
            if (type is "RefPlane" or "RefAxis" or "RefPoint" or "CoordSys")
            {
                items.Add(new
                {
                    name = Try(() => current.Name),
                    type,
                });
            }

            feature = Try(() => current.GetNextFeature());
        }

        return new
        {
            document = DescribeDocument(doc),
            referenceGeometry = items,
            truncated = guard >= 1000,
        };
    }

    private static object ListBom(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("list_bom requires an assembly document.");
        }

        var flat = new List<object>();
        CollectBomLines((IAssemblyDoc)doc, null, flat);
        return new
        {
            document = DescribeDocument(doc),
            lines = flat,
            lineCount = flat.Count,
        };
    }

    private static int CountAssemblyMates(ModelDoc2 doc)
    {
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is null)
        {
            return 0;
        }

        int mateCount = 0;
        object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
        int guard = 0;
        while (subFeature is not null && guard++ < 500)
        {
            mateCount++;
            subFeature = Try(() => ((dynamic)subFeature).GetNextSubFeature());
        }

        return mateCount;
    }

    private static object ListMates(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("list_mates requires an assembly document.");
        }

        var mates = new List<object>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is not null)
        {
            object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
            int guard = 0;
            while (subFeature is not null && guard++ < 200)
            {
                dynamic current = subFeature;
                mates.Add(new
                {
                    name = Try(() => current.Name),
                    type = Try(() => current.GetTypeName2()),
                });
                subFeature = Try(() => current.GetNextSubFeature());
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            mates,
            mateCount = CountAssemblyMates(doc),
        };
    }

    private static object SetComponentVisible(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        bool visible = BoolArg(args, "visible", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_visible requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        int state = visible
            ? (int)swComponentVisibilityState_e.swComponentVisible
            : (int)swComponentVisibilityState_e.swComponentHidden;
        component.Visible = state;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            visible,
        };
    }

    private static object SetComponentFixed(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        bool fixedState = BoolArg(args, "fixed", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_fixed requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (fixedState)
        {
            component.Select4(false, null, false);
            ((IAssemblyDoc)doc).FixComponent();
        }
        else
        {
            component.Select4(false, null, false);
            ((IAssemblyDoc)doc).UnfixComponent();
        }

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            fixedState = Try(() => component.IsFixed()),
        };
    }

    private static object RenameComponent(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string fromName = RequiredStringArg(args, "from");
        string toName = RequiredStringArg(args, "to");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("rename_component requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, fromName)
            ?? throw new InvalidOperationException($"Component not found: {fromName}");

        string? previous = Try(() => component.Name2) as string;
        doc.ClearSelection2(true);
        bool selected = Try(() => component.Select4(false, null, false)) as bool? ?? false;
        if (!selected)
        {
            throw new InvalidOperationException($"Could not select component for rename: {fromName}");
        }

        bool renamed = Try(() => ((dynamic)component).SetName(toName)) as bool? ?? false;
        if (!renamed)
        {
            component.Name2 = toName;
        }

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            from = previous,
            to = Try(() => component.Name2),
            renamed,
        };
    }

}