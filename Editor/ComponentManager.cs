using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
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
            dataManager = new ComponentDataManager();
            uiRenderer = new ComponentUIRenderer(dataManager);
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
            catch (Exception ex) when (!(ex is ExitGUIException))
            {
                Debug.LogError($"Error occurred while drawing ComponentManager GUI: {ex.Message}");
            }
        }

        private void DrawTableLayout()
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

        private void DrawButtons()
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

        // Hierarchy内での選択が可能かどうかを判定
        private bool CanSelectInHierarchy()
        {
            if (targetObject == null) return false;

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

        /// <summary>
        /// Prefab編集モード内の対応するGameObjectを取得
        /// </summary>
        private GameObject GetCorrespondingPrefabModeObject(GameObject gameObject, UnityEditor.SceneManagement.PrefabStage prefabStage)
        {
            if (prefabStage == null || !PrefabUtility.IsPartOfPrefabAsset(targetObject))
                return gameObject;

            string relativePath = ComponentPathUtility.GetRelativePathFromAncestor(gameObject.transform, targetObject.transform);
            Transform childTransform = prefabStage.prefabContentsRoot.transform.Find(relativePath);

            return childTransform != null ? childTransform.gameObject : gameObject;
        }

        // ヒエラルキー内で選択する処理
        private void SelectInHierarchy()
        {
            if (targetObject == null) return;

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

        /// <summary>
        /// 選択されたアイテムの削除確認メッセージを作成
        /// </summary>
        private string CreateDeleteConfirmMessage(List<GameObject> selectedGameObjects, List<ComponentInfo> selectedComponents)
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

            confirmMessage += "\nUndo is available.";
            return confirmMessage;
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
                    "Delete",
                    "Cancel"
                );

                // ユーザーが「削除する」を選んだ場合のみ処理を実行
                if (confirmResult)
                {
                    // 処理を Undo でまとめる
                    Undo.SetCurrentGroupName("Remove GameObjects and Components");
                    int undoGroup = Undo.GetCurrentGroup();
                    List<string> failedItems = new List<string>();

                    try
                    {
                        // 選択されたGameObjectを削除
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

                        // 選択されたコンポーネントを削除
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
                            "Deletion Partially Completed",
                            $"Some selected items could not be deleted:\n\n{string.Join("\n", failedItems)}",
                            "OK"
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
