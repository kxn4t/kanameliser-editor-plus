using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Data structure for storing material setup information from a GameObject
    /// </summary>
    [Serializable]
    public class MaterialSetupData
    {
        public string objectName;
        public string relativePath;
        public Material[] materials;
        public int[] materialSlots;
        public int hierarchyDepth;
        public string rootObjectName;

        public MaterialSetupData()
        {
            materials = new Material[0];
            materialSlots = new int[0];
        }

        public MaterialSetupData(string objectName, string relativePath, Material[] materials, int depth = 0, string rootName = "")
        {
            this.objectName = objectName;
            this.relativePath = relativePath;
            this.materials = materials ?? new Material[0];
            this.materialSlots = new int[this.materials.Length];
            this.hierarchyDepth = depth;
            this.rootObjectName = rootName;
            for (int i = 0; i < this.materials.Length; i++)
            {
                this.materialSlots[i] = i;
            }
        }
    }

    /// <summary>
    /// Container for all copied material data
    /// </summary>
    [Serializable]
    public class CopiedMaterialData
    {
        public List<MaterialSetupData> materialSetups;
        public string sourceRootName;
        public DateTime copyTime;

        public CopiedMaterialData()
        {
            materialSetups = new List<MaterialSetupData>();
            copyTime = DateTime.Now;
        }
    }

    /// <summary>
    /// Session storage for material setup data
    /// </summary>
    public static class MAMaterialHelperSession
    {
        private static CopiedMaterialData _copiedData;

        /// <summary>
        /// Gets the currently copied material data
        /// </summary>
        public static CopiedMaterialData CopiedData => _copiedData;

        /// <summary>
        /// Checks if there is copied data available
        /// </summary>
        public static bool HasCopiedData => _copiedData != null && _copiedData.materialSetups.Count > 0;

        /// <summary>
        /// Stores material data in the session
        /// </summary>
        public static void StoreCopiedData(CopiedMaterialData data)
        {
            _copiedData = data;
        }

        /// <summary>
        /// Clears the copied data
        /// </summary>
        public static void ClearCopiedData()
        {
            _copiedData = null;
        }

        /// <summary>
        /// Gets a description of the copied data for display
        /// </summary>
        public static string GetCopiedDataDescription()
        {
            if (!HasCopiedData) return "No data copied";

            var objectCount = _copiedData.materialSetups.Count;
            var totalMaterials = 0;
            foreach (var setup in _copiedData.materialSetups)
            {
                totalMaterials += setup.materials.Length;
            }

            return $"Copied from '{_copiedData.sourceRootName}': {objectCount} objects, {totalMaterials} materials";
        }

        /// <summary>
        /// Gets the groups from copied material data
        /// </summary>
        public static List<List<MaterialSetupData>> GetCopiedDataGroups()
        {
            if (!HasCopiedData) return new List<List<MaterialSetupData>>();

            var groups = new List<List<MaterialSetupData>>();
            var currentGroup = new List<MaterialSetupData>();

            foreach (var setup in _copiedData.materialSetups)
            {
                if (setup.objectName.StartsWith("__GROUP_START_"))
                {
                    // Start a new group
                    if (currentGroup.Count > 0)
                    {
                        groups.Add(currentGroup);
                    }
                    currentGroup = new List<MaterialSetupData>();
                }
                else
                {
                    // Add to current group
                    currentGroup.Add(setup);
                }
            }

            // Add the last group if it has content
            if (currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
            }

            // If no groups were found (legacy single object), treat all as one group
            if (groups.Count == 0 && _copiedData.materialSetups.Count > 0)
            {
                groups.Add(new List<MaterialSetupData>(_copiedData.materialSetups));
            }

            return groups;
        }

        /// <summary>
        /// Gets the number of groups in the copied data
        /// </summary>
        public static int GetGroupCount()
        {
            return GetCopiedDataGroups().Count;
        }
    }
}
