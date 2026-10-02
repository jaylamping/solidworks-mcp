using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SolidWorks.Interop.cosworks;
using SolidWorks.Interop.sldworks;

// Linear static FEA through SOLIDWORKS Simulation: material, fixtures, loads on
// selector-chosen faces, mesh, solve, and peak stress/displacement/FOS. The study is
// deleted afterwards unless keep_study is set, so the part is left as it was.
internal static partial class Program
{
    private sealed record FeaMaterial(string Name, double EPa, double Nu, double YieldPa, double DensityKgM3, string? Note);

    // Typical properties. Printed parts are anisotropic: layer (Z) strength is often 50-70% of these values.
    private static readonly Dictionary<string, FeaMaterial> FeaMaterials = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLA"] = new("PLA (printed, typical)", 3.5e9, 0.36, 50e6, 1240, "FDM; layer-direction strength is lower"),
        ["PETG"] = new("PETG (printed, typical)", 2.1e9, 0.38, 45e6, 1270, "FDM; layer-direction strength is lower"),
        ["ABS"] = new("ABS (printed, typical)", 2.2e9, 0.35, 40e6, 1040, "FDM; layer-direction strength is lower"),
        ["ASA"] = new("ASA (printed, typical)", 2.0e9, 0.35, 42e6, 1070, "FDM; layer-direction strength is lower"),
        ["PC"] = new("PC (printed, typical)", 2.3e9, 0.37, 60e6, 1200, "FDM; layer-direction strength is lower"),
        ["PA-CF"] = new("PA-CF (printed, typical)", 6.0e9, 0.35, 80e6, 1200, "FDM; strongly anisotropic"),
        ["PETG-CF"] = new("PETG-CF (printed, typical)", 4.0e9, 0.37, 50e6, 1300, "FDM; strongly anisotropic"),
        ["6061-T6"] = new("Aluminum 6061-T6", 68.9e9, 0.33, 275e6, 2700, null),
        ["7075-T6"] = new("Aluminum 7075-T6", 71.7e9, 0.33, 503e6, 2810, null),
        ["STEEL"] = new("Steel AISI 1020", 200e9, 0.29, 351e6, 7900, null),
        ["STAINLESS"] = new("Stainless 304", 193e9, 0.29, 215e6, 8000, null),
    };

    private static CosmosWorks AttachSimulation(ISldWorks app)
    {
        string revision = app.RevisionNumber();
        int major = int.TryParse(revision.Split('.')[0], out int m) ? m : 34;
        string[] progIds = { $"SldWorks.Simulation.{major - 15}", "SldWorks.Simulation" };
        CwAddincallback? cb = progIds.Select(p => Try(() => app.GetAddInObject(p)) as CwAddincallback).FirstOrDefault(x => x is not null);
        int loadResult = 0;
        if (cb is null)
        {
            string dll = Path.Combine(Path.GetDirectoryName(app.GetExecutablePath()) ?? @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS", "Simulation", "cosworks.dll");
            if (!File.Exists(dll))
            {
                dll = Path.Combine(app.GetExecutablePath(), "Simulation", "cosworks.dll");
            }

            loadResult = app.LoadAddIn(dll);
            cb = progIds.Select(p => Try(() => app.GetAddInObject(p)) as CwAddincallback).FirstOrDefault(x => x is not null);
        }

        return cb?.CosmosWorks ?? throw WorkerException.Worker(
            "SIMULATION_UNAVAILABLE",
            $"SOLIDWORKS Simulation add-in is not available (LoadAddIn returned {loadResult}; 7 = license error).",
            new Dictionary<string, object?> { ["revision"] = revision },
            ["Enable SOLIDWORKS Simulation under Tools > Add-Ins.", "Static studies need a Premium/Simulation license."]);
    }

    private static object[] Wrap(IEnumerable<ResolvedEntity> items) =>
        items.Select(i => (object)new DispatchWrapper(i.Com)).ToArray();

    private static object SimulateStatic(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "simulate_static");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        var timer = Stopwatch.StartNew();

        // Isolate the bodies under study (e.g. a bracket in a part that also holds an imported
        // actuator): a temporary Keep Body feature, removed again in finally.
        string? isolateFeature = null;
        if (Prop(args, "bodies") is not null)
        {
            List<ResolvedEntity> keep = BodiesArg(doc, args, "bodies", s);
            doc.ClearSelection2(true);
            SelectResolved(doc, keep, 0, append: false, "bodies");
            Feature keepFeature = doc.FeatureManager.InsertDeleteBody2(true)
                ?? throw FeatureFailed(doc, "keep-body isolation", new Dictionary<string, object?>());
            isolateFeature = keepFeature.Name;
            TryVoid(() => doc.EditRebuild3());
        }

        try
        {
            return SimulateStaticCore(app, doc, args, s, timer, isolateFeature is not null ? SolidBodies(doc).Count() : (int?)null);
        }
        finally
        {
            if (isolateFeature is not null)
            {
                TryVoid(() => DeleteFeatureByName(doc, isolateFeature));
                TryVoid(() => doc.EditRebuild3());
            }
        }
    }

    private static object SimulateStaticCore(ISldWorks app, ModelDoc2 doc, JsonElement? args, double s, Stopwatch timer, int? isolatedBodies)
    {
        CosmosWorks cw = AttachSimulation(app);
        CWModelDoc cwDoc = cw.ActiveDoc ?? throw WorkerException.Worker("SIMULATION_NO_DOC", "Simulation could not see the part (is it the active window?).", new Dictionary<string, object?>());
        CWStudyManager sm = cwDoc.StudyManager;
        string studyName = StringArg(args, "study_name") ?? $"mcp_static_{Guid.NewGuid().ToString("N")[..6]}";
        CWStudy study = sm.CreateNewStudy3(studyName, 0 /* static */, 0, out int err);
        if (study is null || err != 0)
        {
            throw WorkerException.Worker("STUDY_CREATE_FAILED", $"CreateNewStudy3 failed (swsStudyError_e {err}).", new Dictionary<string, object?> { ["study"] = studyName });
        }

        bool keep = BoolArg(args, "keep_study");
        try
        {
            FeaMaterial material = ApplyFeaMaterial(app, study, args);
            CWLoadsAndRestraintsManager lbc = study.LoadsAndRestraintsManager;

            var fixtureReport = new List<object>();
            JsonElement fixtures = Prop(args, "fixtures") ?? throw new ArgumentException("fixtures is required");
            foreach (JsonElement fx in fixtures.EnumerateArray())
            {
                List<ResolvedEntity> faces = ResolveSelectors(doc, Prop(fx, "faces") ?? throw new ArgumentException("fixture.faces is required"), s, "fixtures.faces");
                int type = (Str(fx, "type") ?? "fixed").ToLowerInvariant() switch
                {
                    "fixed" => 0,
                    "immovable" => 1,
                    "roller" or "sliding" => 3,
                    "hinge" or "fixed_hinge" => 4,
                    _ => throw WorkerException.Validation("BAD_FIXTURE", $"Unknown fixture type {Str(fx, "type")}", new Dictionary<string, object?>()),
                };
                lbc.AddRestraint(type, Wrap(faces), null, out err);
                if (err != 0)
                {
                    throw WorkerException.Worker("FIXTURE_FAILED", $"AddRestraint failed (swsRestraintError_e {err}).", new Dictionary<string, object?> { ["faces"] = faces.Select(f => f.Info).ToArray() });
                }

                fixtureReport.Add(new { type = Str(fx, "type") ?? "fixed", faces = faces.Count });
            }

            var loadReport = new List<object>();
            JsonElement loads = Prop(args, "loads") ?? throw new ArgumentException("loads is required");
            foreach (JsonElement ld in loads.EnumerateArray())
            {
                loadReport.Add(ApplyFeaLoad(doc, lbc, ld, s));
            }

            if (Prop(args, "gravity") is JsonElement g && g.ValueKind != JsonValueKind.False)
            {
                // Gravity direction reference: Top Plane normal (model Y). Value is set to 9.81 m/s^2 along -Y.
                doc.ClearSelection2(true);
                Feature? top = FindFeatureByName(doc, "Top Plane");
                CWGravity? gravity = top is null ? null : lbc.AddGravity(top, out err);
                loadReport.Add(new { type = "gravity", ok = gravity is not null && err == 0, error = err });
            }

            CWMesh mesh = study.Mesh;
            string quality = StringArg(args, "mesh_quality") ?? "high";
            mesh.Quality = quality == "draft" ? 0 : 1;
            mesh.MesherType = 0;
            mesh.GetDefaultElementSizeAndTolerance(0 /* mm */, out double element, out double tolerance);
            if (Prop(args, "element_size") is not null)
            {
                element = DoubleArg(args, "element_size") * s / 0.001;
                tolerance = element * 0.05;
            }

            err = study.CreateMesh(0 /* mm */, element, tolerance);
            if (err != 0 || mesh.IsMeshFailed2)
            {
                throw WorkerException.Worker("MESH_FAILED", $"Meshing failed (swsStudyMeshError_e {err}).", new Dictionary<string, object?> { ["elementSizeMm"] = element },
                    ["Try a smaller element_size, or simplify tiny features (small fillets/chamfers)."]);
            }

            err = study.RunAnalysis();
            if (err != 0)
            {
                throw WorkerException.Worker("ANALYSIS_FAILED", $"RunAnalysis failed (swsRunAnalysisError_e {err}).", new Dictionary<string, object?> { ["code"] = err },
                    ["30 = invalid loads/fixtures; 24 = solver failure (often an under-constrained part: add fixtures)."]);
            }

            CWResults results = study.Results;
            object[] vm = (object[])results.GetMinMaxStress(9 /* VON */, 0, 1, null, 3 /* MPa */, out err);
            object[] disp = (object[])results.GetMinMaxDisplacement(3 /* URES */, 1, null, 0 /* mm */, out int err2);
            double maxVm = Convert.ToDouble(vm[3]);
            double maxDisp = Convert.ToDouble(disp[3]);
            double[]? stressAt = NodeLocation(mesh, Convert.ToInt32(vm[2]), s);
            double[]? dispAt = NodeLocation(mesh, Convert.ToInt32(disp[2]), s);
            double fos = material.YieldPa / 1e6 / Math.Max(maxVm, 1e-9);

            return new
            {
                document = DescribeDocument(doc),
                study = keep ? studyName : null,
                material = new
                {
                    material.Name,
                    youngsModulusMPa = material.EPa / 1e6,
                    poisson = material.Nu,
                    yieldMPa = material.YieldPa / 1e6,
                    note = material.Note,
                },
                fixtures = fixtureReport,
                loads = loadReport,
                mesh = new
                {
                    quality,
                    elementSizeMm = Math.Round(element, 3),
                    nodes = Try(() => mesh.NodeCount),
                    elements = Try(() => mesh.ElementCount),
                },
                results = new
                {
                    maxVonMisesMPa = Math.Round(maxVm, 3),
                    maxVonMisesAt = stressAt,
                    maxDisplacementMm = Math.Round(maxDisp, 5),
                    maxDisplacementAt = dispAt,
                    factorOfSafety = Math.Round(fos, 3),
                    units = UnitsLabel(args),
                },
                isolatedBodies,
                seconds = Math.Round(timer.Elapsed.TotalSeconds, 1),
                notes = new[]
                {
                    "Linear static, small displacement. Peak stress at sharp re-entrant corners and fixture edges is a mesh singularity; judge those regions by trend, not absolute value.",
                    "factorOfSafety = yield / max von Mises.",
                },
            };
        }
        finally
        {
            if (!keep)
            {
                TryVoid(() => sm.DeleteStudy(studyName));
            }
        }
    }

    private static double[]? NodeLocation(CWMesh mesh, int node, double scale)
    {
        try
        {
            mesh.GetNodeLocation(node, out double x, out double y, out double z);
            return Round(new[] { x, y, z }, scale, 3);
        }
        catch
        {
            return null;
        }
    }

    private static FeaMaterial ApplyFeaMaterial(ISldWorks app, CWStudy study, JsonElement? args)
    {
        FeaMaterial? chosen = null;
        string? libraryName = null;
        if (Prop(args, "custom_material") is JsonElement cm)
        {
            chosen = new FeaMaterial(Str(cm, "name") ?? "custom", Num(cm, "youngs_modulus_mpa") * 1e6, Num(cm, "poisson", 0.35), Num(cm, "yield_mpa") * 1e6, Num(cm, "density_kg_m3", 1200), null);
        }
        else
        {
            string name = StringArg(args, "material") ?? "PLA";
            if (!FeaMaterials.TryGetValue(name, out chosen))
            {
                libraryName = name;
            }
        }

        CWSolidManager solids = study.SolidManager;
        string library = Path.Combine(app.GetExecutablePath(), "lang", "english", "sldmaterials", "solidworks materials.sldmat");
        for (int c = 0; c < solids.ComponentCount; c++)
        {
            CWSolidComponent comp = solids.GetComponentAt(c, out _);
            for (int b = 0; b < comp.SolidBodyCount; b++)
            {
                CWSolidBody body = comp.GetSolidBodyAt(b, out _);
                if (libraryName is not null)
                {
                    if (!body.SetLibraryMaterial2(library, libraryName))
                    {
                        throw WorkerException.Validation("MATERIAL_NOT_FOUND", $"'{libraryName}' is neither a built-in FEA material nor in the SOLIDWORKS library.", new Dictionary<string, object?>(),
                            [$"Built-ins: {string.Join(", ", FeaMaterials.Keys)}", "Or pass custom_material {youngs_modulus_mpa, poisson, yield_mpa}."]);
                    }

                    continue;
                }

                CWMaterial mat = body.GetDefaultMaterial();
                mat.MaterialUnits = 0; // SI
                mat.MaterialName = chosen!.Name;
                mat.SetPropertyByName2("EX", chosen.EPa, false);
                mat.SetPropertyByName2("NUXY", chosen.Nu, false);
                mat.SetPropertyByName2("DENS", chosen.DensityKgM3, false);
                mat.SetPropertyByName2("SIGYLD", chosen.YieldPa, false);
                mat.SetPropertyByName2("SIGXT", chosen.YieldPa * 1.1, false);
                int rc = body.SetSolidBodyMaterial(mat);
                if (rc != 0 && rc != 32)
                {
                    throw WorkerException.Worker("MATERIAL_FAILED", $"SetSolidBodyMaterial failed (swsMaterialErrorWarning_e {rc}).", new Dictionary<string, object?>());
                }
            }
        }

        if (libraryName is not null)
        {
            CWSolidBody first = solids.GetComponentAt(0, out _).GetSolidBodyAt(0, out _);
            CWMaterial m = first.GetSolidBodyMaterial();
            double Prop2(string n) => Try(() => m.GetPropertyByName2(0, n, out bool _)) as double? ?? 0;
            return new FeaMaterial(libraryName, Prop2("EX"), Prop2("NUXY"), Prop2("SIGYLD") > 0 ? Prop2("SIGYLD") : Prop2("SIGXT"), Prop2("DENS"), Prop2("SIGYLD") > 0 ? null : "library material has no yield strength; FOS uses tensile strength");
        }

        return chosen!;
    }

    private static object ApplyFeaLoad(ModelDoc2 doc, CWLoadsAndRestraintsManager lbc, JsonElement ld, double s)
    {
        string type = (Str(ld, "type") ?? "force").ToLowerInvariant();
        List<ResolvedEntity> faces = ResolveSelectors(doc, Prop(ld, "faces") ?? throw new ArgumentException("load.faces is required"), s, "loads.faces");
        object[] entities = Wrap(faces);
        int err;
        switch (type)
        {
            case "force":
            {
                double value = Num(ld, "value_n");
                if (Prop(ld, "direction") is JsonElement dirEl)
                {
                    // Components along model X/Y/Z, referenced to Front Plane (in-plane X, Y; normal Z).
                    double[] dir = Normalize3(Vec(dirEl, 3));
                    double[] comps = dir.Select(d => d * value).ToArray();
                    int ucode = (Math.Abs(comps[0]) > 0 ? 1 : 0) | (Math.Abs(comps[1]) > 0 ? 2 : 0) | (Math.Abs(comps[2]) > 0 ? 4 : 0);
                    Feature front = FindFeatureByName(doc, "Front Plane") ?? throw new InvalidOperationException("Front Plane not found");
                    CWForce force = lbc.AddForce3(0, 0, 2, 0, 0, 0, null, null, false, false, 0, 0, ucode, 0,
                        new double[] { comps[0], comps[1], comps[2], 0, 0, 0 }, false, false, entities, front, false, out err);
                    if (force is null || err != 0)
                    {
                        throw WorkerException.Worker("LOAD_FAILED", $"AddForce3 (directional) failed (swsForceError_e {err}).", new Dictionary<string, object?>());
                    }

                    return new { type, valueN = value, direction = dir, faces = faces.Count };
                }
                else
                {
                    CWForce force = lbc.AddForce3(1, 0, -1, 0, 0, 0, null, null, false, false, 0, 0, 0, value,
                        new double[] { 1, 1, 1, 1, 1, 1 }, false, false, entities, null, false, out err);
                    if (force is null || err != 0)
                    {
                        throw WorkerException.Worker("LOAD_FAILED", $"AddForce3 (normal) failed (swsForceError_e {err}).", new Dictionary<string, object?>());
                    }

                    return new { type, valueN = value, direction = "normal (into face)", faces = faces.Count };
                }
            }
            case "pressure":
            {
                double mpa = Num(ld, "value_mpa");
                CWPressure p = lbc.AddPressure(0, entities, null, out err);
                if (p is null || err != 0)
                {
                    throw WorkerException.Worker("LOAD_FAILED", $"AddPressure failed (swsPressureError_e {err}).", new Dictionary<string, object?>());
                }

                p.PressureBeginEdit();
                p.Unit = 0; // Pa (swsStrengthUnit_e)
                p.Value = mpa * 1e6;
                int end = p.PressureEndEdit();
                double applied = (Try(() => p.Value) as double?) ?? 0;
                if (end != 0 || Math.Abs(applied) < 1e-12)
                {
                    throw WorkerException.Worker("LOAD_FAILED", $"Pressure edit did not apply (PressureEndEdit {end}, value read back {applied}).", new Dictionary<string, object?>());
                }

                return new { type, valueMPa = mpa, faces = faces.Count, appliedPa = applied, unitCode = Try(() => p.Unit) };
            }
            default:
                throw WorkerException.Validation("BAD_LOAD", $"Unknown load type '{type}' (use force or pressure).", new Dictionary<string, object?>());
        }
    }
}
