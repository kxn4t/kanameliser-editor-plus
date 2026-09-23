using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Empty objects of the source (anchors, organizing folders, leaf objects) that the target lacks.
    /// No component ever asks for them, so the component list cannot bring them over.
    /// </summary>
    internal static class MissingObjects
    {
        /// <summary>True for an object that has nothing but its Transform.</summary>
        public static bool IsEmpty(Transform transform)
        {
            // Missing scripts come back as null entries and still count as something
            return transform != null && transform.GetComponents<Component>().Length == 1;
        }

        /// <summary>
        /// True for an empty object that can be offered for creation. Bones are never created, nested prefabs
        /// are listed on their own, and an unconfirmed match may well be the counterpart.
        /// </summary>
        public static bool IsMissingEmpty(Transform transform, TransformMap map)
        {
            if (!IsEmpty(transform) || map.TryResolve(transform, out _)) return false;
            if (map.SourceSkeleton.IsBone(transform)) return false;
            if (NestedPrefabs.GetPrefabAsset(transform, map.SourceRoot) != null) return false;

            var mapping = map.Get(transform);
            return mapping == null || mapping.State != MappingState.NeedsReview;
        }

        /// <summary>
        /// Lists the missing empty objects. Only the topmost ones are returned: the empty objects below them
        /// are created along with them.
        /// </summary>
        /// <param name="scope">
        /// Limits the search to this part of the source (itself included), such as the source of a mirror copy
        /// within the avatar that the map spans. The topmost missing objects within it are returned, even when
        /// the objects above it are missing too: those are created on the way.
        /// </param>
        public static List<Transform> FindRoots(TransformMap map, Transform scope = null)
        {
            var result = new List<Transform>();
            if (scope == null || scope == map.SourceRoot) Visit(map.SourceRoot, false);
            else if (!NestedPrefabs.AnyAncestorBelow(scope, map.SourceRoot, IsOutOfReach)) VisitChild(scope, false);
            return result;

            // A missing prefab arrives as a whole, and nothing can be created below a missing bone
            bool IsOutOfReach(Transform transform) =>
                !map.TryResolve(transform, out _) &&
                (NestedPrefabs.GetPrefabAsset(transform, map.SourceRoot) != null ||
                 map.SourceSkeleton.IsBone(transform));

            void Visit(Transform parent, bool parentComesAlong)
            {
                foreach (Transform child in parent)
                    VisitChild(child, parentComesAlong);
            }

            void VisitChild(Transform child, bool parentComesAlong)
            {
                if (IsOutOfReach(child)) return;

                bool missingEmpty = IsMissingEmpty(child, map);
                if (missingEmpty && !parentComesAlong) result.Add(child);

                Visit(child, missingEmpty);
            }
        }

        /// <summary>Number of empty objects below <paramref name="root"/> that are created along with it.</summary>
        public static int CountBelow(Transform root, TransformMap map)
        {
            int count = 0;
            foreach (Transform child in root)
            {
                if (IsMissingEmpty(child, map)) count += 1 + CountBelow(child, map);
            }

            return count;
        }
    }
}
