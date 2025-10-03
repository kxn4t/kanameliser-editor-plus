using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Handles copying material setup from GameObjects
    /// </summary>
    public static class MaterialSetupCopier
    {
        private const int MAX_HIERARCHY_DEPTH = 10;

        /// <summary>
        /// Copies material setup from the specified GameObject and its children
        /// </summary>
        public static CopiedMaterialData CopyMaterialSetup(GameObject sourceRoot)
        {
            if (sourceRoot == null)
                throw new System.ArgumentNullException(nameof(sourceRoot));

            var copiedData = new CopiedMaterialData
            {
                sourceRootName = sourceRoot.name
            };

            // Scan hierarchy and collect material data
            ScanHierarchy(sourceRoot.transform, "", copiedData.materialSetups, 0, sourceRoot);

            return copiedData;
        }

        /// <summary>
        /// Recursively scans the hierarchy for renderers and their materials
        /// </summary>
        private static void ScanHierarchy(Transform current, string relativePath, List<MaterialSetupData> materialSetups, int depth, GameObject rootObject)
        {
            if (depth >= MAX_HIERARCHY_DEPTH)
                return;

            // Build current relative path (without root name)
            string currentPath = string.IsNullOrEmpty(relativePath)
                ? ""
                : relativePath;

            if (depth > 0)
            {
                currentPath = string.IsNullOrEmpty(currentPath)
                    ? current.name
                    : $"{currentPath}/{current.name}";
            }

            // Check for Renderer components
            Renderer renderer = current.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials != null && materials.Length > 0)
                {
                    var setupData = new MaterialSetupData(
                        current.name,
                        currentPath,
                        materials,
                        depth,
                        rootObject.name
                    );
                    materialSetups.Add(setupData);
                }
            }

            // Recursively scan children
            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                if (child != null)
                {
                    ScanHierarchy(child, currentPath, materialSetups, depth + 1, rootObject);
                }
            }
        }

        /// <summary>
        /// Validates if the source GameObject has any materials to copy
        /// </summary>
        public static bool HasMaterials(GameObject gameObject)
        {
            if (gameObject == null) return false;

            var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies material setup from multiple GameObjects
        /// </summary>
        public static CopiedMaterialData CopyMaterialSetupFromMultiple(GameObject[] sourceRoots)
        {
            if (sourceRoots == null || sourceRoots.Length == 0)
                throw new System.ArgumentException("Source roots array cannot be null or empty", nameof(sourceRoots));

            var copiedData = new CopiedMaterialData();

            // Process each source root as a separate group
            for (int i = 0; i < sourceRoots.Length; i++)
            {
                var sourceRoot = sourceRoots[i];
                if (sourceRoot == null) continue;

                // Create a group marker to separate different source objects
                var groupStartMarker = new MaterialSetupData
                {
                    objectName = $"__GROUP_START_{i}__",
                    relativePath = sourceRoot.name,
                    materials = new Material[0],
                    materialSlots = new int[0]
                };
                copiedData.materialSetups.Add(groupStartMarker);

                // Scan hierarchy for this source root
                ScanHierarchy(sourceRoot.transform, "", copiedData.materialSetups, 0, sourceRoot);

                // Update source root name to include group info
                if (i == 0)
                {
                    copiedData.sourceRootName = sourceRoot.name;
                }
                else
                {
                    copiedData.sourceRootName += $", {sourceRoot.name}";
                }
            }

            return copiedData;
        }

        /// <summary>
        /// Gets statistics about materials in a GameObject hierarchy
        /// </summary>
        public static (int objectCount, int materialCount) GetMaterialStats(GameObject gameObject)
        {
            if (gameObject == null) return (0, 0);

            int objectCount = 0;
            int materialCount = 0;

            var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                {
                    objectCount++;
                    materialCount += renderer.sharedMaterials.Length;
                }
            }

            return (objectCount, materialCount);
        }
    }
}
