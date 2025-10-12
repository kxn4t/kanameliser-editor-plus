using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Editor window for batch setting Anchor Override and Bounds on meshes
    /// </summary>
    public class AOBoundsSetterWindow : EditorWindow
    {
        private ObjectField rootObjectField;
        private Button refreshButton;
        private ObjectField anchorOverrideField;
        private Vector3Field boundsCenterField;
        private Vector3Field boundsExtentField;
        private ObjectField rootBoneField;
        private Button applyButton;
        private ScrollView meshListScrollView;
        private Toggle headerToggle;

        private GameObject rootObject;
        private List<MeshRendererData> meshRendererDataList = new List<MeshRendererData>();

        [MenuItem("Tools/Kanameliser Editor Plus/AO Bounds Setter")]
        public static void ShowWindow()
        {
            var window = GetWindow<AOBoundsSetterWindow>();
            window.titleContent = new GUIContent("AO Bounds Setter");
            window.minSize = new Vector2(800, 600);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            // Load USS stylesheet
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/net.kanameliser.editor-plus/Editor/AOBoundsSetter/AOBoundsSetterWindow.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Main container
            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("main-container");
            root.Add(mainContainer);

            CreateRootObjectSection(mainContainer);
            CreateBatchSettingsSection(mainContainer);
            CreateMeshListSection(mainContainer);

            UpdateApplyButtonState();
        }

        private void CreateRootObjectSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("root-object-section");

            var label = new Label("Root Object");
            label.AddToClassList("section-label");
            section.Add(label);

            // Root Object Field with Refresh Button
            var rootObjectContainer = new VisualElement();
            rootObjectContainer.AddToClassList("root-object-container");

            refreshButton = new Button(OnRefreshButtonClicked)
            {
                text = "↻",
                tooltip = "Reload meshes from the root object"
            };
            refreshButton.AddToClassList("refresh-button");
            rootObjectContainer.Add(refreshButton);

            rootObjectField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true
            };
            rootObjectField.AddToClassList("root-object-field");
            rootObjectField.RegisterValueChangedCallback(OnRootObjectChanged);
            rootObjectContainer.Add(rootObjectField);

            section.Add(rootObjectContainer);

            container.Add(section);
        }

        private void OnRefreshButtonClicked()
        {
            RefreshMeshList();
        }

        private void CreateBatchSettingsSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");

            var label = new Label("Batch Settings");
            label.AddToClassList("section-title");
            section.Add(label);

            var aoLabel = new Label("Anchor Override:");
            aoLabel.AddToClassList("field-label");
            section.Add(aoLabel);

            var anchorOverrideContainer = new VisualElement();
            anchorOverrideContainer.AddToClassList("transform-field-container");

            anchorOverrideField = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = true
            };
            anchorOverrideField.AddToClassList("transform-field");
            anchorOverrideContainer.Add(anchorOverrideField);

            var anchorDropdownButton = new Button(() => OnTransformSelectorButtonClicked(anchorOverrideField))
            {
                text = "▼"
            };
            anchorDropdownButton.AddToClassList("dropdown-button");
            anchorOverrideContainer.Add(anchorDropdownButton);

            section.Add(anchorOverrideContainer);

            var rootBoneLabel = new Label("Root Bone (SkinnedMeshRenderer only):");
            rootBoneLabel.AddToClassList("field-label-with-spacing");
            section.Add(rootBoneLabel);

            var rootBoneContainer = new VisualElement();
            rootBoneContainer.AddToClassList("transform-field-container");

            rootBoneField = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = true
            };
            rootBoneField.AddToClassList("transform-field");
            rootBoneContainer.Add(rootBoneField);

            var rootBoneDropdownButton = new Button(() => OnTransformSelectorButtonClicked(rootBoneField))
            {
                text = "▼"
            };
            rootBoneDropdownButton.AddToClassList("dropdown-button");
            rootBoneContainer.Add(rootBoneDropdownButton);

            section.Add(rootBoneContainer);

            var boundsLabel = new Label("Bounds (SkinnedMeshRenderer only):");
            boundsLabel.AddToClassList("field-label-with-spacing");
            section.Add(boundsLabel);

            boundsCenterField = new Vector3Field("Center");
            section.Add(boundsCenterField);

            boundsExtentField = new Vector3Field("Extent")
            {
                value = Vector3.one
            };
            section.Add(boundsExtentField);

            applyButton = new Button(OnApplyButtonClicked)
            {
                text = "Apply to Selected Meshes"
            };
            applyButton.AddToClassList("apply-button");
            section.Add(applyButton);

            container.Add(section);
        }

        private void CreateMeshListSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("mesh-list-section");

            var label = new Label("Mesh List");
            label.AddToClassList("section-label");
            section.Add(label);

            // Container for header and list
            var listContainer = new VisualElement();
            listContainer.AddToClassList("list-container");

            var header = CreateMeshListHeader();
            listContainer.Add(header);

            meshListScrollView = new ScrollView
            {
                mode = ScrollViewMode.VerticalAndHorizontal
            };
            meshListScrollView.AddToClassList("mesh-list-scroll");
            listContainer.Add(meshListScrollView);

            section.Add(listContainer);

            container.Add(section);
        }

        private VisualElement CreateMeshListHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("mesh-list-header");

            headerToggle = new Toggle();
            headerToggle.AddToClassList("header-toggle");
            headerToggle.RegisterValueChangedCallback(OnHeaderToggleChanged);
            header.Add(headerToggle);

            var objectNameLabel = new Label("Object Name");
            objectNameLabel.AddToClassList("header-label-object-name");
            header.Add(objectNameLabel);

            var aoLabel = new Label("Anchor Override");
            aoLabel.AddToClassList("header-label-ao");
            header.Add(aoLabel);

            var rootBoneLabel = new Label("Root Bone");
            rootBoneLabel.AddToClassList("header-label-root-bone");
            header.Add(rootBoneLabel);

            var boundsLabel = new Label("Bounds");
            boundsLabel.AddToClassList("header-label-bounds");
            header.Add(boundsLabel);

            return header;
        }

        private void OnRootObjectChanged(ChangeEvent<Object> evt)
        {
            rootObject = evt.newValue as GameObject;
            RefreshMeshList();
        }

        private void RefreshMeshList()
        {
            meshRendererDataList.Clear();
            meshListScrollView.Clear();

            if (rootObject == null)
            {
                UpdateHeaderToggle();
                UpdateApplyButtonState();
                return;
            }

            // Exclude prefab assets
            if (PrefabUtility.IsPartOfPrefabAsset(rootObject))
            {
                Debug.LogWarning("Prefab assets are not supported. Please select a scene object.");
                UpdateHeaderToggle();
                UpdateApplyButtonState();
                return;
            }

            // Get all Renderers recursively
            var renderers = rootObject.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                var data = new MeshRendererData
                {
                    Renderer = renderer,
                    IsSelected = true
                };
                meshRendererDataList.Add(data);

                var row = CreateMeshListRow(data);
                meshListScrollView.Add(row);
            }

            UpdateHeaderToggle();
            UpdateApplyButtonState();
        }

        private VisualElement CreateMeshListRow(MeshRendererData data)
        {
            var row = new VisualElement();
            row.AddToClassList("mesh-list-row");

            // Checkbox for individual selection
            var toggle = new Toggle();
            toggle.value = data.IsSelected;
            toggle.AddToClassList("row-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                data.IsSelected = evt.newValue;
                UpdateRowStyle(row, data.IsSelected);
                UpdateHeaderToggle();
                UpdateApplyButtonState();
            });
            row.Add(toggle);

            // Icon + Object name (clickable to select in hierarchy)
            var nameContainer = new VisualElement();
            nameContainer.AddToClassList("name-container");
            nameContainer.AddToClassList("clickable-label");
            nameContainer.RegisterCallback<MouseDownEvent>(evt =>
            {
                Selection.activeGameObject = data.Renderer.gameObject;
                EditorGUIUtility.PingObject(data.Renderer.gameObject);
            });

            var iconContent = EditorGUIUtility.ObjectContent(data.Renderer, data.Renderer.GetType());
            var icon = new Image
            {
                image = iconContent.image
            };
            icon.AddToClassList("name-icon");
            nameContainer.Add(icon);

            var nameLabel = new Label(data.Renderer.gameObject.name);
            nameLabel.AddToClassList("name-label");
            nameContainer.Add(nameLabel);

            row.Add(nameContainer);

            // Anchor Override (clickable to select in hierarchy)
            var aoLabel = new Label(GetAnchorOverrideText(data.Renderer));
            aoLabel.AddToClassList("ao-label");
            aoLabel.AddToClassList("clickable-label");
            aoLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (data.Renderer.probeAnchor != null)
                {
                    Selection.activeGameObject = data.Renderer.probeAnchor.gameObject;
                    EditorGUIUtility.PingObject(data.Renderer.probeAnchor.gameObject);
                }
            });
            row.Add(aoLabel);

            // Root Bone (clickable to select in hierarchy)
            var rootBoneLabel = new Label(GetRootBoneText(data.Renderer));
            rootBoneLabel.AddToClassList("root-bone-label");
            rootBoneLabel.AddToClassList("clickable-label");
            rootBoneLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (data.Renderer is SkinnedMeshRenderer smr && smr.rootBone != null)
                {
                    Selection.activeGameObject = smr.rootBone.gameObject;
                    EditorGUIUtility.PingObject(smr.rootBone.gameObject);
                }
            });
            row.Add(rootBoneLabel);

            // Bounds (SkinnedMeshRenderer only)
            var boundsLabel = new Label(GetBoundsText(data.Renderer));
            boundsLabel.AddToClassList("bounds-label");
            row.Add(boundsLabel);

            UpdateRowStyle(row, data.IsSelected);

            return row;
        }

        private static void UpdateRowStyle(VisualElement row, bool isSelected)
        {
            if (!isSelected)
            {
                row.style.opacity = 0.5f;
            }
            else
            {
                row.style.opacity = 1f;
            }
        }

        private static string GetAnchorOverrideText(Renderer renderer)
        {
            if (renderer.probeAnchor == null)
                return "(None)";
            return renderer.probeAnchor.name;
        }

        private static string GetRootBoneText(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
            {
                if (smr.rootBone == null)
                    return "(None)";
                return smr.rootBone.name;
            }
            return "-";
        }

        private static string GetBoundsText(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
            {
                var bounds = smr.localBounds;
                var extent = bounds.extents;
                return $"C:({bounds.center.x:F2},{bounds.center.y:F2},{bounds.center.z:F2}) E:({extent.x:F2},{extent.y:F2},{extent.z:F2})";
            }
            return "-";
        }

        private void OnHeaderToggleChanged(ChangeEvent<bool> evt)
        {
            bool newValue = evt.newValue;

            // If showMixedValue is true, deselect all
            if (headerToggle.showMixedValue)
            {
                newValue = false;
            }

            // Update data
            foreach (var data in meshRendererDataList)
            {
                data.IsSelected = newValue;
            }

            // Update UI for each row
            var rows = meshListScrollView.Children().ToList();
            for (int i = 0; i < rows.Count && i < meshRendererDataList.Count; i++)
            {
                var row = rows[i];
                var data = meshRendererDataList[i];

                // Find and update the toggle in this row
                var toggle = row.Q<Toggle>();
                if (toggle != null)
                {
                    toggle.SetValueWithoutNotify(data.IsSelected);
                }

                UpdateRowStyle(row, data.IsSelected);
            }

            // Update header toggle state to reflect the new selection
            UpdateHeaderToggle();
            UpdateApplyButtonState();
        }

        private void UpdateHeaderToggle()
        {
            if (meshRendererDataList.Count == 0)
            {
                headerToggle.SetValueWithoutNotify(false);
                headerToggle.showMixedValue = false;
                return;
            }

            int selectedCount = meshRendererDataList.Count(d => d.IsSelected);

            if (selectedCount == 0)
            {
                headerToggle.SetValueWithoutNotify(false);
                headerToggle.showMixedValue = false;
            }
            else if (selectedCount == meshRendererDataList.Count)
            {
                headerToggle.SetValueWithoutNotify(true);
                headerToggle.showMixedValue = false;
            }
            else
            {
                headerToggle.SetValueWithoutNotify(true);
                headerToggle.showMixedValue = true;
            }
        }

        private void UpdateApplyButtonState()
        {
            bool hasSelection = meshRendererDataList.Any(d => d.IsSelected);
            applyButton.SetEnabled(hasSelection && rootObject != null);
        }

        private void OnApplyButtonClicked()
        {
            var selectedRenderers = meshRendererDataList
                .Where(d => d.IsSelected)
                .Select(d => d.Renderer)
                .ToArray();

            if (selectedRenderers.Length == 0)
            {
                Debug.LogWarning("No meshes selected.");
                return;
            }

            // Record for Undo
            Undo.RecordObjects(selectedRenderers, "Apply AO, Bounds and Root Bone Settings");

            Transform anchorOverride = anchorOverrideField.value as Transform;
            Vector3 center = boundsCenterField.value;
            Vector3 extent = boundsExtentField.value;
            Vector3 size = extent * 2f; // Convert Extent to Size
            Bounds bounds = new Bounds(center, size);
            Transform rootBone = rootBoneField.value as Transform;

            int aoAppliedCount = 0;
            int boundsAppliedCount = 0;
            int rootBoneAppliedCount = 0;

            foreach (var renderer in selectedRenderers)
            {
                // Apply Anchor Override
                if (anchorOverride != null || renderer.probeAnchor != null)
                {
                    renderer.probeAnchor = anchorOverride;
                    aoAppliedCount++;
                }

                // Apply Bounds and Root Bone (SkinnedMeshRenderer only)
                if (renderer is SkinnedMeshRenderer smr)
                {
                    smr.localBounds = bounds;
                    boundsAppliedCount++;

                    if (rootBone != null || smr.rootBone != null)
                    {
                        smr.rootBone = rootBone;
                        rootBoneAppliedCount++;
                    }
                }
            }

            Debug.Log($"Applied settings: Anchor Override ({aoAppliedCount}), Bounds ({boundsAppliedCount}), Root Bone ({rootBoneAppliedCount})");

            RefreshMeshList();
        }

        private void OnTransformSelectorButtonClicked(ObjectField targetField)
        {
            if (rootObject == null)
            {
                Debug.LogWarning("Please set a Root Object first.");
                return;
            }

            // Get the ObjectField's screen position (instead of button)
            var fieldRect = targetField.worldBound;
            TransformSelectorWindow.Show(fieldRect, rootObject, (selectedTransform) =>
            {
                targetField.value = selectedTransform;
            });
        }
    }

    /// <summary>
    /// Data class for mesh renderer
    /// </summary>
    public class MeshRendererData
    {
        public Renderer Renderer;
        public bool IsSelected;
    }
}
