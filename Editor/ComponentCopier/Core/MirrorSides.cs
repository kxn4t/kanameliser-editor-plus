using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// The side each object of a hierarchy belongs to in a mirror copy, judged the way <see cref="MirrorMapper"/>
    /// pairs them: the humanoid bones of the Animator first, then the side marker of the name, then humanoid
    /// names in the armature. An object belongs to the side of its outermost sided ancestor below the root,
    /// itself included, because a part can carry both sides of its own: "Earring_R/Chain_L" belongs to the
    /// right earring. None on the middle line.
    /// </summary>
    internal sealed class MirrorSides
    {
        private readonly Transform root;
        private readonly SkeletonInfo skeleton;
        private readonly Dictionary<Transform, HumanBodyBones> animatorBones;
        private readonly Dictionary<Transform, Side> cache = new();

        /// <summary>Reads the skeleton and the humanoid bones of <paramref name="root"/>.</summary>
        public MirrorSides(Transform root)
            : this(root, SkeletonInfo.Analyze(root), new Dictionary<Transform, HumanBodyBones>())
        {
            // Like the mapper, where a transform stands for two bones the last one counts
            foreach (var pair in TransformMapper.CollectAnimatorBones(root))
                animatorBones[pair.Value] = pair.Key;
        }

        internal MirrorSides(
            Transform root, SkeletonInfo skeleton, Dictionary<Transform, HumanBodyBones> animatorBones)
        {
            this.root = root;
            this.skeleton = skeleton;
            this.animatorBones = animatorBones;
        }

        public Side Of(Transform transform)
        {
            if (transform == null || transform == root) return Side.None;
            if (cache.TryGetValue(transform, out var side)) return side;

            side = Of(transform.parent);
            if (side == Side.None) side = OwnSide(transform);
            cache[transform] = side;
            return side;
        }

        private Side OwnSide(Transform transform)
        {
            // Whatever its name, the map pairs an Animator bone with the bone on the other side
            if (animatorBones.TryGetValue(transform, out var animatorBone))
                return HumanoidBoneDictionary.SideOf(animatorBone);

            var side = SideName.GetSide(transform.name);
            if (side != Side.None) return side;

            // Humanoid names are a bone concept: a mesh object called "UpperLeftArm" is paired like any other
            if (!HumanoidBoneDictionary.TryFindBone(RenameSuffix.Strip(transform.name), out var bone))
                return Side.None;
            if (skeleton.HasSkeleton && !skeleton.IsInArmature(transform)) return Side.None;
            return HumanoidBoneDictionary.SideOf(bone);
        }
    }
}
