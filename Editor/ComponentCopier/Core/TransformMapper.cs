using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Builds a <see cref="TransformMap"/> between two hierarchies.
    /// Resolution runs top-down: children are matched against the children of their parent's counterpart,
    /// so a manual correction of one node carries over to its whole subtree.
    /// Only unambiguous matches become <see cref="MappingState.Confirmed"/>; similar-name matches are
    /// reported as suggestions because a wrong reference target silently breaks component behavior.
    /// The armature (bones and everything below them) and the remaining objects are matched separately;
    /// see <see cref="SkeletonInfo"/>.
    /// </summary>
    internal static class TransformMapper
    {
        private const int MinAffixVotes = 3;
        private const int MinAffixBaseNameLength = 3;

        /// <param name="manual">
        /// User-specified mappings. They take priority over every automatic rule.
        /// A null value marks the source as having no counterpart.
        /// </param>
        /// <param name="scope">
        /// When given, only these source transforms (and the ancestors leading to them) are resolved beyond
        /// exact matches. Resolving compares a transform against every target, which is too slow for whole
        /// avatars when just a few of their objects are of interest.
        /// </param>
        public static TransformMap Build(
            Transform sourceRoot, Transform targetRoot, IReadOnlyDictionary<Transform, Transform> manual = null,
            IEnumerable<Transform> scope = null)
        {
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

            var context = new Context(sourceRoot, targetRoot, manual, scope);
            context.Run();
            return context.Map;
        }

        /// <summary>
        /// Reads humanoid bones from the Animator. Animator.GetBoneTransform knows the bones by their path, but
        /// returns null for prefab assets (and inactive avatars); the bone names of the Avatar asset stand in
        /// then. By name, the first transform wins, which can be the bone of an outfit placed above the
        /// Armature: an outfit repeats the bone names of the avatar.
        /// </summary>
        internal static Dictionary<HumanBodyBones, Transform> CollectAnimatorBones(Transform root)
        {
            var result = new Dictionary<HumanBodyBones, Transform>();

            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return result;

            Dictionary<string, Transform> byName = null;
            foreach (var humanBone in animator.avatar.humanDescription.human)
            {
                int index = Array.IndexOf(HumanTrait.BoneName, humanBone.humanName);
                if (index < 0 || index >= (int)HumanBodyBones.LastBone) continue;

                var bone = (HumanBodyBones)index;
                var bound = animator.GetBoneTransform(bone);
                if (bound != null && bound != root && bound.IsChildOf(root))
                {
                    result[bone] = bound;
                    continue;
                }

                if (byName == null)
                {
                    byName = new Dictionary<string, Transform>();
                    foreach (var transform in Hierarchy.Descendants(root))
                    {
                        if (!byName.ContainsKey(transform.name)) byName[transform.name] = transform;
                    }
                }

                if (byName.TryGetValue(humanBone.boneName, out var named)) result[bone] = named;
            }

            return result;
        }

        private sealed class Context
        {
            public readonly TransformMap Map;

            private readonly Transform sourceRoot;
            private readonly Transform targetRoot;
            private readonly IReadOnlyDictionary<Transform, Transform> manual;
            private readonly HashSet<Transform> usedTargets = new();
            // Null resolves everything
            private readonly HashSet<Transform> resolveScope;

            // Regions are only told apart when both sides have a skeleton to compare
            private readonly bool separateRegions;

            private readonly List<Transform> sourceAll;
            private readonly List<Transform> targetAll;
            private readonly Dictionary<(bool inArmature, string name), List<Transform>> targetsByName = new();
            private readonly Dictionary<(bool inArmature, string name), int> sourceNameCount = new();
            private readonly Dictionary<Transform, string> targetPaths = new();

            private readonly Dictionary<Transform, HumanBodyBones> sourceAnimatorBones = new();
            private readonly Dictionary<HumanBodyBones, Transform> targetAnimatorBones;

            private readonly Dictionary<Transform, HumanBodyBones> sourceDictionaryBones = new();
            private readonly Dictionary<HumanBodyBones, int> sourceDictionaryBoneCount = new();
            private readonly Dictionary<HumanBodyBones, List<Transform>> targetDictionaryBones = new();

            private AffixRule affixRule;

            public Context(
                Transform sourceRoot, Transform targetRoot, IReadOnlyDictionary<Transform, Transform> manual,
                IEnumerable<Transform> scope)
            {
                this.sourceRoot = sourceRoot;
                this.targetRoot = targetRoot;
                this.manual = manual ?? new Dictionary<Transform, Transform>();

                if (scope != null)
                {
                    resolveScope = new HashSet<Transform>();
                    foreach (var transform in scope)
                    {
                        for (var current = transform; current != null && current != sourceRoot; current = current.parent)
                            resolveScope.Add(current);
                    }
                }

                Map = new TransformMap(sourceRoot, targetRoot);
                separateRegions = Map.SourceSkeleton.HasSkeleton && Map.TargetSkeleton.HasSkeleton;

                sourceAll = Hierarchy.Descendants(sourceRoot);
                targetAll = Hierarchy.Descendants(targetRoot);

                foreach (var target in targetAll)
                {
                    var key = (TargetInArmature(target), target.name);
                    if (!targetsByName.TryGetValue(key, out var list))
                        targetsByName[key] = list = new List<Transform>();
                    list.Add(target);

                    targetPaths[target] = ObjectMatcher.GetRelativePathFromRoot(target, targetRoot);
                }

                foreach (var source in sourceAll)
                {
                    var key = (SourceInArmature(source), source.name);
                    sourceNameCount.TryGetValue(key, out var count);
                    sourceNameCount[key] = count + 1;
                }

                foreach (var pair in CollectAnimatorBones(sourceRoot))
                    sourceAnimatorBones[pair.Value] = pair.Key;
                targetAnimatorBones = CollectAnimatorBones(targetRoot);
            }

            public void Run()
            {
                Map.SetRootAndManual(manual);
                usedTargets.Add(targetRoot);
                usedTargets.UnionWith(Map.All
                    .Where(m => m.State == MappingState.Manual && m.Target != null)
                    .Select(m => m.Target));

                // Exact matches claim their targets first so that the global rules below cannot steal them.
                ExactPass(sourceRoot, targetRoot, true);

                affixRule = DetectAffixRule();
                // Needs the affix rule: "Hips_v2" only resolves to Hips once the suffix is known
                BuildDictionaryBones();

                ResolvePass(sourceRoot, targetRoot);
                Map.OfferAutomaticAnswers(manual.Keys, source => Resolve(source, NearestMappedAncestorTarget(source)));
            }

            private Transform NearestMappedAncestorTarget(Transform source)
            {
                for (var parent = source.parent; parent != null && parent != sourceRoot; parent = parent.parent)
                {
                    if (Map.TryResolve(parent, out var target)) return target;
                }

                return targetRoot;
            }

            #region Regions

            private bool SourceInArmature(Transform source) =>
                separateRegions && Map.SourceSkeleton.IsInArmature(source);

            private bool TargetInArmature(Transform target) =>
                separateRegions && Map.TargetSkeleton.IsInArmature(target);

            /// <summary>Bones never match objects outside of the armature, and vice versa.</summary>
            private bool SameRegion(Transform source, Transform target) =>
                SourceInArmature(source) == TargetInArmature(target);

            private bool IsAvailable(Transform source, Transform target) =>
                !usedTargets.Contains(target) && SameRegion(source, target);

            #endregion

            #region Passes

            /// <summary>
            /// Matches same-name children. Same-name siblings are paired by their order of appearance.
            /// Descends only through matched pairs.
            /// </summary>
            private void ExactPass(Transform source, Transform target, bool pathExact)
            {
                var occurrence = new Dictionary<string, int>();

                foreach (Transform child in source)
                {
                    occurrence.TryGetValue(child.name, out var index);
                    occurrence[child.name] = index + 1;

                    var existing = Map.Get(child);
                    if (existing != null)
                    {
                        if (existing.IsUsable) ExactPass(child, existing.Target, false);
                        continue;
                    }

                    var match = Hierarchy.FindChild(target, child.name, index);
                    if (match == null || !IsAvailable(child, match)) continue;

                    Apply(TransformMapping.Confirmed(
                        child, match, pathExact ? MappingReason.ExactPath : MappingReason.ChildOfMappedParent));
                    ExactPass(child, match, pathExact);
                }
            }

            /// <summary>
            /// Resolves everything the exact pass left over.
            /// <paramref name="contextTarget"/> is the counterpart of the nearest usable ancestor, so children of an
            /// unmatched intermediate object are still matched below the right parent.
            /// </summary>
            private void ResolvePass(Transform source, Transform contextTarget)
            {
                foreach (Transform child in source)
                {
                    // Nothing of interest below; the exact pass has already covered what it could
                    if (resolveScope != null && !resolveScope.Contains(child)) continue;

                    if (!Map.Contains(child))
                    {
                        var resolved = Resolve(child, contextTarget);
                        Apply(resolved);
                        if (resolved.IsUsable) ExactPass(child, resolved.Target, false);
                    }

                    var mapping = Map.Get(child);
                    ResolvePass(child, mapping.IsUsable ? mapping.Target : contextTarget);
                }
            }

            private TransformMapping Resolve(Transform source, Transform contextTarget)
            {
                bool inArmature = SourceInArmature(source);
                var contextChildren = Hierarchy.Children(contextTarget).Where(t => SameRegion(source, t)).ToList();

                // Same-name child of the nearest mapped ancestor (covers extra intermediate objects on the source side)
                var sameNameChildren = contextChildren
                    .Where(t => t.name == source.name && !usedTargets.Contains(t))
                    .ToList();
                if (sameNameChildren.Count == 1)
                {
                    return TransformMapping.Confirmed(source, sameNameChildren[0], MappingReason.ChildOfMappedParent);
                }

                // Child that was only renamed: "Armature" vs "Armature.1", "Hips" vs "Hips_v2".
                // Safe to confirm because the parents already correspond and the name is unique on both sides.
                string canonical = CanonicalSourceName(source.name);
                var renamedChildren = contextChildren.Where(t => CanonicalTargetName(t.name) == canonical).ToList();
                if (renamedChildren.Count == 1 && !usedTargets.Contains(renamedChildren[0]) &&
                    Hierarchy.Children(source.parent).Count(s => CanonicalSourceName(s.name) == canonical) == 1)
                {
                    return TransformMapping.Confirmed(source, renamedChildren[0], MappingReason.RenamedChild);
                }

                // Humanoid bones defined by both Animators
                if (sourceAnimatorBones.TryGetValue(source, out var animatorBone) &&
                    targetAnimatorBones.TryGetValue(animatorBone, out var animatorTarget) &&
                    IsAvailable(source, animatorTarget))
                {
                    return TransformMapping.Confirmed(source, animatorTarget, MappingReason.HumanoidAnimator);
                }

                // Humanoid bone synonym dictionary
                if (sourceDictionaryBones.TryGetValue(source, out var dictionaryBone) &&
                    targetDictionaryBones.TryGetValue(dictionaryBone, out var dictionaryTargets))
                {
                    var unused = dictionaryTargets.Where(t => IsAvailable(source, t)).ToList();
                    bool unique = sourceDictionaryBoneCount[dictionaryBone] == 1 && dictionaryTargets.Count == 1;

                    if (unique && unused.Count == 1)
                    {
                        return TransformMapping.Confirmed(source, unused[0], MappingReason.HumanoidDictionary);
                    }

                    if (unused.Count > 0)
                    {
                        return Suggest(source, unused, MappingReason.HumanoidDictionary);
                    }
                }

                // Exact name anywhere in the same region of the target
                if (targetsByName.TryGetValue((inArmature, source.name), out var sameName))
                {
                    var unused = sameName.Where(t => !usedTargets.Contains(t)).ToList();
                    if (unused.Count == 1 && sameName.Count == 1 && sourceNameCount[(inArmature, source.name)] == 1)
                    {
                        return TransformMapping.Confirmed(source, unused[0], MappingReason.UniqueName);
                    }

                    if (unused.Count > 0)
                    {
                        return Suggest(source, unused, MappingReason.AmbiguousName);
                    }
                }

                // Prefix / suffix match away from the mapped parent: plausible, but the structure does not back it up
                string affixName = affixRule?.ExpectedTargetName(source.name);
                if (affixName != null && targetsByName.TryGetValue((inArmature, affixName), out var affixTargets))
                {
                    var unused = affixTargets.Where(t => !usedTargets.Contains(t)).ToList();
                    if (unused.Count > 0)
                    {
                        return Suggest(source, unused, MappingReason.AffixStripped);
                    }
                }

                // Avatars often lack UpperChest. Chest is usually already taken, so this stays a suggestion.
                if (sourceDictionaryBones.TryGetValue(source, out var bone) && bone == HumanBodyBones.UpperChest &&
                    !targetDictionaryBones.ContainsKey(HumanBodyBones.UpperChest) &&
                    targetDictionaryBones.TryGetValue(HumanBodyBones.Chest, out var chestTargets) &&
                    chestTargets.Count == 1)
                {
                    return Suggest(source, chestTargets, MappingReason.UpperChestFallback);
                }

                // Names that differ only by case or structural suffixes (.001, _01, (1), ...)
                string normalized = ObjectMatcher.NormalizeName(source.name);
                var normalizedTargets = targetAll
                    .Where(t => IsAvailable(source, t))
                    .Where(t => string.Equals(
                        ObjectMatcher.NormalizeName(t.name), normalized, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (normalizedTargets.Count > 0)
                {
                    return Suggest(source, normalizedTargets, MappingReason.NormalizedName);
                }

                // Fuzzy matches are listed as candidates only; nothing is preselected.
                var fuzzyTargets = targetAll
                    .Where(t => IsAvailable(source, t))
                    .Where(t => ObjectMatcher.HasCommonBaseName(
                        t.name, source.name, MappingCandidates.FuzzyMinTokenLength));

                return TransformMapping.Unmapped(source, Rank(source, fuzzyTargets));
            }

            #endregion

            #region Names

            private string CanonicalSourceName(string name)
            {
                string stripped = RenameSuffix.Strip(name);
                if (affixRule != null && !affixRule.OnTarget) stripped = RenameSuffix.Strip(affixRule.Strip(stripped));
                return stripped;
            }

            private string CanonicalTargetName(string name)
            {
                string stripped = RenameSuffix.Strip(name);
                if (affixRule != null && affixRule.OnTarget) stripped = RenameSuffix.Strip(affixRule.Strip(stripped));
                return stripped;
            }

            private void BuildDictionaryBones()
            {
                // Humanoid names are a bone concept: a mesh object called "Head" is not the Head bone
                foreach (var source in sourceAll)
                {
                    if (separateRegions && !SourceInArmature(source)) continue;
                    if (!HumanoidBoneDictionary.TryFindBone(CanonicalSourceName(source.name), out var bone)) continue;

                    sourceDictionaryBones[source] = bone;
                    sourceDictionaryBoneCount.TryGetValue(bone, out var count);
                    sourceDictionaryBoneCount[bone] = count + 1;
                }

                foreach (var target in targetAll)
                {
                    if (separateRegions && !TargetInArmature(target)) continue;
                    if (!HumanoidBoneDictionary.TryFindBone(CanonicalTargetName(target.name), out var bone)) continue;

                    if (!targetDictionaryBones.TryGetValue(bone, out var bones))
                        targetDictionaryBones[bone] = bones = new List<Transform>();
                    bones.Add(target);
                }
            }

            /// <summary>
            /// Detects a prefix / suffix shared by many bones on one side (e.g. "Hips" vs "Hips_v2").
            /// </summary>
            private AffixRule DetectAffixRule()
            {
                var sourceNames = sourceAll
                    .Where(s => !Map.Contains(s) && (!separateRegions || SourceInArmature(s)))
                    .Select(s => RenameSuffix.Strip(s.name))
                    .Where(n => n.Length >= MinAffixBaseNameLength)
                    .Distinct()
                    .ToList();
                var targetNames = targetAll
                    .Where(t => !usedTargets.Contains(t) && (!separateRegions || TargetInArmature(t)))
                    .Select(t => RenameSuffix.Strip(t.name))
                    .Where(n => n.Length >= MinAffixBaseNameLength)
                    .Distinct()
                    .ToList();

                var votes = new Dictionary<(bool onTarget, string prefix, string suffix), int>();

                foreach (var s in sourceNames)
                {
                    foreach (var t in targetNames)
                    {
                        if (s.Length == t.Length) continue;

                        bool onTarget = t.Length > s.Length;
                        string longer = onTarget ? t : s;
                        string shorter = onTarget ? s : t;

                        int index = longer.IndexOf(shorter, StringComparison.Ordinal);
                        if (index < 0) continue;

                        var key = (onTarget, longer.Substring(0, index), longer.Substring(index + shorter.Length));
                        votes.TryGetValue(key, out var count);
                        votes[key] = count + 1;
                    }
                }

                if (votes.Count == 0) return null;

                var best = votes.OrderByDescending(v => v.Value).First();
                if (best.Value < MinAffixVotes) return null;

                return new AffixRule(best.Key.onTarget, best.Key.prefix, best.Key.suffix);
            }

            #endregion

            #region Helpers

            /// <summary>Records a mapping. A confirmed one claims its target, so that no other source gets it.</summary>
            private void Apply(TransformMapping mapping)
            {
                Map.Set(mapping);
                if (mapping.State == MappingState.Confirmed && mapping.Target != null) usedTargets.Add(mapping.Target);
            }

            private TransformMapping Suggest(Transform source, IEnumerable<Transform> targets, MappingReason reason) =>
                TransformMapping.Suggested(source, Rank(source, targets), reason);

            private List<MappingCandidate> Rank(Transform source, IEnumerable<Transform> targets) =>
                MappingCandidates.Rank(ObjectMatcher.GetRelativePathFromRoot(source, sourceRoot), targets, targetPaths);

            #endregion
        }

        private sealed class AffixRule
        {
            private readonly string prefix;
            private readonly string suffix;

            /// <summary>True when the target names carry the affix, false when the source names do.</summary>
            public bool OnTarget { get; }

            public AffixRule(bool onTarget, string prefix, string suffix)
            {
                OnTarget = onTarget;
                this.prefix = prefix;
                this.suffix = suffix;
            }

            /// <summary>Removes the affix. Names that do not carry it are returned unchanged.</summary>
            public string Strip(string name)
            {
                if (name.Length <= prefix.Length + suffix.Length) return name;
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) return name;
                if (!name.EndsWith(suffix, StringComparison.Ordinal)) return name;
                return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            }

            /// <summary>Returns the name the counterpart is expected to have, or null when the rule does not apply.</summary>
            public string ExpectedTargetName(string sourceName)
            {
                if (OnTarget) return prefix + sourceName + suffix;

                string stripped = Strip(sourceName);
                return stripped == sourceName ? null : stripped;
            }
        }
    }
}
