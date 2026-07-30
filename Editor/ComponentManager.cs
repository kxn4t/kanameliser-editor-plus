using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// ComponentManager: lists components of the specified object and its children, allowing bulk removal
    /// </summary>
    public class ComponentManager : EditorWindow
    {
        private GameObject targetObject;
        private string gameObjectFilter = "";
        private string componentFilter = "";
        private Vector2 scrollPosition;
        private bool showEmptyObjects = false;
        private bool searchInPaths = false;
        private bool showAllComponentsOnMatch = false;

        // Data management and UI rendering helpers
        private ComponentDataManager dataManager;
        private ComponentUIRenderer uiRenderer;

        [MenuItem("Tools/Kanameliser Editor Plus/Component Manager")]
        public static void ShowWindow()
        {
            GetWindow<ComponentManager>("Component Manager");
        }

        private void OnEnable()
        {
            dataManager = new ComponentDataManager();
            uiRenderer = new ComponentUIRenderer(dataManager);
            Localization.RegisterLanguageChangeCallback(this, w => w.Repaint());
        }

        // Exception policy:
        // Scope                                            | Policy
        // OnGUI                                            | Log non-ExitGUI exceptions; let ExitGUIException propagate.
        // RefreshComponentsList / CalculateGameObjectPath  | Preserve operation-specific fallback and log.
        // RemoveSelectedComponents item loops               | Record failures and continue with remaining deletions.
        // All other Component Manager operations            | Propagate to OnGUI or Unity for visibility.

        private void OnGUI()
        {
            try
            {
                // Adjust column widths based on the window width
                uiRenderer.AdjustColumnWidths(EditorGUIUtility.currentViewWidth);

                Localization.ShowLanguageUI();

                GUILayout.Label("Component Manager", EditorStyles.boldLabel);

                EditorGUILayout.Space();

                // Draw the target object field
                GameObject previousTarget = targetObject;
                targetObject = uiRenderer.DrawTargetObjectField(targetObject, dataManager);

                // Refresh the list when the target object changes
                if (previousTarget != targetObject && targetObject != null)
                {
                    dataManager.RefreshComponentsList(targetObject);
                }

                EditorGUILayout.Space();

                // Draw the filter fields
                var (newGameObjectFilter, newComponentFilter, newSearchInPaths, newShowAllComponentsOnMatch, newShowEmptyObjects) =
                    uiRenderer.DrawFilters(gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch, showEmptyObjects);

                // Apply filter setting changes
                bool filtersChanged = newGameObjectFilter != gameObjectFilter ||
                                     newComponentFilter != componentFilter ||
                                     newSearchInPaths != searchInPaths ||
                                     newShowAllComponentsOnMatch != showAllComponentsOnMatch;

                bool emptyObjectsSettingChanged = newShowEmptyObjects != showEmptyObjects;

                if (filtersChanged || emptyObjectsSettingChanged)
                {
                    gameObjectFilter = newGameObjectFilter;
                    componentFilter = newComponentFilter;
                    searchInPaths = newSearchInPaths;
                    showAllComponentsOnMatch = newShowAllComponentsOnMatch;
                    showEmptyObjects = newShowEmptyObjects;

                    // Refresh the list when showEmptyObjects changed
                    if (emptyObjectsSettingChanged && targetObject != null)
                    {
                        dataManager.RefreshComponentsList(targetObject);
                    }
                }

                EditorGUILayout.Space();

                if (targetObject != null && dataManager.ComponentsByGameObject.Count > 0)
                {
                    DrawTableLayout();

                    // Request a repaint on the next frame while resizing
                    if (uiRenderer.IsResizingGameObjectColumn)
                    {
                        Repaint();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(Localization.S("componentManager.selectPrompt"), MessageType.Info);
                }

                EditorGUILayout.Space();
                DrawButtons();
            }
            catch (Exception ex) when (!(ex is ExitGUIException))
            {
                Debug.LogError($"Error occurred while drawing ComponentManager GUI: {ex.Message}");
            }
        }

        private void DrawTableLayout()
        {
            // Divider above the table header
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

            // Fixed-layout header row
            Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));

            // GameObjects and components to display after filtering
            var (filteredGameObjects, filteredComponents) = dataManager.GetFilteredItems(
                targetObject, gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch);

            // Draw the table header
            uiRenderer.DrawTableHeader(totalRect, filteredGameObjects, filteredComponents);

            // Handle the resize handle
            Rect resizeHandleRect = new Rect(
                totalRect.x + ComponentConstants.CHECKBOX_WIDTH + uiRenderer.GameObjectColumnWidth,
                totalRect.y,
                ComponentConstants.RESIZE_HANDLE_WIDTH,
                totalRect.height);
            uiRenderer.HandleColumnResize(resizeHandleRect);

            // Divider below the header
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

            // Scrollable content area
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);


            // Draw the filtered GameObjects
            foreach (var gameObject in filteredGameObjects)
            {
                if (gameObject == null) continue;

                // Filtered components belonging to this GameObject
                var components = dataManager.ComponentsByGameObject[gameObject];

                // Apply component filtering via the shared method
                List<ComponentInfo> gameObjectFilteredComponents = dataManager.FilterComponentsByName(
                    components, componentFilter, showAllComponentsOnMatch);

                // Draw the combined row (GameObject and its components)
                uiRenderer.DrawCombinedRow(gameObject, gameObjectFilteredComponents, targetObject);

                // Divider
                EditorGUILayout.Space();
                Rect rect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            // Get the selected GameObjects and components
            var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

            // Check whether any GameObject or component is selected
            bool anyGameObjectSelected = selectedGameObjects.Count > 0;
            bool anyComponentSelected = selectedComponents.Count > 0;

            // Select button - enabled/disabled based on conditions
            bool canSelect = CanSelectInHierarchy() && (anyGameObjectSelected || anyComponentSelected);
            GUI.enabled = canSelect;
            if (GUILayout.Button(Localization.S("componentManager.selectInHierarchy"), GUILayout.Height(30)))
            {
                SelectInHierarchy();
            }

            // Remove button
            GUI.enabled = anyGameObjectSelected || anyComponentSelected;
            if (GUILayout.Button(Localization.S("componentManager.removeSelectedItems"), GUILayout.Height(30)))
            {
                RemoveSelectedComponents();
            }

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // Determine whether selecting in the Hierarchy is possible
        private bool CanSelectInHierarchy()
        {
            if (targetObject == null) return false;

            // Get the current prefab editing mode info
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            bool isPrefabMode = prefabStage != null;

            // Case 1: the object is a scene object (an object in the Hierarchy)
            bool isSceneObject = !PrefabUtility.IsPartOfPrefabAsset(targetObject) &&
                               !EditorUtility.IsPersistent(targetObject);

            // Case 2: a prefab asset matching the prefab open in prefab editing mode was specified from Assets
            bool isPrefabAssetMatchingCurrentStage = false;
            if (isPrefabMode && PrefabUtility.IsPartOfPrefabAsset(targetObject))
            {
                // Check whether the prefab asset being edited matches the target object's prefab asset
                GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabStage.prefabContentsRoot);
                GameObject targetPrefabAsset = targetObject;
                isPrefabAssetMatchingCurrentStage = (prefabAsset == targetPrefabAsset);
            }

            // Selectable when either condition is met
            return isSceneObject || isPrefabAssetMatchingCurrentStage;
        }

        /// <summary>
        /// Gets the corresponding GameObject inside prefab editing mode
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, UnityEditor.SceneManagement.PrefabStage prefabStage)
        {
            if (prefabStage == null || !PrefabUtility.IsPartOfPrefabAsset(targetObject))
                return gameObject;

            string relativePath = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
            Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(relativePath);

            return childTransform != null ? childTransform.gameObject : gameObject;
        }

        // Select objects in the Hierarchy
        private void SelectInHierarchy()
        {
            if (targetObject == null) return;

            // Get the selected GameObjects and components
            var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

            // Objects to select
            List<UnityEngine.Object> objectsToSelect = new List<UnityEngine.Object>();

            // Get the current prefab editing mode info
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

            // Collect the checked GameObjects
            foreach (var gameObject in selectedGameObjects)
            {
                if (gameObject == null) continue;

                // In prefab editing mode, get the corresponding object inside the prefab stage
                GameObject objectToAdd = GetCorrespondingPrefabModeObject(gameObject, prefabStage);
                objectsToSelect.Add(objectToAdd);
            }

            // Check component selection states and add their GameObjects
            foreach (var compInfo in selectedComponents)
            {
                if (compInfo == null || compInfo.GameObject == null) continue;

                GameObject gameObject = compInfo.GameObject;
                if (!selectedGameObjects.Contains(gameObject)) // Only when the GameObject itself is not selected
                {
                    // In prefab editing mode, get the corresponding object inside the prefab stage
                    GameObject objectToAdd = GetCorrespondingPrefabModeObject(gameObject, prefabStage);
                    objectsToSelect.Add(objectToAdd);
                }
            }

            // Remove duplicates
            objectsToSelect = objectsToSelect.Distinct().ToList();

            // Select in the Hierarchy
            if (objectsToSelect.Count > 0)
            {
                Selection.objects = objectsToSelect.ToArray();
            }
        }

        /// <summary>
        /// Builds the deletion confirmation message for the selected items
        /// </summary>
        private string CreateDeleteConfirmMessage(List<GameObject> selectedGameObjects, List<ComponentInfo> selectedComponents)
        {
            string confirmMessage = Localization.S("componentManager.confirmDeletion.header") + "\n\n";

            // Append the list of selected GameObjects
            if (selectedGameObjects.Count > 0)
            {
                confirmMessage += Localization.S("componentManager.confirmDeletion.selectedGameObjects") + "\n";
                for (int i = 0; i < selectedGameObjects.Count; i++)
                {
                    if (selectedGameObjects[i] == null) continue;

                    if (i < 10 || i == selectedGameObjects.Count - 1) // Show the first 10 items and the last one
                    {
                        confirmMessage += "- " + ComponentPathUtility.GetGameObjectPath(selectedGameObjects[i], targetObject) + "\n";
                    }
                    else if (i == 10) // Ellipsis
                    {
                        confirmMessage += Localization.S("componentManager.confirmDeletion.moreGameObjects", selectedGameObjects.Count - 10) + "\n";
                        break;
                    }
                }
                confirmMessage += "\n";
            }

            // Append the list of selected components
            if (selectedComponents.Count > 0)
            {
                confirmMessage += Localization.S("componentManager.confirmDeletion.selectedComponents") + "\n";
                for (int i = 0; i < selectedComponents.Count; i++)
                {
                    if (selectedComponents[i] == null || selectedComponents[i].GameObject == null) continue;

                    if (i < 10 || i == selectedComponents.Count - 1) // Show the first 10 items and the last one
                    {
                        var comp = selectedComponents[i];
                        confirmMessage += "- " + ComponentPathUtility.GetGameObjectPath(comp.GameObject, targetObject) + " : " + comp.Name + "\n";
                    }
                    else if (i == 10) // Ellipsis
                    {
                        confirmMessage += Localization.S("componentManager.confirmDeletion.moreComponents", selectedComponents.Count - 10) + "\n";
                        break;
                    }
                }
            }

            confirmMessage += "\n" + Localization.S("componentManager.confirmDeletion.undoAvailable");
            return confirmMessage;
        }

        private void RemoveSelectedComponents()
        {
            try
            {
                // Get the selected GameObjects and components
                var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

                if (selectedGameObjects.Count == 0 && selectedComponents.Count == 0) return;

                // Build the message shown in the confirmation dialog
                string confirmMessage = CreateDeleteConfirmMessage(selectedGameObjects, selectedComponents);

                // Show the confirmation dialog
                bool confirmResult = EditorUtility.DisplayDialog(
                    Localization.S("componentManager.confirmDeletion"),
                    confirmMessage,
                    Localization.S("common.delete"),
                    Localization.S("common.cancel")
                );

                // Proceed only when the user chose to delete
                if (confirmResult)
                {
                    // Group the operations into a single Undo step
                    Undo.SetCurrentGroupName("Remove GameObjects and Components");
                    int undoGroup = Undo.GetCurrentGroup();
                    List<string> failedItems = new List<string>();

                    try
                    {
                        // Delete the selected GameObjects
                        foreach (var gameObject in selectedGameObjects)
                        {
                            if (gameObject == null) continue;

                            string gameObjectName = gameObject.name;
                            try
                            {
                                Undo.DestroyObjectImmediate(gameObject);
                            }
                            catch (Exception ex)
                            {
                                failedItems.Add($"GameObject: {gameObjectName}");
                                Debug.LogWarning($"Failed to delete GameObject '{gameObjectName}': {ex.Message}");
                            }
                        }

                        // Delete the selected components
                        foreach (var componentInfo in selectedComponents)
                        {
                            if (componentInfo == null || componentInfo.Component == null) continue;

                            Component component = componentInfo.Component;
                            string componentName = componentInfo.Name;
                            try
                            {
                                Undo.DestroyObjectImmediate(component);
                            }
                            catch (Exception ex)
                            {
                                failedItems.Add($"Component: {componentName}");
                                Debug.LogWarning($"Failed to delete component '{componentName}': {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            Undo.CollapseUndoOperations(undoGroup);
                        }
                        finally
                        {
                            dataManager.RefreshComponentsList(targetObject);
                        }
                    }

                    if (failedItems.Count > 0)
                    {
                        EditorUtility.DisplayDialog(
                            Localization.S("componentManager.deletionPartiallyCompleted"),
                            Localization.S("componentManager.deletionPartiallyCompleted.message", string.Join("\n", failedItems)),
                            Localization.S("common.ok")
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while removing components: {ex.Message}");
            }
        }
    }
}
