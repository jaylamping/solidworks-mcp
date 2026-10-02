using System.Runtime.InteropServices;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Less common features: rib, draft, helix, inserted (derived) parts, split / combine
// bodies, and standards-based Hole Wizard holes (tapped / counterbored with real sizes).
internal static partial class Program
{
    private static Feature LastFeatureOfType(ModelDoc2 doc, string what, params string[] typeNames)
    {
        Feature? f = Try(() => doc.FeatureByPositionReverse(0)) as Feature;
        string? type = f is null ? null : Try(() => f.GetTypeName2()) as string;
        if (f is null || type is null || !typeNames.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            throw FeatureFailed(doc, what, new Dictionary<string, object?> { ["lastFeatureType"] = type });
        }

        return f;
    }

    private static object Rib(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "rib");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        string sketch = RequiredStringArg(args, "sketch");
        double thickness = DoubleArg(args, "thickness", 2) * s;
        bool twoSided = !string.Equals(StringArg(args, "side"), "one", StringComparison.OrdinalIgnoreCase);
        bool normal = string.Equals(StringArg(args, "direction"), "normal", StringComparison.OrdinalIgnoreCase);
        double draft = Deg(DoubleArg(args, "draft_deg", 0));
        bool flip = BoolArg(args, "flip_material");
        string? before = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string;

        Feature? Attempt(bool reverseMaterial)
        {
            doc.ClearSelection2(true);
            SelectSketchByName(doc, sketch, append: false, mark: 0);
            doc.FeatureManager.InsertRib(twoSided, BoolArg(args, "flip_side"), thickness, 0, reverseMaterial, draft > 0, BoolArg(args, "draft_outward"), draft, normal, false);
            Feature? f = Try(() => doc.FeatureByPositionReverse(0)) as Feature;
            return f is not null && (Try(() => f.GetTypeName2()) as string) == "Rib" && (Try(() => f.Name) as string) != before ? f : null;
        }

        // The material side of a rib sketch is ambiguous; try the other side before failing.
        Feature? rib = Attempt(flip) ?? Attempt(!flip);
        if (rib is null)
        {
            throw FeatureFailed(doc, "rib", new Dictionary<string, object?> { ["sketch"] = sketch },
                "The rib sketch must be an open line/chain whose extension meets the part on both ends (or one end for a single-ended rib).",
                "Sketch the rib on a plane that cuts through the part (e.g. a mid plane).");
        }

        return FeatureOutcome(doc, rib, args, s, StringArg(args, "name"));
    }

    private static object Draft(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "draft");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        ResolvedEntity neutral = ResolveSingle(doc, Prop(args, "neutral") ?? throw new ArgumentException("neutral is required"), s, "neutral");
        List<ResolvedEntity> faces = ResolveSelectors(doc, Prop(args, "faces") ?? throw new ArgumentException("faces is required"), s, "faces");
        doc.ClearSelection2(true);
        SelectResolved(doc, new[] { neutral }, 1, append: false, "neutral");
        SelectResolved(doc, faces, 2, append: true, "faces");
        // Propagate along tangent faces by default: drafting one face of a filleted wall otherwise fails.
        int propagate = (StringArg(args, "propagate") ?? "tangent") switch { "none" => 0, "all_neutral" => 2, "inner_loops" => 3, "outer_loop" => 4, _ => 1 };
        object? created = doc.FeatureManager.InsertMultiFaceDraft(Deg(DoubleArg(args, "angle_deg", 2)), BoolArg(args, "flip"), false, propagate, false, false);
        Feature feature = CheckCreated(doc, created, "draft", new Dictionary<string, object?> { ["faces"] = faces.Count })!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object Helix(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "helix");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        ResolvedEntity target = ResolveSingle(doc, Prop(args, "on") ?? throw new ArgumentException("on is required"), s, "on");
        doc.ClearSelection2(true);
        SelectResolved(doc, new[] { target }, 0, append: false, "on");
        doc.SketchManager.InsertSketch(true);
        ISketch sketch = doc.SketchManager.ActiveSketch as ISketch ?? throw WorkerException.Worker("SKETCH_OPEN_FAILED", "Could not open the helix base sketch.", new Dictionary<string, object?>());
        var space = new SketchSpace
        {
            Math = (MathUtility)app.GetMathUtility(),
            ModelToSketch = (MathTransform)sketch.ModelToSketchTransform,
            SketchToModel = (MathTransform)((MathTransform)sketch.ModelToSketchTransform).Inverse(),
            ModelCoords = string.Equals(StringArg(args, "space"), "model", StringComparison.OrdinalIgnoreCase),
            Scale = s,
        };
        double[] c = space.P(Prop(args, "center") is JsonElement ce ? Vec(ce) : new double[] { 0, 0 });
        double radius = DoubleArg(args, "diameter", 10) * s / 2;
        doc.SketchManager.AddToDB = true;
        doc.SketchManager.CreateCircleByRadius(c[0], c[1], 0, radius);
        doc.SketchManager.AddToDB = false;

        double pitch = DoubleArg(args, "pitch", 1) * s;
        double height = DoubleArg(args, "height", 0) * s;
        double revolutions = DoubleArg(args, "revolutions", 0);
        int defBy;
        if (height > 0 && revolutions <= 0)
        {
            defBy = (int)swHelixDefinedBy_e.swHelixDefinedByHeightAndPitch;
        }
        else if (height > 0)
        {
            defBy = (int)swHelixDefinedBy_e.swHelixDefinedByHeightAndRevolution;
        }
        else
        {
            defBy = (int)swHelixDefinedBy_e.swHelixDefinedByPitchAndRevolution;
            revolutions = revolutions > 0 ? revolutions : 5;
        }

        double taper = Deg(DoubleArg(args, "taper_deg", 0));
        doc.InsertHelix(BoolArg(args, "reverse"), BoolArg(args, "clockwise"), taper != 0, BoolArg(args, "taper_outward"), defBy,
            height, pitch, revolutions, taper, Deg(DoubleArg(args, "start_angle_deg", 0)));
        EnsureNoActiveSketch(doc);
        Feature helix = LastFeatureOfType(doc, "helix", "Helix");

        // InsertHelix direction is relative to the sketch, not obviously to the plane normal:
        // measure where the curve went and flip so it runs along +normal (or -normal with reverse).
        double[] origin = space.ToModel(new[] { c[0], c[1], 0.0 });
        double[] normal = Normalize3(Sub(space.ToModel(new[] { c[0], c[1], 1.0 }), origin));
        double wanted = BoolArg(args, "reverse") ? -1 : 1;
        double side = HelixSide(helix, origin, normal);
        bool flipped = false;
        if (side * wanted < 0 && helix.GetDefinition() is HelixFeatureData hd)
        {
            hd.ReverseDirection = !hd.ReverseDirection;
            flipped = helix.ModifyDefinition(hd, doc, null);
            TryVoid(() => doc.EditRebuild3());
            side = HelixSide(helix, origin, normal);
        }

        object outcome = FeatureOutcome(doc, helix, args, s, StringArg(args, "name"));
        (double[]? start, double[]? tangent, double[]? end) = CurveEnds(helix);
        return new
        {
            outcome,
            direction = side >= 0 ? "along plane normal" : "against plane normal",
            planeNormal = normal.Select(x => Math.Round(x, 4)).ToArray(),
            flipped,
            startPoint = start is null ? null : Round(start, s, 4),
            startTangent = tangent?.Select(x => Math.Round(x, 4)).ToArray(),
            endPoint = end is null ? null : Round(end, s, 4),
            hint = "Sweep profiles must sit at startPoint on a plane roughly normal to startTangent (e.g. a plane through the helix axis and startPoint).",
        };
    }

    /// <summary>Start point, start tangent, and end point of a reference-curve feature (helix, composite curve).</summary>
    private static (double[]? Start, double[]? Tangent, double[]? End) CurveEnds(Feature curveFeature)
    {
        if (Try(() => curveFeature.GetSpecificFeature2()) is not ReferenceCurve rc
            || Try(() => rc.GetSegments()) is not object[] segments || segments.Length == 0)
        {
            return (null, null, null);
        }

        Edge first = (Edge)segments[0];
        Edge last = (Edge)segments[^1];
        double[]? p0 = Try(() => first.GetCurveParams2()) as double[];
        double[]? p1 = Try(() => last.GetCurveParams2()) as double[];
        double[]? tangent = null;
        if (p0 is { Length: >= 8 } && Try(() => first.GetCurve()) is Curve c && Try(() => c.Evaluate2(p0[6], 1)) is double[] ev && ev.Length >= 6)
        {
            tangent = Normalize3(new[] { ev[3], ev[4], ev[5] });
        }

        return (p0?[..3], tangent, p1 is { Length: >= 6 } ? p1[3..6] : null);
    }

    /// <summary>Signed offset of a curve feature's box center from a plane (along its normal).</summary>
    private static double HelixSide(Feature helix, double[] origin, double[] normal)
    {
        object box = null!;
        if (!(Try(() => helix.GetBox(ref box)) as bool? ?? false) || box is not double[] b || b.Length < 6)
        {
            return 0;
        }

        double[] center = { (b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2 };
        return Dot3(Sub(center, origin), normal);
    }

    private static object InsertPart(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "insert_part");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);

        // Assembly context: take the component's file and its placement in the assembly.
        double[]? placement = null;
        double[]? componentBox = null;
        string? sourceArg = StringArg(args, "source");
        if (Prop(args, "from_assembly") is JsonElement fa)
        {
            string asmPath = Str(fa, "assembly") ?? throw new ArgumentException("from_assembly.assembly is required");
            string componentName = Str(fa, "component") ?? throw new ArgumentException("from_assembly.component is required");
            ModelDoc2 asm = OpenDocument(app, asmPath);
            if (asm.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                throw WorkerException.Validation("ASSEMBLY_REQUIRED", $"{asmPath} is not an assembly.", new Dictionary<string, object?>());
            }

            Component2 component = FindComponent((IAssemblyDoc)asm, null, componentName)
                ?? throw WorkerException.Validation("COMPONENT_NOT_FOUND", $"Component not found: {componentName}", new Dictionary<string, object?>(),
                    ["List components with solidworks_list_components."]);
            sourceArg = component.GetPathName();
            placement = ((MathTransform)component.Transform2).ArrayData as double[];
            componentBox = Try(() => component.GetBox(false, false)) as double[];
            doc = OpenDocument(app, PathGuard.NormalizeCadPath(RequiredStringArg(args, "path")));
        }

        string source = PathGuard.NormalizeCadPath(sourceArg ?? throw new ArgumentException("source or from_assembly is required"));
        if (!File.Exists(source))
        {
            throw WorkerException.Validation("SOURCE_NOT_FOUND", $"Part not found: {source}", new Dictionary<string, object?>());
        }

        int options = (int)swInsertPartOptions_e.swInsertPartImportSolids;
        if (BoolArg(args, "import_planes")) options |= (int)swInsertPartOptions_e.swInsertPartImportPlanes;
        if (BoolArg(args, "import_axes")) options |= (int)swInsertPartOptions_e.swInsertPartImportAxes;
        var before = SolidBodies(doc).Select(BoxKey).ToHashSet();
        Feature stock = ((PartDoc)doc).InsertPart3(source, options, StringArg(args, "configuration") ?? "")
            ?? throw FeatureFailed(doc, "inserted part", new Dictionary<string, object?> { ["source"] = source });
        if (StringArg(args, "name") is string rename)
        {
            TryVoid(() => stock.Name = rename);
        }

        TryVoid(() => doc.EditRebuild3());
        var inserted = SolidBodies(doc).Where(b => !before.Contains(BoxKey(b))).ToList();
        var moves = new List<string>();
        if (inserted.Count > 0 && placement is { Length: >= 12 })
        {
            moves.AddRange(ApplyRigidTransform(doc, inserted, placement));
        }
        else if (inserted.Count > 0 && (Prop(args, "rotate") is not null || Prop(args, "translate") is not null))
        {
            moves.AddRange(MoveBodies(doc, inserted, args, s));
        }

        return new
        {
            document = DescribeDocument(doc),
            featureName = Try(() => stock.Name),
            source,
            insertedBodies = SolidBodies(doc).Where(b => !before.Contains(BoxKey(b))).Select(b => new
            {
                name = Try(() => b.Name),
                boundingBox = Try(() => b.GetBodyBox()) is double[] bb ? new { min = Round(bb[..3], s), max = Round(bb[3..6], s) } : null,
            }).ToList(),
            moveFeatures = moves,
            componentBoxInAssembly = componentBox is { Length: >= 6 } ? new { min = Round(componentBox[..3], s), max = Round(componentBox[3..6], s) } : null,
            part = PartGeometrySummary(doc, s),
        };
    }

    /// <summary>
    /// Applies a SolidWorks MathTransform (row-vector convention: p' = p·R + t) to bodies as successive
    /// single-axis rotations about the origin (X, then Y, then Z) followed by a translation.
    /// </summary>
    private static List<string> ApplyRigidTransform(ModelDoc2 doc, List<Body2> bodies, double[] t)
    {
        // Column-vector matrix M = R^T, so M[r][c] = t[c*3 + r]; decompose M = Rz(g) * Ry(b) * Rx(a).
        double m20 = t[2], m21 = t[5], m22 = t[8], m10 = t[1], m00 = t[0];
        double b = Math.Asin(Math.Clamp(-m20, -1, 1));
        double a = Math.Atan2(m21, m22);
        double g = Math.Atan2(m10, m00);
        var features = new List<string>();
        foreach ((double angle, double[] axis) in new[] { (a, new[] { 1.0, 0, 0 }), (b, new[] { 0.0, 1, 0 }), (g, new[] { 0.0, 0, 1 }) })
        {
            if (Math.Abs(angle) < 1e-9)
            {
                continue;
            }

            var step = JsonSerializer.SerializeToElement(new { rotate = new { axis_point = new[] { 0.0, 0, 0 }, axis_direction = axis, angle_deg = angle * 180 / Math.PI } });
            var names = SolidBodies(doc).Select(x => Try(() => x.Name) as string ?? "").ToHashSet();
            features.AddRange(MoveBodies(doc, bodies, step, 1.0));
            TryVoid(() => doc.EditRebuild3());
            var produced = SolidBodies(doc).Where(x => !names.Contains(Try(() => x.Name) as string ?? "")).ToList();
            if (produced.Count == bodies.Count)
            {
                bodies = produced;
            }
        }

        if (Math.Abs(t[9]) + Math.Abs(t[10]) + Math.Abs(t[11]) > 1e-12)
        {
            var step = JsonSerializer.SerializeToElement(new { translate = new[] { t[9], t[10], t[11] } });
            features.AddRange(MoveBodies(doc, bodies, step, 1.0));
        }

        return features;
    }

    /// <summary>Rotate then translate bodies (SolidWorks applies only one of them per Move/Copy feature).</summary>
    private static List<string> MoveBodies(ModelDoc2 doc, List<Body2> bodies, JsonElement? args, double s, bool copy = false)
    {
        var created = new List<string>();
        void SelectBodies()
        {
            doc.ClearSelection2(true);
            bool append = false;
            foreach (Body2 b in bodies)
            {
                // Body2.Select2 can silently fail (e.g. inserted/derived bodies); fall back to the body name.
                bool ok = (Try(() => b.Select2(append, NewSelectData(doc, 1))) as bool?) == true;
                if (!ok && Try(() => b.Name) is string bodyName)
                {
                    ok = doc.Extension.SelectByID2(bodyName, "SOLIDBODY", 0, 0, 0, append, 1, null, 0);
                }

                if (!ok)
                {
                    throw WorkerException.Worker("BODY_SELECT_FAILED", $"Could not select body {Try(() => b.Name)} to move it.", new Dictionary<string, object?>());
                }

                append = true;
            }
        }

        if (Prop(args, "rotate") is JsonElement rot)
        {
            double[] pivot = Scaled(VecProp(rot, "axis_point", 3), s);
            double[] dir = Normalize3(VecProp(rot, "axis_direction", 3));
            int axisIndex = Array.FindIndex(dir, v => Math.Abs(Math.Abs(v) - 1) < 1e-6);
            if (axisIndex < 0)
            {
                throw WorkerException.Validation("UNSUPPORTED_ROTATION", "rotate.axis_direction must be a principal axis ([1,0,0], [0,1,0], [0,0,1] or negatives).", new Dictionary<string, object?>());
            }

            double[] angles = new double[3];
            angles[axisIndex] = Deg(Num(rot, "angle_deg")) * Math.Sign(dir[axisIndex]);
            // Track bodies by name: a copy rotated onto itself (e.g. 90 degrees of a square) has the same box.
            var beforeRotate = SolidBodies(doc).Select(b => Try(() => b.Name) as string ?? "").ToHashSet();
            SelectBodies();
            // Empirically the three rotation slots apply about Z, Y, X (in that order), despite their names.
            Feature f = doc.FeatureManager.InsertMoveCopyBody2(0, 0, 0, 0, pivot[0], pivot[1], pivot[2], angles[2], angles[1], angles[0], copy, copy ? 1 : 0)
                ?? throw FeatureFailed(doc, "rotate body", new Dictionary<string, object?>());
            created.Add(f.Name);
            TryVoid(() => doc.EditRebuild3());
            // After a rotate (copy or not) the bodies to translate are the ones the rotate produced.
            var produced = SolidBodies(doc).Where(b => !beforeRotate.Contains(Try(() => b.Name) as string ?? "")).ToList();
            if (produced.Count > 0)
            {
                bodies = produced;
            }

            copy = false;
        }

        if (Prop(args, "translate") is JsonElement tr)
        {
            double[] t = Scaled(Vec(tr, 3), s);
            SelectBodies();
            Feature f = doc.FeatureManager.InsertMoveCopyBody2(t[0], t[1], t[2], 0, 0, 0, 0, 0, 0, 0, copy, copy ? 1 : 0)
                ?? throw FeatureFailed(doc, "translate body", new Dictionary<string, object?>());
            created.Add(f.Name);
        }

        return created;
    }

    private static object SplitBody(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "split_body");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        ResolvedEntity tool = ResolveSingle(doc, Prop(args, "tool") ?? throw new ArgumentException("tool is required"), s, "tool");
        doc.ClearSelection2(true);
        SelectResolved(doc, new[] { tool }, 0, append: false, "tool");
        object? created = null;
        int pieceCount = 0;
        // Variants follow the documented example: clear the selection between Pre/PostSplit, consume the cut.
        // consume=true removes the resulting bodies from this part (they are meant to be saved out); keep them by default.
        foreach (bool consume in new[] { BoolArg(args, "consume_tool", false), !BoolArg(args, "consume_tool", false) })
        {
            doc.ClearSelection2(true);
            SelectResolved(doc, new[] { tool }, 0, append: true, "tool");
            object[] pieces = doc.FeatureManager.PreSplitBody2() as object[] ?? Array.Empty<object>();
            pieceCount = pieces.Length;
            if (pieces.Length == 0)
            {
                continue;
            }

            doc.ClearSelection2(true);
            DispatchWrapper[] bodies = pieces.Select(p => new DispatchWrapper(p)).ToArray();
            DispatchWrapper[] origins = pieces.Select(_ => new DispatchWrapper(null)).ToArray();
            object paths = pieces.Select(_ => "").ToArray();
            created = doc.FeatureManager.PostSplitBody2(bodies, consume, origins, paths, "");
            if (created is not null)
            {
                break;
            }
        }

        if (pieceCount == 0)
        {
            throw FeatureFailed(doc, "split (no resulting bodies)", new Dictionary<string, object?>(), "The tool must fully cut through the body.");
        }

        Feature feature = CheckCreated(doc, created, "split", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object Combine(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "combine");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        int op = (StringArg(args, "operation") ?? "add").ToLowerInvariant() switch
        {
            "add" or "union" => (int)swBodyOperationType_e.SWBODYADD,
            "subtract" or "cut" => (int)swBodyOperationType_e.SWBODYCUT,
            "common" or "intersect" => (int)swBodyOperationType_e.SWBODYINTERSECT,
            var other => throw WorkerException.Validation("BAD_OPERATION", $"Unknown operation {other}", new Dictionary<string, object?>()),
        };
        List<ResolvedEntity> target = BodiesArg(doc, args, "target", s);
        List<ResolvedEntity> tools = BodiesArg(doc, args, "tools", s);
        if (target.Count != 1 || tools.Count == 0)
        {
            throw WorkerException.Validation("BAD_BODIES", "combine needs exactly one target body and at least one tool body.", new Dictionary<string, object?>());
        }

        if (op == (int)swBodyOperationType_e.SWBODYINTERSECT)
        {
            // InsertCombineFeature(common) is unreliable through the API on this SolidWorks build; the
            // existing combine_bodies handler synthesizes it robustly from subtract operations.
            var names = target.Concat(tools).Select(b => Try(() => ((Body2)b.Com!).Name) as string).ToArray();
            var delegated = new Dictionary<string, object?>
            {
                ["path"] = StringArg(args, "path"),
                ["operation"] = "common",
                ["body_names"] = names,
                ["confirm"] = true,
            };
            object result = CombineBodies(JsonSerializer.SerializeToElement(delegated));
            return new { result, part = PartGeometrySummary(doc, s) };
        }

        doc.ClearSelection2(true);
        SelectResolved(doc, target, 1, append: false, "target");
        SelectResolved(doc, tools, 2, append: true, "tools");
        object? created = doc.FeatureManager.InsertCombineFeature(op, null, null);
        Feature feature = CheckCreated(doc, created, "combine", new Dictionary<string, object?>(),
            "For common, the bodies must overlap; for subtract, the tools must intersect the target.")!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ Hole Wizard

    private static object HoleWizard(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "hole_wizard");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        string kind = (StringArg(args, "type") ?? "tapped").ToLowerInvariant();
        string size = StringArg(args, "size") ?? "M3";
        JsonElement positions = Prop(args, "positions") ?? throw new ArgumentException("positions is required");
        var points = positions.EnumerateArray().Select(p => Scaled(Vec(p, 3), s)).ToList();
        ResolvedEntity face = ResolveSingle(doc, Prop(args, "on") ?? throw new ArgumentException("on is required"), s, "on");
        Face2 face2 = face.Com as Face2 ?? throw WorkerException.Validation("FACE_REQUIRED", "hole_wizard needs a planar face in `on`.", new Dictionary<string, object?>());
        double[] normal = Normalize3(FaceNormal(face2) ?? throw WorkerException.Validation("FACE_NOT_PLANAR", "hole_wizard needs a planar face.", new Dictionary<string, object?>()));

        (int type, int fastener, string[] sizes) = kind switch
        {
            "tapped" or "tap" => ((int)swWzdGeneralHoleTypes_e.swWzdTap, (int)swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole, TapSizeVariants(size)),
            "counterbore" or "socket_head" => ((int)swWzdGeneralHoleTypes_e.swWzdCounterBore, (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap, new[] { size }),
            "countersink" or "flat_head" => ((int)swWzdGeneralHoleTypes_e.swWzdCounterSink, (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead, new[] { size }),
            "clearance" => ((int)swWzdGeneralHoleTypes_e.swWzdHole, (int)swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances, new[] { size, size.Replace("M", "M") + ".0", ClearanceDrill(size) }),
            _ => throw WorkerException.Validation("BAD_HOLE_TYPE", $"Unknown hole type {kind} (tapped, counterbore, countersink, clearance). For dowel/press-fit holes use solidworks_hole with the exact diameter.", new Dictionary<string, object?>()),
        };

        double? depth = Prop(args, "depth") is not null ? DoubleArg(args, "depth") * s : null;
        double? threadDepth = Prop(args, "thread_depth") is not null ? DoubleArg(args, "thread_depth") * s : null;
        short end = depth is null ? (short)swEndConditions_e.swEndCondThroughAll : (short)swEndConditions_e.swEndCondBlind;
        int cosmetic = BoolArg(args, "cosmetic_thread", true) ? (int)swWzdHoleCosmeticThreadTypes_e.swCosmeticThreadWithCallout : (int)swWzdHoleCosmeticThreadTypes_e.swCosmeticThreadNone;

        Feature? hole = null;
        string? usedSize = null;
        var attempts = sizes.Select(sz => (fastener, sz)).ToList();
        if (type == (int)swWzdGeneralHoleTypes_e.swWzdHole)
        {
            // Plain holes: the drill-size table takes numeric diameters ("5.5"), the clearance table M sizes.
            attempts.Add(((int)swWzdHoleStandardFastenerTypes_e.swStandardISODrillSizes, ClearanceDrill(size)));
            attempts.Add(((int)swWzdHoleStandardFastenerTypes_e.swStandardISODrillSizes, ClearanceDrill(size) + "mm"));
        }

        foreach ((int fastenerType, string candidate) in attempts)
        {
            fastener = fastenerType;
            doc.ClearSelection2(true);
            double[] p0 = points[0];
            // Pick the face exactly at the first hole location (a ray from just above it).
            double lift = 0.001;
            doc.Extension.SelectByRay(p0[0] + normal[0] * lift, p0[1] + normal[1] * lift, p0[2] + normal[2] * lift, -normal[0], -normal[1], -normal[2], 0.0001, (int)swSelectType_e.swSelFACES, false, 0, 0);
            // Plain Hole Wizard holes reject through-all via the API; a blind hole deeper than the
            // whole part is geometrically identical. Screw holes accept a placeholder depth.
            double partSpan = Try(() => ((PartDoc)doc).GetPartBox(true)) is double[] pb && pb.Length >= 6 ? Dist3(pb[..3], pb[3..6]) : 1.0;
            bool plainThrough = type == (int)swWzdGeneralHoleTypes_e.swWzdHole && depth is null;
            short endUsed = plainThrough ? (short)swEndConditions_e.swEndCondBlind : end;
            double d = depth ?? (plainThrough ? partSpan * 1.1 : 0.01);
            hole = type == (int)swWzdGeneralHoleTypes_e.swWzdTap
                ? doc.FeatureManager.HoleWizard5(type, (int)swWzdHoleStandards_e.swStandardISO, fastener, candidate, endUsed, -1, d, -1,
                    threadDepth ?? -1, -1, -1, -1, -1, -1, cosmetic, 0, -1, -1, -1, -1, "", false, true, true, false, false, false)
                : doc.FeatureManager.HoleWizard5(type, (int)swWzdHoleStandards_e.swStandardISO, fastener, candidate, endUsed,
                    // Plain holes need an explicit drill diameter; screw holes take it from the standard table.
                    type == (int)swWzdGeneralHoleTypes_e.swWzdHole ? PlainHoleDiameter(kind, size) : -1, d, -1,
                    -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, "", false, true, true, false, false, false);
            if (hole is not null)
            {
                usedSize = candidate;
                break;
            }
        }

        if (hole is null)
        {
            throw FeatureFailed(doc, $"{kind} hole {size}", new Dictionary<string, object?> { ["triedSizes"] = sizes },
                "Use ISO sizes like M3, M4, M5 (tapped: M3x0.5).");
        }

        // Additional positions: add sketch points to the hole's placement sketch.
        if (points.Count > 1)
        {
            Feature? placement = SubFeatures(hole).FirstOrDefault(f => (Try(() => f.GetTypeName2()) as string) is "ProfileFeature");
            if (placement is not null)
            {
                doc.ClearSelection2(true);
                placement.Select2(false, 0);
                doc.EditSketch();
                ISketch sk = (ISketch)doc.SketchManager.ActiveSketch;
                MathUtility math = (MathUtility)app.GetMathUtility();
                MathTransform m2s = (MathTransform)sk.ModelToSketchTransform;
                doc.SketchManager.AddToDB = true;
                foreach (double[] p in points.Skip(1))
                {
                    double[] q = (double[])((MathPoint)((MathPoint)math.CreatePoint(p)).MultiplyTransform(m2s)).ArrayData;
                    doc.SketchManager.CreatePoint(q[0], q[1], 0);
                }

                doc.SketchManager.AddToDB = false;
                doc.SketchManager.InsertSketch(true);
                TryVoid(() => doc.ForceRebuild3(false));
            }
        }

        object outcome = FeatureOutcome(doc, hole, args, s, StringArg(args, "name"));
        return new { outcome, size = usedSize, kind, holes = points.Count };
    }

    private static double PlainHoleDiameter(string kind, string size) =>
        double.TryParse(ClearanceDrill(size), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mm) ? mm / 1000.0 : -1;

    /// <summary>ISO 273 normal-fit clearance drill for a metric screw size ("M5" -> "5.5").</summary>
    private static string ClearanceDrill(string size)
    {
        var table = new Dictionary<string, string> { ["M2"] = "2.4", ["M2.5"] = "2.9", ["M3"] = "3.4", ["M4"] = "4.5", ["M5"] = "5.5", ["M6"] = "6.6", ["M8"] = "9", ["M10"] = "11", ["M12"] = "13.5" };
        return table.TryGetValue(size.Split('x')[0].Trim(), out string? d) ? d : size.TrimStart('M');
    }

    private static string[] TapSizeVariants(string size)
    {
        var pitches = new Dictionary<string, string> { ["M2"] = "0.4", ["M2.5"] = "0.45", ["M3"] = "0.5", ["M4"] = "0.7", ["M5"] = "0.8", ["M6"] = "1.0", ["M8"] = "1.25", ["M10"] = "1.5", ["M12"] = "1.75" };
        string bare = size.Split('x')[0].Trim();
        var list = new List<string> { size };
        if (pitches.TryGetValue(bare, out string? pitch))
        {
            list.Add($"{bare}x{pitch}");
            list.Add($"{bare}x{pitch.TrimEnd('0').TrimEnd('.')}");
        }

        list.Add(bare);
        return list.Distinct().ToArray();
    }
}
