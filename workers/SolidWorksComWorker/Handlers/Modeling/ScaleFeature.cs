using System.Text.Json;
using SolidWorks.Interop.sldworks;

// Scale feature: uniform or per-axis scaling of bodies about their centroid or the part origin
// (shrinkage compensation for printed/cast parts, quick resizing of a concept).
internal static partial class Program
{
    private static object ScaleBody(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "scale");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        doc.ClearSelection2(true);
        if (Prop(args, "bodies") is not null)
        {
            SelectResolved(doc, BodiesArg(doc, args, "bodies", s), 0, append: false, "bodies");
        }

        double[] factors = Prop(args, "factor") is JsonElement f && f.ValueKind == JsonValueKind.Array
            ? Vec(f, 3)
            : Enumerable.Repeat(Prop(args, "factor") is not null ? DoubleArg(args, "factor") : 1.0, 3).ToArray();
        if (factors.Any(v => v <= 0))
        {
            throw WorkerException.Validation("BAD_SCALE", "Scale factors must be positive.", new Dictionary<string, object?>());
        }

        bool uniform = factors.Distinct().Count() == 1;
        int about = (StringArg(args, "about") ?? "centroid").ToLowerInvariant() switch
        {
            "centroid" => 0,
            "origin" => 1,
            _ => throw WorkerException.Validation("BAD_SCALE_POINT", "about must be centroid or origin.", new Dictionary<string, object?>()),
        };
        object? created = doc.FeatureManager.InsertScale((short)about, uniform, factors[0], factors[1], factors[2]);
        Feature feature = CheckCreated(doc, created, "scale", new Dictionary<string, object?> { ["factor"] = factors, ["about"] = about })!;
        return FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
    }
}
