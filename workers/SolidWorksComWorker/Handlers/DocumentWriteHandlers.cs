using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object SaveDocument(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        // Assemblies validate mate health via force rebuild unless explicitly skipped.
        bool skipMateValidation = BoolArg(args, "skip_mate_validation", defaultValue: false);
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document to save.");
        }

        bool isAssembly = doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
        List<object> preSaveMateFailures = [];
        List<object> postSaveMateFailures = [];
        bool? preForceOk = null;
        bool? postForceOk = null;

        if (isAssembly && !skipMateValidation)
        {
            preForceOk = ForceRebuildDocument(doc, out bool forceOk);
            _ = forceOk;
            List<MateHealthEntry> preMates = CollectMateHealthEntries(doc);
            preSaveMateFailures = MateFailuresFrom(preMates);
            if (preSaveMateFailures.Count > 0)
            {
                return new
                {
                    document = DescribeDocument(doc),
                    ok = false,
                    saved = false,
                    errors = 0,
                    warnings = 0,
                    mateValidation = true,
                    preForceOk,
                    preSaveMateFailures,
                    postSaveMateFailures,
                    remediation = new[]
                    {
                        "Force rebuild exposed unsolved mates before save.",
                        "Heal mates (or restore a checkpoint), then retry save_document / confirm_and_save.",
                        "Do not treat this as locked-in. skip_mate_validation is recovery-only.",
                    },
                };
            }
        }

        int errors = 0;
        int warnings = 0;
        bool saved = doc.Save3(
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ref errors,
            ref warnings);

        if (!saved || errors != 0)
        {
            throw new InvalidOperationException($"Save failed. errors={errors}, warnings={warnings}");
        }

        if (isAssembly && !skipMateValidation)
        {
            postForceOk = ForceRebuildDocument(doc, out bool forceOk);
            _ = forceOk;
            List<MateHealthEntry> postMates = CollectMateHealthEntries(doc);
            postSaveMateFailures = MateFailuresFrom(postMates);
            if (postSaveMateFailures.Count > 0)
            {
                return new
                {
                    document = DescribeDocument(doc),
                    ok = false,
                    saved = true,
                    errors,
                    warnings,
                    mateValidation = true,
                    preForceOk,
                    postForceOk,
                    preSaveMateFailures,
                    postSaveMateFailures,
                    remediation = new[]
                    {
                        "Save wrote the file, but force rebuild after save exposed unsolved mates.",
                        "Restore from the latest checkpoint, heal mates, then confirm_and_save.",
                    },
                };
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            ok = true,
            saved = true,
            errors,
            warnings,
            mateValidation = isAssembly && !skipMateValidation,
            preForceOk,
            postForceOk,
            preSaveMateFailures,
            postSaveMateFailures,
        };
    }

    private static object ConfirmAndSave(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        bool looksGood = BoolArg(args, "looks_good", defaultValue: false);
        bool confirm = BoolArg(args, "confirm", defaultValue: false);
        bool reopen = BoolArg(args, "reopen", defaultValue: true);
        string? previewPath = StringArg(args, "preview_path");
        double poseTolerance = DoubleArg(args, "pose_tolerance", 1e-6);

        if (!looksGood || !confirm)
        {
            throw new InvalidOperationException(
                "confirm_and_save requires looks_good: true and confirm: true after explicit user approval.");
        }

        string allowedPath = PathGuard.AssertAllowedPath(inputPath);
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, allowedPath);
        bool isAssembly = doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;

        object checkpoint = CreateDocumentCheckpoint(allowedPath, reason: "confirm_and_save", force: true);

        Dictionary<string, double[]>? preTransforms = null;
        if (isAssembly)
        {
            preTransforms = CaptureComponentTransforms((IAssemblyDoc)doc);
        }

        bool preForceOk = ForceRebuildDocument(doc, out _);
        List<MateHealthEntry> preMates = isAssembly ? CollectMateHealthEntries(doc) : [];
        List<object> preMateFailures = MateFailuresFrom(preMates);
        if (preMateFailures.Count > 0)
        {
            return new
            {
                document = DescribeDocument(doc),
                ok = false,
                saved = false,
                poseStable = false,
                looksGood,
                previewPath,
                reopen = false,
                preForceOk,
                preMateFailures,
                postSaveMateFailures = Array.Empty<object>(),
                postReopenMateFailures = Array.Empty<object>(),
                jumpedComponents = Array.Empty<object>(),
                checkpoint,
                remediation = new[]
                {
                    "Force rebuild before save exposed unsolved mates (What's Wrong class failures).",
                    "Do not declare locked-in. Heal or restore_from_checkpoint, then retry confirm_and_save.",
                },
            };
        }

        int saveErrors = 0;
        int saveWarnings = 0;
        bool saved = doc.Save3(
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            ref saveErrors,
            ref saveWarnings);
        if (!saved || saveErrors != 0)
        {
            return new
            {
                document = DescribeDocument(doc),
                ok = false,
                saved = false,
                poseStable = false,
                looksGood,
                previewPath,
                reopen = false,
                preForceOk,
                saveErrors,
                saveWarnings,
                saveErrorMeaning = DecodeSaveErrors(saveErrors),
                preMateFailures,
                postSaveMateFailures = Array.Empty<object>(),
                postReopenMateFailures = Array.Empty<object>(),
                jumpedComponents = Array.Empty<object>(),
                checkpoint,
                remediation = new[]
                {
                    $"Save3 failed. errors={saveErrors} warnings={saveWarnings}.",
                },
            };
        }

        bool postSaveForceOk = ForceRebuildDocument(doc, out _);
        List<MateHealthEntry> postSaveMates = isAssembly ? CollectMateHealthEntries(doc) : [];
        List<object> postSaveMateFailures = MateFailuresFrom(postSaveMates);
        if (postSaveMateFailures.Count > 0)
        {
            return new
            {
                document = DescribeDocument(doc),
                ok = false,
                saved = true,
                poseStable = false,
                looksGood,
                previewPath,
                reopen = false,
                preForceOk,
                postSaveForceOk,
                saveErrors,
                saveWarnings,
                preMateFailures,
                postSaveMateFailures,
                postReopenMateFailures = Array.Empty<object>(),
                jumpedComponents = Array.Empty<object>(),
                checkpoint,
                remediation = new[]
                {
                    "Save succeeded, but force rebuild after save exposed unsolved mates.",
                    "Restore from checkpoint, heal, ask the user again, then retry confirm_and_save.",
                },
            };
        }

        bool didReopen = false;
        bool postReopenForceOk = true;
        List<object> postReopenMateFailures = [];
        List<object> jumpedComponents = [];
        bool poseStable = true;

        if (reopen && isAssembly)
        {
            string? title = Try(() => doc.GetTitle()) as string;
            if (!string.IsNullOrWhiteSpace(title))
            {
                TryVoid(() => app.CloseDoc(title));
            }

            doc = OpenDocument(app, allowedPath);
            didReopen = true;
            postReopenForceOk = ForceRebuildDocument(doc, out _);
            List<MateHealthEntry> postReopenMates = CollectMateHealthEntries(doc);
            postReopenMateFailures = MateFailuresFrom(postReopenMates);

            Dictionary<string, double[]> afterTransforms = CaptureComponentTransforms((IAssemblyDoc)doc);
            if (preTransforms is not null)
            {
                jumpedComponents = DiffComponentTransforms(preTransforms, afterTransforms, poseTolerance);
            }

            poseStable = jumpedComponents.Count == 0 && postReopenMateFailures.Count == 0;
        }

        bool ok = saved
            && preMateFailures.Count == 0
            && postSaveMateFailures.Count == 0
            && postReopenMateFailures.Count == 0
            && poseStable;

        return new
        {
            document = DescribeDocument(doc),
            ok,
            saved = true,
            poseStable,
            looksGood,
            previewPath,
            reopen = didReopen,
            preForceOk,
            postSaveForceOk,
            postReopenForceOk,
            saveErrors,
            saveWarnings,
            preMateFailures,
            postSaveMateFailures,
            postReopenMateFailures,
            jumpedComponents,
            checkpoint,
            remediation = ok
                ? Array.Empty<string>()
                : new[]
                {
                    "confirm_and_save gate failed after reopen/pose check.",
                    "Restore from checkpoint if needed, heal, and retry only after user re-approval.",
                },
        };
    }

    private static object CloseAllDocuments(JsonElement? args)
    {
        bool saveFirst = BoolArg(args, "save_first", defaultValue: false);
        ISldWorks app = AttachSolidWorks(startIfMissing: false);

        var closed = new List<object>();
        object? docsObj = Try(() => app.GetDocuments()) as object;
        if (docsObj is object[] docs)
        {
            foreach (object entry in docs.ToArray())
            {
                if (entry is not ModelDoc2 doc)
                {
                    continue;
                }

                string? title = Try(() => doc.GetTitle()) as string;
                string? path = Try(() => doc.GetPathName()) as string;
                if (saveFirst && !string.IsNullOrWhiteSpace(path))
                {
                    int saveErrors = 0;
                    int saveWarnings = 0;
                    TryVoid(() => doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings));
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    TryVoid(() => app.CloseDoc(title));
                }

                closed.Add(new { title, path });
            }
        }

        return new
        {
            closedCount = closed.Count,
            closed,
        };
    }

    private static object DiagnosePartSave(JsonElement? args)
    {
        string path = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("diagnose_part_save requires a part document.");
        }

        var featureFindings = new List<object>();
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 500)
        {
            dynamic current = feature;
            string? typeName = Try(() => (string)current.GetTypeName2()) as string;
            if (string.Equals(typeName, "ICE", StringComparison.OrdinalIgnoreCase))
            {
                featureFindings.Add(new
                {
                    name = Try(() => (string)current.Name),
                    type = typeName,
                    note = "in_context_feature",
                });
            }

            feature = Try(() => current.GetNextFeature());
        }

        var externalRefs = new List<object>();
        bool listedRefs = false;
        try
        {
            dynamic extension = doc.Extension;
            object? refsObj = extension.ListExternalFileReferences();
            if (refsObj is object[] refsArray)
            {
                listedRefs = true;
                foreach (object entry in refsArray)
                {
                    externalRefs.Add(new { entry });
                }
            }
        }
        catch
        {
            // External reference listing varies by SolidWorks version.
        }

        bool rebuildOk = Try(() => doc.ForceRebuild3(false)) as bool? ?? false;
        doc.EditRebuild3();

        int saveErrors = 0;
        int saveWarnings = 0;
        bool saveOk = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings);

        return new
        {
            document = DescribeDocument(doc),
            readOnly = Try(() => doc.IsOpenedReadOnly()),
            hasExternalRefs = Try(() => ((dynamic)doc.Extension).HasExternalReferences()),
            rebuildOk,
            saveOk,
            saveErrors,
            saveWarnings,
            saveErrorMeaning = DecodeSaveErrors(saveErrors),
            externalRefCount = externalRefs.Count,
            externalRefs,
            failedFeatureCount = featureFindings.Count,
            inContextFeatures = featureFindings,
        };
    }

    private static string DecodeSaveErrors(int errors) => errors switch
    {
        0 => "none",
        1 => "generic",
        2 => "read_only",
        3 => "need_rebuild",
        4 => "need_rebuild_blocking",
        5 => "file_not_found",
        6 => "file_with_same_title_open",
        7 => "custom_property_error",
        _ => $"unknown_{errors}",
    };

    private static object CloneSolidBodyPart(JsonElement? args)
    {
        string sourcePath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "source_part_path"));
        string outputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_part_path"));
        string? assemblyPath = StringArg(args, "assembly_path");
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            assemblyPath = PathGuard.AssertAllowedPath(assemblyPath);
        }
        string? componentName = StringArg(args, "component_name");
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        CloseDocumentIfOpen(app, outputPath);
        CloseDocumentIfOpen(app, sourcePath);
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            CloseDocumentIfOpen(app, assemblyPath);
        }

        double[]? componentTransform = null;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(componentName))
        {
            ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
            Component2? component = FindComponent((IAssemblyDoc)asmDoc, null, componentName);
            if (component is not null)
            {
                componentTransform = NormalizeTransformMatrix(ReadComponentTransformMatrix(component));
            }
        }

        Body2? copiedBody = CopyPrimarySolidBody(app, sourcePath)
            ?? throw new InvalidOperationException($"No solid body found in source part: {sourcePath}");

        string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string ?? string.Empty;
        }

        ModelDoc2? newDoc = null;
        if (!string.IsNullOrWhiteSpace(template) && File.Exists(template))
        {
            newDoc = Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2;
        }

        newDoc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
        if (newDoc is null)
        {
            throw new InvalidOperationException("Failed to create a blank part for solid-body clone.");
        }

        Feature? imported = Try(() => ((dynamic)newDoc).CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
        if (imported is null)
        {
            dynamic part = (IPartDoc)newDoc;
            object[] bodyArray = [copiedBody];
            bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, null, 0)) as bool? == true
                || Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
            if (!inserted)
            {
                throw new InvalidOperationException("CreateFeatureFromBody3 and InsertBodies both failed.");
            }
        }
        else
        {
            TryVoid(() => imported.Name = "Imported-Solid-Body1");
        }
        newDoc.EditRebuild3();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        int saveErrors = 0;
        int saveWarnings = 0;
        bool saved = newDoc.Extension.SaveAs(
            outputPath,
            0,
            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
            null,
            ref saveErrors,
            ref saveWarnings);
        if (!saved || saveErrors != 0)
        {
            throw new InvalidOperationException(
                $"Clone save failed. errors={saveErrors} ({DecodeSaveErrors(saveErrors)}), warnings={saveWarnings}");
        }

        CloseDocumentIfOpen(app, Path.GetFullPath(outputPath));

        object? replaceResult = null;
        object? saveAssemblyResult = null;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(componentName))
        {
            replaceResult = ReplaceComponentsByPathInternal(
                app,
                assemblyPath,
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(outputPath),
                "Default",
                saveAssembly: false);

            if (componentTransform is not null)
            {
                ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
                Component2? component = FindComponent((IAssemblyDoc)asmDoc, null, componentName);
                if (component is not null)
                {
                    MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
                    MathTransform? transform = mathUtil is null
                        ? null
                        : Try(() => mathUtil.CreateTransform(componentTransform)) as MathTransform;
                    if (transform is not null)
                    {
                        TryVoid(() => component.Transform2 = transform);
                    }
                }
            }

            if (save)
            {
                ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
                int asmErrors = 0;
                int asmWarnings = 0;
                bool asmSaved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
                saveAssemblyResult = new { asmSaved, asmErrors, asmWarnings };
            }
        }

        // Verify clone saves cleanly.
        CloseDocumentIfOpen(app, outputPath);
        ModelDoc2 verifyDoc = OpenDocument(app, outputPath);
        int verifyErrors = 0;
        int verifyWarnings = 0;
        bool verifySaved = verifyDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref verifyErrors, ref verifyWarnings);

        return new
        {
            sourcePath,
            outputPath,
            assemblyPath,
            componentName,
            importedFeature = imported is not null ? Try(() => imported.Name) : "InsertBodies",
            saved,
            saveErrors,
            verifySaved,
            verifyErrors,
            replaceResult,
            saveAssemblyResult,
        };
    }

    private static object MirrorPartFile(JsonElement? args)
    {
        string sourcePath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "source_part_path"));
        string outputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "output_part_path"));
        string mirrorPlane = StringArg(args, "mirror_plane") ?? "Right Plane";
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        CloseDocumentIfOpen(app, outputPath);
        CloseDocumentIfOpen(app, sourcePath);

        ModelDoc2 sourceDoc = OpenDocument(app, sourcePath);
        if (sourceDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("mirror_part_file requires a part document.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        string mode = "insert_mirror_part";
        bool mirrored = false;
        dynamic extension = sourceDoc.Extension;
        mirrored = Try(() => (bool)extension.InsertMirrorPart2(outputPath, true, true, 0)) as bool? ?? false;
        if (!mirrored)
        {
            mirrored = Try(() => (bool)extension.InsertMirrorPart(outputPath, true, true)) as bool? ?? false;
        }

        if (!mirrored)
        {
            mode = "copy_body_mirror_transform";
            Body2? sourceBody = GetPrimarySolidBody(sourceDoc)
                ?? throw new InvalidOperationException("Source part has no solid body to mirror.");

            Body2? copiedBody = Try(() => sourceBody.Copy()) as Body2
                ?? throw new InvalidOperationException("Failed to copy source solid body.");

            MathUtility? mathUtil = Try(() => app.GetMathUtility()) as MathUtility;
            MathTransform? mirrorTransform = mathUtil is null
                ? null
                : Try(() => mathUtil.CreateTransform(MirrorXMatrix())) as MathTransform;
            if (mirrorTransform is not null)
            {
                TryVoid(() => copiedBody.ApplyTransform(mirrorTransform));
            }

            string template = Try(() => app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)) as string
                ?? string.Empty;
            ModelDoc2? newDoc = !string.IsNullOrWhiteSpace(template) && File.Exists(template)
                ? Try(() => app.NewDocument(template, 0, 0, 0)) as ModelDoc2
                : null;
            newDoc ??= Try(() => app.NewDocument("", (int)swDocumentTypes_e.swDocPART, 0, 0)) as ModelDoc2;
            if (newDoc is null)
            {
                throw new InvalidOperationException("Failed to create blank part for mirrored body.");
            }

            Feature? imported = Try(() => ((dynamic)newDoc).CreateFeatureFromBody3(copiedBody, false, 0)) as Feature;
            if (imported is null)
            {
                dynamic part = (IPartDoc)newDoc;
                object[] bodyArray = [copiedBody];
                bool inserted = Try(() => (bool)part.InsertBodies2(bodyArray, null, 0)) as bool? == true
                    || Try(() => (bool)part.InsertBodies(bodyArray, null)) as bool? == true;
                if (!inserted)
                {
                    throw new InvalidOperationException("Failed to import mirrored body into new part.");
                }
            }
            else
            {
                TryVoid(() => imported.Name = "Mirror-Left-Body1");
            }

            newDoc.EditRebuild3();
            int saveErrors = 0;
            int saveWarnings = 0;
            mirrored = newDoc.Extension.SaveAs(
                outputPath,
                0,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);
            if (!mirrored || saveErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Mirrored body save failed. errors={saveErrors}, warnings={saveWarnings}");
            }

            CloseDocumentIfOpen(app, Path.GetFullPath(outputPath));
        }
        else
        {
            CloseDocumentIfOpen(app, outputPath);
        }

        if (!mirrored)
        {
            mode = "mirror_feature";
            Body2? sourceBody = GetPrimarySolidBody(sourceDoc)
                ?? throw new InvalidOperationException("Source part has no solid body to mirror.");

            sourceDoc.ClearSelection2(true);
            if (!sourceDoc.Extension.SelectByID2(mirrorPlane, "PLANE", 0, 0, 0, false, 1, null, 0))
            {
                throw new InvalidOperationException($"Could not select mirror plane: {mirrorPlane}");
            }

            Entity? bodyEntity = sourceBody as Entity;
            if (bodyEntity is null || !bodyEntity.Select4(false, null))
            {
                throw new InvalidOperationException("Could not select source solid body for mirror.");
            }

            dynamic featMgr = sourceDoc.FeatureManager;
            Feature? mirrorFeature = Try(() => featMgr.InsertMirrorFeature2(
                true,
                false,
                false,
                false,
                1,
                1)) as Feature;
            if (mirrorFeature is null)
            {
                throw new InvalidOperationException("InsertMirrorFeature2 returned null.");
            }

            TryVoid(() => mirrorFeature.Name = "Mirror-Left1");
            sourceDoc.EditRebuild3();

            int saveErrors = 0;
            int saveWarnings = 0;
            mirrored = sourceDoc.Extension.SaveAs(
                outputPath,
                0,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref saveErrors,
                ref saveWarnings);
            if (!mirrored || saveErrors != 0)
            {
                throw new InvalidOperationException(
                    $"Mirror feature save failed. errors={saveErrors}, warnings={saveWarnings}");
            }

            CloseDocumentIfOpen(app, outputPath);
        }

        ModelDoc2? mirroredDoc = File.Exists(outputPath) ? OpenDocument(app, outputPath) : null;
        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (mirroredDoc is not null && save)
        {
            saved = mirroredDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            sourcePath,
            outputPath,
            mode,
            mirrored,
            saved,
            errors,
            warnings,
            document = mirroredDoc is not null ? DescribeDocument(mirroredDoc) : null,
        };
    }

    private static object MakeComponentIndependent(JsonElement? args)
    {
        string assemblyPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "assembly_path"));
        string componentName = RequiredStringArg(args, "component_name");
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 asmDoc = OpenDocument(app, assemblyPath);
        if (asmDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("make_component_independent requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)asmDoc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? partPath = Try(() => component.GetPathName()) as string;
        asmDoc.ClearSelection2(true);
        if (!SelectAssemblyComponent(asmDoc, component, append: false))
        {
            throw new InvalidOperationException($"Could not select component: {componentName}");
        }

        bool madeIndependent = false;
        int option = 1;
        dynamic dynComponent = component;
        madeIndependent = Try(() => (bool)dynComponent.MakeIndependent(option)) as bool? ?? false;
        if (!madeIndependent)
        {
            madeIndependent = Try(() => (bool)dynComponent.MakeIndependent(0)) as bool? ?? false;
        }

        asmDoc.EditRebuild3();

        bool asmSaved = false;
        int asmErrors = 0;
        int asmWarnings = 0;
        if (save)
        {
            asmSaved = asmDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref asmErrors, ref asmWarnings);
        }

        bool partSaved = false;
        int partErrors = 0;
        int partWarnings = 0;
        bool partRebuildOk = false;
        if (!string.IsNullOrWhiteSpace(partPath) && File.Exists(partPath))
        {
            CloseDocumentIfOpen(app, partPath);
            ModelDoc2 partDoc = OpenDocument(app, partPath);
            partRebuildOk = Try(() => partDoc.ForceRebuild3(false)) as bool? ?? false;
            partDoc.EditRebuild3();
            partSaved = partDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref partErrors, ref partWarnings);
        }

        return new
        {
            assembly = DescribeDocument(asmDoc),
            component = Try(() => component.Name2),
            partPath,
            madeIndependent,
            asmSaved,
            asmErrors,
            asmWarnings,
            partSaved,
            partErrors,
            partWarnings,
            partRebuildOk,
        };
    }

    private static object SetCustomProperties(JsonElement? args)
    {
        string path = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        bool save = BoolArg(args, "save", defaultValue: true);
        IReadOnlyDictionary<string, string> properties = PropertiesArg(args);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CAD document does not exist.", path);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, path);
        CustomPropertyManager? manager = Try(() => doc.Extension.CustomPropertyManager[""]) as CustomPropertyManager;
        if (manager is null)
        {
            throw new InvalidOperationException("CustomPropertyManager is unavailable on this document.");
        }

        var applied = new List<object>();
        foreach (KeyValuePair<string, string> entry in properties)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            string value = entry.Value ?? "";
            TryVoid(() =>
            {
                manager.Add3(
                    entry.Key,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    value,
                    1);
            });

            applied.Add(new { name = entry.Key, value });
        }

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after setting properties. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            applied,
            saved,
            errors,
            warnings,
            customProperties = ListCustomProperties(doc),
        };
    }

    private static object ReplaceComponentsByPath(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string fromPartPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "from_part_path"));
        string toPartPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "to_part_path"));
        string configName = StringArg(args, "configuration") ?? "Default";
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        return ReplaceComponentsByPathInternal(app, inputPath, fromPartPath, toPartPath, configName, save);
    }

    private static object ReplaceComponentPath(JsonElement? args)
    {
        string inputPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "path"));
        string componentName = RequiredStringArg(args, "component_name");
        string toPartPath = PathGuard.AssertAllowedPath(RequiredStringArg(args, "to_part_path"));
        string configName = StringArg(args, "configuration") ?? "Default";
        bool save = BoolArg(args, "save", defaultValue: true);

        if (!File.Exists(toPartPath))
        {
            throw new FileNotFoundException("Replacement part does not exist.", toPartPath);
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("replace_component_path requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? previousPath = Try(() => component.GetPathName()) as string;
        string? previousName = Try(() => component.Name2) as string;
        doc.ClearSelection2(true);
        if (!SelectAssemblyComponent(doc, component, append: false))
        {
            throw new InvalidOperationException($"Could not select component: {componentName}");
        }

        bool ok = false;
        TryVoid(() =>
        {
            ok = assembly.ReplaceComponents2(
                toPartPath,
                configName,
                true,
                0,
                true);
        });

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (save)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        }

        return new
        {
            document = DescribeDocument(doc),
            component = previousName,
            fromPath = previousPath,
            toPath = toPartPath,
            ok,
            saved,
            errors,
            warnings,
        };
    }

    private static object ReplaceComponentsByPathInternal(
        ISldWorks app,
        string inputPath,
        string fromPartPath,
        string toPartPath,
        string configName,
        bool saveAssembly)
    {
        if (!File.Exists(toPartPath))
        {
            throw new FileNotFoundException("Replacement part does not exist.", toPartPath);
        }

        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("replace_components_by_path requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        var replaced = new List<object>();

        foreach (Component2 component in EnumerateAllComponents(assembly))
        {
            string? componentPath = Try(() => component.GetPathName()) as string;
            if (string.IsNullOrWhiteSpace(componentPath))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(componentPath);
            if (!fullPath.Equals(fromPartPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? previousName = Try(() => component.Name2) as string;
            doc.ClearSelection2(true);
            bool selected = Try(() => component.Select4(false, null, false)) as bool? ?? false;
            if (!selected)
            {
                throw new InvalidOperationException($"Could not select component for replace: {previousName}");
            }

            bool ok = false;
            TryVoid(() =>
            {
                ok = assembly.ReplaceComponents2(
                    toPartPath,
                    configName,
                    true,
                    0,
                    true);
            });

            replaced.Add(new
            {
                name = previousName,
                fromPath = componentPath,
                toPath = toPartPath,
                ok,
            });
        }

        if (replaced.Count == 0)
        {
            throw new InvalidOperationException(
                $"No components reference part path: {fromPartPath}");
        }

        doc.ClearSelection2(true);
        doc.EditRebuild3();

        bool saved = false;
        int errors = 0;
        int warnings = 0;
        if (saveAssembly)
        {
            saved = doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            if (!saved || errors != 0)
            {
                throw new InvalidOperationException($"Save failed after replace. errors={errors}, warnings={warnings}");
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            replacedCount = replaced.Count,
            replaced,
            saved,
            errors,
            warnings,
        };
    }

}
