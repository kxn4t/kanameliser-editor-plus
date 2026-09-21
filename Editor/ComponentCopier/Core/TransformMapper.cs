using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        private const int MaxCandidates = 8;
        private const int FuzzyMinTokenLength = 3;

        // Suffixes added when an object is renamed to avoid a clash: "Armature.1" (Modular Avatar setups),
        // "Hips.001" (Blender), "Collider (1)" (Unity). "_01" is deliberately not included: it usually
        // numbers the links of a chain and is part of the real name.
        private static readonly Regex RenameSuffix = new Regex(@"(\.\d+|\s\(\d+\))$", RegexOptions.Compiled);

        /// <param name="manual">
        /// User-specified mappings. They take priority over every automatic rule.
        /// A null value marks the source as having no counterpart.
        /// </param>
        public static TransformMap Build(
            Transform sourceRoot, Transform targetRoot, IReadOnlyDictionary<Transform, Transform> manual = null)
        {
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

            var context = new Context(sourceRoot, targetRoot, manual);
            context.Run();
            return context.Map;
        }

        internal static string StripRenameSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            string stripped = RenameSuffix.Replace(name, "");
            return stripped.Length > 0 ? stripped : name;
        }

        private sealed class Context
        {
            public readonly TransformMap Map;

            private readonly Transform sourceRoot;
            private readonly Transform targetRoot;
            private readonly IReadOnlyDictionary<Transform, Transform> manual;
            private readonly HashSet<Transform> usedTargets = new();

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

            public Context(Transform sourceRoot, Transform targetRoot, IReadOnlyDictionary<Transform, Transform> manual)
            {
                this.sourceRoot = sourceRoot;
                this.targetRoot = targetRoot;
                this.manual = manual ?? new Dictionary<Transform, Transform>();
                Map = new TransformMap(sourceRoot, targetRoot);
                separateRegions = Map.SourceSkeleton.HasSkeleton && Map.TargetSkeleton.HasSkeleton;

                sourceAll = Descendants(sourceRoot);
                targetAll = Descendants(targetRoot);

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
                Map.Set(new TransformMapping
                {
                    Source = sourceRoot,
                    Target = targetRoot,
                    State = MappingState.Confirmed,
                    Reason = MappingReason.Root,
                });
                usedTargets.Add(targetRoot);

                foreach (var pair in manual)
                {
                    if (pair.Key == null || pair.Key == sourceRoot) continue;
                    Map.Set(new TransformMapping
                    {
                        Source = pair.Key,
                        Target = pair.Value,
                        State = MappingState.Manual,
                        Reason = MappingReason.Manual,
                    });
                    if (pair.Value != null) usedTargets.Add(pair.Value);
                }

                // Exact matches claim their targets first so that the global rules below cannot steal them.
                ExactPass(sourceRoot, targetRoot, true);

                affixRule = DetectAffixRule();
                // Needs the affix rule: "Hips_v2" only resolves to Hips once the suffix is known
                BuildDictionaryBones();

                ResolvePass(sourceRoot, targetRoot);
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

                    var match = FindChildByNameAndOccurrence(target, child.name, index);
                    if (match == null || !IsAvailable(child, match)) continue;

                    Confirm(child, match, pathExact ? MappingReason.ExactPath : MappingReason.ChildOfMappedParent);
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
                    if (!Map.Contains(child))
                    {
                        Resolve(child, contextTarget);

                        var resolved = Map.Get(child);
                        if (resolved.IsUsable) ExactPass(child, resolved.Target, false);
                    }

                    var mapping = Map.Get(child);
                    ResolvePass(child, mapping.IsUsable ? mapping.Target : contextTarget);
                }
            }

            private void Resolve(Transform source, Transform contextTarget)
            {
                bool inArmature = SourceInArmature(source);
                var contextChildren = Children(contextTarget).Where(t => SameRegion(source, t)).ToList();

                // Same-name child of the nearest mapped ancestor (covers extra intermediate objects on the source side)
                var sameNameChildren = contextChildren
                    .Where(t => t.name == source.name && !usedTargets.Contains(t))
                    .ToList();
                if (sameNameChildren.Count == 1)
                {
                    Confirm(source, sameNameChildren[0], MappingReason.ChildOfMappedParent);
                    return;
                }

                // Child that was only renamed: "Armature" vs "Armature.1", "Hips" vs "Hips_v2".
                // Safe to confirm because the parents already correspond and the name is unique on both sides.
                string canonical = CanonicalSourceName(source.name);
                var renamedChildren = contextChildren.Where(t => CanonicalTargetName(t.name) == canonical).ToList();
                if (renamedChildren.Count == 1 && !usedTargets.Contains(renamedChildren[0]) &&
                    Children(source.parent).Count(s => CanonicalSourceName(s.name) == canonical) == 1)
                {
                    Confirm(source, renamedChildren[0], MappingReason.RenamedChild);
                    return;
                }

                // Humanoid bones defined by both Animators
                if (sourceAnimatorBones.TryGetValue(source, out var animatorBone) &&
                    targetAnimatorBones.TryGetValue(animatorBone, out var animatorTarget) &&
                    IsAvailable(source, animatorTarget))
                {
                    Confirm(source, animatorTarget, MappingReason.HumanoidAnimator);
                    return;
                }

                // Humanoid bone synonym dictionary
                if (sourceDictionaryBones.TryGetValue(source, out var dictionaryBone) &&
                    targetDictionaryBones.TryGetValue(dictionaryBone, out var dictionaryTargets))
                {
                    var unused = dictionaryTargets.Where(t => IsAvailable(source, t)).ToList();
                    bool unique = sourceDictionaryBoneCount[dictionaryBone] == 1 && dictionaryTargets.Count == 1;

                    if (unique && unused.Count == 1)
                    {
                        Confirm(source, unused[0], MappingReason.HumanoidDictionary);
                        return;
                    }

                    if (unused.Count > 0)
                    {
                        Suggest(source, unused, MappingReason.HumanoidDictionary);
                        return;
                    }
                }

                // Exact name anywhere in the same region of the target
                if (targetsByName.TryGetValue((inArmature, source.name), out var sameName))
                {
                    var unused = sameName.Where(t => !usedTargets.Contains(t)).ToList();
                    if (unused.Count == 1 && sameName.Count == 1 && sourceNameCount[(inArmature, source.name)] == 1)
                    {
                        Confirm(source, unused[0], MappingReason.UniqueName);
                        return;
                    }

                    if (unused.Count > 0)
                    {
                        Suggest(source, unused, MappingReason.AmbiguousName);
                        return;
                    }
                }

                // Prefix / suffix match away from the mapped parent: plausible, but the structure does not back it up
                string affixName = affixRule?.ExpectedTargetName(source.name);
                if (affixName != null && targetsByName.TryGetValue((inArmature, affixName), out var affixTargets))
                {
                    var unused = affixTargets.Where(t => !usedTargets.Contains(t)).ToList();
                    if (unused.Count > 0)
                    {
                        Suggest(source, unused, MappingReason.AffixStripped);
                        return;
                    }
                }

                // Avatars often lack UpperChest. Chest is usually already taken, so this stays a suggestion.
                if (sourceDictionaryBones.TryGetValue(source, out var bone) && bone == HumanBodyBones.UpperChest &&
                    !targetDictionaryBones.ContainsKey(HumanBodyBones.UpperChest) &&
                    targetDictionaryBones.TryGetValue(HumanBodyBones.Chest, out var chestTargets) &&
                    chestTargets.Count == 1)
                {
                    Suggest(source, chestTargets, MappingReason.UpperChestFallback);
                    return;
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
                    Suggest(source, normalizedTargets, MappingReason.NormalizedName);
                    return;
                }

                // Fuzzy matches are listed as candidates only; nothing is preselected.
                var fuzzyTargets = targetAll
                    .Where(t => IsAvailable(source, t))
                    .Where(t => ObjectMatcher.HasCommonBaseName(t.name, source.name, FuzzyMinTokenLength))
                    .ToList();

                Map.Set(new TransformMapping
                {
                    Source = source,
                    State = MappingState.Unmapped,
                    Reason = MappingReason.None,
                    Candidates = Rank(source, fuzzyTargets),
                });
            }

            #endregion

            #region Names

            private string CanonicalSourceName(string name)
            {
                string stripped = StripRenameSuffix(name);
                if (affixRule != null && !affixRule.OnTarget) stripped = StripRenameSuffix(affixRule.Strip(stripped));
                return stripped;
            }

            private string CanonicalTargetName(string name)
            {
                string stripped = StripRenameSuffix(name);
                if (affixRule != null && affixRule.OnTarget) stripped = StripRenameSuffix(affixRule.Strip(stripped));
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
                    .Select(s => StripRenameSuffix(s.name))
                    .Where(n => n.Length >= MinAffixBaseNameLength)
                    .Distinct()
                    .ToList();
                var targetNames = targetAll
                    .Where(t => !usedTargets.Contains(t) && (!separateRegions || TargetInArmature(t)))
                    .Select(t => StripRenameSuffix(t.name))
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

            private void Confirm(Transform source, Transform target, MappingReason reason)
            {
                Map.Set(new TransformMapping
                {
                    Source = source,
                    Target = target,
                    State = MappingState.Confirmed,
                    Reason = reason,
                });
                usedTargets.Add(target);
            }

            private void Suggest(Transform source, List<Transform> targets, MappingReason reason)
            {
                var candidates = Rank(source, targets);
                Map.Set(new TransformMapping
                {
                    Source = source,
                    Target = candidates[0].Target,
                    State = MappingState.NeedsReview,
                    Reason = reason,
                    Candidates = candidates,
                });
            }

            private List<MappingCandidate> Rank(Transform source, List<Transform> targets)
            {
                string sourcePath = ObjectMatcher.GetRelativePathFromRoot(source, sourceRoot);
                return targets
                    .Select(t => new MappingCandidate
                    {
                        Target = t,
                        Score = ObjectMatcher.PathSegmentScore(sourcePath, targetPaths[t]),
                    })
                    .OrderByDescending(c => c.Score)
                    .ThenBy(c => ObjectMatcher.LevenshteinDistance(sourcePath, targetPaths[c.Target]))
                    .Take(MaxCandidates)
                    .ToList();
            }

            private static IEnumerable<Transform> Children(Transform parent)
            {
                if (parent == null) yield break;
                foreach (Transform child in parent)
                    yield return child;
            }

            private static Transform FindChildByNameAndOccurrence(Transform parent, string name, int occurrence)
            {
                int seen = 0;
                foreach (Transform child in parent)
                {
                    if (child.name != name) continue;
                    if (seen == occurrence) return child;
                    seen++;
                }

                return null;
            }

            private static List<Transform> Descendants(Transform root)
            {
                return root.GetComponentsInChildren<Transform>(true).Where(t => t != root).ToList();
            }

            /// <summary>
            /// Reads humanoid bones from the Avatar asset instead of Animator.GetBoneTransform,
            /// which returns null for prefab assets.
            /// </summary>
            private static Dictionary<HumanBodyBones, Transform> CollectAnimatorBones(Transform root)
            {
                var result = new Dictionary<HumanBodyBones, Transform>();

                var animator = root.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return result;

                var byName = new Dictionary<string, Transform>();
                foreach (var transform in Descendants(root))
                {
                    if (!byName.ContainsKey(transform.name)) byName[transform.name] = transform;
                }

                foreach (var humanBone in animator.avatar.humanDescription.human)
                {
                    int index = Array.IndexOf(HumanTrait.BoneName, humanBone.humanName);
                    if (index < 0) continue;
                    if (byName.TryGetValue(humanBone.boneName, out var transform))
                        result[(HumanBodyBones)index] = transform;
                }

                return result;
            }

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
