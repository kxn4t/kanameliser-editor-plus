using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.Editor.MAMaterialHelper.MaterialSwap;
using Kanameliser.Editor.MAMaterialHelper.MaterialSetter;

namespace Kanameliser.Editor.MAMaterialHelper
{
    /// <summary>
    /// Unity Editor menu commands for MA Material Helper functionality
    /// </summary>
    public static class MAMaterialHelperEditor
    {
        private const string MENU_PATH_COPY = "GameObject/Kanameliser Editor Plus/Copy Material Setup";
        private const string MENU_PATH_CREATE_SETTER = "GameObject/Kanameliser Editor Plus/Create Material Setter";
        private const string MENU_PATH_CREATE_SETTER_ALL_SLOTS = "GameObject/Kanameliser Editor Plus/[Optional] Create Material Setter (All Slots)";
        private const string MENU_PATH_CREATE_SWAP = "GameObject/Kanameliser Editor Plus/Create Material Swap";
        private const string MENU_PATH_CREATE_SWAP_PER_OBJECT = "GameObject/Kanameliser Editor Plus/[Optional] Create Material Swap (Per Object)";
        private const int MENU_PRIORITY = 100;

#if MODULAR_AVATAR_INSTALLED
        [MenuItem(MENU_PATH_COPY, false, MENU_PRIORITY)]
        public static void CopyMaterialSetup()
        {
            var selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0) return;

            MAMaterialHelperUtils.TryExecute(() =>
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

                MAMaterialHelperSession.StoreCopiedData(copiedData);

                string objectNames = selectedObjects.Length == 1
                    ? selectedObjects[0].name
                    : $"{selectedObjects.Length} objects";
                MAMaterialHelperUtils.LogSuccess($"Copied material setup from {objectNames} ({copiedData.materialSetups.Count} material setups)");
            }, "copy material setup");
        }

        [MenuItem(MENU_PATH_CREATE_SETTER, false, MENU_PRIORITY + 1)]
        public static void CreateMaterialSetter()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MAMaterialHelperSession.HasCopiedData)
            {
                MAMaterialHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MAMaterialHelperUtils.TryExecute(() =>
            {
                var result = MaterialSetterGenerator.CreateMaterialSetter(selected, MAMaterialHelperSession.CopiedData, skipUnchanged: true);

                if (result.success)
                {
                    MAMaterialHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MAMaterialHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material setter");
        }

        [MenuItem(MENU_PATH_CREATE_SETTER_ALL_SLOTS, false, MENU_PRIORITY + 2)]
        public static void CreateMaterialSetterAllSlots()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MAMaterialHelperSession.HasCopiedData)
            {
                MAMaterialHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MAMaterialHelperUtils.TryExecute(() =>
            {
                var result = MaterialSetterGenerator.CreateMaterialSetter(selected, MAMaterialHelperSession.CopiedData, skipUnchanged: false);

                if (result.success)
                {
                    MAMaterialHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MAMaterialHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material setter (all slots)");
        }

        [MenuItem(MENU_PATH_CREATE_SWAP, false, MENU_PRIORITY + 3)]
        public static void CreateMaterialSwap()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MAMaterialHelperSession.HasCopiedData)
            {
                MAMaterialHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MAMaterialHelperUtils.TryExecute(() =>
            {
                var result = MaterialSwapGenerator.CreateMaterialSwap(selected, MAMaterialHelperSession.CopiedData);

                if (result.success)
                {
                    MAMaterialHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MAMaterialHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material swap");
        }

        [MenuItem(MENU_PATH_CREATE_SWAP_PER_OBJECT, false, MENU_PRIORITY + 4)]
        public static void CreateMaterialSwapPerObject()
        {
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            if (!MAMaterialHelperSession.HasCopiedData)
            {
                MAMaterialHelperUtils.ShowErrorDialog("No material setup has been copied. Please copy a material setup first.");
                return;
            }

            MAMaterialHelperUtils.TryExecute(() =>
            {
                var result = MaterialSwapGenerator.CreateMaterialSwapPerObject(selected, MAMaterialHelperSession.CopiedData);

                if (result.success)
                {
                    MAMaterialHelperUtils.LogSuccess(result.message);
                }
                else
                {
                    MAMaterialHelperUtils.ShowWarningDialog(result.message);
                }
            }, "create material swap per object");
        }

        // Validation methods
        [MenuItem(MENU_PATH_COPY, true)]
        public static bool ValidateCopyMaterialSetup()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem(MENU_PATH_CREATE_SETTER, true)]
        public static bool ValidateCreateMaterialSetter()
        {
            return Selection.activeGameObject != null && MAMaterialHelperSession.HasCopiedData;
        }

        [MenuItem(MENU_PATH_CREATE_SETTER_ALL_SLOTS, true)]
        public static bool ValidateCreateMaterialSetterAllSlots()
        {
            return Selection.activeGameObject != null && MAMaterialHelperSession.HasCopiedData;
        }

        [MenuItem(MENU_PATH_CREATE_SWAP, true)]
        public static bool ValidateCreateMaterialSwap()
        {
            return Selection.activeGameObject != null && MAMaterialHelperSession.HasCopiedData;
        }

        [MenuItem(MENU_PATH_CREATE_SWAP_PER_OBJECT, true)]
        public static bool ValidateCreateMaterialSwapPerObject()
        {
            return Selection.activeGameObject != null && MAMaterialHelperSession.HasCopiedData;
        }
#else
        // When Modular Avatar is not available, we don't show the menu items at all
        // This prevents any compilation errors and keeps the menus clean
#endif
    }
}
