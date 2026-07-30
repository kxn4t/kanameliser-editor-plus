using System;
using UnityEngine;

namespace Kanameliser.EditorPlus.Runtime
{
    /// <summary>
    /// Material slot remapping data for a single renderer.
    /// <para>
    /// <c>referenceSlotForHostSlot[j] = i</c> means "host slot <c>j</c> corresponds to reference
    /// (original) slot <c>i</c>". A value of <c>-1</c> marks an unresolved slot that has no
    /// corresponding reference slot.
    /// </para>
    /// Only the index mapping is persisted; the materials themselves are not stored, so editing the
    /// host materials after generation does not invalidate the mapping.
    /// </summary>
    [Serializable]
    public class RendererSlotRemap
    {
        /// <summary>
        /// Direct reference to the target renderer. This is the primary lookup key and survives
        /// object renames and moves within the hierarchy. <see cref="rendererPath"/> is kept only as
        /// a fallback (lost reference / legacy data) and for display.
        /// </summary>
        public Renderer renderer;

        /// <summary>Relative path of the renderer from the <see cref="MaterialSlotRemapping"/> transform (fallback / display).</summary>
        public string rendererPath;

        /// <summary>Renderer type name (e.g. "SkinnedMeshRenderer") used to disambiguate the lookup.</summary>
        public string rendererType;

        /// <summary>host slot index -> reference slot index (-1 = unresolved).</summary>
        public int[] referenceSlotForHostSlot;
    }
}
