using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Kanameliser.EditorPlus
{
    [InitializeOnLoad]
    internal class MeshInfoDisplay
    {
        private const string MenuPath = "Tools/Kanameliser Editor Plus/Show Mesh Info Display";
        private static bool isDisplayVisible;
        private static Texture2D backgroundTexture;

        private static int totalTriangles;
        private static int totalMaterials;
        private static int totalMeshes;
        private static int totalMaterialSlots;
        private static bool hasChildObjects;
        private static GameObject[] previousSelection;

        static MeshInfoDisplay()
        {
            isDisplayVisible = EditorPrefs.GetBool("MeshInfoDisplayVisible", true);
            Menu.SetChecked(MenuPath, isDisplayVisible);

            backgroundTexture = CreateTexture(150, 95, new Color(0.2f, 0.2f, 0.2f, 0.85f));

            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem(MenuPath)]
        private static void ToggleDisplayVisibility()
        {
            isDisplayVisible = !isDisplayVisible;
            EditorPrefs.SetBool("MeshInfoDisplayVisible", isDisplayVisible);
            Menu.SetChecked(MenuPath, isDisplayVisible);
            SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!isDisplayVisible || Selection.activeGameObject == null)
                return;

            if (previousSelection != Selection.gameObjects)
            {
                CalculateMeshInfo();
                previousSelection = Selection.gameObjects;
            }

            Handles.BeginGUI();

            GUIStyle backgroundStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = backgroundTexture },
                padding = new RectOffset(10, 10, 10, 10)
            };
            GUILayout.BeginArea(new Rect(50, 10, 150, 95), backgroundStyle);

            GUIStyle titleStyle = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUIStyle totalStyle = new GUIStyle(titleStyle)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal
            };
            GUIStyle infoStyle = new GUIStyle
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            Rect titleRect = new Rect(10, 5, 200, 20);
            GUI.Label(titleRect, "Mesh Info", titleStyle);

            if (hasChildObjects)
            {
                Rect totalRect = new Rect(titleRect.x + 75, titleRect.y + 3, 50, 20);
                GUI.Label(totalRect, "(Total)", totalStyle);
            }

            GUILayout.Space(20);
            GUILayout.Label($"Triangles: {totalTriangles}", infoStyle);
            GUILayout.Label($"Materials: {totalMaterials}", infoStyle);
            GUILayout.Label($"Material Slots: {totalMaterialSlots}", infoStyle);
            GUILayout.Label($"Meshes: {totalMeshes}", infoStyle);

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void CalculateMeshInfo()
        {
            totalTriangles = 0;
            totalMaterials = 0;
            totalMeshes = 0;
            totalMaterialSlots = 0;
            hasChildObjects = false;

            HashSet<Mesh> processedMeshes = new HashSet<Mesh>();
            HashSet<Material> processedMaterials = new HashSet<Material>();

            foreach (GameObject obj in Selection.gameObjects)
            {
                totalTriangles += ProcessMeshData(obj, ref processedMeshes, ref processedMaterials, ref hasChildObjects);
            }

            totalMeshes = processedMeshes.Count;
            totalMaterials = processedMaterials.Count;
        }

        // 再帰的にメッシュデータを取得
        private static int ProcessMeshData(GameObject obj, ref HashSet<Mesh> processedMeshes, ref HashSet<Material> processedMaterials, ref bool hasChildObjects)
        {
            if (obj.CompareTag("EditorOnly"))
                return 0;

            if (!hasChildObjects && obj.transform.childCount > 0)
                hasChildObjects = true;

            int triangleCount = 0;

            // MeshFilterをチェック
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            Renderer renderer = obj.GetComponent<Renderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null && processedMeshes.Add(meshFilter.sharedMesh))
            {
                triangleCount += CountTriangles(meshFilter.sharedMesh);

                if (renderer != null)
                {
                    foreach (Material mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                            processedMaterials.Add(mat);
                    }
                    totalMaterialSlots += renderer.sharedMaterials.Length;
                }
            }

            // SkinnedMeshRendererをチェック
            SkinnedMeshRenderer skinnedMeshRenderer = obj.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null && processedMeshes.Add(skinnedMeshRenderer.sharedMesh))
            {
                triangleCount += CountTriangles(skinnedMeshRenderer.sharedMesh);

                foreach (Material mat in skinnedMeshRenderer.sharedMaterials)
                {
                    if (mat != null)
                        processedMaterials.Add(mat);
                }
                totalMaterialSlots += skinnedMeshRenderer.sharedMaterials.Length;
            }

            // 子オブジェクトを再帰的に処理
            foreach (Transform child in obj.transform)
            {
                triangleCount += ProcessMeshData(child.gameObject, ref processedMeshes, ref processedMaterials, ref hasChildObjects);
            }

            return triangleCount;
        }

        private static int CountTriangles(Mesh mesh)
        {
            int triangleCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                triangleCount += (int)mesh.GetIndexCount(i) / 3;
            }
            return triangleCount;
        }

        private static Texture2D CreateTexture(int width, int height, Color col)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
