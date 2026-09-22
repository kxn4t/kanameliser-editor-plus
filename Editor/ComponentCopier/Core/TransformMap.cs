using System.Collections.Generic;
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

        public IEnumerable<TransformMapping> All => mappings.Values;

        public void Set(TransformMapping mapping) => mappings[mapping.Source] = mapping;

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
