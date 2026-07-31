using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object InsertComponent(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string partPath = RequiredStringArg(args, "part_path");
        string? name = StringArg(args, "name");
        string? configuration = StringArg(args, "configuration") ?? string.Empty;
        bool save = BoolArg(args, "save", defaultValue: true);

        string resolvedPartPath = PathGuard.AssertAllowedPath(Path.GetFullPath(partPath));
        if (!File.Exists(resolvedPartPath))
        {
            throw new InvalidOperationException($"Part file not found: {resolvedPartPath}");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("insert_component requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        AssemblyDoc assemblyDoc = (AssemblyDoc)doc;
        TryVoid(() => app.ActivateDoc3(doc.GetTitle(), true, 0, 0));

        // Ensure the part loads before inserting into the assembly.
        OpenDocument(app, resolvedPartPath);

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object[]? beforeComponents = Try(() => assembly.GetComponents(false)) as object[];
        if (beforeComponents is not null)
        {
            foreach (object entry in beforeComponents)
            {
                if (entry is Component2 existing && Try(() => existing.Name2) is string existingName)
                {
                    existingNames.Add(existingName);
                }
            }
        }

        Component2? component = null;
        try
        {
            component = assemblyDoc.AddComponent5(
                resolvedPartPath,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                configuration,
                false,
                string.Empty,
                0,
                0,
                0) as Component2;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"AddComponent5 failed for {resolvedPartPath}: {ex.Message}");
        }

        if (component is null)
        {
            object[]? afterComponents = Try(() => assembly.GetComponents(false)) as object[];
            if (afterComponents is not null)
            {
                foreach (object entry in afterComponents)
                {
                    if (entry is not Component2 candidate)
                    {
                        continue;
                    }

                    string? candidateName = Try(() => candidate.Name2) as string;
                    if (candidateName is not null && !existingNames.Contains(candidateName))
                    {
                        component = candidate;
                        break;
                    }
                }
            }
        }

        if (component is null)
        {
            throw new InvalidOperationException($"Failed to insert component: {resolvedPartPath}");
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            doc.ClearSelection2(true);
            bool selected = Try(() => component.Select4(false, null, false)) as bool? ?? false;
            if (!selected)
            {
                throw new InvalidOperationException($"Could not select inserted component for rename: {Try(() => component.Name2)}");
            }

            bool renamed = Try(() => ((dynamic)component).SetName(name)) as bool? ?? false;
            if (!renamed)
            {
                TryVoid(() => component.Name2 = name);
            }

            doc.ClearSelection2(true);
        }

        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after insert. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            partPath,
            saved,
            errors,
            warnings,
        };
    }

    private static bool SelectAssemblyComponent(ModelDoc2 asmDoc, Component2 component, bool append, int mark = 0)
    {
        string? compName = Try(() => component.Name2) as string;
        string? docTitle = Try(() => asmDoc.GetTitle()) as string;
        if (!string.IsNullOrWhiteSpace(compName) && !string.IsNullOrWhiteSpace(docTitle))
        {
            string selectionId = compName.Contains('@', StringComparison.Ordinal)
                ? compName
                : $"{compName}@{docTitle}";
            if (Try(() => asmDoc.Extension.SelectByID2(
                    selectionId,
                    "COMPONENT",
                    0,
                    0,
                    0,
                    append,
                    mark,
                    null,
                    0)) as bool? == true)
            {
                return true;
            }
        }

        SelectData? selectData = mark == 0 ? null : CreateSelectData(asmDoc, mark);
        return Try(() => component.Select4(append, selectData, false)) as bool? == true;
    }

    private static Body2? CopyToolBodyForBracketCut(
        ISldWorks app,
        Component2 toolInAsm,
        string toolPartPath)
    {
        object? compBodiesObj = Try(() => toolInAsm.GetBodies2((int)swBodyType_e.swSolidBody));
        if (compBodiesObj is object[] compBodies)
        {
            foreach (object entry in compBodies)
            {
                if (entry is not Body2 sourceBody)
                {
                    continue;
                }

                Body2? copied = Try(() => sourceBody.Copy()) as Body2;
                if (copied is not null)
                {
                    return copied;
                }
            }
        }

        return CopyPrimarySolidBody(app, toolPartPath);
    }

    private static Body2? CopyPrimarySolidBody(ISldWorks app, string toolPartPath)
    {
        ModelDoc2 vendorDoc = OpenDocument(app, toolPartPath);
        Body2? sourceBody = GetPrimarySolidBody(vendorDoc);
        if (sourceBody is null)
        {
            return null;
        }

        return Try(() => sourceBody.Copy()) as Body2;
    }

    private static Body2? ImportBodyIntoPart(
        ModelDoc2 partDoc,
        Body2 copiedBody,
        Body2 targetBody,
        string toolPartPath,
        MathTransform? insertTransform)
    {
        dynamic part = (IPartDoc)partDoc;
        dynamic model = partDoc;
        dynamic featMgr = partDoc.FeatureManager;
        object[] bodyArray = [copiedBody];

        bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, insertTransform, 0)) as bool? == true
            || Try(() => (bool)part.InsertBodies(bodyArray, insertTransform)) as bool? == true;
        if (!inserted)
        {
            inserted = Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
        }

        if (!inserted)
        {
            Feature? imported = Try(() => (Feature)model.CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
            if (imported is not null)
            {
                partDoc.EditRebuild3();
                Body2? importedBody = GetInsertedToolSolidBody(partDoc, targetBody);
                if (importedBody is not null && insertTransform is not null)
                {
                    TryVoid(() => importedBody.ApplyTransform(insertTransform));
                    partDoc.EditRebuild3();
                }

                return importedBody;
            }
        }
        else
        {
            partDoc.EditRebuild3();
            Body2? importedBody = GetInsertedToolSolidBody(partDoc, targetBody);
            if (importedBody is not null)
            {
                return importedBody;
            }
        }

        string fullToolPath = Path.GetFullPath(toolPartPath);
        int insertErrors = 0;
        Feature? insertedPart = Try(() => (Feature)featMgr.InsertPart(fullToolPath, 1, ref insertErrors)) as Feature;
        if (insertedPart is null)
        {
            insertErrors = 0;
            insertedPart = Try(() => (Feature)featMgr.InsertPart(fullToolPath, 0, ref insertErrors)) as Feature;
        }

        if (insertedPart is not null)
        {
            partDoc.EditRebuild3();
            Body2? insertedBody = GetInsertedToolSolidBody(partDoc, targetBody);
            if (insertedBody is not null && insertTransform is not null)
            {
                TryVoid(() => insertedBody.ApplyTransform(insertTransform));
                partDoc.EditRebuild3();
            }

            return insertedBody;
        }

        return null;
    }

    private static void CloseDocumentIfOpen(ISldWorks app, string path)
    {
        ModelDoc2? doc = FindOpenDocument(app, Path.GetFullPath(path));
        if (doc is null)
        {
            return;
        }

        string? title = Try(() => doc.GetTitle()) as string;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        TryVoid(() => app.CloseDoc(title));
    }

    private static Feature? InsertToolPartFeature(dynamic featMgr, string toolPartPath)
    {
        int errors = 0;
        Feature? inserted = Try(() => featMgr.InsertPart(toolPartPath, 0, ref errors)) as Feature;
        if (inserted is null || errors != 0)
        {
            throw new InvalidOperationException($"InsertPart failed for tool part. errors={errors}");
        }

        return inserted;
    }

    private static Body2? GetPrimarySolidBody(ModelDoc2 partDoc)
    {
        object? bodiesObj = Try(() => ((IPartDoc)partDoc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is object[] bodies)
        {
            foreach (object entry in bodies)
            {
                if (entry is Body2 body)
                {
                    return body;
                }
            }
        }

        return null;
    }

    private static Body2? GetInsertedToolSolidBody(ModelDoc2 partDoc, Body2 targetBody)
    {
        object? bodiesObj = Try(() => ((IPartDoc)partDoc).GetBodies2((int)swBodyType_e.swSolidBody, true));
        if (bodiesObj is not object[] bodies)
        {
            return null;
        }

        foreach (object entry in bodies)
        {
            if (entry is Body2 body && !ReferenceEquals(body, targetBody))
            {
                return body;
            }
        }

        return null;
    }

    private static double[] InvertTransformMatrix(double[] matrix)
    {
        if (matrix.Length != 16)
        {
            throw new InvalidOperationException("Transform matrix must contain 16 numbers.");
        }

        double r00 = matrix[0];
        double r01 = matrix[1];
        double r02 = matrix[2];
        double r10 = matrix[3];
        double r11 = matrix[4];
        double r12 = matrix[5];
        double r20 = matrix[6];
        double r21 = matrix[7];
        double r22 = matrix[8];
        double tx = matrix[9];
        double ty = matrix[10];
        double tz = matrix[11];

        return
        [
            r00, r10, r20, 0,
            r01, r11, r21, 0,
            r02, r12, r22, 0,
            -(r00 * tx + r10 * ty + r20 * tz),
            -(r01 * tx + r11 * ty + r21 * tz),
            -(r02 * tx + r12 * ty + r22 * tz),
            1,
            0, 0, 0,
        ];
    }

}