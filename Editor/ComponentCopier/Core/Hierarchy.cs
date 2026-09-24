using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
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

        /// <summary>
        /// A path below <paramref name="root"/> that tells same-name siblings apart, for keys: every segment
        /// carries the <see cref="SiblingOccurrence"/> of its object ("Hips#0/Chain#1"). Empty for the root
        /// itself. Not meant to be shown; the plain path is <see cref="ObjectMatcher.GetRelativePathFromRoot"/>.
        /// </summary>
        public static string IdentityPath(Transform transform, Transform root)
        {
            var segments = new List<string>();
            for (var current = transform; current != null && current != root; current = current.parent)
                segments.Add(current.name + "#" + SiblingOccurrence(current));
            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>
        /// The <see cref="IdentityPath"/> of <paramref name="root"/> and everything below it, in one walk:
        /// counting the same-name siblings anew for every object would go through a bone's hundreds of children
        /// once per child.
        /// </summary>
        public static Dictionary<Transform, string> IdentityPaths(Transform root)
        {
            var paths = new Dictionary<Transform, string> { [root] = "" };
            var occurrences = new Dictionary<(Transform parent, string name), int>();

            // Parents come before their children
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == root) continue;

                var key = (transform.parent, transform.name);
                occurrences.TryGetValue(key, out int occurrence);
                occurrences[key] = occurrence + 1;

                string parentPath = paths[transform.parent];
                string segment = transform.name + "#" + occurrence;
                paths[transform] = parentPath.Length == 0 ? segment : parentPath + "/" + segment;
            }

            return paths;
        }
    }
}
