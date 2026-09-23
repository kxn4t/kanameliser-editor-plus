using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum MappingState
    {
        /// <summary>Resolved with high confidence. Used automatically.</summary>
        Confirmed,
        /// <summary>A suggestion exists but must be confirmed by the user before it is used.</summary>
        NeedsReview,
        /// <summary>No counterpart found. <see cref="TransformMapping.Candidates"/> may still list weak candidates.</summary>
        Unmapped,
        /// <summary>Specified by the user. A null target means "has no counterpart".</summary>
        Manual,
    }

    internal enum MappingReason
    {
        None,
        Root,
        ExactPath,
        ChildOfMappedParent,
        /// <summary>
        /// Child of the mapped parent whose name differs only by a rename suffix (".1", ".001", " (1)")
        /// or by the detected prefix / suffix.
        /// </summary>
        RenamedChild,
        HumanoidAnimator,
        HumanoidDictionary,
        UniqueName,
        AmbiguousName,
        AffixStripped,
        UpperChestFallback,
        NormalizedName,
        /// <summary>Humanoid bone paired with the bone on the other side (mirror copy).</summary>
        MirroredBone,
        /// <summary>Name with its side marker flipped: "Hand_L" ↔ "Hand_R" (mirror copy).</summary>
        MirroredName,
        /// <summary>Object on the middle line that is its own counterpart (mirror copy).</summary>
        SelfCenter,
        Manual,
    }

    internal sealed class MappingCandidate
    {
        public Transform Target;
        public float Score;
    }

    internal sealed class TransformMapping
    {
        public Transform Source;
        public Transform Target;
        public MappingState State;
        public MappingReason Reason;
        public List<MappingCandidate> Candidates = new();

        /// <summary>True when the mapping may be used without further user confirmation.</summary>
        public bool IsUsable =>
            Target != null && (State == MappingState.Confirmed || State == MappingState.Manual);

        public static TransformMapping Confirmed(Transform source, Transform target, MappingReason reason) =>
            new TransformMapping { Source = source, Target = target, State = MappingState.Confirmed, Reason = reason };

        /// <summary>The best of the ranked candidates as a suggestion; unmapped when there is none.</summary>
        public static TransformMapping Suggested(
            Transform source, List<MappingCandidate> candidates, MappingReason reason)
        {
            if (candidates.Count == 0) return Unmapped(source, candidates);

            return new TransformMapping
            {
                Source = source,
                Target = candidates[0].Target,
                State = MappingState.NeedsReview,
                Reason = reason,
                Candidates = candidates,
            };
        }

        /// <param name="candidates">Weak candidates the user may pick from; nothing is preselected.</param>
        public static TransformMapping Unmapped(Transform source, List<MappingCandidate> candidates) =>
            new TransformMapping
            {
                Source = source,
                State = MappingState.Unmapped,
                Reason = MappingReason.None,
                Candidates = candidates,
            };

        /// <param name="target">Null when the user said the source has no counterpart.</param>
        public static TransformMapping Manual(Transform source, Transform target) =>
            new TransformMapping { Source = source, Target = target, State = MappingState.Manual, Reason = MappingReason.Manual };
    }

    /// <summary>The candidates a mapping offers to choose from, shared by both mappers.</summary>
    internal static class MappingCandidates
    {
        public const int MaxCount = 8;

        /// <summary>Shortest name token two names must share to make a fuzzy candidate.</summary>
        public const int FuzzyMinTokenLength = 3;

        /// <summary>Best first: by how much of the path agrees, then by how close the paths are.</summary>
        public static List<MappingCandidate> Rank(
            string sourcePath, IEnumerable<Transform> targets, IReadOnlyDictionary<Transform, string> targetPaths)
        {
            return targets
                .Select(t => new MappingCandidate
                {
                    Target = t,
                    Score = ObjectMatcher.PathSegmentScore(sourcePath, targetPaths[t]),
                })
                .OrderByDescending(c => c.Score)
                .ThenBy(c => ObjectMatcher.LevenshteinDistance(sourcePath, targetPaths[c.Target]))
                .Take(MaxCount)
                .ToList();
        }

        /// <summary>
        /// The automatic answer for a source that the user mapped by hand: a confirmed target, or the candidates
        /// it would have offered.
        /// </summary>
        public static List<MappingCandidate> FromAutomatic(TransformMapping automatic)
        {
            return automatic.State == MappingState.Confirmed && automatic.Target != null
                ? new List<MappingCandidate> { new MappingCandidate { Target = automatic.Target, Score = 1f } }
                : automatic.Candidates;
        }
    }

    /// <summary>
    /// Correspondence table from source transforms to target transforms.
    /// The same table decides both where components are placed and where references are redirected.
    /// </summary>
    internal sealed class TransformMap
    {
        private readonly Dictionary<Transform, TransformMapping> mappings = new();

        public Transform SourceRoot { get; }
        public Transform TargetRoot { get; }
        public SkeletonInfo SourceSkeleton { get; }
        public SkeletonInfo TargetSkeleton { get; }

        public TransformMap(Transform sourceRoot, Transform targetRoot)
        {
            SourceRoot = sourceRoot;
            TargetRoot = targetRoot;
            SourceSkeleton = SkeletonInfo.Analyze(sourceRoot);
            TargetSkeleton = SkeletonInfo.Analyze(targetRoot);
        }

        /// <summary>A map within one hierarchy (mirror copy): both sides share the root and the skeleton.</summary>
        public TransformMap(Transform root)
        {
            SourceRoot = root;
            TargetRoot = root;
            SourceSkeleton = TargetSkeleton = SkeletonInfo.Analyze(root);
        }

        private MirrorSides sides;

        /// <summary>
        /// The side of each source object, for a map within one hierarchy (mirror copy). The mirror mapper
        /// passes the one it pairs the objects with.
        /// </summary>
        public MirrorSides Sides
        {
            get => sides ??= new MirrorSides(SourceRoot);
            set => sides = value;
        }

        public IEnumerable<TransformMapping> All => mappings.Values;

        public void Set(TransformMapping mapping) => mappings[mapping.Source] = mapping;

        /// <summary>
        /// Records the pair of roots and the user's mappings, which come before every automatic rule. Manual
        /// mappings are part of every map, whatever object they are about: one map serves the source, another
        /// the avatar around it, and the user's word holds in both.
        /// </summary>
        public void SetRootAndManual(IReadOnlyDictionary<Transform, Transform> manual)
        {
            Set(TransformMapping.Confirmed(SourceRoot, TargetRoot, MappingReason.Root));
            if (manual == null) return;

            foreach (var pair in manual)
            {
                if (pair.Key == null || pair.Key == SourceRoot) continue;
                Set(TransformMapping.Manual(pair.Key, pair.Value));
            }
        }

        /// <summary>
        /// A manual mapping skips every automatic rule, which would leave it without candidates: after choosing
        /// "no counterpart" (or "keep as is") the menu had nothing to offer for changing one's mind. So the
        /// automatic answer is worked out after all and kept as candidates only.
        /// </summary>
        /// <param name="resolve">
        /// The automatic answer for a source. Must not change the map: called last, when everything else has
        /// claimed its target, the answer only fills the menu.
        /// </param>
        public void OfferAutomaticAnswers(IEnumerable<Transform> manualSources, Func<Transform, TransformMapping> resolve)
        {
            foreach (var source in manualSources)
            {
                if (source == null || source == SourceRoot || !source.IsChildOf(SourceRoot)) continue;

                var mapping = Get(source);
                if (mapping == null || mapping.State != MappingState.Manual) continue;

                mapping.Candidates = MappingCandidates.FromAutomatic(resolve(source));
            }
        }

        public bool Contains(Transform source) => source != null && mappings.ContainsKey(source);

        public TransformMapping Get(Transform source)
        {
            if (source == null) return null;
            return mappings.TryGetValue(source, out var mapping) ? mapping : null;
        }

        /// <summary>
        /// Resolves a source transform to its target. Fails for unconfirmed suggestions.
        /// </summary>
        public bool TryResolve(Transform source, out Transform target)
        {
            var mapping = Get(source);
            if (mapping != null && mapping.IsUsable)
            {
                target = mapping.Target;
                return true;
            }

            target = null;
            return false;
        }
    }
}
