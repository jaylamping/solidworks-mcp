using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object ActuatorListModels(JsonElement? args)
    {
        _ = args;
        return new
        {
            models = ActuatorCatalog.All().Select(model => new
            {
                id = model.Id,
                partNumber = model.PartNumber,
                vendorPath = ActuatorCatalog.ResolveVendorPath(model.Id, null),
                envelopeMm = new
                {
                    width = model.EnvelopeWidthMm,
                    depth = model.EnvelopeDepthMm,
                    height = model.EnvelopeHeightMm,
                },
                mount = new
                {
                    boltCircleDiameterMm = model.BoltCircleDiameterMm,
                    boltCount = model.BoltCount,
                    boltHoleDiameterMm = model.BoltHoleDiameterMm,
                    defaultClearanceMm = model.DefaultClearanceMm,
                },
                notes = model.Notes,
            }),
        };
    }

    private static object ActuatorGetEnvelope(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        string vendorPath = ActuatorCatalog.ResolveVendorPath(modelId, StringArg(args, "vendor_path"));

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, vendorPath);
        ActuatorCatalog.ModelSpec spec = ActuatorCatalog.Resolve(modelId);

        double[]? box = doc.GetType() == (int)swDocumentTypes_e.swDocPART
            ? Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[]
            : null;

        object? measured = null;
        if (box is not null && box.Length >= 6)
        {
            measured = new
            {
                minM = new[] { box[0], box[1], box[2] },
                maxM = new[] { box[3], box[4], box[5] },
                sizeMm = new[]
                {
                    (box[3] - box[0]) * 1000.0,
                    (box[4] - box[1]) * 1000.0,
                    (box[5] - box[2]) * 1000.0,
                },
            };
        }

        return new
        {
            model = spec.Id,
            partNumber = spec.PartNumber,
            vendorPath,
            catalogEnvelopeMm = new
            {
                width = spec.EnvelopeWidthMm,
                depth = spec.EnvelopeDepthMm,
                height = spec.EnvelopeHeightMm,
            },
            measured,
            mount = new
            {
                boltCircleDiameterMm = spec.BoltCircleDiameterMm,
                boltCount = spec.BoltCount,
                boltHoleDiameterMm = spec.BoltHoleDiameterMm,
            },
        };
    }

    private static object ActuatorProbeMountFace(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        string vendorPath = ActuatorCatalog.ResolveVendorPath(modelId, StringArg(args, "vendor_path"));
        string planeName = StringArg(args, "plane_name") ?? "Top Plane";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, vendorPath);
        ActuatorCatalog.ModelSpec spec = ActuatorCatalog.Resolve(modelId);

        bool planeExists = FindFeatureByName(doc, planeName) is not null
            || doc.Extension.SelectByID2(planeName, "PLANE", 0, 0, 0, false, 0, null, 0);

        double[]? box = Try(() => ((IPartDoc)doc).GetPartBox(true)) as double[];
        return new
        {
            model = spec.Id,
            vendorPath,
            suggestedMountPlane = planeName,
            planeSelectable = planeExists,
            mountPattern = new
            {
                boltCircleDiameterMm = spec.BoltCircleDiameterMm,
                boltCount = spec.BoltCount,
                boltHoleDiameterMm = spec.BoltHoleDiameterMm,
            },
            partBoxM = box is null ? null : new
            {
                min = new[] { box[0], box[1], box[2] },
                max = new[] { box[3], box[4], box[5] },
            },
        };
    }

    private static object ActuatorMountHolePattern(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        ActuatorCatalog.ModelSpec spec = ActuatorCatalog.Resolve(modelId);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("actuator_mount_hole_pattern requires a part document.");
        }

        string planeName = StringArg(args, "plane_name") ?? "mount_face";
        double boltCircleDiameterMm = DoubleArg(args, "bolt_circle_diameter_mm", spec.BoltCircleDiameterMm);
        int boltCount = IntArg(args, "bolt_count", spec.BoltCount);
        double holeDiameterMm = DoubleArg(args, "hole_diameter_mm", spec.BoltHoleDiameterMm);
        double startAngleDeg = DoubleArg(args, "start_angle_deg", 0);
        string featurePrefix = StringArg(args, "feature_prefix") ?? $"{spec.Id}_mount_holes";
        bool save = BoolArg(args, "save", defaultValue: true);

        if (boltCount < 1)
        {
            throw WorkerException.Validation(
                "INVALID_BOLT_COUNT",
                "bolt_count must be >= 1.",
                new Dictionary<string, object?> { ["bolt_count"] = boltCount });
        }

        if (!SelectPlane(doc, planeName))
        {
            Feature? mountPlane = InsertOffsetPlaneFromTop(doc, 0.0, planeName);
            if (mountPlane is null && !SelectPlane(doc, "Top Plane"))
            {
                throw new InvalidOperationException($"Could not select or create mount plane: {planeName}");
            }

            if (mountPlane is not null)
            {
                SelectPlane(doc, planeName);
            }
        }

        SketchManager sketchMgr = doc.SketchManager;
        sketchMgr.InsertSketch(true);

        double boltRadiusM = (boltCircleDiameterMm / 1000.0) / 2.0;
        double holeRadiusM = (holeDiameterMm / 1000.0) / 2.0;
        double startRad = startAngleDeg * Math.PI / 180.0;
        var centers = new List<double[]>();
        for (int i = 0; i < boltCount; i++)
        {
            double angle = startRad + ((2.0 * Math.PI * i) / boltCount);
            double x = boltRadiusM * Math.Cos(angle);
            double y = boltRadiusM * Math.Sin(angle);
            TryVoid(() => sketchMgr.CreateCircleByRadius(x, y, 0, holeRadiusM));
            centers.Add(new[] { x, y });
        }

        sketchMgr.InsertSketch(true);
        Feature? cut = ExtrudeCutThroughAll(doc);
        if (cut is not null)
        {
            TryVoid(() => cut.Name = featurePrefix);
        }

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            document = DescribeDocument(doc),
            model = spec.Id,
            planeName,
            boltCircleDiameterMm,
            boltCount,
            holeDiameterMm,
            centersM = centers,
            featureName = Try(() => cut?.Name),
            created = cut is not null,
            saved,
            errors,
            warnings,
        };
    }

    private static object ActuatorCutCavity(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        ActuatorCatalog.ModelSpec spec = ActuatorCatalog.Resolve(modelId);

        string? toolPath = StringArg(args, "tool_part_path");
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            toolPath = ActuatorCatalog.ResolveVendorPath(modelId, null);
        }

        double clearanceMm = DoubleArg(args, "clearance_mm", spec.DefaultClearanceMm);

        var merged = new Dictionary<string, object?>();
        if (args is not null && args.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in args.Value.EnumerateObject())
            {
                merged[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.ToString(),
                };
            }
        }

        merged["tool_part_path"] = toolPath;
        merged["clearance_mm"] = clearanceMm;
        merged["confirm"] = true;

        string json = JsonSerializer.Serialize(merged);
        using JsonDocument document = JsonDocument.Parse(json);
        return CutActuatorCavity(document.RootElement);
    }

    private static object ActuatorAddUrdfFrame(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        ActuatorCatalog.ModelSpec spec = ActuatorCatalog.Resolve(modelId);
        string vendorPath = ActuatorCatalog.ResolveVendorPath(modelId, StringArg(args, "path"));

        var merged = new Dictionary<string, object?> { ["path"] = vendorPath, ["confirm"] = true };
        if (args is not null && args.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in args.Value.EnumerateObject())
            {
                if (property.Name.Equals("path", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("model", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                merged[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.ToString(),
                };
            }
        }

        string json = JsonSerializer.Serialize(merged);
        using JsonDocument document = JsonDocument.Parse(json);
        object result = spec.Id.Equals("rs03", StringComparison.OrdinalIgnoreCase)
            ? VendorAddRs03UrdfFrame(document.RootElement)
            : VendorAddRs03UrdfFrame(document.RootElement);

        return new
        {
            model = spec.Id,
            vendorPath,
            result,
            note = spec.Id.Equals("rs03", StringComparison.OrdinalIgnoreCase)
                ? "RS03 uses measured vendor bbox for urdf_link_frame placement."
                : "RS02/RS04 reuse RS03 urdf frame builder until model-specific origins are calibrated.",
        };
    }

    private static object ActuatorInsertVendor(JsonElement? args)
    {
        string modelId = StringArg(args, "model") ?? "rs03";
        string assemblyPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string vendorPath = ActuatorCatalog.ResolveVendorPath(modelId, StringArg(args, "vendor_path"));
        string? componentPrefix = StringArg(args, "component_prefix") ?? $"actuator_{modelId}";

        var merged = new Dictionary<string, object?>
        {
            ["path"] = assemblyPath,
            ["part_path"] = vendorPath,
            ["name"] = componentPrefix,
            ["confirm"] = true,
        };

        if (args is not null && args.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in args.Value.EnumerateObject())
            {
                if (merged.ContainsKey(property.Name))
                {
                    continue;
                }

                merged[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.ToString(),
                };
            }
        }

        string json = JsonSerializer.Serialize(merged);
        using JsonDocument document = JsonDocument.Parse(json);
        object insertResult = InsertComponent(document.RootElement);
        return new
        {
            model = modelId,
            vendorPath,
            componentPrefix,
            insert = insertResult,
        };
    }
}
