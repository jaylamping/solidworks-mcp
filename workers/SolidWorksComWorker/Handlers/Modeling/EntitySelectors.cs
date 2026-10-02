using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

/// <summary>
/// One resolved selection target. Select() puts it on the SolidWorks selection list.
/// </summary>
internal sealed class ResolvedEntity
{
    public required string Kind { get; init; }
    public required object Info { get; init; }
    public object? Com { get; init; }
    public required Func<ModelDoc2, bool, int, bool> Select { get; init; }
}

// Geometry-driven entity selectors so tools can target faces/edges/vertices by
// position instead of relying on whatever the user last clicked.
//
// Selector grammar (all coordinates are model space, in the call's `units`):
//   "Top Plane"                                   named plane/axis/sketch/feature/point
//   {"plane"|"axis"|"sketch"|"feature"|"point"|"coord_sys"|"body": "<name>"}
//   {"face":   {"near":[x,y,z], "normal":[nx,ny,nz]?, "type":"plane|cylinder|cone|sphere|torus|any"?, "max_distance":d?}}
//   {"edge":   {"near":[x,y,z], "direction":[dx,dy,dz]?, "type":"line|circle|any"?, "radius":r?, "max_distance":d?}}
//   {"vertex": {"near":[x,y,z]}}
//   {"faces":  {filters…}}   every face matching all filters
//   {"edges":  {filters…}}   every edge matching all filters
// Filters for faces/edges: type, normal (faces), direction (line edges), radius (circular),
//   box:[xmin,ymin,zmin,xmax,ymax,zmax] (entity midpoint inside), of_feature:"Boss-Extrude1",
//   on_face:{face selector body} (edges bounding that face), body:"<body name>".
internal static partial class Program
{
    private const double DirectionDotTolerance = 0.9995;

    private static List<ResolvedEntity> ResolveSelectors(ModelDoc2 doc, JsonElement selectors, double scale, string argName)
    {
        var output = new List<ResolvedEntity>();
        if (selectors.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in selectors.EnumerateArray())
            {
                output.AddRange(ResolveSelector(doc, item, scale, argName));
            }
        }
        else
        {
            output.AddRange(ResolveSelector(doc, selectors, scale, argName));
        }

        if (output.Count == 0)
        {
            throw WorkerException.Validation(
                "SELECTOR_EMPTY",
                $"'{argName}' did not match any entity.",
                new Dictionary<string, object?> { ["selector"] = Truncate(selectors.GetRawText(), 400) },
                ["Inspect faces/edges with solidworks_list_entities to pick coordinates."]);
        }

        return output;
    }

    private static ResolvedEntity ResolveSingle(ModelDoc2 doc, JsonElement selector, double scale, string argName)
    {
        List<ResolvedEntity> items = ResolveSelectors(doc, selector, scale, argName);
        if (items.Count != 1)
        {
            throw WorkerException.Validation(
                "SELECTOR_AMBIGUOUS",
                $"'{argName}' must resolve to exactly one entity but matched {items.Count}.",
                new Dictionary<string, object?> { ["matches"] = items.Select(i => i.Info).Take(10).ToArray() });
        }

        return items[0];
    }

    private static void SelectResolved(ModelDoc2 doc, IEnumerable<ResolvedEntity> items, int mark, bool append, string argName)
    {
        bool first = !append;
        foreach (ResolvedEntity item in items)
        {
            bool ok = item.Select(doc, !first, mark);
            first = false;
            if (!ok)
            {
                throw WorkerException.Validation(
                    "SELECT_FAILED",
                    $"Could not select {item.Kind} for '{argName}'.",
                    new Dictionary<string, object?> { ["entity"] = item.Info, ["mark"] = mark });
            }
        }
    }

    private static IEnumerable<ResolvedEntity> ResolveSelector(ModelDoc2 doc, JsonElement selector, double scale, string argName)
    {
        if (selector.ValueKind == JsonValueKind.String)
        {
            return new[] { ResolveNamed(doc, selector.GetString()!, null) };
        }

        if (selector.ValueKind != JsonValueKind.Object)
        {
            throw WorkerException.Validation("BAD_SELECTOR", $"Selector for '{argName}' must be a string or object.", new Dictionary<string, object?>());
        }

        foreach ((string key, string swType) in new[]
                 {
                     ("plane", "PLANE"), ("axis", "AXIS"), ("sketch", "SKETCH"), ("point", "DATUMPOINT"),
                     ("coord_sys", "COORDSYS"), ("feature", "BODYFEATURE"),
                 })
        {
            if (Str(selector, key) is string name)
            {
                return new[] { ResolveNamed(doc, name, swType) };
            }
        }

        if (Str(selector, "body") is string bodyName && !Has(selector, "face") && !Has(selector, "edge"))
        {
            return new[] { ResolveBody(doc, bodyName) };
        }

        if (Prop(selector, "face") is JsonElement face)
        {
            return new[] { NearestFace(doc, face, scale) };
        }

        if (Prop(selector, "edge") is JsonElement edge)
        {
            return new[] { NearestEdge(doc, edge, scale) };
        }

        if (Prop(selector, "vertex") is JsonElement vertex)
        {
            return new[] { NearestVertex(doc, vertex, scale) };
        }

        if (Prop(selector, "faces") is JsonElement faces)
        {
            return FilterFaces(doc, faces, scale).Select(f => FaceEntity(f, scale, null)).ToList();
        }

        if (Prop(selector, "edges") is JsonElement edges)
        {
            return FilterEdges(doc, edges, scale).Select(e => EdgeEntity(e, scale, null)).ToList();
        }

        throw WorkerException.Validation(
            "BAD_SELECTOR",
            $"Unrecognized selector for '{argName}': {Truncate(selector.GetRawText(), 200)}",
            new Dictionary<string, object?>(),
            ["Use a name string, or one of plane/axis/sketch/feature/point/body/face/edge/vertex/faces/edges."]);
    }

    // Feature type name -> SelectByID2 type, for named reference entities.
    private static readonly Dictionary<string, string> NamedSelectTypes = new(StringComparer.Ordinal)
    {
        ["RefPlane"] = "PLANE",
        ["RefAxis"] = "AXIS",
        ["ProfileFeature"] = "SKETCH",
        ["3DProfileFeature"] = "SKETCH",
        ["RefPoint"] = "DATUMPOINT",
        ["CoordSys"] = "COORDSYS",
        // Curve features must be selected as curves to act as sweep paths / guides.
        ["Helix"] = "REFERENCECURVES",
        ["CompositeCurve"] = "REFERENCECURVES",
        ["RefCurve"] = "REFERENCECURVES",
        ["CurveInFile"] = "REFERENCECURVES",
        ["CurveThroughFreePoints"] = "REFERENCECURVES",
    };

    /// <summary>
    /// Resolves a named entity through the feature tree. Must not touch the selection list:
    /// handlers resolve selectors while building multi-entity selections.
    /// </summary>
    private static ResolvedEntity ResolveNamed(ModelDoc2 doc, string name, string? swType)
    {
        Feature? feature = FindFeatureByName(doc, name);
        string? featureType = feature is null ? null : Try(() => feature.GetTypeName2()) as string;
        if (feature is not null && swType != "BODYFEATURE"
            && featureType is not null && NamedSelectTypes.TryGetValue(featureType, out string? selectType)
            && (swType is null || swType == selectType))
        {
            string t = selectType;
            return new ResolvedEntity
            {
                Kind = t.ToLowerInvariant(),
                Com = feature,
                Info = new { kind = t.ToLowerInvariant(), name },
                Select = (d, append, mark) => d.Extension.SelectByID2(name, t, 0, 0, 0, append, mark, null, 0) || feature.Select2(append, mark),
            };
        }

        if (feature is not null)
        {
            return new ResolvedEntity
            {
                Kind = "feature",
                Com = feature,
                Info = new { kind = "feature", name, type = Try(() => feature.GetTypeName2()) },
                Select = (_, append, mark) => feature.Select2(append, mark),
            };
        }

        throw WorkerException.Validation(
            "ENTITY_NOT_FOUND",
            $"No plane/axis/sketch/point/feature named '{name}'.",
            new Dictionary<string, object?> { ["name"] = name, ["type"] = swType },
            ["Default planes are 'Front Plane', 'Top Plane', 'Right Plane'.", "Run solidworks_part_report to list features."]);
    }

    private static ResolvedEntity ResolveBody(ModelDoc2 doc, string name)
    {
        Body2 body = SolidBodies(doc).FirstOrDefault(b => string.Equals(Try(() => b.Name) as string, name, StringComparison.OrdinalIgnoreCase))
            ?? throw WorkerException.Validation(
                "BODY_NOT_FOUND",
                $"Solid body not found: {name}",
                new Dictionary<string, object?> { ["available"] = SolidBodies(doc).Select(b => Try(() => b.Name)).ToArray() });
        return new ResolvedEntity
        {
            Kind = "body",
            Com = body,
            Info = new { kind = "body", name },
            Select = (d, append, mark) => body.Select2(append, NewSelectData(d, mark)),
        };
    }

    // ---- faces ----

    private static IEnumerable<Face2> AllFaces(ModelDoc2 doc, string? bodyName = null) =>
        SolidBodies(doc)
            .Where(b => bodyName is null || string.Equals(Try(() => b.Name) as string, bodyName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(b => (Try(() => b.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>());

    private static string SurfaceKind(Face2 face)
    {
        Surface? s = Try(() => face.GetSurface()) as Surface;
        if (s is null) return "unknown";
        if (Try(() => s.IsPlane()) as bool? == true) return "plane";
        if (Try(() => s.IsCylinder()) as bool? == true) return "cylinder";
        if (Try(() => s.IsCone()) as bool? == true) return "cone";
        if (Try(() => s.IsSphere()) as bool? == true) return "sphere";
        if (Try(() => s.IsTorus()) as bool? == true) return "torus";
        return "freeform";
    }

    private static double[]? FaceNormal(Face2 face) => Try(() => face.Normal) as double[];

    private static double[]? CylinderParams(Face2 face)
    {
        Surface? s = Try(() => face.GetSurface()) as Surface;
        return s is not null && Try(() => s.IsCylinder()) as bool? == true ? Try(() => s.CylinderParams) as double[] : null;
    }

    private static double[] FaceCenter(Face2 face)
    {
        double[]? box = Try(() => face.GetBox()) as double[];
        return box is { Length: >= 6 } ? new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 } : new double[3];
    }

    private static double FaceDistance(Face2 face, double[] p)
    {
        double[]? q = Try(() => face.GetClosestPointOn(p[0], p[1], p[2])) as double[];
        return q is { Length: >= 3 } ? Dist3(p, q) : double.MaxValue;
    }

    private static ResolvedEntity FaceEntity(Face2 face, double scale, double? distance)
    {
        string kind = SurfaceKind(face);
        double[]? normal = kind == "plane" ? FaceNormal(face) : null;
        double[]? cyl = kind == "cylinder" ? CylinderParams(face) : null;
        return new ResolvedEntity
        {
            Kind = "face",
            Com = face,
            Info = new
            {
                kind = "face",
                surface = kind,
                center = Round(FaceCenter(face), scale),
                normal = normal is null ? null : normal.Select(x => Math.Round(x, 4)).ToArray(),
                axis = cyl is null ? null : new { origin = Round(cyl[..3], scale), direction = cyl[3..6].Select(x => Math.Round(x, 4)).ToArray(), radius = Math.Round(cyl[6] / scale, 4) },
                area = Math.Round(((Try(() => face.GetArea()) as double?) ?? 0) / (scale * scale), 4),
                distance = distance is null ? (double?)null : Math.Round(distance.Value / scale, 5),
            },
            Select = (d, append, mark) => ((Entity)face).Select4(append, NewSelectData(d, mark)),
        };
    }

    private static bool FaceMatches(Face2 face, JsonElement filter, double scale)
    {
        string? type = Str(filter, "type");
        string kind = SurfaceKind(face);
        if (type is not null && type != "any" && !string.Equals(type, kind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Prop(filter, "normal") is JsonElement nEl)
        {
            double[]? normal = kind == "plane" ? FaceNormal(face) : null;
            if (normal is null || Dot3(Normalize3(normal), Normalize3(Vec(nEl, 3))) < DirectionDotTolerance)
            {
                return false;
            }
        }

        if (OptNum(filter, "radius") is double r)
        {
            double[]? cyl = CylinderParams(face);
            if (cyl is null || Math.Abs(cyl[6] - r * scale) > Math.Max(1e-6, r * scale * 1e-3))
            {
                return false;
            }
        }

        if (Prop(filter, "box") is JsonElement boxEl && !InBox(FaceCenter(face), Scaled(Vec(boxEl, 6), scale)))
        {
            return false;
        }

        return true;
    }

    private static List<Face2> FilterFaces(ModelDoc2 doc, JsonElement filter, double scale)
    {
        IEnumerable<Face2> faces = Str(filter, "of_feature") is string featureName
            ? FeatureFaces(doc, featureName)
            : AllFaces(doc, Str(filter, "body"));
        return faces.Where(f => FaceMatches(f, filter, scale)).ToList();
    }

    private static IEnumerable<Face2> FeatureFaces(ModelDoc2 doc, string featureName)
    {
        Feature feature = FindFeatureByName(doc, featureName) ?? throw WorkerException.Validation(
            "FEATURE_NOT_FOUND", $"Feature not found: {featureName}", new Dictionary<string, object?> { ["feature"] = featureName });
        return (Try(() => feature.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>();
    }

    private static ResolvedEntity NearestFace(ModelDoc2 doc, JsonElement spec, double scale)
    {
        double[] p = Scaled(VecProp(spec, "near", 3), scale);
        double maxDistance = OptNum(spec, "max_distance") is double md ? md * scale : 0.002;
        IEnumerable<Face2> pool = Str(spec, "of_feature") is string feat ? FeatureFaces(doc, feat) : AllFaces(doc, Str(spec, "body"));
        var ranked = pool
            .Where(f => FaceMatches(f, spec, scale))
            .Select(f => (face: f, d: FaceDistance(f, p)))
            .OrderBy(x => x.d)
            .ThenByDescending(x => (Try(() => x.face.GetArea()) as double?) ?? 0)
            .Take(3)
            .ToList();
        if (ranked.Count == 0 || ranked[0].d > maxDistance)
        {
            throw WorkerException.Validation(
                "FACE_NOT_FOUND",
                ranked.Count == 0 ? "No face matched the filters." : $"Nearest matching face is {ranked[0].d / scale:0.###} away (max_distance {maxDistance / scale:0.###}).",
                new Dictionary<string, object?> { ["near"] = Round(p, scale), ["closest"] = ranked.Select(x => FaceEntity(x.face, scale, x.d).Info).ToArray() },
                ["Pick a point inside the face, or add normal/type filters, or raise max_distance."]);
        }

        return FaceEntity(ranked[0].face, scale, ranked[0].d);
    }

    // ---- edges ----

    private static IEnumerable<Edge> AllEdges(ModelDoc2 doc, string? bodyName = null) =>
        SolidBodies(doc)
            .Where(b => bodyName is null || string.Equals(Try(() => b.Name) as string, bodyName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(b => (Try(() => b.GetEdges()) as object[] ?? Array.Empty<object>()).OfType<Edge>());

    private sealed record EdgeGeom(string Kind, double[] Start, double[] End, double[] Mid, double Length, double[]? Direction, double[]? Center, double? Radius);

    private static EdgeGeom DescribeEdgeGeometry(Edge edge)
    {
        Curve? curve = Try(() => edge.GetCurve()) as Curve;
        double[] cp = Try(() => edge.GetCurveParams2()) as double[] ?? new double[11];
        double[] start = cp[..3];
        double[] end = cp[3..6];
        double t0 = cp.Length > 6 ? cp[6] : 0;
        double t1 = cp.Length > 7 ? cp[7] : 0;
        double[] mid = curve is not null && Try(() => curve.Evaluate2((t0 + t1) / 2, 0)) is double[] ev && ev.Length >= 3
            ? ev[..3]
            : new[] { (start[0] + end[0]) / 2, (start[1] + end[1]) / 2, (start[2] + end[2]) / 2 };
        double length = curve is not null ? (Try(() => curve.GetLength3(t0, t1)) as double?) ?? 0 : 0;

        if (curve is not null && Try(() => curve.IsLine()) as bool? == true)
        {
            return new EdgeGeom("line", start, end, mid, length, Normalize3(new[] { end[0] - start[0], end[1] - start[1], end[2] - start[2] }), null, null);
        }

        if (curve is not null && Try(() => curve.IsCircle()) as bool? == true && Try(() => curve.CircleParams) is double[] c && c.Length >= 7)
        {
            return new EdgeGeom("circle", start, end, mid, length, Normalize3(c[3..6]), c[..3], c[6]);
        }

        return new EdgeGeom("curve", start, end, mid, length, null, null, null);
    }

    private static ResolvedEntity EdgeEntity(Edge edge, double scale, double? distance)
    {
        EdgeGeom g = DescribeEdgeGeometry(edge);
        return new ResolvedEntity
        {
            Kind = "edge",
            Com = edge,
            Info = new
            {
                kind = "edge",
                curve = g.Kind,
                start = Round(g.Start, scale),
                end = Round(g.End, scale),
                mid = Round(g.Mid, scale),
                length = Math.Round(g.Length / scale, 4),
                direction = g.Kind == "line" ? g.Direction!.Select(x => Math.Round(x, 4)).ToArray() : null,
                center = g.Center is null ? null : Round(g.Center, scale),
                axis = g.Kind == "circle" ? g.Direction!.Select(x => Math.Round(x, 4)).ToArray() : null,
                radius = g.Radius is null ? (double?)null : Math.Round(g.Radius.Value / scale, 4),
                distance = distance is null ? (double?)null : Math.Round(distance.Value / scale, 5),
            },
            Select = (d, append, mark) => ((Entity)edge).Select4(append, NewSelectData(d, mark)),
        };
    }

    /// <summary>
    /// Staged edge filter: every COM call costs ~10 ms out of process, so reject on the cheapest
    /// facts first (curve kind, then line direction / circle radius and axis) and only compute the
    /// full geometry when a box filter needs the midpoint.
    /// </summary>
    private static bool EdgeMatches(Edge edge, JsonElement filter, double scale)
    {
        string? type = Str(filter, "type");
        JsonElement? dEl = Prop(filter, "direction");
        JsonElement? aEl = Prop(filter, "axis");
        double? r = OptNum(filter, "radius");
        JsonElement? boxEl = Prop(filter, "box");
        bool wantsLine = string.Equals(type, "line", StringComparison.OrdinalIgnoreCase) || dEl is not null;
        bool wantsCircle = string.Equals(type, "circle", StringComparison.OrdinalIgnoreCase) || aEl is not null || r is not null;

        if (wantsLine || wantsCircle || (type is not null && type != "any"))
        {
            Curve? curve = Try(() => edge.GetCurve()) as Curve;
            if (curve is null)
            {
                return false;
            }

            if (wantsLine)
            {
                if (Try(() => curve.IsLine()) as bool? != true)
                {
                    return false;
                }

                if (dEl is JsonElement d && (Try(() => curve.LineParams) is not double[] lp || lp.Length < 6
                    || Math.Abs(Dot3(Normalize3(lp[3..6]), Normalize3(Vec(d, 3)))) < DirectionDotTolerance))
                {
                    return false;
                }
            }
            else if (wantsCircle)
            {
                if (Try(() => curve.IsCircle()) as bool? != true || Try(() => curve.CircleParams) is not double[] cp || cp.Length < 7)
                {
                    return false;
                }

                if (r is double rr && Math.Abs(cp[6] - rr * scale) > Math.Max(1e-6, rr * scale * 1e-3))
                {
                    return false;
                }

                if (aEl is JsonElement a && Math.Abs(Dot3(Normalize3(cp[3..6]), Normalize3(Vec(a, 3)))) < DirectionDotTolerance)
                {
                    return false;
                }
            }
            else
            {
                // type: "curve" (neither line nor circle).
                if (Try(() => curve.IsLine()) as bool? == true || Try(() => curve.IsCircle()) as bool? == true)
                {
                    return false;
                }
            }
        }

        if (boxEl is JsonElement box && !InBox(DescribeEdgeGeometry(edge).Mid, Scaled(Vec(box, 6), scale)))
        {
            return false;
        }

        return true;
    }

    private static List<Edge> FilterEdges(ModelDoc2 doc, JsonElement filter, double scale)
    {
        IEnumerable<Edge> pool;
        if (Prop(filter, "on_face") is JsonElement faceSpec)
        {
            Face2 face = (Face2)NearestFace(doc, faceSpec, scale).Com!;
            pool = (Try(() => face.GetEdges()) as object[] ?? Array.Empty<object>()).OfType<Edge>();
        }
        else if (Str(filter, "of_feature") is string featureName)
        {
            pool = FeatureFaces(doc, featureName).SelectMany(f => (Try(() => f.GetEdges()) as object[] ?? Array.Empty<object>()).OfType<Edge>());
        }
        else
        {
            pool = AllEdges(doc, Str(filter, "body"));
        }

        // Faces share edges; de-duplicate by geometry signature.
        var seen = new HashSet<string>();
        var output = new List<Edge>();
        foreach (Edge edge in pool)
        {
            if (!EdgeMatches(edge, filter, scale))
            {
                continue;
            }

            EdgeGeom g = DescribeEdgeGeometry(edge);
            string key = string.Join(",", g.Mid.Concat(new[] { g.Length }).Select(x => Math.Round(x, 7)));
            if (seen.Add(key))
            {
                output.Add(edge);
            }
        }

        return output;
    }

    private static ResolvedEntity NearestEdge(ModelDoc2 doc, JsonElement spec, double scale)
    {
        double[] p = Scaled(VecProp(spec, "near", 3), scale);
        double maxDistance = OptNum(spec, "max_distance") is double md ? md * scale : 0.002;
        IEnumerable<Edge> pool = Prop(spec, "on_face") is not null || Str(spec, "of_feature") is not null
            ? FilterEdges(doc, spec, scale)
            : AllEdges(doc, Str(spec, "body")).Where(e => EdgeMatches(e, spec, scale));
        var ranked = pool
            .Select(e => (edge: e, d: Try(() => e.GetClosestPointOn(p[0], p[1], p[2])) is double[] q && q.Length >= 3 ? Dist3(p, q) : double.MaxValue))
            .OrderBy(x => x.d)
            .Take(3)
            .ToList();
        if (ranked.Count == 0 || ranked[0].d > maxDistance)
        {
            throw WorkerException.Validation(
                "EDGE_NOT_FOUND",
                ranked.Count == 0 ? "No edge matched the filters." : $"Nearest matching edge is {ranked[0].d / scale:0.###} away (max_distance {maxDistance / scale:0.###}).",
                new Dictionary<string, object?> { ["near"] = Round(p, scale), ["closest"] = ranked.Select(x => EdgeEntity(x.edge, scale, x.d).Info).ToArray() },
                ["Pick a point on the edge (e.g. its midpoint), or add direction/type filters."]);
        }

        return EdgeEntity(ranked[0].edge, scale, ranked[0].d);
    }

    // ---- vertices ----

    private static ResolvedEntity NearestVertex(ModelDoc2 doc, JsonElement spec, double scale)
    {
        double[] p = Scaled(VecProp(spec, "near", 3), scale);
        var best = SolidBodies(doc)
            .SelectMany(b => (Try(() => b.GetVertices()) as object[] ?? Array.Empty<object>()).OfType<Vertex>())
            .Select(v => (vertex: v, point: Try(() => v.GetPoint()) as double[]))
            .Where(x => x.point is { Length: >= 3 })
            .Select(x => (x.vertex, x.point, d: Dist3(p, x.point!)))
            .OrderBy(x => x.d)
            .FirstOrDefault();
        if (best.vertex is null)
        {
            throw WorkerException.Validation("VERTEX_NOT_FOUND", "No vertex found.", new Dictionary<string, object?>());
        }

        Vertex vertex = best.vertex;
        return new ResolvedEntity
        {
            Kind = "vertex",
            Com = vertex,
            Info = new { kind = "vertex", point = Round(best.point, scale), distance = Math.Round(best.d / scale, 5) },
            Select = (d, append, mark) => ((Entity)vertex).Select4(append, NewSelectData(d, mark)),
        };
    }

    /// <summary>
    /// Bodies from a name, {body:"name"}, or {near:[x,y,z]} (the body closest to a point) — names
    /// change as features are added, so geometric lookup is the robust option.
    /// </summary>
    private static List<ResolvedEntity> BodiesArg(ModelDoc2 doc, JsonElement? args, string argName, double scale)
    {
        JsonElement? v = Prop(args, argName);
        if (v is null)
        {
            return new List<ResolvedEntity>();
        }

        IEnumerable<JsonElement> items = v.Value.ValueKind == JsonValueKind.Array ? v.Value.EnumerateArray() : new[] { v.Value };
        var output = new List<ResolvedEntity>();
        foreach (JsonElement item in items)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                output.Add(ResolveBody(doc, item.GetString()!));
            }
            else if (Str(item, "body") is string name)
            {
                output.Add(ResolveBody(doc, name));
            }
            else if (Prop(item, "near") is JsonElement nearEl)
            {
                double[] p = Scaled(Vec(nearEl, 3), scale);
                // A point inside a body selects that body even when another body's surface is closer;
                // when several bodies contain it (nested/overlapping), the innermost (nearest surface) wins.
                Body2 best = SolidBodies(doc)
                    .Select(b => (body: b, d: SignedBodyDistance(b, p)))
                    .OrderBy(x => x.d < 0 ? 0 : 1)
                    .ThenBy(x => Math.Abs(x.d))
                    .Select(x => x.body)
                    .FirstOrDefault() ?? throw WorkerException.Validation("BODY_NOT_FOUND", "No solid bodies in the part.", new Dictionary<string, object?>());
                output.Add(ResolveBody(doc, Try(() => best.Name) as string ?? ""));
            }
            else
            {
                throw WorkerException.Validation("BAD_BODY_SELECTOR", $"'{argName}' entries must be a body name or {{near:[x,y,z]}}.", new Dictionary<string, object?>());
            }
        }

        return output;
    }

    /// <summary>
    /// Distance from a point to a body's surface, negative when the point is inside the body
    /// (judged by the outward normal at the closest surface point).
    /// </summary>
    private static double SignedBodyDistance(Body2 body, double[] p)
    {
        Face2? nearest = null;
        double[]? q = null;
        double best = double.MaxValue;
        foreach (Face2 face in (Try(() => body.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>())
        {
            if (Try(() => face.GetClosestPointOn(p[0], p[1], p[2])) is double[] c && c.Length >= 3)
            {
                double d = Dist3(p, c);
                if (d < best)
                {
                    best = d;
                    nearest = face;
                    q = c[..3];
                }
            }
        }

        if (nearest is null || q is null || best < 1e-9)
        {
            return best;
        }

        double[]? n = FaceNormal(nearest);
        if (n is null || Math.Abs(Dot3(n, n)) < 1e-12)
        {
            // Curved face: surface normal at the point, flipped when the face runs against the surface.
            if (Try(() => ((Surface)nearest.GetSurface()).EvaluateAtPoint(q[0], q[1], q[2])) is double[] ev && ev.Length >= 6)
            {
                n = new[] { ev[3], ev[4], ev[5] };
                if ((Try(() => nearest.FaceInSurfaceSense()) as bool?) == true)
                {
                    n = n.Select(x => -x).ToArray();
                }
            }
        }

        return n is not null && Dot3(Sub(p, q), n) < 0 ? -best : best;
    }

    private static bool InBox(double[] p, double[] box) =>
        p[0] >= Math.Min(box[0], box[3]) - 1e-9 && p[0] <= Math.Max(box[0], box[3]) + 1e-9
        && p[1] >= Math.Min(box[1], box[4]) - 1e-9 && p[1] <= Math.Max(box[1], box[4]) + 1e-9
        && p[2] >= Math.Min(box[2], box[5]) - 1e-9 && p[2] <= Math.Max(box[2], box[5]) + 1e-9;
}
