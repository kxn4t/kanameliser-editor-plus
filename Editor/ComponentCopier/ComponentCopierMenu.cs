using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Context menu entries that open the Component Copier with the clicked object already filled in.
    /// The window itself stays the single place where the copy is set up and confirmed.
    /// </summary>
    internal static class ComponentCopierMenu
    {
        private const string HierarchyMenu = "GameObject/Kanameliser Editor Plus/Component Copier/";
        private const string AssetMenu = "Assets/Kanameliser Editor Plus/Component Copier/";
        private const string ComponentMenu = "CONTEXT/Component/Kanameliser Editor Plus/Copy with Component Copier";

        // Next to the entries of Modular Avatar in the Hierarchy context menu
        private const int HierarchyPriority = 49;

        [MenuItem(HierarchyMenu + "Use as Source", false, HierarchyPriority)]
        private static void UseSelectionAsSource() => ComponentCopierWindow.Open(source: Selection.activeGameObject);

        [MenuItem(HierarchyMenu + "Use as Target", false, HierarchyPriority)]
        private static void UseSelectionAsTarget() => ComponentCopierWindow.Open(target: Selection.activeGameObject);

        // GameObject menu items run once per selected object, so they are limited to a single selection
        [MenuItem(HierarchyMenu + "Use as Source", true)]
        [MenuItem(HierarchyMenu + "Use as Target", true)]
        private static bool ValidateSingleSelection() => Selection.gameObjects.Length == 1;

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
    }
}
