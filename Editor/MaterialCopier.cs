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

        public MaterialData(string name, Material[] mats, int depth, string path)
        {
            objectName = name;
            materials = mats != null ? (Material[])mats.Clone() : new Material[0];
            hierarchyDepth = depth;
            relativePath = path;
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
                        relativePath
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
                        relativePath
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
                // Find and apply matching material data
                var matchingData = FindMatchingMaterialData(obj, rootObject, depth);
                if (matchingData != null)
                {
                    applied += ApplyMaterialsToObject(obj, matchingData);
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

        // Find matching material data with two-stage matching: exact match, then case-insensitive
        private static MaterialData FindMatchingMaterialData(GameObject targetObj, GameObject rootObject, int depth)
        {
            if (targetObj == null || copiedMaterials == null) return null;

            string targetName = targetObj.name;
            string targetRelativePath = GetRelativePath(targetObj, rootObject);

            // Stage 1: Exact match (case-sensitive)
            var exactMatches = copiedMaterials.Where(data => data.objectName == targetName).ToList();
            if (exactMatches.Count > 0)
            {
                return GetBestDepthMatch(exactMatches, depth);
            }

            // Stage 2: Case-insensitive match
            var caseInsensitiveMatches = copiedMaterials.Where(data =>
                string.Equals(data.objectName, targetName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (caseInsensitiveMatches.Count > 0)
            {
                return GetBestDepthMatch(caseInsensitiveMatches, depth);
            }

            return null;
        }

        // Select best match based on hierarchy depth - prioritize same depth, then closest depth
        private static MaterialData GetBestDepthMatch(List<MaterialData> candidates, int targetDepth)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // Prefer same depth matches
            var sameDepthMatches = candidates.Where(data => data.hierarchyDepth == targetDepth).ToList();
            if (sameDepthMatches.Count > 0)
            {
                return sameDepthMatches.First();
            }

            // Select closest depth if no exact depth match
            return candidates.OrderBy(data => Math.Abs(data.hierarchyDepth - targetDepth)).First();
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
    }
}