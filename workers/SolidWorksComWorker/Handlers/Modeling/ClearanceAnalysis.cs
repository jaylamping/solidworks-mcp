using System.Diagnostics;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Clearance between bodies or faces of a part: minimum distance with witness points, and for
// overlapping bodies the interference volume (boolean intersection of temporary copies; the
// model is not changed). Typical use: a bracket against an inserted actuator or a mating part.
internal static partial class Program
{
    private static List<ResolvedEntity> ClearanceSide(ModelDoc2 doc, JsonElement? args, string key, double s)
    {
        JsonElement side = Prop(args, key) ?? throw new ArgumentException($"{key} is required");
        if (Prop(side, "bodies") is not null)
        {
            return BodiesArg(doc, side, "bodies", s);
        }

        if (Prop(side, "faces") is JsonElement faces)
        {
            return ResolveSelectors(doc, faces, s, $"{key}.faces");
        }

        throw WorkerException.Validation("BAD_CLEARANCE_SIDE", $"{key} needs bodies or faces.", new Dictionary<string, object?>());
    }

    private static object MeasureClearance(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "measure_clearance");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        var timer = Stopwatch.StartNew();
        List<ResolvedEntity> a = ClearanceSide(doc, args, "a", s);
        List<ResolvedEntity> b = ClearanceSide(doc, args, "b", s);

        double best = double.MaxValue;
        double[]? pa = null;
        double[]? pb = null;
        foreach (ResolvedEntity ea in a)
        {
            foreach (ResolvedEntity eb in b)
            {
                if (ReferenceEquals(ea.Com, eb.Com))
                {
                    continue;
                }

                object p1 = null!;
                object p2 = null!;
                double d = doc.ClosestDistance(ea.Com, eb.Com, out p1, out p2);
                if (d >= 0 && d < best)
                {
                    best = d;
                    pa = p1 as double[];
                    pb = p2 as double[];
                }
            }
        }

        if (best == double.MaxValue)
        {
            throw WorkerException.Worker("CLEARANCE_FAILED", "SOLIDWORKS could not compute a distance between the given entities.", new Dictionary<string, object?>());
        }

        // Overlap: intersect temporary copies of each body pair.
        double interference = 0;
        var overlaps = new List<object>();
        foreach (Body2 ba in a.Select(e => e.Com).OfType<Body2>())
        {
            foreach (Body2 bb in b.Select(e => e.Com).OfType<Body2>())
            {
                if (ReferenceEquals(ba, bb))
                {
                    continue;
                }

                Body2 ca = (Body2)ba.Copy();
                Body2 cb = (Body2)bb.Copy();
                object result = ca.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, cb, out int err);
                double volume = 0;
                double[]? center = null;
                if (result is object[] pieces)
                {
                    foreach (Body2 piece in pieces.OfType<Body2>())
                    {
                        if (piece.GetMassProperties(1.0) is double[] mp && mp.Length > 3)
                        {
                            volume += mp[3];
                            center ??= [mp[0], mp[1], mp[2]];
                        }
                    }
                }

                if (volume > 1e-15)
                {
                    interference += volume;
                    overlaps.Add(new { a = ba.Name, b = bb.Name, volume = Math.Round(volume / (s * s * s), 4), center = Round(center, s, 3) });
                }
            }
        }

        double distance = best / s;
        double? minimum = Prop(args, "min_clearance") is not null ? DoubleArg(args, "min_clearance") : null;
        bool intersecting = interference > 0;
        return new
        {
            document = DescribeDocument(doc),
            units = UnitsLabel(args),
            minDistance = Math.Round(intersecting ? 0 : distance, 4),
            pointOnA = Round(pa, s, 3),
            pointOnB = Round(pb, s, 3),
            intersecting,
            interferenceVolume = Math.Round(interference / (s * s * s), 4),
            overlaps,
            minClearance = minimum,
            pass = minimum is double m ? !intersecting && distance >= m - 1e-9 : (bool?)null,
            entities = new { a = a.Count, b = b.Count },
            seconds = Math.Round(timer.Elapsed.TotalSeconds, 2),
        };
    }
}
