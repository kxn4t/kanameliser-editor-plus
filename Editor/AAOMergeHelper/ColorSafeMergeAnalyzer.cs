using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if MODULAR_AVATAR_INSTALLED
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.Editor.AAOMergeHelper
{
    /// <summary>
    /// Determines which materials must be excluded from material slot merging of
    /// AAO Merge Skinned Mesh so that color changes driven by MA Material Setter /
    /// Material Swap keep working after the merge.
    ///
    /// AAO merges slots sharing the same material into a single slot, so a material
    /// change applied to one source slot would leak into every slot merged with it.
    /// Merging a material group is safe only when every material-changing component
    /// affects all slots of the group identically (or none of them).
    /// </summary>
    internal static class ColorSafeMergeAnalyzer
    {
        /// <summary>A material slot of a merge source renderer.</summary>
        internal readonly struct SlotInfo
        {
            public readonly Renderer Renderer;
            public readonly int SlotIndex;
            public readonly Material Material;

            public SlotInfo(Renderer renderer, int slotIndex, Material material)
            {
                Renderer = renderer;
                SlotIndex = slotIndex;
                Material = material;
            }
        }

        /// <summary>
        /// Material changes of a single component: for each affected slot, the target
        /// materials in component entry order. Slots not present are unaffected.
        /// </summary>
        internal sealed class ComponentChanges : Dictionary<(Renderer renderer, int slotIndex), List<Material>>
        {
            public void Add(Renderer renderer, int slotIndex, Material target)
            {
                if (!TryGetValue((renderer, slotIndex), out var list))
                    this[(renderer, slotIndex)] = list = new List<Material>();
                list.Add(target);
            }
        }

        /// <summary>
        /// Collects materials that must not be slot-merged when merging <paramref name="renderers"/>,
        /// by scanning MA Material Setter / Material Swap components under <paramref name="avatarRoot"/>.
        /// </summary>
        public static List<Material> CollectDoNotMergeMaterials(IReadOnlyList<Renderer> renderers, GameObject avatarRoot)
        {
#if MODULAR_AVATAR_INSTALLED
            var slots = CollectSlots(renderers);
            var changes = CollectMaterialChanges(avatarRoot, new HashSet<Renderer>(renderers));
            return ComputeDoNotMergeMaterials(slots, changes);
#else
            return new List<Material>();
#endif
        }

        internal static List<SlotInfo> CollectSlots(IReadOnlyList<Renderer> renderers)
        {
            var slots = new List<SlotInfo>();
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null)
                        slots.Add(new SlotInfo(renderer, i, materials[i]));
            }
            return slots;
        }

        /// <summary>
        /// Returns the materials whose slots are changed non-uniformly by at least one component.
        /// Groups with a single slot are always mergeable: they have no same-material partner,
        /// so excluding them would not change the result.
        /// </summary>
        internal static List<Material> ComputeDoNotMergeMaterials(
            List<SlotInfo> slots, List<ComponentChanges> changesPerComponent)
        {
            var result = new List<Material>();
            foreach (var group in slots.GroupBy(x => x.Material))
            {
                var groupSlots = group.ToList();
                if (groupSlots.Count < 2) continue;

                if (changesPerComponent.Any(changes => !IsUniform(changes, groupSlots)))
                    result.Add(group.Key);
            }
            return result;
        }

        private static bool IsUniform(ComponentChanges changes, List<SlotInfo> groupSlots)
        {
            changes.TryGetValue((groupSlots[0].Renderer, groupSlots[0].SlotIndex), out var first);
            for (var i = 1; i < groupSlots.Count; i++)
            {
                changes.TryGetValue((groupSlots[i].Renderer, groupSlots[i].SlotIndex), out var other);
                if ((first == null) != (other == null)) return false;
                if (first != null && !first.SequenceEqual(other)) return false;
            }
            return true;
        }

#if MODULAR_AVATAR_INSTALLED
        /// <summary>
        /// Resolves MA Material Setter / Material Swap components into per-slot changes,
        /// mirroring how MA registers reactions at build time (ReactiveObjectAnalyzer).
        /// Components on inactive objects are included: menu toggles can activate them.
        /// </summary>
        internal static List<ComponentChanges> CollectMaterialChanges(
            GameObject avatarRoot, HashSet<Renderer> mergeTargets)
        {
            var result = new List<ComponentChanges>();

            foreach (var setter in avatarRoot.GetComponentsInChildren<ModularAvatarMaterialSetter>(true))
            {
                if (setter.Objects == null) continue;

                var changes = new ComponentChanges();
                foreach (var entry in setter.Objects)
                {
                    var target = entry.Object?.Get(setter);
                    if (target == null) continue;
                    if (!target.TryGetComponent<Renderer>(out var renderer)) continue;
                    if (!mergeTargets.Contains(renderer)) continue;
                    // MA skips out-of-range slots at build time as well
                    if (entry.MaterialIndex < 0 || entry.MaterialIndex >= renderer.sharedMaterials.Length) continue;

                    changes.Add(renderer, entry.MaterialIndex, entry.Material);
                }
                if (changes.Count > 0) result.Add(changes);
            }

            foreach (var swap in avatarRoot.GetComponentsInChildren<ModularAvatarMaterialSwap>(true))
            {
                if (swap.Swaps == null || swap.Swaps.Count == 0) continue;
                // A null root means the swap applies to the whole avatar
                var root = swap.Root?.Get(swap);

                var changes = new ComponentChanges();
                foreach (var renderer in mergeTargets)
                {
                    if (root != null && !renderer.transform.IsChildOf(root.transform)) continue;

                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null) continue;
                        foreach (var matSwap in swap.Swaps)
                            if (matSwap.From == materials[i])
                                changes.Add(renderer, i, matSwap.To);
                    }
                }
                if (changes.Count > 0) result.Add(changes);
            }

            return result;
        }
#endif
    }
}
