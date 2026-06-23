using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
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

}
