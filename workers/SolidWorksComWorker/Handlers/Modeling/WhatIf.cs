using System.Text.Json;
using System.Text.Json.Nodes;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Design exploration: apply each variant (dimension edits and/or extra feature steps), measure it
// with the same analysis, then undo it, so variants are compared on identical terms and the part
// ends exactly as it started.
internal static partial class Program
{
    private static object TryVariants(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "try_variants");
        double s = UnitScale(args);
        string path = RequiredStringArg(args, "path");
        string units = UnitsLabel(args);
        EnsureNoActiveSketch(doc);
        JsonElement variants = Prop(args, "variants") ?? throw new ArgumentException("variants is required");
        JsonElement? analysis = Prop(args, "analysis");
        bool thickness = BoolArg(args, "thickness");
        double density = DoubleArg(args, "density_g_cm3", 0);
        if (density <= 0 && analysis is JsonElement an && Str(an, "material") is string mat && FeaMaterials.TryGetValue(mat, out FeaMaterial? fm))
        {
            density = fm.DensityKgM3 / 1000.0;
        }

        double baselineVolume = SolidVolume(doc);
        var rows = new List<object>();
        var list = new List<(string Name, JsonElement? Spec)> { ("baseline", null) };
        if (!BoolArg(args, "include_baseline", true))
        {
            list.Clear();
        }

        list.AddRange(variants.EnumerateArray().Select((v, i) => (Str(v, "name") ?? $"variant_{i + 1}", (JsonElement?)v)));
        foreach ((string name, JsonElement? spec) in list)
        {
            string marker = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string ?? "";
            var reverts = new Dictionary<string, double>();
            object? error = null;
            object? fea = null;
            object? wall = null;
            double volume = 0;
            try
            {
                if (spec is JsonElement v && Prop(v, "dimensions") is JsonElement dims)
                {
                    var call = new Dictionary<string, object?> { ["path"] = path, ["units"] = units, ["values"] = JsonSerializer.Deserialize<object>(dims.GetRawText()) };
                    JsonNode? result = JsonSerializer.SerializeToNode(SetDimensions(JsonSerializer.SerializeToElement(call)), WriteJson);
                    foreach (JsonNode? change in result?["changed"]?.AsArray() ?? new JsonArray())
                    {
                        reverts[change!["dimension"]!.GetValue<string>()] = change["from"]!.GetValue<double>();
                    }
                }

                if (spec is JsonElement v2 && Prop(v2, "steps") is JsonElement steps)
                {
                    foreach (JsonElement step in steps.EnumerateArray())
                    {
                        RunVariantStep(step, path, units);
                    }
                }

                TryVoid(() => doc.EditRebuild3());
                volume = SolidVolume(doc);
                if (analysis is JsonElement a)
                {
                    JsonObject feaArgs = JsonNode.Parse(a.GetRawText())!.AsObject();
                    feaArgs["path"] = path;
                    feaArgs["units"] = units;
                    JsonNode? feaResult = JsonSerializer.SerializeToNode(SimulateStatic(JsonSerializer.SerializeToElement(feaArgs)), WriteJson);
                    fea = feaResult?["results"];
                }

                if (thickness)
                {
                    JsonNode? t = JsonSerializer.SerializeToNode(ThicknessCheck(JsonSerializer.SerializeToElement(new { path, units })), WriteJson);
                    wall = new { thinnest = t?["thinnest"]?.GetValue<double>(), percentile5 = t?["percentile5"]?.GetValue<double>(), median = t?["median"]?.GetValue<double>() };
                }
            }
            catch (WorkerException ex)
            {
                error = new { ex.Error.Code, ex.Error.Message };
            }
            catch (Exception ex)
            {
                error = new { Code = "VARIANT_FAILED", ex.Message };
            }
            finally
            {
                UndoVariant(doc, marker, reverts, path, units);
            }

            double grams = density > 0 ? volume * 1e6 * density : 0;
            rows.Add(new
            {
                variant = name,
                volume = Math.Round(volume / (s * s * s), 2),
                massGrams = density > 0 ? Math.Round(grams, 1) : (double?)null,
                fea,
                wall,
                error,
            });
        }

        double after = SolidVolume(doc);
        return new
        {
            document = DescribeDocument(doc),
            units,
            variants = rows,
            restored = Math.Abs(after - baselineVolume) <= Math.Max(1e-12, baselineVolume * 1e-6),
            notes = new[] { "Each variant is applied, measured, and undone (added features deleted, dimensions reverted) before the next one." },
        };
    }

    private static double SolidVolume(ModelDoc2 doc) =>
        SolidBodies(doc).Sum(b => Try(() => b.GetMassProperties(1.0)) is double[] mp && mp.Length >= 4 ? mp[3] : 0);

    /// <summary>Runs one {tool, args} step through the worker's own command handlers.</summary>
    private static void RunVariantStep(JsonElement step, string path, string units)
    {
        string tool = Str(step, "tool") ?? throw new ArgumentException("variant step needs tool");
        string command = tool.StartsWith("solidworks_", StringComparison.Ordinal) ? tool["solidworks_".Length..] : tool;
        if (command is "try_variants" or "save_document" or "close_document" or "new_document" or "export")
        {
            throw WorkerException.Validation("STEP_NOT_ALLOWED", $"{tool} cannot run inside a variant.", new Dictionary<string, object?>());
        }

        if (!CommandRegistry.TryGetValue(command, out Func<JsonElement?, object>? handler))
        {
            throw WorkerException.Validation("UNKNOWN_TOOL", $"Unknown tool in variant step: {tool}", new Dictionary<string, object?>());
        }

        JsonObject stepArgs = Prop(step, "args") is JsonElement a ? JsonNode.Parse(a.GetRawText())!.AsObject() : new JsonObject();
        stepArgs["path"] = path;
        stepArgs.TryAdd("units", units);
        stepArgs.TryAdd("confirm", true);
        handler(JsonSerializer.SerializeToElement(stepArgs));
    }

    /// <summary>Deletes every feature after the marker (newest first) and reverts changed dimensions.</summary>
    private static void UndoVariant(ModelDoc2 doc, string marker, Dictionary<string, double> reverts, string path, string units)
    {
        EnsureNoActiveSketch(doc);
        TryVoid(() => doc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, ""));
        var tree = FeatureTree(doc).Select(f => Try(() => f.Name) as string ?? "").ToList();
        int index = tree.IndexOf(marker);
        if (index >= 0)
        {
            foreach (string name in tree.Skip(index + 1).Reverse())
            {
                if (FindFeatureByName(doc, name) is not null)
                {
                    TryVoid(() => DeleteFeatureByName(doc, name));
                }
            }
        }

        if (reverts.Count > 0)
        {
            var call = new Dictionary<string, object?> { ["path"] = path, ["units"] = units, ["values"] = reverts, ["rollback_on_error"] = false };
            TryVoid(() => SetDimensions(JsonSerializer.SerializeToElement(call)));
        }

        TryVoid(() => doc.ForceRebuild3(false));
    }
}
