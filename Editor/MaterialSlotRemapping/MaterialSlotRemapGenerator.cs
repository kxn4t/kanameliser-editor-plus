using System.Collections.Generic;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.Editor.MAMaterialHelper.SlotRemapping
{
    /// <summary>
    /// Result of a <see cref="MaterialSlotRemapGenerator.Generate"/> call.
    /// </summary>
    public class RemapGenerationResult
    {
        public bool success;
        public List<RendererSlotRemap> remaps = new List<RendererSlotRemap>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public int matchedRendererCount;
    }

    /// <summary>
    /// Generates per-renderer material slot mappings between a host (converted) outfit and a
    /// reference (original) prefab, using material asset identity to pair slots.
    /// </summary>
    public static class MaterialSlotRemapGenerator
    {
        /// <summary>
        /// Builds the slot mapping for every renderer under the component, validating that matched
        /// renderers have equal slot counts. Slot-count mismatch is a hard failure (nothing saved).
        /// </summary>
        public static RemapGenerationResult Generate(MaterialSlotRemapping component)
        {
            var result = new RemapGenerationResult();

            if (component == null)
            {
                result.errors.Add("No MaterialSlotRemapping component.");
                return result;
            }
            if (component.referencePrefab == null)
            {
                result.errors.Add("Reference prefab is not set.");
                return result;
            }

            var hostRoot = component.transform;
            var refRoot = component.referencePrefab.transform;

            var hostRenderers = hostRoot.GetComponentsInChildren<Renderer>(true);
            var matchedRefTargets = new HashSet<Transform>();
            var remaps = new List<RendererSlotRemap>();

            foreach (var hostRenderer in hostRenderers)
            {
                var hostMaterials = hostRenderer.sharedMaterials;
                if (hostMaterials == null || hostMaterials.Length == 0) continue;

                string relPath = ObjectMatcher.GetRelativePathFromRoot(hostRenderer.transform, hostRoot);
                int depth = string.IsNullOrEmpty(relPath) ? 0 : relPath.Split('/').Length;
                string rendererType = hostRenderer.GetType().Name;

                var refT = ObjectMatcher.FindMatchingObject(
                    refRoot, hostRenderer.name, relPath, depth, hostRoot.name, matchedRefTargets, rendererType);

                if (refT == null)
                {
                    result.warnings.Add($"No matching renderer in reference for '{DisplayPath(relPath)}'.");
                    continue;
                }

                var refRenderer = refT.GetComponent<Renderer>();
                if (refRenderer == null) continue;
                var refMaterials = refRenderer.sharedMaterials ?? new Material[0];

                if (hostMaterials.Length != refMaterials.Length)
                {
                    result.errors.Add(
                        $"Slot count mismatch at '{DisplayPath(relPath)}': host {hostMaterials.Length} vs reference {refMaterials.Length}.");
                    continue;
                }

                var map = BuildSlotMap(hostMaterials, refMaterials, out var unresolved);
                if (unresolved.Count > 0)
                {
                    result.warnings.Add(
                        $"Unresolved slot(s) at '{DisplayPath(relPath)}': [{string.Join(", ", unresolved)}] - no matching material in reference. Fix manually.");
                }

                remaps.Add(new RendererSlotRemap
                {
                    renderer = hostRenderer,
                    rendererPath = relPath,
                    rendererType = rendererType,
                    referenceSlotForHostSlot = map
                });
                result.matchedRendererCount++;
            }

            // Slot-count mismatch on any renderer blocks the whole generation.
            if (result.errors.Count > 0)
            {
                result.success = false;
                return result;
            }

            if (remaps.Count == 0)
            {
                result.errors.Add("No matching renderers found between host and reference.");
                result.success = false;
                return result;
            }

            result.remaps = remaps;
            result.success = true;
            return result;
        }

        /// <summary>
        /// Builds a host-slot -> reference-slot index map by material asset identity (greedy 1:1).
        /// Each reference slot is consumed at most once so duplicate materials still produce a valid
        /// permutation. Host slots with no matching reference material are mapped to -1 (unresolved).
        /// </summary>
        public static int[] BuildSlotMap(Material[] hostMaterials, Material[] referenceMaterials, out List<int> unresolved)
        {
            unresolved = new List<int>();
            int n = hostMaterials.Length;
            var map = new int[n];
            var refUsed = new bool[referenceMaterials.Length];

            for (int j = 0; j < n; j++)
            {
                int found = -1;
                for (int i = 0; i < referenceMaterials.Length; i++)
                {
                    if (refUsed[i]) continue;
                    if (referenceMaterials[i] == hostMaterials[j])
                    {
                        found = i;
                        break;
                    }
                }

                if (found >= 0)
                {
                    map[j] = found;
                    refUsed[found] = true;
                }
                else
                {
                    map[j] = -1;
                    unresolved.Add(j);
                }
            }

            return map;
        }

        private static string DisplayPath(string relPath)
        {
            return string.IsNullOrEmpty(relPath) ? "(root)" : relPath;
        }
    }
}
