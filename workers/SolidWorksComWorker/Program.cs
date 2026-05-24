using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal sealed record WorkerRequest(string Command, JsonElement? Args);

internal static class Program
{
    private const string ComLockName = @"Global\MarengoSolidWorksComWorker";
    private static readonly TimeSpan ComLockTimeout = TimeSpan.FromMinutes(5);

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
        using Mutex comLock = new(false, ComLockName);
        if (!comLock.WaitOne(ComLockTimeout))
        {
            return WriteError(
                "Timed out waiting for SolidWorks COM lock. Another worker or script is using SolidWorks.");
        }

        try
        {
            return RunWorker();
        }
        finally
        {
            comLock.ReleaseMutex();
        }
    }

    private static int RunWorker()
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
                "delete_mates_in_range" => DeleteMatesInRange(request.Args),
                "unfix_all_components" => UnfixAllComponents(request.Args),
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
                "close_all_documents" => CloseAllDocuments(request.Args),
                "diagnose_part_save" => DiagnosePartSave(request.Args),
                "clone_solid_body_part" => CloneSolidBodyPart(request.Args),
                "mirror_part_file" => MirrorPartFile(request.Args),
                "make_component_independent" => MakeComponentIndependent(request.Args),
                "set_custom_properties" => SetCustomProperties(request.Args),
                "replace_components_by_path" => ReplaceComponentsByPath(request.Args),
                "replace_component_path" => ReplaceComponentPath(request.Args),
                "layout_add_shoulder_mounts" => LayoutAddShoulderMounts(request.Args),
                "place_shoulder_roll_motors" => PlaceShoulderRollMotors(request.Args),
                "get_component_transform" => GetComponentTransform(request.Args),
                "capture_shoulder_roll_golden" => CaptureShoulderRollGolden(request.Args),
                "apply_shoulder_roll_golden" => ApplyShoulderRollGolden(request.Args),
                "mate_shoulder_roll_motor" => MateShoulderRollMotor(request.Args),
                "vendor_add_rs03_urdf_frame" => VendorAddRs03UrdfFrame(request.Args),
                "insert_component" => InsertComponent(request.Args),
                "cut_actuator_cavity" => CutActuatorCavity(request.Args),
                "build_torso_compute_shelf" => BuildTorsoComputeShelf(request.Args),
                "mate_torso_compute_shelf" => MateTorsoComputeShelf(request.Args),
                "debug_mate_entities" => DebugMateEntities(request.Args),
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
        if (refType == "RefPlane")
        {
            return SelectComponentPlaneFeature(assemblyDoc, component, feature, append, mark);
        }

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

        return false;
    }

    private static bool SelectComponentPlaneFeature(
        ModelDoc2 assemblyDoc,
        Component2 component,
        Feature planeFeature,
        bool append,
        int mark)
    {
        string? componentName = Try(() => component.Name2) as string;
        string? planeName = Try(() => planeFeature.Name) as string;
        if (componentName is not null && planeName is not null
            && assemblyDoc.Extension.SelectByID2(
                $"{planeName}@{componentName}",
                "PLANE",
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

        object? facesObj = Try(() => planeFeature.GetFaces());
        if (facesObj is object[] planeFaces && planeFaces.Length > 0 && planeFaces[0] is Face2 planeFace)
        {
            SelectData? selectData = CreateSelectData(assemblyDoc, mark);
            Entity? entity = planeFace as Entity;
            if (entity is not null && selectData is not null
                && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
            {
                return true;
            }

            if (SelectFeatureFaceByRay(assemblyDoc, component, planeFace, append, mark))
            {
                return true;
            }
        }

        if (Try(() => planeFeature.GetSpecificFeature2()) is RefPlane refPlane)
        {
            MathTransform? planeTransform = Try(() => refPlane.Transform) as MathTransform;
            MathTransform? componentTransform = Try(() => component.Transform2) as MathTransform;
            if (planeTransform?.ArrayData is double[] planeMatrix && componentTransform is not null)
            {
                double[] normalized = NormalizeTransformMatrix(planeMatrix);
                double[] asmOrigin = TransformPointManual(
                    componentTransform,
                    normalized[9],
                    normalized[10],
                    normalized[11]);
                double[] asmNormal = TransformDirectionManual(
                    componentTransform,
                    normalized[6],
                    normalized[7],
                    normalized[8]);
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    if (assemblyDoc.Extension.SelectByRay(
                            asmOrigin[0] + (asmNormal[0] * 0.01 * sign),
                            asmOrigin[1] + (asmNormal[1] * 0.01 * sign),
                            asmOrigin[2] + (asmNormal[2] * 0.01 * sign),
                            asmNormal[0] * sign,
                            asmNormal[1] * sign,
                            asmNormal[2] * sign,
                            0.001,
                            mark,
                            append,
                            0,
                            0))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static double[] TransformDirectionManual(MathTransform transform, double x, double y, double z)
    {
        double[] matrix = Try(() => transform.ArrayData) as double[] ?? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        return
        [
            matrix[0] * x + matrix[1] * y + matrix[2] * z,
            matrix[3] * x + matrix[4] * y + matrix[5] * z,
            matrix[6] * x + matrix[7] * y + matrix[8] * z,
        ];
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

        object result = DeleteAllMatesInternal(doc);
        return new
        {
            document = DescribeDocument(doc),
            deleted = result.GetType().GetProperty("deleted")?.GetValue(result),
            mateCountBefore = result.GetType().GetProperty("mateCountBefore")?.GetValue(result),
            mateCountAfter = result.GetType().GetProperty("mateCountAfter")?.GetValue(result),
        };
    }

    private static object UnfixAllComponents(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        string? exceptPrefix = StringArg(args, "except_prefix");

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("unfix_all_components requires an assembly document.");
        }

        object unfixResult = UnfixAllComponentsInternal((IAssemblyDoc)doc, exceptPrefix);
        return new
        {
            document = DescribeDocument(doc),
            unfixed = unfixResult.GetType().GetProperty("unfixed")?.GetValue(unfixResult),
            unfixedCount = unfixResult.GetType().GetProperty("unfixedCount")?.GetValue(unfixResult),
        };
    }

    private static object RepairTorsoFrame_DISABLED(JsonElement? args)
    {
        string assemblyPath = RequiredStringArg(args, "path");
        bool includeBrackets = BoolArg(args, "include_brackets", defaultValue: true);
        bool rebuildConfigs = BoolArg(args, "rebuild_configs", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, assemblyPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("repair_torso_frame requires an assembly document.");
        }

        object deleteResult = DeleteAllMatesInternal(doc);
        object unfixResult = UnfixAllComponentsInternal((IAssemblyDoc)doc, exceptPrefix: null);
        object buildResult = TorsoFrameBuildMates(args);

        return new
        {
            document = DescribeDocument(doc),
            deleteResult,
            unfixResult,
            buildResult,
        };
    }

    private static object DeleteAllMatesInternal(ModelDoc2 doc)
    {
        var mateFeatures = CollectMateFeatures(doc);
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
            deleted,
            mateCountBefore = mateFeatures.Count,
            mateCountAfter = CountAssemblyMates(doc),
        };
    }

    private static object UnfixAllComponentsInternal(IAssemblyDoc assembly, string? exceptPrefix)
    {
        ModelDoc2 doc = (ModelDoc2)assembly;
        var unfixed = new List<string>();
        object[]? roots = Try(() => assembly.GetComponents(true)) as object[];
        if (roots is not null)
        {
            foreach (object entry in roots)
            {
                if (entry is not Component2 component)
                {
                    continue;
                }

                string? name = Try(() => component.Name2) as string;
                if (name is not null
                    && !string.IsNullOrWhiteSpace(exceptPrefix)
                    && name.Contains(exceptPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Try(() => component.IsFixed()) as bool? != true)
                {
                    continue;
                }

                component.Select4(false, null, false);
                assembly.UnfixComponent();
                doc.ClearSelection2(true);
                if (name is not null)
                {
                    unfixed.Add(name);
                }
            }
        }

        doc.EditRebuild3();

        return new
        {
            unfixed,
            unfixedCount = unfixed.Count,
        };
    }

    private static List<Feature> CollectMateFeatures(ModelDoc2 doc)
    {
        var mateFeatures = new List<Feature>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is not null)
        {
            object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
            int guard = 0;
            while (subFeature is not null && guard++ < 500)
            {
                if (subFeature is Feature feature)
                {
                    mateFeatures.Add(feature);
                }

                subFeature = Try(() => ((dynamic)subFeature).GetNextSubFeature());
            }
        }

        return mateFeatures;
    }

    private static object DeleteMatesInRange(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        int minNumber = (int)DoubleArg(args, "min_number", 0);
        int maxNumber = (int)DoubleArg(args, "max_number", 0);
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("delete_mates_in_range requires an assembly document.");
        }

        var deletedNames = new List<string>();
        foreach (Feature mate in CollectMateFeatures(doc))
        {
            string? name = Try(() => mate.Name) as string;
            if (name is null || !TryParseMateNumber(name, out int number))
            {
                continue;
            }

            if (number < minNumber || number > maxNumber)
            {
                continue;
            }

            doc.ClearSelection2(true);
            if (Try(() => mate.Select2(false, -1)) as bool? != true)
            {
                continue;
            }

            if (Try(() => doc.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Children)) as bool? == true)
            {
                deletedNames.Add(name);
            }
        }

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
            minNumber,
            maxNumber,
            deleted = deletedNames,
            deletedCount = deletedNames.Count,
            mateCountAfter = CountAssemblyMates(doc),
            saved,
            errors,
            warnings,
        };
    }

    private static bool TryParseMateNumber(string mateName, out int number)
    {
        number = 0;
        if (!mateName.StartsWith("Coincident", StringComparison.OrdinalIgnoreCase)
            && !mateName.StartsWith("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int prefixLength = mateName.StartsWith("Coincident", StringComparison.OrdinalIgnoreCase) ? 10 : 8;
        return int.TryParse(mateName[prefixLength..], out number);
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

        ApplyComponentTransformMatrix(component, matrix);

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

    private static object GetComponentTransform(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_component_transform requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        double[] matrix = ReadComponentTransformMatrix(component);
        object? box = Try(() => component.GetBox(false, false));

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            matrix,
            boundingBoxM = Normalize(box),
            isFixed = Try(() => component.IsFixed()),
        };
    }

    private static object CaptureShoulderRollGolden(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string leftPrefix = StringArg(args, "left_component") ?? "actuator_rs03_left_shoulder_roll";
        string rightPrefix = StringArg(args, "right_component") ?? "actuator_rs03_right_shoulder_roll";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("capture_shoulder_roll_golden requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? left = FindComponent(assembly, null, leftPrefix)
            ?? throw new InvalidOperationException($"Left shoulder motor not found: {leftPrefix}");

        Component2? right = FindComponent(assembly, null, rightPrefix)
            ?? FindShoulderRollRightComponent(assembly, left);
        if (right is null)
        {
            throw new InvalidOperationException($"Right shoulder motor not found: {rightPrefix}");
        }

        return new
        {
            document = DescribeDocument(doc),
            capturedAtUtc = DateTime.UtcNow.ToString("o"),
            convention = new
            {
                axis = "assembly ICE: X width, Y up, Z depth",
                leftSide = "-X half, inner flange near -40.4 mm",
                rightSide = "+X half, inner flange near +40.4 mm",
                shoulderTopYM = 0.495,
            },
            left = DescribeGoldenShoulderComponent(left),
            right = DescribeGoldenShoulderComponent(right),
        };
    }

    private static object ApplyShoulderRollGolden(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool save = BoolArg(args, "save", defaultValue: true);
        bool fix = BoolArg(args, "fix", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("apply_shoulder_roll_golden requires an assembly document.");
        }

        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("apply_shoulder_roll_golden requires golden side payloads.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        var applied = new List<object>();

        foreach (string side in new[] { "left", "right" })
        {
            if (!args.Value.TryGetProperty(side, out JsonElement sidePayload)
                || sidePayload.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string componentName = sidePayload.TryGetProperty("component", out JsonElement componentElement)
                && componentElement.ValueKind == JsonValueKind.String
                ? componentElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(componentName))
            {
                componentName = sidePayload.TryGetProperty("componentPrefix", out JsonElement prefixElement)
                    && prefixElement.ValueKind == JsonValueKind.String
                    ? prefixElement.GetString() ?? string.Empty
                    : side.Equals("left", StringComparison.OrdinalIgnoreCase)
                        ? "actuator_rs03_left_shoulder_roll"
                        : "actuator_rs03_right_shoulder_roll";
            }

            double[] matrix = sidePayload.TryGetProperty("matrix", out JsonElement matrixElement)
                ? matrixElement.EnumerateArray().Select(entry => entry.GetDouble()).ToArray()
                : throw new InvalidOperationException($"Missing matrix for {side}.");

            Component2? component = FindComponent(assembly, null, componentName)
                ?? throw new InvalidOperationException($"Component not found for {side}: {componentName}");

            if (Try(() => component.IsFixed()) as bool? == true)
            {
                component.Select4(false, null, false);
                assembly.UnfixComponent();
                doc.ClearSelection2(true);
            }

            ApplyComponentTransformMatrix(component, matrix);

            if (fix)
            {
                component.Select4(false, null, false);
                assembly.FixComponent();
                doc.ClearSelection2(true);
            }

            applied.Add(new
            {
                side,
                component = Try(() => component.Name2),
                matrix,
                boundingBoxM = Normalize(Try(() => component.GetBox(false, false))),
                isFixed = Try(() => component.IsFixed()),
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
                throw new InvalidOperationException($"Save failed after golden apply. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            applied,
            saved,
            errors,
            warnings,
        };
    }

    private static object DescribeGoldenShoulderComponent(Component2 component)
    {
        double[] matrix = ReadComponentTransformMatrix(component);
        double[]? box = Try(() => component.GetBox(false, false)) as double[];

        return new
        {
            component = Try(() => component.Name2),
            componentPrefix = Try(() => component.Name2) as string,
            matrix,
            boundingBoxM = box,
            minXM = box is { Length: >= 1 } ? box[0] : (double?)null,
            maxXM = box is { Length: >= 4 } ? box[3] : (double?)null,
            shoulderTopYM = box is { Length: >= 5 } ? box[4] : (double?)null,
            isFixed = Try(() => component.IsFixed()),
        };
    }

    private static Component2? FindShoulderRollRightComponent(IAssemblyDoc assembly, Component2 left)
    {
        string? leftName = Try(() => left.Name2) as string;
        foreach (Component2 component in EnumerateAllComponents(assembly))
        {
            string? name = Try(() => component.Name2) as string;
            if (name is null || name.Equals(leftName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.Contains("vendor_robstride_rs03_vendor", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("right_shoulder_roll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("waist_yaw", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double[]? box = Try(() => component.GetBox(false, false)) as double[];
            if (box is { Length: >= 4 } && box[0] > 0)
            {
                return component;
            }
        }

        return null;
    }

    private static double[] ReadComponentTransformMatrix(Component2 component)
    {
        if (component.Transform2 is not MathTransform transform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        double[] matrix = Try(() => transform.ArrayData) as double[]
            ?? throw new InvalidOperationException("Transform data unavailable.");

        return NormalizeTransformMatrix(matrix);
    }

    private static double[] NormalizeTransformMatrix(double[] matrix)
    {
        if (matrix.Length == 16)
        {
            return matrix;
        }

        if (matrix.Length == 12)
        {
            return
            [
                matrix[0], matrix[1], matrix[2], 0,
                matrix[3], matrix[4], matrix[5], 0,
                matrix[6], matrix[7], matrix[8], 0,
                matrix[9], matrix[10], matrix[11], 1,
            ];
        }

        throw new InvalidOperationException($"Unexpected transform matrix size: {matrix.Length}.");
    }

    private static bool IsNearIdentityTransform(double[] matrix)
    {
        return Math.Abs(matrix[0] - 1.0) < 1e-9
            && Math.Abs(matrix[1]) < 1e-9
            && Math.Abs(matrix[2]) < 1e-9
            && Math.Abs(matrix[3]) < 1e-9
            && Math.Abs(matrix[4] - 1.0) < 1e-9
            && Math.Abs(matrix[5]) < 1e-9
            && Math.Abs(matrix[6]) < 1e-9
            && Math.Abs(matrix[7]) < 1e-9
            && Math.Abs(matrix[8] - 1.0) < 1e-9
            && Math.Abs(matrix[9]) < 1e-9
            && Math.Abs(matrix[10]) < 1e-9
            && Math.Abs(matrix[11]) < 1e-9;
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

    private static object LayoutAddShoulderMounts(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool save = BoolArg(args, "save", defaultValue: true);
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);
        double innerRailM = DoubleArg(args, "inner_rail_mm", 55.0) / 1000.0;
        double outerPokeM = DoubleArg(args, "outer_poke_mm", 95.0) / 1000.0;
        double depthOffsetM = DoubleArg(args, "depth_offset_mm", 0.0) / 1000.0;

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("layout_add_shoulder_mounts requires a part document.");
        }

        double[]? innerBox = GetFeatureBoundingBoxInPart(doc, "torso_inner_clear");
        double[]? outerBox = GetFeatureBoundingBoxInPart(doc, "torso_outer_envelope");
        if (innerBox is null || outerBox is null)
        {
            throw new InvalidOperationException(
                "Layout part must contain torso_inner_clear and torso_outer_envelope ICE.");
        }

        double shoulderHeight = outerBox[4];
        double midDepth = (innerBox[2] + innerBox[5]) / 2.0;
        double depth = midDepth + depthOffsetM;
        double jointLeftX = innerRailM / 2.0;
        double jointRightX = -jointLeftX;

        var created = new List<object>();
        foreach ((string planeName, double offsetM) in new[]
                 {
                     ("shoulder_rail_inner_left", innerRailM),
                     ("shoulder_rail_inner_right", -innerRailM),
                     ("shoulder_poke_outer_left", outerPokeM),
                     ("shoulder_poke_outer_right", -outerPokeM),
                 })
        {
            if (replaceExisting)
            {
                DeleteFeatureByName(doc, planeName);
            }
            else if (FindFeatureByName(doc, planeName) is not null)
            {
                created.Add(new { name = planeName, type = "plane", skipped = true, reason = "already_exists" });
                continue;
            }

            Feature? plane = InsertOffsetPlaneFromRight(doc, offsetM, planeName);
            if (plane is null)
            {
                throw new InvalidOperationException($"Failed to create offset plane: {planeName}");
            }

            created.Add(new { name = planeName, type = "plane", offsetM = offsetM, skipped = false });
        }

        foreach ((string name, double jointX) in new[]
                 {
                     ("shoulder_mount_left", jointLeftX),
                     ("shoulder_mount_right", jointRightX),
                 })
        {
            if (replaceExisting)
            {
                DeleteFeatureByName(doc, name);
            }
            else if (FindFeatureByName(doc, name) is not null)
            {
                created.Add(new { name, type = "coord_sys", skipped = true, reason = "already_exists" });
                continue;
            }

            Feature? coordFeature = CreateShoulderMountCoordSys(doc, name, jointX, shoulderHeight, depth);
            if (coordFeature is null)
            {
                throw new InvalidOperationException($"Failed to create coordinate system: {name}");
            }

            created.Add(new
            {
                name,
                type = "coord_sys",
                role = "joint_axis",
                originM = new[] { jointX, shoulderHeight, depth },
                skipped = false,
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
                throw new InvalidOperationException($"Save failed after shoulder mounts. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            shoulderHeightM = shoulderHeight,
            innerRailM = innerRailM,
            outerPokeM = outerPokeM,
            innerClearBoxM = innerBox,
            created,
            saved,
            errors,
            warnings,
        };
    }

    private static object InsertComponent(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string partPath = RequiredStringArg(args, "part_path");
        string? name = StringArg(args, "name");
        string? configuration = StringArg(args, "configuration") ?? string.Empty;
        bool save = BoolArg(args, "save", defaultValue: true);

        string resolvedPartPath = Path.GetFullPath(partPath);
        if (!File.Exists(resolvedPartPath))
        {
            throw new InvalidOperationException($"Part file not found: {resolvedPartPath}");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("insert_component requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        AssemblyDoc assemblyDoc = (AssemblyDoc)doc;
        TryVoid(() => app.ActivateDoc3(doc.GetTitle(), true, 0, 0));

        // Ensure the part loads before inserting into the assembly.
        OpenDocument(app, resolvedPartPath);

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object[]? beforeComponents = Try(() => assembly.GetComponents(false)) as object[];
        if (beforeComponents is not null)
        {
            foreach (object entry in beforeComponents)
            {
                if (entry is Component2 existing && Try(() => existing.Name2) is string existingName)
                {
                    existingNames.Add(existingName);
                }
            }
        }

        Component2? component = null;
        try
        {
            component = assemblyDoc.AddComponent5(
                resolvedPartPath,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                configuration,
                false,
                string.Empty,
                0,
                0,
                0) as Component2;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"AddComponent5 failed for {resolvedPartPath}: {ex.Message}");
        }

        if (component is null)
        {
            object[]? afterComponents = Try(() => assembly.GetComponents(false)) as object[];
            if (afterComponents is not null)
            {
                foreach (object entry in afterComponents)
                {
                    if (entry is not Component2 candidate)
                    {
                        continue;
                    }

                    string? candidateName = Try(() => candidate.Name2) as string;
                    if (candidateName is not null && !existingNames.Contains(candidateName))
                    {
                        component = candidate;
                        break;
                    }
                }
            }
        }

        if (component is null)
        {
            throw new InvalidOperationException($"Failed to insert component: {resolvedPartPath}");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            doc.ClearSelection2(true);
            bool selected = Try(() => component.Select4(false, null, false)) as bool? ?? false;
            if (!selected)
            {
                throw new InvalidOperationException($"Could not select inserted component for rename: {Try(() => component.Name2)}");
            }

            bool renamed = Try(() => ((dynamic)component).SetName(name)) as bool? ?? false;
            if (!renamed)
            {
                TryVoid(() => component.Name2 = name);
            }

            doc.ClearSelection2(true);
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
                throw new InvalidOperationException($"Save failed after insert. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            partPath,
            saved,
            errors,
            warnings,
        };
    }

    private static object CutActuatorCavity(JsonElement? args)
    {
        string assemblyPath = RequiredStringArg(args, "path");
        string bracketPartPath = StringArg(args, "bracket_part_path")
            ?? "C:/code/marengo/hardware/cad/parts/marengo_shoulder_roll_mount_bracket_right_revA.SLDPRT";
        string toolPartPath = StringArg(args, "tool_part_path")
            ?? "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";
        string bracketComponent = StringArg(args, "bracket_component") ?? "marengo_shoulder_roll_mount_bracket_right_revA";
        string toolComponent = StringArg(args, "tool_component") ?? "actuator_rs03_right_shoulder_roll";
        double clearanceM = DoubleArg(args, "clearance_mm", 0.5) / 1000.0;
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("cut_actuator_cavity requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2? bracketInAsm = FindComponent(assembly, null, bracketComponent)
            ?? throw new InvalidOperationException($"Bracket component not found: {bracketComponent}");
        Component2? toolInAsm = FindComponent(assembly, null, toolComponent)
            ?? throw new InvalidOperationException($"Tool component not found: {toolComponent}");

        object? inContextResult = CutActuatorCavityInContext(app, asmDoc, assembly, bracketInAsm, toolInAsm, clearanceM);
        if (inContextResult is not null)
        {
            bool saved = false;
            int errors = 0;
            int warnings = 0;
            if (save)
            {
                saved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                if (!saved || errors != 0)
                {
                    throw new InvalidOperationException($"Assembly save failed. errors={errors}, warnings={warnings}");
                }

                ModelDoc2 bracketDoc = OpenDocument(app, bracketPartPath);
                saved = bracketDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            }

            return new
            {
                mode = "edit_part_indent",
                document = DescribeDocument(asmDoc),
                bracketPartPath,
                toolComponent = Try(() => toolInAsm.Name2),
                result = inContextResult,
                saved,
                errors,
                warnings,
            };
        }

        return CutActuatorCavityByInsertPart(
            app,
            assemblyPath,
            bracketPartPath,
            toolPartPath,
            bracketInAsm,
            toolInAsm,
            clearanceM,
            save);
    }

    private static bool SelectAssemblyComponent(ModelDoc2 asmDoc, Component2 component, bool append, int mark = 0)
    {
        string? compName = Try(() => component.Name2) as string;
        string? docTitle = Try(() => asmDoc.GetTitle()) as string;
        if (!string.IsNullOrWhiteSpace(compName) && !string.IsNullOrWhiteSpace(docTitle))
        {
            string selectionId = compName.Contains('@', StringComparison.Ordinal)
                ? compName
                : $"{compName}@{docTitle}";
            if (Try(() => asmDoc.Extension.SelectByID2(
                    selectionId,
                    "COMPONENT",
                    0,
                    0,
                    0,
                    append,
                    mark,
                    null,
                    0)) as bool? == true)
            {
                return true;
            }
        }

        SelectData? selectData = mark == 0 ? null : CreateSelectData(asmDoc, mark);
        return Try(() => component.Select4(append, selectData, false)) as bool? == true;
    }

    private static object? CutActuatorCavityInContext(
        ISldWorks app,
        ModelDoc2 asmDoc,
        IAssemblyDoc assembly,
        Component2 bracketInAsm,
        Component2 toolInAsm,
        double clearanceM)
    {
        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, bracketInAsm, append: false))
        {
            return null;
        }

        ModelDoc2? partDoc;
        try
        {
            int editInfo = 0;
            TryVoid(() => assembly.EditPart2(false, false, ref editInfo));
            partDoc = app.IActiveDoc2 as ModelDoc2;
        }
        catch
        {
            return null;
        }

        if (partDoc is null)
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        string? bracketPath = Try(() => bracketInAsm.GetPathName()) as string;
        string? activePath = Try(() => partDoc.GetPathName()) as string;
        if (!string.IsNullOrWhiteSpace(bracketPath)
            && !string.IsNullOrWhiteSpace(activePath)
            && !string.Equals(Path.GetFullPath(activePath), Path.GetFullPath(bracketPath), StringComparison.OrdinalIgnoreCase))
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, toolInAsm, append: false, mark: 1))
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        dynamic featMgr = partDoc.FeatureManager;
        Feature? indentFeature = Try(() => featMgr.InsertIndent(
            false,
            clearanceM,
            false,
            false,
            1)) as Feature;

        if (indentFeature is null)
        {
            indentFeature = Try(() => featMgr.InsertIndent(false, clearanceM, false, false)) as Feature;
        }

        if (indentFeature is null)
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        TryVoid(() => indentFeature.Name = "Indent-Actuator-Cavity1");
        partDoc.EditRebuild3();
        TryVoid(() => assembly.EditAssembly());
        asmDoc.EditRebuild3();

        return new
        {
            feature = Try(() => indentFeature.Name),
            clearanceM,
            massAfter = Normalize(Try(() => partDoc.Extension.CreateMassProperty()?.Mass)),
            boundingBoxAfter = Normalize(Try(() => ((IPartDoc)partDoc).GetPartBox(true))),
        };
    }

    private static object CutActuatorCavityByInsertPart(
        ISldWorks app,
        string assemblyPath,
        string bracketPartPath,
        string toolPartPath,
        Component2 bracketInAsm,
        Component2 toolInAsm,
        double clearanceM,
        bool save)
    {
        double[] toolMatrix = ReadComponentTransformMatrix(toolInAsm);
        if (bracketInAsm is not null)
        {
            double[] bracketMatrix = NormalizeTransformMatrix(ReadComponentTransformMatrix(bracketInAsm));
            if (!IsNearIdentityTransform(bracketMatrix))
            {
                toolMatrix = MultiplyTransformMatrix(
                    InvertTransformMatrix(bracketMatrix),
                    NormalizeTransformMatrix(toolMatrix));
            }
        }
        else
        {
            toolMatrix = NormalizeTransformMatrix(toolMatrix);
        }

        string resolvedToolPartPath = Try(() => toolInAsm.GetPathName()) as string ?? toolPartPath;
        string? toolComponentName = Try(() => toolInAsm.Name2) as string;

        MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
        MathTransform? insertTransform = mathUtil is null
            ? null
            : Try(() => mathUtil.CreateTransform(toolMatrix)) as MathTransform;

        Body2? copiedToolBody = CopyToolBodyForBracketCut(app, toolInAsm, resolvedToolPartPath);

        CloseDocumentIfOpen(app, assemblyPath);
        CloseDocumentIfOpen(app, bracketPartPath);
        CloseDocumentIfOpen(app, resolvedToolPartPath);

        ModelDoc2 bracketDoc = OpenDocument(app, bracketPartPath);
        if (bracketDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("cut_actuator_cavity requires a bracket part document.");
        }

        Body2? targetBody = GetPrimarySolidBody(bracketDoc)
            ?? throw new InvalidOperationException("Bracket part has no solid body to cut.");

        FeatureManager featMgr = bracketDoc.FeatureManager;
        dynamic dynFeatMgr = featMgr;

        copiedToolBody ??= CopyPrimarySolidBody(app, resolvedToolPartPath);
        if (copiedToolBody is null)
        {
            throw new InvalidOperationException("Failed to copy actuator solid body from assembly or vendor part.");
        }

        Body2? toolBody = ImportBodyIntoPart(
                bracketDoc,
                copiedToolBody,
                targetBody,
                resolvedToolPartPath,
                insertTransform)
            ?? throw new InvalidOperationException(
                $"Failed to import copied actuator body into bracket part. toolPart={resolvedToolPartPath}");

        const int subtractOperation = 1;
        Feature? combineFeature = Try(() => dynFeatMgr.InsertCombineFeature(
            subtractOperation,
            2,
            new object[] { targetBody, toolBody })) as Feature;

        if (combineFeature is null)
        {
            throw new InvalidOperationException("InsertCombineFeature subtract returned null after importing tool body.");
        }

        TryVoid(() => combineFeature.Name = "Cut-Actuator-Cavity1");
        bracketDoc.EditRebuild3();

        bool partSaved = false;
        int partErrors = 0;
        int partWarnings = 0;
        if (save)
        {
            partSaved = bracketDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref partErrors, ref partWarnings);
            if (!partSaved || partErrors != 0)
            {
                throw new InvalidOperationException($"Bracket save failed. errors={partErrors}, warnings={partWarnings}");
            }
        }

        ModelDoc2 reopenedAsmDoc = OpenDocument(app, assemblyPath);
        if (save)
        {
            reopenedAsmDoc.EditRebuild3();
            int asmErrors = 0;
            int asmWarnings = 0;
            bool asmSaved = reopenedAsmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
            if (!asmSaved || asmErrors != 0)
            {
                throw new InvalidOperationException($"Assembly save failed. errors={asmErrors}, warnings={asmWarnings}");
            }
        }

        return new
        {
            mode = "copy_body_subtract",
            document = DescribeDocument(bracketDoc),
            assembly = DescribeDocument(reopenedAsmDoc),
            bracketPartPath,
            toolPartPath = resolvedToolPartPath,
            toolComponent = toolComponentName,
            combineFeature = Try(() => combineFeature.Name),
            clearanceM,
            massAfter = Normalize(Try(() => bracketDoc.Extension.CreateMassProperty()?.Mass)),
            boundingBoxAfter = Normalize(Try(() => ((IPartDoc)bracketDoc).GetPartBox(true))),
            saved = partSaved,
            partErrors,
            partWarnings,
        };
    }

    private static Body2? CopyToolBodyForBracketCut(
        ISldWorks app,
        Component2 toolInAsm,
        string toolPartPath)
    {
        object? compBodiesObj = Try(() => toolInAsm.GetBodies2((int)swBodyType_e.swSolidBody));
        if (compBodiesObj is object[] compBodies)
        {
            foreach (object entry in compBodies)
            {
                if (entry is not Body2 sourceBody)
                {
                    continue;
                }

                Body2? copied = Try(() => sourceBody.Copy()) as Body2;
                if (copied is not null)
                {
                    return copied;
                }
            }
        }

        return CopyPrimarySolidBody(app, toolPartPath);
    }

    private static Body2? CopyPrimarySolidBody(ISldWorks app, string toolPartPath)
    {
        ModelDoc2 vendorDoc = OpenDocument(app, toolPartPath);
        Body2? sourceBody = GetPrimarySolidBody(vendorDoc);
        if (sourceBody is null)
        {
            return null;
        }

        return Try(() => sourceBody.Copy()) as Body2;
    }

    private static Body2? ImportBodyIntoPart(
        ModelDoc2 partDoc,
        Body2 copiedBody,
        Body2 targetBody,
        string toolPartPath,
        MathTransform? insertTransform)
    {
        dynamic part = (IPartDoc)partDoc;
        dynamic model = partDoc;
        dynamic featMgr = partDoc.FeatureManager;
        object[] bodyArray = [copiedBody];

        bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, insertTransform, 0)) as bool? == true
            || Try(() => (bool)part.InsertBodies(bodyArray, insertTransform)) as bool? == true;
        if (!inserted)
        {
            inserted = Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
        }

        if (!inserted)
        {
            Feature? imported = Try(() => (Feature)model.CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
            if (imported is not null)
            {
                partDoc.EditRebuild3();
                Body2? importedBody = GetInsertedToolSolidBody(partDoc, targetBody);
                if (importedBody is not null && insertTransform is not null)
                {
                    TryVoid(() => importedBody.ApplyTransform(insertTransform));
                    partDoc.EditRebuild3();
                }

                return importedBody;
            }
        }
        else
        {
            partDoc.EditRebuild3();
            Body2? importedBody = GetInsertedToolSolidBody(partDoc, targetBody);
            if (importedBody is not null)
            {
                return importedBody;
            }
        }

        string fullToolPath = Path.GetFullPath(toolPartPath);
        int insertErrors = 0;
        Feature? insertedPart = Try(() => (Feature)featMgr.InsertPart(fullToolPath, 1, ref insertErrors)) as Feature;
        if (insertedPart is null)
        {
            insertErrors = 0;
            insertedPart = Try(() => (Feature)featMgr.InsertPart(fullToolPath, 0, ref insertErrors)) as Feature;
        }

        if (insertedPart is not null)
        {
            partDoc.EditRebuild3();
            Body2? insertedBody = GetInsertedToolSolidBody(partDoc, targetBody);
            if (insertedBody is not null && insertTransform is not null)
            {
                TryVoid(() => insertedBody.ApplyTransform(insertTransform));
                partDoc.EditRebuild3();
            }

            return insertedBody;
        }

        return null;
    }

    private static void CloseDocumentIfOpen(ISldWorks app, string path)
    {
        ModelDoc2? doc = FindOpenDocument(app, Path.GetFullPath(path));
        if (doc is null)
        {
            return;
        }

        string? title = Try(() => doc.GetTitle()) as string;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        TryVoid(() => app.CloseDoc(title));
    }

    private static Feature? InsertToolPartFeature(dynamic featMgr, string toolPartPath)
    {
        int errors = 0;
        Feature? inserted = Try(() => featMgr.InsertPart(toolPartPath, 0, ref errors)) as Feature;
        if (inserted is null || errors != 0)
        {
            throw new InvalidOperationException($"InsertPart failed for tool part. errors={errors}");
        }

        return inserted;
    }

    private static Body2? GetPrimarySolidBody(ModelDoc2 partDoc)
    {
        object? bodiesObj = Try(() => ((IPartDoc)partDoc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is object[] bodies)
        {
            foreach (object entry in bodies)
            {
                if (entry is Body2 body)
                {
                    return body;
                }
            }
        }

        return null;
    }

    private static Body2? GetInsertedToolSolidBody(ModelDoc2 partDoc, Body2 targetBody)
    {
        object? bodiesObj = Try(() => ((IPartDoc)partDoc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        foreach (object entry in bodies)
        {
            if (entry is Body2 body && !ReferenceEquals(body, targetBody))
            {
                return body;
            }
        }

        return null;
    }

    private static double[] InvertTransformMatrix(double[] matrix)
    {
        if (matrix.Length != 16)
        {
            throw new InvalidOperationException("Transform matrix must contain 16 numbers.");
        }

        double r00 = matrix[0];
        double r01 = matrix[1];
        double r02 = matrix[2];
        double r10 = matrix[3];
        double r11 = matrix[4];
        double r12 = matrix[5];
        double r20 = matrix[6];
        double r21 = matrix[7];
        double r22 = matrix[8];
        double tx = matrix[9];
        double ty = matrix[10];
        double tz = matrix[11];

        return
        [
            r00, r10, r20, 0,
            r01, r11, r21, 0,
            r02, r12, r22, 0,
            -(r00 * tx + r10 * ty + r20 * tz),
            -(r01 * tx + r11 * ty + r21 * tz),
            -(r02 * tx + r12 * ty + r22 * tz),
            1,
            0, 0, 0,
        ];
    }

    private static object PlaceShoulderRollMotors(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout_revA-1";
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
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout_revA";
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

    private static object MateTorsoComputeShelf(JsonElement? args)
    {
        string assemblyPath = RequiredStringArg(args, "path");
        string shelfComponent = StringArg(args, "shelf_component") ?? "marengo_torso_compute_shelf_upper_revA";
        string layoutComponent = StringArg(args, "layout_component") ?? "marengo_torso_layout_revA";
        string leftActuator = StringArg(args, "left_actuator") ?? "actuator_rs03_left_shoulder_roll";
        string rightActuator = StringArg(args, "right_actuator") ?? "actuator_rs03_right_shoulder_roll";
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_torso_compute_shelf requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2? layout = ResolveTorsoLayoutComponent(assembly)
            ?? FindComponent(assembly, null, layoutComponent)
            ?? throw new InvalidOperationException($"Layout component not found: {layoutComponent}");
        Component2? shelf = FindComponent(assembly, null, shelfComponent)
            ?? throw new InvalidOperationException($"Shelf component not found: {shelfComponent}");
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
        double centerX = (innerAsm[0] + innerAsm[3]) / 2.0;
        double centerZ = (innerAsm[2] + innerAsm[5]) / 2.0;

        string layoutPartPath = Try(() => layout.GetPathName()) as string
            ?? throw new InvalidOperationException("Layout component path unavailable.");
        layoutPartPath = Path.GetFullPath(layoutPartPath);
        ModelDoc2 layoutDoc = OpenDocument(app, layoutPartPath);
        double[]? innerPart = GetFeatureBoundingBoxInPart(layoutDoc, "torso_inner_clear");
        double shelfSeatLayoutY = shelfSeatAssemblyY;
        if (innerPart is not null && innerPart.Length >= 6)
        {
            shelfSeatLayoutY = innerPart[1] + (shelfSeatAssemblyY - innerAsm[1]);
        }

        object layoutPlaneResult = EnsureLayoutComputeShelfSeat(layoutDoc, shelfSeatLayoutY, save: false);

        asmDoc = OpenDocument(app, assemblyPath);
        assembly = (IAssemblyDoc)asmDoc;
        layout = ResolveTorsoLayoutComponent(assembly)
            ?? FindComponent(assembly, null, layoutComponent);
        shelf = FindComponent(assembly, null, shelfComponent)
            ?? throw new InvalidOperationException($"Shelf component not found after layout update: {shelfComponent}");

        object placementResult = ResetComputeShelfComponent(
            asmDoc,
            assembly,
            shelf);
        object mateResult = MateComputeShelfToLayout(asmDoc, assembly, shelf, layout!);

        bool layoutSaved = false;
        int layoutErrors = 0;
        int layoutWarnings = 0;
        if (save)
        {
            layoutSaved = layoutDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref layoutErrors, ref layoutWarnings);
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
                    $"Assembly save failed after compute shelf mate. errors={asmErrors}, warnings={asmWarnings}");
            }
        }

        return new
        {
            document = DescribeDocument(asmDoc),
            shelfComponent = Try(() => shelf.Name2),
            layoutComponent = Try(() => layout?.Name2),
            shelfSeatAssemblyYM = shelfSeatAssemblyY,
            shelfSeatLayoutYM = shelfSeatLayoutY,
            layoutPlaneResult,
            placementResult,
            mateResult,
            layoutSaved,
            layoutErrors,
            layoutWarnings,
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
        };
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

    private static Component2? ResolveTorsoLayoutComponent(IAssemblyDoc assembly)
    {
        Component2? frame = FindComponent(assembly, null, "marengo_torso_frame_asm_revA");
        if (frame is not null)
        {
            Component2? nested = FindComponent(assembly, frame, "marengo_torso_layout_revA");
            if (nested is not null)
            {
                return nested;
            }
        }

        return FindComponent(assembly, null, "marengo_torso_layout_revA");
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
            catch
            {
                throw new InvalidOperationException(
                    "SolidWorks is running but COM attach failed. Close duplicate SolidWorks instances "
                    + "(Task Manager → end extra SLDWORKS.exe with no window) and retry.");
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
        string fullPath = Path.GetFullPath(path);
        ModelDoc2? existing = FindOpenDocument(app, fullPath);
        if (existing is not null)
        {
            int activateErrors = 0;
            string? title = Try(() => existing.GetTitle()) as string;
            if (!string.IsNullOrWhiteSpace(title))
            {
                Try(() => app.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors));
            }

            return existing;
        }

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

        string? openedTitle = Try(() => doc.GetTitle()) as string;
        if (!string.IsNullOrWhiteSpace(openedTitle))
        {
            int activateErrors = 0;
            Try(() => app.ActivateDoc3(openedTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors));
        }

        return doc;
    }

    private static ModelDoc2? FindOpenDocument(ISldWorks app, string fullPath)
    {
        ModelDoc2? byPath = Try(() => app.GetOpenDocumentByName(fullPath)) as ModelDoc2;
        if (byPath is not null)
        {
            return byPath;
        }

        string fileName = Path.GetFileName(fullPath);
        ModelDoc2? byName = Try(() => app.GetOpenDocumentByName(fileName)) as ModelDoc2;
        if (byName is null)
        {
            return null;
        }

        string? openPath = Try(() => byName.GetPathName()) as string;
        if (string.IsNullOrWhiteSpace(openPath))
        {
            return null;
        }

        return string.Equals(Path.GetFullPath(openPath), fullPath, StringComparison.OrdinalIgnoreCase)
            ? byName
            : null;
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
