using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// One-shot sketch builder: open a sketch on a plane/face (or reopen one), add a
// batch of entities with inference disabled, close it, and report what it holds.
internal static partial class Program
{
    private sealed class SketchSpace
    {
        public required MathUtility Math { get; init; }
        public required MathTransform ModelToSketch { get; init; }
        public required MathTransform SketchToModel { get; init; }
        public required bool ModelCoords { get; init; }
        public required double Scale { get; init; }

        /// <summary>Converts an input point (user units, sketch or model space) to sketch meters.</summary>
        public double[] P(double[] input)
        {
            if (!ModelCoords)
            {
                return new[] { input[0] * Scale, input[1] * Scale, 0.0 };
            }

            double[] model = { input[0] * Scale, input[1] * Scale, (input.Length > 2 ? input[2] : 0) * Scale };
            MathPoint mp = (MathPoint)Math.CreatePoint(model);
            double[] s = (double[])((MathPoint)mp.MultiplyTransform(ModelToSketch)).ArrayData;
            return new[] { s[0], s[1], 0.0 };
        }

        public double[] ToModel(double[] sketchMeters)
        {
            MathPoint mp = (MathPoint)Math.CreatePoint(sketchMeters);
            return (double[])((MathPoint)mp.MultiplyTransform(SketchToModel)).ArrayData;
        }
    }

    private static object Sketch(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "sketch");
        double scale = UnitScale(args);
        EnsureNoActiveSketch(doc);

        string? editName = StringArg(args, "edit_sketch");
        string target;
        doc.ClearSelection2(true);
        if (!string.IsNullOrWhiteSpace(editName))
        {
            SelectSketchByName(doc, editName, append: false, mark: 0);
            target = $"sketch:{editName}";
            TryVoid(() => doc.EditSketch());
            if (doc.SketchManager.ActiveSketch is null)
            {
                throw WorkerException.Worker("SKETCH_EDIT_FAILED", $"Could not reopen sketch {editName}.", new Dictionary<string, object?>());
            }
        }
        else
        {
            JsonElement on = Prop(args, "on") ?? throw WorkerException.Validation(
                "MISSING_SKETCH_TARGET",
                "Provide 'on' (plane name or face selector) or 'edit_sketch'.",
                new Dictionary<string, object?>());
            ResolvedEntity entity = ResolveSingle(doc, on, scale, "on");
            if (entity.Kind is not ("plane" or "face" or "feature"))
            {
                throw WorkerException.Validation("BAD_SKETCH_TARGET", $"Cannot sketch on a {entity.Kind}.", new Dictionary<string, object?> { ["target"] = entity.Info });
            }

            SelectResolved(doc, new[] { entity }, 0, append: false, "on");
            target = JsonSerializer.Serialize(entity.Info, WriteJsonCompact);
            doc.SketchManager.InsertSketch(true);
        }

        ISketch? sketch = doc.SketchManager.ActiveSketch as ISketch;
        if (sketch is null)
        {
            doc.ClearSelection2(true);
            throw WorkerException.Worker("SKETCH_OPEN_FAILED", "SolidWorks did not open a sketch on the target.", new Dictionary<string, object?> { ["target"] = target },
                ["Sketch targets must be planes or planar faces."]);
        }

        string sketchName = ((Feature)sketch).Name;
        MathUtility math = (MathUtility)app.GetMathUtility();
        MathTransform m2s = (MathTransform)sketch.ModelToSketchTransform;
        var space = new SketchSpace
        {
            Math = math,
            ModelToSketch = m2s,
            SketchToModel = (MathTransform)m2s.Inverse(),
            ModelCoords = string.Equals(StringArg(args, "space"), "model", StringComparison.OrdinalIgnoreCase),
            Scale = scale,
        };

        SketchManager sm = doc.SketchManager;
        bool oldAddToDb = sm.AddToDB;
        bool oldDisplay = sm.DisplayWhenAdded;
        sm.AddToDB = true;
        sm.DisplayWhenAdded = false;

        var results = new List<object>();
        var failures = new List<object>();
        var removed = new List<object>();
        try
        {
            // Edit mode: delete the entities closest to the given points before adding new ones.
            if (Prop(args, "remove") is JsonElement removeList)
            {
                foreach (JsonElement r in removeList.EnumerateArray())
                {
                    double[] at = space.P(VecProp(r, "near"));
                    (SketchSegment? seg, double d, string kind) = NearestSketchSegment(sketch, at);
                    double maxDistance = (OptNum(r, "max_distance") ?? 1.0) * scale;
                    if (seg is null || d > maxDistance)
                    {
                        throw WorkerException.Validation("SKETCH_ENTITY_NOT_FOUND",
                            $"No sketch entity within {maxDistance / scale} of {string.Join(",", Vec(Prop(r, "near")!.Value))} (closest {(seg is null ? "none" : Math.Round(d / scale, 3).ToString())}).",
                            new Dictionary<string, object?>(), ["Use solidworks_feature_details on the sketch to see entity coordinates (sketch space)."]);
                    }

                    doc.ClearSelection2(true);
                    seg.Select4(false, NewSelectData(doc, 0));
                    bool ok = doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    removed.Add(new { kind, distance = Math.Round(d / scale, 4), deleted = ok });
                }

                doc.ClearSelection2(true);
            }

            JsonElement entities = Prop(args, "entities") ?? default;
            if (entities.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement e in entities.EnumerateArray())
                {
                    string type = (Str(e, "type") ?? "?").ToLowerInvariant();
                    try
                    {
                        int created = AddSketchEntity(doc, sm, space, e, type);
                        results.Add(new { index, type, segments = created });
                        if (created == 0)
                        {
                            failures.Add(new { index, type, reason = "API returned nothing", spec = Truncate(e.GetRawText(), 200) });
                        }
                    }
                    catch (WorkerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new { index, type, reason = ex.Message, spec = Truncate(e.GetRawText(), 200) });
                    }

                    index++;
                }
            }
        }
        finally
        {
            sm.AddToDB = oldAddToDb;
            sm.DisplayWhenAdded = oldDisplay;
        }

        int regions = (Try(() => sketch.GetSketchRegionCount()) as int?) ?? -1;
        int contours = (Try(() => (sketch.GetSketchContours() as object[])?.Length) as int?) ?? -1;
        int segmentCount = (Try(() => (sketch.GetSketchSegments() as object[])?.Length) as int?) ?? -1;
        sm.InsertSketch(true);
        doc.ClearSelection2(true);

        string? rename = StringArg(args, "name");
        Feature sketchFeature = (Feature)sketch;
        if (!string.IsNullOrWhiteSpace(rename) && string.IsNullOrWhiteSpace(editName))
        {
            TryVoid(() => sketchFeature.Name = rename);
            sketchName = sketchFeature.Name;
        }

        if (failures.Count > 0 && BoolArg(args, "atomic", defaultValue: true))
        {
            if (string.IsNullOrWhiteSpace(editName))
            {
                TryVoid(() => DeleteFeatureByName(doc, sketchName));
            }

            throw WorkerException.Validation(
                "SKETCH_ENTITY_FAILED",
                $"{failures.Count} sketch entit{(failures.Count == 1 ? "y" : "ies")} could not be created; " + (string.IsNullOrWhiteSpace(editName) ? "the sketch was deleted." : "the sketch was left as-is."),
                new Dictionary<string, object?> { ["failures"] = failures, ["created"] = results },
                ["Fix the failing entity or pass atomic:false to keep the partial sketch."]);
        }

        double[] origin = space.ToModel(new[] { 0.0, 0.0, 0.0 });
        double[] xTip = space.ToModel(new[] { 1.0, 0.0, 0.0 });
        double[] yTip = space.ToModel(new[] { 0.0, 1.0, 0.0 });
        double[] zTip = space.ToModel(new[] { 0.0, 0.0, 1.0 });
        return new
        {
            document = DescribeDocument(doc),
            sketchName,
            target,
            units = UnitsLabel(args),
            entities = results,
            removed = removed.Count > 0 ? removed : null,
            failures,
            downstreamErrors = string.IsNullOrWhiteSpace(editName) ? null : WhatsWrong(doc),
            segmentCount,
            contourCount = contours,
            regionCount = regions,
            sketchFrameInModel = new
            {
                origin = Round(origin, scale),
                xAxis = Normalize3(Sub(xTip, origin)).Select(x => Math.Round(x, 5)).ToArray(),
                yAxis = Normalize3(Sub(yTip, origin)).Select(x => Math.Round(x, 5)).ToArray(),
                normal = Normalize3(Sub(zTip, origin)).Select(x => Math.Round(x, 5)).ToArray(),
            },
            hint = regions == 0 ? "No closed regions: this sketch can't drive a boss/cut (fine for paths, axes, construction)." : null,
        };
    }

    private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

    /// <summary>Closest line/arc/circle segment of the active sketch to a sketch-space point.</summary>
    private static (SketchSegment? Segment, double Distance, string Kind) NearestSketchSegment(ISketch sketch, double[] p)
    {
        SketchSegment? best = null;
        double bestD = double.MaxValue;
        string kind = "";
        foreach (SketchSegment seg in (sketch.GetSketchSegments() as object[] ?? Array.Empty<object>()).OfType<SketchSegment>())
        {
            double d = double.MaxValue;
            string k = "other";
            if (seg is SketchLine line && line.GetStartPoint2() is SketchPoint a && line.GetEndPoint2() is SketchPoint b)
            {
                double[] ab = { b.X - a.X, b.Y - a.Y };
                double len2 = ab[0] * ab[0] + ab[1] * ab[1];
                double t = len2 < 1e-18 ? 0 : Math.Clamp(((p[0] - a.X) * ab[0] + (p[1] - a.Y) * ab[1]) / len2, 0, 1);
                d = Math.Sqrt(Math.Pow(p[0] - (a.X + t * ab[0]), 2) + Math.Pow(p[1] - (a.Y + t * ab[1]), 2));
                k = "line";
            }
            else if (seg is SketchArc arc && arc.GetCenterPoint2() is SketchPoint c)
            {
                double rr = arc.GetRadius();
                double dc = Math.Sqrt(Math.Pow(p[0] - c.X, 2) + Math.Pow(p[1] - c.Y, 2));
                d = Math.Abs(dc - rr);
                k = arc.IsCircle() == 1 ? "circle" : "arc";
                if (k == "arc" && arc.GetStartPoint2() is SketchPoint s0 && arc.GetEndPoint2() is SketchPoint s1)
                {
                    // Off the arc's span: distance to the nearer end point instead.
                    double a0 = Math.Atan2(s0.Y - c.Y, s0.X - c.X), a1 = Math.Atan2(s1.Y - c.Y, s1.X - c.X), ap = Math.Atan2(p[1] - c.Y, p[0] - c.X);
                    if (arc.GetRotationDir() == -1)
                    {
                        (a0, a1) = (a1, a0);
                    }

                    double span = (a1 - a0 + 4 * Math.PI) % (2 * Math.PI);
                    double rel = (ap - a0 + 4 * Math.PI) % (2 * Math.PI);
                    if (rel > span)
                    {
                        d = Math.Min(Math.Sqrt(Math.Pow(p[0] - s0.X, 2) + Math.Pow(p[1] - s0.Y, 2)), Math.Sqrt(Math.Pow(p[0] - s1.X, 2) + Math.Pow(p[1] - s1.Y, 2)));
                    }
                }
            }

            if (d < bestD)
            {
                bestD = d;
                best = seg;
                kind = k;
            }
        }

        return (best, bestD, kind);
    }

    /// <summary>Adds one entity; returns how many sketch segments/points it created.</summary>
    private static int AddSketchEntity(ModelDoc2 doc, SketchManager sm, SketchSpace s, JsonElement e, string type)
    {
        bool construction = Flag(e, "construction");
        var created = new List<object?>();
        switch (type)
        {
            case "line":
            case "centerline":
            {
                double[] a = s.P(VecProp(e, "from"));
                double[] b = s.P(VecProp(e, "to"));
                created.Add(type == "centerline"
                    ? sm.CreateCenterLine(a[0], a[1], 0, b[0], b[1], 0)
                    : sm.CreateLine(a[0], a[1], 0, b[0], b[1], 0));
                break;
            }
            case "circle":
            {
                double[] c = s.P(VecProp(e, "center"));
                double r = (OptNum(e, "radius") ?? Num(e, "diameter") / 2) * s.Scale;
                created.Add(sm.CreateCircleByRadius(c[0], c[1], 0, r));
                break;
            }
            case "arc":
            {
                // Center + start + end, counterclockwise unless "clockwise": true.
                double[] c = s.P(VecProp(e, "center"));
                double[] a = s.P(VecProp(e, "start"));
                double[] b = s.P(VecProp(e, "end"));
                short dir = (short)(Flag(e, "clockwise") ? -1 : 1);
                created.Add(sm.CreateArc(c[0], c[1], 0, a[0], a[1], 0, b[0], b[1], 0, dir));
                break;
            }
            case "arc3":
            {
                double[] a = s.P(VecProp(e, "start"));
                double[] m = s.P(VecProp(e, "mid"));
                double[] b = s.P(VecProp(e, "end"));
                created.Add(sm.Create3PointArc(a[0], a[1], 0, b[0], b[1], 0, m[0], m[1], 0));
                break;
            }
            case "rectangle":
            {
                double[] a = s.P(VecProp(e, "corner1"));
                double[] b = s.P(VecProp(e, "corner2"));
                created.AddRange(AsArray(sm.CreateCornerRectangle(a[0], a[1], 0, b[0], b[1], 0)));
                break;
            }
            case "center_rectangle":
            {
                double[] c = s.P(VecProp(e, "center"));
                double w = Num(e, "width") * s.Scale;
                double h = Num(e, "height") * s.Scale;
                if (OptNum(e, "corner_radius") is double cr && cr > 0)
                {
                    created.AddRange(AddRoundedPolyline(sm, RectCorners(c, w, h), closed: true, cr * s.Scale));
                }
                else
                {
                    created.AddRange(AsArray(sm.CreateCenterRectangle(c[0], c[1], 0, c[0] + w / 2, c[1] + h / 2, 0)));
                }

                break;
            }
            case "polyline":
            case "polygon_points":
            {
                JsonElement pts = Prop(e, "points") ?? throw new ArgumentException("polyline needs points");
                var points = pts.EnumerateArray().Select(p => s.P(Vec(p))).ToList();
                bool closed = Flag(e, "closed", fallback: type == "polygon_points");
                double radius = (OptNum(e, "corner_radius") ?? 0) * s.Scale;
                created.AddRange(AddRoundedPolyline(sm, points, closed, radius));
                break;
            }
            case "slot":
            {
                double[] a = s.P(VecProp(e, "start"));
                double[] b = s.P(VecProp(e, "end"));
                double w = Num(e, "width") * s.Scale;
                // Straight center-to-center slot drawn as two lines + two end arcs so it is
                // independent of CreateSketchSlot's dimensioning quirks.
                created.AddRange(AddStraightSlot(sm, a, b, w));
                break;
            }
            case "polygon":
            {
                double[] c = s.P(VecProp(e, "center"));
                int sides = (int)Num(e, "sides");
                double r = Num(e, "radius") * s.Scale;
                double rot = Deg(OptNum(e, "rotation_deg") ?? 90);
                bool radiusIsVertex = !string.Equals(Str(e, "radius_is"), "inscribed", StringComparison.OrdinalIgnoreCase);
                // "radius" is the circumscribed (vertex) radius by default; radius_is:"inscribed" means flat-to-center.
                double vertexRadius = radiusIsVertex ? r : r / Math.Cos(Math.PI / sides);
                var points = Enumerable.Range(0, sides)
                    .Select(i => new[] { c[0] + vertexRadius * Math.Cos(rot + 2 * Math.PI * i / sides), c[1] + vertexRadius * Math.Sin(rot + 2 * Math.PI * i / sides), 0.0 })
                    .ToList();
                created.AddRange(AddRoundedPolyline(sm, points, closed: true, 0));
                break;
            }
            case "spline":
            {
                JsonElement pts = Prop(e, "points") ?? throw new ArgumentException("spline needs points");
                double[] flat = pts.EnumerateArray().SelectMany(p => s.P(Vec(p))).ToArray();
                created.Add(sm.CreateSpline2(flat, Flag(e, "natural_ends", fallback: true)));
                break;
            }
            case "point":
            {
                double[] p = s.P(VecProp(e, "at"));
                created.Add(sm.CreatePoint(p[0], p[1], 0));
                break;
            }
            case "ellipse":
            {
                double[] c = s.P(VecProp(e, "center"));
                double a = Num(e, "major_radius") * s.Scale;
                double b = Num(e, "minor_radius") * s.Scale;
                double rot = Deg(OptNum(e, "rotation_deg") ?? 0);
                created.Add(sm.CreateEllipse(
                    c[0], c[1], 0,
                    c[0] + a * Math.Cos(rot), c[1] + a * Math.Sin(rot), 0,
                    c[0] - b * Math.Sin(rot), c[1] + b * Math.Cos(rot), 0));
                break;
            }
            case "text":
            {
                double[] at = s.P(VecProp(e, "at"));
                string text = Str(e, "text") ?? throw new ArgumentException("text needs text");
                bool oldDb = sm.AddToDB;
                sm.AddToDB = false;
                SketchText? st = doc.InsertSketchText(at[0], at[1], 0, text, 0, 0, 0, 100, 100) as SketchText;
                sm.AddToDB = oldDb;
                if (st is not null && st.GetTextFormat() is TextFormat tf)
                {
                    tf.CharHeight = (OptNum(e, "height") ?? 5) * s.Scale;
                    if (Str(e, "font") is string font)
                    {
                        tf.TypeFaceName = font;
                    }

                    tf.Bold = Flag(e, "bold");
                    st.SetTextFormat(false, tf);
                }

                created.Add(st);
                break;
            }
            case "convert_edges":
            case "offset_edges":
            {
                // Project model edges (or a face's boundary) into the sketch, optionally offset.
                JsonElement sel = Prop(e, "from") ?? throw new ArgumentException($"{type} needs from (edge/face selector)");
                List<ResolvedEntity> targets = ResolveSelectors(doc, sel, s.Scale, type);
                bool oldDb = sm.AddToDB;
                sm.AddToDB = false;
                doc.ClearSelection2(true);
                SelectResolved(doc, targets, 0, append: false, type);
                ISketch? active = sm.ActiveSketch as ISketch;
                int before = (Try(() => (active?.GetSketchSegments() as object[])?.Length) as int?) ?? 0;
                bool ok = sm.SketchUseEdge3(false, Flag(e, "inner_loops", true));
                if (ok && type == "offset_edges")
                {
                    // SketchUseEdge3 leaves nothing selected: select the segments it just added, then offset them.
                    var added = (active?.GetSketchSegments() as object[] ?? Array.Empty<object>()).Skip(before).OfType<SketchSegment>().ToList();

                    // One or several offsets of the same projected edges (e.g. [-2.2, -3.4] = a lip ring).
                    JsonElement distEl = Prop(e, "distance") ?? throw new ArgumentException("offset_edges needs distance");
                    double[] distances = distEl.ValueKind == JsonValueKind.Array ? Vec(distEl, 1) : new[] { distEl.GetDouble() };
                    for (int i = 0; i < distances.Length && ok; i++)
                    {
                        doc.ClearSelection2(true);
                        bool append = false;
                        foreach (SketchSegment seg in added)
                        {
                            seg.Select4(append, NewSelectData(doc, 0));
                            append = true;
                        }

                        // Turn the projected originals into construction on the last pass so only offsets bound regions.
                        bool last = i == distances.Length - 1;
                        int makeConstruction = last && !Flag(e, "keep_original")
                            ? (int)swSkOffsetMakeConstructionType_e.swSkOffsetMakeOrigConstruction
                            : (int)swSkOffsetMakeConstructionType_e.swSkOffsetDontMakeConstruction;
                        ok = sm.SketchOffset2(distances[i] * s.Scale, false, true, (int)swSkOffsetCapEndType_e.swSkOffsetArcCaps, makeConstruction, false);
                    }
                }

                sm.AddToDB = oldDb;
                doc.ClearSelection2(true);
                created.Add(ok ? (object)true : null);
                break;
            }
            default:
                throw WorkerException.Validation(
                    "BAD_SKETCH_ENTITY",
                    $"Unknown sketch entity type '{type}'.",
                    new Dictionary<string, object?>(),
                    ["Types: line, centerline, circle, arc, arc3, rectangle, center_rectangle, polyline, slot, polygon, spline, point, ellipse, text, convert_edges, offset_edges."]);
        }

        int count = 0;
        foreach (object? item in created)
        {
            if (item is null)
            {
                continue;
            }

            count++;
            if (construction && item is SketchSegment seg)
            {
                TryVoid(() => seg.ConstructionGeometry = true);
            }
        }

        return count;
    }

    private static IEnumerable<object?> AsArray(object? value) => value is object[] arr ? arr : new[] { value };

    private static List<double[]> RectCorners(double[] c, double w, double h) => new()
    {
        new[] { c[0] - w / 2, c[1] - h / 2, 0.0 },
        new[] { c[0] + w / 2, c[1] - h / 2, 0.0 },
        new[] { c[0] + w / 2, c[1] + h / 2, 0.0 },
        new[] { c[0] - w / 2, c[1] + h / 2, 0.0 },
    };

    /// <summary>
    /// Draws a polyline, replacing each interior corner with a tangent arc of the given radius.
    /// Geometry is computed here so no sketch-fillet selection is needed.
    /// </summary>
    private static List<object?> AddRoundedPolyline(SketchManager sm, List<double[]> pts, bool closed, double radius)
    {
        var output = new List<object?>();
        int n = pts.Count;
        if (n < 2)
        {
            throw new ArgumentException("polyline needs at least 2 points");
        }

        if (closed && Dist3(pts[0], pts[n - 1]) < 1e-12)
        {
            pts = pts.Take(n - 1).ToList();
            n = pts.Count;
        }

        // For each vertex compute trimmed in/out points and an optional fillet arc.
        var inPt = new double[n][];
        var outPt = new double[n][];
        var arcs = new (double[] Center, double[] Start, double[] End, short Dir)?[n];
        for (int i = 0; i < n; i++)
        {
            inPt[i] = pts[i];
            outPt[i] = pts[i];
            bool interior = closed || (i > 0 && i < n - 1);
            if (!interior || radius <= 0)
            {
                continue;
            }

            double[] p = pts[i];
            double[] prev = pts[(i - 1 + n) % n];
            double[] next = pts[(i + 1) % n];
            double[] u = Normalize3(Sub(prev, p));
            double[] v = Normalize3(Sub(next, p));
            double cos = Math.Clamp(Dot3(u, v), -1, 1);
            double angle = Math.Acos(cos);
            if (angle < 1e-6 || Math.Abs(angle - Math.PI) < 1e-6)
            {
                continue; // collinear or reversing: no fillet
            }

            double tangentLength = radius / Math.Tan(angle / 2);
            double maxLength = Math.Min(Dist3(prev, p), Dist3(next, p)) / 2;
            if (tangentLength > maxLength + 1e-12)
            {
                throw new ArgumentException($"corner_radius too large for corner {i}");
            }

            double[] t1 = { p[0] + u[0] * tangentLength, p[1] + u[1] * tangentLength, 0 };
            double[] t2 = { p[0] + v[0] * tangentLength, p[1] + v[1] * tangentLength, 0 };
            double[] bis = Normalize3(new[] { u[0] + v[0], u[1] + v[1], 0 });
            double centerDist = radius / Math.Sin(angle / 2);
            double[] center = { p[0] + bis[0] * centerDist, p[1] + bis[1] * centerDist, 0 };
            double cross = (t1[0] - center[0]) * (t2[1] - center[1]) - (t1[1] - center[1]) * (t2[0] - center[0]);
            arcs[i] = (center, t1, t2, (short)(cross > 0 ? 1 : -1));
            inPt[i] = t1;
            outPt[i] = t2;
        }

        int segments = closed ? n : n - 1;
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % n;
            double[] a = outPt[i];
            double[] b = inPt[j];
            if (Dist3(a, b) > 1e-12)
            {
                output.Add(sm.CreateLine(a[0], a[1], 0, b[0], b[1], 0));
            }
        }

        foreach (var arc in arcs)
        {
            if (arc is { } a)
            {
                output.Add(sm.CreateArc(a.Center[0], a.Center[1], 0, a.Start[0], a.Start[1], 0, a.End[0], a.End[1], 0, a.Dir));
            }
        }

        return output;
    }

    private static List<object?> AddStraightSlot(SketchManager sm, double[] a, double[] b, double width)
    {
        double r = width / 2;
        double[] d = Normalize3(Sub(b, a));
        double[] nrm = { -d[1], d[0], 0 };
        double[] a1 = { a[0] + nrm[0] * r, a[1] + nrm[1] * r, 0 };
        double[] a2 = { a[0] - nrm[0] * r, a[1] - nrm[1] * r, 0 };
        double[] b1 = { b[0] + nrm[0] * r, b[1] + nrm[1] * r, 0 };
        double[] b2 = { b[0] - nrm[0] * r, b[1] - nrm[1] * r, 0 };
        return new List<object?>
        {
            sm.CreateLine(a1[0], a1[1], 0, b1[0], b1[1], 0),
            sm.CreateLine(a2[0], a2[1], 0, b2[0], b2[1], 0),
            // End caps: semicircles bulging away from the slot body.
            sm.CreateArc(b[0], b[1], 0, b2[0], b2[1], 0, b1[0], b1[1], 0, 1),
            sm.CreateArc(a[0], a[1], 0, a1[0], a1[1], 0, a2[0], a2[1], 0, 1),
        };
    }
}
