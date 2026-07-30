using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.Runtime
{
    /// <summary>
    /// Editor-only metadata that records how each renderer's material slots in this (converted)
    /// outfit map back to the slots of a reference (original) outfit.
    /// <para>
    /// Outfit conversion tools can reorder material slots within a renderer. MA Material Helper's
    /// Setter/Swap generation is slot-index based, so a reordered target ends up with colors on the
    /// wrong slots. This component stores a per-renderer slot mapping (generated once against a
    /// reference prefab) that the generation step consults to place materials on the correct slots.
    /// </para>
    /// The component does nothing at runtime and is removed during avatar build (NDMF pass, plus the
    /// VRChat SDK strips non-whitelisted components as a backstop).
    /// </summary>
    [DisallowMultipleComponent]
    public class MaterialSlotRemapping : MonoBehaviour
    {
        /// <summary>
        /// The reference (original, pre-conversion) outfit. Used only to (re)generate the mapping;
        /// not required by Setter/Swap generation once the mapping exists.
        /// </summary>
        public GameObject referencePrefab;

        /// <summary>Per-renderer slot mappings under this object.</summary>
        public List<RendererSlotRemap> remaps = new List<RendererSlotRemap>();
    }
}
