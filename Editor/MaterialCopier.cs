using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Material data storage for copy/paste operations
    /// </summary>
    [Serializable]
    public class MaterialData
    {
        public string objectName;
        public Material[] materials;
        public int hierarchyDepth;
        public string relativePath;
        public string rootObjectName;

        public MaterialData(string name, Material[] mats, int depth, string path, string rootName)
        {
            objectName = name;
            materials = mats != null ? (Material[])mats.Clone() : new Material[0];
            hierarchyDepth = depth;
            relativePath = path;
            rootObjectName = rootName;
        }
    }
    /// <summary>
    /// Material copy and paste functionality for Unity Editor
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
                        rootObject.name
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
                        rootObject.name
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
                    var matchingData = FindMatchingMaterialData(obj, rootObject, depth);
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

        // Find matching material data with multi-stage matching prioritizing relative path
        private static MaterialData FindMatchingMaterialData(GameObject targetObj, GameObject rootObject, int depth)
        {
            if (targetObj == null || copiedMaterials == null) return null;

            string targetName = targetObj.name;
            string targetRelativePath = GetRelativePath(targetObj, rootObject);

            // Priority 1: Exact relative path AND object name match
            var exactPathMatches = copiedMaterials.Where(data =>
                data.relativePath == targetRelativePath &&
                data.objectName == targetName).ToList();
            if (exactPathMatches.Count > 0)
            {
                // If multiple matches, prefer those with matching rootObjectName
                if (exactPathMatches.Count > 1)
                {
                    var rootNameMatches = exactPathMatches.Where(data =>
                        data.rootObjectName == rootObject.name).ToList();
                    if (rootNameMatches.Count > 0)
                    {
                        return rootNameMatches.First();
                    }
                }
                return exactPathMatches.First();
            }

            // Priority 2: Same depth + exact name match
            var sameDepthExactNameMatches = copiedMaterials.Where(data =>
                data.hierarchyDepth == depth && data.objectName == targetName).ToList();
            if (sameDepthExactNameMatches.Count > 0)
            {
                // Filter by parent hierarchy if multiple matches
                if (sameDepthExactNameMatches.Count > 1)
                {
                    sameDepthExactNameMatches = FilterByParentHierarchy(
                        sameDepthExactNameMatches, targetRelativePath, rootObject);
                }
                return GetBestDepthMatch(sameDepthExactNameMatches, depth, rootObject, targetRelativePath);
            }

            // Priority 3: Exact name match (any depth, closest depth preferred)
            var exactNameMatches = copiedMaterials.Where(data => data.objectName == targetName).ToList();
            if (exactNameMatches.Count > 0)
            {
                // Filter by parent hierarchy if multiple matches
                if (exactNameMatches.Count > 1)
                {
                    exactNameMatches = FilterByParentHierarchy(
                        exactNameMatches, targetRelativePath, rootObject);
                }
                return GetBestDepthMatch(exactNameMatches, depth, rootObject, targetRelativePath);
            }

            // Priority 4: Case-insensitive name match (any depth, closest depth preferred)
            var caseInsensitiveMatches = copiedMaterials.Where(data =>
                string.Equals(data.objectName, targetName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (caseInsensitiveMatches.Count > 0)
            {
                // Filter by parent hierarchy if multiple matches
                if (caseInsensitiveMatches.Count > 1)
                {
                    caseInsensitiveMatches = FilterByParentHierarchy(
                        caseInsensitiveMatches, targetRelativePath, rootObject);
                }
                return GetBestDepthMatch(caseInsensitiveMatches, depth, rootObject, targetRelativePath);
            }

            return null;
        }

        // Select best match based on hierarchy depth - prioritize same depth, then closest depth
        private static MaterialData GetBestDepthMatch(List<MaterialData> candidates, int targetDepth, GameObject rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // Prefer same depth matches
            var sameDepthMatches = candidates.Where(data => data.hierarchyDepth == targetDepth).ToList();
            if (sameDepthMatches.Count > 0)
            {
                // If multiple with same depth, use similarity scoring
                if (sameDepthMatches.Count > 1)
                {
                    return SelectBySimilarity(sameDepthMatches, rootObject, targetRelativePath);
                }
                return sameDepthMatches.First();
            }

            // Select by closest depth, then by similarity
            var groupedByDepth = candidates
                .GroupBy(data => Math.Abs(data.hierarchyDepth - targetDepth))
                .OrderBy(g => g.Key)
                .First();

            var closestDepthCandidates = groupedByDepth.ToList();
            if (closestDepthCandidates.Count > 1)
            {
                return SelectBySimilarity(closestDepthCandidates, rootObject, targetRelativePath);
            }

            return closestDepthCandidates.First();
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
        /// Get relative path from root object to target object
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

        /// <summary>
        /// Calculates Levenshtein distance (edit distance) between two strings
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int[,] d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(
                        d[i - 1, j] + 1,      // deletion
                        d[i, j - 1] + 1),     // insertion
                        d[i - 1, j - 1] + cost); // substitution
                }
            }

            return d[s1.Length, s2.Length];
        }

        /// <summary>
        /// Selects the best candidate from multiple matches based on name similarity
        /// </summary>
        private static MaterialData SelectBySimilarity(List<MaterialData> candidates, GameObject rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates.First();

            return candidates
                .OrderBy(data => LevenshteinDistance(data.rootObjectName, rootObject.name))  // rootObject name similarity
                .ThenBy(data => LevenshteinDistance(data.relativePath, targetRelativePath))  // path similarity
                .First();
        }

        /// <summary>
        /// Filters candidates by matching parent hierarchy from bottom to top
        /// </summary>
        private static List<MaterialData> FilterByParentHierarchy(
            List<MaterialData> candidates,
            string targetRelativePath,
            GameObject rootObject)
        {
            if (candidates.Count <= 1) return candidates;

            string rootName = rootObject.name;
            string[] targetParts = string.IsNullOrEmpty(targetRelativePath)
                ? new string[0]
                : targetRelativePath.Split('/');

            // Level 0: Filter by rootObjectName first
            var rootNameFiltered = candidates.Where(data =>
                data.rootObjectName == rootName).ToList();
            if (rootNameFiltered.Count > 0)
            {
                candidates = rootNameFiltered;
                if (candidates.Count == 1) return candidates;
            }

            // Get maximum depth among candidates
            int maxDepth = candidates.Max(c => c.relativePath.Split('/').Length);

            // Start from parent level (one level up from the object itself)
            // We compare backwards, matching parent hierarchy
            for (int level = 1; level < maxDepth; level++)
            {
                var filtered = candidates.Where(data =>
                {
                    string[] sourceParts = data.relativePath.Split('/');

                    // Check if source has enough depth for this level
                    if (sourceParts.Length <= level) return false;

                    // Get parent name at this level (counting from the end)
                    // level=1: immediate parent, level=2: grandparent, etc.
                    int sourceIndex = sourceParts.Length - 1 - level;
                    string sourceParent = sourceParts[sourceIndex];

                    // For target, we need to compare with appropriate level
                    int targetIndex = targetParts.Length - 1 - level;

                    if (targetIndex >= 0)
                    {
                        // Target has this level in its path
                        string targetParent = targetParts[targetIndex];
                        return sourceParent == targetParent;
                    }
                    else
                    {
                        // Target doesn't have this level (we're beyond target's depth)
                        // Compare with rootObject name
                        return sourceParent == rootName;
                    }
                }).ToList();

                // If filtering narrowed down candidates, use filtered list
                if (filtered.Count > 0)
                {
                    candidates = filtered;

                    // If narrowed down to one, we're done
                    if (candidates.Count == 1) break;
                }
                // If filtered.Count == 0, keep the previous candidates and stop filtering
                else
                {
                    break;
                }
            }

            return candidates;
        }
    }
}