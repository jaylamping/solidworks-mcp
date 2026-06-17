using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object CreateMalletMount(JsonElement? args)
    {
        string partPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "part_path"));
        double handleDiameterMm = DoubleArg(args, "handle_diameter_mm", 26.0);
        bool save = BoolArg(args, "save", defaultValue: true);
        string owner = StringArg(args, "owner") ?? "Joey Lamping";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        object buildResult = RebuildMalletMountPartFile(app, partPath, handleDiameterMm, owner, save);
        return buildResult;
    }

    private static object RebuildMalletMountPartFile(
        ISldWorks app,
        string partPath,
        double handleDiameterMm,
        string owner,
        bool save)
    {
        CloseDocumentIfOpen(app, partPath);

        string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
            ?? string.Empty;
        ModelDoc2? doc = !string.IsNullOrWhiteSpace(template) && File.Exists(template)
            ? Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2
            : null;
        doc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
        if (doc is null)
        {
            throw new InvalidOperationException("Failed to create blank part for mallet mount rebuild.");
        }

        CreateMalletMountSolid(doc, handleDiameterMm);

        DeleteFeatureByName(doc, "mount_face");
        Feature? mountFace = InsertOffsetPlaneFromTop(doc, 0.0, "mount_face");
        if (mountFace is null)
        {
            throw new InvalidOperationException("Failed to create mount_face on mallet mount part.");
        }

        doc.EditRebuild3();

        Directory.CreateDirectory(Path.GetDirectoryName(partPath) ?? ".");
        CloseDocumentIfOpen(app, partPath);

        int saveErrors = 0;
        int saveWarnings = 0;
        bool partSaved = doc.Extension.SaveAs(
            partPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref saveErrors,
            ref saveWarnings);
        if (!partSaved || saveErrors != 0)
        {
            throw new InvalidOperationException(
                $"Mallet mount part save failed. errors={saveErrors}, warnings={saveWarnings}");
        }

        object propsResult = SetMalletMountCustomProperties(app, partPath, owner, save: false);

        if (save)
        {
            ModelDoc2 savedDoc = OpenDocument(app, partPath);
            int errors = 0;
            int warnings = 0;
            bool savedOk = savedDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!savedOk || errors != 0)
            {
                throw new InvalidOperationException(
                    $"Mallet mount part final save failed. errors={errors}, warnings={warnings}");
            }
        }

        double[]? box = Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[];
        return new
        {
            partPath,
            handleDiameterMm,
            boundingBoxM = Normalize(box),
            propsResult,
            saved = partSaved,
            saveErrors,
            saveWarnings,
        };
    }

    private static object SetMalletMountCustomProperties(
        ISldWorks app,
        string path,
        string owner,
        bool save)
    {
        ModelDoc2 doc = OpenDocument(app, path);
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            throw new InvalidOperationException("CustomPropertyManager is unavailable on this document.");
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["process"] = "print",
            ["material"] = "PETG",
            ["revision"] = "A",
            ["owner"] = owner,
            ["part_role"] = "mallet_mount",
        };

        foreach ((string name, string value) in properties)
        {
            TryVoid(() => manager.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value, 1));
        }

        if (save)
        {
            int errors = 0;
            int warnings = 0;
            doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new { path, properties };
    }

    private static void CreateMalletMountSolid(ModelDoc2 doc, double handleDiameterMm)
    {
        const double plateHalf = 0.0235;
        const double plateThickM = 0.005;
        const double clampHalfXM = 0.015;
        const double clampHalfYM = 0.018;
        double handleRadiusM = (handleDiameterMm / 1000.0) / 2.0;

        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("Could not select Top Plane for mallet mount base.");
        }

        SketchManager sketchMgr = doc.SketchManager;
        sketchMgr.InsertSketch(true);
        sketchMgr.CreateCornerRectangle(-plateHalf, -plateHalf, 0, plateHalf, plateHalf, 0);
        sketchMgr.InsertSketch(true);

        Feature? baseExtrude = Try(() => doc.FeatureManager.FeatureExtrusion2(
            true, false, false, 0, 0, plateThickM, 0, false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false)) as Feature;
        if (baseExtrude is null)
        {
            throw new InvalidOperationException("Failed to extrude mallet mount base plate.");
        }

        DeleteFeatureByName(doc, "clamp_base_plane");
        Feature? clampBasePlane = InsertOffsetPlaneFromTop(doc, plateThickM, "clamp_base_plane");
        if (clampBasePlane is null)
        {
            throw new InvalidOperationException("Failed to create clamp_base_plane.");
        }

        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("clamp_base_plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("Could not select clamp_base_plane.");
        }

        sketchMgr.InsertSketch(true);
        sketchMgr.CreateCornerRectangle(-clampHalfXM, -clampHalfYM, 0, clampHalfXM, clampHalfYM, 0);
        sketchMgr.CreateCircleByRadius(0, 0, 0, handleRadiusM);
        sketchMgr.InsertSketch(true);

        Feature? clampExtrude = Try(() => doc.FeatureManager.FeatureExtrusion2(
            true, false, false, 0, 0, 0.012, 0, false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false)) as Feature;
        if (clampExtrude is null)
        {
            throw new InvalidOperationException("Failed to extrude mallet mount clamp body.");
        }

        doc.EditRebuild3();
    }
}
