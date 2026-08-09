using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static readonly string[] DefaultUrdfRequiredRefs = ["urdf_link_frame", "joint_axis"];

    private static object UrdfReadiness(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        string[] required = StringArrayArg(args, "required_refs") ?? DefaultUrdfRequiredRefs;

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? rootDoc = string.IsNullOrWhiteSpace(inputPath)
            ? app.ActiveDoc as ModelDoc2
            : OpenDocument(app, inputPath);
        if (rootDoc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        var scopes = new List<object>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectUrdfScopes(rootDoc, componentName: null, scopes, visited, required);

        var limitMates = new List<object>();
        if (rootDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            CollectLimitMates(rootDoc, limitMates);
        }

        return new
        {
            document = DescribeDocument(rootDoc),
            requiredRefs = required,
            scopeCount = scopes.Count,
            scopes,
            limitMates,
            limitMateCount = limitMates.Count,
        };
    }

    private static void CollectUrdfScopes(
        ModelDoc2 doc,
        string? componentName,
        List<object> scopes,
        HashSet<string> visited,
        string[] required)
    {
        string? path = Try(() => doc.GetPathName()) as string;
        string visitKey = !string.IsNullOrWhiteSpace(path)
            ? path
            : $"{componentName ?? doc.GetTitle()}::{doc.GetType()}";
        if (!visited.Add(visitKey))
        {
            return;
        }

        var references = ListRefGeometryItems(doc);
        var present = references
            .Select(item => (item.GetType().GetProperty("name")?.GetValue(item) as string ?? "").Trim())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = required.Where(present.Contains).ToArray();
        var missing = required.Where(name => !present.Contains(name)).ToArray();

        scopes.Add(new
        {
            path,
            componentName,
            title = Try(() => doc.GetTitle()) as string,
            documentType = doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY
                ? "assembly"
                : doc.GetType() == (int)swDocumentTypes_e.swDocPART
                    ? "part"
                    : "unknown",
            references,
            found,
            missing,
        });

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return;
        }

        object[]? components = Try(() => ((IAssemblyDoc)doc).GetComponents(false)) as object[];
        if (components is null)
        {
            return;
        }

        foreach (object entry in components)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            ModelDoc2? childDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
            if (childDoc is null)
            {
                continue;
            }

            string? childName = Try(() => component.Name2) as string;
            CollectUrdfScopes(childDoc, childName, scopes, visited, required);
        }
    }

    private static List<object> ListRefGeometryItems(ModelDoc2 doc)
    {
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
                    name = Try(() => current.Name) as string,
                    type,
                });
            }

            feature = Try(() => current.GetNextFeature());
        }

        return items;
    }

    private static void CollectLimitMates(ModelDoc2 doc, List<object> mates)
    {
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is null)
        {
            return;
        }

        object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
        int guard = 0;
        while (subFeature is not null && guard++ < 500)
        {
            dynamic current = subFeature;
            string? name = Try(() => current.Name) as string;
            string? type = Try(() => current.GetTypeName2()) as string;
            string typeLower = (type ?? "").ToLowerInvariant();
            if (typeLower.Contains("limit") || typeLower.Contains("angle") || typeLower.Contains("distance"))
            {
                mates.Add(new { name, type });
            }

            subFeature = Try(() => current.GetNextSubFeature());
        }
    }

    private static object AddUrdfFrame(JsonElement? args)
    {
        string path = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string name = StringArg(args, "name") ?? "urdf_link_frame";
        double originX = DoubleArg(args, "origin_x_m", 0);
        double originY = DoubleArg(args, "origin_y_m", 0);
        double originZ = DoubleArg(args, "origin_z_m", 0);
        string xAxisRef = StringArg(args, "x_axis_ref") ?? "Front Plane";
        string yAxisRef = StringArg(args, "y_axis_ref") ?? "Top Plane";
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);
        bool save = BoolArg(args, "save", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("add_urdf_frame requires a part document.");
        }

        if (replaceExisting)
        {
            DeleteFeatureByName(doc, name);
        }
        else if (FindFeatureByName(doc, name) is not null)
        {
            return new
            {
                document = DescribeDocument(doc),
                name,
                skipped = true,
                reason = "already_exists",
            };
        }

        Feature? coordFeature = CreateCoordSysAtPoint(doc, name, originX, originY, originZ, xAxisRef, yAxisRef);
        if (coordFeature is null)
        {
            throw new InvalidOperationException(
                $"Failed to create coordinate system '{name}'. Check origin and axis plane names ({xAxisRef}, {yAxisRef}).");
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
                throw new InvalidOperationException($"Save failed after add_urdf_frame. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            name,
            originM = new[] { originX, originY, originZ },
            xAxisRef,
            yAxisRef,
            saved,
            errors,
            warnings,
        };
    }

    private static string[]? StringArrayArg(JsonElement? args, string name)
    {
        if (args is null
            || args.Value.ValueKind != JsonValueKind.Object
            || !args.Value.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                string? text = entry.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text.Trim());
                }
            }
        }

        return items.Count == 0 ? null : items.ToArray();
    }
}
