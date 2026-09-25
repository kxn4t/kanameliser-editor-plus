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
        /// True for the root of a nested prefab that the target lacks, which is brought over as a whole. A prefab of
        /// which the user mapped an object by hand is in the target, in part at least: its objects go where the map
        /// says, and the missing ones are created one by one like any other object.
        /// </summary>
        public static bool IsMissing(Transform transform, TransformMap map)
        {
            return GetPrefabAsset(transform, map.SourceRoot) != null && !map.TryResolve(transform, out _) &&
                   !map.HasManualMappingWithin(transform);
        }

        /// <summary>
        /// Lists the nested prefabs of the source that have no counterpart in the target, see <see cref="IsMissing"/>.
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
            else if (!Hierarchy.AnyAncestorBelow(scope, map.SourceRoot, t => IsMissing(t, map))) VisitChild(scope);
            return result;

            void Visit(Transform parent)
            {
                foreach (Transform child in parent)
                    VisitChild(child);
            }

            void VisitChild(Transform child)
            {
                if (IsMissing(child, map)) result.Add(child);
                else Visit(child);
            }
        }

        /// <summary>
        /// The objects and components of a prefab instance, by the object of the asset at
        /// <paramref name="assetPath"/> that each one corresponds to. Whatever was added to the instance
        /// corresponds to nothing and is left out.
        /// </summary>
        public static Dictionary<Object, Object> CorrespondingObjects(Transform instanceRoot, string assetPath)
        {
            var byAssetObject = new Dictionary<Object, Object>();
            foreach (var transform in instanceRoot.GetComponentsInChildren<Transform>(true))
            {
                Add(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>())
                {
                    // Missing scripts come back as null
                    if (component != null) Add(component);
                }
            }

            return byAssetObject;

            void Add(Object instanceObject)
            {
                var assetObject = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(instanceObject, assetPath);
                if (assetObject != null) byAssetObject[assetObject] = instanceObject;
            }
        }

        /// <summary>
        /// What <paramref name="instanceRoot"/> removed from <paramref name="asset"/>: the objects and components
        /// of the asset that no object or component of the instance corresponds to ("removed GameObject" and
        /// "removed component" overrides). Objects below a removed object are left out; they go with it.
        /// </summary>
        public static (List<GameObject> objects, List<Component> components) FindRemoved(
            Transform instanceRoot, GameObject asset)
        {
            var present = CorrespondingObjects(instanceRoot, AssetDatabase.GetAssetPath(asset));
            var objects = new List<GameObject>();
            var components = new List<Component>();

            foreach (var transform in asset.GetComponentsInChildren<Transform>(true))
            {
                if (!present.ContainsKey(transform.gameObject))
                {
                    bool belowRemoved = Hierarchy.AnyAncestorBelow(
                        transform, asset.transform, ancestor => !present.ContainsKey(ancestor.gameObject));
                    if (!belowRemoved) objects.Add(transform.gameObject);
                    continue;
                }

                foreach (var component in transform.GetComponents<Component>())
                {
                    // Missing scripts come back as null on both sides and are left alone
                    if (component == null || component is Transform || present.ContainsKey(component)) continue;
                    components.Add(component);
                }
            }

            return (objects, components);
        }
    }
}
