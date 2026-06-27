using UnityEngine;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    /// <summary>
    /// Resolves <see cref="MaterialSlotRemapping"/> data during Material Setter/Swap generation.
    /// </summary>
    public static class MaterialSlotRemapUtil
    {
        /// <summary>
        /// Returns the host-slot -> reference-slot index map for a matched target renderer, or
        /// <c>null</c> when no remapping applies (identity / current behavior). <c>null</c> is also
        /// returned when the renderer is not covered by a mapping or the stored mapping is stale
        /// (its length no longer matches the renderer's slot count).
        /// </summary>
        public static int[] ResolveSlotMap(Transform matchedTarget)
        {
            if (matchedTarget == null) return null;

            var component = matchedTarget.GetComponentInParent<MaterialSlotRemapping>(true);
            if (component == null || component.remaps == null) return null;

            var renderer = matchedTarget.GetComponent<Renderer>();
            if (renderer == null) return null;

            string relPath = ObjectMatcher.GetRelativePathFromRoot(matchedTarget, component.transform);
            string rendererType = renderer.GetType().Name;

            RendererSlotRemap match = null;
            foreach (var remap in component.remaps)
            {
                if (remap == null || remap.rendererPath != relPath) continue;
                if (!string.IsNullOrEmpty(remap.rendererType) &&
                    !string.IsNullOrEmpty(rendererType) &&
                    remap.rendererType != rendererType)
                {
                    continue;
                }
                match = remap;
                break;
            }

            if (match == null || match.referenceSlotForHostSlot == null) return null;

            int slotCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
            if (match.referenceSlotForHostSlot.Length != slotCount)
            {
                Debug.LogWarning(
                    $"[MA Material Helper] Slot remapping for '{relPath}' is stale " +
                    $"(stored {match.referenceSlotForHostSlot.Length} slots, renderer has {slotCount}); " +
                    "ignoring remap for this renderer. Please re-generate the mapping.",
                    component);
                return null;
            }

            return match.referenceSlotForHostSlot;
        }
    }
}
