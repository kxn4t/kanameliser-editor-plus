using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Material data storage for copy/paste operations.
    /// </summary>
    [Serializable]
    public class MaterialData
    {
        public string objectName;
        public Material[] materials;
        public int hierarchyDepth;
        public string relativePath;
        public string rootObjectName;
        public string rendererType;

        public MaterialData(string name, Material[] mats, int depth, string path, string rootName, string rendererTypeName = "")
        {
            objectName = name;
            materials = mats != null ? (Material[])mats.Clone() : new Material[0];
            hierarchyDepth = depth;
            relativePath = path;
            rootObjectName = rootName;
            rendererType = rendererTypeName;
        }
    }

    /// <summary>
    /// Material copy and paste functionality for Unity Editor.
    /// Uses the same 5-tier matching algorithm as ObjectMatcher / RendererMatcher V3.
    /// </summary>
    internal class MaterialCopier
    {
        private static List<MaterialData> copiedMaterials = new List<MaterialData>();

        [MenuItem("GameObject/Kanameliser Editor Plus/Copy Materials", false, 0)]
        private static void CopyMaterials()
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[MaterialCopier] No GameObjects selected for copying materials.");
                return;
            }

            copiedMaterials.Clear();
            int totalCopied = 0;

            foreach (GameObject selectedObject in Selection.gameObjects)
            {
                if (selectedObject == null) continue;

                totalCopied += CollectMaterialsFromHierarchy(selectedObject, selectedObject, 0);
            }

            Debug.Log($"[MaterialCopier] Copied materials from {totalCopied} objects.");
        }

        [MenuItem("GameObject/Kanameliser Editor Plus/Paste Materials", false, 0)]
        private static void PasteMaterials()
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[MaterialCopier] No GameObjects selected for pasting materials.");
                return;
            }

            if (copiedMaterials == null || copiedMaterials.Count == 0)
            {
                Debug.LogWarning("[MaterialCopier] No materials data found. Please copy materials first.");
                return;
            }

            Undo.SetCurrentGroupName("Paste Materials");
            int undoGroup = Undo.GetCurrentGroup();

            int totalPasted = 0;

            foreach (GameObject selectedObject in Selection.gameObjects)
            {
                if (selectedObject == null) continue;

                totalPasted += ApplyMaterialsToHierarchy(selectedObject, selectedObject, 0);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[MaterialCopier] Applied materials to {totalPasted} objects.");
        }

        [MenuItem("GameObject/Kanameliser Editor Plus/Copy Materials", true)]
        private static bool ValidateCopyMaterials()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem("GameObject/Kanameliser Editor Plus/Paste Materials", true)]
        private static bool ValidatePasteMaterials()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0 &&
                   copiedMaterials != null && copiedMaterials.Count > 0;
        }

        // Recursively collect material data from object hierarchy
        private static int CollectMaterialsFromHierarchy(GameObject obj, GameObject rootObject, int depth)
        {
            if (obj == null) return 0;

            int collected = 0;

            try
            {
                // Collect materials from MeshRenderer
                var meshRenderer = obj.GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.sharedMaterials != null)
                {
                    string relativePath = GetRelativePath(obj, rootObject);
                    var materialData = new MaterialData(
                        obj.name,
                        meshRenderer.sharedMaterials,
                        depth,
                        relativePath,
                        rootObject.name,
                        meshRenderer.GetType().Name
                    );
                    copiedMaterials.Add(materialData);
                    collected++;
                }

                // Collect materials from SkinnedMeshRenderer
                var skinnedMeshRenderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMaterials != null)
                {
                    string relativePath = GetRelativePath(obj, rootObject);
                    var materialData = new MaterialData(
                        obj.name,
                        skinnedMeshRenderer.sharedMaterials,
                        depth,
                        relativePath,
                        rootObject.name,
                        skinnedMeshRenderer.GetType().Name
                    );
                    copiedMaterials.Add(materialData);
                    collected++;
                }

                // Process child objects recursively
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    var child = obj.transform.GetChild(i);
                    if (child != null)
                    {
                        collected += CollectMaterialsFromHierarchy(child.gameObject, rootObject, depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MaterialCopier] Error collecting materials from {obj.name}: {ex.Message}");
            }

            return collected;
        }

        // Apply materials to object hierarchy recursively
        private static int ApplyMaterialsToHierarchy(GameObject obj, GameObject rootObject, int depth)
        {
            if (obj == null) return 0;

            int applied = 0;

            try
            {
                // Only process objects with Renderer components
                var renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Find and apply matching material data
                    string targetRelativePath = GetRelativePath(obj, rootObject);
                    string targetRendererType = renderer.GetType().Name;
                    var matchingData = FindMatchingMaterialData(obj.name, targetRelativePath, depth, targetRendererType, rootObject.name);
                    if (matchingData != null)
                    {
                        applied += ApplyMaterialsToObject(obj, matchingData);
                    }
                }

                // Process child objects recursively
                for (int i = 0; i < obj.transform.childCount; i++)
                {
                    var child = obj.transform.GetChild(i);
                    if (child != null)
                    {
                        applied += ApplyMaterialsToHierarchy(child.gameObject, rootObject, depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MaterialCopier] Error applying materials to {obj.name}: {ex.Message}");
            }

            return applied;
        }

        /// <summary>
        /// Find matching material data using the same 5-tier algorithm as ObjectMatcher V3.
        /// Direction is reversed: given a target object, find the best source MaterialData.
        /// P1: Exact path + name, P2: Same depth + name, P3: Name (any depth),
        /// P4: Case-insensitive name, P5: Similar name (scored).
        /// All tiers apply rendererType filtering when non-empty.
        /// </summary>
        private static MaterialData FindMatchingMaterialData(string targetName, string targetRelativePath, int targetDepth, string targetRendererType = "", string targetRootName = "")
        {
            if (copiedMaterials == null || copiedMaterials.Count == 0) return null;

            // Apply renderer type filter to base candidates
            var baseCandidates = copiedMaterials.Where(d =>
                string.IsNullOrEmpty(targetRendererType) ||
                string.IsNullOrEmpty(d.rendererType) ||
                d.rendererType == targetRendererType).ToList();

            // P1: Exact relative path AND exact name match
            var p1 = baseCandidates.Where(d =>
                d.relativePath == targetRelativePath && d.objectName == targetName).ToList();
            if (p1.Count > 0)
                return SelectBestSource(p1, targetRelativePath, targetDepth, targetRootName);

            // P2: Same depth + exact name match
            var p2 = baseCandidates.Where(d =>
                d.hierarchyDepth == targetDepth && d.objectName == targetName).ToList();
            if (p2.Count > 0)
                return SelectBestSource(p2, targetRelativePath, targetDepth, targetRootName);

            // P3: Exact name match (any depth)
            var p3 = baseCandidates.Where(d => d.objectName == targetName).ToList();
            if (p3.Count > 0)
                return SelectBestSource(p3, targetRelativePath, targetDepth, targetRootName);

            // P4: Case-insensitive name match
            var p4 = baseCandidates.Where(d =>
                string.Equals(d.objectName, targetName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (p4.Count > 0)
                return SelectBestSource(p4, targetRelativePath, targetDepth, targetRootName);

            // P5: Similar name match (scored)
            // Eligibility: NormalizeName match OR HasCommonBaseName(minToken=3)
            string normalizedTarget = ObjectMatcher.NormalizeName(targetName);
            var p5 = baseCandidates.Where(d =>
            {
                string normalizedSource = ObjectMatcher.NormalizeName(d.objectName);
                return string.Equals(normalizedTarget, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                       ObjectMatcher.HasCommonBaseName(d.objectName, targetName, 3);
            }).ToList();
            if (p5.Count > 0)
                return SelectBestFuzzySource(p5, targetName, targetRelativePath, targetDepth, targetRootName);

            // P5 cross-type fallback: relax rendererType filter
            // Catches cross-renderer-type matches (e.g. MeshRenderer ↔ SkinnedMeshRenderer)
            // when source and target use different renderer types for the same logical mesh.
            if (!string.IsNullOrEmpty(targetRendererType))
            {
                var crossTypeCandidates = copiedMaterials.Where(d =>
                    !string.IsNullOrEmpty(d.rendererType) &&
                    d.rendererType != targetRendererType).ToList();

                var p5CrossType = crossTypeCandidates.Where(d =>
                {
                    string normalizedSource = ObjectMatcher.NormalizeName(d.objectName);
                    return string.Equals(normalizedTarget, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                           ObjectMatcher.HasCommonBaseName(d.objectName, targetName, 3);
                }).ToList();
                if (p5CrossType.Count > 0)
                    return SelectBestFuzzySource(p5CrossType, targetName, targetRelativePath, targetDepth, targetRootName);
            }

            return null;
        }

        /// <summary>
        /// Selects the best source MaterialData for P1-P4 tiers using path segment scoring,
        /// ancestor context, root name affinity, depth proximity, and Levenshtein distance.
        /// Uses ObjectMatcher's shared scoring utilities.
        /// </summary>
        private static MaterialData SelectBestSource(List<MaterialData> candidates, string targetRelativePath, int targetDepth, string targetRootName = "")
        {
            if (candidates.Count == 1) return candidates[0];

            return candidates
                .OrderByDescending(d =>
                    ObjectMatcher.PathSegmentScore(targetRelativePath, d.relativePath) +
                    ObjectMatcher.AncestorContextScore(d.rootObjectName, targetRelativePath) +
                    RootNameBonus(d.rootObjectName, targetRootName))
                .ThenBy(d => Math.Abs(d.hierarchyDepth - targetDepth))
                .ThenBy(d => ObjectMatcher.LevenshteinDistance(targetRelativePath ?? "", d.relativePath ?? ""))
                .First();
        }

        /// <summary>
        /// Selects the best source MaterialData for P5 fuzzy tier using combined name + path scoring.
        /// Score: NameScore(norm=100, fuzzy=60) + PathSegmentScore + AncestorContextScore + RootNameBonus.
        /// Tiebreakers: highest score, then depth proximity, then Levenshtein distance.
        /// </summary>
        private static MaterialData SelectBestFuzzySource(List<MaterialData> candidates, string targetName, string targetRelativePath, int targetDepth, string targetRootName = "")
        {
            if (candidates.Count == 1) return candidates[0];

            string normalizedTarget = ObjectMatcher.NormalizeName(targetName);

            return candidates
                .OrderByDescending(d =>
                {
                    string normalizedSource = ObjectMatcher.NormalizeName(d.objectName);
                    float nameScore = string.Equals(normalizedTarget, normalizedSource, StringComparison.OrdinalIgnoreCase)
                        ? 100f
                        : 60f;
                    return nameScore +
                           ObjectMatcher.PathSegmentScore(targetRelativePath, d.relativePath) +
                           ObjectMatcher.AncestorContextScore(d.rootObjectName, targetRelativePath) +
                           RootNameBonus(d.rootObjectName, targetRootName);
                })
                .ThenBy(d => Math.Abs(d.hierarchyDepth - targetDepth))
                .ThenBy(d => ObjectMatcher.LevenshteinDistance(targetRelativePath ?? "", d.relativePath ?? ""))
                .First();
        }

        /// <summary>
        /// Bonus score when source root name matches the paste target root name.
        /// Disambiguates multi-root copy/paste scenarios where AncestorContextScore
        /// cannot help (relative paths do not contain the root name itself).
        /// </summary>
        private static float RootNameBonus(string sourceRootName, string targetRootName)
        {
            if (string.IsNullOrEmpty(sourceRootName) || string.IsNullOrEmpty(targetRootName))
                return 0f;
            if (sourceRootName == targetRootName)
                return 30f;
            if (ObjectMatcher.HasCommonBaseName(sourceRootName, targetRootName, 1))
                return 20f;
            return 0f;
        }

        // Apply materials to object, handling array size differences by pasting available slots
        private static int ApplyMaterialsToObject(GameObject obj, MaterialData materialData)
        {
            if (obj == null || materialData == null || materialData.materials == null) return 0;

            int applied = 0;

            try
            {
                // Apply materials to MeshRenderer
                var meshRenderer = obj.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    Undo.RecordObject(meshRenderer, "Apply Materials to MeshRenderer");

                    Material[] currentMaterials = meshRenderer.sharedMaterials;
                    Material[] newMaterials = new Material[currentMaterials.Length];

                    // Paste materials up to available slots
                    for (int i = 0; i < newMaterials.Length; i++)
                    {
                        newMaterials[i] = i < materialData.materials.Length ?
                                         materialData.materials[i] : currentMaterials[i];
                    }

                    meshRenderer.sharedMaterials = newMaterials;
                    applied++;
                }

                // Apply materials to SkinnedMeshRenderer
                var skinnedMeshRenderer = obj.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                {
                    Undo.RecordObject(skinnedMeshRenderer, "Apply Materials to SkinnedMeshRenderer");

                    Material[] currentMaterials = skinnedMeshRenderer.sharedMaterials;
                    Material[] newMaterials = new Material[currentMaterials.Length];

                    for (int i = 0; i < newMaterials.Length; i++)
                    {
                        newMaterials[i] = i < materialData.materials.Length ?
                                         materialData.materials[i] : currentMaterials[i];
                    }

                    skinnedMeshRenderer.sharedMaterials = newMaterials;
                    applied++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MaterialCopier] Error applying materials to {obj.name}: {ex.Message}");
            }

            return applied;
        }

        /// <summary>
        /// Get relative path from root object to target object.
        /// </summary>
        private static string GetRelativePath(GameObject obj, GameObject rootObject)
        {
            if (obj == null) return "";
            if (obj == rootObject) return "";

            string path = obj.name;
            Transform parent = obj.transform.parent;

            while (parent != null && parent.gameObject != rootObject)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
