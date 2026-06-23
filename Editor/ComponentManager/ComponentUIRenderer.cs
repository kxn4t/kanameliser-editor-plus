using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
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
                GameObject resultTargetObject = targetObject;
                if (EditorGUI.EndChangeCheck() && newTargetObject != null)
                {
                    dataManager.RefreshComponentsList(newTargetObject);
                    resultTargetObject = newTargetObject;
                }

                EditorGUILayout.EndHorizontal();
                return resultTargetObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while drawing target object field: {ex.Message}");
                return targetObject;
            }
        }
    }

}
