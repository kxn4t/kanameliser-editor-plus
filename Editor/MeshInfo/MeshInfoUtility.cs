using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    internal static class MeshInfoUtility
    {
        public static int CountTriangles(Mesh mesh)
        {
            int triangleCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                triangleCount += (int)mesh.GetIndexCount(i) / 3;
            }
            return triangleCount;
        }

        public static int ProcessMesh(Mesh mesh, HashSet<Mesh> processedMeshes)
        {
            // Skip meshes that have already been processed to avoid double counting
            if (!processedMeshes.Add(mesh))
                return 0;

            return CountTriangles(mesh);
        }

        public static void ProcessMaterials(Material[] materials, HashSet<Material> processedMaterials)
        {
            foreach (var mat in materials)
            {
                if (mat != null)
                    processedMaterials.Add(mat);
            }
        }

        public static int ProcessStandardMeshComponents(GameObject obj, HashSet<Mesh> processedMeshes, HashSet<Material> processedMaterials, ref int totalMaterialSlots)
        {
            int triangleCount = 0;

            var meshFilter = obj.GetComponent<MeshFilter>();
            var meshRenderer = obj.GetComponent<MeshRenderer>();

            // Process standard mesh objects (MeshFilter + MeshRenderer combination)
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                triangleCount += ProcessMesh(meshFilter.sharedMesh, processedMeshes);

                if (meshRenderer != null && meshRenderer.sharedMaterials != null)
                {
                    ProcessMaterials(meshRenderer.sharedMaterials, processedMaterials);
                    totalMaterialSlots += meshRenderer.sharedMaterials.Length;
                }
            }

            // Process skinned mesh objects (commonly used for avatars with bone deformation)
            var skinnedMeshRenderer = obj.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                triangleCount += ProcessMesh(skinnedMeshRenderer.sharedMesh, processedMeshes);

                if (skinnedMeshRenderer.sharedMaterials != null)
                {
                    ProcessMaterials(skinnedMeshRenderer.sharedMaterials, processedMaterials);
                    totalMaterialSlots += skinnedMeshRenderer.sharedMaterials.Length;
                }
            }

            return triangleCount;
        }
    }
}