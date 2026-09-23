using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// The surroundings of a copied hierarchy: usually the avatar an outfit sits on. Components of the outfit
    /// refer to it (a bone a constraint follows, a collider on the body), and when the outfit is copied to
    /// another avatar those references should follow.
    /// </summary>
    internal static class ExternalContext
    {
        // Compared by name because the SDK is not referenced from this assembly
        private static readonly string[] AvatarRootTypeNames =
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
            "nadena.dev.ndmf.runtime.components.NDMFAvatarRoot",
        };

        /// <summary>True for an object that carries an avatar root marker (the VRChat avatar descriptor).</summary>
        public static bool IsAvatarRoot(Transform transform)
        {
            return transform != null && transform.GetComponents<Component>()
                .Any(c => c != null && Array.IndexOf(AvatarRootTypeNames, c.GetType().FullName) >= 0);
        }

        /// <summary>
        /// Returns the avatar root above <paramref name="root"/>, or else its topmost ancestor.
        /// Null when <paramref name="root"/> has no parent, i.e. there are no surroundings to speak of.
        /// </summary>
        public static Transform FindRoot(Transform root)
        {
            if (root == null || root.parent == null) return null;

            for (var current = root.parent; current != null; current = current.parent)
            {
                if (IsAvatarRoot(current)) return current;
            }

            return root.root;
        }

        /// <summary>
        /// False when there is nothing to redirect to: one side has no surroundings, or both share them (two
        /// outfits on the same avatar), in which case the references are right as they are.
        /// </summary>
        public static bool CanRedirect(Transform sourceRoot, Transform targetRoot)
        {
            var sourceContext = FindRoot(sourceRoot);
            var targetContext = FindRoot(targetRoot);
            return sourceContext != null && targetContext != null && sourceContext != targetContext;
        }

        /// <summary>
        /// Maps the surroundings of the source to the surroundings of the target, resolving only the referenced
        /// objects. Returns null when <see cref="CanRedirect"/> says no, or none of the objects is part of the
        /// source's surroundings.
        /// </summary>
        public static TransformMap BuildMap(
            Transform sourceRoot, Transform targetRoot, IEnumerable<Transform> referenced,
            IReadOnlyDictionary<Transform, Transform> manual = null)
        {
            if (!CanRedirect(sourceRoot, targetRoot)) return null;

            var sourceContext = FindRoot(sourceRoot);
            var targetContext = FindRoot(targetRoot);

            var scope = referenced.Where(t => Hierarchy.IsInside(t, sourceContext)).ToList();
            return scope.Count > 0 ? TransformMapper.Build(sourceContext, targetContext, manual, scope) : null;
        }
    }
}
