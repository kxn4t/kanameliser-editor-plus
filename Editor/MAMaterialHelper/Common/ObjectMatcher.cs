using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Handles object matching logic for finding corresponding objects between hierarchies.
    /// Uses a 5-tier priority algorithm with fuzzy path/name matching for nested prefab support.
    /// Algorithm reference: color-variant-generator RendererMatcher.cs V3
    /// </summary>
    public static class ObjectMatcher
    {
        // Scoring constants for PathSegmentScore
        private const float ExactSegmentScore = 100f;
        private const float NormalizedSegmentScore = 75f;
        private const float FuzzySegmentScore = 50f;
        private const float SegmentWeightDecay = 0.7f;
        private const float GapPenalty = -5f;
        private const float ExactAncestorBonus = 30f;
        private const float FuzzyAncestorBonus = 20f;
        private const int EligibilityMinTokenLength = 3;
        private const int RankingMinTokenLength = 1;
        private const float NormalizedNameScore = 100f;
        private const float FuzzyNameScore = 60f;

        private static readonly char[] NameSeparators = { '_', '-', '.', ' ' };

        // Suffix patterns for NormalizeName, order matters
        private static readonly Regex[] SuffixPatterns =
        {
            new Regex(@"[_\-\s]\d+$", RegexOptions.Compiled),                                    // Numeric
            new Regex(@"\s*\(\d+\)$", RegexOptions.Compiled),                                     // Parenthesized
            new Regex(@"\.\d{3}$", RegexOptions.Compiled),                                         // Blender
            new Regex(@"[_\-\s]v(?:er)?\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase),   // Version
            new Regex(@"[_\-\s](?:copy|variant)$", RegexOptions.Compiled | RegexOptions.IgnoreCase) // Copy
        };

        private static readonly Regex TrailingSeparators = new Regex(@"[_\-\.\s]+$", RegexOptions.Compiled);

        /// <summary>
        /// Finds a matching object in the target hierarchy with 5-tier matching.
        /// P1: Exact path + name, P2: Same depth + exact name,
        /// P3: Exact name (any depth), P4: Case-insensitive name, P5: Similar name (scored)
        /// </summary>
        /// <param name="root">Root transform of the target hierarchy to search.</param>
        /// <param name="objectName">Name of the source object to match.</param>
        /// <param name="sourceRelativePath">Relative path of the source object from its root.</param>
        /// <param name="sourceDepth">Depth of the source object in its hierarchy.</param>
        /// <param name="sourceRootName">Name of the source root object for ancestor context scoring.</param>
        /// <param name="matchedPaths">
        /// Optional set of already-matched target paths (relative to root).
        /// Matched targets are excluded from candidates to prevent duplicate matching.
        /// The selected match is automatically added to this set.
        /// </param>
        /// <param name="sourceRendererType">
        /// Optional renderer type name (e.g. "SkinnedMeshRenderer", "MeshRenderer").
        /// When non-empty, candidates are filtered to match this renderer type.
        /// </param>
        public static Transform FindMatchingObject(Transform root, string objectName, string sourceRelativePath, int sourceDepth = 0, string sourceRootName = "", HashSet<string> matchedPaths = null, string sourceRendererType = "")
        {
            if (root == null) return null;

            Debug.Log($"[MA Material Helper] MATCH: Looking for '{objectName}' (path: '{sourceRelativePath}', depth: {sourceDepth}, root: '{sourceRootName}') in '{root.name}'");

            // Pre-compute relative paths for all renderer transforms to avoid repeated hierarchy walks
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var candidates = allTransforms
                .Where(t => t.GetComponent<Renderer>() != null)
                .Where(t => string.IsNullOrEmpty(sourceRendererType) ||
                            t.GetComponent<Renderer>()?.GetType().Name == sourceRendererType)
                .Select(t => (transform: t, path: GetRelativePathFromRoot(t, root)))
                .Where(c => matchedPaths == null || !matchedPaths.Contains(c.path))
                .ToList();
            Debug.Log($"[MA Material Helper] MATCH: Objects with Renderer: {candidates.Count}" +
                (matchedPaths != null ? $" (excluded {matchedPaths.Count} already matched)" : ""));

            var result = TryMatch(candidates, objectName, sourceRelativePath, sourceDepth, sourceRootName);

            if (result.transform != null)
            {
                // Track matched target to prevent duplicate matching
                matchedPaths?.Add(result.path);
                return result.transform;
            }

            // P5 cross-type fallback: relax rendererType filter
            // Catches cross-renderer-type matches (e.g. MeshRenderer ↔ SkinnedMeshRenderer)
            // when source and target use different renderer types for the same logical mesh.
            if (!string.IsNullOrEmpty(sourceRendererType))
            {
                var crossTypeCandidates = allTransforms
                    .Where(t => t.GetComponent<Renderer>() != null)
                    .Where(t => t.GetComponent<Renderer>().GetType().Name != sourceRendererType)
                    .Select(t => (transform: t, path: GetRelativePathFromRoot(t, root)))
                    .Where(c => matchedPaths == null || !matchedPaths.Contains(c.path))
                    .ToList();

                if (crossTypeCandidates.Count > 0)
                {
                    string normalizedSource = NormalizeName(objectName);
                    var p5CrossType = crossTypeCandidates
                        .Where(c =>
                        {
                            string normalizedCandidate = NormalizeName(c.transform.name);
                            return string.Equals(normalizedSource, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
                                   HasCommonBaseName(c.transform.name, objectName, EligibilityMinTokenLength);
                        })
                        .ToList();

                    if (p5CrossType.Count > 0)
                    {
                        var selected = SelectBestFuzzyCandidate(p5CrossType, objectName, sourceRelativePath, sourceDepth, sourceRootName);
                        Debug.Log($"[MA Material Helper] MATCH: P5 - Fuzzy (cross-type): '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                        matchedPaths?.Add(selected.path);
                        return selected.transform;
                    }
                }
            }

            // Log objects that were excluded due to no Renderer
            var excludedNoRenderer = allTransforms
                .Where(t => t.GetComponent<Renderer>() == null && t.name == objectName)
                .ToList();
            if (excludedNoRenderer.Count > 0)
            {
                Debug.LogWarning($"[MA Material Helper] MATCH: Found {excludedNoRenderer.Count} matching objects without Renderer (excluded):");
                foreach (var excluded in excludedNoRenderer.Take(5))
                {
                    Debug.LogWarning($"[MA Material Helper] MATCH:   - '{excluded.name}' at '{GetFullPath(excluded)}'");
                }
            }

            Debug.Log($"[MA Material Helper] MATCH: No match found for '{objectName}'");
            return null;
        }

        /// <summary>
        /// Tries to find a matching target using 5 priority levels.
        /// Candidates are (Transform, precomputed relative path) tuples.
        /// Returns the matched tuple or a default (null, null) if no match found.
        /// </summary>
        private static (Transform transform, string path) TryMatch(
            List<(Transform transform, string path)> candidates,
            string objectName, string sourceRelativePath, int sourceDepth, string sourceRootName)
        {
            // P1: Exact relative path AND exact name match
            var p1 = candidates
                .Where(c => c.path == sourceRelativePath && c.transform.name == objectName)
                .ToList();
            if (p1.Count > 0)
            {
                var selected = SelectBestCandidate(p1, sourceRelativePath, sourceDepth, sourceRootName);
                Debug.Log($"[MA Material Helper] MATCH: P1 - Exact path: '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                return selected;
            }

            // P2: Same depth + exact name match
            var p2 = candidates
                .Where(c =>
                {
                    int depth = string.IsNullOrEmpty(c.path) ? 0 : c.path.Split('/').Length;
                    return depth == sourceDepth && c.transform.name == objectName;
                })
                .ToList();
            if (p2.Count > 0)
            {
                var selected = SelectBestCandidate(p2, sourceRelativePath, sourceDepth, sourceRootName);
                Debug.Log($"[MA Material Helper] MATCH: P2 - Depth+name: '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                return selected;
            }

            // P3: Exact name match (any depth)
            var p3 = candidates.Where(c => c.transform.name == objectName).ToList();
            if (p3.Count > 0)
            {
                var selected = SelectBestCandidate(p3, sourceRelativePath, sourceDepth, sourceRootName);
                Debug.Log($"[MA Material Helper] MATCH: P3 - Name: '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                return selected;
            }

            // P4: Case-insensitive name match
            var p4 = candidates
                .Where(c => string.Equals(c.transform.name, objectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (p4.Count > 0)
            {
                var selected = SelectBestCandidate(p4, sourceRelativePath, sourceDepth, sourceRootName);
                Debug.Log($"[MA Material Helper] MATCH: P4 - Case-insensitive: '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                return selected;
            }

            // P5: Similar name match (scored)
            // Eligibility: NormalizeName match OR HasCommonBaseName(minToken=3)
            string normalizedSource = NormalizeName(objectName);
            var p5 = candidates
                .Where(c =>
                {
                    string normalizedCandidate = NormalizeName(c.transform.name);
                    return string.Equals(normalizedSource, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
                           HasCommonBaseName(c.transform.name, objectName, EligibilityMinTokenLength);
                })
                .ToList();
            if (p5.Count > 0)
            {
                var selected = SelectBestFuzzyCandidate(p5, objectName, sourceRelativePath, sourceDepth, sourceRootName);
                Debug.Log($"[MA Material Helper] MATCH: P5 - Fuzzy: '{selected.transform.name}' at '{GetFullPath(selected.transform)}'");
                return selected;
            }

            return (null, null);
        }

        #region Candidate Selection

        /// <summary>
        /// Selects the best candidate from multiple matches for P1-P4 tiers using path segment scoring,
        /// ancestor context, depth proximity, and Levenshtein distance on full paths.
        /// </summary>
        private static (Transform transform, string path) SelectBestCandidate(
            List<(Transform transform, string path)> candidates,
            string sourceRelativePath,
            int sourceDepth,
            string sourceRootName)
        {
            if (candidates.Count == 1) return candidates[0];

            return candidates
                .OrderByDescending(c =>
                    PathSegmentScore(sourceRelativePath, c.path) +
                    AncestorContextScore(sourceRootName, c.path))
                .ThenBy(c =>
                {
                    int depth = string.IsNullOrEmpty(c.path) ? 0 : c.path.Split('/').Length;
                    return Math.Abs(depth - sourceDepth);
                })
                .ThenBy(c => LevenshteinDistance(sourceRelativePath ?? "", c.path ?? ""))
                .First();
        }

        /// <summary>
        /// Selects the best candidate for P5 fuzzy tier using combined name + path scoring.
        /// Score: NameScore(norm=100, fuzzy=60) + PathSegmentScore + AncestorContextScore.
        /// Tiebreakers: highest score, then depth proximity, then Levenshtein distance.
        /// </summary>
        private static (Transform transform, string path) SelectBestFuzzyCandidate(
            List<(Transform transform, string path)> candidates,
            string objectName,
            string sourceRelativePath,
            int sourceDepth,
            string sourceRootName)
        {
            if (candidates.Count == 1) return candidates[0];

            string normalizedSource = NormalizeName(objectName);

            return candidates
                .OrderByDescending(c =>
                {
                    string normalizedCandidate = NormalizeName(c.transform.name);
                    float nameScore = string.Equals(normalizedSource, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                        ? NormalizedNameScore
                        : FuzzyNameScore;
                    return nameScore +
                           PathSegmentScore(sourceRelativePath, c.path) +
                           AncestorContextScore(sourceRootName, c.path);
                })
                .ThenBy(c =>
                {
                    int depth = string.IsNullOrEmpty(c.path) ? 0 : c.path.Split('/').Length;
                    return Math.Abs(depth - sourceDepth);
                })
                .ThenBy(c => LevenshteinDistance(sourceRelativePath ?? "", c.path ?? ""))
                .First();
        }

        #endregion

        #region Path Utilities

        /// <summary>
        /// Gets the relative path from a specific root.
        /// </summary>
        public static string GetRelativePathFromRoot(Transform transform, Transform root)
        {
            if (transform == null || root == null) return "";

            var path = new List<string>();
            var current = transform;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        /// <summary>
        /// Gets the full hierarchy path of a transform.
        /// </summary>
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return "";

            var path = transform.name;
            var parent = transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        #endregion

        #region Name Normalization

        /// <summary>
        /// Strips trailing structural suffixes repeatedly until stable.
        /// Never strips if result would be empty.
        /// After pattern stripping, trims trailing separators (_-. space).
        /// Patterns (order matters, first match per pass, restart):
        /// 1. Numeric: [_\-\s]\d+$
        /// 2. Parenthesized: \s*\(\d+\)$
        /// 3. Blender: \.\d{3}$
        /// 4. Version: [_\-\s]v(?:er)?\d+$
        /// 5. Copy: [_\-\s](?:copy|variant)$
        /// </summary>
        internal static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";

            string current = name;
            bool changed = true;

            while (changed)
            {
                changed = false;

                foreach (var pattern in SuffixPatterns)
                {
                    var match = pattern.Match(current);
                    if (match.Success)
                    {
                        string candidate = current.Substring(0, match.Index);
                        if (candidate.Length > 0)
                        {
                            current = candidate;
                            changed = true;
                            break; // restart from first pattern
                        }
                    }
                }
            }

            // Trim trailing separators
            string trimmed = TrailingSeparators.Replace(current, "");
            if (trimmed.Length > 0)
                current = trimmed;

            // Never return empty — fall back to original name
            return string.IsNullOrEmpty(current) ? name : current;
        }

        #endregion

        #region Fuzzy Matching Utilities

        /// <summary>
        /// Computes a path similarity score by aligning segments from the leaf side.
        /// 3-level scoring: exact=100, normalized=75, fuzzy(minToken=1)=50.
        /// Weight decreases for segments further from the leaf.
        /// </summary>
        internal static float PathSegmentScore(string sourcePath, string targetPath)
        {
            string[] sourceSegments = string.IsNullOrEmpty(sourcePath)
                ? Array.Empty<string>() : sourcePath.Split('/');
            string[] targetSegments = string.IsNullOrEmpty(targetPath)
                ? Array.Empty<string>() : targetPath.Split('/');

            if (sourceSegments.Length == 0 && targetSegments.Length == 0)
                return 0f;

            float score = 0f;
            float weight = 1.0f;
            int alignCount = Math.Min(sourceSegments.Length, targetSegments.Length);

            // Compare segments from the leaf upward
            for (int i = 0; i < alignCount; i++)
            {
                string s = sourceSegments[sourceSegments.Length - 1 - i];
                string t = targetSegments[targetSegments.Length - 1 - i];

                if (s == t)
                    score += ExactSegmentScore * weight;
                else if (string.Equals(NormalizeName(s), NormalizeName(t), StringComparison.OrdinalIgnoreCase))
                    score += NormalizedSegmentScore * weight;
                else if (HasCommonBaseName(s, t, RankingMinTokenLength))
                    score += FuzzySegmentScore * weight;
                // else: mismatch, adds nothing

                weight *= SegmentWeightDecay;
            }

            // Penalty for unmatched segments at the top
            int extraSegments = Math.Abs(sourceSegments.Length - targetSegments.Length);
            score += extraSegments * GapPenalty;

            return score;
        }

        /// <summary>
        /// Computes a bonus score when the source root object name matches
        /// an ancestor in the target path (exact or fuzzy).
        /// Helps disambiguate multi-root copy scenarios.
        /// Uses minTokenLength=1 for HasCommonBaseName.
        /// </summary>
        internal static float AncestorContextScore(string sourceRootName, string targetPath)
        {
            if (string.IsNullOrEmpty(sourceRootName) || string.IsNullOrEmpty(targetPath))
                return 0f;

            string[] targetSegments = targetPath.Split('/');

            foreach (var segment in targetSegments)
            {
                if (segment == sourceRootName)
                    return ExactAncestorBonus;
                if (HasCommonBaseName(segment, sourceRootName, RankingMinTokenLength))
                    return FuzzyAncestorBonus;
            }

            return 0f;
        }

        /// <summary>
        /// Determines whether two names share a common base name using token-based comparison.
        /// Returns true if:
        /// - Both names have at least 2 tokens and their first tokens match, OR
        /// - One name is a single token that equals the first token of the other multi-token name
        ///   (e.g. "Shoes" matches "Shoes_red").
        /// In both cases the matching token must be >= minTokenLength.
        /// Separators: '_', '-', '.', space.
        /// </summary>
        /// <param name="a">First name to compare.</param>
        /// <param name="b">Second name to compare.</param>
        /// <param name="minTokenLength">Minimum length of the first token for a match.</param>
        internal static bool HasCommonBaseName(string a, string b, int minTokenLength = 3)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;

            // Token-based only: split by separators and compare first tokens
            string[] partsA = a.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
            string[] partsB = b.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);

            // Both have 2+ tokens: compare first tokens
            if (partsA.Length >= 2 && partsB.Length >= 2 &&
                partsA[0].Length >= minTokenLength && partsA[0] == partsB[0])
                return true;

            // One is a single token matching the other's first token
            // e.g. "Shoes" ↔ "Shoes_red"
            if (partsA.Length == 1 && partsB.Length >= 2 &&
                partsA[0].Length >= minTokenLength && partsA[0] == partsB[0])
                return true;
            if (partsB.Length == 1 && partsA.Length >= 2 &&
                partsB[0].Length >= minTokenLength && partsB[0] == partsA[0])
                return true;

            return false;
        }

        /// <summary>
        /// Computes the Levenshtein (edit) distance between two strings.
        /// Used as a final tiebreaker for candidate selection.
        /// Uses single-row optimization for O(n) memory instead of O(n×m).
        /// </summary>
        internal static int LevenshteinDistance(string s, string t)
        {
            if (s == null) s = "";
            if (t == null) t = "";

            int sLen = s.Length;
            int tLen = t.Length;

            if (sLen == 0) return tLen;
            if (tLen == 0) return sLen;

            var prev = new int[tLen + 1];
            var curr = new int[tLen + 1];

            for (int j = 0; j <= tLen; j++)
                prev[j] = j;

            for (int i = 1; i <= sLen; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= tLen; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }

                var temp = prev;
                prev = curr;
                curr = temp;
            }

            return prev[tLen];
        }

        #endregion
    }
}
