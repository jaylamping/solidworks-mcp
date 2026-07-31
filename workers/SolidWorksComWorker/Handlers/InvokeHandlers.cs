using System.Reflection;
using System.Text.Json;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static readonly HashSet<string> InvokeReadAllowlist = new(StringComparer.Ordinal)
    {
        "GetTitle",
        "GetPathName",
        "GetType",
        "GetConfigurationNames",
        "RevisionNumber",
        "GetDocumentCount",
        "GetActiveDoc",
        "GetMassProperty",
    };

    private static readonly HashSet<string> InvokeWriteAllowlist = new(StringComparer.Ordinal)
    {
        "EditRebuild3",
        "ShowConfiguration2",
    };

    private static bool InvokeWriteEnabled() =>
        string.Equals(
            System.Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_INVOKE_WRITE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static object Invoke(JsonElement? args) => InvokeSingle(args);

    private static object BatchInvoke(JsonElement? args)
    {
        if (args is null || !args.Value.TryGetProperty("calls", out JsonElement calls) || calls.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("batch_invoke requires calls array.");
        }

        var results = new List<object>();
        int index = 0;
        foreach (JsonElement call in calls.EnumerateArray())
        {
            try
            {
                results.Add(new { index, ok = true, data = InvokeSingle(call) });
            }
            catch (Exception ex)
            {
                results.Add(new { index, ok = false, error = ex.Message });
            }

            index++;
        }

        return new { count = results.Count, results };
    }

    private static object InvokeSingle(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        string target = StringArg(args, "target") ?? "active_doc";
        string member = RequiredStringArg(args, "member");
        bool isProperty = BoolArg(args, "property", defaultValue: false);
        string? componentName = StringArg(args, "component_name");
        string? persistReference = StringArg(args, "persist_reference");

        bool isWrite = !isProperty && !member.StartsWith("Get", StringComparison.Ordinal) && !member.StartsWith("Is", StringComparison.Ordinal);
        if (isWrite)
        {
            if (!InvokeWriteEnabled())
            {
                throw new InvalidOperationException(
                    "Invoke write blocked. Set SOLIDWORKS_MCP_INVOKE_WRITE=true and use allowlisted members only.");
            }

            if (!InvokeWriteAllowlist.Contains(member))
            {
                throw new InvalidOperationException($"Invoke member not allowlisted for write: {member}");
            }
        }
        else if (!InvokeReadAllowlist.Contains(member))
        {
            throw new InvalidOperationException($"Invoke member not allowlisted for read: {member}");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        object targetObj = ResolveInvokeTarget(app, inputPath, target, componentName, persistReference);

        ScanInvokeArgsForPaths(args);

        object? result;
        if (isProperty)
        {
            result = targetObj.GetType().InvokeMember(
                member,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                targetObj,
                null);
        }
        else
        {
            object?[] invokeArgs = ParseInvokeArgs(args, targetObj);
            result = targetObj.GetType().InvokeMember(
                member,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                targetObj,
                invokeArgs);
        }

        return new
        {
            target,
            member,
            result = SerializeInvokeResult(app, result),
        };
    }

    private static object ResolveInvokeTarget(
        ISldWorks app,
        string? inputPath,
        string target,
        string? componentName,
        string? persistReference)
    {
        return target switch
        {
            "app" => app,
            "active_doc" => string.IsNullOrWhiteSpace(inputPath)
                ? app.ActiveDoc as ModelDoc2 ?? throw new InvalidOperationException("No active document.")
                : OpenDocument(app, inputPath),
            "component" => ResolveComponentTarget(app, inputPath, componentName),
            "persist_ref" => ResolvePersistRefTarget(app, inputPath, persistReference),
            _ => throw new InvalidOperationException($"Unknown invoke target: {target}"),
        };
    }

    private static object ResolveComponentTarget(ISldWorks app, string? inputPath, string? componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            throw new ArgumentException("component_name required for component target.");
        }

        ModelDoc2 doc = string.IsNullOrWhiteSpace(inputPath)
            ? app.ActiveDoc as ModelDoc2 ?? throw new InvalidOperationException("No active document.")
            : OpenDocument(app, inputPath);

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("component target requires an assembly.");
        }

        return FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");
    }

    private static object ResolvePersistRefTarget(ISldWorks app, string? inputPath, string? persistReference)
    {
        if (string.IsNullOrWhiteSpace(persistReference))
        {
            throw new ArgumentException("persist_reference required for persist_ref target.");
        }

        ModelDoc2 doc = string.IsNullOrWhiteSpace(inputPath)
            ? app.ActiveDoc as ModelDoc2 ?? throw new InvalidOperationException("No active document.")
            : OpenDocument(app, inputPath);

        byte[] bytes = Convert.FromBase64String(persistReference);
        int errorCode = 0;
        object? entity = doc.Extension.GetObjectByPersistReference3(bytes, out errorCode);
        if (entity is null)
        {
            throw new InvalidOperationException($"GetObjectByPersistReference3 failed: errorCode={errorCode}");
        }

        return entity;
    }

    private static object?[] ParseInvokeArgs(JsonElement? args, object targetObj)
    {
        if (args is null || !args.Value.TryGetProperty("args", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<object?>();
        foreach (JsonElement entry in arr.EnumerateArray())
        {
            list.Add(DeserializeInvokeArg(entry, targetObj));
        }

        return list.ToArray();
    }

    private static object? DeserializeInvokeArg(JsonElement entry, object targetObj)
    {
        if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("$persistRef", out JsonElement pref))
        {
            string? b64 = pref.GetString();
            if (string.IsNullOrWhiteSpace(b64))
            {
                throw new ArgumentException("Empty $persistRef.");
            }

            ModelDoc2? doc = targetObj as ModelDoc2
                ?? (targetObj as Component2)?.GetModelDoc2() as ModelDoc2
                ?? throw new InvalidOperationException("Cannot resolve persist ref without document context.");

            byte[] bytes = Convert.FromBase64String(b64);
            int code = 0;
            return doc.Extension.GetObjectByPersistReference3(bytes, out code)
                ?? throw new InvalidOperationException($"Persist ref resolve failed: {code}");
        }

        return entry.ValueKind switch
        {
            JsonValueKind.String => entry.GetString(),
            JsonValueKind.Number => entry.TryGetInt64(out long l) ? l : entry.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => entry.GetRawText(),
        };
    }

    private static object? SerializeInvokeResult(ISldWorks app, object? result)
    {
        if (result is null) return null;
        if (result is string or bool or int or long or float or double) return result;
        if (result is Array arr)
        {
            return arr.Cast<object?>().Select(v => SerializeInvokeResult(app, v)).ToArray();
        }

        if (result is ModelDoc2 doc)
        {
            byte[]? bytes = Try(() => doc.Extension.GetPersistReference3(doc)) as byte[];
            return new Dictionary<string, object?>
            {
                ["$type"] = result.GetType().FullName,
                ["$persistRef"] = bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : null,
                ["title"] = Try(() => doc.GetTitle()),
            };
        }

        if (result is Entity entity)
        {
            ModelDoc2? owner = app.ActiveDoc as ModelDoc2;
            byte[]? bytes = owner is null ? null : Try(() => owner.Extension.GetPersistReference3(entity)) as byte[];
            return new Dictionary<string, object?>
            {
                ["$type"] = result.GetType().FullName,
                ["$persistRef"] = bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : null,
            };
        }

        return new Dictionary<string, object?>
        {
            ["$type"] = result.GetType().FullName,
            ["value"] = Try(() => result.ToString()),
        };
    }

    private static void ScanInvokeArgsForPaths(JsonElement? args)
    {
        if (args is null || !args.Value.TryGetProperty("args", out JsonElement arr)) return;
        foreach (JsonElement entry in arr.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                string? s = entry.GetString();
                if (LooksLikePath(s)) PathGuard.AssertAllowedPath(s!);
            }
        }
    }

    private static bool LooksLikePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains('\\') || value.Contains('/') || value.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
    }
}
