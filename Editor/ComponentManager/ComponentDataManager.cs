using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// コンポーネントデータを管理するクラス
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
        /// コンポーネントリストを更新する
        /// </summary>
        public void RefreshComponentsList(GameObject targetObject)
        {
            if (targetObject == null) return;

            componentsByGameObject.Clear();
            gameObjectSelectionState.Clear();

            ComponentPathUtility.ClearCache();

            try
            {
                // ターゲットとその子オブジェクトを収集
                Transform[] transforms = targetObject.GetComponentsInChildren<Transform>(true);
                foreach (Transform transform in transforms)
                {
                    if (transform == null) continue;

                    GameObject gameObject = transform.gameObject;
                    if (gameObject == null) continue;

                    // オブジェクトのコンポーネントを収集
                    Component[] components = gameObject.GetComponents<Component>();

                    // Transform以外のコンポーネントをフィルタリング
                    List<ComponentInfo> componentInfos = components
                        .Where(c => c != null && !(c is Transform))
                        .Select(c => new ComponentInfo
                        {
                            GameObject = gameObject,
                            Component = c,
                            IsSelected = false
                        })
                        .ToList();

                    // コンポーネントがないオブジェクトも表示するオプションがオンの場合
                    if (componentInfos.Count > 0 || showEmptyObjects)
                    {
                        // コンポーネントがなくてもGameObjectを登録
                        componentsByGameObject[gameObject] = componentInfos;
                        gameObjectSelectionState[gameObject] = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while updating component list: {ex.Message}");
            }
        }

        /// <summary>
        /// 階層順にソートされたGameObjectのリストを取得
        /// </summary>
        public List<GameObject> GetOrderedGameObjects(GameObject targetObject)
        {
            List<GameObject> ordered = new List<GameObject>();
            if (targetObject == null) return ordered;

            try
            {
                // GameObjectの順番を取得
                Queue<Transform> queue = new Queue<Transform>();
                queue.Enqueue(targetObject.transform);

                while (queue.Count > 0)
                {
                    Transform currentTransform = queue.Dequeue();
                    if (currentTransform == null) continue;

                    GameObject currentGO = currentTransform.gameObject;
                    if (currentGO == null) continue;

                    // 辞書に存在するGameObjectのみを対象とする
                    // （コンポーネントがないオブジェクトも表示するオプションがオンの場合は辞書に含まれている）
                    if (componentsByGameObject.ContainsKey(currentGO))
                    {
                        ordered.Add(currentGO);
                    }

                    // 子要素をキューに追加
                    for (int i = 0; i < currentTransform.childCount; i++)
                    {
                        Transform child = currentTransform.GetChild(i);
                        if (child != null)
                        {
                            queue.Enqueue(child);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while ordering GameObjects: {ex.Message}");
            }

            return ordered;
        }

        /// <summary>
        /// GameObject名またはパスによるフィルタリングを行う
        /// </summary>
        private bool IsGameObjectMatchingFilter(GameObject gameObject, GameObject targetObject, string gameObjectFilter, bool searchInPaths)
        {
            if (string.IsNullOrEmpty(gameObjectFilter))
            {
                return true;
            }

            try
            {
                string filterLower = gameObjectFilter.ToLower();

                if (searchInPaths)
                {
                    // パスを含めて検索
                    string path = ComponentPathUtility.GetGameObjectPath(gameObject, targetObject);
                    return path.ToLower().Contains(filterLower);
                }
                else
                {
                    // 名前のみで検索
                    return gameObject.name.ToLower().Contains(filterLower);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred during GameObject filtering: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// コンポーネント名によるフィルタリングを行う
        /// </summary>
        public List<ComponentInfo> FilterComponentsByName(List<ComponentInfo> components, string componentFilter, bool showAllComponentsOnMatch)
        {
            if (string.IsNullOrEmpty(componentFilter))
            {
                return components.ToList();
            }

            try
            {
                string filterLower = componentFilter.ToLower();

                // コンポーネント名でフィルタリング
                var matchingComponents = components
                    .Where(c => c.Name.ToLower().Contains(filterLower))
                    .ToList();

                // 一致するコンポーネントを持つGameObjectの全コンポーネントを表示するオプション
                return (showAllComponentsOnMatch && matchingComponents.Any())
                    ? components.ToList()
                    : matchingComponents;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred during component filtering: {ex.Message}");
                return new List<ComponentInfo>();
            }
        }

        /// <summary>
        /// フィルタリングされたGameObjectとコンポーネントを取得
        /// </summary>
        public (List<GameObject> gameObjects, List<ComponentInfo> components) GetFilteredItems(
            GameObject targetObject, string gameObjectFilter, string componentFilter,
            bool searchInPaths, bool showAllComponentsOnMatch)
        {
            List<GameObject> filteredGameObjects = new List<GameObject>();
            List<ComponentInfo> filteredComponents = new List<ComponentInfo>();

            try
            {
                var orderedGameObjects = GetOrderedGameObjects(targetObject);

                // GameObjectとコンポーネントのフィルタリング
                foreach (var gameObject in orderedGameObjects)
                {
                    if (!componentsByGameObject.ContainsKey(gameObject)) continue;

                    // GameObject名またはパスによるフィルタリング
                    bool gameObjectMatched = IsGameObjectMatchingFilter(gameObject, targetObject, gameObjectFilter, searchInPaths);
                    if (!gameObjectMatched) continue;

                    var components = componentsByGameObject[gameObject];
                    var matchingComponents = FilterComponentsByName(components, componentFilter, showAllComponentsOnMatch);

                    // フィルタリング結果に基づいて表示対象を決定
                    if (string.IsNullOrEmpty(componentFilter) || matchingComponents.Any())
                    {
                        filteredGameObjects.Add(gameObject);
                        filteredComponents.AddRange(matchingComponents);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred during item filtering: {ex.Message}");
            }

            return (filteredGameObjects, filteredComponents);
        }

        /// <summary>
        /// 選択されたGameObjectとコンポーネントを取得
        /// </summary>
        public (List<GameObject> gameObjects, List<ComponentInfo> components) GetSelectedItems()
        {
            try
            {
                // 選択されたGameObjectを取得
                var selectedGameObjects = componentsByGameObject.Keys
                    .Where(go => gameObjectSelectionState.ContainsKey(go) && gameObjectSelectionState[go])
                    .ToList();

                // 選択されたコンポーネントを取得（GameObjectが選択されていないもののみ）
                var selectedComponents = componentsByGameObject
                    .Where(entry => !gameObjectSelectionState.ContainsKey(entry.Key) || !gameObjectSelectionState[entry.Key])
                    .SelectMany(entry => entry.Value.Where(c => c.IsSelected))
                    .ToList();

                return (selectedGameObjects, selectedComponents);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while getting selected items: {ex.Message}");
                return (new List<GameObject>(), new List<ComponentInfo>());
            }
        }
    }

}
