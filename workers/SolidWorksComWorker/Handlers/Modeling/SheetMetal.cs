using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Sheet metal: base flange from a sketch (an open profile becomes a bent channel/bracket with a bend
// at every corner; a closed profile becomes a flat tab), and flat-pattern export (DXF) with the flat size.
internal static partial class Program
{
    private static object SheetMetalBaseFlange(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "sheet_metal_base_flange");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        string sketch = RequiredStringArg(args, "sketch");
        double thickness = DoubleArg(args, "thickness") * s;
        double radius = DoubleArg(args, "bend_radius", DoubleArg(args, "thickness")) * s;
        double depth = Prop(args, "depth") is not null ? DoubleArg(args, "depth") * s : 0;
        bool midPlane = BoolArg(args, "mid_plane");
        doc.ClearSelection2(true);
        SelectSketchByName(doc, sketch, false, 0);

        double kFactor = Prop(args, "k_factor") is not null ? DoubleArg(args, "k_factor") : 0.5;
        int endCondition = midPlane ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind;
        string? via = null;
        object? created = null;

        // 1) Feature-data API.
        if (Try(() => doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmBaseFlange)) is BaseFlangeFeatureData data)
        {
            TryVoid(() => data.Thickness = thickness);
            TryVoid(() => data.BendRadius = radius);
            TryVoid(() => data.ReverseThickness = BoolArg(args, "reverse_thickness"));
            TryVoid(() => data.ReverseDirection = BoolArg(args, "reverse"));
            TryVoid(() => data.D1EndConditionType = endCondition);
            TryVoid(() => data.D1EndConditionDistance = depth);
            TryVoid(() => data.OverrideKFactor = true);
            TryVoid(() => data.KFactor = kFactor);
            doc.ClearSelection2(true);
            SelectSketchByName(doc, sketch, false, 0);
            created = doc.FeatureManager.CreateFeature(data);
            via = created is not null ? "feature_data" : null;
        }

        // 2) Legacy call with an explicit K-factor bend allowance.
        if (created is null)
        {
            CustomBendAllowance cba = doc.FeatureManager.CreateCustomBendAllowance();
            cba.Type = (int)swBendAllowanceTypes_e.swBendAllowanceKFactor;
            cba.KFactor = kFactor;
            doc.ClearSelection2(true);
            SelectSketchByName(doc, sketch, false, 0);
            created = doc.FeatureManager.InsertSheetMetalBaseFlange2(
                thickness, BoolArg(args, "reverse_thickness"), radius,
                depth, midPlane ? depth : 0, BoolArg(args, "reverse"),
                endCondition, (int)swEndConditions_e.swEndCondBlind,
                0, cba, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, 0, 0.5, true,
                true, false, true);
            via = created is not null ? "legacy" : null;
        }

        Feature feature = CheckCreated(doc, created, "sheet-metal base flange", new Dictionary<string, object?>
        {
            ["sketch"] = sketch,
            ["thickness"] = thickness / s,
            ["bendRadius"] = radius / s,
            ["depth"] = depth / s,
        }, "Open profiles (a polyline) need depth; closed profiles make a flat tab.", "One sheet-metal base flange per body.")!;

        object outcome = FeatureOutcome(doc, feature, args, s, StringArg(args, "name"));
        return new { outcome, bends = CountBends(doc), via };
    }

    private static int CountBends(ModelDoc2 doc) =>
        FeatureList(doc).Sum(f => SubFeatureList(f).Count(sf => (Try(() => sf.GetTypeName2()) as string) is "OneBend" or "SketchBend"));

    private static IEnumerable<Feature> FeatureList(ModelDoc2 doc)
    {
        for (Feature? f = doc.FirstFeature() as Feature; f is not null; f = f.GetNextFeature() as Feature)
        {
            yield return f;
        }
    }

    private static IEnumerable<Feature> SubFeatureList(Feature f)
    {
        for (Feature? sf = f.GetFirstSubFeature() as Feature; sf is not null; sf = sf.GetNextSubFeature() as Feature)
        {
            yield return sf;
        }
    }

    private static object FlatPattern(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "flat_pattern");
        double s = UnitScale(args);
        EnsureNoActiveSketch(doc);
        Feature flat = FeatureList(doc).FirstOrDefault(f => f.GetTypeName2() == "FlatPattern")
            ?? throw WorkerException.Validation("NOT_SHEET_METAL", "The part has no flat pattern (create it with solidworks_sheet_metal_base_flange).", new Dictionary<string, object?>());

        // Unfold, measure the flat body, fold again.
        object? flatSize = null;
        doc.ClearSelection2(true);
        bool unfolded = flat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        try
        {
            doc.EditRebuild3();
            Body2? body = SolidBodies(doc).FirstOrDefault();
            if (body?.GetBodyBox() is double[] box)
            {
                double[] size = [(box[3] - box[0]) / s, (box[4] - box[1]) / s, (box[5] - box[2]) / s];
                double[] sorted = size.OrderByDescending(v => v).ToArray();
                flatSize = new { length = Math.Round(sorted[0], 3), width = Math.Round(sorted[1], 3), thickness = Math.Round(sorted[2], 3), box = Round(box, s, 3) };
            }
        }
        finally
        {
            if (unfolded)
            {
                flat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                doc.EditRebuild3();
            }
        }

        string? dxf = null;
        bool exported = false;
        if (StringArg(args, "output_path") is string outPath)
        {
            dxf = PathGuard.AssertAllowedPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dxf) ?? ".");
            int options = 1 /* geometry */ | (BoolArg(args, "bend_lines", true) ? 4 : 0);
            exported = ((PartDoc)doc).ExportToDWG2(dxf, doc.GetPathName(), (int)swExportToDWG_e.swExportToDWG_ExportSheetMetal, true, null, false, false, options, null)
                && File.Exists(dxf);
        }

        return new
        {
            document = DescribeDocument(doc),
            units = UnitsLabel(args),
            flatSize,
            bends = CountBends(doc),
            dxf,
            exported,
        };
    }
}
