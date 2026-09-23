using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Nested prefabs inside the source hierarchy, e.g. a prefab that bundles PhysBone settings, or a hat
    /// placed below the Head bone.
    /// </summary>
    internal static class NestedPrefabs
    {
        /// <summary>
        /// Returns the prefab asset when <paramref name="transform"/> is the root of a nested prefab instance
        /// below <paramref name="sourceRoot"/>, otherwise null.
        /// </summary>
        public static GameObject GetPrefabAsset(Transform transform, Transform sourceRoot)
        {
            if (transform == null || transform == sourceRoot) return null;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject)) return null;

            // Path based on purpose: GetCorrespondingObjectFromSource returns the object inside the outer
            // prefab, and ...FromOriginalSource skips variants and returns their base.
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Lists the nested prefabs of the source that have no counterpart in the target.
        /// Only the outermost ones are returned: a prefab inside a missing prefab arrives with it.
        /// </summary>
        /// <param name="scope">
        /// Limits the search to this part of the source (itself included), such as the source of a mirror copy
        /// within the avatar that the map spans.
        /// </param>
        public static List<Transform> FindMissingRoots(TransformMap map, Transform scope = null)
        {
            var result = new List<Transform>();
            if (scope == null || scope == map.SourceRoot) Visit(map.SourceRoot);
            // Inside a missing prefab, the outer prefab is what arrives
            else if (!AnyAncestorBelow(scope, map.SourceRoot, IsMissing)) VisitChild(scope);
            return result;

            bool IsMissing(Transform transform) =>
                GetPrefabAsset(transform, map.SourceRoot) != null && !map.TryResolve(transform, out _);

            void Visit(Transform parent)
            {
                foreach (Transform child in parent)
                    VisitChild(child);
            }

            void VisitChild(Transform child)
            {
                if (IsMissing(child)) result.Add(child);
                else Visit(child);
            }
        }

        /// <summary>True when an object between <paramref name="transform"/> and <paramref name="root"/> matches.</summary>
        public static bool AnyAncestorBelow(Transform transform, Transform root, Func<Transform, bool> predicate)
        {
            for (var ancestor = transform.parent; ancestor != null && ancestor != root; ancestor = ancestor.parent)
            {
                if (predicate(ancestor)) return true;
            }

            return false;
        }

        /// <summary>Index of a transform among its same-name siblings.</summary>
        public static int SiblingOccurrence(Transform transform)
        {
            if (transform.parent == null) return 0;

            int occurrence = 0;
            foreach (Transform sibling in transform.parent)
            {
                if (sibling == transform) break;
                if (sibling.name == transform.name) occurrence++;
            }

            return occurrence;
        }

        /// <param name="skip">Children that do not count, such as the ones a copy has just created.</param>
        public static Transform FindChild(Transform parent, string name, int occurrence, HashSet<Transform> skip = null)
        {
            int seen = 0;
            foreach (Transform child in parent)
            {
                if (child.name != name || (skip != null && skip.Contains(child))) continue;
                if (seen == occurrence) return child;
                seen++;
            }

            return null;
        }
    }
}
