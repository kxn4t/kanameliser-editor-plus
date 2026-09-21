using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Generic SerializedProperty traversal shared by reference remapping and diffing.
    /// Walking serialized data instead of typed fields keeps the tool free of hard SDK dependencies.
    /// </summary>
    internal static class ReferenceWalker
    {
        // Bookkeeping properties that must never be compared or remapped
        private static readonly HashSet<string> IgnoredPaths = new()
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_GameObject",
            "m_Script",
            "m_EditorHideFlags",
            "m_EditorClassIdentifier",
            "m_Name",
        };

        /// <summary>
        /// Enumerates leaf properties (including hidden ones, which can hold references too).
        /// The yielded property is the live iterator; copy what you need before moving on.
        /// </summary>
        public static IEnumerable<SerializedProperty> Leaves(SerializedObject serializedObject)
        {
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                if (iterator.depth == 0 && IgnoredPaths.Contains(iterator.propertyPath))
                {
                    enterChildren = false;
                    continue;
                }

                bool isContainer = iterator.propertyType == SerializedPropertyType.Generic ||
                                   iterator.propertyType == SerializedPropertyType.ManagedReference;
                enterChildren = isContainer;

                if (!isContainer) yield return iterator;
            }
        }

        /// <summary>
        /// Returns the transform a referenced object lives on, or null for non-hierarchy objects (assets).
        /// </summary>
        public static Transform GetTransform(Object value)
        {
            return value switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null,
            };
        }

        public static bool IsInside(Transform transform, Transform root)
        {
            return transform != null && root != null && (transform == root || transform.IsChildOf(root));
        }
    }
}
