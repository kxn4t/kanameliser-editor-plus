using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.Editor.MaterialSwapHelper
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

        public MaterialSetupData()
        {
            materials = new Material[0];
            materialSlots = new int[0];
        }

        public MaterialSetupData(string objectName, string relativePath, Material[] materials)
        {
            this.objectName = objectName;
            this.relativePath = relativePath;
            this.materials = materials ?? new Material[0];
            this.materialSlots = new int[this.materials.Length];
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
    public static class MaterialSwapHelperSession
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
    }
}