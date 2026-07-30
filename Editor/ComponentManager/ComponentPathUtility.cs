using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Path-related utility class
    /// </summary>
    public static class ComponentPathUtility
    {
        // Path cache
        private static Dictionary<(GameObject, GameObject), string> pathCache = new Dictionary<(GameObject, GameObject), string>();

        /// <summary>
        /// Clears the path cache
        /// </summary>
        public static void ClearCache()
        {
            pathCache.Clear();
        }

        /// <summary>
        /// Gets the GameObject path (using the cache)
        /// </summary>
        public static string GetGameObjectPath(GameObject go, GameObject targetObject)
        {
            if (go == null) return "null";

            var cacheKey = (go, targetObject);

            // Return the cached path when available
            if (pathCache.TryGetValue(cacheKey, out string cachedPath))
            {
                return cachedPath;
            }

            string path = CalculateGameObjectPath(go, targetObject);

            // Store in the cache
            pathCache[cacheKey] = path;

            return path;
        }

        /// <summary>
        /// Performs the actual path calculation
        /// </summary>
        private static string CalculateGameObjectPath(GameObject go, GameObject targetObject)
        {
            if (go == null) return "null";

            try
            {
                // When targetObject is null, return the conventional full path
                if (targetObject == null)
                {
                    return GetFullPath(go);
                }

                // When the object is targetObject itself, return targetObject's name
                if (go == targetObject)
                {
                    return targetObject.name;
                }

                // Build the path relative to targetObject
                if (IsChildOf(go.transform, targetObject.transform))
                {
                    // go is a descendant of targetObject
                    return targetObject.name + "/" + GetRelativePathFromAncestor(go.transform, targetObject.transform);
                }
                else if (IsChildOf(targetObject.transform, go.transform))
                {
                    // targetObject is a descendant of go
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

                // Find the common ancestor and build a relative path
                Transform commonAncestor = FindCommonAncestor(go.transform, targetObject.transform);
                if (commonAncestor != null)
                {
                    string upPath = "";
                    Transform current = targetObject.transform;

                    // Upward path from targetObject to the common ancestor
                    while (current != null && current != commonAncestor)
                    {
                        upPath += "../";
                        current = current.parent;
                    }

                    // Downward path from the common ancestor to go
                    string downPath = GetRelativePathFromAncestor(go.transform, commonAncestor);

                    return targetObject.name + "/" + (upPath + downPath).TrimEnd('/');
                }

                // When unrelated, return the full path (prefixed with the target object's name)
                return targetObject.name + " → " + GetFullPath(go);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occurred while getting path: {ex.Message}");
                return go.name;
            }
        }

        // Check whether a Transform is a descendant of another Transform
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

        // Get the path relative to an ancestor
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

        // Find the common ancestor of two Transforms
        private static Transform FindCommonAncestor(Transform t1, Transform t2)
        {
            if (t1 == null || t2 == null) return null;

            // Store all ancestors of t1
            HashSet<Transform> t1Ancestors = new HashSet<Transform>();
            Transform current = t1;

            while (current != null)
            {
                t1Ancestors.Add(current);
                current = current.parent;
            }

            // Walk up from t2 looking for a match in t1's ancestor set
            current = t2;
            while (current != null)
            {
                if (t1Ancestors.Contains(current))
                    return current;
                current = current.parent;
            }

            return null; // No common ancestor
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
