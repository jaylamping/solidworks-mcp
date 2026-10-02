using System.Runtime.InteropServices;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// FDM printability: overhangs, bed contact, bed fit and mass for each axis-aligned
// print orientation, computed from the part's tessellation.
internal static partial class Program
{
    private static readonly Dictionary<string, double> FilamentDensity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLA"] = 1.24, ["PETG"] = 1.27, ["ABS"] = 1.04, ["ASA"] = 1.07, ["TPU"] = 1.21,
        ["PA"] = 1.14, ["NYLON"] = 1.14, ["PA-CF"] = 1.20, ["PETG-CF"] = 1.30, ["PC"] = 1.20,
    };

    private sealed record Tri(double[] A, double[] B, double[] C, double[] N, double Area, Face2? Face = null);

    /// <summary>
    /// Display tessellation of every solid face. A part opened from disk has none until it is
    /// shown, so retry after rebuilding and redrawing the (activated) document.
    /// </summary>
    private static List<Tri> TessellatePart(ModelDoc2 doc)
    {
        List<Tri> tris = TessellateOnce(doc);
        if (tris.Count == 0 && SolidBodies(doc).Any())
        {
            TryVoid(() => doc.ForceRebuild3(false));
            TryVoid(() => doc.ViewZoomtofit2());
            TryVoid(() => doc.GraphicsRedraw2());
            tris = TessellateOnce(doc);
        }

        if (tris.Count == 0)
        {
            tris = TessellateFromModel(doc);
        }

        return tris;
    }

    /// <summary>Geometry-based tessellation (no graphics needed) for parts that were never displayed.</summary>
    private static List<Tri> TessellateFromModel(ModelDoc2 doc)
    {
        var tris = new List<Tri>();
        foreach (Body2 body in SolidBodies(doc))
        {
            if (Try(() => body.GetTessellation(null)) is not Tessellation tess)
            {
                continue;
            }

            tess.NeedFaceFacetMap = true;
            tess.NeedVertexNormal = true;
            tess.ImprovedQuality = true;
            if (!tess.Tessellate())
            {
                continue;
            }

            foreach (Face2 face in (Try(() => body.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>())
            {
                if (Try(() => tess.GetFaceFacets(face)) is not int[] facets)
                {
                    continue;
                }

                foreach (int facet in facets)
                {
                    if (Try(() => tess.GetFacetFins(facet)) is not int[] fins || fins.Length < 3)
                    {
                        continue;
                    }

                    var corners = fins.Take(3)
                        .Select(fin => Try(() => tess.GetFinVertices(fin)) as int[])
                        .Select(v => v is { Length: > 0 } ? v[0] : -1)
                        .ToArray();
                    if (corners.Any(v => v < 0))
                    {
                        continue;
                    }

                    double[][] pts = corners.Select(v => (double[])tess.GetVertexPoint(v)).ToArray();
                    double[] ab = Sub(pts[1], pts[0]);
                    double[] ac = Sub(pts[2], pts[0]);
                    double[] cross = { ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0] };
                    double len = Math.Sqrt(Dot3(cross, cross));
                    if (len < 1e-18)
                    {
                        continue;
                    }

                    double[] normal = Normalize3(cross);
                    double[] vn = corners.Select(v => Try(() => tess.GetVertexNormal(v)) as double[] ?? new double[3])
                        .Aggregate(new double[3], (acc, n) => new[] { acc[0] + n[0], acc[1] + n[1], acc[2] + n[2] });
                    if (Dot3(vn, normal) < 0)
                    {
                        normal = normal.Select(x => -x).ToArray();
                    }

                    tris.Add(new Tri(pts[0], pts[1], pts[2], normal, len / 2, face));
                }
            }
        }

        return tris;
    }

    private static List<Tri> TessellateOnce(ModelDoc2 doc)
    {
        var tris = new List<Tri>();
        foreach (Body2 body in SolidBodies(doc))
        {
            foreach (Face2 face in (Try(() => body.GetFaces()) as object[] ?? Array.Empty<object>()).OfType<Face2>())
            {
                if (Try(() => face.GetTessTriangles(true)) is not float[] v || Try(() => face.GetTessNorms()) is not float[] n)
                {
                    continue;
                }

                for (int i = 0; i + 8 < v.Length; i += 9)
                {
                    double[] a = { v[i], v[i + 1], v[i + 2] };
                    double[] b = { v[i + 3], v[i + 4], v[i + 5] };
                    double[] c = { v[i + 6], v[i + 7], v[i + 8] };
                    double[] ab = Sub(b, a);
                    double[] ac = Sub(c, a);
                    double[] cross = { ab[1] * ac[2] - ab[2] * ac[1], ab[2] * ac[0] - ab[0] * ac[2], ab[0] * ac[1] - ab[1] * ac[0] };
                    double len = Math.Sqrt(Dot3(cross, cross));
                    if (len < 1e-18)
                    {
                        continue;
                    }

                    double[] normal = Normalize3(cross);
                    // Align the winding normal with the outward tessellation normal.
                    if (i + 8 < n.Length)
                    {
                        double[] tn = { n[i] + n[i + 3] + n[i + 6], n[i + 1] + n[i + 4] + n[i + 7], n[i + 2] + n[i + 5] + n[i + 8] };
                        if (Dot3(tn, normal) < 0)
                        {
                            normal = normal.Select(x => -x).ToArray();
                        }
                    }

                    tris.Add(new Tri(a, b, c, normal, len / 2, face));
                }
            }
        }

        return tris;
    }

    /// <summary>
    /// Local wall thickness: rays cast inward from surface samples to the opposite wall.
    /// Finds thin walls that are weak in FEA terms and unreliable to print.
    /// </summary>
    private static object ThicknessCheck(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "thickness_check");
        double s = UnitScale(args);
        double minWall = DoubleArg(args, "min_wall", 1.2) * s;
        int maxSamples = (int)DoubleArg(args, "samples", 3000);
        List<Tri> tris = TessellatePart(doc);
        if (tris.Count == 0)
        {
            throw WorkerException.Worker("NO_TESSELLATION", "Could not tessellate the part.", new Dictionary<string, object?>());
        }

        // Area-weighted sample: every triangle centroid when small, else stride + all large triangles.
        List<Tri> samples = tris.Count <= maxSamples
            ? tris
            : tris.Where((t, i) => i % (int)Math.Ceiling(tris.Count / (double)maxSamples) == 0).ToList();
        const double eps = 1e-6;
        var points = new double[samples.Count * 3];
        var vectors = new double[samples.Count * 3];
        var origins = new List<double[]>();
        for (int i = 0; i < samples.Count; i++)
        {
            Tri t = samples[i];
            double[] c = { (t.A[0] + t.B[0] + t.C[0]) / 3, (t.A[1] + t.B[1] + t.C[1]) / 3, (t.A[2] + t.B[2] + t.C[2]) / 3 };
            // Facet centroids sit off curved faces (chord sag); snap onto the exact surface first.
            if (t.Face is not null && Try(() => t.Face.GetClosestPointOn(c[0], c[1], c[2])) is double[] q && q.Length >= 3)
            {
                c = q[..3];
            }

            origins.Add(c);
            for (int k = 0; k < 3; k++)
            {
                points[i * 3 + k] = c[k] - t.N[k] * eps;
                vectors[i * 3 + k] = -t.N[k];
            }
        }

        // Bodies must be marshalled as IDispatch wrappers; raw RCW arrays fault the server.
        List<Body2> bodyList = SolidBodies(doc).ToList();
        string?[] bodyNames = bodyList.Select(b => Try(() => b.Name) as string).ToArray();
        string?[] ownBody = samples.Select(t => t.Face is null ? null : Try(() => ((Body2)t.Face.GetBody()).Name) as string).ToArray();
        DispatchWrapper[] bodies = bodyList.Select(b => new DispatchWrapper(b)).ToArray();
        int hits = doc.Extension.RayIntersections(bodies, points, vectors,
            (int)(swRayPtsOpts_e.swRayPtsOptsNORMALS | swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT), 1e-7, 1e-7, true);
        double[] raw = doc.GetRayIntersectionsPoints() as double[] ?? Array.Empty<double>();
        var thickness = new double[samples.Count];
        Array.Fill(thickness, double.MaxValue);
        for (int h = 0; h + 8 < raw.Length; h += 9)
        {
            int ray = (int)raw[h + 1];
            int body = (int)raw[h];
            if (ray < 0 || ray >= samples.Count)
            {
                continue;
            }

            // Wall thickness is measured within the sample's own body; touching bodies (split parts,
            // multi-body layouts) must not read as zero-thickness walls.
            if (ownBody[ray] is not null && body >= 0 && body < bodyNames.Length && bodyNames[body] != ownBody[ray])
            {
                continue;
            }

            double[] p = { raw[h + 3], raw[h + 4], raw[h + 5] };
            double d = Dist3(p, origins[ray]);
            if (d > 2 * eps && d < thickness[ray])
            {
                thickness[ray] = d;
            }
        }

        var measured = Enumerable.Range(0, samples.Count).Where(i => thickness[i] < double.MaxValue).ToList();
        if (measured.Count == 0)
        {
            throw WorkerException.Worker("RAYCAST_FAILED", $"Ray casting returned no usable hits ({hits} intersections).", new Dictionary<string, object?>());
        }

        var sorted = measured.Select(i => thickness[i]).OrderBy(x => x).ToList();
        double Pct(double q) => sorted[Math.Clamp((int)(q * (sorted.Count - 1)), 0, sorted.Count - 1)] / s;
        // Thinnest spots, de-duplicated so one thin wall does not fill the list.
        var thin = new List<object>();
        var taken = new List<double[]>();
        foreach (int i in measured.OrderBy(i => thickness[i]))
        {
            if (thickness[i] >= minWall || thin.Count >= 12)
            {
                break;
            }

            if (taken.Any(q => Dist3(q, origins[i]) < Math.Max(minWall * 3, 0.002)))
            {
                continue;
            }

            taken.Add(origins[i]);
            thin.Add(new { thickness = Math.Round(thickness[i] / s, 3), at = Round(origins[i], s, 2), surfaceNormal = samples[i].N.Select(x => Math.Round(x, 3)).ToArray() });
        }

        double areaBelow = measured.Where(i => thickness[i] < minWall).Sum(i => samples[i].Area);
        double areaTotal = measured.Sum(i => samples[i].Area);
        return new
        {
            document = DescribeDocument(doc),
            units = UnitsLabel(args),
            minWall = minWall / s,
            samples = measured.Count,
            thinnest = Math.Round(sorted[0] / s, 3),
            percentile5 = Math.Round(Pct(0.05), 3),
            median = Math.Round(Pct(0.5), 3),
            surfaceShareBelowMinWall = Math.Round(areaBelow / Math.Max(areaTotal, 1e-18), 4),
            thinSpots = thin,
            notes = new[]
            {
                "Thickness is measured along the inward surface normal to the opposite wall (sampled from the display tessellation).",
                "Fillet/edge samples can read thin near sharp corners; judge by thinSpots locations.",
            },
        };
    }

    private static object PrintCheck(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "print_check");
        double s = UnitScale(args);
        double overhangDeg = DoubleArg(args, "overhang_deg", 45);
        double threshold = -Math.Sin(Deg(overhangDeg));
        double[] bed = Prop(args, "bed") is JsonElement bedEl ? Scaled(Vec(bedEl, 3), s) : new[] { 0.256, 0.256, 0.256 };
        string material = StringArg(args, "material") ?? "PLA";
        double density = FilamentDensity.TryGetValue(material, out double d) ? d : DoubleArg(args, "density_g_cm3", 1.24);

        List<Tri> tris = TessellatePart(doc);
        if (tris.Count == 0)
        {
            throw WorkerException.Worker("NO_TESSELLATION", "Could not tessellate the part (no solid bodies, or SolidWorks is not displaying it).", new Dictionary<string, object?>());
        }

        var candidates = new List<(string Name, double[] Up)>
        {
            ("+Z up", new[] { 0.0, 0, 1 }), ("-Z up", new[] { 0.0, 0, -1 }),
            ("+Y up", new[] { 0.0, 1, 0 }), ("-Y up", new[] { 0.0, -1, 0 }),
            ("+X up", new[] { 1.0, 0, 0 }), ("-X up", new[] { -1.0, 0, 0 }),
        };
        if (Prop(args, "up") is JsonElement upEl)
        {
            candidates.Insert(0, ("requested", Normalize3(Vec(upEl, 3))));
        }

        double contactTol = 0.0001;
        var results = candidates.Select(c =>
        {
            double[] up = c.Up;
            double zmin = tris.Min(t => Math.Min(Dot3(t.A, up), Math.Min(Dot3(t.B, up), Dot3(t.C, up))));
            double zmax = tris.Max(t => Math.Max(Dot3(t.A, up), Math.Max(Dot3(t.B, up), Dot3(t.C, up))));
            double contact = 0, overhang = 0, flatDown = 0;
            double[]? worst = null;
            foreach (Tri t in tris)
            {
                double nd = Dot3(t.N, up);
                if (nd >= threshold)
                {
                    continue;
                }

                double zc = (Dot3(t.A, up) + Dot3(t.B, up) + Dot3(t.C, up)) / 3;
                if (nd < -0.999 && zc - zmin < contactTol)
                {
                    contact += t.Area;
                }
                else
                {
                    overhang += t.Area;
                    if (nd < -0.999)
                    {
                        flatDown += t.Area;
                    }

                    worst ??= new[] { (t.A[0] + t.B[0] + t.C[0]) / 3, (t.A[1] + t.B[1] + t.C[1]) / 3, (t.A[2] + t.B[2] + t.C[2]) / 3 };
                }
            }

            // Footprint extents along two axes perpendicular to up.
            double[] e1 = Math.Abs(up[0]) > 0.9 ? new[] { 0.0, 1, 0 } : new[] { 1.0, 0, 0 };
            double[] e2 = { up[1] * e1[2] - up[2] * e1[1], up[2] * e1[0] - up[0] * e1[2], up[0] * e1[1] - up[1] * e1[0] };
            double Span(double[] axis) => tris.Max(t => Math.Max(Dot3(t.A, axis), Math.Max(Dot3(t.B, axis), Dot3(t.C, axis))))
                - tris.Min(t => Math.Min(Dot3(t.A, axis), Math.Min(Dot3(t.B, axis), Dot3(t.C, axis))));
            double w = Span(e1), l = Span(e2), h = zmax - zmin;
            bool fits = Math.Max(w, l) <= Math.Max(bed[0], bed[1]) && Math.Min(w, l) <= Math.Min(bed[0], bed[1]) && h <= bed[2];
            return new
            {
                orientation = c.Name,
                up = up.Select(x => Math.Round(x, 4)).ToArray(),
                overhangArea = Math.Round(overhang / (s * s), 2),
                flatDownFacingArea = Math.Round(flatDown / (s * s), 2),
                bedContactArea = Math.Round(contact / (s * s), 2),
                height = Math.Round(h / s, 3),
                footprint = new[] { Math.Round(w / s, 3), Math.Round(l / s, 3) },
                fitsBed = fits,
                exampleOverhangAt = worst is null ? null : Round(worst, s, 2),
            };
        }).ToList();

        var ranked = results
            .Where(r => r.orientation != "requested")
            .OrderByDescending(r => r.fitsBed)
            .ThenBy(r => r.overhangArea)
            .ThenByDescending(r => r.bedContactArea)
            .ThenBy(r => r.height)
            .ToList();

        double volume = SolidBodies(doc).Sum(b => Try(() => b.GetMassProperties(1.0)) is double[] mp && mp.Length >= 4 ? mp[3] : 0);
        double grams = volume * 1e6 * density;
        return new
        {
            document = DescribeDocument(doc),
            units = UnitsLabel(args),
            overhangLimitDeg = overhangDeg,
            material,
            densityGcm3 = density,
            solidMassGrams = Math.Round(grams, 1),
            filamentMeters175 = Math.Round(volume * 1e9 / (Math.PI * 0.875 * 0.875) / 1000, 2),
            best = ranked.First(),
            requested = results.FirstOrDefault(r => r.orientation == "requested"),
            orientations = ranked,
            triangles = tris.Count,
            notes = new[]
            {
                "overhangArea counts down-facing surface steeper than overhang_deg from vertical, excluding bed contact; flatDownFacingArea is the part of it that is horizontal (bridges or support).",
                "Mass is for a 100% solid part; slicer infill lowers it.",
            },
        };
    }
}
