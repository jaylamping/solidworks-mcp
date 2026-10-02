using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Shared plumbing for the part-modeling tools: document targeting, units,
// JSON argument helpers, and feature outcome / failure reporting.
internal static partial class Program
{
    private const double ModelTolerance = 1e-9;

    /// <summary>
    /// Resolves the target document and refuses anything that is not a part.
    /// Prevents modeling commands from silently landing in an open assembly.
    /// </summary>
    private static ModelDoc2 RequirePartDocument(ISldWorks app, JsonElement? args, string command)
    {
        ModelDoc2 doc = ResolveDocument(app, args);
        int type = doc.GetType();
        if (type != (int)swDocumentTypes_e.swDocPART)
        {
            string kind = type == (int)swDocumentTypes_e.swDocASSEMBLY ? "assembly" : type == (int)swDocumentTypes_e.swDocDRAWING ? "drawing" : $"type {type}";
            throw WorkerException.Validation(
                "PART_DOCUMENT_REQUIRED",
                $"{command} only works on part documents, but the target is an {kind}: {Try(() => doc.GetTitle())}.",
                new Dictionary<string, object?>
                {
                    ["command"] = command,
                    ["target"] = DescribeDocument(doc),
                    ["pathProvided"] = !string.IsNullOrWhiteSpace(StringArg(args, "path")),
                },
                ["Pass path to the .SLDPRT you intend to edit.", "Create one with solidworks_new_document first."]);
        }

        return doc;
    }

    /// <summary>Length scale from the `units` arg: mm (default), m, cm, in.</summary>
    private static double UnitScale(JsonElement? args, string fallback = "mm")
    {
        string units = (StringArg(args, "units") ?? fallback).Trim().ToLowerInvariant();
        return units switch
        {
            "mm" or "millimeter" or "millimeters" => 0.001,
            "cm" => 0.01,
            "m" or "meter" or "meters" => 1.0,
            "in" or "inch" or "inches" => 0.0254,
            _ => throw WorkerException.Validation(
                "BAD_UNITS",
                $"Unsupported units '{units}'. Use mm, cm, m, or in.",
                new Dictionary<string, object?> { ["units"] = units }),
        };
    }

    private static string UnitsLabel(JsonElement? args) => (StringArg(args, "units") ?? "mm").Trim().ToLowerInvariant();

    private static double Deg(double degrees) => degrees * Math.PI / 180.0;

    // ---- JsonElement helpers (for nested objects; top-level args use StringArg/DoubleArg) ----

    private static bool Has(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null;

    private static JsonElement? Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static JsonElement? Prop(JsonElement? e, string name) => e is null ? null : Prop(e.Value, name);

    private static double Num(JsonElement e, string name, double? fallback = null)
    {
        JsonElement? v = Prop(e, name);
        if (v is { ValueKind: JsonValueKind.Number })
        {
            return v.Value.GetDouble();
        }

        if (v is { ValueKind: JsonValueKind.String } && double.TryParse(v.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        return fallback ?? throw WorkerException.Validation(
            "MISSING_NUMBER",
            $"Missing numeric field '{name}' in {Truncate(e.GetRawText(), 160)}",
            new Dictionary<string, object?> { ["field"] = name });
    }

    private static double? OptNum(JsonElement e, string name)
    {
        JsonElement? v = Prop(e, name);
        return v is { ValueKind: JsonValueKind.Number } ? v.Value.GetDouble() : null;
    }

    private static string? Str(JsonElement e, string name)
    {
        JsonElement? v = Prop(e, name);
        return v is { ValueKind: JsonValueKind.String } ? v.Value.GetString() : null;
    }

    private static bool Flag(JsonElement e, string name, bool fallback = false)
    {
        JsonElement? v = Prop(e, name);
        return v?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static double[] Vec(JsonElement e, int minLength = 2)
    {
        if (e.ValueKind != JsonValueKind.Array)
        {
            throw WorkerException.Validation("BAD_POINT", $"Expected a numeric array, got {Truncate(e.GetRawText(), 120)}", new Dictionary<string, object?>());
        }

        double[] values = e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (values.Length < minLength)
        {
            throw WorkerException.Validation("BAD_POINT", $"Expected at least {minLength} numbers, got {values.Length}.", new Dictionary<string, object?>());
        }

        return values;
    }

    private static double[] VecProp(JsonElement e, string name, int minLength = 2)
    {
        JsonElement v = Prop(e, name) ?? throw WorkerException.Validation(
            "MISSING_POINT",
            $"Missing point/vector field '{name}' in {Truncate(e.GetRawText(), 160)}",
            new Dictionary<string, object?> { ["field"] = name });
        return Vec(v, minLength);
    }

    private static double[] Scaled(double[] v, double scale) => v.Select(x => x * scale).ToArray();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static double[] Normalize3(double[] v)
    {
        double len = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return len < ModelTolerance ? new[] { 0.0, 0.0, 0.0 } : new[] { v[0] / len, v[1] / len, v[2] / len };
    }

    private static double Dot3(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double Dist3(double[] a, double[] b) =>
        Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));

    private static double[] Round(double[]? v, double scale, int digits = 4) =>
        v is null ? Array.Empty<double>() : v.Select(x => Math.Round(x / scale, digits)).ToArray();

    // ---- Feature outcome / failure reporting ----

    private static object DescribeSelection(ModelDoc2 doc)
    {
        SelectionMgr? mgr = Try(() => doc.SelectionManager) as SelectionMgr;
        if (mgr is null)
        {
            return Array.Empty<object>();
        }

        int count = (Try(() => mgr.GetSelectedObjectCount2(-1)) as int?) ?? 0;
        var items = new List<object>();
        for (int i = 1; i <= count; i++)
        {
            int index = i;
            int type = (Try(() => mgr.GetSelectedObjectType3(index, -1)) as int?) ?? 0;
            items.Add(new
            {
                index,
                type = Enum.IsDefined(typeof(swSelectType_e), type) ? ((swSelectType_e)type).ToString() : type.ToString(),
                mark = Try(() => mgr.GetSelectedObjectMark(index)),
            });
        }

        return items;
    }

    private static List<object> WhatsWrong(ModelDoc2 doc)
    {
        var output = new List<object>();
        int count = (Try(() => doc.Extension.GetWhatsWrongCount()) as int?) ?? 0;
        if (count <= 0)
        {
            return output;
        }

        object? features = null;
        object? errorCodes = null;
        object? warnings = null;
        bool ok = (Try(() => doc.Extension.GetWhatsWrong(out features, out errorCodes, out warnings)) as bool?) ?? false;
        if (!ok || features is not object[] featureArray)
        {
            return output;
        }

        int[] codes = errorCodes as int[] ?? Array.Empty<int>();
        bool[] warns = warnings as bool[] ?? Array.Empty<bool>();
        for (int i = 0; i < featureArray.Length; i++)
        {
            int code = i < codes.Length ? codes[i] : 0;
            output.Add(new
            {
                feature = Try(() => ((Feature)featureArray[i]).Name),
                error = DescribeFeatureError(code),
                warning = i < warns.Length && warns[i],
            });
        }

        return output;
    }

    private static string DescribeFeatureError(int code) =>
        code == 0 ? "none" : Enum.IsDefined(typeof(swFeatureError_e), code) ? ((swFeatureError_e)code).ToString() : $"code {code}";

    /// <summary>Throws a FEATURE_FAILED error carrying selection + rebuild diagnostics.</summary>
    private static WorkerException FeatureFailed(ModelDoc2 doc, string what, Dictionary<string, object?>? context = null, params string[] remediation)
    {
        var ctx = context ?? new Dictionary<string, object?>();
        ctx["selectionAtCall"] = DescribeSelection(doc);
        ctx["whatsWrong"] = WhatsWrong(doc);
        ctx["activeSketch"] = Try(() => ((Feature)doc.SketchManager.ActiveSketch)?.Name);
        return WorkerException.Worker(
            "FEATURE_FAILED",
            $"SolidWorks did not create the {what}. The API returned null; see context.selectionAtCall and whatsWrong.",
            ctx,
            remediation.Length > 0 ? remediation : ["Check that the sketch has closed, non-intersecting contours.", "Check the referenced faces/edges still exist (solidworks_part_report)."]);
    }

    /// <summary>
    /// Rebuilds and validates a freshly created feature. If it rebuilt with an error
    /// and rollback is on, the feature is deleted so the part stays clean.
    /// </summary>
    private static object FeatureOutcome(ModelDoc2 doc, Feature feature, JsonElement? args, double scale, string? rename = null)
    {
        if (!string.IsNullOrWhiteSpace(rename))
        {
            TryVoid(() => feature.Name = rename);
        }

        TryVoid(() => doc.ClearSelection2(true));
        TryVoid(() => doc.EditRebuild3());
        bool isWarning = false;
        int code = (Try(() => feature.GetErrorCode2(out isWarning)) as int?) ?? 0;
        string name = Try(() => feature.Name) as string ?? "?";
        string typeName = Try(() => feature.GetTypeName2()) as string ?? "?";

        if (code != 0 && !isWarning && BoolArg(args, "rollback_on_error", defaultValue: true))
        {
            var whatsWrong = WhatsWrong(doc);
            TryVoid(() => DeleteFeatureByName(doc, name));
            TryVoid(() => doc.EditRebuild3());
            throw WorkerException.Worker(
                "FEATURE_REBUILD_ERROR",
                $"{typeName} '{name}' was created but failed to rebuild ({DescribeFeatureError(code)}); it was rolled back.",
                new Dictionary<string, object?>
                {
                    ["feature"] = name,
                    ["error"] = DescribeFeatureError(code),
                    ["whatsWrong"] = whatsWrong,
                },
                ["Pass rollback_on_error:false to keep the failed feature for inspection."]);
        }

        return new
        {
            document = DescribeDocument(doc),
            featureName = name,
            featureType = typeName,
            rebuildError = code == 0 ? null : DescribeFeatureError(code),
            rebuildWarning = code != 0 && isWarning,
            part = PartGeometrySummary(doc, scale),
        };
    }

    private static object PartGeometrySummary(ModelDoc2 doc, double scale)
    {
        if (doc is not PartDoc part)
        {
            return new { };
        }

        object[] bodies = Try(() => part.GetBodies2((int)swBodyType_e.swSolidBody, false)) as object[] ?? Array.Empty<object>();
        double volume = 0;
        double area = 0;
        foreach (object entry in bodies)
        {
            if (entry is Body2 body && Try(() => body.GetMassProperties(1.0)) is double[] mp && mp.Length >= 5)
            {
                volume += mp[3];
                area += mp[4];
            }
        }

        double[]? box = Try(() => part.GetPartBox(true)) as double[];
        return new
        {
            units = scale switch { 0.001 => "mm", 1.0 => "m", 0.01 => "cm", 0.0254 => "in", _ => scale.ToString() },
            solidBodies = bodies.Length,
            bodyNames = bodies.OfType<Body2>().Select(b => Try(() => b.Name) as string).ToArray(),
            volume = Math.Round(volume / (scale * scale * scale), 3),
            surfaceArea = Math.Round(area / (scale * scale), 3),
            boundingBox = box is { Length: >= 6 } ? new { min = Round(box[..3], scale), max = Round(box[3..6], scale) } : null,
        };
    }

    private static IEnumerable<Body2> SolidBodies(ModelDoc2 doc)
    {
        if (doc is not PartDoc part)
        {
            return Array.Empty<Body2>();
        }

        object[] bodies = Try(() => part.GetBodies2((int)swBodyType_e.swSolidBody, false)) as object[] ?? Array.Empty<object>();
        return bodies.OfType<Body2>();
    }

    /// <summary>Selects the named sketch (mark) for a feature that consumes it.</summary>
    private static Feature SelectSketchByName(ModelDoc2 doc, string sketchName, bool append, int mark)
    {
        Feature sketch = FindFeatureByName(doc, sketchName) ?? throw WorkerException.Validation(
            "SKETCH_NOT_FOUND",
            $"Sketch not found: {sketchName}",
            new Dictionary<string, object?> { ["sketch"] = sketchName },
            ["List sketches with solidworks_list_sketches or solidworks_part_report."]);

        if (!sketch.Select2(append, mark))
        {
            throw WorkerException.Validation("SELECT_FAILED", $"Could not select sketch {sketchName}.", new Dictionary<string, object?> { ["sketch"] = sketchName });
        }

        return sketch;
    }

    private static SelectData NewSelectData(ModelDoc2 doc, int mark)
    {
        SelectionMgr mgr = (SelectionMgr)doc.SelectionManager;
        SelectData data = (SelectData)mgr.CreateSelectData();
        data.Mark = mark;
        return data;
    }

    /// <summary>Exits any open sketch so feature creation starts from a clean state.</summary>
    private static void EnsureNoActiveSketch(ModelDoc2 doc)
    {
        if (Try(() => doc.SketchManager.ActiveSketch) is not null)
        {
            TryVoid(() => doc.SketchManager.InsertSketch(true));
        }
    }
}
