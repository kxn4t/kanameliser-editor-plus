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
    /// </summary>
    internal static class TransformMapper
    {
        private const int MinAffixVotes = 3;
        private const int MinAffixBaseNameLength = 3;
        private const int MaxCandidates = 8;
        private const int FuzzyMinTokenLength = 3;

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

        private sealed class Context
        {
            public readonly TransformMap Map;

            private readonly Transform sourceRoot;
            private readonly Transform targetRoot;
            private readonly IReadOnlyDictionary<Transform, Transform> manual;
            private readonly HashSet<Transform> usedTargets = new();

            private readonly List<Transform> targetAll;
            private readonly Dictionary<string, List<Transform>> targetsByName = new();
            private readonly Dictionary<string, int> sourceNameCount = new();
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

                targetAll = Descendants(targetRoot);
                foreach (var target in targetAll)
                {
                    if (!targetsByName.TryGetValue(target.name, out var list))
                        targetsByName[target.name] = list = new List<Transform>();
                    list.Add(target);

                    targetPaths[target] = ObjectMatcher.GetRelativePathFromRoot(target, targetRoot);

                    if (HumanoidBoneDictionary.TryFindBone(target.name, out var bone))
                    {
                        if (!targetDictionaryBones.TryGetValue(bone, out var bones))
                            targetDictionaryBones[bone] = bones = new List<Transform>();
                        bones.Add(target);
                    }
                }

                foreach (var source in Descendants(sourceRoot))
                {
                    sourceNameCount.TryGetValue(source.name, out var count);
                    sourceNameCount[source.name] = count + 1;

                    if (HumanoidBoneDictionary.TryFindBone(source.name, out var bone))
                    {
                        sourceDictionaryBones[source] = bone;
                        sourceDictionaryBoneCount.TryGetValue(bone, out var boneCount);
                        sourceDictionaryBoneCount[bone] = boneCount + 1;
                    }
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

                ResolvePass(sourceRoot, targetRoot);
            }

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
                    if (match == null || usedTargets.Contains(match)) continue;

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
                // Same-name child of the nearest mapped ancestor (covers extra intermediate objects on the source side)
                var sameNameChildren = UnusedChildren(contextTarget).Where(t => t.name == source.name).ToList();
                if (sameNameChildren.Count == 1)
                {
                    Confirm(source, sameNameChildren[0], MappingReason.ChildOfMappedParent);
                    return;
                }

                // Humanoid bones defined by both Animators
                if (sourceAnimatorBones.TryGetValue(source, out var animatorBone) &&
                    targetAnimatorBones.TryGetValue(animatorBone, out var animatorTarget) &&
                    !usedTargets.Contains(animatorTarget))
                {
                    Confirm(source, animatorTarget, MappingReason.HumanoidAnimator);
                    return;
                }

                // Humanoid bone synonym dictionary
                if (sourceDictionaryBones.TryGetValue(source, out var dictionaryBone) &&
                    targetDictionaryBones.TryGetValue(dictionaryBone, out var dictionaryTargets))
                {
                    var unused = dictionaryTargets.Where(t => !usedTargets.Contains(t)).ToList();
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

                // Exact name anywhere in the target
                if (targetsByName.TryGetValue(source.name, out var sameName))
                {
                    var unused = sameName.Where(t => !usedTargets.Contains(t)).ToList();
                    if (unused.Count == 1 && sameName.Count == 1 && sourceNameCount[source.name] == 1)
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

                // Common prefix / suffix. Kept as a suggestion so the whole rule can be confirmed in bulk.
                var affixName = affixRule?.Apply(source.name);
                if (affixName != null && targetsByName.TryGetValue(affixName, out var affixTargets))
                {
                    var unused = affixTargets.Where(t => !usedTargets.Contains(t)).ToList();
                    var underContext = unused.Where(t => t.parent == contextTarget).ToList();
                    var picked = underContext.Count > 0 ? underContext : unused;
                    if (picked.Count > 0)
                    {
                        Suggest(source, picked, MappingReason.AffixStripped);
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
                    .Where(t => !usedTargets.Contains(t))
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
                    .Where(t => !usedTargets.Contains(t))
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

            private IEnumerable<Transform> UnusedChildren(Transform parent)
            {
                foreach (Transform child in parent)
                {
                    if (!usedTargets.Contains(child)) yield return child;
                }
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

            /// <summary>
            /// Detects a prefix / suffix shared by many bones on one side (e.g. "Hips" vs "Hips_v2").
            /// </summary>
            private AffixRule DetectAffixRule()
            {
                var sourceNames = Descendants(sourceRoot)
                    .Where(s => !Map.Contains(s))
                    .Select(s => s.name)
                    .Where(n => n.Length >= MinAffixBaseNameLength)
                    .Distinct()
                    .ToList();
                var targetNames = targetAll
                    .Where(t => !usedTargets.Contains(t))
                    .Select(t => t.name)
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
        }

        private sealed class AffixRule
        {
            private readonly bool onTarget;
            private readonly string prefix;
            private readonly string suffix;

            public AffixRule(bool onTarget, string prefix, string suffix)
            {
                this.onTarget = onTarget;
                this.prefix = prefix;
                this.suffix = suffix;
            }

            /// <summary>Returns the expected target name, or null when the rule does not apply.</summary>
            public string Apply(string sourceName)
            {
                if (onTarget) return prefix + sourceName + suffix;

                if (sourceName.Length <= prefix.Length + suffix.Length) return null;
                if (!sourceName.StartsWith(prefix, StringComparison.Ordinal)) return null;
                if (!sourceName.EndsWith(suffix, StringComparison.Ordinal)) return null;
                return sourceName.Substring(prefix.Length, sourceName.Length - prefix.Length - suffix.Length);
            }
        }
    }
}
