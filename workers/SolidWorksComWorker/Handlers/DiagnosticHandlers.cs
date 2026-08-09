using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object AssemblyDiagnostics(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("assembly_diagnostics requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        int componentCount = 0;
        int fixedCount = 0;
        int floatCount = 0;
        int lightweightCount = 0;
        int suppressedCount = 0;
        int hiddenCount = 0;
        var brokenRefs = new List<string>();

        void Walk(Component2? parent)
        {
            object[]? roots = parent is null
                ? Try(() => assembly.GetComponents(false)) as object[]
                : Try(() => parent.GetChildren()) as object[];

            if (roots is null)
            {
                return;
            }

            foreach (object item in roots)
            {
                if (item is not Component2 component)
                {
                    continue;
                }

                componentCount++;
                if (Try(() => component.IsFixed()) as bool? == true)
                {
                    fixedCount++;
                }
                else
                {
                    floatCount++;
                }

                if (ComponentIsLightweight(component))
                {
                    lightweightCount++;
                }

                if (Try(() => component.IsSuppressed()) as bool? == true)
                {
                    suppressedCount++;
                }

                if (Try(() => component.Visible) as int? == 0)
                {
                    hiddenCount++;
                }

                string? compPath = Try(() => component.GetPathName()) as string;
                if (string.IsNullOrWhiteSpace(compPath) || !File.Exists(compPath))
                {
                    brokenRefs.Add(Try(() => component.Name2) as string ?? "?");
                }

                Walk(component);
            }
        }

        Walk(null);

        return new
        {
            document = DescribeDocument(doc),
            componentCount,
            fixedCount,
            floatCount,
            lightweightCount,
            suppressedCount,
            hiddenCount,
            brokenReferenceComponents = brokenRefs,
            mateCount = CountAssemblyMates(doc),
            note = "Review the document's component and mate state before making changes.",
        };
    }

    private static object ComponentMassProperties(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("component_mass_properties requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (ComponentIsLightweight(component))
        {
            throw new InvalidOperationException(
                $"Component {componentName} is lightweight. Run resolve_lightweight first.");
        }

        if (Try(() => component.IsSuppressed()) as bool? == true)
        {
            throw new InvalidOperationException($"Component {componentName} is suppressed.");
        }

        ModelDoc2? compDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (compDoc is null)
        {
            throw new InvalidOperationException($"Model unavailable for component: {componentName}");
        }

        MassProperty? massProperty = Try(() => compDoc.Extension.CreateMassProperty()) as MassProperty;
        if (massProperty is null)
        {
            throw new InvalidOperationException("CreateMassProperty failed.");
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            path = Try(() => component.GetPathName()),
            mass = Normalize(Try(() => massProperty.Mass)),
            centerOfMass = Normalize(Try(() => massProperty.CenterOfMass)),
            momentsOfInertia = Normalize(Try(() => massProperty.GetMomentOfInertia(0))),
            urdfNote = "Prefer export_urdf_package for inertia expressed about urdf_link_frame.",
        };
    }

    private static object ResolveLightweight(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("resolve_lightweight requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        bool ok = Try(() => assembly.ResolveAllLightWeightComponents(false)) as bool? ?? false;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            resolved = ok,
            note = "Resolution applies to the SolidWorks session; save assembly to persist load state if needed.",
        };
    }

    private static bool ComponentIsLightweight(Component2 component)
    {
        int? state = Try(() => component.GetSuppression()) as int?;
        if (state is null)
        {
            return false;
        }

        return state == (int)swComponentSuppressionState_e.swComponentLightweight
            || state == (int)swComponentSuppressionState_e.swComponentFullyLightweight;
    }

    private static object DiagnoseCom(JsonElement? args)
    {
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: false);
        var context = new Dictionary<string, object?>();

        bool swProcessRunning = IsSolidWorksProcessRunning();
        bool mutexHeld = false;
        try
        {
            using Mutex probe = new(false, WorkerConstants.ComMutexName);
            mutexHeld = !probe.WaitOne(0);
        }
        catch
        {
            mutexHeld = true;
        }

        string? interopVersion = typeof(ISldWorks).Assembly.GetName().Version?.ToString();
        string workerVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        object? attachResult = null;
        WorkerError? attachWarning = null;
        if (swProcessRunning || startIfMissing)
        {
            (ISldWorks? app, WorkerError? warning) = TryAttachForDiagnose(startIfMissing);
            attachWarning = warning;
            if (app is not null)
            {
                attachResult = new
                {
                    revision = Try(() => app.RevisionNumber()),
                    activeDocument = DescribeDocument(Try(() => app.ActiveDoc)),
                };
            }
        }

        return new
        {
            platform = System.Environment.OSVersion.Platform.ToString(),
            swProcessRunning,
            comMutexHeld = mutexHeld,
            interopVersion,
            workerVersion,
            allowedRoots = PathGuard.AllowedRoots(),
            attach = attachResult,
            attachWarning,
        };
    }

    private static (ISldWorks? App, WorkerError? Warning) TryAttachForDiagnose(bool startIfMissing)
    {
        try
        {
            return (AttachSolidWorks(startIfMissing), null);
        }
        catch (Exception ex)
        {
            return (null, SwErrorDecoder.FromException(ex, "ISldWorks", new Dictionary<string, object?>()));
        }
    }

    private static object DiagnoseDocument(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2 doc = inputPath is not null
            ? OpenDocument(app, PathGuard.AssertAllowedPath(inputPath))
            : Try(() => app.ActiveDoc) as ModelDoc2
                ?? throw WorkerException.Validation(
                    "NO_ACTIVE_DOCUMENT",
                    "No active document and no path provided.",
                    new Dictionary<string, object?>());

        return new
        {
            document = DescribeDocument(doc),
            saved = Try(() => doc.GetSaveFlag()) as int? == 0,
            rebuildNeeded = Try(() => doc.GetUpdateStamp()) is not null,
            units = DescribeUnits(app, doc),
            customPropertyCount = ListCustomProperties(doc).Count,
            featureCount = CountTopLevelFeatures(doc),
        };
    }

    private static object DiagnoseSelection(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2 doc = inputPath is not null
            ? OpenDocument(app, PathGuard.AssertAllowedPath(inputPath))
            : Try(() => app.ActiveDoc) as ModelDoc2
                ?? throw WorkerException.Validation(
                    "NO_ACTIVE_DOCUMENT",
                    "No active document and no path provided.",
                    new Dictionary<string, object?>());

        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int count = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        var entities = new List<object>();
        for (int i = 1; i <= count; i++)
        {
            object? entity = Try(() => selectionMgr?.GetSelectedObject6(i, -1));
            int mark = Try(() => selectionMgr?.GetSelectedObjectMark(i)) as int? ?? 0;
            string? typeName = entity?.GetType().Name;
            entities.Add(new { index = i, mark, typeName });
        }

        return new
        {
            document = DescribeDocument(doc),
            selectedCount = count,
            entities,
        };
    }

    private static object ExplainError(JsonElement? args)
    {
        string? code = StringArg(args, "code");
        string? hresult = StringArg(args, "hresult");
        int swErrorCode = (int)DoubleArg(args, "sw_error_code", -1);
        string? message = StringArg(args, "message") ?? "Explained error";

        if (swErrorCode >= 0)
        {
            (string name, string[] remediation) = SwErrorDecoder.DecodeMateError(swErrorCode);
            return new WorkerError(
                code ?? "SW_MATE_ERROR",
                message,
                "solidworks",
                SwErrorCode: swErrorCode,
                SwErrorName: name,
                Remediation: remediation,
                DocLink: $"solidworks://errors/sw/{name}/{swErrorCode}");
        }

        if (!string.IsNullOrWhiteSpace(hresult))
        {
            int parsed = hresult.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(hresult, 16)
                : int.TryParse(hresult, out int dec) ? dec : 0;
            return new WorkerError(
                code ?? "HRESULT_LOOKUP",
                message,
                "com",
                Hresult: SwErrorDecoder.DecodeHresult(parsed),
                Remediation: SwErrorDecoder.DecodeHresult(parsed) is not null
                    ? ["See docs/errors/ for HRESULT remediation guides."]
                    : null);
        }

        return new WorkerError(
            code ?? "UNKNOWN_ERROR",
            message,
            "worker",
            Remediation: ["Provide code, hresult, or sw_error_code for a specific explanation."]);
    }

    private static int CountTopLevelFeatures(ModelDoc2 doc)
    {
        Feature? feature = Try(() => doc.FirstFeature()) as Feature;
        int count = 0;
        while (feature is not null)
        {
            count++;
            feature = Try(() => feature.GetNextFeature()) as Feature;
        }

        return count;
    }
}
