using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Editing an existing feature tree: rollback (insert mid-tree), reorder, and equations /
// global variables for parametric designs.
internal static partial class Program
{
    private static object Rollback(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "rollback");
        EnsureNoActiveSketch(doc);
        string? before = StringArg(args, "before");
        string? after = StringArg(args, "after");
        bool ok;
        string position;
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after))
        {
            string name = before ?? after!;
            if (FindFeatureByName(doc, name) is null)
            {
                throw WorkerException.Validation("FEATURE_NOT_FOUND", $"Feature not found: {name}", new Dictionary<string, object?>());
            }

            int where = before is not null ? (int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature : (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature;
            ok = doc.FeatureManager.EditRollback(where, name);
            position = before is not null ? $"before {name}" : $"after {name}";
        }
        else
        {
            ok = doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            position = "end";
        }

        if (!ok)
        {
            throw WorkerException.Worker("ROLLBACK_FAILED", $"Could not move the rollback bar {position}.", new Dictionary<string, object?>());
        }

        TryVoid(() => doc.EditRebuild3());
        return new
        {
            document = DescribeDocument(doc),
            rollbackBar = position,
            note = position == "end" ? null : "New features are inserted at the rollback bar. Call solidworks_rollback with no arguments to roll forward to the end when done.",
            part = PartGeometrySummary(doc, UnitScale(args)),
        };
    }

    private static object ReorderFeature(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "reorder_feature");
        EnsureNoActiveSketch(doc);
        string feature = RequiredStringArg(args, "feature");
        string? before = StringArg(args, "before");
        string? after = StringArg(args, "after");
        (int location, string target) = before is not null ? ((int)swMoveLocation_e.swMoveBefore, before)
            : after is not null ? ((int)swMoveLocation_e.swMoveAfter, after)
            : ((int)swMoveLocation_e.swMoveToEnd, "");
        bool ok = doc.Extension.ReorderFeature2(feature, target, location);
        if (!ok)
        {
            throw WorkerException.Worker("REORDER_FAILED", $"Could not move {feature} {(before is not null ? "before " + before : after is not null ? "after " + after : "to the end")}.",
                new Dictionary<string, object?>(),
                ["A feature cannot move above its parents (its sketch, the faces it references)."]);
        }

        TryVoid(() => doc.ForceRebuild3(false));
        return new { document = DescribeDocument(doc), moved = feature, before, after, part = PartGeometrySummary(doc, UnitScale(args)) };
    }

    private static object Equations(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "equations");
        EnsureNoActiveSketch(doc);
        EquationMgr eq = doc.GetEquationMgr();
        var changes = new List<object>();

        // Globals: {"Width": "40mm"} or {"Width": 40} (number = mm unless units given).
        if (Prop(args, "globals") is JsonElement globals)
        {
            string unitSuffix = UnitsLabel(args);
            foreach (JsonProperty g in globals.EnumerateObject())
            {
                string rhs = g.Value.ValueKind == JsonValueKind.Number ? $"{g.Value.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)}{unitSuffix}" : g.Value.GetString() ?? "0";
                changes.Add(UpsertEquation(eq, $"\"{g.Name}\"", rhs));
            }
        }

        // Links: {"D1@Boss-Extrude1": "\"Width\" / 2"}.
        if (Prop(args, "links") is JsonElement links)
        {
            foreach (JsonProperty l in links.EnumerateObject())
            {
                changes.Add(UpsertEquation(eq, $"\"{l.Name}\"", l.Value.GetString() ?? throw new ArgumentException($"links.{l.Name} must be a string expression")));
            }
        }

        foreach (string remove in NamesArg(args, "remove"))
        {
            for (int i = eq.GetCount() - 1; i >= 0; i--)
            {
                if (LhsName(eq.Equation[i]) == remove)
                {
                    eq.Delete(i);
                    changes.Add(new { removed = remove });
                }
            }
        }

        TryVoid(() => eq.EvaluateAll());
        TryVoid(() => doc.ForceRebuild3(false));
        var listing = Enumerable.Range(0, eq.GetCount()).Select(i => new
        {
            equation = Try(() => eq.Equation[i]),
            value = Try(() => Math.Round(eq.Value[i], 6)),
            global = Try(() => eq.GlobalVariable[i]),
        }).ToList();

        var errors = FeatureTree(doc)
            .Select(f => (f, code: (Try(() => f.GetErrorCode2(out bool _)) as int?) ?? 0))
            .Where(x => x.code != 0)
            .Select(x => new { feature = Try(() => x.f.Name), error = DescribeFeatureError(x.code) })
            .ToList();
        return new { document = DescribeDocument(doc), changes, equations = listing, rebuildErrors = errors, part = PartGeometrySummary(doc, UnitScale(args)) };
    }

    private static string LhsName(string equation) => equation.Split('=')[0].Trim().Trim('"');

    private static object UpsertEquation(EquationMgr eq, string lhs, string rhs)
    {
        string text = $"{lhs} = {rhs}";
        string name = lhs.Trim('"');
        for (int i = 0; i < eq.GetCount(); i++)
        {
            if (LhsName(eq.Equation[i]) == name)
            {
                eq.Equation[i] = text;
                return new { updated = text };
            }
        }

        int count = eq.GetCount();
        string compact = $"{lhs}= {rhs}";
        int index = -1;
        string? how = null;
        foreach ((string label, Func<int> add) in new (string, Func<int>)[]
                 {
                     ("Add3(end, all configs)", () => eq.Add3(count, text, true, (int)swInConfigurationOpts_e.swAllConfiguration, null)),
                     ("Add3(-1, all configs)", () => eq.Add3(-1, text, true, (int)swInConfigurationOpts_e.swAllConfiguration, null)),
                     ("Add2(end)", () => eq.Add2(count, text, true)),
                     ("Add2(end, compact)", () => eq.Add2(count, compact, true)),
                     ("Add(end)", () => eq.Add(count, text)),
                 })
        {
            index = (Try(() => add()) as int?) ?? -1;
            if (index >= 0)
            {
                how = label;
                break;
            }
        }

        if (index >= 0)
        {
            return new { added = text, via = how };
        }

        throw WorkerException.Validation("EQUATION_REJECTED", $"SolidWorks rejected the equation: {text}", new Dictionary<string, object?>(),
            ["Global: \"Name\" = 40mm. Link: \"D1@Boss-Extrude1\" = \"Name\" / 2. Dimension names come from solidworks_feature_details."]);
    }
}
