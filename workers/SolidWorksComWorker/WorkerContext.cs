using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{

    private static Component2? ResolveTorsoLayoutComponent(IAssemblyDoc assembly)
    {
        Component2? frame = FindComponent(assembly, null, "marengo_torso_frame_asm");
        if (frame is not null)
        {
            Component2? nested = FindComponent(assembly, frame, "marengo_torso_layout");
            if (nested is not null)
            {
                return nested;
            }
        }

        return FindComponent(assembly, null, "marengo_torso_layout");
    }

    private static Component2? FindComponent(IAssemblyDoc assembly, Component2? parent, string nameOrPrefix)
    {
        if (parent is null)
        {
            Component2? direct = Try(() => assembly.GetComponentByName(nameOrPrefix)) as Component2;
            if (direct is not null)
            {
                return direct;
            }
        }

        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(true)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return null;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            string? name = Try(() => component.Name2) as string;
            if (name is not null
                && (name.Equals(nameOrPrefix, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(nameOrPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return component;
            }

            Component2? nested = FindComponent(assembly, component, nameOrPrefix);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void CollectComponentTree(IAssemblyDoc assembly, Component2? parent, List<object> output, int depth)
    {
        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(false)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            output.Add(DescribeComponent(component, depth));
            CollectComponentTree(assembly, component, output, depth + 1);
        }
    }

    private static void CollectBomLines(IAssemblyDoc assembly, Component2? parent, List<object> output)
    {
        object[]? roots = parent is null
            ? Try(() => assembly.GetComponents(true)) as object[]
            : Try(() => parent.GetChildren()) as object[];

        if (roots is null)
        {
            return;
        }

        foreach (object entry in roots)
        {
            if (entry is not Component2 component)
            {
                continue;
            }

            output.Add(DescribeComponent(component, depth: 0));
            CollectBomLines(assembly, component, output);
        }
    }

    private static object DescribeComponent(Component2 component, int depth)
    {
        return new
        {
            name = Try(() => component.Name2),
            path = Try(() => component.GetPathName()),
            configuration = Try(() => component.ReferencedConfiguration),
            suppressed = Try(() => component.IsSuppressed()),
            isFixed = Try(() => component.IsFixed()),
            visible = Try(() => component.Visible),
            depth,
        };
    }

    private static object DescribeUnits(ISldWorks app, ModelDoc2 doc)
    {
        int? lengthUnit = Try(() => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear)) as int?;
        int? massUnit = Try(() => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsMassPropMass)) as int?;

        return new
        {
            length = lengthUnit,
            mass = massUnit,
            documentType = Try(() => doc.GetType()),
        };
    }

    private static IReadOnlyList<object> ListCustomProperties(ModelDoc2 doc)
    {
        var properties = new List<object>();
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            return properties;
        }

        string[]? names = Try(() => manager.GetNames()) as string[];
        if (names is null)
        {
            return properties;
        }

        foreach (string name in names)
        {
            string value = "";
            string resolved = "";
            TryVoid(() => manager.Get2(name, out value, out resolved));
            properties.Add(new
            {
                name,
                value = string.IsNullOrWhiteSpace(resolved) ? value : resolved,
            });
        }

        return properties;
    }

    private static ISldWorks AttachSolidWorks(bool startIfMissing)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SolidWorks COM automation requires Windows.");
        }

        Type? swType = Type.GetTypeFromProgID("SldWorks.Application", throwOnError: false);
        if (swType is null)
        {
            throw new InvalidOperationException("SldWorks.Application COM ProgID is not registered.");
        }

        Guid classId = ComClassId.FromProgId("SldWorks.Application");

        if (IsSolidWorksProcessRunning())
        {
            try
            {
                return (ISldWorks)RunningObjectTable.GetActiveObject(classId);
            }
            catch
            {
                throw new InvalidOperationException(
                    "SolidWorks is running but COM attach failed. Close duplicate SolidWorks instances "
                    + "(Task Manager → end extra SLDWORKS.exe with no window) and retry.");
            }
        }

        if (startIfMissing)
        {
            object? created = Activator.CreateInstance(swType);
            if (created is null)
            {
                throw new InvalidOperationException("Failed to start SolidWorks via COM.");
            }

            ISldWorks app = (ISldWorks)created;
            app.Visible = true;
            return app;
        }

        throw new InvalidOperationException("SolidWorks is not running. Pass start_if_missing=true to launch it.");
    }

    private static bool IsSolidWorksProcessRunning()
    {
        int currentProcessId = System.Environment.ProcessId;
        return Process.GetProcesses().Any(process =>
            process.Id != currentProcessId
            && process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase));
    }

    private static ModelDoc2 OpenDocument(ISldWorks app, string path)
    {
        string fullPath = Path.GetFullPath(path);
        ModelDoc2? existing = FindOpenDocument(app, fullPath);
        if (existing is not null)
        {
            int activateErrors = 0;
            string? title = Try(() => existing.GetTitle()) as string;
            if (!string.IsNullOrWhiteSpace(title))
            {
                Try(() => app.ActivateDoc3(title, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors));
            }

            return existing;
        }

        int errors = 0;
        int warnings = 0;
        if (IsNeutralCad(path))
        {
            object? importData = Try(() => app.GetImportFileData(path));
            ModelDoc2? imported = app.LoadFile4(path, "r", importData, ref errors);
            if (imported is null || errors != 0)
            {
                throw new InvalidOperationException(
                    $"SolidWorks failed to import {path}. errors={errors} ({DecodeFileLoadErrors(errors)}), warnings={warnings}");
            }

            return imported;
        }

        int docType = DocumentType(path);
        ModelDoc2? doc = app.OpenDoc6(path, docType, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as ModelDoc2;
        if (doc is null || errors != 0)
        {
            throw new InvalidOperationException(
                $"SolidWorks failed to open {path}. errors={errors} ({DecodeFileLoadErrors(errors)}), warnings={warnings}");
        }

        string? openedTitle = Try(() => doc.GetTitle()) as string;
        if (!string.IsNullOrWhiteSpace(openedTitle))
        {
            int activateErrors = 0;
            Try(() => app.ActivateDoc3(openedTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateErrors));
        }

        return doc;
    }

    private static ModelDoc2? FindOpenDocument(ISldWorks app, string fullPath)
    {
        ModelDoc2? byPath = Try(() => app.GetOpenDocumentByName(fullPath)) as ModelDoc2;
        if (byPath is not null)
        {
            return byPath;
        }

        string fileName = Path.GetFileName(fullPath);
        ModelDoc2? byName = Try(() => app.GetOpenDocumentByName(fileName)) as ModelDoc2;
        if (byName is null)
        {
            return null;
        }

        string? openPath = Try(() => byName.GetPathName()) as string;
        if (string.IsNullOrWhiteSpace(openPath))
        {
            return null;
        }

        return string.Equals(Path.GetFullPath(openPath), fullPath, StringComparison.OrdinalIgnoreCase)
            ? byName
            : null;
    }

    private static bool IsNeutralCad(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".step" or ".stp" or ".iges" or ".igs";
    }

    private static string DecodeFileLoadErrors(int errors)
    {
        if (errors == 0)
        {
            return "none";
        }

        var names = new List<string>();
        foreach (swFileLoadError_e value in Enum.GetValues<swFileLoadError_e>())
        {
            int flag = (int)value;
            if (flag != 0 && (errors & flag) == flag)
            {
                names.Add(value.ToString());
            }
        }

        return names.Count == 0 ? "unknown" : string.Join("|", names);
    }

    private static int DocumentType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".sldasm" => 2,
            ".slddrw" => 3,
            ".sldprt" => 1,
            ".step" => 1,
            ".stp" => 1,
            _ => 1,
        };
    }

    private static object? DescribeDocument(object? doc)
    {
        if (doc is null)
        {
            return null;
        }

        if (doc is ModelDoc2 modelDoc)
        {
            return new
            {
                title = Try(() => modelDoc.GetTitle()),
                path = Try(() => modelDoc.GetPathName()),
                type = Try(() => modelDoc.GetType()),
            };
        }

        dynamic d = doc;
        return new
        {
            title = Try(() => d.GetTitle()),
            path = Try(() => d.GetPathName()),
            type = Try(() => d.GetType()),
        };
    }

    private static object? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Array array)
        {
            var normalized = new List<object?>();
            foreach (object? item in array)
            {
                normalized.Add(Normalize(item));
            }

            return normalized;
        }

        return value;
    }

    private static object? Try(Func<object?> action)
    {
        try
        {
            return action();
        }
        catch
        {
            return null;
        }
    }

    private static void TryVoid(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Preview preparation is best-effort.
        }
    }

    private static string RequiredStringArg(JsonElement? args, string name)
    {
        return StringArg(args, name) ?? throw new ArgumentException($"Missing required argument: {name}");
    }

    private static string? StringArg(JsonElement? args, string name)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return args.Value.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, string> PropertiesArg(JsonElement? args)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("properties object is required.");
        }

        if (!args.Value.TryGetProperty("properties", out JsonElement props) || props.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("properties object is required.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty prop in props.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText(),
            };
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("properties object must contain at least one entry.");
        }

        return result;
    }

    private static bool BoolArg(JsonElement? args, string name, bool defaultValue = false)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        return args.Value.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True
            ? true
            : value.ValueKind == JsonValueKind.False
                ? false
                : defaultValue;
    }

    private static double DoubleArg(JsonElement? args, string name, double defaultValue = 0)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            return defaultValue;
        }

        if (!args.Value.TryGetProperty(name, out JsonElement value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), out double parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static double[] DoubleArrayArg(JsonElement? args, string name)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Missing required argument: {name}");
        }

        if (!args.Value.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"Missing required array argument: {name}");
        }

        var values = new List<double>();
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number)
            {
                throw new ArgumentException($"Array argument {name} must contain only numbers.");
            }

            values.Add(entry.GetDouble());
        }

        return values.ToArray();
    }

    private static string[] AllowedRoots()
    {
        string? raw = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_ALLOWED_ROOTS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ["C:\\code\\marengo"];
        }

        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string AssertAllowedPath(string inputPath)
    {
        string resolved = Path.GetFullPath(inputPath);
        string normalized = resolved.Replace('/', '\\').ToLowerInvariant();
        foreach (string root in AllowedRoots())
        {
            string normalizedRoot = Path.GetFullPath(root).Replace('/', '\\').ToLowerInvariant().TrimEnd('\\');
            if (normalized == normalizedRoot || normalized.StartsWith(normalizedRoot + "\\", StringComparison.Ordinal))
            {
                return resolved;
            }
        }

        throw new InvalidOperationException(
            $"Path is outside allowed CAD roots: {resolved}. Set SOLIDWORKS_MCP_ALLOWED_ROOTS.");
    }
}