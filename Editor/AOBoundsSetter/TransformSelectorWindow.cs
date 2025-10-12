using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Window for selecting a Transform from a filtered hierarchy
    /// </summary>
    public class TransformSelectorWindow : EditorWindow
    {
        private GameObject rootObject;
        private Action<Transform> onSelectionChanged;
        private TextField searchField;
        private ScrollView transformListScrollView;
        private List<Transform> allTransforms = new List<Transform>();
        private List<Transform> filteredTransforms = new List<Transform>();
        private string searchText = "";

        public static void Show(Rect fieldRect, GameObject rootObject, Action<Transform> onSelectionChanged)
        {
            var window = CreateInstance<TransformSelectorWindow>();
            window.rootObject = rootObject;
            window.onSelectionChanged = onSelectionChanged;

            // Position the window below the field
            Vector2 windowSize = new Vector2(300, 400);

            // Convert to screen position, align to the right edge of the field
            Vector2 screenPosition = GUIUtility.GUIToScreenPoint(new Vector2(fieldRect.xMax - windowSize.x, fieldRect.yMax));

            // Ensure window stays within screen bounds
            float screenWidth = Screen.currentResolution.width;
            float screenHeight = Screen.currentResolution.height;

            if (screenPosition.x < 0)
            {
                screenPosition.x = 0;
            }
            if (screenPosition.x + windowSize.x > screenWidth)
            {
                screenPosition.x = screenWidth - windowSize.x;
            }
            if (screenPosition.y + windowSize.y > screenHeight)
            {
                screenPosition.y = GUIUtility.GUIToScreenPoint(new Vector2(fieldRect.x, fieldRect.y)).y - windowSize.y;
            }

            window.position = new Rect(screenPosition, windowSize);
            window.ShowPopup();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingTop = 5;
            root.style.paddingBottom = 5;
            root.style.paddingLeft = 5;
            root.style.paddingRight = 5;

            // Search field
            searchField = new TextField();
            searchField.style.marginBottom = 5;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);

            // Add placeholder text
            var placeholderLabel = new Label("Search...");
            placeholderLabel.style.position = Position.Absolute;
            placeholderLabel.style.left = 3;
            placeholderLabel.style.top = 2;
            placeholderLabel.style.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            placeholderLabel.pickingMode = PickingMode.Ignore;

            searchField.RegisterCallback<FocusInEvent>(evt =>
            {
                if (string.IsNullOrEmpty(searchField.value))
                {
                    placeholderLabel.style.display = DisplayStyle.None;
                }
            });
            searchField.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (string.IsNullOrEmpty(searchField.value))
                {
                    placeholderLabel.style.display = DisplayStyle.Flex;
                }
            });

            var searchContainer = new VisualElement();
            searchContainer.style.position = Position.Relative;
            searchContainer.Add(searchField);
            searchContainer.Add(placeholderLabel);
            root.Add(searchContainer);

            // Transform list
            transformListScrollView = new ScrollView();
            transformListScrollView.style.flexGrow = 1;
            transformListScrollView.style.borderTopWidth = 1;
            transformListScrollView.style.borderBottomWidth = 1;
            transformListScrollView.style.borderLeftWidth = 1;
            transformListScrollView.style.borderRightWidth = 1;
            transformListScrollView.style.borderTopColor = new Color(0.2f, 0.2f, 0.2f);
            transformListScrollView.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f);
            transformListScrollView.style.borderLeftColor = new Color(0.2f, 0.2f, 0.2f);
            transformListScrollView.style.borderRightColor = new Color(0.2f, 0.2f, 0.2f);
            root.Add(transformListScrollView);

            RefreshTransformList();

            // Focus search field
            searchField.Focus();
        }

        private void OnSearchTextChanged(ChangeEvent<string> evt)
        {
            searchText = evt.newValue ?? "";
            RefreshFilteredList();
        }

        private void RefreshTransformList()
        {
            allTransforms.Clear();

            if (rootObject == null)
            {
                RefreshFilteredList();
                return;
            }

            // Get all transforms recursively
            var transforms = rootObject.GetComponentsInChildren<Transform>(true);
            allTransforms.AddRange(transforms);

            RefreshFilteredList();
        }

        private void RefreshFilteredList()
        {
            transformListScrollView.Clear();
            filteredTransforms.Clear();

            // Filter by search text
            if (string.IsNullOrEmpty(searchText))
            {
                filteredTransforms.AddRange(allTransforms);
            }
            else
            {
                filteredTransforms.AddRange(allTransforms.Where(t =>
                    t.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Create list items
            foreach (var transform in filteredTransforms)
            {
                var item = CreateTransformListItem(transform);
                transformListScrollView.Add(item);
            }
        }

        private VisualElement CreateTransformListItem(Transform transform)
        {
            var item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.alignItems = Align.Center;
            item.style.paddingTop = 4;
            item.style.paddingBottom = 4;
            item.style.paddingLeft = 4;
            item.style.paddingRight = 4;

            // Icon
            var iconContent = EditorGUIUtility.ObjectContent(transform, typeof(Transform));
            var icon = new Image
            {
                image = iconContent.image
            };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 4;
            icon.style.flexShrink = 0;
            item.Add(icon);

            // Name
            var nameLabel = new Label(transform.name);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            item.Add(nameLabel);

            // Hover effect
            item.RegisterCallback<MouseEnterEvent>(evt =>
            {
                item.style.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
            });
            item.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                item.style.backgroundColor = Color.clear;
            });

            // Single-click to select
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                onSelectionChanged?.Invoke(transform);
                Close();
            });

            return item;
        }

        private void OnLostFocus()
        {
            // Close window when it loses focus
            Close();
        }
    }
}
