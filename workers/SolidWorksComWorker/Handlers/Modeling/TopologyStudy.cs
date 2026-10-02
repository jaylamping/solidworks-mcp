using System.Text.Json;
using SolidWorks.Interop.cosworks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Topology optimization (SOLIDWORKS Simulation "Topology Study"): which material carries the load
// and which can be removed, for a stiffness goal with a mass target or a minimum-mass goal with
// strength/stiffness limits, plus manufacturing controls. The result is the "Material Mass" plot,
// saved as images from the requested views; the part itself is not changed.
internal static partial class Program
{
    private static object SetupTopology(ModelDoc2 doc, CWStudy study, JsonElement? args, double s)
    {
        JsonElement topo = Prop(args, "topology") ?? JsonDocument.Parse("{}").RootElement;
        CWTopologyStudyManager tm = study.TopologyStudyManager;
        var report = new Dictionary<string, object?>();
        string goal = (Str(topo, "goal") ?? "stiffness").ToLowerInvariant();
        int goalType = goal switch
        {
            "stiffness" => 0,
            "min_displacement" => 1,
            "min_mass" => 2,
            _ => throw WorkerException.Validation("BAD_TOPOLOGY_GOAL", $"Unknown goal '{goal}' (stiffness, min_displacement, min_mass).", new Dictionary<string, object?>()),
        };
        report["goal"] = goal;

        // The goal is committed first: stiffness/displacement goals then own a default mass
        // constraint ("Mass Constraint 1"), which is edited rather than duplicated (only one allowed).
        tm.BeginEdit();
        tm.CreateGoal(goalType);
        int end = tm.EndEdit();
        if (end != 0)
        {
            throw WorkerException.Worker("TOPOLOGY_SETUP_FAILED", $"Setting the topology goal failed (swsTopologyStudyError_e {end}).", new Dictionary<string, object?> { ["goal"] = goal });
        }

        tm.BeginEdit();
        if (goalType != 2)
        {
            double reduce = Num(topo, "mass_reduction_percent", 50);
            CWTopologyMassConstraint mc = Try(() => tm.GetMassConstraint("Mass Constraint 1", out int _)) as CWTopologyMassConstraint
                ?? tm.CreateMassConstraint(out int _);
            mc.BeginEdit();
            mc.SetMassPreference(1 /* percentage */);
            mc.SetValue(reduce);
            int mcEnd = mc.EndEdit();
            report["massReductionPercent"] = reduce;
            if (mcEnd != 0)
            {
                report["massConstraintError"] = mcEnd;
            }
        }

        var limits = new List<object>();
        if (OptNum(topo, "min_factor_of_safety") is double fos)
        {
            CWTopologyFactorOfSafetyConstraint c = tm.CreateFactorOfSafetyConstraint(out int err);
            c.BeginEdit();
            c.SetComponent(0);
            c.SetComparator(1 /* greater than */);
            c.SetValue(fos);
            limits.Add(new { type = "min_factor_of_safety", value = fos, create = err, end = c.EndEdit() });
        }

        if (OptNum(topo, "max_stress_mpa") is double stress)
        {
            CWTopologyStressConstraint c = tm.CreateStressConstraint(out int err);
            c.BeginEdit();
            c.SetComponent(0);
            c.SetComparator(0 /* less than */);
            c.SetValuationPreference(0 /* absolute */);
            c.SetUnit(3 /* N/mm^2 */);
            c.SetValue(stress);
            limits.Add(new { type = "max_stress_mpa", value = stress, create = err, end = c.EndEdit() });
        }

        if (OptNum(topo, "max_displacement") is double disp)
        {
            CWTopologyDisplacementConstraint c = tm.CreateDisplacementConstraint(out int err);
            c.BeginEdit();
            c.SetComponent(3 /* URES */);
            c.SetComparator(0 /* less than */);
            c.SetValuationPreference(0 /* absolute */);
            c.SetUnit(0 /* mm */);
            c.SetValue(disp * s * 1000);
            c.SetLocationPreference(0 /* auto: max over the model */);
            limits.Add(new { type = "max_displacement_mm", value = disp * s * 1000, create = err, end = c.EndEdit() });
        }

        if (goalType == 2 && limits.Count == 0)
        {
            tm.EndEdit();
            throw WorkerException.Validation("TOPOLOGY_LIMIT_REQUIRED", "goal min_mass needs a limit: min_factor_of_safety, max_stress_mpa or max_displacement.", new Dictionary<string, object?>());
        }

        report["limits"] = limits;

        if (OptNum(topo, "min_member_thickness") is double minT)
        {
            CWTopologyThicknessControl tc = tm.CreateThicknessControl(out int err);
            tc.BeginEdit();
            tc.SetIncludeMinMemberThickness2(true);
            tc.SetMinimumMemberThicknessUnit(0 /* mm */);
            tc.SetMinimumMemberThickness(minT * s * 1000);
            int tcEnd = tc.EndEdit();
            report["minMemberThicknessMm"] = minT * s * 1000;
            if (err != 0 || tcEnd != 0)
            {
                report["thicknessControlError"] = new { err, tcEnd };
            }
        }

        var preserved = new List<object>();
        foreach (JsonElement pr in Prop(topo, "preserve") is JsonElement prs ? prs.EnumerateArray().ToArray() : [])
        {
            List<ResolvedEntity> faces = ResolveSelectors(doc, Prop(pr, "faces") ?? throw new ArgumentException("topology.preserve.faces is required"), s, "topology.preserve.faces");
            var pc = tm.CreatePreservedRegionControl(out int err);
            pc.BeginEdit();
            pc.SelectFaces(Wrap(faces));
            if (OptNum(pr, "depth") is double depth)
            {
                pc.SetIncludeRegionDepth2(true);
                pc.SetAreaDepthUnit(0);
                pc.SetAreaDepth(depth * s * 1000);
            }

            preserved.Add(new { faces = faces.Count, create = err, end = pc.EndEdit() });
        }

        report["preserved"] = preserved;

        if (Prop(topo, "symmetry") is JsonElement sym)
        {
            JsonElement planes = Prop(sym, "planes") ?? throw new ArgumentException("topology.symmetry.planes is required");
            object[] planeObjs = planes.EnumerateArray().Select(p => ResolveSingle(doc, p, s, "topology.symmetry.planes").Com!).ToArray();
            CWTopologySymmetryControl sc = tm.CreateSymmetryControl(out int err);
            sc.BeginEdit();
            sc.SelectSymmetryType(planeObjs.Length switch { 1 => 0, 2 => 1, _ => 2 });
            sc.SelectFirstSymmetryPlane(planeObjs[0]);
            if (planeObjs.Length > 1)
            {
                sc.SelectSecondSymmetryPlane(planeObjs[1]);
            }

            if (planeObjs.Length > 2)
            {
                sc.SelectThirdSymmetryPlane(planeObjs[2]);
            }

            report["symmetry"] = new { planes = planeObjs.Length, create = err, end = sc.EndEdit() };
        }

        end = tm.EndEdit();
        if (end != 0)
        {
            throw WorkerException.Worker("TOPOLOGY_SETUP_FAILED", $"Topology constraints/controls were rejected (swsTopologyStudyError_e {end}).", new Dictionary<string, object?> { ["setup"] = report });
        }

        TryVoid(() => study.TopologyStudyOptions.SetPreservedRegionSetting(2 /* loads and fixtures */));
        return report;
    }

    private static object TopologyResults(ISldWorks app, ModelDoc2 doc, CWResults results, JsonElement? args)
    {
        string[] names = (Try(() => results.GetPlotNames()) as object[] ?? []).OfType<string>().ToArray();
        string? massPlot = names.FirstOrDefault(n =>
        {
            int type = -1;
            string comp = "";
            bool nodal = false, deformed = false;
            double scale = 0;
            TryVoid(() => results.GetPlotDefinition(n, out type, out comp, out nodal, out deformed, out scale));
            return type == 70;
        }) ?? names.FirstOrDefault(n => n.StartsWith("Material Mass", StringComparison.OrdinalIgnoreCase));

        var images = new List<object>();
        if (Prop(args, "plot") is JsonElement spec && massPlot is not null)
        {
            string basePath = PathGuard.AssertAllowedPath(Str(spec, "path") ?? throw new ArgumentException("plot.path is required"));
            Directory.CreateDirectory(Path.GetDirectoryName(basePath) ?? ".");
            string[] views = Prop(spec, "views") is JsonElement vs
                ? vs.EnumerateArray().Select(v => v.GetString() ?? "isometric").ToArray()
                : [Str(spec, "view") ?? "isometric"];
            CWPlot? plot = Try(() => results.GetPlot(massPlot, out int _)) as CWPlot;
            TryVoid(() => plot?.ActivatePlot());
            for (int i = 0; i < views.Length; i++)
            {
                string view = views[i].ToLowerInvariant();
                string outPath = i == 0 ? basePath : Path.Combine(Path.GetDirectoryName(basePath) ?? ".", $"{Path.GetFileNameWithoutExtension(basePath)}_{view}{Path.GetExtension(basePath)}");
                int errors = 0;
                int warnings = 0;
                bool ok = WithReferenceGeometryHidden(app, doc, () =>
                {
                    TryVoid(() => doc.ClearSelection2(true));
                    TryVoid(() => doc.ShowNamedView2("*" + char.ToUpperInvariant(view[0]) + view[1..], -1));
                    TryVoid(() => doc.ViewZoomtofit2());
                    TryVoid(() => doc.GraphicsRedraw2());
                    return doc.Extension.SaveAs(outPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                });
                images.Add(new { view, path = outPath, ok = ok && File.Exists(outPath) });
            }
        }

        return new
        {
            plot = massPlot,
            images,
            note = "The Material Mass plot shows the material the optimizer keeps (load paths). Redesign by hand along it: keep the bosses and mounting faces, connect them with ribs/webs following the kept regions, and pocket out the rest; then verify the new design with a static study.",
        };
    }
}
