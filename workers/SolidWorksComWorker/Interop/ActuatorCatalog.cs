internal static class ActuatorCatalog
{
    internal sealed record ModelSpec(
        string Id,
        string PartNumber,
        string RelativeVendorPath,
        double EnvelopeWidthMm,
        double EnvelopeDepthMm,
        double EnvelopeHeightMm,
        double BoltCircleDiameterMm,
        int BoltCount,
        double BoltHoleDiameterMm,
        double DefaultClearanceMm,
        string Notes);

    private static readonly ModelSpec[] Models =
    [
        new(
            "rs00",
            "RS00",
            "cad/vendor/vendor_robstride_rs00_vendor.SLDPRT",
            57,
            57,
            51.4,
            40,
            4,
            3.0,
            0.5,
            "Measured envelope 57×57×51.4 mm from Desktop vendor SLDPRT (2026-06). Bolt pattern layout default."),
        new(
            "rs02",
            "RS02",
            "cad/vendor/vendor_robstride_rs02_vendor.SLDPRT",
            78.5,
            78.5,
            45.5,
            50,
            4,
            3.4,
            0.5,
            "Measured envelope 78.5×78.5×45.5 mm from Desktop vendor SLDPRT (2026-06). Bolt pattern still layout default."),
        new(
            "rs03",
            "RS03",
            "cad/vendor/vendor_robstride_rs03_vendor.SLDPRT",
            99.5,
            98.5,
            56.6,
            58,
            4,
            3.4,
            0.5,
            "Measured envelope 99.5×98.5×56.6 mm from Desktop vendor SLDPRT (2026-06)."),
        new(
            "rs04",
            "RS04",
            "cad/vendor/vendor_robstride_rs04_vendor.SLDPRT",
            120,
            120,
            55.7,
            68,
            6,
            4.0,
            0.5,
            "Measured envelope 120×120×55.7 mm from Desktop vendor SLDPRT (2026-06). Bolt pattern still layout default."),
        new(
            "rs05",
            "RS05",
            "cad/vendor/vendor_robstride_rs05_vendor.SLDPRT",
            65,
            65,
            47,
            75,
            6,
            4.5,
            0.5,
            "Mounting envelope 65×65×47 mm from RS05.STEP assembly (143 components). Desktop SLDPRT is body-only 46×46×47 mm, same 23 g mass — use STEP/asm for bracket clearance, SLDPRT for solid cavity tool."),
    ];

    internal static IReadOnlyList<ModelSpec> All() => Models;

    internal static ModelSpec Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Models.First(m => m.Id.Equals("rs03", StringComparison.OrdinalIgnoreCase));
        }

        string normalized = modelId.Trim().ToLowerInvariant();
        foreach (ModelSpec model in Models)
        {
            if (model.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || model.PartNumber.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        throw WorkerException.Validation(
            "UNKNOWN_ACTUATOR_MODEL",
            $"Unknown actuator model '{modelId}'. Expected rs00, rs02, rs03, rs04, or rs05.",
            new Dictionary<string, object?> { ["model"] = modelId });
    }

    internal static string ResolveVendorPath(string? modelId, string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return PathGuard.AssertAllowedPath(overridePath);
        }

        ModelSpec model = Resolve(modelId);
        string marengoRoot = Environment.GetEnvironmentVariable("MARENGO_ROOT") ?? "C:/code/marengo";
        string combined = Path.Combine(marengoRoot.Replace('\\', '/'), model.RelativeVendorPath.Replace('\\', '/'));
        return PathGuard.AssertAllowedPath(combined);
    }
}
