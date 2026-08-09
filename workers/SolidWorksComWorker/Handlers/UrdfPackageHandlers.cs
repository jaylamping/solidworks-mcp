using System.Globalization;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object ExportUrdfPackage(JsonElement? args)
    {
        UrdfManifest manifest = LoadUrdfManifest(args);
        string assemblyPath = PathGuard.AssertAllowedPath(
            StringArg(args, "path")
            ?? StringArg(args, "assembly_path")
            ?? manifest.AssemblyPath);
        string packageRoot = PathGuard.AssertAllowedPath(manifest.PackageRoot);
        string urdfOutputPath = PathGuard.AssertAllowedPath(manifest.UrdfOutputPath);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, assemblyPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("export_urdf_package requires an assembly.");
        }

        string staging = packageRoot + ".staging_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        PathGuard.AssertAllowedPath(staging);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(staging, "meshes", "visual"));
        Directory.CreateDirectory(Path.Combine(staging, "meshes", "collision"));

        var warnings = new List<string>();
        var linkPayloads = new List<object>();
        var linkPoses = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        foreach (UrdfManifestLink link in manifest.Links)
        {
            string frameRef = string.IsNullOrWhiteSpace(link.FrameRef) ? "urdf_link_frame" : link.FrameRef!;
            string frameBodyName = string.IsNullOrWhiteSpace(link.FrameBody) ? link.Bodies[0] : link.FrameBody!;
            Component2 frameBody = FindComponent((IAssemblyDoc)doc, null, frameBodyName)
                ?? throw new InvalidOperationException($"Frame body not found: {frameBodyName}");

            double[] frameWorld = ResolveLinkFrameWorld(frameBody, frameRef, warnings);
            linkPoses[link.LinkName] = frameWorld;

            var bodyMasses = new List<(double Mass, double[] ComWorld, double[] Icom, double[] ComponentWorld)>();
            foreach (string bodyName in link.Bodies)
            {
                Component2 component = FindComponent((IAssemblyDoc)doc, null, bodyName)
                    ?? throw new InvalidOperationException($"Link body not found: {bodyName}");
                bodyMasses.Add(ReadComponentMassInWorld(component));
            }

            AggregatedInertia aggregated = AggregateInertiaInFrame(bodyMasses, frameWorld, link.MassOverrideKg);
            string meshFile = SanitizeFileName(link.LinkName) + ".stl";
            string visualRel = "meshes/visual/" + meshFile;
            string collisionRel = "meshes/collision/" + meshFile;
            string visualAbs = Path.Combine(staging, visualRel.Replace('/', Path.DirectorySeparatorChar));
            string collisionAbs = Path.Combine(staging, collisionRel.Replace('/', Path.DirectorySeparatorChar));
            PathGuard.AssertAllowedPath(visualAbs);
            PathGuard.AssertAllowedPath(collisionAbs);

            ExportComponentStl(frameBody, visualAbs, warnings);
            File.Copy(visualAbs, collisionAbs, overwrite: true);

            linkPayloads.Add(new
            {
                linkName = link.LinkName,
                bodies = link.Bodies,
                frameRef,
                poseWorld = PoseFromMatrix(frameWorld),
                massKg = aggregated.Mass,
                comInLink = aggregated.ComInLink,
                inertiaAboutLink = aggregated.Inertia,
                visualMesh = visualRel.Replace('\\', '/'),
                collisionMesh = collisionRel.Replace('\\', '/'),
                meshOrigin = PoseFromMatrix(
                    ChildInParent(
                        frameWorld,
                        MatrixFromTransform(Try(() => frameBody.Transform2) as MathTransform) ?? IdentityMatrix())),
                warnings = aggregated.Warnings,
            });
        }

        var jointPayloads = new List<object>();
        foreach (UrdfManifestJoint joint in manifest.Joints)
        {
            if (!linkPoses.TryGetValue(joint.Parent, out double[]? parentMat)
                || !linkPoses.TryGetValue(joint.Child, out double[]? childMat))
            {
                throw new InvalidOperationException($"Joint {joint.JointName} references unknown links.");
            }

            double[] origin = ChildInParent(parentMat, childMat);
            double[] axisInParent = [0, 0, 0];
            if (joint.Type != "fixed")
            {
                string axisRef = string.IsNullOrWhiteSpace(joint.AxisRef) ? "joint_axis" : joint.AxisRef!;
                string axisBodyName = string.IsNullOrWhiteSpace(joint.AxisBody)
                    ? manifest.Links.First(l => l.LinkName.Equals(joint.Child, StringComparison.OrdinalIgnoreCase)).Bodies[0]
                    : joint.AxisBody!;
                Component2 axisBody = FindComponent((IAssemblyDoc)doc, null, axisBodyName)
                    ?? throw new InvalidOperationException($"Axis body not found: {axisBodyName}");
                double[] axisWorld = ResolveAxisWorld(axisBody, axisRef, warnings);
                axisInParent = TransformDirectionInverse(parentMat, axisWorld);
                NormalizeInPlace(axisInParent);
                if (joint.AxisSign < 0)
                {
                    axisInParent[0] *= -1;
                    axisInParent[1] *= -1;
                    axisInParent[2] *= -1;
                }
            }

            object? limit = null;
            var jointWarnings = new List<string>();
            if (manifest.LimitPrecedence == "external_calibrated")
            {
                if (joint.LimitLower is not null && joint.LimitUpper is not null)
                {
                    limit = LimitPayload(
                        joint.LimitLower.Value,
                        joint.LimitUpper.Value,
                        joint.Effort,
                        joint.Velocity,
                        joint.LimitSign,
                        "external_calibrated",
                        null);
                }
                else
                {
                    jointWarnings.Add("limit_precedence=external_calibrated; CAD mate limits not written into package.");
                }
            }
            else if (manifest.LimitPrecedence == "manifest_override")
            {
                if (joint.LimitLower is null || joint.LimitUpper is null)
                {
                    throw new InvalidOperationException(
                        $"Joint '{joint.JointName}' requires limitLower and limitUpper for manifest_override.");
                }
                limit = LimitPayload(
                    joint.LimitLower.Value,
                    joint.LimitUpper.Value,
                    joint.Effort,
                    joint.Velocity,
                    joint.LimitSign,
                    "manifest_override",
                    null);
            }
            else if (!string.IsNullOrWhiteSpace(joint.LimitMate))
            {
                limit = ReadLimitMatePayload(
                    doc,
                    joint.LimitMate!,
                    joint.Effort,
                    joint.Velocity,
                    joint.LimitSign);
            }
            else if (joint.Type == "revolute")
            {
                jointWarnings.Add("No limit_mate; revolute joint exported without limits.");
            }

            jointPayloads.Add(new
            {
                jointName = joint.JointName,
                type = joint.Type,
                parent = joint.Parent,
                child = joint.Child,
                origin = PoseFromMatrix(origin),
                axis = axisInParent,
                limit,
                warnings = jointWarnings,
            });
        }

        var package = new
        {
            schemaVersion = 1,
            units = new { length = "m", angle = "rad", mass = "kg" },
            handedness = "right",
            rotation = "rpy_intrinsic_xyz",
            robotName = manifest.RobotName,
            assemblyPath,
            exportedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            zeroConfiguration = manifest.ZeroConfiguration ?? "export_pose",
            limitPrecedence = manifest.LimitPrecedence,
            links = linkPayloads,
            joints = jointPayloads,
            warnings,
        };

        string packageJson = Path.Combine(staging, "package.json");
        PathGuard.AssertAllowedPath(packageJson);
        File.WriteAllText(
            packageJson,
            JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }));

        AtomicReplaceDirectory(staging, packageRoot);

        return new
        {
            document = DescribeDocument(doc),
            packageRoot,
            packageJson = Path.Combine(packageRoot, "package.json"),
            urdfOutputPath,
            linkCount = linkPayloads.Count,
            jointCount = jointPayloads.Count,
            warnings,
        };
    }

    private sealed record UrdfManifest(
        string RobotName,
        string AssemblyPath,
        string PackageRoot,
        string UrdfOutputPath,
        string? ZeroConfiguration,
        string LimitPrecedence,
        List<UrdfManifestLink> Links,
        List<UrdfManifestJoint> Joints);

    private sealed record UrdfManifestLink(
        string LinkName,
        List<string> Bodies,
        string? FrameRef,
        string? FrameBody,
        double? MassOverrideKg);

    private sealed record UrdfManifestJoint(
        string JointName,
        string Type,
        string Parent,
        string Child,
        string? AxisRef,
        string? AxisBody,
        string? LimitMate,
        double? Effort,
        double? Velocity,
        int AxisSign,
        double? LimitLower,
        double? LimitUpper,
        int LimitSign);

    private static UrdfManifest LoadUrdfManifest(JsonElement? args)
    {
        JsonElement root;
        if (args is not null
            && args.Value.ValueKind == JsonValueKind.Object
            && args.Value.TryGetProperty("manifest", out JsonElement inline)
            && inline.ValueKind == JsonValueKind.Object)
        {
            root = inline;
        }
        else
        {
            string manifestPath = PathGuard.AssertAllowedPath(
                RequiredStringArg(args, "manifest_path"));
            root = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement.Clone();
        }

        string limitPrecedence = ManifestString(root, "limitPrecedence", "limit_precedence") ?? "cad_mate";

        var links = new List<UrdfManifestLink>();
        foreach (JsonElement link in ManifestArray(root, "links"))
        {
            var bodies = link.GetProperty("bodies").EnumerateArray().Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            links.Add(new UrdfManifestLink(
                ManifestRequiredString(link, "linkName", "name"),
                bodies,
                ManifestString(link, "frameRef", "frame_ref"),
                ManifestString(link, "frameBody", "frame_body"),
                ManifestNumber(link, "massOverrideKg", "mass_override_kg")
                    is double massOverride
                    ? massOverride
                    : null));
        }

        var joints = new List<UrdfManifestJoint>();
        foreach (JsonElement joint in ManifestArray(root, "joints"))
        {
            joints.Add(new UrdfManifestJoint(
                ManifestRequiredString(joint, "jointName", "name"),
                ManifestString(joint, "type") ?? "revolute",
                ManifestRequiredString(joint, "parent"),
                ManifestRequiredString(joint, "child"),
                ManifestString(joint, "axisRef", "axis_ref"),
                ManifestString(joint, "axisBody", "axis_body", "axis_component"),
                ManifestString(joint, "limitMate", "limit_mate"),
                ManifestNumber(joint, "effort"),
                ManifestNumber(joint, "velocity"),
                ManifestNumber(joint, "axisSign", "axis_sign") is double axisSign ? (int)axisSign : 1,
                ManifestNumber(joint, "limitLower", "limit_lower")
                    ?? ManifestNestedNumber(joint, "limit_override", "lower_rad"),
                ManifestNumber(joint, "limitUpper", "limit_upper")
                    ?? ManifestNestedNumber(joint, "limit_override", "upper_rad"),
                ManifestNumber(joint, "limitSign", "limit_sign") is double limitSign ? (int)limitSign : 1));
        }

        return new UrdfManifest(
            ManifestString(root, "robotName", "robot_name") ?? "cad_package",
            ManifestRequiredString(root, "assemblyPath", "assembly_path"),
            ManifestRequiredString(root, "packageRoot", "package_root"),
            ManifestRequiredString(root, "urdfOutputPath", "urdf_output_path"),
            ManifestString(root, "zeroConfiguration", "zero_configuration") ?? "export_pose",
            limitPrecedence,
            links,
            joints);
    }

    private static JsonElement.ArrayEnumerator ManifestArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Manifest property '{name}' must be an array.");
        }
        return array.EnumerateArray();
    }

    private static string? ManifestString(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (value.TryGetProperty(name, out JsonElement property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }
        return null;
    }

    private static string ManifestRequiredString(JsonElement value, params string[] names) =>
        ManifestString(value, names)
        ?? throw new InvalidOperationException($"Manifest property '{names[0]}' is required.");

    private static double? ManifestNumber(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (value.TryGetProperty(name, out JsonElement property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetDouble(out double result))
            {
                return result;
            }
        }
        return null;
    }

    private static double? ManifestNestedNumber(JsonElement value, string objectName, string propertyName)
    {
        return value.TryGetProperty(objectName, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object
            ? ManifestNumber(nested, propertyName)
            : null;
    }

    private static double[] ResolveLinkFrameWorld(Component2 component, string frameRef, List<string> warnings)
    {
        double[] componentMat = MatrixFromTransform(Try(() => component.Transform2) as MathTransform)
            ?? IdentityMatrix();
        ModelDoc2? partDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (partDoc is null)
        {
            throw new InvalidOperationException(
                $"No model document for frame-owning component '{Try(() => component.Name2)}'.");
        }

        Feature? feature = FindFeatureByName(partDoc, frameRef);
        if (feature is null)
        {
            warnings.Add(
                $"Missing coordinate system '{frameRef}' on '{Try(() => component.Name2)}'; using component origin.");
            return componentMat;
        }

        double[]? local = TryGetFeatureTransform(feature);
        if (local is null)
        {
            warnings.Add(
                $"Could not read transform for '{frameRef}' on '{Try(() => component.Name2)}'; using component origin.");
            return componentMat;
        }

        return MultiplyMatrix(componentMat, local);
    }

    private static double[] ResolveAxisWorld(Component2 component, string axisRef, List<string> warnings)
    {
        double[] componentMat = MatrixFromTransform(Try(() => component.Transform2) as MathTransform)
            ?? IdentityMatrix();
        ModelDoc2? partDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (partDoc is null)
        {
            throw new InvalidOperationException(
                $"No model document for axis-owning component '{Try(() => component.Name2)}'.");
        }

        Feature? feature = FindFeatureByName(partDoc, axisRef);
        if (feature is null)
        {
            warnings.Add(
                $"Missing reference axis '{axisRef}' on '{Try(() => component.Name2)}'; defaulting to +Z.");
            return TransformDirection(componentMat, [0, 0, 1]);
        }

        double[]? localDir = TryGetAxisDirection(feature);
        if (localDir is null)
        {
            warnings.Add($"Could not read direction for '{axisRef}'; defaulting to +Z.");
            return TransformDirection(componentMat, [0, 0, 1]);
        }

        return TransformDirection(componentMat, localDir);
    }

    private static double[]? TryGetFeatureTransform(Feature feature)
    {
        object? specific = Try(() => feature.GetSpecificFeature2());
        if (specific is null)
        {
            return null;
        }

        dynamic dyn = specific;
        MathTransform? transform = Try(() => (MathTransform)dyn.Transform) as MathTransform;
        return MatrixFromTransform(transform);
    }

    private static double[]? TryGetAxisDirection(Feature feature)
    {
        object? specific = Try(() => feature.GetSpecificFeature2());
        if (specific is RefAxis axis)
        {
            double[]? data = Try(() => axis.GetRefAxisParams()) as double[];
            if (data is { Length: >= 6 })
            {
                return
                [
                    data[3] - data[0],
                    data[4] - data[1],
                    data[5] - data[2],
                ];
            }
        }

        return TryGetFeatureTransform(feature) is double[] mat
            ? [mat[0], mat[3], mat[6]]
            : null;
    }

    private static (double Mass, double[] ComWorld, double[] Icom, double[] ComponentWorld) ReadComponentMassInWorld(Component2 component)
    {
        ModelDoc2 compDoc = Try(() => component.GetModelDoc2()) as ModelDoc2
            ?? throw new InvalidOperationException($"Model unavailable for {Try(() => component.Name2)}");
        MassProperty massProperty = Try(() => compDoc.Extension.CreateMassProperty()) as MassProperty
            ?? throw new InvalidOperationException("CreateMassProperty failed.");
        massProperty.UseSystemUnits = true;
        double mass = Try(() => massProperty.Mass) as double? ?? 0;
        double[] comLocal = Try(() => massProperty.CenterOfMass) as double[] ?? [0, 0, 0];
        double[] moments = Try(() => massProperty.GetMomentOfInertia(0)) as double[]
            ?? [0, 0, 0, 0, 0, 0];
        // GetMomentOfInertia(0) → [Lxx, Lyy, Lzz, Lxy, Lzx, Lyz] about COM.
        double[] icom =
        [
            moments.ElementAtOrDefault(0),
            moments.ElementAtOrDefault(3),
            moments.ElementAtOrDefault(4),
            moments.ElementAtOrDefault(1),
            moments.ElementAtOrDefault(5),
            moments.ElementAtOrDefault(2),
        ];
        double[] componentMat = MatrixFromTransform(Try(() => component.Transform2) as MathTransform)
            ?? IdentityMatrix();
        double[] comWorld = TransformPoint(componentMat, comLocal);
        return (mass, comWorld, icom, componentMat);
    }

    private sealed record AggregatedInertia(
        double Mass,
        double[] ComInLink,
        object Inertia,
        List<string> Warnings);

    private static AggregatedInertia AggregateInertiaInFrame(
        List<(double Mass, double[] ComWorld, double[] Icom, double[] ComponentWorld)> bodies,
        double[] frameWorld,
        double? massOverride)
    {
        var warnings = new List<string>();
        double mass = bodies.Sum(b => b.Mass);
        if (mass <= 0)
        {
            throw new InvalidOperationException("Link aggregate mass is zero.");
        }

        double cx = bodies.Sum(b => b.Mass * b.ComWorld[0]) / mass;
        double cy = bodies.Sum(b => b.Mass * b.ComWorld[1]) / mass;
        double cz = bodies.Sum(b => b.Mass * b.ComWorld[2]) / mass;
        double[] comWorld = [cx, cy, cz];
        double[] comInLink = WorldPointInFrame(frameWorld, comWorld);

        // Rotate each body COM inertia into link axes and apply parallel-axis to link origin.
        double ixx = 0, ixy = 0, ixz = 0, iyy = 0, iyz = 0, izz = 0;
        foreach (var body in bodies)
        {
            double[] bodyToLink = MultiplyMatrix(InvertRigid(frameWorld), body.ComponentWorld);
            double[] bodyInertia = RotateInertia(body.Icom, bodyToLink);
            double[] comInLinkBody = WorldPointInFrame(frameWorld, body.ComWorld);
            double x = comInLinkBody[0];
            double y = comInLinkBody[1];
            double z = comInLinkBody[2];
            double m = body.Mass;
            ixx += bodyInertia[0] + m * (y * y + z * z);
            iyy += bodyInertia[3] + m * (x * x + z * z);
            izz += bodyInertia[5] + m * (x * x + y * y);
            ixy += bodyInertia[1] - m * x * y;
            ixz += bodyInertia[2] - m * x * z;
            iyz += bodyInertia[4] - m * y * z;
        }

        warnings.Add(
            "Inertia aggregation rotates SolidWorks COM tensors into frame_ref and applies the parallel-axis theorem; verify against the golden fixture for production dynamics.");

        if (massOverride is not null)
        {
            double scale = massOverride.Value / mass;
            mass = massOverride.Value;
            ixx *= scale;
            ixy *= scale;
            ixz *= scale;
            iyy *= scale;
            iyz *= scale;
            izz *= scale;
            warnings.Add($"Applied massOverrideKg={massOverride.Value} with linear inertia scale.");
        }

        return new AggregatedInertia(
            mass,
            comInLink,
            new { ixx, ixy, ixz, iyy, iyz, izz },
            warnings);
    }

    private static void ExportComponentStl(Component2 component, string outputPath, List<string> warnings)
    {
        string? partPath = Try(() => component.GetPathName()) as string;
        ModelDoc2? partDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        ISldWorks app = AttachSolidWorks(startIfMissing: false);
        if (!string.IsNullOrWhiteSpace(partPath))
        {
            partDoc = OpenDocument(app, partPath) ?? partDoc;
        }

        if (partDoc is null)
        {
            throw new InvalidOperationException($"Cannot export STL for {Try(() => component.Name2)}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        TryVoid(() => app.ActivateDoc3(
            partDoc.GetTitle(),
            false,
            (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
            0));

        int errors = 0;
        int warningsCode = 0;
        int options = (int)swSaveAsOptions_e.swSaveAsOptions_Silent
            | (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
        bool ok = partDoc.Extension.SaveAs(
            outputPath,
            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
            options,
            null,
            ref errors,
            ref warningsCode);
        if ((!ok || !File.Exists(outputPath)) && File.Exists(outputPath + ".stl"))
        {
            File.Move(outputPath + ".stl", outputPath, overwrite: true);
        }

        if (!File.Exists(outputPath))
        {
            // Last resort: write a tiny placeholder mesh so package generation can proceed.
            WritePlaceholderStl(outputPath);
            warnings.Add(
                $"STL export failed for {Try(() => component.Name2)} (errors={errors}, warnings={warningsCode}); wrote placeholder mesh.");
            return;
        }

        warnings.Add(
            $"STL for {Try(() => component.Name2)} exported in part default CS; ensure urdf_link_frame matches part origin or set meshOrigin.");
    }

    private static void WritePlaceholderStl(string outputPath)
    {
        // Minimal ASCII STL tetrahedron so URDF consumers still find a mesh file.
        const string stl = """
            solid placeholder
              facet normal 0 0 0
                outer loop
                  vertex 0 0 0
                  vertex 0.001 0 0
                  vertex 0 0.001 0
                endloop
              endfacet
            endsolid placeholder
            """;
        File.WriteAllText(outputPath, stl);
    }

    private static object ReadLimitMatePayload(
        ModelDoc2 doc,
        string mateName,
        double? effort,
        double? velocity,
        int sign)
    {
        Feature mateFeature = FindFeatureByName(doc, mateName)
            ?? throw new InvalidOperationException($"limit_mate not found: {mateName}");
        if (Try(() => mateFeature.GetDefinition()) is not IAngleMateFeatureData def)
        {
            throw new InvalidOperationException($"limit_mate '{mateName}' is not an angle mate.");
        }

        double min = Try(() => def.MinimumAngle) as double? ?? 0;
        double max = Try(() => def.MaximumAngle) as double? ?? 0;
        if (min > max)
        {
            (min, max) = (max, min);
        }

        return LimitPayload(min, max, effort, velocity, sign, "cad_mate", mateName);
    }

    private static object LimitPayload(
        double lower,
        double upper,
        double? effort,
        double? velocity,
        int sign,
        string source,
        string? mateName)
    {
        lower *= sign;
        upper *= sign;
        if (lower > upper)
        {
            (lower, upper) = (upper, lower);
        }
        return new
        {
            lower,
            upper,
            effort = effort ?? 0,
            velocity = velocity ?? 0,
            source,
            mateName,
        };
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void AtomicReplaceDirectory(string staging, string destination)
    {
        string backup = $"{destination}.bak_{Guid.NewGuid():N}";
        PathGuard.AssertAllowedPath(staging);
        PathGuard.AssertAllowedPath(destination);
        PathGuard.AssertAllowedPath(backup);
        bool movedExisting = false;
        try
        {
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
                movedExisting = true;
            }

            Directory.Move(staging, destination);
            if (movedExisting)
            {
                TryVoid(() => Directory.Delete(backup, recursive: true));
            }
        }
        catch
        {
            if (Directory.Exists(destination))
            {
                TryVoid(() => Directory.Delete(destination, recursive: true));
            }
            if (movedExisting && Directory.Exists(backup))
            {
                TryVoid(() => Directory.Move(backup, destination));
            }
            throw;
        }
    }

    private static double[] IdentityMatrix() =>
        [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];

    private static double[]? MatrixFromTransform(MathTransform? transform)
    {
        double[]? data = Try(() => transform?.ArrayData) as double[];
        if (data is null || data.Length < 16)
        {
            return null;
        }

        // SolidWorks MathTransform ArrayData is 16 doubles (4x4 style).
        return
        [
            data[0], data[1], data[2],
            data[3], data[4], data[5],
            data[6], data[7], data[8],
            data[9] / 1.0, data[10] / 1.0, data[11] / 1.0,
        ];
    }

    private static double[] MultiplyMatrix(double[] a, double[] b)
    {
        double[] r = new double[12];
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                r[row * 3 + col] =
                    a[row * 3 + 0] * b[0 * 3 + col]
                    + a[row * 3 + 1] * b[1 * 3 + col]
                    + a[row * 3 + 2] * b[2 * 3 + col];
            }
        }

        double[] t = TransformPoint(a, [b[9], b[10], b[11]]);
        r[9] = t[0];
        r[10] = t[1];
        r[11] = t[2];
        return r;
    }

    private static double[] TransformPoint(double[] m, double[] p) =>
    [
        m[0] * p[0] + m[1] * p[1] + m[2] * p[2] + m[9],
        m[3] * p[0] + m[4] * p[1] + m[5] * p[2] + m[10],
        m[6] * p[0] + m[7] * p[1] + m[8] * p[2] + m[11],
    ];

    private static double[] TransformDirection(double[] m, double[] d) =>
    [
        m[0] * d[0] + m[1] * d[1] + m[2] * d[2],
        m[3] * d[0] + m[4] * d[1] + m[5] * d[2],
        m[6] * d[0] + m[7] * d[1] + m[8] * d[2],
    ];

    private static double[] TransformDirectionInverse(double[] m, double[] d) =>
    [
        m[0] * d[0] + m[3] * d[1] + m[6] * d[2],
        m[1] * d[0] + m[4] * d[1] + m[7] * d[2],
        m[2] * d[0] + m[5] * d[1] + m[8] * d[2],
    ];

    private static double[] RotateInertia(double[] source, double[] transform)
    {
        double[,] tensor =
        {
            { source[0], source[1], source[2] },
            { source[1], source[3], source[4] },
            { source[2], source[4], source[5] },
        };
        double[,] rotation =
        {
            { transform[0], transform[1], transform[2] },
            { transform[3], transform[4], transform[5] },
            { transform[6], transform[7], transform[8] },
        };
        double[,] rotated = new double[3, 3];
        double[,] result = new double[3, 3];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                for (int index = 0; index < 3; index++)
                {
                    rotated[row, column] += rotation[row, index] * tensor[index, column];
                }
            }
        }
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                for (int index = 0; index < 3; index++)
                {
                    result[row, column] += rotated[row, index] * rotation[column, index];
                }
            }
        }
        return
        [
            result[0, 0], result[0, 1], result[0, 2],
            result[1, 1], result[1, 2], result[2, 2],
        ];
    }

    private static double[] WorldPointInFrame(double[] frameWorld, double[] worldPoint)
    {
        double[] delta =
        [
            worldPoint[0] - frameWorld[9],
            worldPoint[1] - frameWorld[10],
            worldPoint[2] - frameWorld[11],
        ];
        return TransformDirectionInverse(frameWorld, delta);
    }

    private static double[] ChildInParent(double[] parent, double[] child)
    {
        double[] invParent = InvertRigid(parent);
        return MultiplyMatrix(invParent, child);
    }

    private static double[] InvertRigid(double[] m)
    {
        double[] r =
        [
            m[0], m[3], m[6],
            m[1], m[4], m[7],
            m[2], m[5], m[8],
            0, 0, 0,
        ];
        double[] t = TransformDirection(r, [-m[9], -m[10], -m[11]]);
        r[9] = t[0];
        r[10] = t[1];
        r[11] = t[2];
        return r;
    }

    private static void NormalizeInPlace(double[] v)
    {
        double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (n < 1e-12)
        {
            v[0] = 0;
            v[1] = 0;
            v[2] = 1;
            return;
        }

        v[0] /= n;
        v[1] /= n;
        v[2] /= n;
    }

    private static object PoseFromMatrix(double[] m)
    {
        double sy = -m[6];
        double pitch = Math.Abs(sy) >= 1 - 1e-10
            ? (Math.PI / 2) * Math.Sign(sy)
            : Math.Asin(sy);
        double roll = Math.Atan2(m[7], m[8]);
        double yaw = Math.Atan2(m[3], m[0]);
        return new
        {
            xyz = new[] { m[9], m[10], m[11] },
            rpy = new[] { roll, pitch, yaw },
        };
    }
}
