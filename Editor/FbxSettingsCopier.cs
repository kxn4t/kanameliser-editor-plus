using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// FBX import settings copy and paste functionality for the Project window.
    /// Copies Model tab settings, basic Rig settings, and Materials tab settings
    /// (including name-matched material remaps) between model assets.
    /// Animation clip definitions and Humanoid bone mappings are intentionally
    /// excluded because they are specific to each source file.
    /// </summary>
    internal class FbxSettingsCopier
    {
        [Serializable]
        private class MaterialRemap
        {
            public string name;
            public Material material;

            public MaterialRemap(string remapName, Material remapMaterial)
            {
                name = remapName;
                material = remapMaterial;
            }
        }

        private class FbxSettingsData
        {
            public string sourceAssetName;

            // Model tab
            public float globalScale;
            public bool useFileScale;
            public bool useFileUnits;
            public bool bakeAxisConversion;
            public bool importBlendShapes;
            public bool importVisibility;
            public bool importCameras;
            public bool importLights;
            public bool preserveHierarchy;
            public bool sortHierarchyByName;
            public ModelImporterMeshCompression meshCompression;
            public bool isReadable;
            public MeshOptimizationFlags meshOptimizationFlags;
            public bool addCollider;
            public bool keepQuads;
            public bool weldVertices;
            public ModelImporterIndexFormat indexFormat;
            public bool legacyBlendShapeNormals;
            public ModelImporterNormals importNormals;
            public ModelImporterNormals importBlendShapeNormals;
            public ModelImporterNormalCalculationMode normalCalculationMode;
            public ModelImporterNormalSmoothingSource normalSmoothingSource;
            public float normalSmoothingAngle;
            public ModelImporterTangents importTangents;
            public bool swapUVChannels;
            public bool generateSecondaryUV;
            public float secondaryUVAngleDistortion;
            public float secondaryUVAreaDistortion;
            public float secondaryUVHardAngle;
            public float secondaryUVPackMargin;
            public ModelImporterSecondaryUVMarginMethod secondaryUVMarginMethod;
            public float secondaryUVMinLightmapResolution;
            public float secondaryUVMinObjectScale;

            // Rig tab (basic settings only; humanDescription is excluded)
            public ModelImporterAnimationType animationType;
            public ModelImporterAvatarSetup avatarSetup;
            public Avatar sourceAvatar;
            public ModelImporterSkinWeights skinWeights;
            public int maxBonesPerVertex;
            public float minBoneWeight;
            public bool optimizeBones;
            public bool optimizeGameObjects;

            // Materials tab
            public ModelImporterMaterialImportMode materialImportMode;
            public bool useSRGBMaterialColor;
            public ModelImporterMaterialLocation materialLocation;
            public ModelImporterMaterialName materialName;
            public ModelImporterMaterialSearch materialSearch;
            public List<MaterialRemap> materialRemaps = new List<MaterialRemap>();
        }

        // Hidden importer property backing the "Legacy Blend Shape Normals" toggle
        private const string LegacyBlendShapeNormalsProperty =
            "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes";

        private static FbxSettingsData copiedSettings;

        [MenuItem("Assets/Kanameliser Editor Plus/Copy FBX Settings", false, 20)]
        private static void CopyFbxSettings()
        {
            var importers = GetSelectedModelImporters();
            if (importers.Count != 1)
            {
                Debug.LogWarning("[FbxSettingsCopier] Select a single FBX asset to copy settings from.");
                return;
            }

            try
            {
                copiedSettings = CaptureSettings(importers[0]);
                Debug.Log($"[FbxSettingsCopier] Copied FBX settings from '{copiedSettings.sourceAssetName}' " +
                          $"({copiedSettings.materialRemaps.Count} material remaps).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FbxSettingsCopier] Error copying FBX settings: {ex.Message}");
            }
        }

        [MenuItem("Assets/Kanameliser Editor Plus/Copy FBX Settings", true)]
        private static bool ValidateCopyFbxSettings()
        {
            return GetSelectedModelImporters().Count == 1;
        }

        [MenuItem("Assets/Kanameliser Editor Plus/Paste FBX Settings", false, 21)]
        private static void PasteFbxSettings()
        {
            if (copiedSettings == null)
            {
                Debug.LogWarning("[FbxSettingsCopier] No FBX settings found. Please copy FBX settings first.");
                return;
            }

            var importers = GetSelectedModelImporters();
            if (importers.Count == 0)
            {
                Debug.LogWarning("[FbxSettingsCopier] No FBX assets selected for pasting settings.");
                return;
            }

            int applied = 0;
            int skipped = 0;
            int totalRemaps = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var importer in importers)
                {
                    try
                    {
                        // Reimporting is expensive, so skip assets that already
                        // match the copied settings.
                        bool settingsChanged = !AreSettingsEqual(CaptureSettings(importer), copiedSettings);
                        int remapsApplied = ApplyMaterialRemaps(importer, copiedSettings);

                        if (!settingsChanged && remapsApplied == 0)
                        {
                            skipped++;
                            continue;
                        }

                        if (settingsChanged)
                        {
                            ApplySettings(importer, copiedSettings);
                        }
                        importer.SaveAndReimport();
                        totalRemaps += remapsApplied;
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[FbxSettingsCopier] Error applying FBX settings to '{importer.assetPath}': {ex.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[FbxSettingsCopier] Applied FBX settings from '{copiedSettings.sourceAssetName}' " +
                      $"to {applied} assets ({totalRemaps} material remaps, {skipped} already up to date).");
        }

        [MenuItem("Assets/Kanameliser Editor Plus/Paste FBX Settings", true)]
        private static bool ValidatePasteFbxSettings()
        {
            return copiedSettings != null && GetSelectedModelImporters().Count > 0;
        }

        private static List<ModelImporter> GetSelectedModelImporters()
        {
            var importers = new List<ModelImporter>();
            if (Selection.assetGUIDs == null) return importers;

            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Restrict to .fbx: other model formats (.obj, .blend, ...) share
                // ModelImporter but differ in scale/rig semantics.
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    importers.Add(importer);
                }
            }
            return importers;
        }

        private static FbxSettingsData CaptureSettings(ModelImporter importer)
        {
            var data = new FbxSettingsData
            {
                sourceAssetName = System.IO.Path.GetFileName(importer.assetPath),

                // Model tab
                globalScale = importer.globalScale,
                useFileScale = importer.useFileScale,
                useFileUnits = importer.useFileUnits,
                bakeAxisConversion = importer.bakeAxisConversion,
                importBlendShapes = importer.importBlendShapes,
                importVisibility = importer.importVisibility,
                importCameras = importer.importCameras,
                importLights = importer.importLights,
                preserveHierarchy = importer.preserveHierarchy,
                sortHierarchyByName = importer.sortHierarchyByName,
                meshCompression = importer.meshCompression,
                isReadable = importer.isReadable,
                meshOptimizationFlags = importer.meshOptimizationFlags,
                addCollider = importer.addCollider,
                keepQuads = importer.keepQuads,
                weldVertices = importer.weldVertices,
                indexFormat = importer.indexFormat,
                importNormals = importer.importNormals,
                importBlendShapeNormals = importer.importBlendShapeNormals,
                normalCalculationMode = importer.normalCalculationMode,
                normalSmoothingSource = importer.normalSmoothingSource,
                normalSmoothingAngle = importer.normalSmoothingAngle,
                importTangents = importer.importTangents,
                swapUVChannels = importer.swapUVChannels,
                generateSecondaryUV = importer.generateSecondaryUV,
                secondaryUVAngleDistortion = importer.secondaryUVAngleDistortion,
                secondaryUVAreaDistortion = importer.secondaryUVAreaDistortion,
                secondaryUVHardAngle = importer.secondaryUVHardAngle,
                secondaryUVPackMargin = importer.secondaryUVPackMargin,
                secondaryUVMarginMethod = importer.secondaryUVMarginMethod,
                secondaryUVMinLightmapResolution = importer.secondaryUVMinLightmapResolution,
                secondaryUVMinObjectScale = importer.secondaryUVMinObjectScale,

                // Rig tab
                animationType = importer.animationType,
                avatarSetup = importer.avatarSetup,
                sourceAvatar = importer.sourceAvatar,
                skinWeights = importer.skinWeights,
                maxBonesPerVertex = importer.maxBonesPerVertex,
                minBoneWeight = importer.minBoneWeight,
                optimizeBones = importer.optimizeBones,
                optimizeGameObjects = importer.optimizeGameObjects,

                // Materials tab
                materialImportMode = importer.materialImportMode,
                useSRGBMaterialColor = importer.useSRGBMaterialColor,
                materialLocation = importer.materialLocation,
                materialName = importer.materialName,
                materialSearch = importer.materialSearch,
            };

            var serializedImporter = new SerializedObject(importer);
            var legacyNormalsProperty = serializedImporter.FindProperty(LegacyBlendShapeNormalsProperty);
            if (legacyNormalsProperty != null)
            {
                data.legacyBlendShapeNormals = legacyNormalsProperty.boolValue;
            }

            foreach (var pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type == typeof(Material) && pair.Value is Material material)
                {
                    data.materialRemaps.Add(new MaterialRemap(pair.Key.name, material));
                }
            }

            return data;
        }

        // Compares the settings that ApplySettings would write, mirroring its
        // conditional handling of sourceAvatar and custom skin weights.
        private static bool AreSettingsEqual(FbxSettingsData current, FbxSettingsData data)
        {
            if (current.globalScale != data.globalScale) return false;
            if (current.useFileScale != data.useFileScale) return false;
            if (current.useFileUnits != data.useFileUnits) return false;
            if (current.bakeAxisConversion != data.bakeAxisConversion) return false;
            if (current.importBlendShapes != data.importBlendShapes) return false;
            if (current.importVisibility != data.importVisibility) return false;
            if (current.importCameras != data.importCameras) return false;
            if (current.importLights != data.importLights) return false;
            if (current.preserveHierarchy != data.preserveHierarchy) return false;
            if (current.sortHierarchyByName != data.sortHierarchyByName) return false;
            if (current.meshCompression != data.meshCompression) return false;
            if (current.isReadable != data.isReadable) return false;
            if (current.meshOptimizationFlags != data.meshOptimizationFlags) return false;
            if (current.addCollider != data.addCollider) return false;
            if (current.keepQuads != data.keepQuads) return false;
            if (current.weldVertices != data.weldVertices) return false;
            if (current.indexFormat != data.indexFormat) return false;
            if (current.legacyBlendShapeNormals != data.legacyBlendShapeNormals) return false;
            if (current.importNormals != data.importNormals) return false;
            if (current.importBlendShapeNormals != data.importBlendShapeNormals) return false;
            if (current.normalCalculationMode != data.normalCalculationMode) return false;
            if (current.normalSmoothingSource != data.normalSmoothingSource) return false;
            if (current.normalSmoothingAngle != data.normalSmoothingAngle) return false;
            if (current.importTangents != data.importTangents) return false;
            if (current.swapUVChannels != data.swapUVChannels) return false;
            if (current.generateSecondaryUV != data.generateSecondaryUV) return false;
            if (current.secondaryUVAngleDistortion != data.secondaryUVAngleDistortion) return false;
            if (current.secondaryUVAreaDistortion != data.secondaryUVAreaDistortion) return false;
            if (current.secondaryUVHardAngle != data.secondaryUVHardAngle) return false;
            if (current.secondaryUVPackMargin != data.secondaryUVPackMargin) return false;
            if (current.secondaryUVMarginMethod != data.secondaryUVMarginMethod) return false;
            if (current.secondaryUVMinLightmapResolution != data.secondaryUVMinLightmapResolution) return false;
            if (current.secondaryUVMinObjectScale != data.secondaryUVMinObjectScale) return false;

            if (current.animationType != data.animationType) return false;
            if (current.avatarSetup != data.avatarSetup) return false;
            if (data.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && data.sourceAvatar != null &&
                current.sourceAvatar != data.sourceAvatar) return false;
            if (current.skinWeights != data.skinWeights) return false;
            if (data.skinWeights == ModelImporterSkinWeights.Custom)
            {
                if (current.maxBonesPerVertex != data.maxBonesPerVertex) return false;
                if (current.minBoneWeight != data.minBoneWeight) return false;
            }
            if (current.optimizeBones != data.optimizeBones) return false;
            if (current.optimizeGameObjects != data.optimizeGameObjects) return false;

            if (current.materialImportMode != data.materialImportMode) return false;
            if (current.useSRGBMaterialColor != data.useSRGBMaterialColor) return false;
            if (current.materialLocation != data.materialLocation) return false;
            if (current.materialName != data.materialName) return false;
            if (current.materialSearch != data.materialSearch) return false;

            return true;
        }

        // Applies copied settings to the importer (material remaps are handled
        // separately by ApplyMaterialRemaps, which must run before this so remap
        // targets are resolved from the current import result).
        // The caller is responsible for calling SaveAndReimport.
        private static void ApplySettings(ModelImporter importer, FbxSettingsData data)
        {
            // Model tab
            importer.globalScale = data.globalScale;
            importer.useFileScale = data.useFileScale;
            importer.useFileUnits = data.useFileUnits;
            importer.bakeAxisConversion = data.bakeAxisConversion;
            importer.importBlendShapes = data.importBlendShapes;
            importer.importVisibility = data.importVisibility;
            importer.importCameras = data.importCameras;
            importer.importLights = data.importLights;
            importer.preserveHierarchy = data.preserveHierarchy;
            importer.sortHierarchyByName = data.sortHierarchyByName;
            importer.meshCompression = data.meshCompression;
            importer.isReadable = data.isReadable;
            importer.meshOptimizationFlags = data.meshOptimizationFlags;
            importer.addCollider = data.addCollider;
            importer.keepQuads = data.keepQuads;
            importer.weldVertices = data.weldVertices;
            importer.indexFormat = data.indexFormat;
            importer.importNormals = data.importNormals;
            importer.importBlendShapeNormals = data.importBlendShapeNormals;
            importer.normalCalculationMode = data.normalCalculationMode;
            importer.normalSmoothingSource = data.normalSmoothingSource;
            importer.normalSmoothingAngle = data.normalSmoothingAngle;
            importer.importTangents = data.importTangents;
            importer.swapUVChannels = data.swapUVChannels;
            importer.generateSecondaryUV = data.generateSecondaryUV;
            importer.secondaryUVAngleDistortion = data.secondaryUVAngleDistortion;
            importer.secondaryUVAreaDistortion = data.secondaryUVAreaDistortion;
            importer.secondaryUVHardAngle = data.secondaryUVHardAngle;
            importer.secondaryUVPackMargin = data.secondaryUVPackMargin;
            importer.secondaryUVMarginMethod = data.secondaryUVMarginMethod;
            importer.secondaryUVMinLightmapResolution = data.secondaryUVMinLightmapResolution;
            importer.secondaryUVMinObjectScale = data.secondaryUVMinObjectScale;

            // Rig tab (humanDescription is intentionally not copied; with
            // CreateFromThisModel the target rebuilds its own avatar)
            importer.animationType = data.animationType;
            importer.avatarSetup = data.avatarSetup;
            if (data.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && data.sourceAvatar != null)
            {
                importer.sourceAvatar = data.sourceAvatar;
            }
            importer.skinWeights = data.skinWeights;
            if (data.skinWeights == ModelImporterSkinWeights.Custom)
            {
                importer.maxBonesPerVertex = data.maxBonesPerVertex;
                importer.minBoneWeight = data.minBoneWeight;
            }
            importer.optimizeBones = data.optimizeBones;
            importer.optimizeGameObjects = data.optimizeGameObjects;

            // Materials tab
            importer.materialImportMode = data.materialImportMode;
            importer.useSRGBMaterialColor = data.useSRGBMaterialColor;
            importer.materialLocation = data.materialLocation;
            importer.materialName = data.materialName;
            importer.materialSearch = data.materialSearch;

            // Hidden property must be applied through SerializedObject, created
            // after the direct property assignments so they are not overwritten.
            var serializedImporter = new SerializedObject(importer);
            var legacyNormalsProperty = serializedImporter.FindProperty(LegacyBlendShapeNormalsProperty);
            if (legacyNormalsProperty != null)
            {
                legacyNormalsProperty.boolValue = data.legacyBlendShapeNormals;
                serializedImporter.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Applies only the remaps whose material name exists on the target asset,
        // leaving unmatched slots untouched (same philosophy as MaterialCopier's
        // name-based matching). Remaps already pointing at the same material are
        // not counted as changes. Returns the number of remaps actually modified.
        private static int ApplyMaterialRemaps(ModelImporter importer, FbxSettingsData data)
        {
            if (data.materialRemaps == null || data.materialRemaps.Count == 0) return 0;

            // Known material names on the target: existing remap keys plus
            // embedded material sub-assets from the current import result.
            var knownNames = new HashSet<string>();
            var existingRemaps = new Dictionary<string, UnityEngine.Object>();
            foreach (var pair in importer.GetExternalObjectMap())
            {
                if (pair.Key.type == typeof(Material))
                {
                    knownNames.Add(pair.Key.name);
                    existingRemaps[pair.Key.name] = pair.Value;
                }
            }
            foreach (var subAsset in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
            {
                if (subAsset is Material embeddedMaterial)
                {
                    knownNames.Add(embeddedMaterial.name);
                }
            }

            int applied = 0;
            foreach (var remap in data.materialRemaps)
            {
                if (remap.material == null) continue;
                if (!knownNames.Contains(remap.name)) continue;
                if (existingRemaps.TryGetValue(remap.name, out var existing) && existing == remap.material) continue;

                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), remap.name),
                    remap.material);
                applied++;
            }
            return applied;
        }
    }
}
