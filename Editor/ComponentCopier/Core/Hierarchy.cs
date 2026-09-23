using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>Lookups in a transform hierarchy, shared by the mappers, the planner and the executor.</summary>
    internal static class Hierarchy
    {
        /// <summary>True for <paramref name="root"/> itself and everything below it.</summary>
        public static bool IsInside(Transform transform, Transform root)
        {
            return transform != null && root != null && (transform == root || transform.IsChildOf(root));
        }

        /// <summary>Everything below <paramref name="root"/>, inactive objects included, in hierarchy order.</summary>
        public static List<Transform> Descendants(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToList();
        }

        /// <summary>The direct children, or nothing for a null parent.</summary>
        public static IEnumerable<Transform> Children(Transform parent)
        {
            if (parent == null) yield break;
            foreach (Transform child in parent)
                yield return child;
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

        /// <summary>
        /// The child with the given name and <see cref="SiblingOccurrence"/>, or null. The counterpart of
        /// <see cref="SiblingOccurrence"/>: same-name siblings are told apart by their order.
        /// </summary>
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
