using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Handles UI rendering
    /// </summary>
    public class ComponentUIRenderer
    {
        private ComponentDataManager dataManager;
        private float gameObjectColumnWidth = 250f;
        private float componentColumnWidth = 250f;
        private bool isResizingGameObjectColumn = false;
        private float totalWidth = 0f;

        // Style definitions
        private GUIStyle headerLabelStyle;
        private GUIStyle pathLabelStyle;
        private GUIStyle componentLabelStyle;
        private GUIStyle dividerStyle;

        public float GameObjectColumnWidth => gameObjectColumnWidth;
        public float ComponentColumnWidth => componentColumnWidth;
        public bool IsResizingGameObjectColumn { get => isResizingGameObjectColumn; set => isResizingGameObjectColumn = value; }

        public ComponentUIRenderer(ComponentDataManager dataManager)
        {
            this.dataManager = dataManager;
            InitializeStyles();
        }

        /// <summary>
        /// Initializes the UI styles
        /// </summary>
        private void InitializeStyles()
        {
            headerLabelStyle = new GUIStyle(EditorStyles.boldLabel);
            headerLabelStyle.alignment = TextAnchor.MiddleLeft;

            pathLabelStyle = new GUIStyle(EditorStyles.miniLabel);
            pathLabelStyle.wordWrap = true;

            componentLabelStyle = new GUIStyle(EditorStyles.label);
            componentLabelStyle.alignment = TextAnchor.MiddleLeft;

            dividerStyle = new GUIStyle();
            dividerStyle.normal.background = EditorGUIUtility.whiteTexture;
        }

        /// <summary>
        /// Adjusts column widths based on the window width
        /// </summary>
        public void AdjustColumnWidths(float windowWidth)
        {
            totalWidth = windowWidth;

            // Subtract the checkbox and resize handle widths from the total available width
            float availableWidth = totalWidth - (ComponentConstants.CHECKBOX_WIDTH * 2) - ComponentConstants.RESIZE_HANDLE_WIDTH - ComponentConstants.COLUMN_MARGIN;

            // Keep both the GameObject and Component columns at or above the minimum width
            float totalMinWidth = ComponentConstants.MIN_COLUMN_WIDTH * 2;
            if (availableWidth < totalMinWidth)
            {
                // Distribute evenly when the available width is insufficient
                gameObjectColumnWidth = availableWidth / 2;
                componentColumnWidth = availableWidth / 2;
            }
            else
            {
                // Cap the GameObject column width at 60% of the total width
                float maxGameObjectWidth = availableWidth * ComponentConstants.MAX_COLUMN_RATIO;
                gameObjectColumnWidth = Mathf.Min(gameObjectColumnWidth, maxGameObjectWidth);

                // Keep the GameObject column at or above the minimum width
                gameObjectColumnWidth = Mathf.Max(gameObjectColumnWidth, ComponentConstants.MIN_COLUMN_WIDTH);

                // Adjust componentColumnWidth based on the GameObject column width (ensuring the minimum width)
                componentColumnWidth = Mathf.Max(availableWidth - gameObjectColumnWidth, ComponentConstants.MIN_COLUMN_WIDTH);

                // Adjust when both column widths together exceed the available width
                if (gameObjectColumnWidth + componentColumnWidth > availableWidth)
                {
                    // Adjust while preserving the ratio
                    float ratio = gameObjectColumnWidth / (gameObjectColumnWidth + componentColumnWidth);
                    gameObjectColumnWidth = availableWidth * ratio;
                    componentColumnWidth = availableWidth * (1 - ratio);
                }
            }
        }

        /// <summary>
        /// Draws the table header
        /// </summary>
        public void DrawTableHeader(Rect totalRect, List<GameObject> filteredGameObjects, List<ComponentInfo> filteredComponents)
        {
            float startX = totalRect.x;

            // GameObject checkbox column
            Rect checkboxRect1 = new Rect(startX, totalRect.y, ComponentConstants.CHECKBOX_WIDTH, totalRect.height);
            bool allGameObjectsSelected = filteredGameObjects.Count > 0 &&
                                         filteredGameObjects.All(go => dataManager.GameObjectSelectionState[go]);
            bool mixedGameObjectSelection = !allGameObjectsSelected &&
                                          filteredGameObjects.Any(go => dataManager.GameObjectSelectionState[go]);
            EditorGUI.showMixedValue = mixedGameObjectSelection;
            bool newAllGameObjectsSelected = EditorGUI.Toggle(checkboxRect1, allGameObjectsSelected);
            EditorGUI.showMixedValue = false;

            if (newAllGameObjectsSelected != allGameObjectsSelected)
            {
                // Always clear all when clicked in a mixed state
                bool newState = newAllGameObjectsSelected;
                if (mixedGameObjectSelection)
                {
                    newState = false;
                }

                // Update selection state for GameObjects only (components are not linked)
                foreach (var go in filteredGameObjects)
                {
                    dataManager.GameObjectSelectionState[go] = newState;
                }
            }

            // GameObject header
            Rect gameObjectHeaderRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y, gameObjectColumnWidth, totalRect.height);
            EditorGUI.LabelField(gameObjectHeaderRect, "GameObject", headerLabelStyle);

            // Resize handle
            Rect resizeHandleRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth, totalRect.y, ComponentConstants.RESIZE_HANDLE_WIDTH, totalRect.height);
            EditorGUI.LabelField(resizeHandleRect, "|", EditorStyles.boldLabel);
            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

            // Component checkbox column
            Rect checkboxRect2 = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth + ComponentConstants.RESIZE_HANDLE_WIDTH, totalRect.y, ComponentConstants.CHECKBOX_WIDTH, totalRect.height);
            bool allComponentsSelected = filteredComponents.Count > 0 &&
                                        filteredComponents.All(c => c.IsSelected);
            bool mixedComponentSelection = !allComponentsSelected &&
                                         filteredComponents.Any(c => c.IsSelected);
            EditorGUI.showMixedValue = mixedComponentSelection;
            bool newAllComponentsSelected = EditorGUI.Toggle(checkboxRect2, allComponentsSelected);
            EditorGUI.showMixedValue = false;

            if (newAllComponentsSelected != allComponentsSelected)
            {
                // Always clear all when clicked in a mixed state
                bool newState = newAllComponentsSelected;
                if (mixedComponentSelection)
                {
                    newState = false;
                }

                // Update only the items visible after filtering
                foreach (var comp in filteredComponents)
                {
                    comp.IsSelected = newState;
                }
            }

            // Component header
            Rect componentHeaderRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth + ComponentConstants.RESIZE_HANDLE_WIDTH + ComponentConstants.CHECKBOX_WIDTH,
                                              totalRect.y, componentColumnWidth, totalRect.height);
            EditorGUI.LabelField(componentHeaderRect, "Component", headerLabelStyle);
        }

        /// <summary>
        /// Draws a row for a GameObject and its components
        /// </summary>
        public void DrawCombinedRow(GameObject gameObj, List<ComponentInfo> components, GameObject targetObject)
        {
            if (gameObj == null) return;

            // Calculate the row height (based on the GameObject plus the component count)
            int componentCount = components.Count;
            int calculatedRowHeight = Mathf.Max((int)ComponentConstants.MIN_ROW_HEIGHT, (int)(ComponentConstants.ROW_HEIGHT + componentCount * ComponentConstants.ROW_HEIGHT)); // Minimum height is 36px

            Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(calculatedRowHeight));
            float startX = totalRect.x;

            // GameObject checkbox - aligned with the GameObject name
            Rect goCheckboxRect = new Rect(startX, totalRect.y, ComponentConstants.CHECKBOX_WIDTH, ComponentConstants.ROW_HEIGHT);
            bool isGameObjectSelected = dataManager.GameObjectSelectionState[gameObj];

            // Use EditorGUI.Toggle to detect state changes
            bool newIsGameObjectSelected = EditorGUI.Toggle(goCheckboxRect, isGameObjectSelected);
            if (newIsGameObjectSelected != isGameObjectSelected)
            {
                dataManager.GameObjectSelectionState[gameObj] = newIsGameObjectSelected;
            }

            // GameObject name
            Rect nameRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y, gameObjectColumnWidth, ComponentConstants.ROW_HEIGHT);
            EditorGUI.LabelField(nameRect, gameObj.name, headerLabelStyle);

            // Make the GameObject name clickable
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && nameRect.Contains(evt.mousePosition))
            {
                Event evtCopy = new Event(evt);
                evt.Use();
                SelectGameObjectInHierarchy(gameObj, targetObject);
                EditorWindow.GetWindow<ComponentManager>().Repaint();
            }

            // GameObject path
            Rect pathRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y + 18, gameObjectColumnWidth, 16);
            string path = ComponentPathUtility.GetGameObjectPath(gameObj, targetObject);
            EditorGUI.LabelField(pathRect, path, pathLabelStyle);

            // Make the path clickable as well
            if (evt.type == EventType.MouseDown && pathRect.Contains(evt.mousePosition))
            {
                Event evtCopy = new Event(evt);
                evt.Use();
                SelectGameObjectInHierarchy(gameObj, targetObject);
                EditorWindow.GetWindow<ComponentManager>().Repaint();
            }

            // Component area
            float componentStartX = startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth + ComponentConstants.RESIZE_HANDLE_WIDTH;

            // Draw each component (stacked vertically without indentation)
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null) continue;

                float yOffset = i * ComponentConstants.ROW_HEIGHT;

                // Component checkbox
                Rect compCheckboxRect = new Rect(componentStartX, totalRect.y + yOffset, ComponentConstants.CHECKBOX_WIDTH, ComponentConstants.ROW_HEIGHT);
                bool isCompSelected = component.IsSelected;

                // Use EditorGUI.Toggle to detect state changes
                bool newIsCompSelected = EditorGUI.Toggle(compCheckboxRect, isCompSelected);
                if (newIsCompSelected != isCompSelected)
                {
                    component.IsSelected = newIsCompSelected;
                }

                // Component icon
                GUIContent content = component.Component != null
                    ? EditorGUIUtility.ObjectContent(component.Component, component.Component.GetType())
                    : new GUIContent("Missing");

                Rect iconRect = new Rect(componentStartX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y + yOffset, ComponentConstants.ICON_WIDTH, ComponentConstants.ROW_HEIGHT);
                EditorGUI.LabelField(iconRect, new GUIContent(content.image));

                // Component name
                Rect compLabelRect = new Rect(componentStartX + ComponentConstants.CHECKBOX_WIDTH + ComponentConstants.ICON_WIDTH, totalRect.y + yOffset,
                                   componentColumnWidth - ComponentConstants.ICON_WIDTH, ComponentConstants.ROW_HEIGHT);
                EditorGUI.LabelField(compLabelRect, component.Name, componentLabelStyle);
            }
        }

        /// <summary>
        /// Handles the column resize handle
        /// </summary>
        public void HandleColumnResize(Rect resizeHandleRect)
        {
            Event evt = Event.current;

            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

            // Handle the resize handle separately per event type
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && resizeHandleRect.Contains(evt.mousePosition))
                    {
                        isResizingGameObjectColumn = true;
                        evt.Use();
                        GUI.changed = true;
                    }
                    break;

                case EventType.MouseDrag:
                    if (evt.button == 0 && isResizingGameObjectColumn)
                    {
                        UpdateColumnWidths(evt.delta.x);
                        evt.Use();
                        GUI.changed = true;
                        EditorWindow.GetWindow<ComponentManager>().Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (evt.button == 0 && isResizingGameObjectColumn)
                    {
                        isResizingGameObjectColumn = false;
                        evt.Use();
                        GUI.changed = true;
                        EditorWindow.GetWindow<ComponentManager>().Repaint();
                    }
                    break;

                case EventType.MouseMove:
                    if (resizeHandleRect.Contains(evt.mousePosition))
                    {
                        EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);
                    }
                    break;
            }
        }

        /// <summary>
        /// Updates the column widths
        /// </summary>
        private void UpdateColumnWidths(float deltaX)
        {
            float availableWidth = totalWidth - (ComponentConstants.CHECKBOX_WIDTH * 2) - ComponentConstants.RESIZE_HANDLE_WIDTH - ComponentConstants.COLUMN_MARGIN;

            // Calculate the new width
            float newWidth = gameObjectColumnWidth + deltaX;

            // Ensure the minimum width while capping the maximum at 60% of the window
            float maxAllowed = Mathf.Max(availableWidth * ComponentConstants.MAX_COLUMN_RATIO, ComponentConstants.MIN_COLUMN_WIDTH);
            newWidth = Mathf.Clamp(newWidth, ComponentConstants.MIN_COLUMN_WIDTH, maxAllowed);

            // Keep the component column from going below the minimum width as well
            float remainingWidth = availableWidth - newWidth;
            if (remainingWidth < ComponentConstants.MIN_COLUMN_WIDTH)
            {
                newWidth = availableWidth - ComponentConstants.MIN_COLUMN_WIDTH;
            }

            // Update the layout when changed
            if (newWidth != gameObjectColumnWidth)
            {
                gameObjectColumnWidth = newWidth;
                componentColumnWidth = availableWidth - gameObjectColumnWidth;
            }
        }

        /// <summary>
        /// Selects the GameObject in the Hierarchy
        /// </summary>
        private void SelectGameObjectInHierarchy(GameObject gameObj, GameObject targetObject)
        {
            if (gameObj == null) return;

            // Get the current prefab editing mode info
            var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

            // In prefab editing mode, get the corresponding object inside the prefab stage
            GameObject objectToSelect = GetCorrespondingPrefabModeObject(gameObj, prefabStage, targetObject);

            // Select in the Hierarchy
            Selection.activeObject = objectToSelect;

            // Move editor focus to the Hierarchy view
            // Try to get the SceneHierarchyWindow first
            var sceneHierarchyWindowType = System.Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor");
            if (sceneHierarchyWindowType != null)
            {
                var hierarchyWindow = EditorWindow.GetWindow(sceneHierarchyWindowType);
                if (hierarchyWindow != null)
                {
                    hierarchyWindow.Focus();
                    return;
                }
            }

            // Fallback
            EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
        }

        /// <summary>
        /// Gets the corresponding GameObject inside prefab editing mode
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, UnityEditor.SceneManagement.PrefabStage prefabStage, GameObject targetObject)
        {
            if (prefabStage == null)
                return gameObject;

            // Check the correspondence directly via instance IDs
            if (PrefabUtility.IsPartOfPrefabInstance(prefabStage.prefabContentsRoot) &&
                PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                // The prefab asset being edited and the target object may belong to the same prefab hierarchy
                GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabStage.prefabContentsRoot);
                GameObject gameObjectPrefabAsset = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);

                if (prefabAsset == gameObjectPrefabAsset)
                {
                    // Part of the same prefab, so find the corresponding object via the relative path
                    string relativePath = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(relativePath);
                        if (childTransform != null)
                            return childTransform.gameObject;
                    }
                }
            }

            // Fallback via path resolution
            string path = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
            if (!string.IsNullOrEmpty(path) && path != ".")
            {
                Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(path);
                if (childTransform != null)
                    return childTransform.gameObject;
            }

            // Search for an object with the same name (last resort)
            if (gameObject != targetObject)
            {
                // Depth-first search for the GameObject within the prefab stage
                return FindMatchingGameObjectInPrefab(gameObject.name, prefabStage.prefabContentsRoot);
            }

            return gameObject;
        }

        /// <summary>
        /// Finds a matching GameObject in the prefab by name
        /// </summary>
        private GameObject FindMatchingGameObjectInPrefab(string objectName, GameObject root)
        {
            if (root.name == objectName)
                return root;

            // Search child objects recursively
            foreach (Transform child in root.transform)
            {
                GameObject result = FindMatchingGameObjectInPrefab(objectName, child.gameObject);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Draws the filter fields
        /// </summary>
        public (string gameObjectFilter, string componentFilter, bool searchInPaths, bool showAllComponentsOnMatch, bool showEmptyObjects)
            DrawFilters(string gameObjectFilter, string componentFilter, bool searchInPaths, bool showAllComponentsOnMatch, bool showEmptyObjects)
        {
            // Filter fields
            EditorGUILayout.BeginHorizontal();

            // Spacing for the checkbox column
            GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH);

            // GameObject filter (uses the same width as the GameObject column, with a minimum width)
            EditorGUILayout.BeginVertical(GUILayout.Width(gameObjectColumnWidth), GUILayout.MinWidth(ComponentConstants.MIN_COLUMN_WIDTH));
            EditorGUILayout.LabelField(Localization.S("componentManager.gameObjectFilter"), headerLabelStyle);
            string newGameObjectFilter = EditorGUILayout.TextField(gameObjectFilter);

            // GameObject filter options
            bool newSearchInPaths = EditorGUILayout.ToggleLeft(" " + Localization.S("componentManager.includePathsInSearch"), searchInPaths);

            EditorGUILayout.EndVertical();

            // Spacing for the resize handle
            GUILayout.Space(ComponentConstants.RESIZE_HANDLE_WIDTH);

            // Spacing for the checkbox column
            GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH);

            // Component filter (uses the remaining width, with a minimum width)
            EditorGUILayout.BeginVertical(GUILayout.Width(componentColumnWidth), GUILayout.MinWidth(ComponentConstants.MIN_COLUMN_WIDTH));
            EditorGUILayout.LabelField(Localization.S("componentManager.componentFilter"), headerLabelStyle);
            string newComponentFilter = EditorGUILayout.TextField(componentFilter);

            // Component filter options
            bool newShowAllComponentsOnMatch = EditorGUILayout.ToggleLeft(" " + Localization.S("componentManager.showAllComponentsOnMatch"), showAllComponentsOnMatch);

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Additional options (laid out horizontally)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH); // Match the left margin

            // Option to also show GameObjects without components
            bool newShowEmptyObjects = EditorGUILayout.ToggleLeft(" " + Localization.S("componentManager.showEmptyObjects"), showEmptyObjects);
            if (newShowEmptyObjects != showEmptyObjects)
            {
                dataManager.ShowEmptyObjects = newShowEmptyObjects;
                showEmptyObjects = newShowEmptyObjects;
            }
            else
            {
                // Sync in case the value differs from dataManager's
                showEmptyObjects = dataManager.ShowEmptyObjects;
            }

            EditorGUILayout.EndHorizontal();

            return (newGameObjectFilter, newComponentFilter, newSearchInPaths, newShowAllComponentsOnMatch, showEmptyObjects);
        }

        /// <summary>
        /// Draws the target object field
        /// </summary>
        public GameObject DrawTargetObjectField(GameObject targetObject, ComponentDataManager dataManager)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(Localization.S("componentManager.targetObject"), GUILayout.Width(EditorGUIUtility.labelWidth - 30));

            // Refresh button (after the label, before the field)
            if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(30), GUILayout.Height(18)))
            {
                if (targetObject != null)
                {
                    // Save the current selection states
                    Dictionary<GameObject, bool> savedGameObjectSelectionState = new Dictionary<GameObject, bool>(dataManager.GameObjectSelectionState);
                    Dictionary<Component, bool> savedComponentSelectionState = new Dictionary<Component, bool>();

                    // Save the selection state of each component
                    foreach (var entry in dataManager.ComponentsByGameObject)
                    {
                        foreach (var compInfo in entry.Value)
                        {
                            if (compInfo.Component != null)
                            {
                                savedComponentSelectionState[compInfo.Component] = compInfo.IsSelected;
                            }
                        }
                    }

                    dataManager.RefreshComponentsList(targetObject);

                    // Restore GameObject selection states
                    foreach (var gameObject in dataManager.GameObjectSelectionState.Keys.ToList())
                    {
                        if (savedGameObjectSelectionState.ContainsKey(gameObject))
                        {
                            dataManager.GameObjectSelectionState[gameObject] = savedGameObjectSelectionState[gameObject];
                        }
                    }

                    // Restore component selection states
                    foreach (var entry in dataManager.ComponentsByGameObject)
                    {
                        foreach (var compInfo in entry.Value)
                        {
                            if (compInfo.Component != null && savedComponentSelectionState.ContainsKey(compInfo.Component))
                            {
                                compInfo.IsSelected = savedComponentSelectionState[compInfo.Component];
                            }
                        }
                    }
                }
            }

            // Object field
            EditorGUI.BeginChangeCheck();
            GameObject newTargetObject = EditorGUILayout.ObjectField("", targetObject, typeof(GameObject), true) as GameObject;
            GameObject resultTargetObject = targetObject;
            if (EditorGUI.EndChangeCheck() && newTargetObject != null)
            {
                dataManager.RefreshComponentsList(newTargetObject);
                resultTargetObject = newTargetObject;
            }

            EditorGUILayout.EndHorizontal();
            return resultTargetObject;
        }
    }

}
