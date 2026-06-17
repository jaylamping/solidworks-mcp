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

            if (roots is null) return;

            foreach (object item in roots)
            {
                if (item is not Component2 component) continue;
                componentCount++;
                if (Try(() => component.IsFixed()) as bool? == true) fixedCount++;
                else floatCount++;
                if (Try(() => component.IsLightweight()) as bool? == true) lightweightCount++;
                if (Try(() => component.IsSuppressed()) as bool? == true) suppressedCount++;
                if (Try(() => component.Visible) as int? == 0) hiddenCount++;
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
            note = "Compare with marengo_design_review for manifest-level checks.",
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

        if (Try(() => component.IsLightweight()) as bool? == true)
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
            urdfNote = "Verify inertia frame alignment before Brawner export.",
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
        bool ok = Try(() => assembly.ResolveAllLightweightComponents()) as bool? ?? false;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            resolved = ok,
            note = "Resolution applies to the SolidWorks session; save assembly to persist load state if needed.",
        };
    }
}
