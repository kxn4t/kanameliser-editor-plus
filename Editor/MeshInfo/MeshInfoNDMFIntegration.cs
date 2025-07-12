#if NDMF_INSTALLED
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    internal class MeshInfoNDMFIntegration
    {
        private readonly MeshInfoCalculator calculator;

        public MeshInfoNDMFIntegration(MeshInfoCalculator calculator)
        {
            this.calculator = calculator;
        }

        public bool IsShowingProxyInfo { get; private set; }
        public bool HasProxyInSelection { get; private set; }

        public MeshInfoData CalculateWithProxy(GameObject[] gameObjects, out MeshInfoData originalData)
        {
            IsShowingProxyInfo = false;
            HasProxyInSelection = NDMFPreviewHelper.HasProxyForSelection(gameObjects);
            originalData = null;

            // When NDMF preview is active and proxy meshes exist, calculate both original and proxy data
            if (NDMFPreviewHelper.IsPreviewActive() && HasProxyInSelection)
            {
                originalData = calculator.CalculateMeshInfo(gameObjects);
                return CalculateProxyMeshInfo(gameObjects);
            }

            return calculator.CalculateMeshInfo(gameObjects);
        }

        private MeshInfoData CalculateProxyMeshInfo(GameObject[] gameObjects)
        {
            var data = new MeshInfoData();
            var processedMeshes = new HashSet<Mesh>();
            var processedMaterials = new HashSet<Material>();
            bool hasChildObjects = false;
            int materialSlots = 0;

            foreach (var obj in gameObjects)
            {
                data.Triangles += ProcessGameObjectWithProxy(obj, processedMeshes, processedMaterials, ref hasChildObjects, ref materialSlots);
            }

            data.Meshes = processedMeshes.Count;
            data.Materials = processedMaterials.Count;
            data.HasChildObjects = hasChildObjects;
            data.MaterialSlots = materialSlots;

            return data;
        }

        private int ProcessGameObjectWithProxy(GameObject obj, HashSet<Mesh> processedMeshes, HashSet<Material> processedMaterials,
            ref bool hasChildObjects, ref int totalMaterialSlots)
        {
            if (obj.CompareTag("EditorOnly"))
                return 0;

            if (!hasChildObjects && obj.transform.childCount > 0)
                hasChildObjects = true;

            int triangleCount = 0;
            var renderer = obj.GetComponent<Renderer>();

            // Check if this renderer has a proxy version for build preview
            if (NDMFPreviewHelper.IsPreviewActive() && renderer != null)
            {
                var proxyRenderer = NDMFPreviewHelper.GetProxyRenderer(renderer);
                if (proxyRenderer != null)
                {
                    // Use proxy mesh data instead of original for accurate build preview
                    IsShowingProxyInfo = true;
                    triangleCount = ProcessProxyRenderer(proxyRenderer, processedMeshes, processedMaterials, ref totalMaterialSlots);

                    foreach (Transform child in obj.transform)
                    {
                        triangleCount += ProcessGameObjectWithProxy(child.gameObject, processedMeshes, processedMaterials, ref hasChildObjects, ref totalMaterialSlots);
                    }

                    return triangleCount;
                }
            }

            triangleCount += MeshInfoUtility.ProcessStandardMeshComponents(obj, processedMeshes, processedMaterials, ref totalMaterialSlots);

            foreach (Transform child in obj.transform)
            {
                triangleCount += ProcessGameObjectWithProxy(child.gameObject, processedMeshes, processedMaterials, ref hasChildObjects, ref totalMaterialSlots);
            }

            return triangleCount;
        }

        private int ProcessProxyRenderer(Renderer proxyRenderer, HashSet<Mesh> processedMeshes, HashSet<Material> processedMaterials, ref int totalMaterialSlots)
        {
            return MeshInfoUtility.ProcessStandardMeshComponents(proxyRenderer.gameObject, processedMeshes, processedMaterials, ref totalMaterialSlots);
        }

    }
}
#endif