using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Handles object matching logic for finding corresponding objects between hierarchies
    /// </summary>
    public static class ObjectMatcher
    {
        /// <summary>
        /// Finds a matching object in the target hierarchy with multi-stage matching
        /// </summary>
        public static Transform FindMatchingObject(Transform root, string objectName, string sourceRelativePath, int sourceDepth = 0, string sourceRootName = "")
        {
            if (root == null) return null;

            Debug.Log($"[MA Material Helper] MATCH: Looking for '{objectName}' (path: '{sourceRelativePath}', depth: {sourceDepth}, root: '{sourceRootName}') in '{root.name}'");

            // Get all transforms with Renderer components
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var transformsWithRenderer = allTransforms
                .Where(t => t.GetComponent<Renderer>() != null)
                .ToList();
            Debug.Log($"[MA Material Helper] MATCH: Objects with Renderer: {transformsWithRenderer.Count}");

            string targetRelativePath = sourceRelativePath;
            int targetDepth = sourceDepth;

            // Priority 1: Exact relative path AND object name match
            var exactPathMatches = transformsWithRenderer
                .Where(t => GetRelativePathFromRoot(t, root) == targetRelativePath &&
                           t.name == objectName)
                .ToList();
            if (exactPathMatches.Count > 0)
            {
                // If multiple matches, prefer those with matching rootObjectName
                if (exactPathMatches.Count > 1 && !string.IsNullOrEmpty(sourceRootName))
                {
                    var rootNameMatches = exactPathMatches
                        .Where(t => root.name == sourceRootName)
                        .ToList();
                    if (rootNameMatches.Count > 0)
                    {
                        Debug.Log($"[MA Material Helper] MATCH: ✓ Priority 1 - Exact path + root match: '{rootNameMatches.First().name}' at '{GetFullPath(rootNameMatches.First())}'");
                        return rootNameMatches.First();
                    }
                }
                Debug.Log($"[MA Material Helper] MATCH: ✓ Priority 1 - Exact path match: '{exactPathMatches.First().name}' at '{GetFullPath(exactPathMatches.First())}'");
                return exactPathMatches.First();
            }

            // Priority 2: Same depth + exact name match
            var sameDepthExactNameMatches = transformsWithRenderer
                .Where(t =>
                {
                    string path = GetRelativePathFromRoot(t, root);
                    int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                    return depth == targetDepth && t.name == objectName;
                })
                .ToList();
            if (sameDepthExactNameMatches.Count > 0)
            {
                Debug.Log($"[MA Material Helper] MATCH: Priority 2 - Same depth ({targetDepth}) + exact name matches: {sameDepthExactNameMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (sameDepthExactNameMatches.Count > 1)
                {
                    sameDepthExactNameMatches = FilterByParentHierarchy(
                        sameDepthExactNameMatches, targetRelativePath, root);
                    Debug.Log($"[MA Material Helper] MATCH: After hierarchy filter: {sameDepthExactNameMatches.Count}");
                }
                var selected = GetBestDepthMatch(sameDepthExactNameMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[MA Material Helper] MATCH: ✓ Priority 2 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
            }

            // Priority 3: Exact name match (any depth, closest depth preferred)
            var exactNameMatches = transformsWithRenderer.Where(t => t.name == objectName).ToList();
            if (exactNameMatches.Count > 0)
            {
                Debug.Log($"[MA Material Helper] MATCH: Priority 3 - Exact name matches: {exactNameMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (exactNameMatches.Count > 1)
                {
                    exactNameMatches = FilterByParentHierarchy(
                        exactNameMatches, targetRelativePath, root);
                    Debug.Log($"[MA Material Helper] MATCH: After hierarchy filter: {exactNameMatches.Count}");
                }
                var selected = GetBestDepthMatch(exactNameMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[MA Material Helper] MATCH: ✓ Priority 3 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
            }

            // Priority 4: Case-insensitive name match
            var caseInsensitiveMatches = transformsWithRenderer
                .Where(t => string.Equals(t.name, objectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (caseInsensitiveMatches.Count > 0)
            {
                Debug.Log($"[MA Material Helper] MATCH: Priority 4 - Case-insensitive matches: {caseInsensitiveMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (caseInsensitiveMatches.Count > 1)
                {
                    caseInsensitiveMatches = FilterByParentHierarchy(
                        caseInsensitiveMatches, targetRelativePath, root);
                    Debug.Log($"[MA Material Helper] MATCH: After hierarchy filter: {caseInsensitiveMatches.Count}");
                }
                var selected = GetBestDepthMatch(caseInsensitiveMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[MA Material Helper] MATCH: ✓ Priority 4 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
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

            Debug.Log($"[MA Material Helper] MATCH: ✗ No match found for '{objectName}'");
            return null;
        }

        /// <summary>
        /// Gets the relative path from a specific root
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
        /// Gets the full hierarchy path of a transform
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

        /// <summary>
        /// Filters candidates by matching parent hierarchy from bottom to top
        /// </summary>
        private static List<Transform> FilterByParentHierarchy(
            List<Transform> candidates,
            string targetRelativePath,
            Transform rootObject)
        {
            if (candidates.Count <= 1) return candidates;

            string rootName = rootObject.name;
            string[] targetParts = string.IsNullOrEmpty(targetRelativePath)
                ? new string[0]
                : targetRelativePath.Split('/');

            // Get maximum depth among candidates
            int maxDepth = candidates.Max(c =>
            {
                string path = GetRelativePathFromRoot(c, rootObject);
                return string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
            });

            // Start from parent level (one level up from the object itself)
            for (int level = 1; level < maxDepth; level++)
            {
                var filtered = candidates.Where(t =>
                {
                    string path = GetRelativePathFromRoot(t, rootObject);
                    string[] sourceParts = string.IsNullOrEmpty(path) ? new string[0] : path.Split('/');
                    if (sourceParts.Length <= level) return false;

                    int sourceIndex = sourceParts.Length - 1 - level;
                    string sourceParent = sourceParts[sourceIndex];

                    int targetIndex = targetParts.Length - 1 - level;

                    if (targetIndex >= 0)
                    {
                        string targetParent = targetParts[targetIndex];
                        return sourceParent == targetParent;
                    }
                    else
                    {
                        return sourceParent == rootName;
                    }
                }).ToList();

                if (filtered.Count > 0)
                {
                    candidates = filtered;
                    if (candidates.Count == 1) break;
                }
                else
                {
                    break;
                }
            }

            return candidates;
        }

        /// <summary>
        /// Select best match based on hierarchy depth - prioritize same depth, then closest depth
        /// </summary>
        private static Transform GetBestDepthMatch(List<Transform> candidates, int targetDepth, Transform rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // Prefer same depth matches
            var sameDepthMatches = candidates.Where(t =>
            {
                string path = GetRelativePathFromRoot(t, rootObject);
                int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                return depth == targetDepth;
            }).ToList();

            if (sameDepthMatches.Count > 0)
            {
                // If multiple with same depth, use similarity scoring
                if (sameDepthMatches.Count > 1)
                {
                    return SelectBySimilarity(sameDepthMatches, rootObject, targetRelativePath);
                }
                return sameDepthMatches.First();
            }

            // Select by closest depth, then by similarity
            var groupedByDepth = candidates
                .GroupBy(t =>
                {
                    string path = GetRelativePathFromRoot(t, rootObject);
                    int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                    return Math.Abs(depth - targetDepth);
                })
                .OrderBy(g => g.Key)
                .First();

            var closestDepthCandidates = groupedByDepth.ToList();
            if (closestDepthCandidates.Count > 1)
            {
                return SelectBySimilarity(closestDepthCandidates, rootObject, targetRelativePath);
            }

            return closestDepthCandidates.First();
        }

        /// <summary>
        /// Selects the best candidate from multiple matches based on name similarity
        /// </summary>
        private static Transform SelectBySimilarity(List<Transform> candidates, Transform rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates.First();

            return candidates
                .OrderBy(t => LevenshteinDistance(rootObject.name, rootObject.name))  // rootObject name similarity
                .ThenBy(t => LevenshteinDistance(GetRelativePathFromRoot(t, rootObject), targetRelativePath))  // path similarity
                .First();
        }

        /// <summary>
        /// Calculates Levenshtein distance (edit distance) between two strings
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int[,] d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(
                        d[i - 1, j] + 1,      // deletion
                        d[i, j - 1] + 1),     // insertion
                        d[i - 1, j - 1] + cost); // substitution
                }
            }

            return d[s1.Length, s2.Length];
        }
    }
}
