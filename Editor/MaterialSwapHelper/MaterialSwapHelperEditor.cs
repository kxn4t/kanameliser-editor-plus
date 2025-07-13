using UnityEditor;
using UnityEngine;

namespace Kanameliser.Editor.MaterialSwapHelper
{
    /// <summary>
    /// Unity Editor menu commands for Material Swap Helper functionality
    /// </summary>
    public static class MaterialSwapHelperEditor
    {
        private const string MENU_PATH_COPY = "GameObject/Kanameliser Editor Plus/Copy Material Setup";
        private const string MENU_PATH_CREATE = "GameObject/Kanameliser Editor Plus/Create Material Swap";
        private const string MENU_PATH_CREATE_PER_OBJECT = "GameObject/Kanameliser Editor Plus/Create Material Swap (Per Object)";
        private const int MENU_PRIORITY = 100;

#if MODULAR_AVATAR_INSTALLED
        [MenuItem(MENU_PATH_COPY, false, MENU_PRIORITY)]
        public static void CopyMaterialSetup()
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0) return;

            MaterialSwapHelperUtils.TryExecute(() =>
            {
                CopiedMaterialData copiedData;

                if (selectedObjects.Length == 1)
                {
                    // Single object - use existing method
                    copiedData = MaterialSetupCopier.CopyMaterialSetup(selectedObjects[0]);
                }
                else
                {
                    // Multiple objects - use new method
                    copiedData = MaterialSetupCopier.CopyMaterialSetupFromMultiple(selectedObjects);
                }

                MaterialSwapHelperSession.StoreCopiedData(copiedData);

                string objectNames = selectedObjects.Length == 1
                    ? selectedObjects[0].name
                    : $"{selectedObjects.Length} objects";
                MaterialSwapHelperUtils.LogSuccess($"Copied material setup from {objectNames} ({copiedData.materialSetups.Count} material setups)");
            }, "copy material setup");
        }

        [MenuItem(MENU_PATH_CREATE, false, MENU_PRIORITY + 1)]
        public static void CreateMaterialSwap()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MaterialSwapHelperSession.HasCopiedData)
            {
                MaterialSwapHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MaterialSwapHelperUtils.TryExecute(() =>
            {
                var result = MaterialSwapGenerator.CreateMaterialSwap(selected, MaterialSwapHelperSession.CopiedData);

                if (result.success)
                {
                    MaterialSwapHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MaterialSwapHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material swap");
        }

        [MenuItem(MENU_PATH_CREATE_PER_OBJECT, false, MENU_PRIORITY + 2)]
        public static void CreateMaterialSwapPerObject()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MaterialSwapHelperSession.HasCopiedData)
            {
                MaterialSwapHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MaterialSwapHelperUtils.TryExecute(() =>
            {
                var result = MaterialSwapGenerator.CreateMaterialSwapPerObject(selected, MaterialSwapHelperSession.CopiedData);

                if (result.success)
                {
                    MaterialSwapHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MaterialSwapHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material swap per object");
        }

        // Validation methods
        [MenuItem(MENU_PATH_COPY, true)]
        public static bool ValidateCopyMaterialSetup()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem(MENU_PATH_CREATE, true)]
        public static bool ValidateCreateMaterialSwap()
        {
            return Selection.activeGameObject != null && MaterialSwapHelperSession.HasCopiedData;
        }

        [MenuItem(MENU_PATH_CREATE_PER_OBJECT, true)]
        public static bool ValidateCreateMaterialSwapPerObject()
        {
            return Selection.activeGameObject != null && MaterialSwapHelperSession.HasCopiedData;
        }
#else
        // When Modular Avatar is not available, we don't show the menu items at all
        // This prevents any compilation errors and keeps the menus clean
#endif
    }
}