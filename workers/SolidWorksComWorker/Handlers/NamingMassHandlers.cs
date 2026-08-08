using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object RenameFeature(JsonElement? args)
    {
        string fromName = RequiredStringArg(args, "from_name");
        string toName = RequiredStringArg(args, "to_name");
        bool save = BoolArg(args, "save", defaultValue: false);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        Feature? feature = FindFeatureByName(doc, fromName)
            ?? throw WorkerException.Validation(
                "FEATURE_NOT_FOUND",
                $"Feature not found: {fromName}",
                new Dictionary<string, object?> { ["from_name"] = fromName });

        try
        {
            feature.Name = toName;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not rename feature '{fromName}' to '{toName}'.",
                ex);
        }

        doc.EditRebuild3();
        string actualName = Try(() => feature.Name) as string ?? string.Empty;
        if (!actualName.Equals(toName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SolidWorks did not apply feature rename '{fromName}' to '{toName}'.");
        }

        if (save)
        {
            SaveDocumentOrThrow(doc, "renaming feature");
        }

        return new
        {
            from = fromName,
            to = actualName,
            applied = true,
            document = DescribeDocument(doc),
        };
    }

    private static object SetMassOverride(JsonElement? args)
    {
        string path = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        double massKg = DoubleArg(args, "mass_kg");
        bool save = BoolArg(args, "save", defaultValue: false);

        if (!double.IsFinite(massKg) || massKg <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(massKg),
                "mass_kg must be a finite positive number.");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("set_mass_override requires a part document.");
        }

        MassProperty massProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty
            ?? throw new InvalidOperationException("CreateMassProperty returned null.");

        massProperty.UseSystemUnits = true;
        massProperty.UserAssigned = true;
        massProperty.OverrideMass = true;
        bool overrideApplied = Try(() => massProperty.SetOverrideMassValue(
            massKg,
            (int)swInConfigurationOpts_e.swThisConfiguration,
            null)) as bool? ?? false;
        if (!overrideApplied)
        {
            throw new InvalidOperationException(
                "SolidWorks rejected the assigned mass override for this configuration.");
        }

        doc.EditRebuild3();

        MassProperty verifyProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty
            ?? throw new InvalidOperationException("CreateMassProperty returned null during readback.");
        verifyProperty.UseSystemUnits = true;
        double mass = Try(() => verifyProperty.Mass) as double? ?? double.NaN;
        double tolerance = Math.Max(1e-9, Math.Abs(massKg) * 1e-6);
        if (!double.IsFinite(mass) || Math.Abs(mass - massKg) > tolerance)
        {
            throw new InvalidOperationException(
                $"Mass override readback did not match the requested value. requested={massKg}, readback={mass}");
        }

        if (save)
        {
            SaveDocumentOrThrow(doc, "setting mass override");
        }

        return new
        {
            path,
            mass_kg = massKg,
            applied = true,
            mass,
        };
    }

    private static void SaveDocumentOrThrow(ModelDoc2 doc, string operation)
    {
        int errors = 0;
        int warnings = 0;
        bool saved = doc.Save3(
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ref errors,
            ref warnings);
        if (!saved || errors != 0)
        {
            throw new InvalidOperationException(
                $"Save failed after {operation}. errors={errors}, warnings={warnings}");
        }
    }
}
