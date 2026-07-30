using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.Editor.MAMaterialHelper.MaterialSetter;
using Kanameliser.Editor.MAMaterialHelper.MaterialSwap;
using Kanameliser.EditorPlus.Runtime;
#if MODULAR_AVATAR_INSTALLED
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.EditorPlus.Tests
{
    public class MaterialSlotRemapIntegrationTests : MatchingRegressionTestBase
    {
#if MODULAR_AVATAR_INSTALLED
        [Test]
        public void CreateMaterialSetter_WithRemap_AssignsSourceToRemappedHostSlots()
        {
            var src0 = CreateMaterial("Src0");
            var src1 = CreateMaterial("Src1");
            var cur0 = CreateMaterial("Cur0");
            var cur1 = CreateMaterial("Cur1");

            var target = CreateGameObject("Avatar");
            var body = CreateGameObject("Body", target).AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { cur0, cur1 };

            var remapping = target.AddComponent<MaterialSlotRemapping>();
            remapping.remaps.Add(new RendererSlotRemap
            {
                rendererPath = "Body",
                rendererType = "MeshRenderer",
                referenceSlotForHostSlot = new[] { 1, 0 }
            });

            var copiedData = new CopiedMaterialData();
            copiedData.materialSetups.Add(new MaterialSetupData("Body", "Body", new[] { src0, src1 }, 1, target.name, "MeshRenderer"));
            MAMaterialHelperSession.StoreCopiedData(copiedData);

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var result = MaterialSetterGenerator.CreateMaterialSetter(target, copiedData, skipUnchanged: false);
                Assert.That(result.success, Is.True);

                var variation = target.transform.Find("Color Menu/Color1");
                Assert.That(variation, Is.Not.Null);
                var setter = variation.GetComponent<ModularAvatarMaterialSetter>();
                Assert.That(setter, Is.Not.Null);

                // host slot 0 <- source[1] = Src1 ; host slot 1 <- source[0] = Src0
                Assert.That(GetMaterialForIndex(setter, 0), Is.SameAs(src1));
                Assert.That(GetMaterialForIndex(setter, 1), Is.SameAs(src0));

                Undo.PerformUndo();
                undoPerformed = true;
            }
            finally
            {
                if (!undoPerformed) Undo.PerformUndo();
            }
        }

        [Test]
        public void CreateMaterialSetter_WithoutRemap_KeepsIdentitySlots()
        {
            var src0 = CreateMaterial("Src0");
            var src1 = CreateMaterial("Src1");
            var cur0 = CreateMaterial("Cur0");
            var cur1 = CreateMaterial("Cur1");

            var target = CreateGameObject("Avatar");
            var body = CreateGameObject("Body", target).AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { cur0, cur1 };

            var copiedData = new CopiedMaterialData();
            copiedData.materialSetups.Add(new MaterialSetupData("Body", "Body", new[] { src0, src1 }, 1, target.name, "MeshRenderer"));
            MAMaterialHelperSession.StoreCopiedData(copiedData);

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var result = MaterialSetterGenerator.CreateMaterialSetter(target, copiedData, skipUnchanged: false);
                Assert.That(result.success, Is.True);

                var setter = target.transform.Find("Color Menu/Color1").GetComponent<ModularAvatarMaterialSetter>();
                Assert.That(GetMaterialForIndex(setter, 0), Is.SameAs(src0));
                Assert.That(GetMaterialForIndex(setter, 1), Is.SameAs(src1));

                Undo.PerformUndo();
                undoPerformed = true;
            }
            finally
            {
                if (!undoPerformed) Undo.PerformUndo();
            }
        }

        [Test]
        public void CreateMaterialSwapPerObject_WithRemap_SwapsUseRemappedSources()
        {
            var src0 = CreateMaterial("Src0");
            var src1 = CreateMaterial("Src1");
            var cur0 = CreateMaterial("Cur0");
            var cur1 = CreateMaterial("Cur1");

            var target = CreateGameObject("Avatar");
            var body = CreateGameObject("Body", target).AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { cur0, cur1 };

            var remapping = target.AddComponent<MaterialSlotRemapping>();
            remapping.remaps.Add(new RendererSlotRemap
            {
                rendererPath = "Body",
                rendererType = "MeshRenderer",
                referenceSlotForHostSlot = new[] { 1, 0 }
            });

            var copiedData = new CopiedMaterialData();
            copiedData.materialSetups.Add(new MaterialSetupData("Body", "Body", new[] { src0, src1 }, 1, target.name, "MeshRenderer"));
            MAMaterialHelperSession.StoreCopiedData(copiedData);

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var result = MaterialSwapGenerator.CreateMaterialSwapPerObject(target, copiedData);
                Assert.That(result.success, Is.True);

                var swap = target.transform.Find("Color Menu/Color1").GetComponent<ModularAvatarMaterialSwap>();
                Assert.That(swap, Is.Not.Null);

                // host slot 0 (Cur0) -> source[1] = Src1 ; host slot 1 (Cur1) -> source[0] = Src0
                Assert.That(GetSwapTarget(swap, cur0), Is.SameAs(src1));
                Assert.That(GetSwapTarget(swap, cur1), Is.SameAs(src0));

                Undo.PerformUndo();
                undoPerformed = true;
            }
            finally
            {
                if (!undoPerformed) Undo.PerformUndo();
            }
        }

        private static Material GetMaterialForIndex(ModularAvatarMaterialSetter setter, int index)
        {
            foreach (var obj in setter.Objects)
            {
                if (obj.MaterialIndex == index) return obj.Material;
            }
            return null;
        }

        private static Material GetSwapTarget(ModularAvatarMaterialSwap swap, Material from)
        {
            foreach (var s in swap.Swaps)
            {
                if (s.From == from) return s.To;
            }
            return null;
        }
#else
        [Test, Ignore("Material slot remapping integration requires Modular Avatar.")]
        public void CreateMaterialSetter_WithRemap_AssignsSourceToRemappedHostSlots()
        {
        }
#endif
    }
}
