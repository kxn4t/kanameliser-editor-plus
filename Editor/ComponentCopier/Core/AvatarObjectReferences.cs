using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Modular Avatar's AvatarObjectReference: a serializable pair of an avatar-relative path and a direct
    /// object reference. MA follows the object when it sits inside the avatar and falls back to the path
    /// otherwise (prefabs made before the object half existed only carry the path). Both halves are handled
    /// together, so that the path never stays behind pointing at the source.
    /// Recognized by its serialized layout, so the assembly needs no reference to MA.
    /// </summary>
    internal static class AvatarObjectReferences
    {
        /// <summary>Path that MA uses for the avatar root itself.</summary>
        public const string AvatarRootPath = "$$$AVATAR_ROOT$$$";

        private const string TypeName = "AvatarObjectReference";
        private const string ObjectField = "targetObject";
        private const string PathField = "referencePath";

        /// <summary>
        /// True when <paramref name="leaf"/> is the object half of an AvatarObjectReference. Returns the path half.
        /// </summary>
        public static bool TryGetPathProperty(SerializedProperty leaf, out SerializedProperty pathProperty)
        {
            pathProperty = null;
            if (leaf.name != ObjectField) return false;

            string path = leaf.propertyPath;
            int dot = path.LastIndexOf('.');
            if (dot < 0) return false;

            var owner = leaf.serializedObject.FindProperty(path.Substring(0, dot));
            if (owner == null || owner.type != TypeName) return false;

            pathProperty = owner.FindPropertyRelative(PathField);
            return pathProperty != null && pathProperty.propertyType == SerializedPropertyType.String;
        }

        /// <summary>
        /// The avatar a path is relative to, as MA sees it: the outermost object at or above
        /// <paramref name="transform"/> that carries an avatar root marker. Null when there is none.
        /// </summary>
        public static Transform FindAvatarRoot(Transform transform)
        {
            Transform found = null;
            for (var current = transform; current != null; current = current.parent)
            {
                if (ExternalContext.IsAvatarRoot(current)) found = current;
            }

            return found;
        }

        /// <summary>
        /// Resolves the path half against the avatar, for a reference whose object half is empty.
        /// </summary>
        public static GameObject ResolvePath(string referencePath, Transform avatarRoot)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(referencePath)) return null;
            if (referencePath == AvatarRootPath) return avatarRoot.gameObject;
            return avatarRoot.Find(referencePath)?.gameObject;
        }

        /// <summary>
        /// The path half that goes with <paramref name="target"/>: empty for no object, and null when the
        /// object is not below <paramref name="avatarRoot"/> (or there is no avatar), where MA has no path
        /// for it either.
        /// </summary>
        public static string PathFor(Object target, Transform avatarRoot)
        {
            var transform = ReferenceWalker.GetTransform(target);
            if (transform == null) return "";
            if (avatarRoot == null || !ReferenceWalker.IsInside(transform, avatarRoot)) return null;
            if (transform == avatarRoot) return AvatarRootPath;

            var segments = new List<string>();
            for (var current = transform; current != avatarRoot; current = current.parent)
                segments.Add(current.name);
            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
