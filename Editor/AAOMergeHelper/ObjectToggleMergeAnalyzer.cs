using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.Editor.AAOMergeHelper
{
    /// <summary>
    /// Selection helpers for merging renderers controlled by MA Object Toggle.
    /// Kept free of Modular Avatar types so the logic is unit-testable:
    /// callers resolve toggle components into <see cref="ToggleEntry"/> values first.
    /// </summary>
    internal static class ObjectToggleMergeAnalyzer
    {
        /// <summary>
        /// A resolved Object Toggle entry. The entry switches <see cref="Root"/> and
        /// therefore affects every renderer in its subtree.
        /// </summary>
        internal readonly struct ToggleEntry
        {
            public readonly object Toggle;
            public readonly Transform Root;
            public readonly bool SetActive;

            public ToggleEntry(object toggle, Transform root, bool setActive)
            {
                Toggle = toggle;
                Root = root;
                SetActive = setActive;
            }
        }

        /// <summary>Returns the renderers not affected by any Object Toggle entry.</summary>
        internal static List<Renderer> ExcludeToggled(
            IReadOnlyList<Renderer> renderers, IReadOnlyList<ToggleEntry> entries)
        {
            return renderers
                .Where(r => !entries.Any(e => r.transform.IsChildOf(e.Root)))
                .ToList();
        }

        /// <summary>
        /// Groups the candidate renderers of one toggle by the active state set by their
        /// governing entry — the deepest entry of that toggle containing the renderer.
        /// Renderers also affected by a different toggle belong to another visibility
        /// unit, so they are dropped.
        /// </summary>
        internal static List<(bool setActive, List<Renderer> renderers)> GroupByEntryValue(
            IReadOnlyList<Renderer> candidates, object toggle, IReadOnlyList<ToggleEntry> allEntries)
        {
            var groups = new List<(bool setActive, List<Renderer> renderers)>();

            foreach (var renderer in candidates)
            {
                var containing = allEntries
                    .Where(e => renderer.transform.IsChildOf(e.Root))
                    .ToList();
                if (containing.Any(e => !ReferenceEquals(e.Toggle, toggle))) continue;

                var governing = containing
                    .OrderByDescending(e => Depth(e.Root))
                    .FirstOrDefault();
                if (governing.Root == null) continue;

                var index = groups.FindIndex(g => g.setActive == governing.SetActive);
                if (index < 0)
                {
                    groups.Add((governing.SetActive, new List<Renderer>()));
                    index = groups.Count - 1;
                }

                groups[index].renderers.Add(renderer);
            }

            return groups;
        }

        private static int Depth(Transform transform)
        {
            var depth = 0;
            for (var current = transform.parent; current != null; current = current.parent)
                depth++;
            return depth;
        }
    }
}
