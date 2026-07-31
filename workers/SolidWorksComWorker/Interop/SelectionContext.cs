using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private enum SelectionFieldKind
    {
        ComponentName,
        ComponentPath,
        FeatureName,
        PlaneName,
        BodyName,
        PersistReference,
        RefName,
    }

    private sealed record SelectionBinding(string ArgName, SelectionFieldKind Field, int SelectionIndex = 1);

    private static readonly Dictionary<string, SelectionBinding[]> SelectionCommandBindings = new(StringComparer.Ordinal)
    {
        ["get_component_box"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["get_component_transform"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["get_mass_properties"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["get_feature_box"] = [new("component_name", SelectionFieldKind.ComponentName, 1), new("feature_name", SelectionFieldKind.FeatureName, 1)],
        ["get_part_feature_box"] =
        [
            new("part_path", SelectionFieldKind.ComponentPath, 1),
            new("feature_name", SelectionFieldKind.FeatureName, 1),
        ],
        ["align_component_to_feature"] =
        [
            new("layout_component", SelectionFieldKind.ComponentName, 1),
            new("layout_feature", SelectionFieldKind.FeatureName, 1),
            new("target_component", SelectionFieldKind.ComponentName, 2),
        ],
        ["probe_feature_faces"] =
        [
            new("component_name", SelectionFieldKind.ComponentName, 1),
            new("feature_name", SelectionFieldKind.FeatureName, 1),
        ],
        ["get_persist_reference"] =
        [
            new("component_name", SelectionFieldKind.ComponentName, 1),
            new("ref", SelectionFieldKind.RefName, 1),
        ],
        ["get_planar_face_index"] =
        [
            new("component_name", SelectionFieldKind.ComponentName, 1),
            new("feature_name", SelectionFieldKind.FeatureName, 1),
        ],
        ["create_sketch"] = [new("plane_name", SelectionFieldKind.PlaneName, 1)],
        ["set_component_visible"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["set_component_fixed"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["rename_component"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["transform_component"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["set_component_transform"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["reset_component_transform"] = [new("component_name", SelectionFieldKind.ComponentName, 1)],
        ["mate_planes"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_coincident"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_parallel"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_distance"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_perpendicular"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_width"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_try_coincident"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_try_parallel"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_try_distance"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_try_perpendicular"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
        ["mate_try_width"] =
        [
            new("component_1", SelectionFieldKind.ComponentName, 1),
            new("ref_1", SelectionFieldKind.RefName, 1),
            new("component_2", SelectionFieldKind.ComponentName, 2),
            new("ref_2", SelectionFieldKind.RefName, 2),
        ],
    };

    private static readonly string[] SelectionAwareCommands =
        SelectionCommandBindings.Keys
            .Concat(["resolve_selection", "get_selection", "diagnose_selection"])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    private static JsonElement? ApplyUseSelection(string command, JsonElement? args)
    {
        if (!BoolArg(args, "use_selection", defaultValue: false))
        {
            return args;
        }

        if (command is "resolve_selection" or "get_selection" or "diagnose_selection")
        {
            return args;
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocumentForSelection(app, args);
        List<ResolvedSelectionItem> items = CollectResolvedSelection(doc);
        if (items.Count == 0)
        {
            throw WorkerException.Validation(
                "NO_SELECTION",
                "use_selection is true but nothing is highlighted in SolidWorks.",
                new Dictionary<string, object?> { ["command"] = command },
                [
                    "Highlight a component, face, body, plane, or edge in the graphics area.",
                    "Call solidworks_resolve_selection to inspect the current referent.",
                ]);
        }

        if (!SelectionCommandBindings.TryGetValue(command, out SelectionBinding[]? bindings))
        {
            throw WorkerException.Validation(
                "UNKNOWN_SELECTION_BINDING",
                $"Command '{command}' does not support use_selection.",
                new Dictionary<string, object?> { ["command"] = command },
                [
                    "Call solidworks_resolve_selection to inspect highlights, then use a selection-aware command.",
                    "See hints.selectionAwareCommands on resolve_selection output.",
                ]);
        }

        bool dualTarget = bindings.Any(static binding => binding.SelectionIndex == 2);
        int selectionIndex = (int)DoubleArg(args, "selection_index", 1);

        var overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (SelectionBinding binding in bindings)
        {
            int resolvedIndex = dualTarget ? binding.SelectionIndex : selectionIndex;
            ResolvedSelectionItem item = RequireSelectionItem(items, resolvedIndex);
            string? value = GetSelectionFieldValue(item, binding.Field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                overrides[binding.ArgName] = value;
            }
        }

        if (string.IsNullOrWhiteSpace(StringArg(args, "path")))
        {
            string? docPath = Try(() => doc.GetPathName()) as string;
            if (!string.IsNullOrWhiteSpace(docPath))
            {
                overrides["path"] = docPath;
            }
        }

        if (command == "get_part_feature_box" && !overrides.ContainsKey("part_path") && overrides.TryGetValue("path", out object? pathValue))
        {
            overrides["part_path"] = pathValue;
        }

        return MergeWorkerArgs(args, overrides);
    }

    private static ModelDoc2 ResolveDocumentForSelection(ISldWorks app, JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            return OpenDocument(app, PathGuard.AssertAllowedPath(inputPath));
        }

        ModelDoc2? activeDoc = Try(() => app.ActiveDoc) as ModelDoc2
            ?? throw WorkerException.Validation(
                "NO_ACTIVE_DOCUMENT",
                "use_selection requires an open SolidWorks document.",
                new Dictionary<string, object?>());

        return activeDoc;
    }

    private static ResolvedSelectionItem RequireSelectionItem(List<ResolvedSelectionItem> items, int index)
    {
        ResolvedSelectionItem? item = items.FirstOrDefault(entry => entry.Index == index);
        if (item is null)
        {
            throw WorkerException.Validation(
                "SELECTION_INDEX_MISSING",
                $"Selection index {index} is not highlighted. Currently selected: {items.Count}.",
                new Dictionary<string, object?> { ["selectionIndex"] = index, ["selectedCount"] = items.Count },
                [
                    "Highlight more entities, or pass selection_index for the item you mean.",
                    "For mates and dual-target ops, highlight two items (index 1 and 2).",
                ]);
        }

        return item;
    }

    private static string? GetSelectionFieldValue(ResolvedSelectionItem item, SelectionFieldKind field) =>
        field switch
        {
            SelectionFieldKind.ComponentName => item.ComponentName,
            SelectionFieldKind.ComponentPath => item.ComponentPath,
            SelectionFieldKind.FeatureName => item.FeatureName,
            SelectionFieldKind.PlaneName => item.PlaneName ?? item.FeatureName,
            SelectionFieldKind.BodyName => item.BodyName,
            SelectionFieldKind.PersistReference => item.PersistReference,
            SelectionFieldKind.RefName => item.PlaneName ?? item.FeatureName ?? item.BodyName ?? item.ComponentName,
            _ => null,
        };

    private static JsonElement? MergeWorkerArgs(JsonElement? args, Dictionary<string, object?> overrides)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (args is not null && args.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in args.Value.EnumerateObject())
            {
                merged[property.Name] = JsonElementToObject(property.Value);
            }
        }

        foreach (KeyValuePair<string, object?> entry in overrides)
        {
            merged[entry.Key] = entry.Value;
        }

        return JsonSerializer.SerializeToElement(merged);
    }

    private static object? JsonElementToObject(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out long integer) ? integer : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => value.EnumerateArray().Select(JsonElementToObject).ToArray(),
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(static prop => prop.Name, static prop => JsonElementToObject(prop.Value), StringComparer.OrdinalIgnoreCase),
            _ => value.GetRawText(),
        };

    private static object BuildSelectionReferent(ResolvedSelectionItem item) =>
        new
        {
            item.Index,
            label = BuildSelectionLabel(item),
            shortLabel = BuildSelectionShortLabel(item),
            kind = item.Kind,
            componentName = item.ComponentName,
            componentPath = item.ComponentPath,
            featureName = item.FeatureName,
            bodyName = item.BodyName,
            planeName = item.PlaneName,
            selectionId = item.SelectionId,
            persistReference = item.PersistReference,
            semanticTags = InferSemanticTags(item),
            toolArgs = BuildSelectionToolArgs(item),
        };

    private static Dictionary<string, string?> BuildSelectionToolArgs(ResolvedSelectionItem item) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["component_name"] = item.ComponentName,
            ["component_path"] = item.ComponentPath,
            ["feature_name"] = item.FeatureName,
            ["plane_name"] = item.PlaneName ?? item.FeatureName,
            ["body_name"] = item.BodyName,
            ["ref_name"] = item.PlaneName ?? item.FeatureName ?? item.BodyName ?? item.ComponentName,
            ["persist_reference"] = item.PersistReference,
        };

    private static string BuildSelectionLabel(ResolvedSelectionItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Kind))
        {
            parts.Add(item.Kind);
        }

        if (!string.IsNullOrWhiteSpace(item.FeatureName))
        {
            parts.Add($"feature {item.FeatureName}");
        }
        else if (!string.IsNullOrWhiteSpace(item.PlaneName))
        {
            parts.Add($"plane {item.PlaneName}");
        }
        else if (!string.IsNullOrWhiteSpace(item.BodyName))
        {
            parts.Add($"body {item.BodyName}");
        }

        if (!string.IsNullOrWhiteSpace(item.ComponentName))
        {
            parts.Add($"on {item.ComponentName}");
        }

        return parts.Count == 0 ? "highlighted entity" : string.Join(" · ", parts);
    }

    private static string BuildSelectionShortLabel(ResolvedSelectionItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ComponentName) && !string.IsNullOrWhiteSpace(item.FeatureName))
        {
            return $"{item.ComponentName} / {item.FeatureName}";
        }

        return item.ComponentName
            ?? item.FeatureName
            ?? item.PlaneName
            ?? item.BodyName
            ?? item.Kind;
    }

    private static string[] InferSemanticTags(ResolvedSelectionItem item)
    {
        var tags = new List<string> { item.Kind };
        if (item.Kind == "plane")
        {
            tags.Add("domain:reference-geometry");
        }

        return tags.Distinct(StringComparer.Ordinal).ToArray();
    }
}
