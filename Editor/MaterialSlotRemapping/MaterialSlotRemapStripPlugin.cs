#if NDMF_INSTALLED
using nadena.dev.ndmf;
using UnityEngine;
using Kanameliser.EditorPlus.Runtime;

[assembly: ExportsPlugin(typeof(Kanameliser.Editor.MAMaterialHelper.SlotRemapping.MaterialSlotRemapStripPlugin))]

namespace Kanameliser.Editor.MAMaterialHelper.SlotRemapping
{
    /// <summary>
    /// Removes the editor-only <see cref="MaterialSlotRemapping"/> components during avatar build.
    /// <para>
    /// The mapping is consumed at edit time by Material Setter/Swap generation, so the component has
    /// no runtime purpose. This pass is gated on NDMF; when NDMF is absent the whole feature is
    /// unavailable (it requires Modular Avatar, which depends on NDMF), so nothing needs to run.
    /// </para>
    /// </summary>
    internal class MaterialSlotRemapStripPlugin : Plugin<MaterialSlotRemapStripPlugin>
    {
        public override string QualifiedName => "net.kanameliser.editor-plus.material-slot-remap-strip";
        public override string DisplayName => "Kanameliser Editor Plus - Material Slot Remapping";

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).Run("Remove MaterialSlotRemapping components", ctx =>
            {
                foreach (var component in ctx.AvatarRootObject.GetComponentsInChildren<MaterialSlotRemapping>(true))
                {
                    Object.DestroyImmediate(component);
                }
            });
        }
    }
}
#endif
