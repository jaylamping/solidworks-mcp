using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    // transform.arrayData is the component's MathTransform relative to the root assembly:
    // [0..8] are rows 0-2 of R (the component's X, Y and Z axes in assembly coordinates),
    // [9..11] is t (the component origin, in metres), [12] is scale and [13..15] are unused.
    // A component point maps to the assembly as p_asm = p_local * R + t (row vector).
    private static object ExportLinkTransforms(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string? outputArg = StringArg(args, "output_path");
        string? outputPath = outputArg is null ? null : PathGuard.AssertAllowedPath(outputArg);
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("export_link_transforms requires an assembly.");
        }

        var transforms = new List<object>();
        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        object[]? components = Try(() => assembly.GetComponents(false)) as object[];
        if (components is not null)
        {
            foreach (object entry in components)
            {
                if (entry is not Component2 component)
                {
                    continue;
                }

                transforms.Add(new
                {
                    name = Try(() => component.Name2),
                    path = Try(() => component.GetPathName()),
                    transform = Normalize(Try(() => component.Transform2)),
                });
            }
        }

        var result = new { document = DescribeDocument(doc), transforms };
        if (outputPath is null)
        {
            return result;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, WriteJson));
        return new { result.document, result.transforms, outputPath };
    }
}
