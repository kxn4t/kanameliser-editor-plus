using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    internal class MeshInfoCalculator
    {
        public MeshInfoData CalculateMeshInfo(GameObject[] gameObjects)
        {
            var data = new MeshInfoData();
            var processedMeshes = new HashSet<Mesh>();
            var processedMaterials = new HashSet<Material>();
            bool hasChildObjects = false;
            int materialSlots = 0;

            foreach (var obj in gameObjects)
            {
                data.Triangles += ProcessGameObject(obj, processedMeshes, processedMaterials, ref hasChildObjects, ref materialSlots);
            }

            data.Meshes = processedMeshes.Count;
            data.Materials = processedMaterials.Count;
            data.HasChildObjects = hasChildObjects;
            data.MaterialSlots = materialSlots;

            return data;
        }

        private int ProcessGameObject(GameObject obj, HashSet<Mesh> processedMeshes, HashSet<Material> processedMaterials,
            ref bool hasChildObjects, ref int totalMaterialSlots)
        {
            // Skip objects tagged as EditorOnly as they won't be included in builds
            if (obj.CompareTag("EditorOnly"))
                return 0;

            // Track if any object in the hierarchy has children to show "(Total)" in UI
            if (!hasChildObjects && obj.transform.childCount > 0)
                hasChildObjects = true;

            int triangleCount = 0;

            triangleCount += MeshInfoUtility.ProcessStandardMeshComponents(obj, processedMeshes, processedMaterials, ref totalMaterialSlots);

            foreach (Transform child in obj.transform)
            {
                triangleCount += ProcessGameObject(child.gameObject, processedMeshes, processedMaterials, ref hasChildObjects, ref totalMaterialSlots);
            }

            return triangleCount;
        }

    }
}