using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// The surroundings of a copied hierarchy: usually the avatar an outfit sits on. Components of the outfit
    /// refer to it (a bone a constraint follows, a collider on the body), and when the outfit is copied to
    /// another avatar those references should follow. See <see cref="AvatarRoots.Surroundings"/>.
    /// </summary>
    internal static class ExternalContext
    {
        /// <summary>
        /// False when there is nothing to redirect to: one side has no surroundings, or both share them (two
        /// outfits on the same avatar), in which case the references are right as they are.
        /// </summary>
        public static bool CanRedirect(Transform sourceRoot, Transform targetRoot)
        {
            var sourceContext = AvatarRoots.Surroundings(sourceRoot);
            var targetContext = AvatarRoots.Surroundings(targetRoot);
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

            var sourceContext = AvatarRoots.Surroundings(sourceRoot);
            var targetContext = AvatarRoots.Surroundings(targetRoot);

            var scope = referenced.Where(t => Hierarchy.IsInside(t, sourceContext)).ToList();
            return scope.Count > 0 ? TransformMapper.Build(sourceContext, targetContext, manual, scope) : null;
        }
    }
}
