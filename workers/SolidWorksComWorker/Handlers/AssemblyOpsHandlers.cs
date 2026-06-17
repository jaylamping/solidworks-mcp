using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object LayoutAddShoulderMounts(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        bool save = BoolArg(args, "save", defaultValue: true);
        bool replaceExisting = BoolArg(args, "replace_existing", defaultValue: true);
        double innerRailM = DoubleArg(args, "inner_rail_mm", 55.0) / 1000.0;
        double outerPokeM = DoubleArg(args, "outer_poke_mm", 95.0) / 1000.0;
        double depthOffsetM = DoubleArg(args, "depth_offset_mm", 0.0) / 1000.0;

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("layout_add_shoulder_mounts requires a part document.");
        }

        double[]? innerBox = GetFeatureBoundingBoxInPart(doc, "torso_inner_clear");
        double[]? outerBox = GetFeatureBoundingBoxInPart(doc, "torso_outer_envelope");
        if (innerBox is null || outerBox is null)
        {
            throw new InvalidOperationException(
                "Layout part must contain torso_inner_clear and torso_outer_envelope ICE.");
        }

        double shoulderHeight = outerBox[4];
        double midDepth = (innerBox[2] + innerBox[5]) / 2.0;
        double depth = midDepth + depthOffsetM;
        double jointLeftX = innerRailM / 2.0;
        double jointRightX = -jointLeftX;

        var created = new List<object>();
        foreach ((string planeName, double offsetM) in new[]
                 {
                     ("shoulder_rail_inner_left", innerRailM),
                     ("shoulder_rail_inner_right", -innerRailM),
                     ("shoulder_poke_outer_left", outerPokeM),
                     ("shoulder_poke_outer_right", -outerPokeM),
                 })
        {
            if (replaceExisting)
            {
                DeleteFeatureByName(doc, planeName);
            }
            else if (FindFeatureByName(doc, planeName) is not null)
            {
                created.Add(new { name = planeName, type = "plane", skipped = true, reason = "already_exists" });
                continue;
            }

            Feature? plane = InsertOffsetPlaneFromRight(doc, offsetM, planeName);
            if (plane is null)
            {
                throw new InvalidOperationException($"Failed to create offset plane: {planeName}");
            }

            created.Add(new { name = planeName, type = "plane", offsetM = offsetM, skipped = false });
        }

        foreach ((string name, double jointX) in new[]
                 {
                     ("shoulder_mount_left", jointLeftX),
                     ("shoulder_mount_right", jointRightX),
                 })
        {
            if (replaceExisting)
            {
                DeleteFeatureByName(doc, name);
            }
            else if (FindFeatureByName(doc, name) is not null)
            {
                created.Add(new { name, type = "coord_sys", skipped = true, reason = "already_exists" });
                continue;
            }

            Feature? coordFeature = CreateShoulderMountCoordSys(doc, name, jointX, shoulderHeight, depth);
            if (coordFeature is null)
            {
                throw new InvalidOperationException($"Failed to create coordinate system: {name}");
            }

            created.Add(new
            {
                name,
                type = "coord_sys",
                role = "joint_axis",
                originM = new[] { jointX, shoulderHeight, depth },
                skipped = false,
            });
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
                throw new InvalidOperationException($"Save failed after shoulder mounts. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            shoulderHeightM = shoulderHeight,
            innerRailM = innerRailM,
            outerPokeM = outerPokeM,
            innerClearBoxM = innerBox,
            created,
            saved,
            errors,
            warnings,
        };
    }

    private static object InsertComponent(JsonElement? args)
    {
        string path = RequiredStringArg(args, "path");
        string partPath = RequiredStringArg(args, "part_path");
        string? name = StringArg(args, "name");
        string? configuration = StringArg(args, "configuration") ?? string.Empty;
        bool save = BoolArg(args, "save", defaultValue: true);

        string resolvedPartPath = AssertAllowedPath(Path.GetFullPath(partPath));
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

    private static object CutActuatorCavity(JsonElement? args)
    {
        bool useSelection = BoolArg(args, "use_selection", defaultValue: false);
        string? assemblyPath = StringArg(args, "path");
        string? bracketPartPath = StringArg(args, "bracket_part_path");
        string? toolPartPath = StringArg(args, "tool_part_path");
        string? bracketComponent = StringArg(args, "bracket_component");
        string? toolComponent = StringArg(args, "tool_component");
        string? modelId = StringArg(args, "model");
        double clearanceM = DoubleArg(args, "clearance_mm", 0.5) / 1000.0;
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = !string.IsNullOrWhiteSpace(assemblyPath)
            ? OpenDocument(app, assemblyPath)
            : Try(() => app.ActiveDoc) as ModelDoc2
                ?? throw WorkerException.Validation(
                    "NO_ACTIVE_DOCUMENT",
                    "No active assembly and no path provided.",
                    new Dictionary<string, object?>());
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("cut_actuator_cavity requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2 bracketInAsm;
        Component2 toolInAsm;
        if (useSelection)
        {
            (bracketInAsm, toolInAsm, bracketPartPath) = ResolveActuatorCavityFromSelection(asmDoc, assembly, modelId);
            toolPartPath ??= Try(() => toolInAsm.GetPathName()) as string;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw WorkerException.Validation(
                    "PATH_REQUIRED",
                    "path is required unless use_selection is true with an active assembly.",
                    new Dictionary<string, object?>());
            }

            if (string.IsNullOrWhiteSpace(bracketComponent) || string.IsNullOrWhiteSpace(toolComponent))
            {
                throw WorkerException.Validation(
                    "TARGETS_REQUIRED",
                    "bracket_component and tool_component are required unless use_selection is true.",
                    new Dictionary<string, object?>(),
                    [
                        "Highlight bracket and actuator in SolidWorks and pass use_selection: true.",
                        "Or pass explicit bracket_component and tool_component names.",
                    ]);
            }

            bracketPartPath ??= "C:/code/marengo/hardware/cad/parts/marengo_shoulder_pitch_mount_bracket_right.SLDPRT";
            toolPartPath ??= "C:/code/marengo/hardware/cad/vendor/vendor_robstride_rs03_vendor.SLDPRT";

            bracketInAsm = FindComponent(assembly, null, bracketComponent)
                ?? throw new InvalidOperationException($"Bracket component not found: {bracketComponent}");
            toolInAsm = FindComponent(assembly, null, toolComponent)
                ?? throw new InvalidOperationException($"Tool component not found: {toolComponent}");
        }

        toolPartPath ??= Try(() => toolInAsm.GetPathName()) as string
            ?? throw new InvalidOperationException("Tool component has no part path.");

        object? inContextResult = CutActuatorCavityInContext(app, asmDoc, assembly, bracketInAsm, toolInAsm, clearanceM);
        if (inContextResult is not null)
        {
            bool saved = false;
            int errors = 0;
            int warnings = 0;
            if (save)
            {
                saved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                if (!saved || errors != 0)
                {
                    throw new InvalidOperationException($"Assembly save failed. errors={errors}, warnings={warnings}");
                }

                ModelDoc2 bracketDoc = OpenDocument(app, bracketPartPath);
                saved = bracketDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            }

            return new
            {
                mode = "edit_part_indent",
                document = DescribeDocument(asmDoc),
                useSelection,
                bracketComponent = Try(() => bracketInAsm.Name2),
                bracketPartPath,
                toolComponent = Try(() => toolInAsm.Name2),
                result = inContextResult,
                saved,
                errors,
                warnings,
            };
        }

        string resolvedAssemblyPath = assemblyPath
            ?? Try(() => asmDoc.GetPathName()) as string
            ?? throw WorkerException.Validation(
                "PATH_REQUIRED",
                "Assembly has no saved path. Save the document or pass path explicitly.",
                new Dictionary<string, object?>());

        return CutActuatorCavityByInsertPart(
            app,
            resolvedAssemblyPath,
            bracketPartPath,
            toolPartPath,
            bracketInAsm,
            toolInAsm,
            clearanceM,
            save);
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

    private static object? CutActuatorCavityInContext(
        ISldWorks app,
        ModelDoc2 asmDoc,
        IAssemblyDoc assembly,
        Component2 bracketInAsm,
        Component2 toolInAsm,
        double clearanceM)
    {
        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, bracketInAsm, append: false))
        {
            return null;
        }

        ModelDoc2? partDoc;
        try
        {
            int editInfo = 0;
            TryVoid(() => assembly.EditPart2(false, false, ref editInfo));
            partDoc = app.IActiveDoc2 as ModelDoc2;
        }
        catch
        {
            return null;
        }

        if (partDoc is null)
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        string? bracketPath = Try(() => bracketInAsm.GetPathName()) as string;
        string? activePath = Try(() => partDoc.GetPathName()) as string;
        if (!string.IsNullOrWhiteSpace(bracketPath)
            && !string.IsNullOrWhiteSpace(activePath)
            && !string.Equals(Path.GetFullPath(activePath), Path.GetFullPath(bracketPath), StringComparison.OrdinalIgnoreCase))
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, toolInAsm, append: false, mark: 1))
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        dynamic featMgr = partDoc.FeatureManager;
        Feature? indentFeature = Try(() => featMgr.InsertIndent(
            false,
            clearanceM,
            false,
            false,
            1)) as Feature;

        if (indentFeature is null)
        {
            indentFeature = Try(() => featMgr.InsertIndent(false, clearanceM, false, false)) as Feature;
        }

        if (indentFeature is null)
        {
            TryVoid(() => assembly.EditAssembly());
            return null;
        }

        TryVoid(() => indentFeature.Name = "Indent-Actuator-Cavity1");
        partDoc.EditRebuild3();
        TryVoid(() => assembly.EditAssembly());
        asmDoc.EditRebuild3();

        return new
        {
            feature = Try(() => indentFeature.Name),
            clearanceM,
            massAfter = Normalize(Try(() => partDoc.Extension.CreateMassProperty()?.Mass)),
            boundingBoxAfter = Normalize(Try(() => ((IPartDoc)partDoc).GetPartBox(true))),
        };
    }

    private static object CutActuatorCavityByInsertPart(
        ISldWorks app,
        string assemblyPath,
        string bracketPartPath,
        string toolPartPath,
        Component2 bracketInAsm,
        Component2 toolInAsm,
        double clearanceM,
        bool save)
    {
        double[] toolMatrix = ReadComponentTransformMatrix(toolInAsm);
        if (bracketInAsm is not null)
        {
            double[] bracketMatrix = NormalizeTransformMatrix(ReadComponentTransformMatrix(bracketInAsm));
            if (!IsNearIdentityTransform(bracketMatrix))
            {
                toolMatrix = MultiplyTransformMatrix(
                    InvertTransformMatrix(bracketMatrix),
                    NormalizeTransformMatrix(toolMatrix));
            }
        }
        else
        {
            toolMatrix = NormalizeTransformMatrix(toolMatrix);
        }

        string resolvedToolPartPath = Try(() => toolInAsm.GetPathName()) as string ?? toolPartPath;
        string? toolComponentName = Try(() => toolInAsm.Name2) as string;

        MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
        MathTransform? insertTransform = mathUtil is null
            ? null
            : Try(() => mathUtil.CreateTransform(toolMatrix)) as MathTransform;

        Body2? copiedToolBody = CopyToolBodyForBracketCut(app, toolInAsm, resolvedToolPartPath);

        CloseDocumentIfOpen(app, assemblyPath);
        CloseDocumentIfOpen(app, bracketPartPath);
        CloseDocumentIfOpen(app, resolvedToolPartPath);

        ModelDoc2 bracketDoc = OpenDocument(app, bracketPartPath);
        if (bracketDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("cut_actuator_cavity requires a bracket part document.");
        }

        Body2? targetBody = GetPrimarySolidBody(bracketDoc)
            ?? throw new InvalidOperationException("Bracket part has no solid body to cut.");

        FeatureManager featMgr = bracketDoc.FeatureManager;
        dynamic dynFeatMgr = featMgr;

        copiedToolBody ??= CopyPrimarySolidBody(app, resolvedToolPartPath);
        if (copiedToolBody is null)
        {
            throw new InvalidOperationException("Failed to copy actuator solid body from assembly or vendor part.");
        }

        Body2? toolBody = ImportBodyIntoPart(
                bracketDoc,
                copiedToolBody,
                targetBody,
                resolvedToolPartPath,
                insertTransform)
            ?? throw new InvalidOperationException(
                $"Failed to import copied actuator body into bracket part. toolPart={resolvedToolPartPath}");

        const int subtractOperation = 1;
        Feature? combineFeature = Try(() => dynFeatMgr.InsertCombineFeature(
            subtractOperation,
            2,
            new object[] { targetBody, toolBody })) as Feature;

        if (combineFeature is null)
        {
            throw new InvalidOperationException("InsertCombineFeature subtract returned null after importing tool body.");
        }

        TryVoid(() => combineFeature.Name = "Cut-Actuator-Cavity1");
        bracketDoc.EditRebuild3();

        bool partSaved = false;
        int partErrors = 0;
        int partWarnings = 0;
        if (save)
        {
            partSaved = bracketDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref partErrors, ref partWarnings);
            if (!partSaved || partErrors != 0)
            {
                throw new InvalidOperationException($"Bracket save failed. errors={partErrors}, warnings={partWarnings}");
            }
        }

        ModelDoc2 reopenedAsmDoc = OpenDocument(app, assemblyPath);
        if (save)
        {
            reopenedAsmDoc.EditRebuild3();
            int asmErrors = 0;
            int asmWarnings = 0;
            bool asmSaved = reopenedAsmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
            if (!asmSaved || asmErrors != 0)
            {
                throw new InvalidOperationException($"Assembly save failed. errors={asmErrors}, warnings={asmWarnings}");
            }
        }

        return new
        {
            mode = "copy_body_subtract",
            document = DescribeDocument(bracketDoc),
            assembly = DescribeDocument(reopenedAsmDoc),
            bracketPartPath,
            toolPartPath = resolvedToolPartPath,
            toolComponent = toolComponentName,
            combineFeature = Try(() => combineFeature.Name),
            clearanceM,
            massAfter = Normalize(Try(() => bracketDoc.Extension.CreateMassProperty()?.Mass)),
            boundingBoxAfter = Normalize(Try(() => ((IPartDoc)bracketDoc).GetPartBox(true))),
            saved = partSaved,
            partErrors,
            partWarnings,
        };
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