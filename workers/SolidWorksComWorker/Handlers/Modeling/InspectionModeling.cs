using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Read-only verification for the modeling loop: what's in the part, what failed,
// and a picture of it.
internal static partial class Program
{
    private static readonly HashSet<string> HousekeepingFeatureTypes = new(StringComparer.Ordinal)
    {
        "CommentsFolder", "FavoriteFolder", "HistoryFolder", "SelectionSetFolder", "SensorFolder",
        "LiveSectionFolder", "DocsFolder", "DetailCabinet", "EnvFolder", "InkMarkupFolder",
        "EqnFolder", "MaterialFolder", "SolidBodyFolder", "SurfaceBodyFolder", "BlockFolder",
        "MateReferenceGroupFolder", "CutListFolder", "SubAtomFolder",
    };

    private static object PartReport(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = RequirePartDocument(app, args, "part_report");
        double scale = UnitScale(args);
        int maxItems = (int)DoubleArg(args, "max_items", 200);

        var features = new List<object>();
        object? cursor = Try(() => doc.FirstFeature());
        int guard = 0;
        while (cursor is Feature feature && guard++ < 5000)
        {
            string type = Try(() => feature.GetTypeName2()) as string ?? "?";
            if (!HousekeepingFeatureTypes.Contains(type))
            {
                bool warning = false;
                int code = (Try(() => feature.GetErrorCode2(out warning)) as int?) ?? 0;
                ISketch? sketch = type is "ProfileFeature" or "3DProfileFeature" ? Try(() => feature.GetSpecificFeature2()) as ISketch : null;
                features.Add(new
                {
                    name = Try(() => feature.Name),
                    type,
                    suppressed = (Try(() => feature.IsSuppressed()) as bool?) == true ? true : (bool?)null,
                    error = code == 0 ? null : DescribeFeatureError(code),
                    warning = code != 0 && warning ? true : (bool?)null,
                    sketchRegions = sketch is null ? (int?)null : Try(() => sketch.GetSketchRegionCount()) as int?,
                    usedBy = sketch is null ? null : (Try(() => feature.GetChildren()) as object[])?.OfType<Feature>().Select(c => Try(() => c.Name) as string).ToArray(),
                });
            }

            cursor = Try(() => feature.GetNextFeature());
        }

        var bodies = SolidBodies(doc).Select(body =>
        {
            double[]? mp = Try(() => body.GetMassProperties(1.0)) as double[];
            double[]? box = Try(() => body.GetBodyBox()) as double[];
            return new
            {
                name = Try(() => body.Name),
                volume = mp is { Length: >= 4 } ? Math.Round(mp[3] / (scale * scale * scale), 3) : (double?)null,
                faces = (Try(() => body.GetFaceCount()) as int?) ?? 0,
                edges = (Try(() => body.GetEdgeCount()) as int?) ?? 0,
                boundingBox = box is { Length: >= 6 } ? new { min = Round(box[..3], scale), max = Round(box[3..6], scale) } : null,
            };
        }).ToList();

        MassProperty? massProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty;
        string? material = Try(() => ((PartDoc)doc).GetMaterialPropertyName2("", out _)) as string;

        object? faces = null;
        if (BoolArg(args, "include_faces") || Prop(args, "face_filter") is not null)
        {
            JsonElement filter = Prop(args, "face_filter") ?? JsonDocument.Parse("{}").RootElement;
            faces = FilterFaces(doc, filter, scale).Take(maxItems).Select(f => FaceEntity(f, scale, null).Info).ToList();
        }

        object? edges = null;
        if (BoolArg(args, "include_edges") || Prop(args, "edge_filter") is not null)
        {
            JsonElement filter = Prop(args, "edge_filter") ?? JsonDocument.Parse("{}").RootElement;
            edges = FilterEdges(doc, filter, scale).Take(maxItems).Select(e => EdgeEntity(e, scale, null).Info).ToList();
        }

        return new
        {
            document = DescribeDocument(doc),
            units = UnitsLabel(args),
            features,
            bodies,
            part = PartGeometrySummary(doc, scale),
            material = string.IsNullOrWhiteSpace(material) ? null : material,
            massKg = material is null ? null : Try(() => Math.Round(massProperty!.Mass, 5)),
            whatsWrong = WhatsWrong(doc),
            faces,
            edges,
        };
    }

    private static object RenderView(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string outputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_path"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        string view = (StringArg(args, "view") ?? "isometric").ToLowerInvariant();
        EnsureNoActiveSketch(doc);

        (string Name, swStandardViews_e Id)? named = view switch
        {
            "isometric" => ("*Isometric", swStandardViews_e.swIsometricView),
            "trimetric" => ("*Trimetric", swStandardViews_e.swTrimetricView),
            "dimetric" => ("*Dimetric", swStandardViews_e.swDimetricView),
            "front" => ("*Front", swStandardViews_e.swFrontView),
            "back" => ("*Back", swStandardViews_e.swBackView),
            "top" => ("*Top", swStandardViews_e.swTopView),
            "bottom" => ("*Bottom", swStandardViews_e.swBottomView),
            "left" => ("*Left", swStandardViews_e.swLeftView),
            "right" => ("*Right", swStandardViews_e.swRightView),
            _ => null,
        };

        int oldDisplayMode = (Try(() => doc.ActiveView is ModelView mv ? mv.DisplayMode : -1) as int?) ?? -1;
        int errors = 0;
        int warnings = 0;
        bool ok;

        // Optional: show only some bodies (e.g. the bracket, not the imported actuator inside the part).
        var hidden = new List<Body2>();
        if (Prop(args, "bodies") is not null)
        {
            var keep = BodiesArg(doc, args, "bodies", UnitScale(args)).Select(b => Try(() => ((Body2)b.Com!).Name) as string).ToHashSet();
            foreach (Body2 body in SolidBodies(doc))
            {
                if (!keep.Contains(Try(() => body.Name) as string) && (Try(() => body.Visible) as bool?) == true)
                {
                    TryVoid(() => body.HideBody(true));
                    hidden.Add(body);
                }
            }
        }

        // Optional section (cut-away) view through a plane or planar face, removed again afterwards.
        bool sectioned = false;
        bool? sectionRemoved = null;
        if (Prop(args, "section") is JsonElement section)
        {
            double sc = UnitScale(args);
            object plane = ResolveSingle(doc, Prop(section, "plane") ?? throw new ArgumentException("section.plane is required"), sc, "section.plane").Com
                ?? throw new ArgumentException("section.plane did not resolve to a plane or planar face");
            ModelViewManager mvm = doc.ModelViewManager;
            SectionViewData data = mvm.CreateSectionViewData();
            data.FirstPlane = plane;
            data.FirstOffset = OptNum(section, "offset") is double off ? off * sc : 0;
            data.FirstReverseDirection = Flag(section, "reverse", false);
            data.ShowSectionCap = true;
            sectioned = mvm.CreateSectionView(data);
            if (!sectioned)
            {
                throw WorkerException.Worker("SECTION_FAILED", "SOLIDWORKS could not create the section view.", new Dictionary<string, object?>(),
                    ["Use a reference plane name or a planar face selector for section.plane."]);
            }
        }

        try
        {
            ok = WithReferenceGeometryHidden(app, doc, () =>
            {
                TryVoid(() => doc.ClearSelection2(true));
                if (named is { } n)
                {
                    TryVoid(() => doc.ShowNamedView2(n.Name, (int)n.Id));
                }

                if (doc.ActiveView is ModelView modelView)
                {
                    TryVoid(() => modelView.DisplayMode = BoolArg(args, "edges", true)
                        ? (int)swViewDisplayMode_e.swViewDisplayMode_ShadedWithEdges
                        : (int)swViewDisplayMode_e.swViewDisplayMode_Shaded);
                }

                TryVoid(() => doc.ViewZoomtofit2());
                TryVoid(() => doc.GraphicsRedraw2());
                return doc.Extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            });
        }
        finally
        {
            if (sectioned)
            {
                sectionRemoved = Try(() => doc.ModelViewManager.RemoveSectionView()) as bool?;
                if (sectionRemoved != true)
                {
                    // Toggle the Section View command off (swCommands_SectionView).
                    sectionRemoved = Try(() => doc.Extension.RunCommand(124, "")) as bool?;
                }
                TryVoid(() => doc.GraphicsRedraw2());
            }

            foreach (Body2 body in hidden)
            {
                TryVoid(() => body.HideBody(false));
            }

            if (oldDisplayMode >= 0 && doc.ActiveView is ModelView restoreView)
            {
                TryVoid(() => restoreView.DisplayMode = oldDisplayMode);
                TryVoid(() => doc.GraphicsRedraw2());
            }
        }

        if (!ok || !File.Exists(outputPath))
        {
            throw WorkerException.Worker(
                "RENDER_FAILED",
                $"SolidWorks could not save the view image (errors={errors}, warnings={warnings}).",
                new Dictionary<string, object?> { ["outputPath"] = outputPath },
                ["Use a .png or .jpg path.", "SolidWorks must be visible (not minimized) to render images."]);
        }

        return new
        {
            document = DescribeDocument(doc),
            outputPath,
            view,
            bytes = new FileInfo(outputPath).Length,
            section = sectioned ? new { removed = sectionRemoved } : null,
        };
    }
}
