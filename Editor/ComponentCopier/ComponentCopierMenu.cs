using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Context menu entries that open the Component Copier with the clicked object already filled in, or the small
    /// "Copy to Other Side" window for that object. The windows stay the place where a copy is set up and confirmed.
    /// </summary>
    internal static class ComponentCopierMenu
    {
        private const string HierarchyMenu = "GameObject/Kanameliser Editor Plus/Component Copier/";
        private const string AssetMenu = "Assets/Kanameliser Editor Plus/Component Copier/";
        private const string ComponentMenu = "CONTEXT/Component/Kanameliser Editor Plus/Copy with Component Copier";

        // Every component header has it, the Transform's included
        private const string OtherSideComponentMenu = "CONTEXT/Component/Kanameliser Editor Plus/Copy to Other Side";

        // Next to the entries of Modular Avatar in the Hierarchy context menu
        private const int HierarchyPriority = 49;

        [MenuItem(HierarchyMenu + "Use as Source", false, HierarchyPriority)]
        private static void UseSelectionAsSource() => ComponentCopierWindow.Open(source: Selection.activeGameObject);

        [MenuItem(HierarchyMenu + "Use as Target", false, HierarchyPriority)]
        private static void UseSelectionAsTarget() => ComponentCopierWindow.Open(target: Selection.activeGameObject);

        // The one selected GameObject: the active object may be another kind of object, or none
        [MenuItem(HierarchyMenu + "Copy to Other Side", false, HierarchyPriority)]
        private static void CopySelectionToOtherSide() => CopyToOtherSideWindow.Open(Selection.gameObjects[0].transform);

        // GameObject menu items run once per selected object, so they are limited to a single selection
        [MenuItem(HierarchyMenu + "Use as Source", true)]
        [MenuItem(HierarchyMenu + "Use as Target", true)]
        private static bool ValidateSingleSelection() => Selection.gameObjects.Length == 1;

        // The copy is written to the object's own hierarchy, which an asset cannot take with Undo
        [MenuItem(HierarchyMenu + "Copy to Other Side", true)]
        private static bool ValidateCopySelectionToOtherSide() =>
            Selection.gameObjects.Length == 1 && !EditorUtility.IsPersistent(Selection.gameObjects[0]);

        [MenuItem(AssetMenu + "Use as Source")]
        private static void UseAssetAsSource() => ComponentCopierWindow.Open(source: Selection.activeGameObject);

        // Only prefab assets can be scanned; folders, models and other assets are not GameObjects
        [MenuItem(AssetMenu + "Use as Source", true)]
        private static bool ValidateAssetSource() =>
            Selection.objects.Length == 1 && Selection.activeObject is GameObject go && EditorUtility.IsPersistent(go);

        [MenuItem(ComponentMenu)]
        private static void CopyComponent(MenuCommand command)
        {
            var component = (Component)command.context;
            ComponentCopierWindow.Open(source: component.gameObject, only: component);
        }

        [MenuItem(ComponentMenu, true)]
        private static bool ValidateCopyComponent(MenuCommand command) =>
            command.context is Component component && component is not Transform;

        [MenuItem(OtherSideComponentMenu)]
        private static void CopyComponentToOtherSide(MenuCommand command)
        {
            var component = (Component)command.context;
            CopyToOtherSideWindow.Open(component.transform, component);
        }

        // With several objects in the Inspector the item runs once for each, and the window only takes one of them
        [MenuItem(OtherSideComponentMenu, true)]
        private static bool ValidateCopyComponentToOtherSide(MenuCommand command) =>
            command.context is Component component && !EditorUtility.IsPersistent(component) &&
            Selection.gameObjects.Length <= 1;
    }
}
