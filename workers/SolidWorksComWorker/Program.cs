using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal sealed record WorkerRequest(string Command, JsonElement? Args);

internal static class Program
{
    private static readonly JsonSerializerOptions ReadJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [STAThread]
    private static int Main()
    {
        try
        {
            string input = Console.In.ReadToEnd();
            WorkerRequest? request = JsonSerializer.Deserialize<WorkerRequest>(input, ReadJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return WriteError("Missing worker command.");
            }

            object data = request.Command switch
            {
                "status" => Status(request.Args),
                "open" => Open(request.Args),
                "export" => Export(request.Args),
                "measure" => Measure(request.Args),
                "list_features" => ListFeatures(request.Args),
                _ => throw new InvalidOperationException($"Unknown worker command: {request.Command}"),
            };

            Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, WriteJson));
            return 0;
        }
        catch (Exception ex)
        {
            return WriteError(ex.Message);
        }
    }

    private static int WriteError(string error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error }, WriteJson));
        return 0;
    }

    private static object Status(JsonElement? args)
    {
        bool startIfMissing = BoolArg(args, "start_if_missing");
        ISldWorks app = AttachSolidWorks(startIfMissing);
        object? version = Try(() => app.RevisionNumber());
        if (version is null)
        {
            throw new InvalidOperationException(
                "SolidWorks COM object was found, but API calls are not responding. Check SolidWorks launch state and COM/type-library registration.");
        }

        object? doc = Try(() => app.ActiveDoc);

        return new
        {
            running = true,
            version,
            activeDocument = DescribeDocument(doc),
        };
    }

    private static object Open(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CAD document does not exist.", path);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2 doc = OpenDocument(app, path);
        return DescribeDocument(doc) ?? new { path };
    }

    private static object Export(JsonElement? args)
    {
        string outputPath = RequiredStringArg(args, "output_path");
        string? inputPath = StringArg(args, "path");
        bool startIfMissing = BoolArg(args, "start_if_missing", defaultValue: true);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        ISldWorks app = AttachSolidWorks(startIfMissing);
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to export.");
        }

        int errors = 0;
        int warnings = 0;
        if (IsPreviewExport(outputPath))
        {
            PreparePreview(app, doc);
        }

        ModelDocExtension extension = doc.Extension;
        bool ok = extension.SaveAs(outputPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);

        return new
        {
            ok,
            outputPath,
            errors,
            warnings,
            exists = File.Exists(outputPath),
        };
    }

    private static bool IsPreviewExport(string outputPath)
    {
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }

    private static void PreparePreview(ISldWorks app, ModelDoc2 doc)
    {
        HideReferenceGeometryForPreview(app);
        TryVoid(() => doc.ClearSelection2(true));
        SelectReferenceFeatures(doc);
        TryVoid(() => doc.BlankRefGeom());
        TryVoid(() => doc.ClearSelection2(true));
        TryVoid(() => doc.BlankSketch());
        TryVoid(() => doc.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView));
        TryVoid(() => doc.ViewZoomtofit2());

        ModelView? view = Try(() => doc.ActiveView) as ModelView;
        if (view is not null)
        {
            TryVoid(() => view.FrameState = (int)swWindowState_e.swWindowMaximized);
            Try(() => view.EnableGraphicsUpdate = true);
        }

        TryVoid(() => doc.GraphicsRedraw2());
    }

    private static void SelectReferenceFeatures(ModelDoc2 doc)
    {
        object? feature = Try(() => doc.FirstFeature());
        bool append = false;
        int guard = 0;
        while (feature is not null && guard++ < 1000)
        {
            dynamic current = feature;
            string? type = Try(() => current.GetTypeName2()) as string;
            if (type is "RefPlane" or "RefAxis" or "RefPoint" or "CoordSys")
            {
                bool selected = Try(() => current.Select2(append, 0)) as bool? ?? false;
                append = append || selected;
            }

            feature = Try(() => current.GetNextFeature());
        }
    }

    private static void HideReferenceGeometryForPreview(ISldWorks app)
    {
        swUserPreferenceToggle_e[] toggles =
        [
            swUserPreferenceToggle_e.swDisplayPlanes,
            swUserPreferenceToggle_e.swDisplayAxes,
            swUserPreferenceToggle_e.swDisplayTemporaryAxes,
            swUserPreferenceToggle_e.swDisplayCoordSystems,
            swUserPreferenceToggle_e.swDisplayOrigins,
            swUserPreferenceToggle_e.swDisplaySketches,
            swUserPreferenceToggle_e.swDisplaySketchPlanes,
        ];

        foreach (swUserPreferenceToggle_e toggle in toggles)
        {
            TryVoid(() => app.SetUserPreferenceToggle((int)toggle, false));
        }
    }

    private static object Measure(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to measure.");
        }

        MassProperty? massProperty = Try(() => doc.Extension.CreateMassProperty()) as MassProperty;
        object? boundingBox = BoundingBox(doc);

        return new
        {
            document = DescribeDocument(doc),
            boundingBox = Normalize(boundingBox),
            mass = Normalize(Try(() => massProperty?.Mass)),
            centerOfMass = Normalize(Try(() => massProperty?.CenterOfMass)),
            momentsOfInertia = Normalize(Try(() => massProperty?.GetMomentOfInertia(0))),
        };
    }

    private static object? BoundingBox(ModelDoc2 doc)
    {
        return doc.GetType() switch
        {
            (int)swDocumentTypes_e.swDocPART => Try(() => ((IPartDoc)doc).GetPartBox(true)),
            (int)swDocumentTypes_e.swDocASSEMBLY => Try(() => ((IAssemblyDoc)doc).GetBox(1)),
            _ => null,
        };
    }

    private static object ListFeatures(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        var features = new List<object>();
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 500)
        {
            dynamic current = feature;
            features.Add(new
            {
                name = Try(() => current.Name),
                type = Try(() => current.GetTypeName2()),
            });
            feature = Try(() => current.GetNextFeature());
        }

        return new
        {
            document = DescribeDocument(doc),
            features,
            truncated = guard >= 500,
        };
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
            catch when (startIfMissing)
            {
                // Fall through to explicit activation below.
            }
            catch
            {
                throw new InvalidOperationException("SolidWorks process is running, but COM attach failed.");
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

        return doc;
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
}

internal static class RunningObjectTable
{
    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object GetActiveObject(Guid classId)
    {
        int hr = GetActiveObject(ref classId, IntPtr.Zero, out object activeObject);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return activeObject;
    }
}

internal static class ComClassId
{
    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string progId,
        out Guid classId);

    public static Guid FromProgId(string progId)
    {
        CLSIDFromProgID(progId, out Guid classId);
        return classId;
    }
}
