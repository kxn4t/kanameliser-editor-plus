using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Manages component data
    /// </summary>
    public class ComponentDataManager
    {
        private Dictionary<GameObject, List<ComponentInfo>> componentsByGameObject = new Dictionary<GameObject, List<ComponentInfo>>();
        private Dictionary<GameObject, bool> gameObjectSelectionState = new Dictionary<GameObject, bool>();
        private bool showEmptyObjects = false;

        public Dictionary<GameObject, List<ComponentInfo>> ComponentsByGameObject => componentsByGameObject;
        public Dictionary<GameObject, bool> GameObjectSelectionState => gameObjectSelectionState;
        public bool ShowEmptyObjects { get => showEmptyObjects; set => showEmptyObjects = value; }

        /// <summary>
        /// Refreshes the component list
        /// </summary>
        public void RefreshComponentsList(GameObject targetObject)
        {
            // Keep checkbox state across refreshes (hierarchy changes, undo/redo)
            var previouslySelectedObjects = new HashSet<GameObject>(
                gameObjectSelectionState.Where(e => e.Value).Select(e => e.Key));
            var previouslySelectedComponents = new HashSet<Component>(
                componentsByGameObject.Values
                    .SelectMany(list => list)
                    .Where(c => c.IsSelected && c.Component != null)
                    .Select(c => c.Component));

            componentsByGameObject.Clear();
            gameObjectSelectionState.Clear();

            ComponentPathUtility.ClearCache();

            if (targetObject == null) return;

            try
            {
                // Collect the target and its child objects
                Transform[] transforms = targetObject.GetComponentsInChildren<Transform>(true);
                foreach (Transform transform in transforms)
                {
                    if (transform == null) continue;

                    GameObject gameObject = transform.gameObject;
                    if (gameObject == null) continue;

                    // Collect the object's components
                    Component[] components = gameObject.GetComponents<Component>();

                    // Filter out Transform components
                    List<ComponentInfo> componentInfos = components
                        .Where(c => c != null && !(c is Transform))
                        .Select(c => new ComponentInfo
                        {
                            GameObject = gameObject,
                            Component = c,
                            IsSelected = previouslySelectedComponents.Contains(c)
                        })
                        .ToList();

                    // When the option to show GameObjects without components is on
                    if (componentInfos.Count > 0 || showEmptyObjects)
                    {
                        // Register the GameObject even when it has no components
                        componentsByGameObject[gameObject] = componentInfos;
                        gameObjectSelectionState[gameObject] = previouslySelectedObjects.Contains(gameObject);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while updating component list: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the list of GameObjects sorted in hierarchy order
        /// </summary>
        public List<GameObject> GetOrderedGameObjects(GameObject targetObject)
        {
            List<GameObject> ordered = new List<GameObject>();
            if (targetObject == null) return ordered;

            // Traverse GameObjects in order
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(targetObject.transform);

            while (queue.Count > 0)
            {
                Transform currentTransform = queue.Dequeue();
                if (currentTransform == null) continue;

                GameObject currentGO = currentTransform.gameObject;
                if (currentGO == null) continue;

                // Only include GameObjects present in the dictionary
                // (GameObjects without components are included when the corresponding option is on)
                if (componentsByGameObject.ContainsKey(currentGO))
                {
                    ordered.Add(currentGO);
                }

                // Enqueue child elements
                for (int i = 0; i < currentTransform.childCount; i++)
                {
                    Transform child = currentTransform.GetChild(i);
                    if (child != null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return ordered;
        }

        /// <summary>
        /// Filters by GameObject name or path
        /// </summary>
        private bool IsGameObjectMatchingFilter(GameObject gameObject, GameObject targetObject, string gameObjectFilter, bool searchInPaths)
        {
            if (string.IsNullOrEmpty(gameObjectFilter))
            {
                return true;
            }

            string filterLower = gameObjectFilter.ToLower();

            if (searchInPaths)
            {
                // Search including the path
                string path = ComponentPathUtility.GetGameObjectPath(gameObject, targetObject);
                return path.ToLower().Contains(filterLower);
            }
            else
            {
                // Search by name only
                return gameObject.name.ToLower().Contains(filterLower);
            }
        }

        /// <summary>
        /// Filters components by name
        /// </summary>
        public List<ComponentInfo> FilterComponentsByName(List<ComponentInfo> components, string componentFilter, bool showAllComponentsOnMatch)
        {
            if (string.IsNullOrEmpty(componentFilter))
            {
                return components.ToList();
            }

            string filterLower = componentFilter.ToLower();

            // Filter by component name
            var matchingComponents = components
                .Where(c => c.Name.ToLower().Contains(filterLower))
                .ToList();

            // Option to show all components of GameObjects that have a matching component
            return (showAllComponentsOnMatch && matchingComponents.Any())
                ? components.ToList()
                : matchingComponents;
        }

        /// <summary>
        /// Gets the filtered GameObjects and components
        /// </summary>
        public (List<GameObject> gameObjects, List<ComponentInfo> components) GetFilteredItems(
            GameObject targetObject, string gameObjectFilter, string componentFilter,
            bool searchInPaths, bool showAllComponentsOnMatch)
        {
            List<GameObject> filteredGameObjects = new List<GameObject>();
            List<ComponentInfo> filteredComponents = new List<ComponentInfo>();

            var orderedGameObjects = GetOrderedGameObjects(targetObject);

            // Filter GameObjects and components
            foreach (var gameObject in orderedGameObjects)
            {
                if (!componentsByGameObject.ContainsKey(gameObject)) continue;

                // Filter by GameObject name or path
                bool gameObjectMatched = IsGameObjectMatchingFilter(gameObject, targetObject, gameObjectFilter, searchInPaths);
                if (!gameObjectMatched) continue;

                var components = componentsByGameObject[gameObject];
                var matchingComponents = FilterComponentsByName(components, componentFilter, showAllComponentsOnMatch);

                // Decide what to display based on the filtering results
                if (string.IsNullOrEmpty(componentFilter) || matchingComponents.Any())
                {
                    filteredGameObjects.Add(gameObject);
                    filteredComponents.AddRange(matchingComponents);
                }
            }

            return (filteredGameObjects, filteredComponents);
        }

        /// <summary>
        /// Gets the selected GameObjects and components
        /// </summary>
        public (List<GameObject> gameObjects, List<ComponentInfo> components) GetSelectedItems()
        {
            // Get the selected GameObjects
            var selectedGameObjects = componentsByGameObject.Keys
                .Where(go => gameObjectSelectionState.ContainsKey(go) && gameObjectSelectionState[go])
                .ToList();

            // Get the selected components (only those whose GameObject is not selected)
            var selectedComponents = componentsByGameObject
                .Where(entry => !gameObjectSelectionState.ContainsKey(entry.Key) || !gameObjectSelectionState[entry.Key])
                .SelectMany(entry => entry.Value.Where(c => c.IsSelected))
                .ToList();

            return (selectedGameObjects, selectedComponents);
        }
    }

}
