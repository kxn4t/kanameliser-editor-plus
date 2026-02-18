using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Kanameliser.EditorPlus
{
    [InitializeOnLoad]
    internal class MeshInfoDisplay
    {
        private static bool isDisplayVisible;
        private static GameObject[] previousSelection;
        private static readonly MeshInfoCalculator calculator = new MeshInfoCalculator();
        private static readonly MeshInfoRenderer renderer = new MeshInfoRenderer();
        private static MeshInfoData currentData;
        private static MeshInfoData originalData;

        private static double lastUpdateTime;
        private static bool forceUpdate;

#if NDMF_INSTALLED
        // Initialize NDMF integration to handle proxy mesh information during build preview
        private static readonly MeshInfoNDMFIntegration ndmfIntegration = new MeshInfoNDMFIntegration(calculator);
#endif

        static MeshInfoDisplay()
        {
            isDisplayVisible = EditorPrefs.GetBool(MeshInfoConstants.PreferenceKey, true);
            Menu.SetChecked(MeshInfoConstants.MenuPath, isDisplayVisible);
            forceUpdate = true;
            lastUpdateTime = 0;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.quitting += OnEditorQuitting;
        }

        [MenuItem(MeshInfoConstants.MenuPath, false, 1100)]
        private static void ToggleDisplayVisibility()
        {
            isDisplayVisible = !isDisplayVisible;
            EditorPrefs.SetBool(MeshInfoConstants.PreferenceKey, isDisplayVisible);
            Menu.SetChecked(MeshInfoConstants.MenuPath, isDisplayVisible);

            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Repaint();
            else
                SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!isDisplayVisible || Selection.activeGameObject == null)
                return;

            // Check if the user has selected different objects in the scene
            bool selectionChanged = HasSelectionChanged();

            if (selectionChanged)
            {
                // Force mesh info update when selection changes to show accurate data
                forceUpdate = true;
                previousSelection = Selection.gameObjects;
            }

            // Update mesh information based on time interval or forced update conditions
            if (ShouldUpdateMeshInfo())
            {
                CalculateMeshInfo();
                lastUpdateTime = EditorApplication.timeSinceStartup;
                forceUpdate = false;
            }

            DrawMeshInfoGUI();
        }

        private static bool HasSelectionChanged()
        {
            var currentSelection = Selection.gameObjects;

            // No change if both previous and current selections are empty
            if (previousSelection == null && currentSelection.Length == 0)
                return false;

            // Selection changed if array lengths differ or previous selection was null
            if (previousSelection == null || previousSelection.Length != currentSelection.Length)
                return true;

            // Compare objects in sequence to detect any changes in selection order or content
            return !previousSelection.SequenceEqual(currentSelection);
        }

        private static bool ShouldUpdateMeshInfo()
        {
            // Always update when explicitly requested (e.g., selection change)
            if (forceUpdate)
                return true;

            // Update at regular intervals to reflect dynamic changes in mesh data
            double currentTime = EditorApplication.timeSinceStartup;
            return (currentTime - lastUpdateTime) >= MeshInfoConstants.UpdateIntervalSeconds;
        }

        private static void DrawMeshInfoGUI()
        {
#if NDMF_INSTALLED
            renderer.DrawMeshInfo(currentData, originalData,
                ndmfIntegration.IsShowingProxyInfo, ndmfIntegration.HasProxyInSelection);
#else
            renderer.DrawMeshInfo(currentData);
#endif
        }

        private static void CalculateMeshInfo()
        {
#if NDMF_INSTALLED
            currentData = ndmfIntegration.CalculateWithProxy(Selection.gameObjects, out originalData);
#else
            currentData = calculator.CalculateMeshInfo(Selection.gameObjects);
            originalData = null;
#endif
        }

        private static void OnEditorQuitting()
        {
            renderer?.Dispose();
        }
    }
}
