using System.Collections.Generic;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.EditorPlus;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.Editor.MAMaterialHelper.SlotRemapping
{
    /// <summary>
    /// Structured description of one ambiguous material group on a renderer.
    /// </summary>
    public class AmbiguousSlotMappingInfo
    {
        public string rendererPath;
        public Material material;
        public List<int> hostSlots = new List<int>();
        public List<int> referenceSlots = new List<int>();
        /// <summary>Estimated reference slot per entry of <see cref="hostSlots"/> (-1 = none).</summary>
        public List<int> estimatedReferenceSlots = new List<int>();
    }

    /// <summary>
    /// Result of a <see cref="MaterialSlotRemapGenerator.Generate"/> call.
    /// </summary>
    public class RemapGenerationResult
    {
        public bool success;
        public List<RendererSlotRemap> remaps = new List<RendererSlotRemap>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> ambiguousMappingDetails = new List<string>();
        public List<AmbiguousSlotMappingInfo> ambiguousMappings = new List<AmbiguousSlotMappingInfo>();
        public int matchedRendererCount;
        public bool hasAmbiguousMappings;
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
                result.errors.Add(Localization.S("slotRemap.error.noComponent"));
                return result;
            }
            if (component.referencePrefab == null)
            {
                result.errors.Add(Localization.S("slotRemap.error.referenceNotSet"));
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
                    result.warnings.Add(Localization.S("slotRemap.warning.noMatchingRenderer", DisplayPath(relPath)));
                    continue;
                }

                var refRenderer = refT.GetComponent<Renderer>();
                if (refRenderer == null) continue;
                var refMaterials = refRenderer.sharedMaterials ?? new Material[0];

                if (hostMaterials.Length != refMaterials.Length)
                {
                    result.errors.Add(Localization.S(
                        "slotRemap.error.slotCountMismatch",
                        DisplayPath(relPath), hostMaterials.Length, refMaterials.Length));
                    continue;
                }

                var map = BuildSlotMap(hostMaterials, refMaterials, out var unresolved);
                foreach (var ambiguity in FindAmbiguousMaterialGroups(hostMaterials, refMaterials))
                {
                    var estimates = new List<string>();
                    var estimatedSlots = new List<int>();
                    foreach (int hostSlot in ambiguity.hostSlots)
                    {
                        int referenceSlot = map[hostSlot];
                        estimatedSlots.Add(referenceSlot);
                        estimates.Add($"{hostSlot} -> {(referenceSlot >= 0 ? referenceSlot.ToString() : "none")}");
                    }

                    string materialName = ambiguity.material == null
                        ? Localization.S("slotRemap.materialNone")
                        : $"'{ambiguity.material.name}'";
                    string detail = Localization.S(
                        "slotRemap.warning.ambiguousDetail",
                        DisplayPath(relPath),
                        materialName,
                        string.Join(", ", ambiguity.hostSlots),
                        string.Join(", ", ambiguity.referenceSlots),
                        string.Join(", ", estimates));
                    result.warnings.Add(Localization.S("slotRemap.warning.ambiguous", detail));
                    result.ambiguousMappingDetails.Add(detail);
                    result.ambiguousMappings.Add(new AmbiguousSlotMappingInfo
                    {
                        rendererPath = relPath,
                        material = ambiguity.material,
                        hostSlots = new List<int>(ambiguity.hostSlots),
                        referenceSlots = new List<int>(ambiguity.referenceSlots),
                        estimatedReferenceSlots = estimatedSlots,
                    });
                    result.hasAmbiguousMappings = true;
                }

                if (unresolved.Count > 0)
                {
                    result.warnings.Add(Localization.S(
                        "slotRemap.warning.unresolvedSlots",
                        DisplayPath(relPath), string.Join(", ", unresolved)));
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
                result.errors.Add(Localization.S("slotRemap.error.noMatchingRenderers"));
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

        private sealed class MaterialOccurrenceGroup
        {
            public Material material;
            public readonly List<int> hostSlots = new List<int>();
            public readonly List<int> referenceSlots = new List<int>();
        }

        private static List<MaterialOccurrenceGroup> FindAmbiguousMaterialGroups(
            Material[] hostMaterials,
            Material[] referenceMaterials)
        {
            var groups = new List<MaterialOccurrenceGroup>();

            for (int i = 0; i < hostMaterials.Length; i++)
            {
                FindOrCreateGroup(groups, hostMaterials[i]).hostSlots.Add(i);
            }

            for (int i = 0; i < referenceMaterials.Length; i++)
            {
                FindOrCreateGroup(groups, referenceMaterials[i]).referenceSlots.Add(i);
            }

            groups.RemoveAll(group =>
                group.hostSlots.Count == 0 ||
                group.referenceSlots.Count == 0 ||
                (group.hostSlots.Count == 1 && group.referenceSlots.Count == 1));
            return groups;
        }

        private static MaterialOccurrenceGroup FindOrCreateGroup(
            List<MaterialOccurrenceGroup> groups,
            Material material)
        {
            foreach (var group in groups)
            {
                if (group.material == material) return group;
            }

            var created = new MaterialOccurrenceGroup { material = material };
            groups.Add(created);
            return created;
        }

        private static string DisplayPath(string relPath)
        {
            return string.IsNullOrEmpty(relPath) ? "(root)" : relPath;
        }
    }
}
