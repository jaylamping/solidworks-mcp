using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private const string InertiaConvention =
        "About each body's (or group's) centre of mass, part axes, row-major 3x3. Products of inertia follow SOLIDWORKS (Lxy = +integral of x*y dm); a URDF inertia uses ixy = -Lxy.";

    // Per-body mass properties of a part, or of an assembly component's part. Hidden bodies (for
    // example embedded reference copies of purchased parts) are listed and flagged, because
    // SOLIDWORKS mass properties still count them.
    private static object BodyMassProperties(JsonElement? args)
    {
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = ResolveDocument(app, args);
        string? componentName = StringArg(args, "component_name");
        string[]? onlyBodies = StringArrayArg(args, "bodies");
        bool visibleOnly = BoolArg(args, "visible_only", defaultValue: false);

        ModelDoc2 partDoc = doc;
        double[]? transform = null;
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                throw WorkerException.Validation(
                    "NOT_AN_ASSEMBLY",
                    "component_name needs an assembly document.",
                    new Dictionary<string, object?> { ["component_name"] = componentName });
            }

            Component2 component = FindComponent((IAssemblyDoc)doc, null, componentName)
                ?? throw WorkerException.Validation("COMPONENT_NOT_FOUND", $"Component not found: {componentName}", new Dictionary<string, object?>());
            partDoc = Try(() => component.GetModelDoc2()) as ModelDoc2
                ?? throw WorkerException.Worker("COMPONENT_NOT_LOADED", $"Component {componentName} is not loaded (lightweight or suppressed?).", new Dictionary<string, object?>(),
                    ["Run solidworks_resolve_lightweight first."]);
            transform = (Try(() => component.Transform2) as MathTransform)?.ArrayData as double[];
        }

        if (partDoc is not PartDoc part)
        {
            throw WorkerException.Validation(
                "NOT_A_PART",
                "body_mass_properties needs a part, or an assembly component that is a part.",
                new Dictionary<string, object?>());
        }

        MassProperty? partMass = Try(() => partDoc.Extension.CreateMassProperty()) as MassProperty;
        double density = Try(() => partMass?.Density) as double? ?? 1000.0;
        string? material = Try(() => part.GetMaterialPropertyName2("", out string _)) as string;

        var all = new BodyMassTotal();
        var visible = new BodyMassTotal();
        var hidden = new BodyMassTotal();
        var rows = new List<object>();
        object[] bodies = Try(() => part.GetBodies2((int)swBodyType_e.swSolidBody, false)) as object[] ?? [];
        foreach (Body2 body in bodies.OfType<Body2>())
        {
            string name = Try(() => body.Name) as string ?? "";
            bool isVisible = Try(() => body.Visible) as bool? ?? true;
            if ((visibleOnly && !isVisible)
                || (onlyBodies is not null && !onlyBodies.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase)))
                || Try(() => body.GetMassProperties(density)) is not double[] mp
                || mp.Length < 12)
            {
                continue;
            }

            // GetMassProperties: [cx, cy, cz, volume, area, mass, Ixx, Iyy, Izz, Ixy, Izx, Iyz].
            double[] com = [mp[0], mp[1], mp[2]];
            double[] inertia = [mp[6], mp[9], mp[10], mp[9], mp[7], mp[11], mp[10], mp[11], mp[8]];
            all.Add(mp[5], mp[3], mp[4], com, inertia);
            (isVisible ? visible : hidden).Add(mp[5], mp[3], mp[4], com, inertia);
            rows.Add(new
            {
                name,
                visible = isVisible,
                volume = mp[3],
                surfaceArea = mp[4],
                mass = mp[5],
                centerOfMass = com,
                centerOfMassAssembly = ToAssemblyPoint(transform, com),
                momentsOfInertia = inertia,
            });
        }

        return new
        {
            document = DescribeDocument(doc),
            componentName,
            partPath = Try(() => partDoc.GetPathName()),
            units = "SI: m, m^2, m^3, kg, kg*m^2",
            density,
            material = string.IsNullOrEmpty(material) ? null : material,
            bodyCount = rows.Count,
            bodies = rows,
            totals = new
            {
                all = all.Describe(transform),
                visible = visible.Describe(transform),
                hidden = hidden.Describe(transform),
            },
            momentsOfInertiaConvention = InertiaConvention,
            note = hidden.Count == 0 || all.Volume <= 0
                ? null
                : $"{hidden.Count} hidden solid bodies hold {100 * hidden.Volume / all.Volume:F1}% of the volume. SOLIDWORKS mass properties include them; totals.visible leaves them out.",
            densityNote = Math.Abs(density - 1000) < 1
                ? "Density is the SOLIDWORKS default (1000 kg/m^3): no material is set, so these masses are volume placeholders."
                : null,
        };
    }

    // Hidden solid bodies still count in SOLIDWORKS mass properties. Report them so a caller can tell
    // a part's own mass from embedded reference copies of other parts.
    private static object? HiddenBodySummary(ModelDoc2? doc)
    {
        if (doc is not PartDoc part
            || Try(() => part.GetBodies2((int)swBodyType_e.swSolidBody, false)) is not object[] bodies)
        {
            return null;
        }

        double totalVolume = 0;
        double hiddenVolume = 0;
        var names = new List<string>();
        foreach (Body2 body in bodies.OfType<Body2>())
        {
            double volume = Try(() => body.GetMassProperties(1.0)) is double[] mp && mp.Length > 3 ? mp[3] : 0;
            totalVolume += volume;
            if (Try(() => body.Visible) as bool? == false)
            {
                hiddenVolume += volume;
                names.Add(Try(() => body.Name) as string ?? "");
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        return new
        {
            count = names.Count,
            bodies = names.Take(10).ToArray(),
            volumeFraction = totalVolume > 0 ? hiddenVolume / totalVolume : 0,
            note = "These hidden solid bodies are included in the mass above. solidworks_body_mass_properties gives totals without them (totals.visible).",
        };
    }

    private static double[]? ToAssemblyPoint(double[]? transform, double[] point)
    {
        if (transform is null || transform.Length < 12)
        {
            return null;
        }

        // ArrayData rows 0-2 are the component axes in assembly coordinates; 9-11 is its origin.
        return
        [
            point[0] * transform[0] + point[1] * transform[3] + point[2] * transform[6] + transform[9],
            point[0] * transform[1] + point[1] * transform[4] + point[2] * transform[7] + transform[10],
            point[0] * transform[2] + point[1] * transform[5] + point[2] * transform[8] + transform[11],
        ];
    }

    private sealed class BodyMassTotal
    {
        private readonly List<(double Mass, double[] Com, double[] Inertia)> items = [];

        public int Count => items.Count;

        public double Volume { get; private set; }

        public double Area { get; private set; }

        public double Mass { get; private set; }

        public void Add(double mass, double volume, double area, double[] com, double[] inertia)
        {
            items.Add((mass, com, inertia));
            Volume += volume;
            Area += area;
            Mass += mass;
        }

        public object Describe(double[]? transform)
        {
            if (items.Count == 0 || Mass <= 0)
            {
                return new { count = items.Count };
            }

            double[] com = [0, 0, 0];
            foreach (var (mass, c, _) in items)
            {
                for (int k = 0; k < 3; k++)
                {
                    com[k] += mass * c[k] / Mass;
                }
            }

            // Parallel-axis shift to the group centre of mass, in the SOLIDWORKS product convention.
            var inertia = new double[9];
            foreach (var (mass, c, body) in items)
            {
                double dx = c[0] - com[0], dy = c[1] - com[1], dz = c[2] - com[2];
                double[] shift = [dy * dy + dz * dz, dx * dy, dx * dz, dx * dy, dx * dx + dz * dz, dy * dz, dx * dz, dy * dz, dx * dx + dy * dy];
                for (int k = 0; k < 9; k++)
                {
                    inertia[k] += body[k] + mass * shift[k];
                }
            }

            return new
            {
                count = items.Count,
                volume = Volume,
                surfaceArea = Area,
                mass = Mass,
                centerOfMass = com,
                centerOfMassAssembly = ToAssemblyPoint(transform, com),
                momentsOfInertia = inertia,
            };
        }
    }
}
