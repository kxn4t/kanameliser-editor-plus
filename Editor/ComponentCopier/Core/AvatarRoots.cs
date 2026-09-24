using System;
using System.Linq;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Finds the avatar around an object. The avatar is the outermost object that carries an avatar root marker,
    /// as NDMF and MA see it: a marker nested inside another avatar does not start an avatar of its own.
    /// </summary>
    internal static class AvatarRoots
    {
        // Compared by name because the SDK is not referenced from this assembly
        private static readonly string[] MarkerTypeNames =
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
            "nadena.dev.ndmf.runtime.components.NDMFAvatarRoot",
        };

        /// <summary>True for an object that carries an avatar root marker (the VRChat avatar descriptor, ...).</summary>
        private static bool HasMarker(Transform transform)
        {
            return transform != null && transform.GetComponents<Component>()
                .Any(c => c != null && Array.IndexOf(MarkerTypeNames, c.GetType().FullName) >= 0);
        }

        /// <summary>The avatar <paramref name="transform"/> belongs to, itself included. Null when there is none.</summary>
        public static Transform Find(Transform transform)
        {
            Transform found = null;
            for (var current = transform; current != null; current = current.parent)
            {
                if (HasMarker(current)) found = current;
            }

            return found;
        }

        /// <summary>
        /// The surroundings of a copied hierarchy: the avatar it sits on, or else its topmost ancestor. Null when
        /// <paramref name="root"/> has no parent, i.e. there are no surroundings to speak of.
        /// </summary>
        public static Transform Surroundings(Transform root)
        {
            if (root == null || root.parent == null) return null;

            var avatar = Find(root.parent);
            return avatar != null ? avatar : root.root;
        }

        /// <summary>
        /// The hierarchy a mirror copy works in: the avatar the source sits on, so that references to the
        /// bones and colliders of the avatar are mirrored too. Outside of an avatar, the outermost model (an
        /// object with an Animator), else the topmost parent: a part such as "UpperLeg_L" has no other side of
        /// its own. The model comes first because an organizing object above it ("Avatars") need not sit on
        /// its middle line, and the outermost one because an outfit FBX on the model has an Animator too.
        /// </summary>
        public static Transform MirrorRoot(Transform source)
        {
            var avatar = Find(source);
            if (avatar != null) return avatar;

            Transform model = null;
            for (var current = source; current != null; current = current.parent)
            {
                if (current.GetComponent<Animator>() != null) model = current;
            }

            return model != null ? model : source.root;
        }
    }
}
