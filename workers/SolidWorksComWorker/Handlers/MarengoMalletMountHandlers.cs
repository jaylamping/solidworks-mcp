using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object CreateMalletMount(JsonElement? args)
    {
        string partPath = RequiredStringArg(args, "part_path");
        partPath = Path.GetFullPath(partPath);
        double handleDiameterMm = DoubleArg(args, "handle_diameter_mm", 26.0);
        bool save = BoolArg(args, "save", defaultValue: true);
        string owner = StringArg(args, "owner") ?? "Joey Lamping";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        object buildResult = RebuildMalletMountPartFile(
            app,
            partPath,
            handleDiameterMm,
            owner,
            save);

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
            throw new InvalidOperationException("Failed to create blank part for mallet mount.");
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
                $"Mallet mount part save failed. errors={saveErrors} ({DecodeSaveErrors(saveErrors)}), warnings={saveWarnings}");
        }

        object propsResult = SetMalletMountCustomProperties(app, partPath, owner, save: false);

        if (save)
        {
            ModelDoc2 savedDoc = OpenDocument(app, partPath);
            int errors = 0;
            int warnings = 0;
            bool saved = savedDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
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
            ["description"] = "RS03 actuator mount for Harbor Freight 60504 rubber mallet handle",
        };

        var applied = new List<object>();
        foreach (KeyValuePair<string, string> entry in properties)
        {
            TryVoid(() =>
            {
                manager.Add3(
                    entry.Key,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    entry.Value ?? "",
                    1);
            });
            applied.Add(new { name = entry.Key, value = entry.Value });
        }

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new { applied, saved, errors, warnings };
    }

    private static void CreateMalletMountSolid(ModelDoc2 doc, double handleDiameterMm)
    {
        const double plateHalf = 0.0235;
        const double plateThickM = 0.005;
        const double clampHalfXM = 0.015;
        const double clampHalfYM = 0.018;
        const double clampHeightM = 0.030;
        const double dowelSlotHalfLenM = 0.01738;
        const double dowelSlotHalfWidthM = 0.0022;
        const double dowelSlotDepthM = 0.0005;
        const double screwHoleRadiusM = 0.0022;
        double handleRadiusM = (handleDiameterMm / 1000.0) / 2.0;

        SketchManager skMgr = doc.SketchManager;
        FeatureManager featMgr = doc.FeatureManager;

        // RS03 actuator mount plate — matches rs03_dowel_mount_v5 (47 x 47 x 5 mm).
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select Top Plane for plate.");
        }

        skMgr.InsertSketch(true);
        object? plateRect = Try(() => skMgr.CreateCornerRectangle(
            -plateHalf,
            -plateHalf,
            0.0,
            plateHalf,
            plateHalf,
            0.0));
        if (plateRect is null)
        {
            throw new InvalidOperationException("mallet mount: plate sketch rectangle failed.");
        }

        skMgr.InsertSketch(true);
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select plate sketch.");
        }

        Feature? plateBoss = ExtrudeActiveSketch(featMgr, plateThickM, flip: false);
        if (plateBoss is null)
        {
            throw new InvalidOperationException("mallet mount: plate extrusion failed.");
        }

        TryVoid(() => plateBoss.Name = "Boss-Plate1");

        // Split clamp body — 30 x 36 x 30 mm, base on top of 5 mm plate.
        DeleteFeatureByName(doc, "clamp_base_plane");
        Feature? clampBasePlane = InsertOffsetPlaneFromTop(doc, plateThickM, "clamp_base_plane");
        if (clampBasePlane is null)
        {
            throw new InvalidOperationException("mallet mount: failed to create clamp base plane.");
        }

        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("clamp_base_plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select clamp base plane.");
        }

        skMgr.InsertSketch(true);
        object? clampRect = Try(() => skMgr.CreateCornerRectangle(
            -clampHalfXM,
            -clampHalfYM,
            0.0,
            clampHalfXM,
            clampHalfYM,
            0.0));
        if (clampRect is null)
        {
            throw new InvalidOperationException("mallet mount: clamp sketch rectangle failed.");
        }

        skMgr.InsertSketch(true);
        doc.ClearSelection2(true);
        string clampSketchName = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string ?? "Sketch2";
        if (!doc.Extension.SelectByID2(clampSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select clamp sketch.");
        }

        Feature? clampBoss = ExtrudeActiveSketch(featMgr, clampHeightM, flip: false);
        if (clampBoss is null)
        {
            throw new InvalidOperationException("mallet mount: clamp extrusion failed.");
        }

        TryVoid(() => clampBoss.Name = "Boss-Clamp1");
        MergeAllSolidBodies(doc);
        doc.EditRebuild3();

        // Handle bore along Y — reference dowel mount uses 19 mm; mallet handle ~26 mm.
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Front Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select Front Plane for handle bore.");
        }

        skMgr.InsertSketch(true);
        double boreZ0 = plateThickM;
        double boreZ1 = plateThickM + clampHeightM;
        object? handleSlot = Try(() => skMgr.CreateCornerRectangle(
            -handleRadiusM,
            boreZ0,
            0.0,
            handleRadiusM,
            boreZ1,
            0.0));
        if (handleSlot is null)
        {
            throw new InvalidOperationException("mallet mount: handle slot sketch failed.");
        }

        skMgr.InsertSketch(true);
        doc.ClearSelection2(true);
        string boreSketchName = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string ?? "Sketch3";
        if (!doc.Extension.SelectByID2(boreSketchName, "SKETCH", 0, 0, 0, false, 4, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select handle bore sketch profile.");
        }

        Feature? boreCut = CutExtrudeThroughAll(featMgr);
        if (boreCut is null)
        {
            throw new InvalidOperationException("mallet mount: handle bore cut failed.");
        }

        TryVoid(() => boreCut.Name = "Cut-Handle-Bore1");

        // RS03 dowel key slot on mount face — 34.76 x 4.4 x 0.5 mm.
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select Top Plane for dowel slot.");
        }

        skMgr.InsertSketch(true);
        object? slotRect = Try(() => skMgr.CreateCornerRectangle(
            -dowelSlotHalfLenM,
            -dowelSlotHalfWidthM,
            0.0,
            dowelSlotHalfLenM,
            dowelSlotHalfWidthM,
            0.0));
        if (slotRect is null)
        {
            throw new InvalidOperationException("mallet mount: dowel slot sketch failed.");
        }

        skMgr.InsertSketch(true);
        doc.ClearSelection2(true);
        string slotSketchName = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string ?? "Sketch4";
        if (!doc.Extension.SelectByID2(slotSketchName, "SKETCH", 0, 0, 0, false, 4, null, 0))
        {
            throw new InvalidOperationException("mallet mount: could not select dowel slot sketch profile.");
        }

        Feature? slotCut = CutExtrudeActiveSketch(featMgr, dowelSlotDepthM, flip: true);
        if (slotCut is null)
        {
            throw new InvalidOperationException("mallet mount: dowel slot cut failed.");
        }

        TryVoid(() => slotCut.Name = "Cut-Dowel-Slot1");

        // M4 clearance holes through clamp ears (split clamp screws).
        (double X, double Y)[] screwCenters =
        [
            (0.011, 0.016),
            (-0.011, 0.016),
            (0.011, -0.016),
            (-0.011, -0.016),
        ];

        int screwIndex = 0;
        foreach ((double x, double y) in screwCenters)
        {
            screwIndex++;
            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2("Top Plane", "PLANE", 0, 0, 0, false, 0, null, 0))
            {
                throw new InvalidOperationException("mallet mount: could not select Top Plane for screw hole.");
            }

            skMgr.InsertSketch(true);
            object? screwCircle = Try(() => skMgr.CreateCircle(x, y, 0.0, x + screwHoleRadiusM, y, 0.0));
            if (screwCircle is null)
            {
                throw new InvalidOperationException($"mallet mount: screw hole sketch {screwIndex} failed.");
            }

            skMgr.InsertSketch(true);
            doc.ClearSelection2(true);
            string screwSketchName = Try(() => ((Feature)doc.FeatureByPositionReverse(0)).Name) as string ?? $"Sketch{4 + screwIndex}";
            if (!doc.Extension.SelectByID2(screwSketchName, "SKETCH", 0, 0, 0, false, 4, null, 0))
            {
                throw new InvalidOperationException($"mallet mount: could not select screw sketch profile {screwIndex}.");
            }

            Feature? screwCut = CutExtrudeThroughAll(featMgr);
            if (screwCut is null)
            {
                throw new InvalidOperationException($"mallet mount: screw hole cut {screwIndex} failed.");
            }

            TryVoid(() => screwCut.Name = $"Cut-Screw-{screwIndex}");
        }

        doc.EditRebuild3();
    }

    private static void MergeAllSolidBodies(ModelDoc2 doc)
    {
        object? bodiesObj = Try(() => ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false));
        if (bodiesObj is not object[] bodies || bodies.Length <= 1)
        {
            return;
        }

        FeatureManager featMgr = doc.FeatureManager;
        dynamic dynFeatMgr = featMgr;
        Feature? combine = Try(() => dynFeatMgr.InsertCombineFeature(0, bodies.Length, bodies)) as Feature;
        if (combine is null)
        {
            throw new InvalidOperationException("mallet mount: failed to merge plate and clamp bodies.");
        }

        TryVoid(() => combine.Name = "Combine-Bodies1");
    }

    private static Feature? CutExtrudeActiveSketch(FeatureManager featMgr, double depthM, bool flip)
    {
        return Try(() => featMgr.FeatureCut3(
            true,
            flip,
            false,
            (int)swEndConditions_e.swEndCondBlind,
            0,
            depthM,
            0.0,
            false,
            false,
            false,
            false,
            0.0,
            0.0,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            false,
            false,
            false,
            0,
            0.0,
            false)) as Feature;
    }

    private static Feature? CutExtrudeThroughAll(FeatureManager featMgr)
    {
        return Try(() => featMgr.FeatureCut3(
            true,
            false,
            false,
            (int)swEndConditions_e.swEndCondThroughAll,
            0,
            0.0,
            0.0,
            false,
            false,
            false,
            false,
            0.0,
            0.0,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            false,
            false,
            false,
            0,
            0.0,
            false)) as Feature;
    }
}
