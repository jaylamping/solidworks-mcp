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
        string path = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
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
        string outputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_path"));
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
        bool ok;
        if (IsPreviewExport(outputPath))
        {
            ok = WithReferenceGeometryHidden(app, doc, () =>
            {
                PreparePreview(doc);
                return doc.Extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            });
        }
        else
        {
            ok = doc.Extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
        }

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

    private static void PreparePreview(ModelDoc2 doc)
    {
        TryVoid(() => doc.ClearSelection2(true));
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

    private static readonly swUserPreferenceToggle_e[] PreviewHiddenToggles =
    [
        swUserPreferenceToggle_e.swDisplayPlanes,
        swUserPreferenceToggle_e.swDisplayAxes,
        swUserPreferenceToggle_e.swDisplayTemporaryAxes,
        swUserPreferenceToggle_e.swDisplayCoordSystems,
        swUserPreferenceToggle_e.swDisplayOrigins,
        swUserPreferenceToggle_e.swDisplaySketches,
        swUserPreferenceToggle_e.swDisplaySketchPlanes,
    ];

    // Hide planes/axes/origins/sketches for an image capture, then put the user's
    // application- and document-level display settings back exactly as they were.
    private static T WithReferenceGeometryHidden<T>(ISldWorks app, ModelDoc2 doc, Func<T> capture)
    {
        // Reference-geometry visibility is stored both in the app and per document.
        const int noOption = (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified;
        var saved = PreviewHiddenToggles.ToDictionary(t => t, t => (Try(() => app.GetUserPreferenceToggle((int)t)) as bool?) ?? true);
        var savedDoc = PreviewHiddenToggles.ToDictionary(t => t, t => (Try(() => doc.Extension.GetUserPreferenceToggle((int)t, noOption)) as bool?) ?? true);
        try
        {
            foreach (swUserPreferenceToggle_e t in PreviewHiddenToggles)
            {
                TryVoid(() => app.SetUserPreferenceToggle((int)t, false));
                TryVoid(() => doc.Extension.SetUserPreferenceToggle((int)t, noOption, false));
            }

            return capture();
        }
        finally
        {
            foreach ((swUserPreferenceToggle_e t, bool value) in saved)
            {
                TryVoid(() => app.SetUserPreferenceToggle((int)t, value));
            }

            foreach ((swUserPreferenceToggle_e t, bool value) in savedDoc)
            {
                TryVoid(() => doc.Extension.SetUserPreferenceToggle((int)t, noOption, value));
            }

            TryVoid(() => doc.GraphicsRedraw2());
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

    private sealed class MateHealthEntry
    {
        public string? Name { get; init; }
        public string? Type { get; init; }
        public int? ErrorCode { get; init; }
        public bool? Suppressed { get; init; }
        public bool? IsAdvancedMate { get; init; }
        public double? MinAngleDeg { get; init; }
        public double? MaxAngleDeg { get; init; }
        public double? AngleDeg { get; init; }
        public bool? FlipDimension { get; init; }

        public object ToListItem() => new
        {
            name = Name,
            type = Type,
            errorCode = ErrorCode,
            suppressed = Suppressed,
            isAdvancedMate = IsAdvancedMate,
            minAngleDeg = MinAngleDeg,
            maxAngleDeg = MaxAngleDeg,
            angleDeg = AngleDeg,
            flipDimension = FlipDimension,
        };

        public bool IsFailure
        {
            get
            {
                if (Suppressed == true)
                {
                    return false;
                }

                // Null errorCode means health could not be read — treat as unsafe.
                return ErrorCode is null or not 0;
            }
        }
    }

    private static List<MateHealthEntry> CollectMateHealthEntries(ModelDoc2 doc)
    {
        var mates = new List<MateHealthEntry>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is null)
        {
            return mates;
        }

        object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
        int guard = 0;
        while (subFeature is not null && guard++ < 200)
        {
            Feature current = (Feature)subFeature;
            string? name = Try(() => current.Name) as string;
            string? typeName = Try(() => current.GetTypeName2()) as string;
            int? errorCode = null;
            try
            {
                bool isWarning = false;
                errorCode = current.GetErrorCode2(out isWarning);
            }
            catch
            {
                errorCode = Try(() => current.GetErrorCode()) as int?;
            }

            bool? suppressed = Try(() => current.IsSuppressed()) as bool?;
            bool? isAdvancedMate = null;
            double? minAngleDeg = null;
            double? maxAngleDeg = null;
            double? angleDeg = null;
            bool? flipDimension = null;
            if (Try(() => current.GetDefinition()) is IAngleMateFeatureData angleMate)
            {
                isAdvancedMate = Try(() => angleMate.IsAdvancedMate) as bool?;
                double? minRad = Try(() => angleMate.MinimumAngle) as double?;
                double? maxRad = Try(() => angleMate.MaximumAngle) as double?;
                double? angRad = Try(() => angleMate.Angle) as double?;
                flipDimension = Try(() => angleMate.FlipDimension) as bool?;
                if (minRad is double min) minAngleDeg = min * 180.0 / Math.PI;
                if (maxRad is double max) maxAngleDeg = max * 180.0 / Math.PI;
                if (angRad is double ang) angleDeg = ang * 180.0 / Math.PI;
            }

            mates.Add(new MateHealthEntry
            {
                Name = name,
                Type = typeName,
                ErrorCode = errorCode,
                Suppressed = suppressed,
                IsAdvancedMate = isAdvancedMate,
                MinAngleDeg = minAngleDeg,
                MaxAngleDeg = maxAngleDeg,
                AngleDeg = angleDeg,
                FlipDimension = flipDimension,
            });
            subFeature = Try(() => current.GetNextSubFeature());
        }

        return mates;
    }

    private static List<object> MateFailuresFrom(IEnumerable<MateHealthEntry> mates) =>
        mates.Where(m => m.IsFailure)
            .Select(m => (object)new
            {
                name = m.Name,
                type = m.Type,
                errorCode = m.ErrorCode,
                suppressed = m.Suppressed,
            })
            .ToList();

    private static bool ForceRebuildDocument(ModelDoc2 doc, out bool forceOk)
    {
        forceOk = Try(() => doc.ForceRebuild3(false)) as bool? ?? false;
        doc.EditRebuild3();
        return forceOk;
    }

    private static Dictionary<string, double[]> CaptureComponentTransforms(IAssemblyDoc assembly)
    {
        var transforms = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        object[]? roots = Try(() => assembly.GetComponents(true)) as object[];
        if (roots is null)
        {
            return transforms;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            string? name = Try(() => component.Name2) as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                transforms[name] = ReadComponentTransformMatrix(component);
            }
            catch
            {
                // Skip components whose transforms cannot be read.
            }
        }

        return transforms;
    }

    private static List<object> DiffComponentTransforms(
        Dictionary<string, double[]> before,
        Dictionary<string, double[]> after,
        double tolerance)
    {
        var jumped = new List<object>();
        foreach ((string name, double[] beforeMatrix) in before)
        {
            if (!after.TryGetValue(name, out double[]? afterMatrix))
            {
                jumped.Add(new { name, reason = "missing_after" });
                continue;
            }

            bool close = beforeMatrix.Length == afterMatrix.Length;
            if (close)
            {
                for (int i = 0; i < beforeMatrix.Length; i++)
                {
                    if (Math.Abs(beforeMatrix[i] - afterMatrix[i]) > tolerance)
                    {
                        close = false;
                        break;
                    }
                }
            }

            if (!close)
            {
                jumped.Add(new { name, reason = "transform_delta", before = beforeMatrix, after = afterMatrix });
            }
        }

        foreach (string name in after.Keys)
        {
            if (!before.ContainsKey(name))
            {
                jumped.Add(new { name, reason = "missing_before" });
            }
        }

        return jumped;
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

        List<MateHealthEntry> entries = CollectMateHealthEntries(doc);
        return new
        {
            document = DescribeDocument(doc),
            mates = entries.Select(e => e.ToListItem()).ToList(),
            mateCount = CountAssemblyMates(doc),
            mateFailures = MateFailuresFrom(entries),
            mateHealthy = !entries.Any(e => e.IsFailure),
        };
    }

    private static object ListMateEntities(JsonElement? args)
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
            throw new InvalidOperationException("list_mate_entities requires an assembly document.");
        }

        var mates = new List<object>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is not null)
        {
            object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
            int guard = 0;
            while (subFeature is not null && guard++ < 200)
            {
                Feature current = (Feature)subFeature;
                string? name = Try(() => current.Name) as string;
                string? typeName = Try(() => current.GetTypeName2()) as string;
                var entities = new List<object>();
                Mate2? mate = Try(() => current.GetSpecificFeature2()) as Mate2;
                int entityCount = mate is null
                    ? 0
                    : Try(() => mate.GetMateEntityCount()) as int? ?? 0;
                for (int i = 0; i < entityCount; i++)
                {
                    MateEntity2? entity = Try(() => mate!.MateEntity(i)) as MateEntity2;
                    if (entity is null)
                    {
                        continue;
                    }

                    Component2? owner = Try(() => entity.ReferenceComponent) as Component2;
                    entities.Add(new
                    {
                        index = i,
                        entityType = Try(() => entity.ReferenceType2),
                        component = Try(() => owner?.Name2),
                        componentPath = Try(() => owner?.GetPathName()),
                        entityParams = Normalize(Try(() => entity.EntityParams)),
                    });
                }

                mates.Add(new
                {
                    name,
                    type = typeName,
                    entityCount,
                    entities,
                });
                subFeature = Try(() => current.GetNextSubFeature());
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            mates,
            mateCount = mates.Count,
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