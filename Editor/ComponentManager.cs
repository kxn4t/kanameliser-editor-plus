using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

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
        private bool showEmptyObjects = false;
        private bool searchInPaths = false;
        private bool showAllComponentsOnMatch = false;
        private float gameObjectColumnWidth = 250f;

        private ComponentDataManager dataManager;

        // UI elements
        private ObjectField targetObjectField;
        private VisualElement gameObjectFilterColumn;
        private Toggle searchInPathsToggle;
        private Toggle showAllComponentsOnMatchToggle;
        private Toggle showEmptyObjectsToggle;
        private Toggle gameObjectHeaderToggle;
        private Toggle componentHeaderToggle;
        private VisualElement tableContainer;
        private ScrollView tableScrollView;
        private HelpBox selectPromptBox;
        private Button selectButton;
        private Button removeButton;

        // Rows currently displayed, kept for in-place toggle updates
        private List<GameObject> currentFilteredGameObjects = new List<GameObject>();
        private List<ComponentInfo> currentFilteredComponents = new List<ComponentInfo>();
        private readonly List<(GameObject gameObject, Toggle toggle)> gameObjectRowToggles = new List<(GameObject, Toggle)>();
        private readonly List<(ComponentInfo component, Toggle toggle)> componentRowToggles = new List<(ComponentInfo, Toggle)>();

        [MenuItem("Tools/Kanameliser Editor Plus/Component Manager")]
        public static void ShowWindow()
        {
            GetWindow<ComponentManager>("Component Manager");
        }

        private void OnEnable()
        {
            dataManager = new ComponentDataManager();
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
        }

        private void OnDisable()
        {
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
        }

        private void OnPrefabStageChanged(PrefabStage stage)
        {
            UpdateButtonStates();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/net.kanameliser.editor-plus/Editor/ComponentManager/ComponentManager.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("main-container");
            root.Add(mainContainer);

            // Language switcher at top
            var langSwitcher = new IMGUIContainer(Localization.ShowLanguageUI);
            langSwitcher.AddToClassList("language-switcher");
            mainContainer.Add(langSwitcher);

            var titleLabel = new Label("Component Manager");
            titleLabel.AddToClassList("window-title");
            mainContainer.Add(titleLabel);

            CreateTargetObjectSection(mainContainer);
            CreateFilterSection(mainContainer);
            CreateTableSection(mainContainer);
            CreateButtonSection(mainContainer);

            // Re-clamp column widths when the window is resized
            root.RegisterCallback<GeometryChangedEvent>(evt => ApplyColumnWidth(gameObjectColumnWidth));

            RebuildTable();

            // Localize ndmf-tr elements (kept up to date on language change by NDMF)
            Localization.LocalizeUIElements(root);

            // Texts set outside ndmf-tr need a manual refresh on language change
            Localization.RegisterLanguageChangeCallback(this, w => w.UpdateLocalizedTexts());
        }

        private void UpdateLocalizedTexts()
        {
            if (searchInPathsToggle == null) return;

            searchInPathsToggle.text = Localization.S("componentManager.includePathsInSearch");
            showAllComponentsOnMatchToggle.text = Localization.S("componentManager.showAllComponentsOnMatch");
            showEmptyObjectsToggle.text = Localization.S("componentManager.showEmptyObjects");
            selectPromptBox.text = Localization.S("componentManager.selectPrompt");
        }

        private void CreateTargetObjectSection(VisualElement container)
        {
            var row = new VisualElement();
            row.AddToClassList("target-row");

            var label = new Label("componentManager.targetObject");
            label.AddToClassList("target-label");
            label.AddToClassList("ndmf-tr");
            row.Add(label);

            // Use the built-in refresh icon; the ↻ glyph is missing from some UI fonts
            var refreshButton = new Button(OnRefreshButtonClicked);
            refreshButton.AddToClassList("refresh-button");
            var refreshIcon = new Image { image = EditorGUIUtility.IconContent("Refresh").image };
            refreshIcon.AddToClassList("refresh-icon");
            refreshButton.Add(refreshIcon);
            row.Add(refreshButton);

            targetObjectField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true
            };
            targetObjectField.AddToClassList("target-object-field");
            targetObjectField.RegisterValueChangedCallback(OnTargetObjectChanged);
            row.Add(targetObjectField);

            container.Add(row);
        }

        private void OnTargetObjectChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            var newTarget = evt.newValue as GameObject;
            if (newTarget == null)
            {
                // Keep the previous target, matching the original IMGUI behavior
                targetObjectField.SetValueWithoutNotify(targetObject);
                return;
            }

            targetObject = newTarget;
            dataManager.RefreshComponentsList(targetObject);
            RebuildTable();
        }

        private void OnRefreshButtonClicked()
        {
            if (targetObject == null) return;

            // Save the current selection states
            var savedGameObjectSelectionState = new Dictionary<GameObject, bool>(dataManager.GameObjectSelectionState);
            var savedComponentSelectionState = new Dictionary<Component, bool>();
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
                if (savedGameObjectSelectionState.TryGetValue(gameObject, out bool selected))
                {
                    dataManager.GameObjectSelectionState[gameObject] = selected;
                }
            }

            // Restore component selection states
            foreach (var entry in dataManager.ComponentsByGameObject)
            {
                foreach (var compInfo in entry.Value)
                {
                    if (compInfo.Component != null && savedComponentSelectionState.TryGetValue(compInfo.Component, out bool selected))
                    {
                        compInfo.IsSelected = selected;
                    }
                }
            }

            RebuildTable();
        }

        private void CreateFilterSection(VisualElement container)
        {
            var filtersRow = new VisualElement();
            filtersRow.AddToClassList("filters-row");

            // GameObject filter column (width kept in sync with the GameObject table column)
            gameObjectFilterColumn = new VisualElement();
            gameObjectFilterColumn.AddToClassList("filter-column-left");

            var gameObjectFilterLabel = new Label("componentManager.gameObjectFilter");
            gameObjectFilterLabel.AddToClassList("filter-label");
            gameObjectFilterLabel.AddToClassList("ndmf-tr");
            gameObjectFilterColumn.Add(gameObjectFilterLabel);

            var gameObjectFilterField = new TextField();
            gameObjectFilterField.RegisterValueChangedCallback(evt =>
            {
                gameObjectFilter = evt.newValue ?? "";
                RebuildTable();
            });
            gameObjectFilterColumn.Add(gameObjectFilterField);

            // Set toggle captions via text (right of the checkbox), not the label,
            // so UpdateLocalizedTexts can swap them on language change
            searchInPathsToggle = new Toggle { text = Localization.S("componentManager.includePathsInSearch") };
            searchInPathsToggle.AddToClassList("filter-option-toggle");
            searchInPathsToggle.RegisterValueChangedCallback(evt =>
            {
                searchInPaths = evt.newValue;
                RebuildTable();
            });
            gameObjectFilterColumn.Add(searchInPathsToggle);

            filtersRow.Add(gameObjectFilterColumn);

            // Component filter column
            var componentColumn = new VisualElement();
            componentColumn.AddToClassList("filter-column-right");

            var componentFilterLabel = new Label("componentManager.componentFilter");
            componentFilterLabel.AddToClassList("filter-label");
            componentFilterLabel.AddToClassList("ndmf-tr");
            componentColumn.Add(componentFilterLabel);

            var componentFilterField = new TextField();
            componentFilterField.RegisterValueChangedCallback(evt =>
            {
                componentFilter = evt.newValue ?? "";
                RebuildTable();
            });
            componentColumn.Add(componentFilterField);

            showAllComponentsOnMatchToggle = new Toggle { text = Localization.S("componentManager.showAllComponentsOnMatch") };
            showAllComponentsOnMatchToggle.AddToClassList("filter-option-toggle");
            showAllComponentsOnMatchToggle.RegisterValueChangedCallback(evt =>
            {
                showAllComponentsOnMatch = evt.newValue;
                RebuildTable();
            });
            componentColumn.Add(showAllComponentsOnMatchToggle);

            filtersRow.Add(componentColumn);

            container.Add(filtersRow);

            showEmptyObjectsToggle = new Toggle { text = Localization.S("componentManager.showEmptyObjects") };
            showEmptyObjectsToggle.AddToClassList("show-empty-toggle");
            showEmptyObjectsToggle.RegisterValueChangedCallback(evt =>
            {
                showEmptyObjects = evt.newValue;
                dataManager.ShowEmptyObjects = showEmptyObjects;
                if (targetObject != null)
                {
                    dataManager.RefreshComponentsList(targetObject);
                }
                RebuildTable();
            });
            container.Add(showEmptyObjectsToggle);
        }

        private void CreateTableSection(VisualElement container)
        {
            tableContainer = new VisualElement();
            tableContainer.AddToClassList("table-container");

            // Header row
            var header = new VisualElement();
            header.AddToClassList("table-header");

            var gameObjectHeaderCell = new VisualElement();
            gameObjectHeaderCell.AddToClassList("go-column");

            gameObjectHeaderToggle = new Toggle();
            gameObjectHeaderToggle.AddToClassList("row-toggle");
            gameObjectHeaderToggle.RegisterValueChangedCallback(OnGameObjectHeaderToggleChanged);
            gameObjectHeaderCell.Add(gameObjectHeaderToggle);

            var gameObjectHeaderLabel = new Label("GameObject");
            gameObjectHeaderLabel.AddToClassList("header-label");
            gameObjectHeaderCell.Add(gameObjectHeaderLabel);

            header.Add(gameObjectHeaderCell);

            var resizeHandle = new VisualElement();
            resizeHandle.AddToClassList("resize-handle");
            var resizeHandleLine = new VisualElement();
            resizeHandleLine.AddToClassList("resize-handle-line");
            resizeHandle.Add(resizeHandleLine);
            SetupColumnResize(resizeHandle);
            header.Add(resizeHandle);

            componentHeaderToggle = new Toggle();
            componentHeaderToggle.AddToClassList("row-toggle");
            componentHeaderToggle.RegisterValueChangedCallback(OnComponentHeaderToggleChanged);
            header.Add(componentHeaderToggle);

            var componentHeaderLabel = new Label("Component");
            componentHeaderLabel.AddToClassList("header-label");
            header.Add(componentHeaderLabel);

            tableContainer.Add(header);

            tableScrollView = new ScrollView();
            tableScrollView.AddToClassList("table-scroll");
            tableContainer.Add(tableScrollView);

            container.Add(tableContainer);

            selectPromptBox = new HelpBox(Localization.S("componentManager.selectPrompt"), HelpBoxMessageType.Info);
            selectPromptBox.AddToClassList("select-prompt");
            container.Add(selectPromptBox);
        }

        private void CreateButtonSection(VisualElement container)
        {
            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("button-row");

            selectButton = new Button(SelectInHierarchy)
            {
                text = "componentManager.selectInHierarchy"
            };
            selectButton.AddToClassList("action-button");
            selectButton.AddToClassList("ndmf-tr");
            buttonRow.Add(selectButton);

            removeButton = new Button(RemoveSelectedComponents)
            {
                text = "componentManager.removeSelectedItems"
            };
            removeButton.AddToClassList("action-button");
            removeButton.AddToClassList("ndmf-tr");
            buttonRow.Add(removeButton);

            container.Add(buttonRow);
        }

        // ── Column resize ──

        private void SetupColumnResize(VisualElement handle)
        {
            bool isResizing = false;
            float dragStartX = 0f;
            float dragStartWidth = 0f;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                isResizing = true;
                dragStartX = evt.position.x;
                dragStartWidth = gameObjectColumnWidth;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!isResizing || !handle.HasPointerCapture(evt.pointerId)) return;
                ApplyColumnWidth(dragStartWidth + (evt.position.x - dragStartX));
            });

            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!isResizing) return;
                isResizing = false;
                handle.ReleasePointer(evt.pointerId);
            });
        }

        /// <summary>
        /// Clamps and applies the GameObject column width to the header and all rows
        /// </summary>
        private void ApplyColumnWidth(float requestedWidth)
        {
            float windowWidth = rootVisualElement.resolvedStyle.width;
            if (float.IsNaN(windowWidth) || windowWidth <= 0) return;

            float availableWidth = windowWidth - (ComponentConstants.CHECKBOX_WIDTH * 2)
                - ComponentConstants.RESIZE_HANDLE_WIDTH - ComponentConstants.COLUMN_MARGIN;

            float newWidth;
            if (availableWidth < ComponentConstants.MIN_COLUMN_WIDTH * 2)
            {
                // Distribute evenly when the available width is insufficient
                newWidth = availableWidth / 2;
            }
            else
            {
                // Cap at 60% of the available width while ensuring the minimum width,
                // and keep the component column at or above the minimum width as well
                float maxAllowed = Mathf.Max(availableWidth * ComponentConstants.MAX_COLUMN_RATIO, ComponentConstants.MIN_COLUMN_WIDTH);
                newWidth = Mathf.Clamp(requestedWidth, ComponentConstants.MIN_COLUMN_WIDTH, maxAllowed);
                if (availableWidth - newWidth < ComponentConstants.MIN_COLUMN_WIDTH)
                {
                    newWidth = availableWidth - ComponentConstants.MIN_COLUMN_WIDTH;
                }
            }

            gameObjectColumnWidth = newWidth;

            float cellWidth = ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth;
            rootVisualElement.Query(className: "go-column").ForEach(cell => cell.style.width = cellWidth);

            // Keep the GameObject filter column aligned with the table column
            if (gameObjectFilterColumn != null)
            {
                gameObjectFilterColumn.style.width = gameObjectColumnWidth;
            }
        }

        // ── Table building ──

        private void RebuildTable()
        {
            tableScrollView.Clear();
            gameObjectRowToggles.Clear();
            componentRowToggles.Clear();

            bool hasContent = targetObject != null && dataManager.ComponentsByGameObject.Count > 0;
            tableContainer.style.display = hasContent ? DisplayStyle.Flex : DisplayStyle.None;
            selectPromptBox.style.display = hasContent ? DisplayStyle.None : DisplayStyle.Flex;

            if (hasContent)
            {
                (currentFilteredGameObjects, currentFilteredComponents) = dataManager.GetFilteredItems(
                    targetObject, gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch);

                foreach (var gameObject in currentFilteredGameObjects)
                {
                    if (gameObject == null) continue;

                    var components = dataManager.ComponentsByGameObject[gameObject];
                    var filteredComponents = dataManager.FilterComponentsByName(
                        components, componentFilter, showAllComponentsOnMatch);

                    tableScrollView.Add(CreateTableRow(gameObject, filteredComponents));
                }

                ApplyColumnWidth(gameObjectColumnWidth);
            }
            else
            {
                currentFilteredGameObjects = new List<GameObject>();
                currentFilteredComponents = new List<ComponentInfo>();
            }

            UpdateHeaderToggles();
            UpdateButtonStates();
        }

        private VisualElement CreateTableRow(GameObject gameObject, List<ComponentInfo> components)
        {
            var row = new VisualElement();
            row.AddToClassList("table-row");

            // GameObject cell: checkbox + name and path
            var gameObjectCell = new VisualElement();
            gameObjectCell.AddToClassList("go-column");

            var gameObjectToggle = new Toggle();
            gameObjectToggle.AddToClassList("row-toggle");
            gameObjectToggle.SetValueWithoutNotify(dataManager.GameObjectSelectionState[gameObject]);
            gameObjectToggle.RegisterValueChangedCallback(evt =>
            {
                dataManager.GameObjectSelectionState[gameObject] = evt.newValue;
                UpdateHeaderToggles();
                UpdateButtonStates();
            });
            gameObjectCell.Add(gameObjectToggle);
            gameObjectRowToggles.Add((gameObject, gameObjectToggle));

            var gameObjectInfo = new VisualElement();
            gameObjectInfo.AddToClassList("go-cell-content");

            var nameLabel = new Label(gameObject.name);
            nameLabel.AddToClassList("go-name-label");
            nameLabel.AddToClassList("clickable-label");
            nameLabel.RegisterCallback<MouseDownEvent>(evt => SelectGameObjectInHierarchy(gameObject));
            gameObjectInfo.Add(nameLabel);

            var pathLabel = new Label(ComponentPathUtility.GetGameObjectPath(gameObject, targetObject));
            pathLabel.AddToClassList("go-path-label");
            pathLabel.AddToClassList("clickable-label");
            pathLabel.RegisterCallback<MouseDownEvent>(evt => SelectGameObjectInHierarchy(gameObject));
            gameObjectInfo.Add(pathLabel);

            gameObjectCell.Add(gameObjectInfo);
            row.Add(gameObjectCell);

            // Component cell: one row per component
            var componentCell = new VisualElement();
            componentCell.AddToClassList("component-cell");

            foreach (var component in components)
            {
                if (component == null) continue;

                var componentRow = new VisualElement();
                componentRow.AddToClassList("component-row");

                var componentToggle = new Toggle();
                componentToggle.AddToClassList("row-toggle");
                componentToggle.SetValueWithoutNotify(component.IsSelected);
                var capturedComponent = component;
                componentToggle.RegisterValueChangedCallback(evt =>
                {
                    capturedComponent.IsSelected = evt.newValue;
                    UpdateHeaderToggles();
                    UpdateButtonStates();
                });
                componentRow.Add(componentToggle);
                componentRowToggles.Add((component, componentToggle));

                var iconContent = component.Component != null
                    ? EditorGUIUtility.ObjectContent(component.Component, component.Component.GetType())
                    : null;
                var icon = new Image { image = iconContent?.image };
                icon.AddToClassList("component-icon");
                componentRow.Add(icon);

                var componentLabel = new Label(component.Name);
                componentLabel.AddToClassList("component-name-label");
                componentRow.Add(componentLabel);

                componentCell.Add(componentRow);
            }

            row.Add(componentCell);
            return row;
        }

        // ── Header toggles ──

        private void OnGameObjectHeaderToggleChanged(ChangeEvent<bool> evt)
        {
            bool newState = evt.newValue;

            // Always clear all when clicked in a mixed state
            if (gameObjectHeaderToggle.showMixedValue)
            {
                newState = false;
            }

            // Update selection state for GameObjects only (components are not linked)
            foreach (var gameObject in currentFilteredGameObjects)
            {
                dataManager.GameObjectSelectionState[gameObject] = newState;
            }

            foreach (var (gameObject, toggle) in gameObjectRowToggles)
            {
                toggle.SetValueWithoutNotify(dataManager.GameObjectSelectionState[gameObject]);
            }

            UpdateHeaderToggles();
            UpdateButtonStates();
        }

        private void OnComponentHeaderToggleChanged(ChangeEvent<bool> evt)
        {
            bool newState = evt.newValue;

            // Always clear all when clicked in a mixed state
            if (componentHeaderToggle.showMixedValue)
            {
                newState = false;
            }

            // Update only the items visible after filtering
            foreach (var component in currentFilteredComponents)
            {
                component.IsSelected = newState;
            }

            foreach (var (component, toggle) in componentRowToggles)
            {
                toggle.SetValueWithoutNotify(component.IsSelected);
            }

            UpdateHeaderToggles();
            UpdateButtonStates();
        }

        private void UpdateHeaderToggles()
        {
            bool allGameObjectsSelected = currentFilteredGameObjects.Count > 0 &&
                currentFilteredGameObjects.All(go => dataManager.GameObjectSelectionState[go]);
            bool anyGameObjectSelected = currentFilteredGameObjects.Any(go => dataManager.GameObjectSelectionState[go]);
            gameObjectHeaderToggle.SetValueWithoutNotify(anyGameObjectSelected);
            gameObjectHeaderToggle.showMixedValue = anyGameObjectSelected && !allGameObjectsSelected;

            bool allComponentsSelected = currentFilteredComponents.Count > 0 &&
                currentFilteredComponents.All(c => c.IsSelected);
            bool anyComponentSelected = currentFilteredComponents.Any(c => c.IsSelected);
            componentHeaderToggle.SetValueWithoutNotify(anyComponentSelected);
            componentHeaderToggle.showMixedValue = anyComponentSelected && !allComponentsSelected;
        }

        private void UpdateButtonStates()
        {
            if (selectButton == null) return;

            var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();
            bool anySelected = selectedGameObjects.Count > 0 || selectedComponents.Count > 0;

            selectButton.SetEnabled(CanSelectInHierarchy() && anySelected);
            removeButton.SetEnabled(anySelected);
        }

        // ── Hierarchy selection ──

        // Determine whether selecting in the Hierarchy is possible
        private bool CanSelectInHierarchy()
        {
            if (targetObject == null) return false;

            // Get the current prefab editing mode info
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
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
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, PrefabStage prefabStage)
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
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

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
        /// Selects the GameObject in the Hierarchy (row click)
        /// </summary>
        private void SelectGameObjectInHierarchy(GameObject gameObject)
        {
            if (gameObject == null) return;

            // Get the current prefab editing mode info
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            // In prefab editing mode, get the corresponding object inside the prefab stage
            GameObject objectToSelect = GetCorrespondingPrefabModeObjectForRowClick(gameObject, prefabStage);

            // Select in the Hierarchy
            Selection.activeObject = objectToSelect;

            // Move editor focus to the Hierarchy view
            // Try to get the SceneHierarchyWindow first
            var sceneHierarchyWindowType = Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor");
            if (sceneHierarchyWindowType != null)
            {
                var hierarchyWindow = GetWindow(sceneHierarchyWindowType);
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
        /// Gets the corresponding GameObject inside prefab editing mode, with fallbacks for row clicks
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObjectForRowClick(GameObject gameObject, PrefabStage prefabStage)
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

        // ── Removal ──

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
                            RebuildTable();
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
