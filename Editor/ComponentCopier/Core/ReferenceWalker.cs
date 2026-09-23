using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>An object reference property of a component and what it points at.</summary>
    internal readonly struct ReferenceSlot
    {
        public readonly string PropertyPath;
        public readonly Object Value;

        /// <summary>
        /// For the object half of an MA AvatarObjectReference: the property path of the path half.
        /// Null for ordinary references.
        /// </summary>
        public readonly string PathPropertyPath;

        public ReferenceSlot(string propertyPath, Object value, string pathPropertyPath)
        {
            PropertyPath = propertyPath;
            Value = value;
            PathPropertyPath = pathPropertyPath;
        }
    }

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
        /// Enumerates the object references a component holds, with the object each one effectively points at.
        /// An MA AvatarObjectReference whose object half is empty still points somewhere through its path,
        /// which is resolved against <paramref name="avatarRoot"/>. Empty references are left out.
        /// </summary>
        public static IEnumerable<ReferenceSlot> References(SerializedObject serializedObject, Transform avatarRoot)
        {
            foreach (var property in Leaves(serializedObject))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                var value = property.objectReferenceValue;
                string pathPropertyPath = null;

                if (AvatarObjectReferences.TryGetPathProperty(property, out var pathProperty))
                {
                    pathPropertyPath = pathProperty.propertyPath;
                    if (value == null)
                        value = AvatarObjectReferences.ResolvePath(pathProperty.stringValue, avatarRoot);
                }

                if (value == null) continue;
                yield return new ReferenceSlot(property.propertyPath, value, pathPropertyPath);
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

        /// <summary>A property path as it is shown: "m_Sources.Array.data[0]" reads "m_Sources[0]".</summary>
        public static string DisplayName(string propertyPath) => propertyPath.Replace(".Array.data[", "[");
    }
}
