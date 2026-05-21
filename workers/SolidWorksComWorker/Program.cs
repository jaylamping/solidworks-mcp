using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal sealed record WorkerRequest(string Command, JsonElement? Args);

internal static class Program
{
    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [STAThread]
    private static int Main()
    {
        try
        {
            string input = Console.In.ReadToEnd();
            WorkerRequest? request = JsonSerializer.Deserialize<WorkerRequest>(input, ReadJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return WriteError("Missing worker command.");
            }

            object data = request.Command switch
            {
                "status" => Status(request.Args),
                "open" => Open(request.Args),
                "export" => Export(request.Args),
                "measure" => Measure(request.Args),
                "list_features" => ListFeatures(request.Args),
                "inspect_document" => InspectDocument(request.Args),
                "list_components" => ListComponents(request.Args),
                "list_reference_geometry" => ListReferenceGeometry(request.Args),
                "list_bom" => ListBom(request.Args),
                "list_mates" => ListMates(request.Args),
                "set_component_visible" => SetComponentVisible(request.Args),
                "set_component_fixed" => SetComponentFixed(request.Args),
                "rename_component" => RenameComponent(request.Args),
                "mate_coord_sys" => MateCoordSys(request.Args),
                "delete_all_mates" => DeleteAllMates(request.Args),
                "set_component_configuration" => SetComponentConfiguration(request.Args),
                "mate_component_origin" => MateComponentOrigin(request.Args),
                "list_configurations" => ListConfigurations(request.Args),
                "list_dimensions" => ListDimensions(request.Args),
                "set_feature_suppression" => SetFeatureSuppression(request.Args),
                "add_configuration_copy" => AddConfigurationCopy(request.Args),
                "get_component_box" => GetComponentBox(request.Args),
                "transform_component" => TransformComponent(request.Args),
                "set_component_transform" => SetComponentTransform(request.Args),
                "set_dimension" => SetDimension(request.Args),
                "mate_planes" => MatePlanes(request.Args),
                "get_feature_box" => GetFeatureBox(request.Args),
                "mate_coincident" => MateCoincident(request.Args),
                "mate_parallel" => MateParallel(request.Args),
                "align_component_to_feature" => AlignComponentToFeature(request.Args),
                "probe_feature_faces" => ProbeFeatureFaces(request.Args),
                "torso_frame_build_mates" => TorsoFrameBuildMates(request.Args),
                "save_document" => SaveDocument(request.Args),
                "set_custom_properties" => SetCustomProperties(request.Args),
                "replace_components_by_path" => ReplaceComponentsByPath(request.Args),
                _ => throw new InvalidOperationException($"Unknown worker command: {request.Command}"),
            };

            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, WriteJson));
            return 0;
        }
        catch (Exception ex)
        {
            return WriteError(ex.Message);
        }
    }

    private static int WriteError(string error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error }, WriteJson));
        return 0;
    }

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

    private static object MateCoordSys(JsonElement? args)
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
            throw new InvalidOperationException("mate_coord_sys requires an assembly document.");
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

        Mate2? mate = assembly.AddMate5(
            (int)swMateType_e.swMateCOORDINATE,
            (int)swMateAlign_e.swMateAlignALIGNED,
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

        if (mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"AddMate5 failed with error code {mateError}.");
        }

        if (mate is null && mateError == 0)
        {
            throw new InvalidOperationException("AddMate5 returned no mate.");
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            mateError,
            mateCreated = mate is not null,
            alreadyConstrained = mateError == 4,
        };
    }

    private static SelectData? CreateSelectData(ModelDoc2 doc, int mark)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        if (selectionMgr is null)
        {
            return null;
        }

        SelectData? selectData = Try(() => selectionMgr.CreateSelectData()) as SelectData;
        if (selectData is null)
        {
            return null;
        }

        TryVoid(() => selectData.Mark = mark);
        return selectData;
    }

    private static bool SelectComponentReference(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        bool append,
        int mark)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return false;
        }

        Feature? feature = FindFeatureByName(componentDoc, referenceName);
        if (feature is null)
        {
            return false;
        }

        string? componentName = Try(() => component.Name2) as string;
        if (componentName is null)
        {
            return false;
        }

        string? refType = Try(() => feature.GetTypeName2()) as string;
        string selectionType = refType switch
        {
            "CoordSys" => "COORDSYS",
            "RefPlane" => "PLANE",
            "RefAxis" => "AXIS",
            _ => "COORDSYS",
        };

        string selectName = $"{referenceName}@{componentName}";
        bool selected = assemblyDoc.Extension.SelectByID2(
            selectName,
            selectionType,
            0,
            0,
            0,
            append,
            mark,
            null,
            0);

        if (selected)
        {
            return true;
        }

        SelectData? selectData = CreateSelectData(assemblyDoc, mark);
        selected = selectData is not null
            && (Try(() => component.Select4(append, selectData, false)) as bool? ?? false);
        if (!selected)
        {
            return false;
        }

        return Try(() => feature.Select2(append, mark)) as bool? ?? false;
    }

    private static Feature? FindFeatureByName(ModelDoc2 doc, string name)
    {
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 1000)
        {
            dynamic current = feature;
            string? featureName = Try(() => current.Name) as string;
            if (featureName is not null && featureName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return feature as Feature;
            }

            feature = Try(() => current.GetNextFeature());
        }

        return null;
    }

    private static object DeleteAllMates(JsonElement? args)
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
            throw new InvalidOperationException("delete_all_mates requires an assembly document.");
        }

        var mateFeatures = new List<Feature>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is not null)
        {
            object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
            int guard = 0;
            while (subFeature is not null && guard++ < 200)
            {
                if (subFeature is Feature feature)
                {
                    mateFeatures.Add(feature);
                }

                subFeature = Try(() => ((dynamic)subFeature).GetNextSubFeature());
            }
        }

        int deleted = 0;
        foreach (Feature mate in mateFeatures)
        {
            doc.ClearSelection2(true);
            bool selected = Try(() => mate.Select2(false, -1)) as bool? ?? false;
            if (!selected)
            {
                continue;
            }

            bool removed = Try(() => doc.Extension.DeleteSelection2(
                (int)swDeleteSelectionOptions_e.swDelete_Children)) as bool? ?? false;
            if (removed)
            {
                deleted++;
            }
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            deleted,
            mateCountBefore = mateFeatures.Count,
        };
    }

    private static object SetComponentConfiguration(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string configuration = RequiredStringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_configuration requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? previous = Try(() => component.ReferencedConfiguration) as string;
        component.ReferencedConfiguration = configuration;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            from = previous,
            to = Try(() => component.ReferencedConfiguration),
        };
    }

    private static object MateComponentOrigin(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string reference = StringArg(args, "reference") ?? "urdf_link_frame";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_component_origin requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        doc.ClearSelection2(true);
        if (!SelectComponentReference(doc, component, reference, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {reference} on {componentName}");
        }

        if (!SelectAssemblyOrigin(doc, append: true, mark: 2))
        {
            throw new InvalidOperationException("Failed to select assembly origin.");
        }

        Mate2? mate = assembly.AddMate5(
            (int)swMateType_e.swMateCOORDINATE,
            (int)swMateAlign_e.swMateAlignALIGNED,
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

        if (mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"AddMate5 failed with error code {mateError}.");
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            reference,
            mateError,
            mateCreated = mate is not null,
            alreadyConstrained = mateError == 4,
        };
    }

    private static bool SelectAssemblyOrigin(ModelDoc2 doc, bool append, int mark)
    {
        (string id, string type)[] originCandidates =
        [
            (string.Empty, "EXTSKETCHPOINT"),
            ("Origin", "ORIGIN"),
            ("Origin", "POINT"),
            ("", "FACE"),
        ];

        foreach ((string id, string type) in originCandidates)
        {
            if (doc.Extension.SelectByID2(id, type, 0, 0, 0, append, mark, null, 0))
            {
                return true;
            }
        }

        Feature? originFeature = FindFeatureByName(doc, "Origin");
        if (originFeature is not null)
        {
            return Try(() => originFeature.Select2(append, mark)) as bool? ?? false;
        }

        return false;
    }

    private static object ListConfigurations(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("list_configurations requires a part document.");
        }

        ConfigurationManager? manager = Try(() => doc.ConfigurationManager) as ConfigurationManager;
        if (manager is null)
        {
            throw new InvalidOperationException("ConfigurationManager unavailable.");
        }

        var names = new List<string>();
        object? configNames = Try(() => doc.GetConfigurationNames());
        if (configNames is string[] stringNames)
        {
            names.AddRange(stringNames);
        }
        else if (configNames is object[] objectNames)
        {
            foreach (object entry in objectNames)
            {
                if (entry is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            active = Try(() => manager.ActiveConfiguration.Name),
            configurations = names,
        };
    }

    private static object ListDimensions(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
            doc.EditRebuild3();
        }

        var dimensions = new List<object>();
        object? featureObj = Try(() => doc.FirstFeature());
        int guard = 0;
        while (featureObj is not null && guard++ < 1000)
        {
            if (featureObj is Feature feature)
            {
                string? featureName = Try(() => feature.Name) as string;
                string? featureType = Try(() => feature.GetTypeName2()) as string;
                object? displayDimensionObj = Try(() => feature.GetFirstDisplayDimension());
                int dimGuard = 0;
                while (displayDimensionObj is not null && dimGuard++ < 100)
                {
                    if (displayDimensionObj is DisplayDimension displayDimension)
                    {
                        Dimension? dimension = Try(() => displayDimension.GetDimension2(0)) as Dimension;
                        if (dimension is not null)
                        {
                            dimensions.Add(new
                            {
                                feature = featureName,
                                featureType,
                                name = Try(() => dimension.FullName),
                                shortName = Try(() => dimension.Name),
                                systemValue = Try(() => dimension.SystemValue),
                            });
                        }

                        displayDimensionObj = Try(() => feature.GetNextDisplayDimension(displayDimension));
                    }
                    else
                    {
                        break;
                    }
                }
            }

            featureObj = Try(() => ((dynamic)featureObj).GetNextFeature());
        }

        return new
        {
            document = DescribeDocument(doc),
            configuration = configuration ?? Try(() => doc.ConfigurationManager.ActiveConfiguration.Name),
            dimensions,
            count = dimensions.Count,
        };
    }

    private static object SetFeatureSuppression(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string featureName = RequiredStringArg(args, "feature_name");
        bool suppressed = BoolArg(args, "suppressed", defaultValue: true);
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
        }

        Feature? feature = FindFeatureByName(doc, featureName)
            ?? throw new InvalidOperationException($"Feature not found: {featureName}");

        string[] configs = string.IsNullOrWhiteSpace(configuration) ? [] : [configuration];
        bool ok = Try(() => feature.SetSuppression2(
            suppressed
                ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
            string.IsNullOrWhiteSpace(configuration)
                ? (int)swInConfigurationOpts_e.swAllConfiguration
                : (int)swInConfigurationOpts_e.swSpecifyConfiguration,
            configs)) as bool? ?? false;

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            feature = featureName,
            configuration = configuration ?? "all",
            suppressed,
            ok,
        };
    }

    private static object AddConfigurationCopy(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string fromConfig = RequiredStringArg(args, "from");
        string toConfig = RequiredStringArg(args, "to");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("add_configuration_copy requires a part document.");
        }

        ConfigurationManager? manager = Try(() => doc.ConfigurationManager) as ConfigurationManager;
        if (manager is null)
        {
            throw new InvalidOperationException("ConfigurationManager unavailable.");
        }

        Configuration? source = Try(() => doc.GetConfigurationByName(fromConfig)) as Configuration;
        if (source is null)
        {
            throw new InvalidOperationException($"Source configuration not found: {fromConfig}");
        }

        Configuration? existing = Try(() => doc.GetConfigurationByName(toConfig)) as Configuration;
        if (existing is not null)
        {
            return new
            {
                document = DescribeDocument(doc),
                from = fromConfig,
                to = toConfig,
                created = false,
                alreadyExists = true,
            };
        }

        object? newConfig = Try(() => manager.AddConfiguration(toConfig, "", "", 0, fromConfig, ""));
        if (newConfig is not Configuration)
        {
            throw new InvalidOperationException($"Failed to create configuration: {toConfig}");
        }

        doc.ShowConfiguration2(toConfig);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            from = fromConfig,
            to = toConfig,
            created = true,
            alreadyExists = false,
        };
    }

    private static object GetComponentBox(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_component_box requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        object? box = Try(() => component.GetBox(false, false));
        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            boundingBox = Normalize(box),
        };
    }

    private static object TransformComponent(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        double tx = DoubleArg(args, "tx", 0);
        double ty = DoubleArg(args, "ty", 0);
        double tz = DoubleArg(args, "tz", 0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("transform_component requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        object? currentTransform = Try(() => component.Transform2) as object;
        if (currentTransform is not MathTransform mathTransform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        double[] data = Try(() => mathTransform.ArrayData) as double[] ?? throw new InvalidOperationException("Transform data unavailable.");
        if (data.Length < 16)
        {
            throw new InvalidOperationException("Unexpected transform matrix size.");
        }

        data[9] += tx;
        data[10] += ty;
        data[11] += tz;
        mathTransform.ArrayData = data;
        component.Transform2 = mathTransform;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            translation = new[] { tx, ty, tz },
        };
    }

    private static object SetComponentTransform(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        double[] matrix = DoubleArrayArg(args, "matrix");
        bool fix = BoolArg(args, "fix", defaultValue: true);

        if (matrix.Length != 16)
        {
            throw new InvalidOperationException("matrix must contain 16 numbers.");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_transform requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (Try(() => component.IsFixed()) as bool? == true)
        {
            component.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        MathTransform? transform = Try(() => component.Transform2) as MathTransform;
        if (transform is null)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        transform.ArrayData = matrix;
        component.Transform2 = transform;

        if (fix)
        {
            component.Select4(false, null, false);
            assembly.FixComponent();
            doc.ClearSelection2(true);
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            fixedState = Try(() => component.IsFixed()),
            matrix,
        };
    }

    private static object SetDimension(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string dimensionName = RequiredStringArg(args, "dimension");
        double valueMeters = DoubleArg(args, "value_meters", 0);
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
        }

        Dimension? dimension = Try(() => doc.Parameter(dimensionName)) as Dimension;
        if (dimension is null)
        {
            throw new InvalidOperationException($"Dimension not found: {dimensionName}");
        }

        double? previous = Try(() => dimension.SystemValue) as double?;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            string[] configurations = [configuration];
            Try(() => dimension.SetSystemValue3(
                valueMeters,
                (int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations,
                configurations));
        }
        else
        {
            dimension.SystemValue = valueMeters;
        }
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            configuration = configuration ?? Try(() => doc.ConfigurationManager.ActiveConfiguration.Name),
            dimension = dimensionName,
            from = previous,
            to = Try(() => dimension.SystemValue),
        };
    }

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

        if (mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"AddMate5 failed with error code {mateError}.");
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
            mateCreated = mate is not null,
            alreadyConstrained = mateError == 4,
        };
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

        const string layoutPrefix = "marengo_torso_layout_revA";
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
                    && (Try(() => component.Name2) as string)?.StartsWith("marengo_torso_layout_revA", StringComparison.OrdinalIgnoreCase) == false)
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
        const string vendorPath = "C:/code/marengo/hardware/cad/vendor/vendor_2020_black_extrusion.SLDPRT";
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
            double[]? partBox = Try(() => partDoc.GetPartBox(true)) as double[];
            return partBox is null ? null : TransformPartBoxToAssembly(component, partBox);
        }

        return null;
    }

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

        if (!File.Exists(toPartPath))
        {
            throw new FileNotFoundException("Replacement part does not exist.", toPartPath);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
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
        if (save)
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

    private static Component2? FindComponent(IAssemblyDoc assembly, Component2? parent, string nameOrPrefix)
    {
        if (parent is null)
        {
            Component2? direct = Try(() => assembly.GetComponentByName(nameOrPrefix)) as Component2;
            if (direct is not null)
            {
                return direct;
            }
        }

        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(true)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return null;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            string? name = Try(() => component.Name2) as string;
            if (name is not null
                && (name.Equals(nameOrPrefix, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(nameOrPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return component;
            }

            Component2? nested = FindComponent(assembly, component, nameOrPrefix);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void CollectComponentTree(IAssemblyDoc assembly, Component2? parent, List<object> output, int depth)
    {
        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(false)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            output.Add(DescribeComponent(component, depth));
            CollectComponentTree(assembly, component, output, depth + 1);
        }
    }

    private static void CollectBomLines(IAssemblyDoc assembly, Component2? parent, List<object> output)
    {
        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(true)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            output.Add(DescribeComponent(component, depth: 0));
            CollectBomLines(assembly, component, output);
        }
    }

    private static object DescribeComponent(Component2 component, int depth)
    {
        return new
        {
            name = Try(() => component.Name2),
            path = Try(() => component.GetPathName()),
            configuration = Try(() => component.ReferencedConfiguration),
            suppressed = Try(() => component.IsSuppressed()),
            isFixed = Try(() => component.IsFixed()),
            visible = Try(() => component.Visible),
            depth,
        };
    }

    private static object DescribeUnits(ISldWorks app, ModelDoc2 doc)
    {
        int? lengthUnit = Try(() => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear)) as int?;
        int? massUnit = Try(() => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsMassPropMass)) as int?;

        return new
        {
            length = lengthUnit,
            mass = massUnit,
            documentType = Try(() => doc.GetType()),
        };
    }

    private static IReadOnlyList<object> ListCustomProperties(ModelDoc2 doc)
    {
        var properties = new List<object>();
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            return properties;
        }

        string[]? names = Try(() => manager.GetNames()) as string[];
        if (names is null)
        {
            return properties;
        }

        foreach (string name in names)
        {
            string value = "";
            string resolved = "";
            TryVoid(() => manager.Get2(name, out value, out resolved));
            properties.Add(new
            {
                name,
                value = string.IsNullOrWhiteSpace(resolved) ? value : resolved,
            });
        }

        return properties;
    }

    private static ISldWorks AttachSolidWorks(bool startIfMissing)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SolidWorks COM automation requires Windows.");
        }

        Type? swType = Type.GetTypeFromProgID("SldWorks.Application", throwOnError: false);
        if (swType is null)
        {
            throw new InvalidOperationException("SldWorks.Application COM ProgID is not registered.");
        }

        Guid classId = ComClassId.FromProgId("SldWorks.Application");

        if (IsSolidWorksProcessRunning())
        {
            try
            {
                return (ISldWorks)RunningObjectTable.GetActiveObject(classId);
            }
            catch when (startIfMissing)
            {
                // Fall through to explicit activation below.
            }
            catch
            {
                throw new InvalidOperationException("SolidWorks process is running, but COM attach failed.");
            }
        }

        if (startIfMissing)
        {
            object? created = Activator.CreateInstance(swType);
            if (created is null)
            {
                throw new InvalidOperationException("Failed to start SolidWorks via COM.");
            }

            ISldWorks app = (ISldWorks)created;
            app.Visible = true;
            return app;
        }

        throw new InvalidOperationException("SolidWorks is not running. Pass start_if_missing=true to launch it.");
    }

    private static bool IsSolidWorksProcessRunning()
    {
        int currentProcessId = System.Environment.ProcessId;
        return Process.GetProcesses().Any(process =>
            process.Id != currentProcessId
            && process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase));
    }

    private static ModelDoc2 OpenDocument(ISldWorks app, string path)
    {
        int errors = 0;
        int warnings = 0;
        if (IsNeutralCad(path))
        {
            object? importData = Try(() => app.GetImportFileData(path));
            ModelDoc2? imported = app.LoadFile4(path, "r", importData, ref errors);
            if (imported is null || errors != 0)
            {
                throw new InvalidOperationException(
                    $"SolidWorks failed to import {path}. errors={errors} ({DecodeFileLoadErrors(errors)}), warnings={warnings}");
            }

            return imported;
        }

        int docType = DocumentType(path);
        ModelDoc2? doc = app.OpenDoc6(path, docType, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as ModelDoc2;
        if (doc is null || errors != 0)
        {
            throw new InvalidOperationException(
                $"SolidWorks failed to open {path}. errors={errors} ({DecodeFileLoadErrors(errors)}), warnings={warnings}");
        }

        string? title = Try(() => doc.GetTitle()) as string;
        if (!string.IsNullOrWhiteSpace(title))
        {
            int activateErrors = 0;
            Try(() => app.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors));
        }

        return doc;
    }

    private static bool IsNeutralCad(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".step" or ".stp" or ".iges" or ".igs";
    }

    private static string DecodeFileLoadErrors(int errors)
    {
        if (errors == 0)
        {
            return "none";
        }

        var names = new List<string>();
        foreach (swFileLoadError_e value in Enum.GetValues<swFileLoadError_e>())
        {
            int flag = (int)value;
            if (flag != 0 && (errors & flag) == flag)
            {
                names.Add(value.ToString());
            }
        }

        return names.Count == 0 ? "unknown" : string.Join("|", names);
    }

    private static int DocumentType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".sldasm" => 2,
            ".slddrw" => 3,
            ".sldprt" => 1,
            ".step" => 1,
            ".stp" => 1,
            _ => 1,
        };
    }

    private static object? DescribeDocument(object? doc)
    {
        if (doc is null)
        {
            return null;
        }

        if (doc is ModelDoc2 modelDoc)
        {
            return new
            {
                title = Try(() => modelDoc.GetTitle()),
                path = Try(() => modelDoc.GetPathName()),
                type = Try(() => modelDoc.GetType()),
            };
        }

        dynamic d = doc;
        return new
        {
            title = Try(() => d.GetTitle()),
            path = Try(() => d.GetPathName()),
            type = Try(() => d.GetType()),
        };
    }

    private static object? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Array array)
        {
            var normalized = new List<object?>();
            foreach (object? item in array)
            {
                normalized.Add(Normalize(item));
            }

            return normalized;
        }

        return value;
    }

    private static object? Try(Func<object?> action)
    {
        try
        {
            return action();
        }
        catch
        {
            return null;
        }
    }

    private static void TryVoid(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Preview preparation is best-effort.
        }
    }

    private static string RequiredStringArg(JsonElement? args, string name)
    {
        return StringArg(args, name) ?? throw new ArgumentException($"Missing required argument: {name}");
    }

    private static string? StringArg(JsonElement? args, string name)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return args.Value.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, string> PropertiesArg(JsonElement? args)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("properties object is required.");
        }

        if (!args.Value.TryGetProperty("properties", out JsonElement props) || props.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("properties object is required.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty prop in props.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText(),
            };
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("properties object must contain at least one entry.");
        }

        return result;
    }

    private static bool BoolArg(JsonElement? args, string name, bool defaultValue = false)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        return args.Value.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True
            ? true
            : value.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }

    private static double DoubleArg(JsonElement? args, string name, double defaultValue = 0)
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
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out double parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static double[] DoubleArrayArg(JsonElement? args, string name)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Missing required argument: {name}");
        }

        if (!args.Value.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Missing required array argument: {name}");
        }

        var values = new List<double>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number)
            {
                throw new ArgumentException($"Array argument {name} must contain only numbers.");
            }

            values.Add(entry.GetDouble());
        }

        return values.ToArray();
    }
}

internal static class RunningObjectTable
{
    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object GetActiveObject(Guid classId)
    {
        int hr = GetActiveObject(ref classId, IntPtr.Zero, out object activeObject);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return activeObject;
    }
}

internal static class ComClassId
{
    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string progId,
        out Guid classId);

    public static Guid FromProgId(string progId)
    {
        CLSIDFromProgID(progId, out Guid classId);
        return classId;
    }
}
