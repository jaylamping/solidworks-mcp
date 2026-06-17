using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private sealed record ResolvedSelectionItem(
        int Index,
        int Mark,
        string Kind,
        int SelectType,
        string? TypeName,
        string? ComponentName,
        string? ComponentPath,
        string? FeatureName,
        string? BodyName,
        string? PlaneName,
        string? SelectionId,
        string? PersistReference);

    private static object ResolveSelection(JsonElement? args)
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

        List<ResolvedSelectionItem> items = CollectResolvedSelection(doc);
        ResolvedSelectionItem? primary = items.FirstOrDefault();
        return new
        {
            document = DescribeDocument(doc),
            selectedCount = items.Count,
            primary = primary is null ? null : BuildSelectionReferent(primary),
            selections = items.Select(BuildSelectionReferent).ToArray(),
            entities = items.Select(DescribeResolvedSelectionEntity).ToArray(),
            hints = BuildGeneralSelectionHints(items),
        };
    }

    private static object DescribeResolvedSelectionEntity(ResolvedSelectionItem item) =>
        new
        {
            item.Index,
            item.Mark,
            kind = item.Kind,
            selectType = item.SelectType,
            typeName = item.TypeName,
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

    private static object BuildGeneralSelectionHints(List<ResolvedSelectionItem> items) =>
        new
        {
            meaning = items.Count == 0
                ? "Nothing highlighted — select the entity you mean in SolidWorks first."
                : items.Count == 1
                    ? "Single referent: pass use_selection: true on worker/MCP tools instead of typing names."
                    : "Multi-select: index 1 is the primary referent; index 2 is the second target (mates, cutouts, etc.).",
            usage = "When you say 'this body', 'here', or 'the selected face', call tools with use_selection: true (optional selection_index).",
            selectionAwareCommands = SelectionAwareCommands,
            examples =
                new[]
                {
                    "get_component_box + use_selection → bbox of highlighted component",
                    "create_sketch + use_selection → sketch on highlighted plane/face",
                    "mate_coincident + use_selection → mate two highlighted refs (indices 1 and 2)",
                    "actuator_cut_cavity + use_selection → cut into highlighted bracket",
                },
        };

    private static List<ResolvedSelectionItem> CollectResolvedSelection(ModelDoc2 doc)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        int count = selectionMgr is null
            ? 0
            : Try(() => selectionMgr.GetSelectedObjectCount2(-1)) as int? ?? 0;

        var items = new List<ResolvedSelectionItem>();
        for (int i = 1; i <= count; i++)
        {
            items.Add(ResolveSelectionItem(doc, selectionMgr!, i));
        }

        return items;
    }

    private static ResolvedSelectionItem ResolveSelectionItem(ModelDoc2 doc, SelectionMgr selectionMgr, int index)
    {
        object? entity = Try(() => selectionMgr.GetSelectedObject6(index, -1));
        int mark = Try(() => selectionMgr.GetSelectedObjectMark(index)) as int? ?? 0;
        int selectType = Try(() => selectionMgr.GetSelectedObjectType3(index, -1)) as int? ?? -1;
        string kind = DescribeSelectType(selectType);
        string? typeName = entity?.GetType().Name;

        Component2? component = Try(() => selectionMgr.GetSelectedObjectsComponent4(index, -1)) as Component2;
        string? componentName = Try(() => component?.Name2) as string;
        string? componentPath = Try(() => component?.GetPathName()) as string;

        string? featureName = null;
        string? bodyName = null;
        string? planeName = null;
        string? selectionId = null;

        if (entity is Face2 face)
        {
            featureName = Try(() => (face.GetFeature() as Feature)?.Name) as string;
            if (Try(() => face.GetBody()) is Body2 body)
            {
                bodyName = Try(() => body.Name) as string;
            }
        }
        else if (entity is Body2 bodyEntity)
        {
            bodyName = Try(() => bodyEntity.Name) as string;
        }
        else if (entity is Feature feature)
        {
            featureName = Try(() => feature.Name) as string;
            if (Try(() => feature.GetTypeName2()) as string == "RefPlane")
            {
                planeName = featureName;
            }
        }
        else if (entity is RefPlane)
        {
            planeName ??= Try(() => (entity as Feature)?.Name) as string;
            featureName ??= planeName;
        }
        else if (entity is Component2 selectedComponent)
        {
            component = selectedComponent;
            componentName = Try(() => selectedComponent.Name2) as string;
            componentPath = Try(() => selectedComponent.GetPathName()) as string;
        }

        if (doc.GetType() == (int)swDocumentTypes_e.swDocPART && string.IsNullOrWhiteSpace(componentPath))
        {
            componentPath = Try(() => doc.GetPathName()) as string;
        }

        if (!string.IsNullOrWhiteSpace(componentName))
        {
            string? docTitle = Try(() => doc.GetTitle()) as string;
            if (!string.IsNullOrWhiteSpace(docTitle))
            {
                selectionId = componentName.Contains('@', StringComparison.Ordinal)
                    ? componentName
                    : $"{componentName}@{docTitle}";
            }

            if (!string.IsNullOrWhiteSpace(featureName))
            {
                selectionId = $"{featureName}@{componentName}";
            }
        }
        else if (!string.IsNullOrWhiteSpace(planeName) && doc.GetType() == (int)swDocumentTypes_e.swDocPART)
        {
            selectionId = planeName;
        }

        string? persistReference = null;
        if (entity is not null)
        {
            byte[]? persistRef = Try(() => doc.Extension.GetPersistReference3(entity)) as byte[];
            if (persistRef is not null && persistRef.Length > 0)
            {
                persistReference = Convert.ToBase64String(persistRef);
            }
        }

        return new ResolvedSelectionItem(
            index,
            mark,
            kind,
            selectType,
            typeName,
            componentName,
            componentPath,
            featureName,
            bodyName,
            planeName,
            selectionId,
            persistReference);
    }

    private static string DescribeSelectType(int type) =>
        type switch
        {
            (int)swSelectType_e.swSelCOMPONENTS => "component",
            (int)swSelectType_e.swSelFACES => "face",
            (int)swSelectType_e.swSelDATUMPLANES => "plane",
            (int)swSelectType_e.swSelBODYFEATURES => "body",
            (int)swSelectType_e.swSelEDGES => "edge",
            (int)swSelectType_e.swSelVERTICES => "vertex",
            _ => $"unknown_{type}",
        };

    private static Component2? FindComponentByName(ModelDoc2 doc, string? componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName) || doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            return null;
        }

        return FindComponent((IAssemblyDoc)doc, null, componentName);
    }

    private static (Component2 Bracket, Component2 Tool, string BracketPartPath) ResolveActuatorCavityFromSelection(
        ModelDoc2 asmDoc,
        IAssemblyDoc assembly,
        string? modelId)
    {
        List<ResolvedSelectionItem> items = CollectResolvedSelection(asmDoc);
        if (items.Count == 0)
        {
            throw new InvalidOperationException(
                "use_selection requires a highlighted bracket body/face/component (and optionally an actuator) in the active assembly.");
        }

        Component2? bracket = null;
        Component2? tool = null;

        foreach (ResolvedSelectionItem item in items)
        {
            Component2? component = FindComponentByName(asmDoc, item.ComponentName);
            if (component is null)
            {
                continue;
            }

            string[] tags = InferSemanticTags(item);
            if (tags.Contains("domain:actuator", StringComparer.Ordinal))
            {
                tool ??= component;
            }
            else
            {
                bracket ??= component;
            }
        }

        List<ResolvedSelectionItem> withComponents = items.Where(i => i.ComponentName is not null).ToList();
        if (withComponents.Count == 1 && bracket is null && tool is null)
        {
            Component2? only = FindComponentByName(asmDoc, withComponents[0].ComponentName);
            if (only is not null)
            {
                if (InferSemanticTags(withComponents[0]).Contains("domain:actuator", StringComparer.Ordinal))
                {
                    tool = only;
                }
                else
                {
                    bracket = only;
                }
            }
        }
        else if (withComponents.Count >= 2)
        {
            foreach (ResolvedSelectionItem item in withComponents)
            {
                Component2? component = FindComponentByName(asmDoc, item.ComponentName);
                if (component is null)
                {
                    continue;
                }

                if (InferSemanticTags(item).Contains("domain:actuator", StringComparer.Ordinal))
                {
                    tool ??= component;
                }
                else
                {
                    bracket ??= component;
                }
            }
        }

        if (tool is null && !string.IsNullOrWhiteSpace(modelId))
        {
            tool = FindComponent(assembly, null, $"actuator_{modelId}")
                ?? FindComponent(assembly, null, "actuator");
        }

        if (bracket is null)
        {
            throw new InvalidOperationException(
                "Could not resolve target from selection. Highlight the part, body, or face you mean.");
        }

        if (tool is null)
        {
            throw new InvalidOperationException(
                "Could not resolve actuator tool from selection. Highlight the actuator component or pass model.");
        }

        if (ReferenceEquals(bracket, tool))
        {
            throw new InvalidOperationException(
                "Selection resolved the same component for bracket and tool. Highlight distinct targets.");
        }

        string? bracketPartPath = Try(() => bracket.GetPathName()) as string;
        if (string.IsNullOrWhiteSpace(bracketPartPath))
        {
            throw new InvalidOperationException("Resolved target component has no part path.");
        }

        return (bracket, tool, bracketPartPath);
    }
}
