using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Selector-driven feature tools. Each handler: resolve the part, build the
// selection set explicitly (with the marks the API expects), create the feature,
// then FeatureOutcome() rebuilds, validates, and rolls back on error.
internal static partial class Program
{
    // Selection marks expected by the feature APIs.
    private const int MarkProfile = 0;
    private const int MarkUpToFace = 1;
    private const int MarkScopeBody = 8;
    private const int MarkFilletEdge = 1;
    private const int MarkChamferEdge = 0;
    private const int MarkRevolveAxis = 16;
    private const int MarkSweepProfile = 1;
    private const int MarkSweepPath = 4;
    private const int MarkLoftProfile = 1;
    private const int MarkLoftGuide = 2;
    private const int MarkPatternDir1 = 1;
    private const int MarkPatternDir2 = 2;
    private const int MarkPatternSeed = 4;
    private const int MarkPatternBody = 256;
    private const int MarkShellFace = 1;
    private const int MarkMirrorPlane = 2;
    private const int MarkMirrorSeed = 1;
    private const int MarkMirrorBody = 256;

    private static int EndCondition(string? end) => (end ?? "blind").ToLowerInvariant() switch
    {
        "blind" => (int)swEndConditions_e.swEndCondBlind,
        "through_all" => (int)swEndConditions_e.swEndCondThroughAll,
        "up_to_next" => (int)swEndConditions_e.swEndCondUpToNext,
        "up_to_face" => (int)swEndConditions_e.swEndCondUpToSurface,
        "offset_from_face" => (int)swEndConditions_e.swEndCondOffsetFromSurface,
        "mid_plane" => (int)swEndConditions_e.swEndCondMidPlane,
        _ => throw WorkerException.Validation("BAD_END_CONDITION", $"Unknown end condition '{end}'.", new Dictionary<string, object?>()),
    };

    private static Feature? CheckCreated(ModelDoc2 doc, object? created, string what, Dictionary<string, object?> ctx, params string[] remediation)
    {
        if (created is Feature f)
        {
            return f;
        }

        throw FeatureFailed(doc, what, ctx, remediation);
    }

    // ------------------------------------------------------------------ extrude

    private static object Extrude(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "extrude");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);

        string sketchName = RequiredStringArg(args, "sketch");
        bool cut = string.Equals(StringArg(args, "mode"), "cut", StringComparison.OrdinalIgnoreCase);
        string end1 = StringArg(args, "end") ?? "blind";
        int t1 = EndCondition(end1);
        bool midPlane = t1 == (int)swEndConditions_e.swEndCondMidPlane;
        double d1 = DoubleArg(args, "depth", 10) * s;
        bool reverse = BoolArg(args, "reverse");
        JsonElement? dir2 = Prop(args, "direction2");
        bool single = dir2 is null || midPlane;
        int t2 = dir2 is JsonElement d2e ? EndCondition(Str(d2e, "end")) : (int)swEndConditions_e.swEndCondBlind;
        double d2 = dir2 is JsonElement d2v ? (OptNum(d2v, "depth") ?? 10) * s : 0.01;
        double draft = Deg(DoubleArg(args, "draft_deg", 0));
        bool draftOn = draft > 0;
        // Empirically (SW 2026) Ddir=true drafts outward, contrary to the API help text.
        bool draftInward = BoolArg(args, "draft_outward");
        double startOffset = DoubleArg(args, "start_offset", 0) * s;
        int t0 = Math.Abs(startOffset) > 0 ? (int)swStartConditions_e.swStartOffset : (int)swStartConditions_e.swStartSketchPlane;
        bool merge = BoolArg(args, "merge", defaultValue: true);
        List<ResolvedEntity> scopeEntities = BodiesArg(doc, args, "scope_bodies", s);

        // Resolve references once; selection is rebuilt per attempt.
        ResolvedEntity? upToEntity = Prop(args, "up_to") is JsonElement upTo ? ResolveSingle(doc, upTo, s, "up_to") : null;
        ResolvedEntity? upTo2Entity = dir2 is JsonElement d2u && Prop(d2u, "up_to") is JsonElement upTo2 ? ResolveSingle(doc, upTo2, s, "direction2.up_to") : null;
        bool useScope = scopeEntities.Count > 0 || !merge;
        bool autoSelect = scopeEntities.Count == 0;
        var ctx = new Dictionary<string, object?> { ["sketch"] = sketchName, ["mode"] = cut ? "cut" : "boss", ["end"] = end1, ["reverse"] = reverse };
        FeatureManager fm = doc.FeatureManager;

        object? Attempt(bool rev, bool sdA, int t1A, int t2A, double d1A, double d2A)
        {
            doc.ClearSelection2(true);
            SelectSketchByName(doc, sketchName, append: false, mark: MarkProfile);
            if (upToEntity is not null)
            {
                SelectResolved(doc, new[] { upToEntity }, MarkUpToFace, append: true, "up_to");
            }

            if (upTo2Entity is not null)
            {
                SelectResolved(doc, new[] { upTo2Entity }, MarkUpToFace, append: true, "direction2.up_to");
            }

            SelectResolved(doc, scopeEntities, MarkScopeBody, append: true, "scope_bodies");

            if (Prop(args, "thin") is JsonElement thin)
            {
                double thk = Num(thin, "thickness") * s;
                int revThinDir = (Str(thin, "side") ?? "one") switch
                {
                    "reverse" => 1,
                    "mid" => 2,
                    "two" => 3,
                    _ => 0,
                };
                int capEnds = Flag(thin, "cap_ends") ? 1 : 0;
                return cut
                    ? fm.FeatureCutThin2(sdA, false, rev, t1A, t2A, d1A, d2A, draftOn, false, draftInward, false, draft, 0,
                        false, false, false, false, thk, thk, thk, revThinDir, capEnds, false, 0, useScope, autoSelect, t0, startOffset, false)
                    : fm.FeatureExtrusionThin2(sdA, false, rev, t1A, t2A, d1A, d2A, draftOn, false, draftInward, false, draft, 0,
                        false, false, false, false, merge, thk, thk, thk, revThinDir, capEnds, false, 0, useScope, autoSelect, t0, startOffset, false);
            }

            return cut
                ? fm.FeatureCut4(sdA, false, rev, t1A, t2A, d1A, d2A, draftOn, false, draftInward, false, draft, 0,
                    false, false, false, false, false, useScope, autoSelect, false, false, false, t0, startOffset, false, false)
                : fm.FeatureExtrusion3(sdA, false, rev, t1A, t2A, d1A, d2A, draftOn, false, draftInward, false, draft, 0,
                    false, false, false, false, merge, useScope, autoSelect, t0, startOffset, false);
        }

        object? created = Attempt(reverse, single, t1, t2, d1, d2);
        if (created is null && midPlane)
        {
            // Native mid-plane cuts can return null where the equivalent two-sided blind cut works.
            int blind = (int)swEndConditions_e.swEndCondBlind;
            created = Attempt(reverse, false, blind, blind, d1 / 2, d1 / 2);
            ctx["midPlaneAsTwoSided"] = created is not null;
        }

        if (created is null && BoolArg(args, "auto_reverse"))
        {
            // A cut started from a plane outside the body points away from it; try the other side.
            created = Attempt(!reverse, single, t1, t2, d1, d2);
            ctx["autoReversed"] = created is not null;
        }

        if (created is null && cut && t1 == (int)swEndConditions_e.swEndCondBlind)
        {
            throw FeatureFailed(doc, "extruded cut", ctx,
                "The cut may be pointing away from the material (common when sketching on a reference plane outside the body): pass reverse:true.",
                "Check the sketch has closed regions and overlaps the body.");
        }

        Feature feature = CheckCreated(doc, created, cut ? "extruded cut" : "extruded boss", ctx)!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ revolve

    private static object Revolve(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "revolve");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);

        string sketchName = RequiredStringArg(args, "sketch");
        bool cut = string.Equals(StringArg(args, "mode"), "cut", StringComparison.OrdinalIgnoreCase);
        double angle = Deg(DoubleArg(args, "angle_deg", 360));
        bool mid = BoolArg(args, "mid_plane");
        double angle2 = Deg(DoubleArg(args, "angle2_deg", 0));
        bool single = !mid && angle2 <= 0;
        int dir1 = mid ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind;
        int dir2 = (int)swEndConditions_e.swEndCondBlind;
        bool merge = BoolArg(args, "merge", defaultValue: true);

        doc.ClearSelection2(true);
        SelectSketchByName(doc, sketchName, append: false, mark: MarkProfile);
        string axisUsed;
        if (Prop(args, "axis") is JsonElement axis)
        {
            ResolvedEntity resolvedAxis = ResolveSingle(doc, axis, s, "axis");
            SelectResolved(doc, new[] { resolvedAxis }, MarkRevolveAxis, append: true, "axis");
            axisUsed = JsonSerializer.Serialize(resolvedAxis.Info, WriteJsonCompact);
        }
        else
        {
            axisUsed = SelectSketchCenterline(doc, sketchName, MarkRevolveAxis);
        }

        bool isThin = false;
        int thinType = 0;
        double thk = 0;
        if (Prop(args, "thin") is JsonElement thin)
        {
            isThin = true;
            thk = Num(thin, "thickness") * s;
            thinType = (Str(thin, "side") ?? "one") == "mid" ? (int)swThinWallType_e.swThinWallMidPlane : (int)swThinWallType_e.swThinWallOneDirection;
        }

        object? created = doc.FeatureManager.FeatureRevolve2(
            single, true, isThin, cut, BoolArg(args, "reverse"), false,
            dir1, dir2, angle, angle2 > 0 ? angle2 : 0, false, false, 0, 0,
            thinType, thk, thk, merge, true, true);
        Feature feature = CheckCreated(doc, created, cut ? "revolved cut" : "revolve", new Dictionary<string, object?> { ["sketch"] = sketchName, ["axis"] = axisUsed })!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ sweep / loft

    private static object Sweep(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "sweep");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        bool cut = string.Equals(StringArg(args, "mode"), "cut", StringComparison.OrdinalIgnoreCase);
        string? profile = StringArg(args, "profile");
        double circularDiameter = DoubleArg(args, "circular_profile_diameter", 0) * s;
        if (profile is null && circularDiameter <= 0)
        {
            throw WorkerException.Validation("SWEEP_PROFILE_REQUIRED", "Provide profile (sketch) or circular_profile_diameter.", new Dictionary<string, object?>());
        }

        JsonElement path = Prop(args, "sweep_path") ?? throw new ArgumentException("sweep_path is required");
        List<ResolvedEntity> pathEntities = ResolveSelectors(doc, path, s, "sweep_path");
        List<ResolvedEntity> guides = Prop(args, "guides") is JsonElement g ? ResolveSelectors(doc, g, s, "guides") : new();
        double twist = Deg(DoubleArg(args, "twist_deg", 0));
        bool merge = BoolArg(args, "merge", defaultValue: true);
        string alignment = StringArg(args, "path_alignment") ?? "auto";
        bool helixPath = pathEntities.Any(e => e.Com is Feature f && (Try(() => f.GetTypeName2()) as string) == "Helix");

        void SelectInputs()
        {
            doc.ClearSelection2(true);
            if (profile is not null)
            {
                SelectSketchByName(doc, profile, append: false, mark: MarkSweepProfile);
                SelectResolved(doc, pathEntities, MarkSweepPath, append: true, "sweep_path");
            }
            else
            {
                SelectResolved(doc, pathEntities, MarkSweepPath, append: false, "sweep_path");
            }

            SelectResolved(doc, guides, MarkLoftGuide, append: true, "guides");
        }

        // SOLIDWORKS 2018+ sweep architecture: feature data + CreateFeature.
        object? created = null;
        if (doc.FeatureManager.CreateDefinition((int)(cut ? swFeatureNameID_e.swFmSweepCut : swFeatureNameID_e.swFmSweep)) is SweepFeatureData data)
        {
            data.TwistControlType = (short)(Math.Abs(twist) > 0 ? (int)swTwistControlType_e.swTwistControlConstantTwistAlongPath : (int)swTwistControlType_e.swTwistControlFollowPath);
            if (Math.Abs(twist) > 0)
            {
                TryVoid(() => data.SetTwistAngle(twist));
            }

            // Helical/3D paths sweep cleanly with minimum-twist alignment (threads, springs).
            int pathAlign = alignment switch
            {
                "none" => (int)swTangencyType_e.swTangencyNone,
                "minimum_twist" => (int)swTangencyType_e.swMinimumTwist,
                _ => helixPath ? (int)swTangencyType_e.swMinimumTwist : (int)swTangencyType_e.swTangencyNone,
            };
            TryVoid(() => data.PathAlignmentType = (short)pathAlign);
            TryVoid(() => data.AlignWithEndFaces = false);
            TryVoid(() => data.MaintainTangency = false);
            TryVoid(() => data.Direction = (short)swSweepDirection_e.swSweepDirection1);
            if (!cut)
            {
                TryVoid(() => data.Merge = merge);
            }

            if (profile is null)
            {
                TryVoid(() => data.CircularProfile = true);
                TryVoid(() => data.CircularProfileDiameter = circularDiameter);
            }

            SelectInputs();
            created = doc.FeatureManager.CreateFeature(data);
        }

        if (created is null && profile is not null)
        {
            SelectInputs();
            int twistType = Math.Abs(twist) > 0 ? (int)swTwistControlType_e.swTwistControlConstantTwistAlongPath : (int)swTwistControlType_e.swTwistControlFollowPath;
            created = cut
                ? doc.FeatureManager.InsertCutSwept5(false, true, twistType, false, false, 0, 0, false, 0, 0, 0, 0, true, true, twist, true, false, false, false, false, 0, 0)
                : doc.FeatureManager.InsertProtrusionSwept4(false, true, twistType, false, false, 0, 0, false, 0, 0, 0, 0, merge, true, true, twist, true, false, 0, 0);
        }

        Feature feature = CheckCreated(doc, created, cut ? "swept cut" : "sweep", new Dictionary<string, object?> { ["helixPath"] = helixPath },
            "The profile must sit at the START of the path (for a helix: its reported startPoint), roughly perpendicular to it.")!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object Loft(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "loft");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        bool cut = string.Equals(StringArg(args, "mode"), "cut", StringComparison.OrdinalIgnoreCase);

        doc.ClearSelection2(true);
        JsonElement profiles = Prop(args, "profiles") ?? throw new ArgumentException("profiles is required");
        bool append = false;
        foreach (JsonElement p in profiles.EnumerateArray())
        {
            SelectResolved(doc, new[] { ResolveSingle(doc, p, s, "profiles") }, MarkLoftProfile, append, "profiles");
            append = true;
        }

        if (Prop(args, "guides") is JsonElement guides)
        {
            foreach (JsonElement g in guides.EnumerateArray())
            {
                SelectResolved(doc, ResolveSelectors(doc, g, s, "guides"), MarkLoftGuide, append: true, "guides");
            }
        }

        bool closed = BoolArg(args, "closed");
        bool merge = BoolArg(args, "merge", defaultValue: true);
        object? created = cut
            ? doc.FeatureManager.InsertCutBlend(closed, true, false, 1.0, (short)0, (short)0, false, 0, 0, (short)0, true, true)
            : doc.FeatureManager.InsertProtrusionBlend2(closed, true, false, 1.0, (short)0, (short)0, 1, 1, true, true, false, 0, 0, (short)0, merge, true, true, 0);
        Feature feature = CheckCreated(doc, created, cut ? "lofted cut" : "loft", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ shell / fillet / chamfer

    private static object Shell(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "shell");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        if (Prop(args, "remove_faces") is JsonElement faces)
        {
            SelectResolved(doc, ResolveSelectors(doc, faces, s, "remove_faces"), MarkShellFace, append: false, "remove_faces");
        }

        double thickness = DoubleArg(args, "thickness", 2) * s;
        doc.InsertFeatureShell(thickness, BoolArg(args, "outward"));
        Feature? feature = Try(() => doc.FeatureByPositionReverse(0)) as Feature;
        if (feature is null || !string.Equals(Try(() => feature.GetTypeName2()) as string, "Shell", StringComparison.OrdinalIgnoreCase))
        {
            throw FeatureFailed(doc, "shell", new Dictionary<string, object?> { ["thickness"] = thickness / s },
                "Wall thickness must be smaller than the smallest fillet/feature it has to offset.");
        }

        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object Fillet(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "fillet");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        double radius = DoubleArg(args, "radius", 1) * s;
        if (Prop(args, "full_round") is not null || Prop(args, "face_set1") is not null)
        {
            return FaceSetFillet(doc, args, s, radius);
        }

        JsonElement edges = Prop(args, "edges") ?? throw new ArgumentException("edges is required");
        List<ResolvedEntity> targets = ResolveSelectors(doc, edges, s, "edges");
        doc.ClearSelection2(true);
        SelectResolved(doc, targets, MarkFilletEdge, append: false, "edges");

        // Constant-radius fillets go through the feature-data API (FeatureFillet3 is obsolete for them).
        object? created = null;
        if (doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet) is SimpleFilletFeatureData2 data
            && data.Initialize((int)swSimpleFilletType_e.swConstRadiusFillet))
        {
            data.DefaultRadius = radius;
            TryVoid(() => data.PropagateToTangentFaces = true);
            TryVoid(() => data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular);
            TryVoid(() => data.OverflowType = (int)swFilletOverFlowType_e.swFilletOverFlowType_Default);
            doc.ClearSelection2(true);
            SelectResolved(doc, targets, MarkFilletEdge, append: false, "edges");
            created = doc.FeatureManager.CreateFeature(data);
        }

        if (created is null)
        {
            doc.ClearSelection2(true);
            SelectResolved(doc, targets, MarkFilletEdge, append: false, "edges");
            created = doc.FeatureManager.FeatureFillet3(
                (int)(swFeatureFilletOptions_e.swFeatureFilletPropagate | swFeatureFilletOptions_e.swFeatureFilletUniformRadius),
                radius, 0, 0, (int)swFeatureFilletType_e.swFeatureFilletType_Simple, 0, 0,
                null, null, null, null, null, null, null);
        }

        Feature feature = CheckCreated(doc, created, "fillet", new Dictionary<string, object?>
        {
            ["radius"] = radius / s,
            ["targets"] = targets.Select(t => t.Info).ToArray(),
        })!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // Full-round fillet (side face set, centre face set, side face set: rounds a rib or wall tip
    // completely) or face fillet (blends two face sets that need not share an edge).
    private static object FaceSetFillet(ModelDoc2 doc, JsonElement? args, double s, double radius)
    {
        bool fullRound = Prop(args, "full_round") is not null;
        var sets = new List<(int Which, List<ResolvedEntity> Faces, string Name)>();
        if (fullRound)
        {
            JsonElement fr = Prop(args, "full_round")!.Value;
            sets.Add(((int)swSimpleFilletWhichFaces_e.swFullRoundFilletSet1, ResolveSelectors(doc, Prop(fr, "side1") ?? throw new ArgumentException("full_round.side1 is required"), s, "full_round.side1"), "side1"));
            sets.Add(((int)swSimpleFilletWhichFaces_e.swFullRoundFilletCenterSet, ResolveSelectors(doc, Prop(fr, "center") ?? throw new ArgumentException("full_round.center is required"), s, "full_round.center"), "center"));
            sets.Add(((int)swSimpleFilletWhichFaces_e.swFullRoundFilletSet2, ResolveSelectors(doc, Prop(fr, "side2") ?? throw new ArgumentException("full_round.side2 is required"), s, "full_round.side2"), "side2"));
        }
        else
        {
            sets.Add(((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1, ResolveSelectors(doc, Prop(args, "face_set1")!.Value, s, "face_set1"), "face_set1"));
            sets.Add(((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2, ResolveSelectors(doc, Prop(args, "face_set2") ?? throw new ArgumentException("face_set2 is required"), s, "face_set2"), "face_set2"));
        }

        if (sets.Any(set => set.Faces.Any(f => f.Com is not Face2)))
        {
            throw WorkerException.Validation("FACES_REQUIRED", "Full-round and face fillets take face selectors.", new Dictionary<string, object?>());
        }

        // Face lists go in through SetFaces; some builds only take them from the selection (marks
        // 2 / 4 for the side sets, 512 for the full-round centre set). Try each way in turn.
        string? via = null;
        object? created = null;
        foreach (string attempt in new[] { "set_faces", "set_faces_wrapped", "selection_marks" })
        {
            doc.ClearSelection2(true);
            var data = (SimpleFilletFeatureData2)doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
            data.Initialize(fullRound ? (int)swSimpleFilletType_e.swFullRoundFillet : (int)swSimpleFilletType_e.swFaceFillet);
            if (!fullRound)
            {
                data.DefaultRadius = radius;
            }

            TryVoid(() => data.PropagateToTangentFaces = true);
            foreach ((int which, List<ResolvedEntity> faces, string _) in sets)
            {
                if (attempt == "set_faces")
                {
                    TryVoid(() => data.SetFaces(which, faces.Select(f => f.Com).ToArray()));
                }
                else if (attempt == "set_faces_wrapped")
                {
                    TryVoid(() => data.SetFaces(which, Wrap(faces)));
                }
                else
                {
                    int mark = which switch
                    {
                        (int)swSimpleFilletWhichFaces_e.swFullRoundFilletCenterSet => 512,
                        (int)swSimpleFilletWhichFaces_e.swFullRoundFilletSet2 or (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2 => 4,
                        _ => 2,
                    };
                    SelectResolved(doc, faces, mark, append: true, "faces");
                }
            }

            created = doc.FeatureManager.CreateFeature(data);
            if (created is not null)
            {
                via = attempt;
                break;
            }
        }
        Feature feature = CheckCreated(doc, created, fullRound ? "full-round fillet" : "face fillet", new Dictionary<string, object?>
        {
            ["faceSets"] = sets.ToDictionary(set => set.Name, set => (object?)set.Faces.Select(f => f.Info).ToArray()),
            ["radius"] = fullRound ? null : radius / s,
        }, fullRound
            ? new[] { "side1 and side2 are the two opposite faces of the rib/wall; center is the face between them (its tip)." }
            : new[] { "The radius must be large enough to reach both face sets." })!;
        return new { outcome = FeatureOutcome(doc, feature, args, s, StringArg(args, "name")), via };
    }

    private static object Chamfer(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "chamfer");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        double distance = DoubleArg(args, "distance", 1) * s;
        double? distance2 = Prop(args, "distance2") is not null ? DoubleArg(args, "distance2") * s : null;
        double angle = Deg(DoubleArg(args, "angle_deg", 45));
        JsonElement edges = Prop(args, "edges") ?? throw new ArgumentException("edges is required");
        List<ResolvedEntity> targets = ResolveSelectors(doc, edges, s, "edges");
        doc.ClearSelection2(true);
        SelectResolved(doc, targets, MarkChamferEdge, append: false, "edges");

        object? created = distance2 is double dd
            ? doc.FeatureManager.InsertFeatureChamfer((int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation, (int)swChamferType_e.swChamferDistanceDistance, distance, 0, dd, 0, 0, 0)
            : doc.FeatureManager.InsertFeatureChamfer((int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation, (int)swChamferType_e.swChamferAngleDistance, distance, angle, 0, 0, 0, 0);
        Feature feature = CheckCreated(doc, created, "chamfer", new Dictionary<string, object?> { ["targets"] = targets.Select(t => t.Info).ToArray() })!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ holes

    /// <summary>
    /// Holes built from sketches + cuts so plain, counterbore, and countersink all share
    /// one predictable code path (no Hole Wizard standards table needed).
    /// </summary>
    private static object Hole(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "hole");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);

        JsonElement on = Prop(args, "on") ?? throw new ArgumentException("on is required");
        JsonElement positions = Prop(args, "positions") ?? throw new ArgumentException("positions is required");
        string space = StringArg(args, "space") ?? "model";
        double diameter = DoubleArg(args, "diameter", 3.4);
        double? depth = Prop(args, "depth") is not null ? DoubleArg(args, "depth") : null;
        string baseName = StringArg(args, "name") ?? "Hole";
        var created = new List<string>();

        string SketchCircles(double dia, string label)
        {
            var entities = positions.EnumerateArray()
                .Select(p => new Dictionary<string, object?> { ["type"] = "circle", ["center"] = Vec(p), ["diameter"] = dia })
                .ToList();
            var sketchArgs = new Dictionary<string, object?>
            {
                ["path"] = StringArg(args, "path"),
                ["units"] = UnitsLabel(args),
                ["on"] = JsonSerializer.Deserialize<object>(on.GetRawText()),
                ["space"] = space,
                ["name"] = $"{baseName}_{label}_Sketch",
                ["entities"] = entities,
            };
            JsonElement sketchJson = JsonSerializer.SerializeToElement(sketchArgs);
            dynamic result = Sketch(sketchJson);
            return (string)result.sketchName;
        }

        object CutFrom(string sketch, double? cutDepth, string label, double draftDeg = 0)
        {
            var cutArgs = new Dictionary<string, object?>
            {
                ["path"] = StringArg(args, "path"),
                ["units"] = UnitsLabel(args),
                ["sketch"] = sketch,
                ["mode"] = "cut",
                ["end"] = cutDepth is null ? "through_all" : "blind",
                ["depth"] = cutDepth,
                ["draft_deg"] = draftDeg > 0 ? draftDeg : null,
                ["draft_outward"] = false,
                ["name"] = $"{baseName}_{label}",
                ["rollback_on_error"] = BoolArg(args, "rollback_on_error", true),
                ["auto_reverse"] = true,
            };
            object result = Extrude(JsonSerializer.SerializeToElement(cutArgs));
            created.Add($"{baseName}_{label}");
            return result;
        }

        // Draw every sketch on the untouched start face first: once the counterbore is cut,
        // the face under the hole centers is gone. Then cut, and undo everything on failure.
        var sketches = new List<string>();
        object? last = null;
        try
        {
            var cuts = new List<(string Sketch, double? Depth, string Label, double Draft)>();
            if (Prop(args, "counterbore") is JsonElement cb)
            {
                string cbSketch = SketchCircles(Num(cb, "diameter"), "CBore");
                sketches.Add(cbSketch);
                cuts.Add((cbSketch, Num(cb, "depth"), "CBore", 0));
            }

            if (Prop(args, "countersink") is JsonElement cs)
            {
                double head = Num(cs, "diameter");
                double halfAngle = (OptNum(cs, "angle_deg") ?? 90) / 2;
                // Drafted (tapering inward) cut from the head diameter down to the hole diameter.
                double csDepth = (head - diameter) / 2 / Math.Tan(Deg(halfAngle));
                string csSketch = SketchCircles(head, "CSink");
                sketches.Add(csSketch);
                cuts.Add((csSketch, csDepth, "CSink", halfAngle));
            }

            string holeSketch = SketchCircles(diameter, "Drill");
            sketches.Add(holeSketch);
            cuts.Add((holeSketch, depth, "Drill", 0));

            foreach (var cut in cuts)
            {
                last = CutFrom(cut.Sketch, cut.Depth, cut.Label, cut.Draft);
            }
        }
        catch
        {
            foreach (string feature in Enumerable.Reverse(created).Concat(sketches))
            {
                TryVoid(() => DeleteFeatureByName(doc, feature));
            }

            TryVoid(() => doc.EditRebuild3());
            throw;
        }

        return new { holes = positions.GetArrayLength(), features = created, result = last };
    }

    // ------------------------------------------------------------------ patterns / mirror

    private static void SelectSeeds(ModelDoc2 doc, JsonElement? args, double s, int featureMark, int bodyMark, bool append)
    {
        foreach (string name in NamesArg(args, "features"))
        {
            SelectResolved(doc, new[] { ResolveNamed(doc, name, "BODYFEATURE") }, featureMark, append, "features");
            append = true;
        }

        foreach (ResolvedEntity body in BodiesArg(doc, args, "bodies", s))
        {
            SelectResolved(doc, new[] { body }, bodyMark, append, "bodies");
            append = true;
        }

        if (!append)
        {
            throw WorkerException.Validation("NO_SEEDS", "Provide features or bodies to pattern/mirror.", new Dictionary<string, object?>());
        }
    }

    private static object PatternLinear(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "pattern_linear");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);

        JsonElement dir = Prop(args, "direction") ?? throw new ArgumentException("direction is required");
        SelectResolved(doc, new[] { ResolveSingle(doc, dir, s, "direction") }, MarkPatternDir1, append: false, "direction");
        int count2 = 1;
        double spacing2 = 0.01;
        bool reverse2 = false;
        if (Prop(args, "direction2") is JsonElement d2)
        {
            SelectResolved(doc, new[] { ResolveSingle(doc, Prop(d2, "direction")!.Value, s, "direction2.direction") }, MarkPatternDir2, append: true, "direction2");
            count2 = (int)Num(d2, "count");
            spacing2 = Num(d2, "spacing") * s;
            reverse2 = Flag(d2, "reverse");
        }

        SelectSeeds(doc, args, s, MarkPatternSeed, MarkPatternBody, append: true);
        var seedBodies = BodiesArg(doc, args, "bodies", s).Select(b => (Body2)b.Com!).ToList();
        var bodiesBefore = SolidBodies(doc).Select(BoxKey).ToList();
        int count = (int)DoubleArg(args, "count", 2);
        double spacing = DoubleArg(args, "spacing", 10) * s;
        bool reverse = BoolArg(args, "reverse");
        object? created = doc.FeatureManager.FeatureLinearPattern5(
            count, spacing, count2, spacing2,
            reverse, reverse2, "NULL", "NULL", false, false,
            false, false, false, false, false, false, false, false, 0, 0, false, false);
        Feature feature = CheckCreated(doc, created, "linear pattern", new Dictionary<string, object?>())!;

        // An edge/axis direction sign is arbitrary. When the caller states the intended
        // direction, measure where the instances landed and flip if they went the other way.
        double[]? actual = PatternOffset(doc, args, feature, seedBodies, bodiesBefore);
        if (Prop(args, "vector") is JsonElement vecEl && Prop(args, "direction2") is null)
        {
            double[] wanted = Normalize3(Vec(vecEl, 3));
            if (actual is null || Dot3(actual, wanted) < 0)
            {
                string flippedName = feature.Name;
                DeleteFeatureByName(doc, flippedName);
                doc.ClearSelection2(true);
                SelectResolved(doc, new[] { ResolveSingle(doc, dir, s, "direction") }, MarkPatternDir1, append: false, "direction");
                SelectSeeds(doc, args, s, MarkPatternSeed, MarkPatternBody, append: true);
                created = doc.FeatureManager.FeatureLinearPattern5(
                    count, spacing, count2, spacing2,
                    !reverse, reverse2, "NULL", "NULL", false, false,
                    false, false, false, false, false, false, false, false, 0, 0, false, false);
                feature = CheckCreated(doc, created, "linear pattern (flipped)", new Dictionary<string, object?>())!;
                actual = PatternOffset(doc, args, feature, seedBodies, bodiesBefore);
            }
        }

        object outcome = FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
        return new
        {
            outcome,
            instanceDirection = actual?.Select(x => Math.Round(x, 4)).ToArray(),
            note = actual is null ? "Pattern produced no new faces: instances may lie outside the body (try reverse or vector)." : null,
        };
    }

    /// <summary>
    /// Unit vector from the seeds to the pattern instances (face centroids for feature
    /// patterns, new-body centroids for body patterns), or null if the pattern made nothing.
    /// </summary>
    private static double[]? PatternOffset(ModelDoc2 doc, JsonElement? args, Feature pattern, List<Body2> seedBodies, List<string> bodiesBeforeBoxes)
    {
        TryVoid(() => doc.EditRebuild3());
        double[]? seedCenter;
        double[]? patternCenter;
        if (seedBodies.Count > 0)
        {
            seedCenter = BodiesCenter(seedBodies);
            // Body order is not stable; new bodies are the ones whose box matches no pre-existing body.
            var beforeKeys = new HashSet<string>(bodiesBeforeBoxes);
            var created = SolidBodies(doc).Where(b => !beforeKeys.Contains(BoxKey(b))).ToList();
            patternCenter = created.Count > 0 ? BodiesCenter(created) : null;
        }
        else
        {
            patternCenter = FacesCenter((Try(() => pattern.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>());
            var seedFaces = NamesArg(args, "features")
                .Select(n => FindFeatureByName(doc, n))
                .Where(f => f is not null)
                .SelectMany(f => (Try(() => f!.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>());
            seedCenter = FacesCenter(seedFaces);
        }

        if (patternCenter is null || seedCenter is null)
        {
            return null;
        }

        double[] delta = Sub(patternCenter, seedCenter);
        return Math.Sqrt(Dot3(delta, delta)) < 1e-9 ? null : Normalize3(delta);
    }

    private static string BoxKey(Body2 body) =>
        Try(() => body.GetBodyBox()) is double[] b ? string.Join(",", b.Take(6).Select(x => Math.Round(x, 7))) : Guid.NewGuid().ToString();

    private static double[]? BodiesCenter(IEnumerable<Body2> bodies)
    {
        double[]? box = null;
        foreach (Body2 body in bodies)
        {
            if (Try(() => body.GetBodyBox()) is double[] b && b.Length >= 6)
            {
                box = box is null ? b[..6] : MergeBoxes(box, b);
            }
        }

        return box is null ? null : BoxCenter(box);
    }

    private static double[]? FacesCenter(IEnumerable<Face2> faces)
    {
        double[]? box = null;
        foreach (Face2 face in faces)
        {
            if (Try(() => face.GetBox()) is double[] b && b.Length >= 6)
            {
                box = box is null ? b[..6] : MergeBoxes(box, b);
            }
        }

        return box is null ? null : BoxCenter(box);
    }

    private static object PatternCircular(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "pattern_circular");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);

        JsonElement axis = Prop(args, "axis") ?? throw new ArgumentException("axis is required");
        SelectResolved(doc, new[] { ResolveSingle(doc, axis, s, "axis") }, MarkPatternDir1, append: false, "axis");
        SelectSeeds(doc, args, s, MarkPatternSeed, MarkPatternBody, append: true);
        double angle = Deg(DoubleArg(args, "angle_deg", 360));
        object? created = doc.FeatureManager.FeatureCircularPattern5(
            (int)DoubleArg(args, "count", 4), angle, BoolArg(args, "reverse"), "NULL", false, true, false,
            false, false, false, 1, 0, "NULL", true);
        Feature feature = CheckCreated(doc, created, "circular pattern", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object Mirror(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "mirror");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);

        JsonElement plane = Prop(args, "plane") ?? throw new ArgumentException("plane is required");
        SelectResolved(doc, new[] { ResolveSingle(doc, plane, s, "plane") }, MarkMirrorPlane, append: false, "plane");
        SelectSeeds(doc, args, s, MarkMirrorSeed, MarkMirrorBody, append: true);
        bool bodies = Prop(args, "bodies") is not null;
        object? created = doc.FeatureManager.InsertMirrorFeature2(bodies, false, BoolArg(args, "merge", true) && bodies, false, (int)swFeatureScope_e.swFeatureScope_AllBodies);
        Feature feature = CheckCreated(doc, created, "mirror", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    /// <summary>Selects the single centerline (construction line) of a sketch as the revolve axis.</summary>
    private static string SelectSketchCenterline(ModelDoc2 doc, string sketchName, int mark)
    {
        Feature feature = FindFeatureByName(doc, sketchName)!;
        ISketch sketch = (ISketch)feature.GetSpecificFeature2();
        var lines = (sketch.GetSketchSegments() as object[] ?? Array.Empty<object>())
            .OfType<SketchSegment>()
            .Where(seg => seg.GetType() == (int)swSketchSegments_e.swSketchLINE && seg.ConstructionGeometry)
            .ToList();
        if (lines.Count != 1)
        {
            throw WorkerException.Validation(
                "REVOLVE_AXIS_REQUIRED",
                $"Sketch {sketchName} has {lines.Count} centerlines; add exactly one centerline or pass axis.",
                new Dictionary<string, object?> { ["sketch"] = sketchName },
                ["Add a {type: centerline, from, to} entity to the profile sketch.", "Or pass axis: an axis name or an edge selector."]);
        }

        if (!lines[0].Select4(true, NewSelectData(doc, mark)))
        {
            throw WorkerException.Validation("SELECT_FAILED", "Could not select the sketch centerline.", new Dictionary<string, object?>());
        }

        return $"centerline in {sketchName}";
    }

    // ------------------------------------------------------------------ reference geometry

    private const int RefPlaneCoincident = 4;
    private const int RefPlaneDistance = 8;
    private const int RefPlaneAngle = 16;
    private const int RefPlaneFlip = 256;

    private static object RefPlane(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "ref_plane");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        object? created;

        if (Prop(args, "through") is JsonElement through)
        {
            var refs = through.EnumerateArray().Select(t => ResolveSingle(doc, t, s, "through")).ToList();
            if (refs.Count != 3)
            {
                throw WorkerException.Validation("BAD_REFS", "through needs exactly three points/vertices.", new Dictionary<string, object?>());
            }

            for (int i = 0; i < 3; i++)
            {
                SelectResolved(doc, new[] { refs[i] }, i, append: i > 0, "through");
            }

            created = doc.FeatureManager.InsertRefPlane(RefPlaneCoincident, 0, RefPlaneCoincident, 0, RefPlaneCoincident, 0);
        }
        else
        {
            JsonElement from = Prop(args, "from") ?? throw new ArgumentException("from is required");
            SelectResolved(doc, new[] { ResolveSingle(doc, from, s, "from") }, 0, append: false, "from");
            if (Prop(args, "angle_deg") is not null)
            {
                JsonElement about = Prop(args, "about") ?? throw new ArgumentException("about (axis/edge) is required with angle_deg");
                SelectResolved(doc, new[] { ResolveSingle(doc, about, s, "about") }, 1, append: true, "about");
                double angle = DoubleArg(args, "angle_deg");
                int flags = RefPlaneAngle | (angle < 0 ? RefPlaneFlip : 0);
                created = doc.FeatureManager.InsertRefPlane(flags, Deg(Math.Abs(angle)), RefPlaneCoincident, 0, 0, 0);
            }
            else
            {
                double offset = DoubleArg(args, "offset", 0) * s;
                int flags = RefPlaneDistance | (offset < 0 ? RefPlaneFlip : 0);
                created = Math.Abs(offset) < 1e-12
                    ? doc.FeatureManager.InsertRefPlane(RefPlaneCoincident, 0, 0, 0, 0, 0)
                    : doc.FeatureManager.InsertRefPlane(flags, Math.Abs(offset), 0, 0, 0, 0);
            }
        }

        Feature feature = CheckCreated(doc, created, "reference plane", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object RefAxis(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "ref_axis");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        JsonElement from = Prop(args, "from") ?? throw new ArgumentException("from is required");
        SelectResolved(doc, ResolveSelectors(doc, from, s, "from"), 0, append: false, "from");
        if (!doc.InsertAxis2(true))
        {
            throw FeatureFailed(doc, "reference axis", new Dictionary<string, object?>(),
                "Use one cylindrical face, one linear edge, two planes, or two points.");
        }

        Feature feature = (Feature)doc.FeatureByPositionReverse(0);
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    // ------------------------------------------------------------------ bodies

    private static object DeleteBody(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "delete_body");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        SelectResolved(doc, BodiesArg(doc, args, "bodies", s), 0, append: false, "bodies");

        object? created = doc.FeatureManager.InsertDeleteBody2(false);
        Feature feature = CheckCreated(doc, created, "delete body", new Dictionary<string, object?>())!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }

    private static object MoveBody(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "move_body");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        var bodies = BodiesArg(doc, args, "bodies", s).Select(b => (Body2)b.Com!).ToList();
        if (Prop(args, "translate") is null && Prop(args, "rotate") is null)
        {
            throw WorkerException.Validation("NOTHING_TO_DO", "Provide translate and/or rotate.", new Dictionary<string, object?>());
        }

        List<string> features = MoveBodies(doc, bodies, args, s, BoolArg(args, "copy"));
        Feature feature = FindFeatureByName(doc, features[^1])!;
        object outcome = FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
        return features.Count > 1 ? new { outcome, features } : outcome;
    }


    private static string[] NamesArg(JsonElement? args, string name)
    {
        JsonElement? v = Prop(args, name);
        if (v is null)
        {
            return Array.Empty<string>();
        }

        return v.Value.ValueKind == JsonValueKind.Array
            ? v.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : v.Value.ValueKind == JsonValueKind.String ? new[] { v.Value.GetString()! } : Array.Empty<string>();
    }
}
