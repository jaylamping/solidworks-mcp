using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object DemoBuildPart(JsonElement? args)
    {
        double sizeMm = DoubleArg(args, "size_mm", 40);
        double holeDiameterMm = DoubleArg(args, "hole_diameter_mm", 18);
        if (!double.IsFinite(sizeMm) || sizeMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeMm), "size_mm must be a finite positive number.");
        }

        if (!double.IsFinite(holeDiameterMm) || holeDiameterMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(holeDiameterMm),
                "hole_diameter_mm must be a finite positive number.");
        }

        if (holeDiameterMm >= sizeMm)
        {
            throw new ArgumentOutOfRangeException(
                nameof(holeDiameterMm),
                "hole_diameter_mm must be smaller than size_mm so the cube keeps walls.");
        }

        string? outputPath = StringArg(args, "output_path");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = PathGuard.AssertAllowedPath(outputPath);
        }

        double sizeM = sizeMm / 1000.0;
        double holeRadiusM = (holeDiameterMm / 1000.0) / 2.0;
        var steps = new List<string>();
        var cutFeatureNames = new List<string>();

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        app.Visible = true;
        steps.Add("solidworks_visible");

        ModelDoc2 doc = DemoCreateBlankPart(app);
        steps.Add("new_part_document");

        Feature boss = DemoExtrudeCenteredCube(doc, sizeM, steps);
        string bossName = Try(() => boss.Name) as string
            ?? throw new InvalidOperationException("The cube boss feature has no name.");

        string[] holePlanes = ["Front Plane", "Top Plane", "Right Plane"];
        foreach (string planeName in holePlanes)
        {
            Feature cut = DemoThroughHoleOnPlane(doc, planeName, holeRadiusM, sizeM, steps);
            string? cutName = Try(() => cut.Name) as string;
            if (!string.IsNullOrWhiteSpace(cutName))
            {
                cutFeatureNames.Add(cutName);
            }
        }

        bool rebuildForced = Try(() => doc.ForceRebuild3(false)) as bool? ?? false;
        doc.EditRebuild3();
        steps.Add("rebuild");
        doc.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
        doc.ViewZoomtofit2();
        doc.GraphicsRedraw2();
        steps.Add("zoom_to_fit");

        bool saved = false;
        int saveErrors = 0;
        int saveWarnings = 0;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            saved = doc.Extension.SaveAs(
                outputPath,
                0,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);
            steps.Add("save_as");
        }

        return new
        {
            document = DescribeDocument(doc),
            shape = "cube_with_through_cylinders",
            dimensions = new
            {
                sizeMm,
                holeDiameterMm,
                sizeM,
                holeRadiusM,
            },
            bossFeatureName = bossName,
            cutFeatureNames,
            steps,
            rebuildForced,
            outputPath,
            saved,
            saveErrors = outputPath is null ? (int?)null : saveErrors,
            saveWarnings = outputPath is null ? (int?)null : saveWarnings,
        };
    }

    private static ModelDoc2 DemoCreateBlankPart(ISldWorks app)
    {
        string template = Try(() => app.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string ?? string.Empty;

        ModelDoc2? doc = null;
        if (!string.IsNullOrWhiteSpace(template) && File.Exists(template))
        {
            doc = Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2;
        }

        doc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
        return doc ?? throw new InvalidOperationException(
            "Failed to create a new part document. Check the SolidWorks default part template setting.");
    }

    private static Feature DemoExtrudeCenteredCube(ModelDoc2 doc, double sizeM, List<string> steps)
    {
        if (!SelectPlane(doc, "Front Plane"))
        {
            throw new InvalidOperationException("Could not select Front Plane.");
        }

        SketchManager sketchManager = doc.SketchManager;
        sketchManager.InsertSketch(true);
        double half = sizeM / 2.0;
        sketchManager.CreateCornerRectangle(-half, -half, 0, half, half, 0);
        doc.GraphicsRedraw2();
        steps.Add("sketch_cube_face");

        Feature? extrude = Try(() => doc.FeatureManager.FeatureExtrusion2(
            true,
            false,
            false,
            (int)swEndConditions_e.swEndCondMidPlane,
            0,
            sizeM,
            0,
            false,
            false,
            false,
            false,
            0,
            0,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            0,
            0,
            false)) as Feature;
        if (extrude is null)
        {
            throw new InvalidOperationException("Failed to extrude the demo cube.");
        }

        doc.EditRebuild3();
        doc.GraphicsRedraw2();
        steps.Add("extrude_cube_midplane");
        return extrude;
    }

    private static Feature DemoThroughHoleOnPlane(
        ModelDoc2 doc,
        string planeName,
        double holeRadiusM,
        double cubeSizeM,
        List<string> steps)
    {
        if (!SelectPlane(doc, planeName))
        {
            throw new InvalidOperationException($"Could not select plane: {planeName}");
        }

        SketchManager sketchManager = doc.SketchManager;
        sketchManager.InsertSketch(true);
        object? circle = Try(() => sketchManager.CreateCircleByRadius(0, 0, 0, holeRadiusM));
        if (circle is null)
        {
            throw new InvalidOperationException($"Failed to sketch the hole circle on {planeName}.");
        }

        doc.GraphicsRedraw2();
        steps.Add($"sketch_hole_{SanitizeStepToken(planeName)}");

        // Principal planes sit at the cube midplanes. One-direction ThroughAll only
        // reaches one face; midplane depth past the cube pierces both opposite faces.
        double cutDepthM = cubeSizeM * 2.0;
        Feature? cut = Try(() => doc.FeatureManager.FeatureCut4(
            true,
            false,
            false,
            (int)swEndConditions_e.swEndCondMidPlane,
            0,
            cutDepthM,
            0.01,
            false,
            false,
            false,
            false,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            0,
            0.0,
            false,
            false)) as Feature;
        doc.EditRebuild3();
        if (cut is null)
        {
            throw new InvalidOperationException($"Failed to cut through-all on {planeName}.");
        }

        doc.GraphicsRedraw2();
        steps.Add($"cut_through_{SanitizeStepToken(planeName)}");
        return cut;
    }

    private static string SanitizeStepToken(string planeName) =>
        planeName.Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant();
}
