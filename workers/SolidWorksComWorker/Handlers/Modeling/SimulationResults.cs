using System.Text.Json;
using SolidWorks.Interop.cosworks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Result post-processing for simulate_static: stress distribution (percentiles, peak away from
// fixtures), face probes, hot-spot diagnostics, plot images, natural frequencies.
internal static partial class Program
{
    private sealed class FeaNodes
    {
        public required Dictionary<int, double[]> Xyz { get; init; } // meters
        public required Dictionary<int, double> VonMises { get; init; } // MPa
    }

    private static FeaNodes ReadFeaNodes(CWResults results, CWMesh mesh)
    {
        // GetNodes: [node, x, y, z] (m). GetStress (nodal): [node, SX, SY, SZ, TXY, TYZ, TXZ, P1, P2, P3, VON, INT].
        object[] nodes = (object[])mesh.GetNodes();
        var xyz = new Dictionary<int, double[]>(nodes.Length / 4);
        for (int i = 0; i + 3 < nodes.Length; i += 4)
        {
            xyz[Convert.ToInt32(nodes[i])] = [Convert.ToDouble(nodes[i + 1]), Convert.ToDouble(nodes[i + 2]), Convert.ToDouble(nodes[i + 3])];
        }

        object[] stress = (object[])results.GetStress(0, 1, null, 3 /* MPa */, out int err);
        if (err != 0)
        {
            throw WorkerException.Worker("RESULTS_FAILED", $"GetStress failed ({err}).", new Dictionary<string, object?>());
        }

        var vm = new Dictionary<int, double>(stress.Length / 12);
        for (int i = 0; i + 11 < stress.Length; i += 12)
        {
            vm[Convert.ToInt32(stress[i])] = Convert.ToDouble(stress[i + 10]);
        }

        return new FeaNodes { Xyz = xyz, VonMises = vm };
    }

    private static int[] NodesOn(CWResults results, IEnumerable<ResolvedEntity> faces)
    {
        object raw = results.GetStressForEntities2(true, 9, 1, null, Wrap(faces), 3, out int err);
        if (err != 0 || raw is not object[] pairs)
        {
            return [];
        }

        var ids = new int[pairs.Length / 2];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = Convert.ToInt32(pairs[2 * i]);
        }

        return ids;
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p / 100 * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static object StressDistribution(FeaNodes fea, CWResults results, List<ResolvedEntity> fixtureFaces, double exclusionM, double s, FeaMaterial material)
    {
        double[] sorted = fea.VonMises.Values.OrderBy(v => v).ToArray();

        // Peak away from the supports: fixed faces create singular stress at their edges that grows
        // without bound as the mesh is refined. Excluding nodes within a couple of elements of the
        // fixture faces gives a peak that converges and compares fairly between variants.
        object? away = null;
        int[] fixtureNodes = NodesOn(results, fixtureFaces);
        if (fixtureNodes.Length > 0)
        {
            double[][] anchors = fixtureNodes.Where(fea.Xyz.ContainsKey).Select(n => fea.Xyz[n]).ToArray();
            double r2 = exclusionM * exclusionM;
            int bestNode = -1;
            double best = double.MinValue;
            foreach ((int node, double v) in fea.VonMises.OrderByDescending(kv => kv.Value))
            {
                if (v <= best || !fea.Xyz.TryGetValue(node, out double[]? p))
                {
                    continue;
                }

                bool near = false;
                foreach (double[] a in anchors)
                {
                    double dx = p[0] - a[0], dy = p[1] - a[1], dz = p[2] - a[2];
                    if (dx * dx + dy * dy + dz * dz < r2)
                    {
                        near = true;
                        break;
                    }
                }

                if (!near)
                {
                    best = v;
                    bestNode = node;
                    break; // values are visited in descending order
                }
            }

            if (bestNode >= 0)
            {
                away = new
                {
                    maxVonMisesMPa = Math.Round(best, 3),
                    at = Round(fea.Xyz[bestNode], s, 3),
                    factorOfSafety = Math.Round(material.YieldPa / 1e6 / Math.Max(best, 1e-9), 3),
                    excludedWithin = Math.Round(exclusionM / s, 3),
                };
            }
        }

        return new
        {
            nodes = sorted.Length,
            vonMisesPercentilesMPa = new
            {
                p50 = Math.Round(Percentile(sorted, 50), 3),
                p90 = Math.Round(Percentile(sorted, 90), 3),
                p99 = Math.Round(Percentile(sorted, 99), 3),
                p99_9 = Math.Round(Percentile(sorted, 99.9), 3),
            },
            awayFromFixtures = away,
        };
    }

    private static object StressProbes(ModelDoc2 doc, FeaNodes fea, CWResults results, JsonElement probes, double s, FeaMaterial material)
    {
        var report = new List<object>();
        foreach (JsonElement p in probes.EnumerateArray())
        {
            List<ResolvedEntity> faces = ResolveSelectors(doc, Prop(p, "faces") ?? throw new ArgumentException("probe.faces is required"), s, "probes.faces");
            int[] nodes = NodesOn(results, faces);
            double[] values = nodes.Where(fea.VonMises.ContainsKey).Select(n => fea.VonMises[n]).ToArray();
            if (values.Length == 0)
            {
                report.Add(new { name = Str(p, "name"), faces = faces.Count, error = "no mesh nodes on these faces" });
                continue;
            }

            int maxNode = nodes.Where(fea.VonMises.ContainsKey).MaxBy(n => fea.VonMises[n]);
            report.Add(new
            {
                name = Str(p, "name"),
                faces = faces.Count,
                nodes = values.Length,
                maxVonMisesMPa = Math.Round(values.Max(), 3),
                maxAt = fea.Xyz.TryGetValue(maxNode, out double[]? at) ? Round(at, s, 3) : null,
                meanVonMisesMPa = Math.Round(values.Average(), 3),
                factorOfSafety = Math.Round(material.YieldPa / 1e6 / Math.Max(values.Max(), 1e-9), 3),
            });
        }

        return report;
    }

    private static object StressHotspots(FeaNodes fea, CWResults results, double s)
    {
        int rc = results.RunStressHotSpotDiagnostics(5, true, out bool found);
        object nodes = null!;
        TryVoid(() => results.GetDetectedHotSpotNodes(out nodes));
        int[] ids = nodes is Array a ? a.Cast<object>().Select(Convert.ToInt32).ToArray() : [];
        return new
        {
            found,
            count = ids.Length,
            top = ids.Where(fea.VonMises.ContainsKey)
                .OrderByDescending(n => fea.VonMises[n])
                .Take(10)
                .Select(n => new { vonMisesMPa = Math.Round(fea.VonMises[n], 3), at = fea.Xyz.TryGetValue(n, out double[]? p) ? Round(p, s, 3) : null })
                .ToArray(),
            note = rc != 0 ? $"diagnostics returned {rc}" : found ? "Hot spots are regions where stress keeps rising with mesh refinement (sharp corners, point contacts); add a fillet or judge by a probe nearby." : "No hot spots detected.",
        };
    }

    private static object FrequencyModes(CWResults results, bool free)
    {
        // [mode, rad/s, Hz, period s] per mode; mass participation [mode, X, Y, Z] fractions.
        object[] raw = (object[])results.GetResonantFrequencies(out int err);
        if (err != 0)
        {
            throw WorkerException.Worker("RESULTS_FAILED", $"GetResonantFrequencies failed ({err}).", new Dictionary<string, object?>());
        }

        object? mass = Try(() => results.GetMassParticipation(out int _));
        object[]? mp = mass as object[];
        int mpStride = mp is not null && raw.Length > 0 ? mp.Length / (raw.Length / 4) : 0;
        var modes = new List<object>();
        for (int i = 0; i + 3 < raw.Length; i += 4)
        {
            int k = i / 4;
            double hz = Convert.ToDouble(raw[i + 2]);
            double[]? participation = mp is not null && mpStride >= 4
                ? mp.Skip(k * mpStride + mpStride - 3).Take(3).Select(v => Math.Round(Convert.ToDouble(v), 4)).ToArray()
                : null;
            modes.Add(new
            {
                mode = Convert.ToInt32(raw[i]),
                hz = Math.Round(hz, 2),
                massParticipationXYZ = participation,
                rigidBody = free && hz < 1.0,
            });
        }

        return new { modes, massParticipationRaw = mp is not null && mpStride < 4 ? RawHead(mp, 30) : null };
    }

    private static object RawHead(object? value, int n = 40) =>
        value is Array a
            ? new { length = a.Length, head = a.Cast<object?>().Take(n).ToArray() }
            : new { length = -1, head = Array.Empty<object?>() };

    private static object SavePlotImage(ISldWorks app, ModelDoc2 doc, CWResults results, JsonElement spec, bool frequency)
    {
        string outputPath = PathGuard.AssertAllowedPath(Str(spec, "path") ?? throw new ArgumentException("plot.path is required"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        string kind = (Str(spec, "result") ?? (frequency ? "mode_shape" : "von_mises")).ToLowerInvariant();
        (int type, int component, int units) = kind switch
        {
            "von_mises" or "stress" => (2, 9, 3),
            "displacement" or "mode_shape" => (1, 3, 0),
            "fos" or "factor_of_safety" => (4, 0, 0),
            _ => throw WorkerException.Validation("BAD_PLOT", $"Unknown plot result '{kind}' (von_mises, displacement, fos, mode_shape).", new Dictionary<string, object?>()),
        };
        CWPlot plot = results.CreatePlot(type, component, units, false, out int err);
        if (plot is null || err != 0)
        {
            throw WorkerException.Worker("PLOT_FAILED", $"CreatePlot failed (swsResultPlotErrorCode_e {err}).", new Dictionary<string, object?> { ["result"] = kind });
        }

        if (frequency)
        {
            TryVoid(() => plot.SetModeShape(IntArg(spec, "mode", 1)));
        }

        TryVoid(() => plot.ShowDeformedPlot(Flag(spec, "deformed", true), 0, 0, true));
        plot.ActivatePlot();
        string view = (Str(spec, "view") ?? "isometric").ToLowerInvariant();
        int errors = 0;
        int warnings = 0;
        bool ok = WithReferenceGeometryHidden(app, doc, () =>
        {
            TryVoid(() => doc.ClearSelection2(true));
            TryVoid(() => doc.ShowNamedView2("*" + char.ToUpperInvariant(view[0]) + view[1..], -1));
            TryVoid(() => doc.ViewZoomtofit2());
            TryVoid(() => doc.GraphicsRedraw2());
            return doc.Extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
        });
        object[]? minMax = Try(() => plot.GetMinMaxResultValues(out int _)) as object[];
        return new
        {
            path = outputPath,
            ok = ok && File.Exists(outputPath),
            result = kind,
            min = minMax?.Length >= 4 ? Math.Round(Convert.ToDouble(minMax[1]), 5) : (double?)null,
            max = minMax?.Length >= 4 ? Math.Round(Convert.ToDouble(minMax[3]), 5) : (double?)null,
        };
    }
}
