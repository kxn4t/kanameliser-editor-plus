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

        public static int ProcessStandardMeshComponents(GameObject obj, HashSet<Mesh> processedMeshes, HashSet<Material> processedMaterials, HashSet<Renderer> processedRenderers, ref int totalMaterialSlots)
        {
            int triangleCount = 0;

            var meshFilter = obj.GetComponent<MeshFilter>();
            var meshRenderer = obj.GetComponent<MeshRenderer>();

            // Process standard mesh objects (MeshFilter + MeshRenderer combination)
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                triangleCount += ProcessMesh(meshFilter.sharedMesh, processedMeshes);

                if (meshRenderer != null && meshRenderer.sharedMaterials != null && processedRenderers.Add(meshRenderer))
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

                if (skinnedMeshRenderer.sharedMaterials != null && processedRenderers.Add(skinnedMeshRenderer))
                {
                    ProcessMaterials(skinnedMeshRenderer.sharedMaterials, processedMaterials);
                    totalMaterialSlots += skinnedMeshRenderer.sharedMaterials.Length;
                }
            }

            return triangleCount;
        }

        public static void ProcessParticleComponents(GameObject obj, MeshInfoData data)
        {
            // Particle systems consume material slots on VRChat avatars (trails add a second slot),
            // tracked separately from the mesh-based counts. Mesh particle polygons are excluded
            // on purpose: VRChat counts them as a separate stat, not as avatar polygons
            if (obj.GetComponent<ParticleSystem>() != null)
            {
                data.ParticleSystems++;

                var particleRenderer = obj.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null && particleRenderer.sharedMaterials != null)
                    data.ParticleMaterialSlots += particleRenderer.sharedMaterials.Length;
            }

            // Trail/Line renderers also consume material slots outside the mesh-based counts
            var trailRenderer = obj.GetComponent<TrailRenderer>();
            if (trailRenderer != null && trailRenderer.sharedMaterials != null)
                data.TrailLineMaterialSlots += trailRenderer.sharedMaterials.Length;

            var lineRenderer = obj.GetComponent<LineRenderer>();
            if (lineRenderer != null && lineRenderer.sharedMaterials != null)
                data.TrailLineMaterialSlots += lineRenderer.sharedMaterials.Length;
        }
    }
}
