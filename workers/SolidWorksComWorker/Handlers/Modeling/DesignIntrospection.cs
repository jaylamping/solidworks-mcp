using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Reading and editing existing designs: what drives each feature, what each sketch
// contains, and safe batch dimension edits.
internal static partial class Program
{
    private static IEnumerable<Feature> FeatureTree(ModelDoc2 doc)
    {
        object? cursor = Try(() => doc.FirstFeature());
        int guard = 0;
        while (cursor is Feature feature && guard++ < 5000)
        {
            yield return feature;
            cursor = Try(() => feature.GetNextFeature());
        }
    }

    private static IEnumerable<Feature> SubFeatures(Feature feature)
    {
        object? cursor = Try(() => feature.GetFirstSubFeature());
        int guard = 0;
        while (cursor is Feature sub && guard++ < 1000)
        {
            yield return sub;
            cursor = Try(() => sub.GetNextSubFeature());
        }
    }

    /// <summary>Driving dimensions owned by a feature, with values in the call's units (angles in degrees).</summary>
    private static List<object> FeatureDimensions(Feature feature, double scale)
    {
        var output = new List<object>();
        object? dd = Try(() => feature.GetFirstDisplayDimension());
        int guard = 0;
        while (dd is DisplayDimension display && guard++ < 500)
        {
            Dimension? dim = Try(() => display.GetDimension2(0)) as Dimension;
            if (dim is not null)
            {
                double value = (Try(() => ((double[])dim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]) as double?)
                    ?? (Try(() => dim.SystemValue) as double?) ?? 0;
                bool angular = (Try(() => dim.GetType()) as int?) == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular;
                string fullName = Try(() => dim.FullName) as string ?? "?";
                output.Add(new
                {
                    name = TrimDocSuffix(fullName),
                    value = angular ? Math.Round(value * 180 / Math.PI, 4) : Math.Round(value / scale, 5),
                    unit = angular ? "deg" : UnitsName(scale),
                });
            }

            dd = Try(() => feature.GetNextDisplayDimension(display));
        }

        return output;
    }

    private static string TrimDocSuffix(string fullName)
    {
        // "D1@Boss-Extrude1@part.SLDPRT" -> "D1@Boss-Extrude1"
        string[] parts = fullName.Split('@');
        return parts.Length >= 3 ? $"{parts[0]}@{parts[1]}" : fullName;
    }

    private static string UnitsName(double scale) => scale switch { 0.001 => "mm", 1.0 => "m", 0.01 => "cm", 0.0254 => "in", _ => "units" };

    private static object DescribeSketch(Feature feature, double scale, int maxEntities)
    {
        ISketch? sketch = Try(() => feature.GetSpecificFeature2()) as ISketch;
        if (sketch is null)
        {
            return new { };
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = new Dictionary<string, long>();
        void Mark(string label)
        {
            t[label] = sw.ElapsedMilliseconds;
            sw.Restart();
        }

        double P(double v) => Math.Round(v / scale, 4);
        // Batch reads: one COM call per entity type instead of ~13 per segment (a 50-arc sketch
        // went from ~19 s to milliseconds). Construction geometry uses the centerline line style.
        const int centerStyle = (int)swLineStyles_e.swLineCENTER;
        var entities = new List<object>();
        double[] lines = Try(() => sketch.GetLines2(1)) as double[] ?? Array.Empty<double>();
        for (int i = 0; i + 11 < lines.Length && entities.Count < maxEntities; i += 12)
        {
            bool construction = (int)lines[i + 2] == centerStyle;
            entities.Add(new
            {
                type = construction ? "centerline" : "line",
                from = new[] { P(lines[i + 6]), P(lines[i + 7]) },
                to = new[] { P(lines[i + 9]), P(lines[i + 10]) },
            });
        }

        double[] arcs = Try(() => sketch.GetArcs2()) as double[] ?? Array.Empty<double>();
        for (int i = 0; i + 15 < arcs.Length && entities.Count < maxEntities; i += 16)
        {
            bool construction = (int)arcs[i + 2] == centerStyle;
            double[] start = { arcs[i + 6], arcs[i + 7] };
            double[] end = { arcs[i + 9], arcs[i + 10] };
            double[] center = { arcs[i + 12], arcs[i + 13] };
            double radius = Math.Sqrt((start[0] - center[0]) * (start[0] - center[0]) + (start[1] - center[1]) * (start[1] - center[1]));
            bool closed = Math.Abs(start[0] - end[0]) < 1e-10 && Math.Abs(start[1] - end[1]) < 1e-10;
            entities.Add(closed
                ? new { type = "circle", center = new[] { P(center[0]), P(center[1]) }, diameter = P(radius * 2), construction } as object
                : new
                {
                    type = "arc",
                    center = new[] { P(center[0]), P(center[1]) },
                    start = new[] { P(start[0]), P(start[1]) },
                    end = new[] { P(end[0]), P(end[1]) },
                    radius = P(radius),
                    clockwise = (int)arcs[i + 15] == -1,
                    construction,
                });
        }

        int splineCount = (Try(() => { int pointCount = 0; return sketch.GetSplineCount(ref pointCount); }) as int?) ?? 0;
        int ellipseCount = (Try(() => sketch.GetEllipseCount()) as int?) ?? 0;
        if (splineCount > 0)
        {
            entities.Add(new { type = "spline", count = splineCount });
        }

        if (ellipseCount > 0)
        {
            entities.Add(new { type = "ellipse", count = ellipseCount });
        }

        int segmentTotal = lines.Length / 12 + arcs.Length / 16 + splineCount + ellipseCount;
        Mark("segments");
        int userPoints = (Try(() => sketch.GetUserPointsCount()) as int?) ?? 0;
        var points = userPoints == 0
            ? new List<double[]>()
            : (Try(() => sketch.GetSketchPoints2()) as object[] ?? Array.Empty<object>()).OfType<SketchPoint>()
                .Where(p => (Try(() => p.Type) as int?) == (int)swSketchPointType_e.swSketchPointType_User)
                .Take(maxEntities)
                .Select(p => new[] { P(p.X), P(p.Y) })
                .ToList();

        Mark("points");
        int status = (Try(() => sketch.GetConstrainedStatus()) as int?) ?? 0;
        Mark("constrained");
        object? regions = Try(() => sketch.GetSketchRegionCount());
        Mark("regions");
        object? relations = Try(() => sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll));
        Mark("relations");
        return new
        {
            constrained = Enum.IsDefined(typeof(swConstrainedStatus_e), status) ? ((swConstrainedStatus_e)status).ToString().Replace("sw", "").Replace("Constrained", "_constrained").TrimStart('_').ToLowerInvariant() : status.ToString(),
            regions,
            segmentCount = segmentTotal,
            relations,
            timingsMs = ProfileTimings ? t : null,
            entities,
            points = points.Count > 0 ? points : null,
            truncated = segmentTotal > maxEntities ? true : (bool?)null,
        };
    }

    [ThreadStatic]
    private static bool ProfileTimings;

    /// <summary>
    /// Suspends graphics updates and FeatureManager refreshes while walking a large tree;
    /// SolidWorks otherwise repaints between COM calls. Restored on dispose.
    /// </summary>
    private static IDisposable QuietDocument(ModelDoc2 doc)
    {
        ModelView? view = Try(() => doc.ActiveView) as ModelView;
        bool graphics = (Try(() => view?.EnableGraphicsUpdate) as bool?) ?? true;
        bool tree = (Try(() => doc.FeatureManager.EnableFeatureTree) as bool?) ?? true;
        TryVoid(() => { if (view is not null) view.EnableGraphicsUpdate = false; });
        TryVoid(() => doc.FeatureManager.EnableFeatureTree = false);
        return new Restore(() =>
        {
            TryVoid(() => doc.FeatureManager.EnableFeatureTree = tree);
            TryVoid(() => { if (view is not null) view.EnableGraphicsUpdate = graphics; });
        });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static object FeatureDetails(JsonElement? args)
    {
        ProfileTimings = BoolArg(args, "profile");
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "feature_details");
        double s = UnitScale(args);
        string[] only = NamesArg(args, "features");
        bool includeSketches = BoolArg(args, "include_sketches", true);
        int maxEntities = (int)DoubleArg(args, "max_entities", 200);
        var wanted = new HashSet<string>(only, StringComparer.OrdinalIgnoreCase);

        var output = new List<object>();
        var started = System.Diagnostics.Stopwatch.StartNew();
        using (QuietDocument(doc))
        {
            foreach (Feature feature in FeatureTree(doc))
            {
                string type = Try(() => feature.GetTypeName2()) as string ?? "?";
                string name = Try(() => feature.Name) as string ?? "?";
                if (HousekeepingFeatureTypes.Contains(type) || type is "OriginProfileFeature")
                {
                    continue;
                }

                if (wanted.Count > 0 && !wanted.Contains(name))
                {
                    continue;
                }

                output.Add(DescribeFeatureDeep(doc, feature, s, includeSketches, maxEntities));
            }
        }

        if (ProfileTimings)
        {
            output.Add(new { totalMs = started.ElapsedMilliseconds });
        }

        return new { document = DescribeDocument(doc), units = UnitsLabel(args), features = output };
    }

    private static object DescribeFeatureDeep(ModelDoc2 doc, Feature feature, double s, bool includeSketches, int maxEntities)
    {
        string type = Try(() => feature.GetTypeName2()) as string ?? "?";
        bool warning = false;
        int code = (Try(() => feature.GetErrorCode2(out warning)) as int?) ?? 0;
        bool isSketch = type is "ProfileFeature" or "3DProfileFeature";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var timings = new Dictionary<string, long>();
        T Timed<T>(string label, Func<T> f)
        {
            long t0 = timer.ElapsedMilliseconds;
            T value = f();
            timings[label] = timer.ElapsedMilliseconds - t0;
            return value;
        }

        var subSketches = Timed("subFeatures", () => SubFeatures(feature).Where(f => (Try(() => f.GetTypeName2()) as string) is "ProfileFeature" or "3DProfileFeature").ToList());
        var dimensions = Timed("dimensions", () => FeatureDimensions(feature, s));
        var definition = Timed("definition", () => DescribeDefinition(doc, feature, type, s));
        var sketch = Timed("sketch", () => isSketch && includeSketches ? DescribeSketch(feature, s, maxEntities) : null);
        var parents = Timed("parents", () => (Try(() => feature.GetParents()) as object[])?.OfType<Feature>().Select(f => Try(() => f.Name) as string).ToArray());
        return new
        {
            name = Try(() => feature.Name),
            type,
            suppressed = (Try(() => feature.IsSuppressed()) as bool?) == true ? true : (bool?)null,
            error = code == 0 ? null : DescribeFeatureError(code),
            warning = code != 0 && warning ? true : (bool?)null,
            dimensions,
            definition,
            sketch,
            consumes = subSketches.Count > 0 ? subSketches.Select(f => Try(() => f.Name)).ToArray() : null,
            parents,
            timingsMs = ProfileTimings ? timings : null,
        };
    }

    // Feature types DescribeDefinition knows; GetDefinition costs a slow cross-process call, so skip the rest.
    private static readonly HashSet<string> DefinitionTypes = new(StringComparer.Ordinal)
    {
        "Extrusion", "Boss", "Cut", "ICE", "BossThin", "CutThin", "Revolution", "RevCut", "Shell", "Fillet",
        "LPattern", "CirPattern", "RefPlane",
    };

    /// <summary>Feature-type specific parameters that are not exposed as dimensions.</summary>
    private static object? DescribeDefinition(ModelDoc2 doc, Feature feature, string type, double s)
    {
        if (!DefinitionTypes.Contains(type))
        {
            return null;
        }

        object? def = Try(() => feature.GetDefinition());
        switch (def)
        {
            case ExtrudeFeatureData2 ex:
                return new
                {
                    kind = "extrude",
                    endCondition = EndConditionName(Try(() => ex.GetEndCondition(true)) as int?),
                    depth = Try(() => Math.Round(ex.GetDepth(true) / s, 4)),
                    bothDirections = Try(() => ex.BothDirections),
                    endCondition2 = (Try(() => ex.BothDirections) as bool?) == true ? EndConditionName(Try(() => ex.GetEndCondition(false)) as int?) : null,
                    depth2 = (Try(() => ex.BothDirections) as bool?) == true ? Try(() => Math.Round(ex.GetDepth(false) / s, 4)) : null,
                    reverse = Try(() => ex.ReverseDirection),
                    draft = (Try(() => ex.GetDraftWhileExtruding(true)) as bool?) == true ? Try(() => Math.Round(ex.GetDraftAngle(true) * 180 / Math.PI, 3)) : null,
                    merge = Try(() => ex.Merge),
                    thin = (Try(() => ex.IsThinFeature()) as bool?) == true ? Try(() => Math.Round(ex.GetWallThickness(true) / s, 4)) : null,
                };
            case RevolveFeatureData2 rv:
                return new
                {
                    kind = "revolve",
                    angle = Try(() => Math.Round(rv.GetRevolutionAngle(true) * 180 / Math.PI, 3)),
                    type = Try(() => rv.Type),
                    isCut = type.Contains("Cut", StringComparison.OrdinalIgnoreCase),
                };
            case ShellFeatureData sh:
                return new { kind = "shell", thickness = Try(() => Math.Round(sh.Thickness / s, 4)), outward = Try(() => sh.Direction), facesRemoved = Try(() => sh.FacesRemovedCount) };
            case SimpleFilletFeatureData2 fl:
                return new { kind = "fillet", radius = Try(() => Math.Round(fl.DefaultRadius / s, 4)), edges = Try(() => fl.GetEdgeCount()) };
            case LinearPatternFeatureData lp:
                return new
                {
                    kind = "linear_pattern",
                    count = Try(() => lp.D1TotalInstances),
                    spacing = Try(() => Math.Round(lp.D1Spacing / s, 4)),
                    count2 = Try(() => lp.D2TotalInstances),
                    spacing2 = Try(() => Math.Round(lp.D2Spacing / s, 4)),
                };
            case CircularPatternFeatureData cp:
                return new
                {
                    kind = "circular_pattern",
                    count = Try(() => cp.TotalInstances),
                    spacing = Try(() => Math.Round(cp.Spacing * 180 / Math.PI, 3)),
                    equalSpacing = Try(() => cp.EqualSpacing),
                };
            case RefPlaneFeatureData rp:
                return new { kind = "ref_plane", type = Try(() => rp.Type) };
            default:
                return null;
        }
    }

    private static string? EndConditionName(int? code) => code is null ? null : code.Value switch
    {
        (int)swEndConditions_e.swEndCondBlind => "blind",
        (int)swEndConditions_e.swEndCondThroughAll => "through_all",
        (int)swEndConditions_e.swEndCondUpToNext => "up_to_next",
        (int)swEndConditions_e.swEndCondUpToSurface => "up_to_face",
        (int)swEndConditions_e.swEndCondOffsetFromSurface => "offset_from_face",
        (int)swEndConditions_e.swEndCondMidPlane => "mid_plane",
        (int)swEndConditions_e.swEndCondUpToVertex => "up_to_vertex",
        (int)swEndConditions_e.swEndCondUpToBody => "up_to_body",
        (int)swEndConditions_e.swEndCondThroughAllBoth => "through_all_both",
        _ => $"code {code}",
    };

    /// <summary>Batch dimension edit with rebuild validation; reverts every change if the part breaks.</summary>
    private static object SetDimensions(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "set_dimensions");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        JsonElement values = Prop(args, "values") ?? throw new ArgumentException("values is required");
        if (values.ValueKind != JsonValueKind.Object)
        {
            throw WorkerException.Validation("BAD_VALUES", "values must be an object of {\"D1@Feature\": number}.", new Dictionary<string, object?>());
        }

        string title = doc.GetTitle();
        var applied = new List<(Dimension Dim, double Old, string Name, double New, bool Angular)>();
        foreach (JsonProperty prop in values.EnumerateObject())
        {
            string name = prop.Name.Contains('@') ? prop.Name : throw WorkerException.Validation("BAD_DIMENSION_NAME", $"Use full names like D1@Boss-Extrude1 (got {prop.Name}).", new Dictionary<string, object?>());
            Dimension? dim = (Try(() => doc.Parameter(name)) as Dimension) ?? (Try(() => doc.Parameter($"{name}@{title}")) as Dimension);
            if (dim is null)
            {
                RevertDimensions(doc, applied);
                throw WorkerException.Validation("DIMENSION_NOT_FOUND", $"Dimension not found: {name}", new Dictionary<string, object?> { ["dimension"] = name },
                    ["List dimensions with solidworks_feature_details."]);
            }

            bool angular = (Try(() => dim.GetType()) as int?) == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular;
            double old = ((double[])dim.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0];
            double next = angular ? Deg(prop.Value.GetDouble()) : prop.Value.GetDouble() * s;
            int rc = dim.SetSystemValue3(next, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
            applied.Add((dim, old, name, next, angular));
            if (rc != (int)swSetValueReturnStatus_e.swSetValue_Successful)
            {
                RevertDimensions(doc, applied);
                throw WorkerException.Validation("DIMENSION_SET_FAILED", $"SolidWorks rejected {name} (status {rc}); all changes reverted.", new Dictionary<string, object?> { ["dimension"] = name });
            }
        }

        TryVoid(() => doc.ForceRebuild3(false));
        var errors = FeatureTree(doc)
            .Select(f => (f, code: (Try(() => f.GetErrorCode2(out bool _)) as int?) ?? 0))
            .Where(x => x.code != 0)
            .Select(x => new { feature = Try(() => x.f.Name), error = DescribeFeatureError(x.code) })
            .ToList();
        if (errors.Count > 0 && BoolArg(args, "rollback_on_error", true))
        {
            RevertDimensions(doc, applied);
            throw WorkerException.Worker("DIMENSION_REBUILD_ERROR", "The new values break the part; all changes were reverted.",
                new Dictionary<string, object?> { ["errors"] = errors, ["attempted"] = applied.Select(a => a.Name).ToArray() });
        }

        return new
        {
            document = DescribeDocument(doc),
            changed = applied.Select(a => new
            {
                dimension = a.Name,
                from = a.Angular ? Math.Round(a.Old * 180 / Math.PI, 4) : Math.Round(a.Old / s, 5),
                to = a.Angular ? Math.Round(a.New * 180 / Math.PI, 4) : Math.Round(a.New / s, 5),
            }),
            rebuildErrors = errors,
            part = PartGeometrySummary(doc, s),
        };
    }

    private static void RevertDimensions(ModelDoc2 doc, List<(Dimension Dim, double Old, string Name, double New, bool Angular)> applied)
    {
        foreach (var a in Enumerable.Reverse(applied))
        {
            TryVoid(() => a.Dim.SetSystemValue3(a.Old, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null));
        }

        TryVoid(() => doc.ForceRebuild3(false));
    }
}
