using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Tells skinning bones apart from every other object of a hierarchy.
    /// Bones and other objects are matched separately: outfits often reuse one name for a mesh object and a bone
    /// ("Skirt"), bone-only rules (humanoid names, prefix / suffix) must not leak to other objects, and a bone
    /// that is missing in the target must never be created as an empty object.
    /// </summary>
    internal sealed class SkeletonInfo
    {
        private readonly HashSet<Transform> bones = new();
        private readonly HashSet<Transform> regionRoots = new();

        /// <summary>False when the hierarchy has no skinned mesh at all; no separation is applied then.</summary>
        public bool HasSkeleton => bones.Count > 0;

        /// <summary>
        /// True for transforms used for skinning and for their ancestors (e.g. "Armature").
        /// </summary>
        public bool IsBone(Transform transform) => transform != null && bones.Contains(transform);

        /// <summary>
        /// True for bones and for everything below them, such as collider objects or unweighted leaf bones.
        /// </summary>
        public bool IsInArmature(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (regionRoots.Contains(current)) return true;
            }

            return false;
        }

        public static SkeletonInfo Analyze(Transform root)
        {
            var info = new SkeletonInfo();
            if (root == null) return info;

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                info.AddWithAncestors(renderer.rootBone, root);
                foreach (var bone in renderer.bones)
                    info.AddWithAncestors(bone, root);
            }

            foreach (var bone in info.bones)
            {
                if (bone.parent == root) info.regionRoots.Add(bone);
            }

            return info;
        }

        private void AddWithAncestors(Transform bone, Transform root)
        {
            // Meshes may be bound to bones outside of this hierarchy (e.g. an outfit already merged into an avatar)
            if (bone == null || bone == root || !bone.IsChildOf(root)) return;

            for (var current = bone; current != null && current != root; current = current.parent)
            {
                if (!bones.Add(current)) break;
            }
        }
    }
}
