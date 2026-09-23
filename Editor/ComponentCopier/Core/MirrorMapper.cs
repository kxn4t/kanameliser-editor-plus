using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Builds a <see cref="TransformMap"/> from one side of a hierarchy to the other side of the same hierarchy
    /// (mirror copy). Objects are paired by the side marker of their name (<see cref="SideName"/>), humanoid
    /// bones by the mirrored <see cref="HumanBodyBones"/>. Objects on the middle line (Hips, Spine, the root)
    /// are their own counterpart, so references to them stay where they are.
    /// The map holds both directions: a left component that refers to a right object has to end up referring
    /// to the left one. Which side is copied is up to the caller, see <see cref="TransformMap.Sides"/>.
    /// Resolution runs top-down like <see cref="TransformMapper"/>: the counterpart of a child is looked for
    /// below the counterpart of its parent first, so "Collider" below "Hand_L" pairs with "Collider" below
    /// "Hand_R" without carrying a marker itself.
    /// </summary>
    internal static class MirrorMapper
    {
        /// <param name="root">
        /// The hierarchy the map spans. Usually the avatar, so that references from an outfit to the bones and
        /// colliders of the avatar are mirrored as well.
        /// </param>
        /// <param name="manual">User-specified mappings. They take priority over every automatic rule.</param>
        public static TransformMap Build(Transform root, IReadOnlyDictionary<Transform, Transform> manual = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var context = new Context(root, manual);
            context.Run();
            return context.Map;
        }

        private sealed class Context
        {
            public readonly TransformMap Map;

            private readonly Transform root;
            private readonly IReadOnlyDictionary<Transform, Transform> manual;
            private readonly Dictionary<(bool inArmature, string name), List<Transform>> byName = new();
            private readonly Dictionary<Transform, string> paths = new();

            private readonly Dictionary<Transform, HumanBodyBones> animatorBones = new();
            private readonly Dictionary<HumanBodyBones, Transform> animatorBoneTransforms;
            private readonly Dictionary<Transform, HumanBodyBones> dictionaryBones = new();
            private readonly Dictionary<HumanBodyBones, List<Transform>> dictionaryBoneTransforms = new();

            public Context(Transform root, IReadOnlyDictionary<Transform, Transform> manual)
            {
                this.root = root;
                this.manual = manual ?? new Dictionary<Transform, Transform>();

                Map = new TransformMap(root);
                var all = Hierarchy.Descendants(root);

                foreach (var transform in all)
                {
                    var key = (InArmature(transform), transform.name);
                    if (!byName.TryGetValue(key, out var list))
                        byName[key] = list = new List<Transform>();
                    list.Add(transform);

                    paths[transform] = ObjectMatcher.GetRelativePathFromRoot(transform, root);
                }

                animatorBoneTransforms = TransformMapper.CollectAnimatorBones(root);
                foreach (var pair in animatorBoneTransforms)
                    animatorBones[pair.Value] = pair.Key;
                Map.Sides = new MirrorSides(root, Map.SourceSkeleton, animatorBones);

                // Humanoid names are a bone concept: a mesh object called "Head" is not the Head bone
                bool hasSkeleton = Map.SourceSkeleton.HasSkeleton;
                foreach (var transform in all)
                {
                    if (hasSkeleton && !InArmature(transform)) continue;
                    if (!HumanoidBoneDictionary.TryFindBone(RenameSuffix.Strip(transform.name), out var bone))
                        continue;

                    dictionaryBones[transform] = bone;
                    if (!dictionaryBoneTransforms.TryGetValue(bone, out var bones))
                        dictionaryBoneTransforms[bone] = bones = new List<Transform>();
                    bones.Add(transform);
                }
            }

            public void Run()
            {
                Map.SetRootAndManual(manual);
                Visit(root, root, root);
                Map.OfferAutomaticAnswers(manual.Keys, source =>
                {
                    var anchor = NearestMappedAncestor(source, out var anchorCounterpart);
                    return Resolve(source, anchor, anchorCounterpart);
                });
            }

            /// <param name="anchor">The nearest mapped ancestor of the children: the parent itself when it is mapped.</param>
            /// <param name="anchorCounterpart">The counterpart of <paramref name="anchor"/>.</param>
            private void Visit(Transform parent, Transform anchor, Transform anchorCounterpart)
            {
                foreach (Transform child in parent)
                {
                    var mapping = Map.Get(child);
                    if (mapping == null)
                    {
                        mapping = Resolve(child, anchor, anchorCounterpart);
                        Map.Set(mapping);
                    }

                    // Below an unmapped object, children are still looked for below the nearest mapped ancestor
                    if (mapping.IsUsable) Visit(child, child, mapping.Target);
                    else Visit(child, anchor, anchorCounterpart);
                }
            }

            private Transform NearestMappedAncestor(Transform source, out Transform counterpart)
            {
                for (var parent = source.parent; parent != null && parent != root; parent = parent.parent)
                {
                    if (Map.TryResolve(parent, out counterpart)) return parent;
                }

                counterpart = root;
                return root;
            }

            /// <param name="anchor">The nearest mapped ancestor of the source: its parent unless that is unmapped.</param>
            /// <param name="anchorCounterpart">
            /// The counterpart of <paramref name="anchor"/>. Equal to the anchor itself on the middle line.
            /// </param>
            private TransformMapping Resolve(Transform source, Transform anchor, Transform anchorCounterpart)
            {
                // Humanoid bones: the Animator knows both sides
                if (animatorBones.TryGetValue(source, out var animatorBone))
                {
                    if (!HumanoidBoneDictionary.TryMirror(animatorBone, out var mirroredBone))
                        return Confirmed(source, source, MappingReason.SelfCenter);
                    if (animatorBoneTransforms.TryGetValue(mirroredBone, out var mirroredTransform))
                        return Confirmed(source, mirroredTransform, MappingReason.MirroredBone);
                }

                int occurrence = Hierarchy.SiblingOccurrence(source);
                bool hasMarker = SideName.TryFlip(source.name, out var flipped);

                // Below the counterpart of the parent first: "Hips/Skirt_L" → "Hips/Skirt_R". Ahead of the bone
                // dictionary, because an outfit on the avatar repeats the bone names of the avatar: the dictionary
                // cannot tell the two "Hand_R" apart, the structure can. Below an unmapped parent, the flipped name
                // is looked for further up and only a guess: "Hips/Pouch_L/Strap_L" without a "Pouch_R" is not
                // the other side of "Hips/Strap_R".
                bool parentMapped = anchor == source.parent;
                if (hasMarker)
                {
                    var sibling = Hierarchy.FindChild(anchorCounterpart, flipped, occurrence);
                    if (sibling != null)
                    {
                        return parentMapped
                            ? Confirmed(source, sibling, MappingReason.MirroredName)
                            : Suggest(source, new List<Transform> { sibling }, MappingReason.MirroredName);
                    }
                }

                // Humanoid bone synonym dictionary: covers names without a marker such as "UpperLeftArm".
                // Both sides are narrowed to the part of the hierarchy the anchor pairs with, for the same reason.
                if (dictionaryBones.TryGetValue(source, out var dictionaryBone) &&
                    HumanoidBoneDictionary.TryMirror(dictionaryBone, out var mirroredDictionaryBone) &&
                    dictionaryBoneTransforms.TryGetValue(mirroredDictionaryBone, out var mirroredBones))
                {
                    var candidates = Below(mirroredBones, anchorCounterpart);
                    var sameBone = Below(dictionaryBoneTransforms[dictionaryBone], anchor);
                    if (candidates.Count == 1 && sameBone.Count == 1)
                        return Confirmed(source, candidates[0], MappingReason.MirroredBone);
                    // A bone elsewhere (the avatar's, for a one-sided outfit) is offered, never taken
                    return Suggest(source, candidates.Count > 0 ? candidates : mirroredBones, MappingReason.MirroredBone);
                }

                if (hasMarker)
                {
                    // Anywhere else, as long as both names are unique: a one-sided "Hat/Ribbon_L" is not the other
                    // side of the "Skirt/Ribbon_R" that pairs with "Skirt/Ribbon_L". The source's own side holds no
                    // counterpart: "Hand_L/Chain_L" is not the other side of the "Chain_R" of the same hand, even
                    // when "Hand_R" has no chains. Below an unmapped parent, neither does the source's own branch,
                    // and anything further away is only a guess.
                    if (byName.TryGetValue((InArmature(source), flipped), out var elsewhere))
                    {
                        var side = Map.Sides.Of(source);
                        var branch = parentMapped ? null : BranchBelow(anchor, source);
                        var candidates = elsewhere
                            .Where(t => Map.Sides.Of(t) != side && (branch == null || !t.IsChildOf(branch)))
                            .ToList();
                        bool uniqueSource = byName.TryGetValue((InArmature(source), source.name), out var namesakes) &&
                                            namesakes.Count == 1;
                        // Unique before the filter as well: the one left over may be one of several
                        if (candidates.Count == 1 && elsewhere.Count == 1 && parentMapped && uniqueSource)
                            return Confirmed(source, candidates[0], MappingReason.MirroredName);
                        if (candidates.Count > 0) return Suggest(source, candidates, MappingReason.MirroredName);
                    }

                    return Unmapped(source, FuzzyChildren(anchorCounterpart, flipped));
                }

                // No marker: on the middle line when the parent is, else the same-name child of the mirrored parent
                if (anchorCounterpart == source.parent)
                    return Confirmed(source, source, MappingReason.SelfCenter);

                var sameName = Hierarchy.FindChild(anchorCounterpart, source.name, occurrence);
                if (sameName != null)
                {
                    // Below an unmapped parent, a plain name such as "Strap" found further up is only a guess:
                    // "Hips/Pouch_L/Strap" without a "Pouch_R" must not land on the "Hips/Strap" of the belt
                    return parentMapped
                        ? Confirmed(source, sameName, MappingReason.ChildOfMappedParent)
                        : Suggest(source, new List<Transform> { sameName }, MappingReason.ChildOfMappedParent);
                }

                return Unmapped(source, FuzzyChildren(anchorCounterpart, source.name));
            }

            /// <summary>The ancestor of <paramref name="source"/> right below <paramref name="anchor"/>.</summary>
            private static Transform BranchBelow(Transform anchor, Transform source)
            {
                var current = source;
                while (current.parent != null && current.parent != anchor) current = current.parent;
                return current;
            }

            private List<Transform> Below(List<Transform> transforms, Transform scope)
            {
                if (scope == root) return transforms;
                return transforms.Where(t => t != scope && t.IsChildOf(scope)).ToList();
            }

            private bool InArmature(Transform transform) =>
                Map.SourceSkeleton.HasSkeleton && Map.SourceSkeleton.IsInArmature(transform);

            private static TransformMapping Confirmed(Transform source, Transform target, MappingReason reason) =>
                TransformMapping.Confirmed(source, target, reason);

            private TransformMapping Suggest(Transform source, IEnumerable<Transform> targets, MappingReason reason) =>
                TransformMapping.Suggested(source, Rank(source, targets), reason);

            private TransformMapping Unmapped(Transform source, IEnumerable<Transform> candidates) =>
                TransformMapping.Unmapped(source, Rank(source, candidates));

            private static IEnumerable<Transform> FuzzyChildren(Transform parent, string name) =>
                Hierarchy.Children(parent)
                    .Where(t => ObjectMatcher.HasCommonBaseName(t.name, name, MappingCandidates.FuzzyMinTokenLength));

            /// <summary>Both sides live in one hierarchy, so the source itself is never its own candidate.</summary>
            private List<MappingCandidate> Rank(Transform source, IEnumerable<Transform> targets) =>
                MappingCandidates.Rank(paths[source], targets.Where(t => t != source), paths);
        }
    }
}
