using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static object MateCoordSys(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string component1 = RequiredStringArg(args, "component_1");
        string ref1 = RequiredStringArg(args, "ref_1");
        string component2 = RequiredStringArg(args, "component_2");
        string ref2 = RequiredStringArg(args, "ref_2");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_coord_sys requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? first = FindComponent(assembly, null, component1)
            ?? throw new InvalidOperationException($"Component not found: {component1}");
        Component2? second = FindComponent(assembly, null, component2)
            ?? throw new InvalidOperationException($"Component not found: {component2}");

        doc.ClearSelection2(true);
        if (!SelectComponentReference(doc, first, ref1, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {ref1} on {component1}");
        }

        if (!SelectComponentReference(doc, second, ref2, append: true, mark: 2))
        {
            throw new InvalidOperationException($"Failed to select {ref2} on {component2}");
        }

        Mate2? mate = assembly.AddMate5(
            (int)swMateType_e.swMateCOORDINATE,
            (int)swMateAlign_e.swMateAlignALIGNED,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            0,
            out int mateError) as Mate2;

        if (mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"AddMate5 failed with error code {mateError}.");
        }

        if (mate is null && mateError == 0)
        {
            throw new InvalidOperationException("AddMate5 returned no mate.");
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component1 = Try(() => first.Name2),
            component2 = Try(() => second.Name2),
            ref1,
            ref2,
            mateError,
            mateCreated = mate is not null,
            alreadyConstrained = mateError == 4,
        };
    }

    private static SelectData? CreateSelectData(ModelDoc2 doc, int mark)
    {
        SelectionMgr? selectionMgr = Try(() => doc.SelectionManager) as SelectionMgr;
        if (selectionMgr is null)
        {
            return null;
        }

        SelectData? selectData = Try(() => selectionMgr.CreateSelectData()) as SelectData;
        if (selectData is null)
        {
            return null;
        }

        TryVoid(() => selectData.Mark = mark);
        return selectData;
    }

    private static bool SelectComponentReference(
        ModelDoc2 assemblyDoc,
        Component2 component,
        string referenceName,
        bool append,
        int mark)
    {
        ModelDoc2? componentDoc = Try(() => component.GetModelDoc2()) as ModelDoc2;
        if (componentDoc is null)
        {
            return false;
        }

        Feature? feature = FindFeatureByName(componentDoc, referenceName);
        if (feature is null)
        {
            return false;
        }

        string? componentName = Try(() => component.Name2) as string;
        if (componentName is null)
        {
            return false;
        }

        string? refType = Try(() => feature.GetTypeName2()) as string;
        if (refType == "RefPlane")
        {
            return SelectComponentPlaneFeature(assemblyDoc, component, feature, append, mark);
        }

        string selectionType = refType switch
        {
            "CoordSys" => "COORDSYS",
            "RefPlane" => "PLANE",
            "RefAxis" => "AXIS",
            _ => "COORDSYS",
        };

        string selectName = $"{referenceName}@{componentName}";
        bool selected = assemblyDoc.Extension.SelectByID2(
            selectName,
            selectionType,
            0,
            0,
            0,
            append,
            mark,
            null,
            0);

        if (selected)
        {
            return true;
        }

        return false;
    }

    private static bool SelectComponentPlaneFeature(
        ModelDoc2 assemblyDoc,
        Component2 component,
        Feature planeFeature,
        bool append,
        int mark)
    {
        string? componentName = Try(() => component.Name2) as string;
        string? planeName = Try(() => planeFeature.Name) as string;
        string? assemblyName = Try(() =>
        {
            string? pathName = assemblyDoc.GetPathName();
            return string.IsNullOrWhiteSpace(pathName)
                ? assemblyDoc.GetTitle()
                : Path.GetFileNameWithoutExtension(pathName);
        }) as string;

        if (componentName is not null && planeName is not null)
        {
            string[] selectIds =
            [
                $"{planeName}@{componentName}",
                string.IsNullOrWhiteSpace(assemblyName)
                    ? string.Empty
                    : $"{planeName}@{componentName}@{assemblyName}",
            ];
            foreach (string selectId in selectIds)
            {
                if (string.IsNullOrWhiteSpace(selectId))
                {
                    continue;
                }

                if (assemblyDoc.Extension.SelectByID2(
                        selectId,
                        "PLANE",
                        0,
                        0,
                        0,
                        append,
                        mark,
                        null,
                        0))
                {
                    return true;
                }
            }
        }

        // Component-scoped feature select (more reliable for standard planes than SelectByID2 alone).
        if (!string.IsNullOrWhiteSpace(planeName))
        {
            Feature? componentPlane = Try(() => component.FeatureByName(planeName)) as Feature;
            SelectData? selectData = CreateSelectData(assemblyDoc, mark);
            if (componentPlane is Entity planeEntity && selectData is not null
                && (Try(() => planeEntity.Select4(append, selectData)) as bool? ?? false))
            {
                return true;
            }

            if (componentPlane is not null
                && (Try(() => componentPlane.Select2(append, mark)) as bool? ?? false))
            {
                return true;
            }
        }

        object? facesObj = Try(() => planeFeature.GetFaces());
        if (facesObj is object[] planeFaces && planeFaces.Length > 0 && planeFaces[0] is Face2 planeFace)
        {
            SelectData? selectData = CreateSelectData(assemblyDoc, mark);
            Entity? entity = planeFace as Entity;
            if (entity is not null && selectData is not null
                && (Try(() => entity.Select4(append, selectData)) as bool? ?? false))
            {
                return true;
            }

            if (SelectFeatureFaceByRay(assemblyDoc, component, planeFace, append, mark))
            {
                return true;
            }
        }

        if (Try(() => planeFeature.GetSpecificFeature2()) is RefPlane refPlane)
        {
            MathTransform? planeTransform = Try(() => refPlane.Transform) as MathTransform;
            MathTransform? componentTransform = Try(() => component.Transform2) as MathTransform;
            if (planeTransform?.ArrayData is double[] planeMatrix && componentTransform is not null)
            {
                double[] normalized = NormalizeTransformMatrix(planeMatrix);
                double[] asmOrigin = TransformPointManual(
                    componentTransform,
                    normalized[9],
                    normalized[10],
                    normalized[11]);
                double[] asmNormal = TransformDirectionManual(
                    componentTransform,
                    normalized[6],
                    normalized[7],
                    normalized[8]);
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    if (assemblyDoc.Extension.SelectByRay(
                            asmOrigin[0] + (asmNormal[0] * 0.01 * sign),
                            asmOrigin[1] + (asmNormal[1] * 0.01 * sign),
                            asmOrigin[2] + (asmNormal[2] * 0.01 * sign),
                            asmNormal[0] * sign,
                            asmNormal[1] * sign,
                            asmNormal[2] * sign,
                            0.001,
                            mark,
                            append,
                            0,
                            0))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static double[] TransformDirectionManual(MathTransform transform, double x, double y, double z)
    {
        double[] matrix = Try(() => transform.ArrayData) as double[] ?? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        return
        [
            matrix[0] * x + matrix[1] * y + matrix[2] * z,
            matrix[3] * x + matrix[4] * y + matrix[5] * z,
            matrix[6] * x + matrix[7] * y + matrix[8] * z,
        ];
    }

    private static Feature? FindFeatureByName(ModelDoc2 doc, string name)
    {
        object? feature = Try(() => doc.FirstFeature());
        int guard = 0;
        while (feature is not null && guard++ < 1000)
        {
            dynamic current = feature;
            string? featureName = Try(() => current.Name) as string;
            if (featureName is not null && featureName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return feature as Feature;
            }

            // Mate features live under the Mates folder as subfeatures.
            Feature? nested = FindSubFeatureByName(current as Feature, name);
            if (nested is not null)
            {
                return nested;
            }

            feature = Try(() => current.GetNextFeature());
        }

        return null;
    }

    private static Feature? FindSubFeatureByName(Feature? parent, string name)
    {
        if (parent is null)
        {
            return null;
        }

        object? subFeature = Try(() => parent.GetFirstSubFeature());
        int guard = 0;
        while (subFeature is not null && guard++ < 500)
        {
            dynamic current = subFeature;
            string? featureName = Try(() => current.Name) as string;
            if (featureName is not null && featureName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return subFeature as Feature;
            }

            Feature? deeper = FindSubFeatureByName(subFeature as Feature, name);
            if (deeper is not null)
            {
                return deeper;
            }

            subFeature = Try(() => current.GetNextSubFeature());
        }

        return null;
    }

    private static object DeleteAllMates(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("delete_all_mates requires an assembly document.");
        }

        object result = DeleteAllMatesInternal(doc);
        return new
        {
            document = DescribeDocument(doc),
            deleted = result.GetType().GetProperty("deleted")?.GetValue(result),
            mateCountBefore = result.GetType().GetProperty("mateCountBefore")?.GetValue(result),
            mateCountAfter = result.GetType().GetProperty("mateCountAfter")?.GetValue(result),
        };
    }

    private static object UnfixAllComponents(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        string? exceptPrefix = StringArg(args, "except_prefix");

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("unfix_all_components requires an assembly document.");
        }

        object unfixResult = UnfixAllComponentsInternal((IAssemblyDoc)doc, exceptPrefix);
        return new
        {
            document = DescribeDocument(doc),
            unfixed = unfixResult.GetType().GetProperty("unfixed")?.GetValue(unfixResult),
            unfixedCount = unfixResult.GetType().GetProperty("unfixedCount")?.GetValue(unfixResult),
        };
    }

    private static object DeleteAllMatesInternal(ModelDoc2 doc)
    {
        var mateFeatures = CollectMateFeatures(doc);
        int deleted = 0;
        foreach (Feature mate in mateFeatures)
        {
            doc.ClearSelection2(true);
            bool selected = Try(() => mate.Select2(false, -1)) as bool? ?? false;
            if (!selected)
            {
                continue;
            }

            bool removed = Try(() => doc.Extension.DeleteSelection2(
                (int)swDeleteSelectionOptions_e.swDelete_Children)) as bool? ?? false;
            if (removed)
            {
                deleted++;
            }
        }

        doc.EditRebuild3();

        return new
        {
            deleted,
            mateCountBefore = mateFeatures.Count,
            mateCountAfter = CountAssemblyMates(doc),
        };
    }

    private static object UnfixAllComponentsInternal(IAssemblyDoc assembly, string? exceptPrefix)
    {
        ModelDoc2 doc = (ModelDoc2)assembly;
        var unfixed = new List<string>();
        object[]? roots = Try(() => assembly.GetComponents(true)) as object[];
        if (roots is not null)
        {
            foreach (object entry in roots)
            {
                if (entry is not Component2 component)
                {
                    continue;
                }

                string? name = Try(() => component.Name2) as string;
                if (name is not null
                    && !string.IsNullOrWhiteSpace(exceptPrefix)
                    && name.Contains(exceptPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Try(() => component.IsFixed()) as bool? != true)
                {
                    continue;
                }

                component.Select4(false, null, false);
                assembly.UnfixComponent();
                doc.ClearSelection2(true);
                if (name is not null)
                {
                    unfixed.Add(name);
                }
            }
        }

        doc.EditRebuild3();

        return new
        {
            unfixed,
            unfixedCount = unfixed.Count,
        };
    }

    private static List<Feature> CollectMateFeatures(ModelDoc2 doc)
    {
        var mateFeatures = new List<Feature>();
        Feature? mateGroup = FindFeatureByName(doc, "Mates");
        if (mateGroup is not null)
        {
            object? subFeature = Try(() => mateGroup.GetFirstSubFeature());
            int guard = 0;
            while (subFeature is not null && guard++ < 500)
            {
                if (subFeature is Feature feature)
                {
                    mateFeatures.Add(feature);
                }

                subFeature = Try(() => ((dynamic)subFeature).GetNextSubFeature());
            }
        }

        return mateFeatures;
    }

    private static object DeleteMatesInRange(JsonElement? args)
    {
        string? inputPath = StringArg(args, "path");
        int minNumber = (int)DoubleArg(args, "min_number", 0);
        int maxNumber = (int)DoubleArg(args, "max_number", 0);
        bool save = BoolArg(args, "save", defaultValue: true);

        ISldWorks app = AttachSolidWorks(startIfMissing: !string.IsNullOrWhiteSpace(inputPath));
        ModelDoc2? doc = string.IsNullOrWhiteSpace(inputPath) ? app.ActiveDoc as ModelDoc2 : OpenDocument(app, inputPath);
        if (doc is null)
        {
            throw new InvalidOperationException("No active SolidWorks document.");
        }

        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("delete_mates_in_range requires an assembly document.");
        }

        var deletedNames = new List<string>();
        foreach (Feature mate in CollectMateFeatures(doc))
        {
            string? name = Try(() => mate.Name) as string;
            if (name is null || !TryParseMateNumber(name, out int number))
            {
                continue;
            }

            if (number < minNumber || number > maxNumber)
            {
                continue;
            }

            doc.ClearSelection2(true);
            if (Try(() => mate.Select2(false, -1)) as bool? != true)
            {
                continue;
            }

            if (Try(() => doc.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Children)) as bool? == true)
            {
                deletedNames.Add(name);
            }
        }

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
            minNumber,
            maxNumber,
            deleted = deletedNames,
            deletedCount = deletedNames.Count,
            mateCountAfter = CountAssemblyMates(doc),
            saved,
            errors,
            warnings,
        };
    }

    private static bool TryParseMateNumber(string mateName, out int number)
    {
        number = 0;
        if (!mateName.StartsWith("Coincident", StringComparison.OrdinalIgnoreCase)
            && !mateName.StartsWith("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int prefixLength = mateName.StartsWith("Coincident", StringComparison.OrdinalIgnoreCase) ? 10 : 8;
        return int.TryParse(mateName[prefixLength..], out number);
    }

    private static object SetComponentConfiguration(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string configuration = RequiredStringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_configuration requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        string? previous = Try(() => component.ReferencedConfiguration) as string;
        component.ReferencedConfiguration = configuration;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            from = previous,
            to = Try(() => component.ReferencedConfiguration),
        };
    }

    private static object MateComponentOrigin(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        string reference = StringArg(args, "reference") ?? "urdf_link_frame";

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("mate_component_origin requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        doc.ClearSelection2(true);
        if (!SelectComponentReference(doc, component, reference, append: false, mark: 1))
        {
            throw new InvalidOperationException($"Failed to select {reference} on {componentName}");
        }

        if (!SelectAssemblyOrigin(doc, append: true, mark: 2))
        {
            throw new InvalidOperationException("Failed to select assembly origin.");
        }

        Mate2? mate = assembly.AddMate5(
            (int)swMateType_e.swMateCOORDINATE,
            (int)swMateAlign_e.swMateAlignALIGNED,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            0,
            out int mateError) as Mate2;

        if (mateError != 0 && mateError != 4)
        {
            throw new InvalidOperationException($"AddMate5 failed with error code {mateError}.");
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            reference,
            mateError,
            mateCreated = mate is not null,
            alreadyConstrained = mateError == 4,
        };
    }

    private static bool SelectAssemblyOrigin(ModelDoc2 doc, bool append, int mark)
    {
        (string id, string type)[] originCandidates =
        [
            (string.Empty, "EXTSKETCHPOINT"),
            ("Origin", "ORIGIN"),
            ("Origin", "POINT"),
            ("", "FACE"),
        ];

        foreach ((string id, string type) in originCandidates)
        {
            if (doc.Extension.SelectByID2(id, type, 0, 0, 0, append, mark, null, 0))
            {
                return true;
            }
        }

        Feature? originFeature = FindFeatureByName(doc, "Origin");
        if (originFeature is not null)
        {
            return Try(() => originFeature.Select2(append, mark)) as bool? ?? false;
        }

        return false;
    }

    private static object ListConfigurations(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("list_configurations requires a part document.");
        }

        ConfigurationManager? manager = Try(() => doc.ConfigurationManager) as ConfigurationManager;
        if (manager is null)
        {
            throw new InvalidOperationException("ConfigurationManager unavailable.");
        }

        var names = new List<string>();
        object? configNames = Try(() => doc.GetConfigurationNames());
        if (configNames is string[] stringNames)
        {
            names.AddRange(stringNames);
        }
        else if (configNames is object[] objectNames)
        {
            foreach (object entry in objectNames)
            {
                if (entry is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return new
        {
            document = DescribeDocument(doc),
            active = Try(() => manager.ActiveConfiguration.Name),
            configurations = names,
        };
    }

    private static object ListDimensions(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
            doc.EditRebuild3();
        }

        var dimensions = new List<object>();
        object? featureObj = Try(() => doc.FirstFeature());
        int guard = 0;
        while (featureObj is not null && guard++ < 1000)
        {
            if (featureObj is Feature feature)
            {
                string? featureName = Try(() => feature.Name) as string;
                string? featureType = Try(() => feature.GetTypeName2()) as string;
                object? displayDimensionObj = Try(() => feature.GetFirstDisplayDimension());
                int dimGuard = 0;
                while (displayDimensionObj is not null && dimGuard++ < 100)
                {
                    if (displayDimensionObj is DisplayDimension displayDimension)
                    {
                        Dimension? dimension = Try(() => displayDimension.GetDimension2(0)) as Dimension;
                        if (dimension is not null)
                        {
                            dimensions.Add(new
                            {
                                feature = featureName,
                                featureType,
                                name = Try(() => dimension.FullName),
                                shortName = Try(() => dimension.Name),
                                systemValue = Try(() => dimension.SystemValue),
                            });
                        }

                        displayDimensionObj = Try(() => feature.GetNextDisplayDimension(displayDimension));
                    }
                    else
                    {
                        break;
                    }
                }
            }

            featureObj = Try(() => ((dynamic)featureObj).GetNextFeature());
        }

        return new
        {
            document = DescribeDocument(doc),
            configuration = configuration ?? Try(() => doc.ConfigurationManager.ActiveConfiguration.Name),
            dimensions,
            count = dimensions.Count,
        };
    }

    private static object SetFeatureSuppression(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string featureName = RequiredStringArg(args, "feature_name");
        bool suppressed = BoolArg(args, "suppressed", defaultValue: true);
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
        }

        Feature? feature = FindFeatureByName(doc, featureName)
            ?? throw new InvalidOperationException($"Feature not found: {featureName}");

        string[] configs = string.IsNullOrWhiteSpace(configuration) ? [] : [configuration];
        bool ok = Try(() => feature.SetSuppression2(
            suppressed
                ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
            string.IsNullOrWhiteSpace(configuration)
                ? (int)swInConfigurationOpts_e.swAllConfiguration
                : (int)swInConfigurationOpts_e.swSpecifyConfiguration,
            configs)) as bool? ?? false;

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            feature = featureName,
            configuration = configuration ?? "all",
            suppressed,
            ok,
        };
    }

    private static object AddConfigurationCopy(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string fromConfig = RequiredStringArg(args, "from");
        string toConfig = RequiredStringArg(args, "to");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            throw new InvalidOperationException("add_configuration_copy requires a part document.");
        }

        ConfigurationManager? manager = Try(() => doc.ConfigurationManager) as ConfigurationManager;
        if (manager is null)
        {
            throw new InvalidOperationException("ConfigurationManager unavailable.");
        }

        Configuration? source = Try(() => doc.GetConfigurationByName(fromConfig)) as Configuration;
        if (source is null)
        {
            throw new InvalidOperationException($"Source configuration not found: {fromConfig}");
        }

        Configuration? existing = Try(() => doc.GetConfigurationByName(toConfig)) as Configuration;
        if (existing is not null)
        {
            return new
            {
                document = DescribeDocument(doc),
                from = fromConfig,
                to = toConfig,
                created = false,
                alreadyExists = true,
            };
        }

        object? newConfig = Try(() => manager.AddConfiguration(toConfig, "", "", 0, fromConfig, ""));
        if (newConfig is not Configuration)
        {
            throw new InvalidOperationException($"Failed to create configuration: {toConfig}");
        }

        doc.ShowConfiguration2(toConfig);
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            from = fromConfig,
            to = toConfig,
            created = true,
            alreadyExists = false,
        };
    }

    private static object GetComponentBox(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_component_box requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        object? box = Try(() => component.GetBox(false, false));
        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            boundingBox = Normalize(box),
        };
    }

    private static object TransformComponent(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        double tx = DoubleArg(args, "tx", 0);
        double ty = DoubleArg(args, "ty", 0);
        double tz = DoubleArg(args, "tz", 0);

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("transform_component requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        object? currentTransform = Try(() => component.Transform2) as object;
        if (currentTransform is not MathTransform mathTransform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        double[] data = Try(() => mathTransform.ArrayData) as double[] ?? throw new InvalidOperationException("Transform data unavailable.");
        if (data.Length < 16)
        {
            throw new InvalidOperationException("Unexpected transform matrix size.");
        }

        data[9] += tx;
        data[10] += ty;
        data[11] += tz;
        mathTransform.ArrayData = data;
        component.Transform2 = mathTransform;
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            translation = new[] { tx, ty, tz },
        };
    }

    private static object SetComponentTransform(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");
        double[] matrix = DoubleArrayArg(args, "matrix");
        bool fix = BoolArg(args, "fix", defaultValue: true);

        if (matrix.Length != 16)
        {
            throw new InvalidOperationException("matrix must contain 16 numbers.");
        }

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("set_component_transform requires an assembly document.");
        }

        IAssemblyDoc assembly = (IAssemblyDoc)doc;
        Component2? component = FindComponent(assembly, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        if (Try(() => component.IsFixed()) as bool? == true)
        {
            component.Select4(false, null, false);
            assembly.UnfixComponent();
            doc.ClearSelection2(true);
        }

        MathTransform? transform = Try(() => component.Transform2) as MathTransform;
        if (transform is null)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        ApplyComponentTransformMatrix(component, matrix);

        if (fix)
        {
            component.Select4(false, null, false);
            assembly.FixComponent();
            doc.ClearSelection2(true);
        }

        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            fixedState = Try(() => component.IsFixed()),
            matrix,
        };
    }

    private static object GetComponentTransform(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string componentName = RequiredStringArg(args, "component_name");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        {
            throw new InvalidOperationException("get_component_transform requires an assembly document.");
        }

        Component2? component = FindComponent((IAssemblyDoc)doc, null, componentName)
            ?? throw new InvalidOperationException($"Component not found: {componentName}");

        double[] matrix = ReadComponentTransformMatrix(component);
        object? box = Try(() => component.GetBox(false, false));

        return new
        {
            document = DescribeDocument(doc),
            component = Try(() => component.Name2),
            matrix,
            boundingBoxM = Normalize(box),
            isFixed = Try(() => component.IsFixed()),
        };
    }

    private static double[] ReadComponentTransformMatrix(Component2 component)
    {
        if (component.Transform2 is not MathTransform transform)
        {
            throw new InvalidOperationException("Component transform unavailable.");
        }

        double[] matrix = Try(() => transform.ArrayData) as double[]
            ?? throw new InvalidOperationException("Transform data unavailable.");

        return NormalizeTransformMatrix(matrix);
    }

    private static double[] NormalizeTransformMatrix(double[] matrix)
    {
        if (matrix.Length == 16)
        {
            return matrix;
        }

        if (matrix.Length == 12)
        {
            return
            [
                matrix[0], matrix[1], matrix[2], 0,
                matrix[3], matrix[4], matrix[5], 0,
                matrix[6], matrix[7], matrix[8], 0,
                matrix[9], matrix[10], matrix[11], 1,
            ];
        }

        throw new InvalidOperationException($"Unexpected transform matrix size: {matrix.Length}.");
    }

    private static bool IsNearIdentityTransform(double[] matrix)
    {
        return Math.Abs(matrix[0] - 1.0) < 1e-9
            && Math.Abs(matrix[1]) < 1e-9
            && Math.Abs(matrix[2]) < 1e-9
            && Math.Abs(matrix[3]) < 1e-9
            && Math.Abs(matrix[4] - 1.0) < 1e-9
            && Math.Abs(matrix[5]) < 1e-9
            && Math.Abs(matrix[6]) < 1e-9
            && Math.Abs(matrix[7]) < 1e-9
            && Math.Abs(matrix[8] - 1.0) < 1e-9
            && Math.Abs(matrix[9]) < 1e-9
            && Math.Abs(matrix[10]) < 1e-9
            && Math.Abs(matrix[11]) < 1e-9;
    }

    private static object SetDimension(JsonElement? args)
    {
        string inputPath = RequiredStringArg(args, "path");
        string dimensionName = RequiredStringArg(args, "dimension");
        double valueMeters = DoubleArg(args, "value_meters", 0);
        string? configuration = StringArg(args, "configuration");

        ISldWorks app = AttachSolidWorks(startIfMissing: true);
        ModelDoc2 doc = OpenDocument(app, inputPath);
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            doc.ShowConfiguration2(configuration);
        }

        Dimension? dimension = Try(() => doc.Parameter(dimensionName)) as Dimension;
        if (dimension is null)
        {
            throw new InvalidOperationException($"Dimension not found: {dimensionName}");
        }

        double? previous = Try(() => dimension.SystemValue) as double?;
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            string[] configurations = [configuration];
            Try(() => dimension.SetSystemValue3(
                valueMeters,
                (int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations,
                configurations));
        }
        else
        {
            dimension.SystemValue = valueMeters;
        }
        doc.EditRebuild3();

        return new
        {
            document = DescribeDocument(doc),
            configuration = configuration ?? Try(() => doc.ConfigurationManager.ActiveConfiguration.Name),
            dimension = dimensionName,
            from = previous,
            to = Try(() => dimension.SystemValue),
        };
    }

}