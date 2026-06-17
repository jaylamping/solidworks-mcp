using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object ExportLinkTransforms(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
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
                    transform = Normalize(Try(() => component.Transform2)),
                });
            }
        }

        return new { document = DescribeDocument(doc), transforms };
    }
}
