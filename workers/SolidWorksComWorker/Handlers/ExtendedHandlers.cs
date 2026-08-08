using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object RebuildDocument(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        bool force = BoolArg(args, "force", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        bool forceOk = !force || (Try(() => doc.ForceRebuild3(false)) as bool? ?? false);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            forceRebuild = force,
            forceOk,
        };
    }

    private static object EnsureOffsetPlane(JsonElement? args)
    {
        string partPath = RequiredStringArg(args, "part_path");
        string planeName = RequiredStringArg(args, "plane_name");
        double offsetM = DoubleArg(args, "offset_m");
        string referencePlane = StringArg(args, "reference_plane") ?? "Top Plane";
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, partPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("ensure_offset_plane requires a part document.");
        }

        if (replaceExisting)
        {
            DeleteFeatureByName(doc, planeName);
        }
        else if (FindFeatureByName(doc, planeName) is not null)
        {
            return new
            {
                document = DescribeDocument(doc),
                planeName,
                skipped = true,
                reason = "already_exists",
            };
        }

        Feature? plane = InsertOffsetPlaneFromReference(doc, referencePlane, offsetM, planeName)
            ?? throw new InvalidOperationException($"Failed to create offset plane: {planeName}");

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
            offsetM,
            referencePlane,
            saved,
            errors,
            warnings,
        };
    }

    private static object ResetComponentTransform(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        bool unfix = BoolArg(args, "unfix", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("reset_component_transform requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (unfix && Try(() => component.IsFixed()) as bool? == true)
        {
            component.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        ApplyComponentTransformMatrix(component, IdentityTransformMatrix());
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            reset = true,
            boundingBoxM = Normalize(Try(() => component.GetBox(false, false))),
        };
    }

    private static object GetPlanarFaceIndex(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string featureName = RequiredStringArg(args, "feature_name");
        string mode = StringArg(args, "mode") ?? "top";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_planar_face_index requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        int faceIndex = mode.Equals("largest", StringComparison.OrdinalIgnoreCase)
            ? GetLargestPlanarFaceIndex(component, featureName)
            : GetTopPlanarFaceIndex(component, featureName);

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            feature = featureName,
            mode,
            faceIndex,
        };
    }

    private static object GetPersistReference(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string refName = RequiredStringArg(args, "ref");
        int faceIndex = (int)DoubleArg(args, "face_index", 0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_persist_reference requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        doc.ClearSelection2(true);
        if (!SelectComponentMateEntity(doc, component, refName, faceIndex, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {refName} on {componentName}");
        }

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        object? entity = Try(() => selectionMgr?.GetSelectedObject6(1, -1))
            ?? throw new InvalidOperationException("Selection unavailable for persist reference.");

        byte[]? persistRef = Try(() => doc.Extension.GetPersistReference3(entity)) as byte[];
        if (persistRef is null || persistRef.Length == 0)
        {
            throw new InvalidOperationException("GetPersistReference3 returned no data.");
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            refName,
            faceIndex,
            persistReference = Convert.ToBase64String(persistRef),
        };
    }

    private static object SelectByPersistReference(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string persistReference = RequiredStringArg(args, "persist_reference");
        int mark = (int)DoubleArg(args, "mark", 1);
        bool append = BoolArg(args, "append", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        byte[] bytes = Convert.FromBase64String(persistReference);
        int errorCode = 0;
        object? entity = Try(() =>
        {
            int code = 0;
            object? resolved = doc.Extension.GetObjectByPersistReference3(bytes, out code);
            errorCode = code;
            return resolved;
        }) ?? throw new InvalidOperationException($"GetObjectByPersistReference3 failed: errorCode={errorCode}");

        bool selected = false;
        if (entity is Entity ent)
        {
            selected = Try(() => ent.Select4(append, null)) as bool? ?? false;
        }

        return new
        {
            document = DescribeDocument(doc),
            selected,
            mark,
        };
    }

    private static object InsertCoordSys(JsonElement? args)
    {
        string partPath = RequiredStringArg(args, "part_path");
        string name = RequiredStringArg(args, "name");
        double originX = DoubleArg(args, "origin_x_m");
        double originY = DoubleArg(args, "origin_y_m");
        double originZ = DoubleArg(args, "origin_z_m");
        string xAxisRef = StringArg(args, "x_axis_ref") ?? "Front Plane";
        string yAxisRef = StringArg(args, "y_axis_ref") ?? "Right Plane";
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, partPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("insert_coord_sys requires a part document.");
        }

        if (replaceExisting)
        {
            DeleteFeatureByName(doc, name);
        }
        else if (FindFeatureByName(doc, name) is not null)
        {
            return new { document = DescribeDocument(doc), name, skipped = true, reason = "already_exists" };
        }

        Feature? coordFeature = CreateCoordSysAtPoint(doc, name, originX, originY, originZ, xAxisRef, yAxisRef)
            ?? throw new InvalidOperationException($"Failed to create coordinate system: {name}");

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
            name,
            originM = new[] { originX, originY, originZ },
            saved,
            errors,
            warnings,
            feature = Try(() => coordFeature.Name),
        };
    }

    private static Feature? CreateCoordSysAtPoint(
        ModelDoc2 doc,
        string name,
        double originX,
        double originY,
        double originZ,
        string xAxisRef,
        string yAxisRef)
    {
        FeatureManager featMgr = doc.FeatureManager;
        doc.ClearSelection2(true);

        bool originSelected = doc.Extension.SelectByID2(
            "", "EXTSKETCHPOINT", originX, originY, originZ, false, 1, null, 0);
        if (!originSelected)
        {
            SketchManager skMgr = doc.SketchManager;
            if (doc.Extension.SelectByID2("Front Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
            {
                skMgr.Insert3DSketch(true);
                TryVoid(() => skMgr.CreatePoint(originX, originY, originZ));
                skMgr.Insert3DSketch(true);
                doc.ClearSelection2(true);
                originSelected = doc.Extension.SelectByID2("", "EXTSKETCHPOINT", originX, originY, originZ, false, 1, null, 0);
            }
        }

        if (!originSelected)
        {
            return null;
        }

        if (!doc.Extension.SelectByID2(xAxisRef, "PLANE", 0, 0, 0, true, 2, null, 0))
        {
            return null;
        }

        if (!doc.Extension.SelectByID2(yAxisRef, "PLANE", 0, 0, 0, true, 4, null, 0))
        {
            return null;
        }

        Feature? coordFeature = Try(() => featMgr.InsertCoordinateSystem(false, false, false)) as Feature;
        doc.ClearSelection2(true);
        if (coordFeature is null)
        {
            return null;
        }

        TryVoid(() => coordFeature.Name = name);
        return coordFeature;
    }

    private static object SelectFaceByRay(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string featureName = RequiredStringArg(args, "feature_name");
        int faceIndex = (int)DoubleArg(args, "face_index", 0);
        int mark = (int)DoubleArg(args, "mark", 1);
        bool append = BoolArg(args, "append", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("select_face_by_ray requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        Feature? feature = componentDoc is null ? null : FindFeatureByName(componentDoc, featureName);
        object? facesObj = feature is null ? null : Try(() => feature.GetFaces());
        if (facesObj is not object[] faces || faceIndex < 0 || faceIndex >= faces.Length || faces[faceIndex] is not Face2 face)
        {
            throw new InvalidOperationException($"Face unavailable: {featureName}[{faceIndex}] on {componentName}");
        }

        doc.ClearSelection2(true);
        bool selected = SelectComponentFeatureFace(doc, component, featureName, faceIndex, append, mark);
        int selectedCount = 0;
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        if (selectionMgr is not null)
        {
            selectedCount = Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            feature = featureName,
            faceIndex,
            selected,
            selectedCount,
            mark,
        };
    }

    private static object GetPartFeatureBox(JsonElement? args)
    {
        string partPath = RequiredStringArg(args, "part_path");
        string featureName = RequiredStringArg(args, "feature_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, partPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("get_part_feature_box requires a part document.");
        }

        double[]? box = GetFeatureBoundingBoxInPart(doc, featureName)
            ?? throw new InvalidOperationException($"Feature box unavailable: {featureName}");

        return new
        {
            document = DescribeDocument(doc),
            feature = featureName,
            boundingBox = Normalize(box),
            center = BoxCenter(box),
        };
    }

    private static object ListInterferences(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("list_interferences requires an assembly document.");
        }

        object? mgrObj = Try(() =>
        {
            object? activeDoc = app.ActiveDoc;
            return app.GetType().InvokeMember(
                "GetInterferenceDetectionManager",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                app,
                null);
        });

        if (mgrObj is not InterferenceDetectionMgr mgr)
        {
            return new
            {
                document = DescribeDocument(doc),
                count = 0,
                interferences = Array.Empty<object>(),
                warning = "InterferenceDetectionMgr unavailable in this SolidWorks interop build.",
            };
        }

        mgr.TreatCoincidenceAsInterference = false;
        mgr.TreatSubAssembliesAsComponents = true;
        mgr.IncludeMultibodyPartInterferences = BoolArg(args, "include_multibody", defaultValue: true);

        object[]? components = Try(() => ((IAssemblyDoc)doc).GetComponents(false)) as object[];
        if (components is null || components.Length == 0)
        {
            return new { document = DescribeDocument(doc), interferences = Array.Empty<object>(), count = 0 };
        }

        TryVoid(() => mgr.GetType().InvokeMember(
            "SetComponents",
            System.Reflection.BindingFlags.InvokeMethod,
            null,
            mgr,
            [components]));
        object[]? interferences = Try(() => mgr.GetInterferences()) as object[];
        var results = new List<object>();
        if (interferences is not null)
        {
            foreach (object entry in interferences)
            {
                if (entry is not Interference interference)
                {
                    continue;
                }

                object[]? interferenceComponents = Try(() => interference.Components) as object[];
                results.Add(new
                {
                    volumeM3 = Try(() => interference.Volume),
                    components = interferenceComponents?.OfType<Component2>()
                        .Select(c => Try(() => c.Name2))
                        .ToArray(),
                });
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            count = results.Count,
            interferences = results,
        };
    }

    private static object DeleteMate(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string mateName = RequiredStringArg(args, "mate_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("delete_mate requires an assembly document.");
        }

        Feature? mateFeature = FindFeatureByName(doc, mateName)
            ?? throw new InvalidOperationException($"Mate feature not found: {mateName}");

        doc.ClearSelection2(true);
        bool selected = Try(() => mateFeature.Select2(false, 0)) as bool? ?? false;
        if (!selected)
        {
            throw new InvalidOperationException($"Failed to select mate: {mateName}");
        }

        bool deleted = Try(() => doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed)) as bool? ?? false;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            mateName,
            deleted,
            mateCount = CountAssemblyMates(doc),
        };
    }

    private static object SetMateSuppression(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string mateName = RequiredStringArg(args, "mate_name");
        bool suppressed = BoolArg(args, "suppressed", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        Feature? mateFeature = FindFeatureByName(doc, mateName)
            ?? throw new InvalidOperationException($"Mate feature not found: {mateName}");

        int state = suppressed
            ? (int)swFeatureSuppressionAction_e.swSuppressFeature
            : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;
        bool ok = Try(() => mateFeature.SetSuppression2(
            state,
            (int)swInConfigurationOpts_e.swAllConfiguration,
            null)) as bool? ?? false;
        doc.EditRebuild3();
        bool? actuallySuppressed = Try(() => mateFeature.IsSuppressed()) as bool?;

        return new
        {
            document = DescribeDocument(doc),
            mateName,
            suppressed,
            actuallySuppressed,
            ok,
        };
    }
}
