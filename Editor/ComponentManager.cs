using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// コンポーネント情報を格納するクラス
    /// </summary>
    public class ComponentInfo
    {
        public GameObject GameObject;
        public Component Component;
        public bool IsSelected;

        public string Name
        {
            get
            {
                return Component != null ? Component.GetType().Name : "Missing Component";
            }
        }
    }

    /// <summary>
    /// 共通の定数を定義するクラス
    /// </summary>
    public static class ComponentConstants
    {
        public const float MIN_COLUMN_WIDTH = 150f;
        public const float MAX_COLUMN_WIDTH = 500f;
        public const float CHECKBOX_WIDTH = 20f;
        public const float RESIZE_HANDLE_WIDTH = 8f;
        public const float ICON_WIDTH = 20f;
        public const float ROW_HEIGHT = 18f;
        public const float MIN_ROW_HEIGHT = 36f;
        public const float COLUMN_MARGIN = 20f;
        public const float MAX_COLUMN_RATIO = 0.6f;
    }

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

    /// <summary>
    /// パス関連のユーティリティクラス
    /// </summary>
    public static class ComponentPathUtility
    {
        // パスのキャッシュ
        private static Dictionary<(GameObject, GameObject), string> pathCache = new Dictionary<(GameObject, GameObject), string>();

        /// <summary>
        /// パスキャッシュをクリア
        /// </summary>
        public static void ClearCache()
        {
            pathCache.Clear();
        }

        /// <summary>
        /// GameObjectのパスを取得（キャッシュ利用）
        /// </summary>
        public static string GetGameObjectPath(GameObject go, GameObject targetObject)
        {
            if (go == null) return "null";

            var cacheKey = (go, targetObject);

            // キャッシュにパスが存在する場合はそれを返す
            if (pathCache.TryGetValue(cacheKey, out string cachedPath))
            {
                return cachedPath;
            }

            string path = CalculateGameObjectPath(go, targetObject);

            // キャッシュに保存
            pathCache[cacheKey] = path;

            return path;
        }

        /// <summary>
        /// 実際のパス計算
        /// </summary>
        private static string CalculateGameObjectPath(GameObject go, GameObject targetObject)
        {
            if (go == null) return "null";

            try
            {
                // targetObjectがnullの場合は、従来通りの完全パスを返す
                if (targetObject == null)
                {
                    return GetFullPath(go);
                }

                // 対象がtargetObject自身の場合は、targetObjectの名前を返す
                if (go == targetObject)
                {
                    return targetObject.name;
                }

                // targetObjectからの相対パスを構築する
                if (IsChildOf(go.transform, targetObject.transform))
                {
                    // goがtargetObjectの子孫である場合
                    return targetObject.name + "/" + GetRelativePathFromAncestor(go.transform, targetObject.transform);
                }
                else if (IsChildOf(targetObject.transform, go.transform))
                {
                    // targetObjectがgoの子孫である場合
                    string upPath = "";
                    Transform parent = targetObject.transform.parent;
                    Transform goTransform = go.transform;

                    while (parent != null && parent != goTransform)
                    {
                        upPath += "../";
                        parent = parent.parent;
                    }

                    if (parent == goTransform)
                    {
                        return targetObject.name + "/" + upPath.TrimEnd('/');
                    }
                }

                // 共通の祖先を見つけて相対パスを構築
                Transform commonAncestor = FindCommonAncestor(go.transform, targetObject.transform);
                if (commonAncestor != null)
                {
                    string upPath = "";
                    Transform current = targetObject.transform;

                    // targetObjectから共通祖先までの上方向のパス
                    while (current != null && current != commonAncestor)
                    {
                        upPath += "../";
                        current = current.parent;
                    }

                    // 共通祖先からgoまでの下方向のパス
                    string downPath = GetRelativePathFromAncestor(go.transform, commonAncestor);

                    return targetObject.name + "/" + (upPath + downPath).TrimEnd('/');
                }

                // 関連性がない場合は完全パスを返す（ターゲットオブジェクト名を先頭に付ける）
                return targetObject.name + " → " + GetFullPath(go);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while getting path: {ex.Message}");
                return go.name;
            }
        }

        // あるTransformが別のTransformの子孫であるかチェック
        private static bool IsChildOf(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;

            Transform current = child.parent;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = current.parent;
            }
            return false;
        }

        // 祖先からの相対パスを取得
        public static string GetRelativePathFromAncestor(Transform descendant, Transform ancestor)
        {
            if (descendant == null || ancestor == null) return string.Empty;

            if (descendant == ancestor)
                return ".";

            string path = descendant.name;
            Transform parent = descendant.parent;

            while (parent != null && parent != ancestor)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        // 二つのTransform間の共通祖先を見つける
        private static Transform FindCommonAncestor(Transform t1, Transform t2)
        {
            if (t1 == null || t2 == null) return null;

            // t1の全祖先を格納
            HashSet<Transform> t1Ancestors = new HashSet<Transform>();
            Transform current = t1;

            while (current != null)
            {
                t1Ancestors.Add(current);
                current = current.parent;
            }

            // t2を辿りながら、t1の祖先セットと一致するものを探す
            current = t2;
            while (current != null)
            {
                if (t1Ancestors.Contains(current))
                    return current;
                current = current.parent;
            }

            return null; // 共通祖先なし
        }

        private static string GetFullPath(GameObject go)
        {
            if (go == null) return "null";

            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }

    /// <summary>
    /// UI描画を担当するクラス
    /// </summary>
    public class ComponentUIRenderer
    {
        private ComponentDataManager dataManager;
        private float gameObjectColumnWidth = 250f;
        private float componentColumnWidth = 250f;
        private bool isResizingGameObjectColumn = false;
        private float totalWidth = 0f;

        // スタイル定義
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
        /// UIスタイルを初期化
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
        /// ウィンドウ幅に基づいてカラム幅を調整
        /// </summary>
        public void AdjustColumnWidths(float windowWidth)
        {
            totalWidth = windowWidth;

            // 使用可能な全体幅からチェックボックスとリサイズハンドル幅を引く
            float availableWidth = totalWidth - (ComponentConstants.CHECKBOX_WIDTH * 2) - ComponentConstants.RESIZE_HANDLE_WIDTH - ComponentConstants.COLUMN_MARGIN;

            // GameObjectカラムとComponentカラムが両方とも最小幅以上になるよう調整
            float totalMinWidth = ComponentConstants.MIN_COLUMN_WIDTH * 2;
            if (availableWidth < totalMinWidth)
            {
                // 利用可能な幅が足りない場合は、均等に分配
                gameObjectColumnWidth = availableWidth / 2;
                componentColumnWidth = availableWidth / 2;
            }
            else
            {
                // GameObjectカラムの最大幅を全幅の60%以下に制限
                float maxGameObjectWidth = availableWidth * ComponentConstants.MAX_COLUMN_RATIO;
                gameObjectColumnWidth = Mathf.Min(gameObjectColumnWidth, maxGameObjectWidth);

                // GameObjectカラムが最小幅を下回らないようにする
                gameObjectColumnWidth = Mathf.Max(gameObjectColumnWidth, ComponentConstants.MIN_COLUMN_WIDTH);

                // componentColumnWidthはGameObjectカラム幅に応じて調整（最小幅を確保）
                componentColumnWidth = Mathf.Max(availableWidth - gameObjectColumnWidth, ComponentConstants.MIN_COLUMN_WIDTH);

                // 両方のカラム幅合計が利用可能幅を超える場合は調整
                if (gameObjectColumnWidth + componentColumnWidth > availableWidth)
                {
                    // 比率を維持しながら調整
                    float ratio = gameObjectColumnWidth / (gameObjectColumnWidth + componentColumnWidth);
                    gameObjectColumnWidth = availableWidth * ratio;
                    componentColumnWidth = availableWidth * (1 - ratio);
                }
            }
        }

        /// <summary>
        /// テーブルヘッダーを描画
        /// </summary>
        public void DrawTableHeader(Rect totalRect, List<GameObject> filteredGameObjects, List<ComponentInfo> filteredComponents)
        {
            float startX = totalRect.x;

            // GameObject チェックボックス列
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
                // 混合状態でクリックされた場合は常に全解除する
                bool newState = newAllGameObjectsSelected;
                if (mixedGameObjectSelection)
                {
                    newState = false;
                }

                // GameObjectのみ選択状態を更新（コンポーネントは連動させない）
                foreach (var go in filteredGameObjects)
                {
                    dataManager.GameObjectSelectionState[go] = newState;
                }
            }

            // GameObject ヘッダー
            Rect gameObjectHeaderRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y, gameObjectColumnWidth, totalRect.height);
            EditorGUI.LabelField(gameObjectHeaderRect, "GameObject", headerLabelStyle);

            // リサイズハンドル
            Rect resizeHandleRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth, totalRect.y, ComponentConstants.RESIZE_HANDLE_WIDTH, totalRect.height);
            EditorGUI.LabelField(resizeHandleRect, "|", EditorStyles.boldLabel);
            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

            // Component チェックボックス列
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
                // 混合状態でクリックされた場合は常に全解除する
                bool newState = newAllComponentsSelected;
                if (mixedComponentSelection)
                {
                    newState = false;
                }

                // フィルター後の表示対象のみを更新
                foreach (var comp in filteredComponents)
                {
                    comp.IsSelected = newState;
                }
            }

            // Component ヘッダー
            Rect componentHeaderRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth + ComponentConstants.RESIZE_HANDLE_WIDTH + ComponentConstants.CHECKBOX_WIDTH,
                                              totalRect.y, componentColumnWidth, totalRect.height);
            EditorGUI.LabelField(componentHeaderRect, "Component", headerLabelStyle);
        }

        /// <summary>
        /// GameObjectとそのコンポーネントの行を描画
        /// </summary>
        public void DrawCombinedRow(GameObject gameObj, List<ComponentInfo> components, GameObject targetObject)
        {
            if (gameObj == null) return;

            try
            {
                // 行の高さを計算 (GameObject + コンポーネント数に基づく)
                int componentCount = components.Count;
                int calculatedRowHeight = Mathf.Max((int)ComponentConstants.MIN_ROW_HEIGHT, (int)(ComponentConstants.ROW_HEIGHT + componentCount * ComponentConstants.ROW_HEIGHT)); // 最低高さは36px

                Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true), GUILayout.Height(calculatedRowHeight));
                float startX = totalRect.x;

                // GameObject チェックボックス - GameObject名と同じ高さに配置
                Rect goCheckboxRect = new Rect(startX, totalRect.y, ComponentConstants.CHECKBOX_WIDTH, ComponentConstants.ROW_HEIGHT);
                bool isGameObjectSelected = dataManager.GameObjectSelectionState[gameObj];

                // EditorGUI.Toggleを使用して状態変更を検出
                bool newIsGameObjectSelected = EditorGUI.Toggle(goCheckboxRect, isGameObjectSelected);
                if (newIsGameObjectSelected != isGameObjectSelected)
                {
                    dataManager.GameObjectSelectionState[gameObj] = newIsGameObjectSelected;
                }

                // GameObject名
                Rect nameRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y, gameObjectColumnWidth, ComponentConstants.ROW_HEIGHT);
                EditorGUI.LabelField(nameRect, gameObj.name, headerLabelStyle);

                // GameObject名の部分をクリックできるように
                Event evt = Event.current;
                if (evt.type == EventType.MouseDown && nameRect.Contains(evt.mousePosition))
                {
                    Event evtCopy = new Event(evt);
                    evt.Use();
                    SelectGameObjectInHierarchy(gameObj, targetObject);
                    EditorWindow.GetWindow<ComponentManager>().Repaint();
                }

                // GameObject パス
                Rect pathRect = new Rect(startX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y + 18, gameObjectColumnWidth, 16);
                string path = ComponentPathUtility.GetGameObjectPath(gameObj, targetObject);
                EditorGUI.LabelField(pathRect, path, pathLabelStyle);

                // パスの部分もクリックできるように
                if (evt.type == EventType.MouseDown && pathRect.Contains(evt.mousePosition))
                {
                    Event evtCopy = new Event(evt);
                    evt.Use();
                    SelectGameObjectInHierarchy(gameObj, targetObject);
                    EditorWindow.GetWindow<ComponentManager>().Repaint();
                }

                // コンポーネント部分
                float componentStartX = startX + ComponentConstants.CHECKBOX_WIDTH + gameObjectColumnWidth + ComponentConstants.RESIZE_HANDLE_WIDTH;

                // 各コンポーネントを描画（インデントなしで縦に並べる）
                for (int i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    if (component == null) continue;

                    float yOffset = i * ComponentConstants.ROW_HEIGHT;

                    // コンポーネントのチェックボックス
                    Rect compCheckboxRect = new Rect(componentStartX, totalRect.y + yOffset, ComponentConstants.CHECKBOX_WIDTH, ComponentConstants.ROW_HEIGHT);
                    bool isCompSelected = component.IsSelected;

                    // EditorGUI.Toggleを使用して状態変更を検出
                    bool newIsCompSelected = EditorGUI.Toggle(compCheckboxRect, isCompSelected);
                    if (newIsCompSelected != isCompSelected)
                    {
                        component.IsSelected = newIsCompSelected;
                    }

                    // コンポーネントアイコン
                    GUIContent content = component.Component != null
                        ? EditorGUIUtility.ObjectContent(component.Component, component.Component.GetType())
                        : new GUIContent("Missing");

                    Rect iconRect = new Rect(componentStartX + ComponentConstants.CHECKBOX_WIDTH, totalRect.y + yOffset, ComponentConstants.ICON_WIDTH, ComponentConstants.ROW_HEIGHT);
                    EditorGUI.LabelField(iconRect, new GUIContent(content.image));

                    // コンポーネント名
                    Rect compLabelRect = new Rect(componentStartX + ComponentConstants.CHECKBOX_WIDTH + ComponentConstants.ICON_WIDTH, totalRect.y + yOffset,
                                       componentColumnWidth - ComponentConstants.ICON_WIDTH, ComponentConstants.ROW_HEIGHT);
                    EditorGUI.LabelField(compLabelRect, component.Name, componentLabelStyle);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing row: {ex.Message}");
            }
        }

        /// <summary>
        /// リサイズハンドルの処理
        /// </summary>
        public void HandleColumnResize(Rect resizeHandleRect)
        {
            Event evt = Event.current;

            EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

            // リサイズハンドルの処理をイベントタイプごとに分離
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
        /// カラム幅を更新する
        /// </summary>
        private void UpdateColumnWidths(float deltaX)
        {
            float availableWidth = totalWidth - (ComponentConstants.CHECKBOX_WIDTH * 2) - ComponentConstants.RESIZE_HANDLE_WIDTH - ComponentConstants.COLUMN_MARGIN;

            // 新しい幅を計算
            float newWidth = gameObjectColumnWidth + deltaX;

            // 最小幅を確保しつつ、最大幅をウィンドウの60%以下に制限
            float maxAllowed = Mathf.Max(availableWidth * ComponentConstants.MAX_COLUMN_RATIO, ComponentConstants.MIN_COLUMN_WIDTH);
            newWidth = Mathf.Clamp(newWidth, ComponentConstants.MIN_COLUMN_WIDTH, maxAllowed);

            // コンポーネント列も最小幅を下回らないようにする
            float remainingWidth = availableWidth - newWidth;
            if (remainingWidth < ComponentConstants.MIN_COLUMN_WIDTH)
            {
                newWidth = availableWidth - ComponentConstants.MIN_COLUMN_WIDTH;
            }

            // 変更があればレイアウトを更新
            if (newWidth != gameObjectColumnWidth)
            {
                gameObjectColumnWidth = newWidth;
                componentColumnWidth = availableWidth - gameObjectColumnWidth;
            }
        }

        /// <summary>
        /// GameObjectをHierarchy上で選択する
        /// </summary>
        private void SelectGameObjectInHierarchy(GameObject gameObj, GameObject targetObject)
        {
            if (gameObj == null) return;

            try
            {
                // 現在のPrefab編集モード情報を取得
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

                // Prefab編集モードの場合、対応するPrefab編集モード内のオブジェクトを取得
                GameObject objectToSelect = GetCorrespondingPrefabModeObject(gameObj, prefabStage, targetObject);

                // ヒエラルキーで選択
                Selection.activeObject = objectToSelect;

                // エディタのフォーカスをHierarchyビューに移す
                try
                {
                    // まずSceneHierarchyWindowを取得してみる
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

                    // フォールバック
                    EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
                }
                catch (System.Exception)
                {
                    // 何らかの例外が発生した場合のフォールバック
                    EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred during hierarchy selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefab編集モード内の対応するGameObjectを取得
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, UnityEditor.SceneManagement.PrefabStage prefabStage, GameObject targetObject)
        {
            if (prefabStage == null)
                return gameObject;

            try
            {
                // 直接インスタンスIDで対応関係を確認
                if (PrefabUtility.IsPartOfPrefabInstance(prefabStage.prefabContentsRoot) &&
                    PrefabUtility.IsPartOfPrefabInstance(gameObject))
                {
                    // 編集中のPrefabアセットとターゲットオブジェクトが同じPrefab階層にある可能性がある
                    GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabStage.prefabContentsRoot);
                    GameObject gameObjectPrefabAsset = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject);

                    if (prefabAsset == gameObjectPrefabAsset)
                    {
                        // 同じPrefabの一部なので、相対パスを使用して対応するオブジェクトを見つける
                        string relativePath = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
                        if (!string.IsNullOrEmpty(relativePath))
                        {
                            Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(relativePath);
                            if (childTransform != null)
                                return childTransform.gameObject;
                        }
                    }
                }

                // パス解決によるフォールバック
                string path = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
                if (!string.IsNullOrEmpty(path) && path != ".")
                {
                    Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(path);
                    if (childTransform != null)
                        return childTransform.gameObject;
                }

                // 同じ名前のオブジェクトを探す（最終手段）
                if (gameObject != targetObject)
                {
                    // 深さ優先探索でGameObjectをPrefabステージから探す
                    return FindMatchingGameObjectInPrefab(gameObject.name, prefabStage.prefabContentsRoot);
                }

                return gameObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while getting prefab mode object: {ex.Message}");
                return gameObject;
            }
        }

        /// <summary>
        /// 名前に基づいて一致するGameObjectをPrefab内で探す
        /// </summary>
        private GameObject FindMatchingGameObjectInPrefab(string objectName, GameObject root)
        {
            if (root.name == objectName)
                return root;

            // 子オブジェクトを再帰的に探索
            foreach (Transform child in root.transform)
            {
                GameObject result = FindMatchingGameObjectInPrefab(objectName, child.gameObject);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// フィルター入力欄を描画
        /// </summary>
        public (string gameObjectFilter, string componentFilter, bool searchInPaths, bool showAllComponentsOnMatch, bool showEmptyObjects)
            DrawFilters(string gameObjectFilter, string componentFilter, bool searchInPaths, bool showAllComponentsOnMatch, bool showEmptyObjects)
        {
            try
            {
                // フィルター入力欄
                EditorGUILayout.BeginHorizontal();

                // チェックボックス分の空白
                GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH);

                // GameObjectフィルター (GameObject列と同じ幅を使用、最小幅も設定)
                EditorGUILayout.BeginVertical(GUILayout.Width(gameObjectColumnWidth), GUILayout.MinWidth(ComponentConstants.MIN_COLUMN_WIDTH));
                EditorGUILayout.LabelField("GameObject Filter:", headerLabelStyle);
                string newGameObjectFilter = EditorGUILayout.TextField(gameObjectFilter);

                // GameObject フィルターオプション
                bool newSearchInPaths = EditorGUILayout.ToggleLeft(" Include paths in search", searchInPaths);

                EditorGUILayout.EndVertical();

                // リサイズハンドル分の空白
                GUILayout.Space(ComponentConstants.RESIZE_HANDLE_WIDTH);

                // チェックボックス分の空白
                GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH);

                // Componentフィルター (残りの幅を使用、最小幅も設定)
                EditorGUILayout.BeginVertical(GUILayout.Width(componentColumnWidth), GUILayout.MinWidth(ComponentConstants.MIN_COLUMN_WIDTH));
                EditorGUILayout.LabelField("Component Filter:", headerLabelStyle);
                string newComponentFilter = EditorGUILayout.TextField(componentFilter);

                // コンポーネント フィルターオプション
                bool newShowAllComponentsOnMatch = EditorGUILayout.ToggleLeft(" Show all components of matching GameObjects", showAllComponentsOnMatch);

                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();

                // 追加オプション（横並び）
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(ComponentConstants.CHECKBOX_WIDTH); // 左側の余白を合わせる

                // コンポーネントがないオブジェクトも表示するオプション
                bool newShowEmptyObjects = EditorGUILayout.ToggleLeft(" Show GameObjects with no components", showEmptyObjects);
                if (newShowEmptyObjects != showEmptyObjects)
                {
                    dataManager.ShowEmptyObjects = newShowEmptyObjects;
                    showEmptyObjects = newShowEmptyObjects;
                }
                else
                {
                    // もしかするとdataManagerの値と一致していない可能性があるので同期する
                    showEmptyObjects = dataManager.ShowEmptyObjects;
                }

                EditorGUILayout.EndHorizontal();

                return (newGameObjectFilter, newComponentFilter, newSearchInPaths, newShowAllComponentsOnMatch, showEmptyObjects);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing filters: {ex.Message}");
                return (gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch, showEmptyObjects);
            }
        }

        /// <summary>
        /// ターゲットオブジェクトフィールドを描画
        /// </summary>
        public GameObject DrawTargetObjectField(GameObject targetObject, ComponentDataManager dataManager)
        {
            try
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Target Object", GUILayout.Width(EditorGUIUtility.labelWidth - 30));

                // 更新ボタンを追加（ラベルの後、フィールドの前）
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(30), GUILayout.Height(18)))
                {
                    if (targetObject != null)
                    {
                        // 現在のチェック状態を保存
                        Dictionary<GameObject, bool> savedGameObjectSelectionState = new Dictionary<GameObject, bool>(dataManager.GameObjectSelectionState);
                        Dictionary<Component, bool> savedComponentSelectionState = new Dictionary<Component, bool>();

                        // 各コンポーネントの選択状態を保存
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

                        // GameObjectのチェック状態を復元
                        foreach (var gameObject in dataManager.GameObjectSelectionState.Keys.ToList())
                        {
                            if (savedGameObjectSelectionState.ContainsKey(gameObject))
                            {
                                dataManager.GameObjectSelectionState[gameObject] = savedGameObjectSelectionState[gameObject];
                            }
                        }

                        // コンポーネントのチェック状態を復元
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

                // オブジェクトフィールド
                EditorGUI.BeginChangeCheck();
                GameObject newTargetObject = EditorGUILayout.ObjectField("", targetObject, typeof(GameObject), true) as GameObject;
                if (EditorGUI.EndChangeCheck() && newTargetObject != null)
                {
                    dataManager.RefreshComponentsList(newTargetObject);
                    return newTargetObject;
                }

                EditorGUILayout.EndHorizontal();
                return targetObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing target object field: {ex.Message}");
                return targetObject;
            }
        }
    }

    /// <summary>
    /// ComponentManager：指定したオブジェクトとその子オブジェクトのコンポーネントを一覧表示して削除できる
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

        // データ管理とUI描画のクラス
        private ComponentDataManager dataManager;
        private ComponentUIRenderer uiRenderer;

        [MenuItem("Tools/Kanameliser Editor Plus/Component Manager")]
        public static void ShowWindow()
        {
            GetWindow<ComponentManager>("Component Manager");
        }

        private void OnEnable()
        {
            try
            {
                dataManager = new ComponentDataManager();
                uiRenderer = new ComponentUIRenderer(dataManager);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while initializing ComponentManager: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            try
            {
                // ウィンドウ幅に基づいてカラム幅を調整
                uiRenderer.AdjustColumnWidths(EditorGUIUtility.currentViewWidth);

                GUILayout.Label("Component Manager", EditorStyles.boldLabel);

                EditorGUILayout.Space();

                // ターゲットオブジェクトフィールドを描画
                GameObject previousTarget = targetObject;
                targetObject = uiRenderer.DrawTargetObjectField(targetObject, dataManager);

                // ターゲットオブジェクト変更時のリスト更新
                if (previousTarget != targetObject && targetObject != null)
                {
                    dataManager.RefreshComponentsList(targetObject);
                }

                EditorGUILayout.Space();

                // フィルター入力欄を描画
                var (newGameObjectFilter, newComponentFilter, newSearchInPaths, newShowAllComponentsOnMatch, newShowEmptyObjects) =
                    uiRenderer.DrawFilters(gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch, showEmptyObjects);

                // フィルター設定が変更された場合は更新
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

                    // showEmptyObjectsが変更された場合は、リストを更新
                    if (emptyObjectsSettingChanged && targetObject != null)
                    {
                        dataManager.RefreshComponentsList(targetObject);
                    }
                }

                EditorGUILayout.Space();

                if (targetObject != null && dataManager.ComponentsByGameObject.Count > 0)
                {
                    DrawTableLayout();

                    // リサイズ中は次のフレームでも再描画を要求
                    if (uiRenderer.IsResizingGameObjectColumn)
                    {
                        Repaint();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Please select a GameObject to display components", MessageType.Info);
                }

                EditorGUILayout.Space();
                DrawButtons();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing ComponentManager GUI: {ex.Message}");
            }
        }

        private void DrawTableLayout()
        {
            try
            {
                // テーブルヘッダー上部の区切り線
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

                // 固定レイアウトのヘッダー行
                Rect totalRect = EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true));

                // フィルター後の表示対象となるGameObjectとComponent
                var (filteredGameObjects, filteredComponents) = dataManager.GetFilteredItems(
                    targetObject, gameObjectFilter, componentFilter, searchInPaths, showAllComponentsOnMatch);

                // テーブルヘッダーを描画
                uiRenderer.DrawTableHeader(totalRect, filteredGameObjects, filteredComponents);

                // リサイズハンドルの処理
                Rect resizeHandleRect = new Rect(
                    totalRect.x + ComponentConstants.CHECKBOX_WIDTH + uiRenderer.GameObjectColumnWidth,
                    totalRect.y,
                    ComponentConstants.RESIZE_HANDLE_WIDTH,
                    totalRect.height);
                uiRenderer.HandleColumnResize(resizeHandleRect);

                // ヘッダー下部の区切り線
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));

                // スクロール可能なコンテンツ領域
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);


                // フィルターされたGameObjectを描画
                foreach (var gameObject in filteredGameObjects)
                {
                    if (gameObject == null) continue;

                    // このGameObjectに関連するフィルター済みコンポーネントのリスト
                    var components = dataManager.ComponentsByGameObject[gameObject];

                    // コンポーネントフィルタリングを共通メソッドを使用して実行
                    List<ComponentInfo> gameObjectFilteredComponents = dataManager.FilterComponentsByName(
                        components, componentFilter, showAllComponentsOnMatch);

                    // 統合された行を描画（GameObjectとそのコンポーネント）
                    uiRenderer.DrawCombinedRow(gameObject, gameObjectFilteredComponents, targetObject);

                    // 区切り線
                    EditorGUILayout.Space();
                    Rect rect = EditorGUILayout.GetControlRect(false, 1);
                    EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
                    EditorGUILayout.Space();
                }

                EditorGUILayout.EndScrollView();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing table layout: {ex.Message}");
            }
        }

        private void DrawButtons()
        {
            try
            {
                EditorGUILayout.BeginHorizontal();

                // 選択されたGameObjectとコンポーネントを取得
                var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

                // GameObjectまたはComponentが選択されているかチェック
                bool anyGameObjectSelected = selectedGameObjects.Count > 0;
                bool anyComponentSelected = selectedComponents.Count > 0;

                // 選択ボタン - 条件に基づいて有効/無効を制御
                bool canSelect = CanSelectInHierarchy() && (anyGameObjectSelected || anyComponentSelected);
                GUI.enabled = canSelect;
                if (GUILayout.Button("Select in Hierarchy", GUILayout.Height(30)))
                {
                    SelectInHierarchy();
                }

                // 削除ボタン
                GUI.enabled = anyGameObjectSelected || anyComponentSelected;
                if (GUILayout.Button("Remove Selected Items", GUILayout.Height(30)))
                {
                    RemoveSelectedComponents();
                }

                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing buttons: {ex.Message}");
            }
        }

        // Hierarchy内での選択が可能かどうかを判定
        private bool CanSelectInHierarchy()
        {
            if (targetObject == null) return false;

            try
            {
                // 現在のPrefab編集モード情報を取得
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                bool isPrefabMode = prefabStage != null;

                // ケース1: オブジェクトがシーン内オブジェクト（Hierarchy上のオブジェクト）
                bool isSceneObject = !PrefabUtility.IsPartOfPrefabAsset(targetObject) &&
                                   !EditorUtility.IsPersistent(targetObject);

                // ケース2: Prefab編集モードで開いているPrefabと同じPrefabがAssetsから指定された
                bool isPrefabAssetMatchingCurrentStage = false;
                if (isPrefabMode && PrefabUtility.IsPartOfPrefabAsset(targetObject))
                {
                    // 編集中のPrefabアセットとターゲットオブジェクトのPrefabアセットが同じか確認
                    GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabStage.prefabContentsRoot);
                    GameObject targetPrefabAsset = targetObject;
                    isPrefabAssetMatchingCurrentStage = (prefabAsset == targetPrefabAsset);
                }

                // いずれかの条件を満たせば選択可能
                return isSceneObject || isPrefabAssetMatchingCurrentStage;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while determining hierarchy selection: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prefab編集モード内の対応するGameObjectを取得
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, UnityEditor.SceneManagement.PrefabStage prefabStage)
        {
            if (prefabStage == null || !PrefabUtility.IsPartOfPrefabAsset(targetObject))
                return gameObject;

            try
            {
                string relativePath = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
                Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(relativePath);

                return childTransform != null ? childTransform.gameObject : gameObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while getting prefab mode object: {ex.Message}");
                return gameObject;
            }
        }

        // ヒエラルキー内で選択する処理
        private void SelectInHierarchy()
        {
            if (targetObject == null) return;

            try
            {
                // 選択されたGameObjectとコンポーネントを取得
                var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

                // 選択用オブジェクトリスト
                List<UnityEngine.Object> objectsToSelect = new List<UnityEngine.Object>();

                // 現在のPrefab編集モード情報を取得
                var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();

                // チェックされたGameObjectを収集
                foreach (var gameObject in selectedGameObjects)
                {
                    if (gameObject == null) continue;

                    // Prefab編集モードの場合、対応するPrefab編集モード内のオブジェクトを取得
                    GameObject objectToAdd = GetCorrespondingPrefabModeObject(gameObject, prefabStage);
                    objectsToSelect.Add(objectToAdd);
                }

                // コンポーネントのチェック状態を確認し、対応するGameObjectを追加
                foreach (var compInfo in selectedComponents)
                {
                    if (compInfo == null || compInfo.GameObject == null) continue;

                    GameObject gameObject = compInfo.GameObject;
                    if (!selectedGameObjects.Contains(gameObject)) // GameObjectが直接選択されていない場合のみ
                    {
                        // Prefab編集モードの場合、対応するPrefab編集モード内のオブジェクトを取得
                        GameObject objectToAdd = GetCorrespondingPrefabModeObject(gameObject, prefabStage);
                        objectsToSelect.Add(objectToAdd);
                    }
                }

                // 重複を除去
                objectsToSelect = objectsToSelect.Distinct().ToList();

                // ヒエラルキーで選択
                if (objectsToSelect.Count > 0)
                {
                    Selection.objects = objectsToSelect.ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred during hierarchy selection: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択されたアイテムの削除確認メッセージを作成
        /// </summary>
        private string CreateDeleteConfirmMessage(List<GameObject> selectedGameObjects, List<ComponentInfo> selectedComponents)
        {
            try
            {
                string confirmMessage = "Are you sure you want to delete the following items?\n\n";

                // 選択されたGameObjectの一覧を追加
                if (selectedGameObjects.Count > 0)
                {
                    confirmMessage += "【Selected GameObjects】\n";
                    for (int i = 0; i < selectedGameObjects.Count; i++)
                    {
                        if (selectedGameObjects[i] == null) continue;

                        if (i < 10 || i == selectedGameObjects.Count - 1) // 最初の10個と最後の1個を表示
                        {
                            confirmMessage += "- " + ComponentPathUtility.GetGameObjectPath(selectedGameObjects[i], targetObject) + "\n";
                        }
                        else if (i == 10) // 省略表示
                        {
                            confirmMessage += "- ... (and " + (selectedGameObjects.Count - 10) + " more GameObjects)\n";
                            break;
                        }
                    }
                    confirmMessage += "\n";
                }

                // 選択されたコンポーネントの一覧を追加
                if (selectedComponents.Count > 0)
                {
                    confirmMessage += "【Selected Components】\n";
                    for (int i = 0; i < selectedComponents.Count; i++)
                    {
                        if (selectedComponents[i] == null || selectedComponents[i].GameObject == null) continue;

                        if (i < 10 || i == selectedComponents.Count - 1) // 最初の10個と最後の1個を表示
                        {
                            var comp = selectedComponents[i];
                            confirmMessage += "- " + ComponentPathUtility.GetGameObjectPath(comp.GameObject, targetObject) + " : " + comp.Name + "\n";
                        }
                        else if (i == 10) // 省略表示
                        {
                            confirmMessage += "- ... (and " + (selectedComponents.Count - 10) + " more Components)\n";
                            break;
                        }
                    }
                }

                confirmMessage += "\nThis action cannot be undone.";
                return confirmMessage;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while creating deletion confirmation message: {ex.Message}");
                return "Error creating confirmation message. Do you want to proceed with deletion?";
            }
        }

        private void RemoveSelectedComponents()
        {
            try
            {
                // 選択されたGameObjectとコンポーネントを取得
                var (selectedGameObjects, selectedComponents) = dataManager.GetSelectedItems();

                if (selectedGameObjects.Count == 0 && selectedComponents.Count == 0) return;

                // 確認ダイアログに表示するメッセージを作成
                string confirmMessage = CreateDeleteConfirmMessage(selectedGameObjects, selectedComponents);

                // 確認ダイアログを表示
                bool confirmResult = EditorUtility.DisplayDialog(
                    "Confirm Deletion",
                    confirmMessage,
                    "Cancel",
                    "Delete"
                );

                // ユーザーが「削除する」を選んだ場合のみ処理を実行
                if (!confirmResult)
                {
                    // 処理を Undo でまとめる
                    Undo.SetCurrentGroupName("Remove GameObjects and Components");
                    int undoGroup = Undo.GetCurrentGroup();

                    // 選択されたGameObjectを削除
                    foreach (var gameObject in selectedGameObjects)
                    {
                        if (gameObject != null)
                        {
                            Undo.DestroyObjectImmediate(gameObject);
                        }
                    }

                    // 選択されたコンポーネントを削除
                    foreach (var componentInfo in selectedComponents)
                    {
                        if (componentInfo != null && componentInfo.Component != null)
                        {
                            Undo.DestroyObjectImmediate(componentInfo.Component);
                        }
                    }

                    Undo.CollapseUndoOperations(undoGroup);
                    dataManager.RefreshComponentsList(targetObject);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while removing components: {ex.Message}");
            }
        }
    }
}
