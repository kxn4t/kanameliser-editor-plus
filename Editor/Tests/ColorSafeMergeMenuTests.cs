// The menu (and the AAO MergeSkinnedMesh component it configures) only exists when
// both AAO and Modular Avatar are installed, so these tests are compiled out otherwise.
#if AVATAR_OPTIMIZER_INSTALLED && MODULAR_AVATAR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Anatawa12.AvatarOptimizer;
using Kanameliser.Editor.AAOMergeHelper;

namespace Kanameliser.EditorPlus.Tests
{
    public class ColorSafeMergeMenuTests : MatchingRegressionTestBase
    {
        private static Transform CommonParent(params Component[] components)
        {
            return ColorSafeMergeMenu.FindCommonParent(components.Select(x => x.transform).ToList());
        }

        [Test]
        public void FindCommonParent_Siblings_ReturnsParent()
        {
            var root = CreateGameObject("Root");
            var a = CreateGameObject("A", root);
            var b = CreateGameObject("B", root);

            Assert.That(CommonParent(a.transform, b.transform), Is.EqualTo(root.transform));
        }

        [Test]
        public void FindCommonParent_DifferentDepths_ReturnsSharedAncestor()
        {
            var root = CreateGameObject("Root");
            var group = CreateGameObject("Group", root);
            var a = CreateGameObject("A", group);
            var b = CreateGameObject("B", root);

            Assert.That(CommonParent(a.transform, b.transform), Is.EqualTo(root.transform));
        }

        [Test]
        public void FindCommonParent_SceneRootObjects_ReturnsNull()
        {
            var a = CreateGameObject("A");
            var b = CreateGameObject("B");

            Assert.That(CommonParent(a.transform, b.transform), Is.Null);
        }

        [Test]
        public void FindCommonParent_AncestorAndDescendant_ReturnsAncestorParent()
        {
            var root = CreateGameObject("Root");
            var a = CreateGameObject("A", root);
            var b = CreateGameObject("B", a);

            Assert.That(CommonParent(a.transform, b.transform), Is.EqualTo(root.transform));
        }

        [Test]
        public void FindCommonParent_SingleObject_ReturnsItsParent()
        {
            var root = CreateGameObject("Root");
            var a = CreateGameObject("A", root);

            Assert.That(CommonParent(a.transform), Is.EqualTo(root.transform));
        }

        [Test]
        public void CreateMergeObject_ConfiguresComponentUnderCommonParent()
        {
            var root = CreateGameObject("Root");
            var a = CreateGameObject("A", root).AddComponent<SkinnedMeshRenderer>();
            var b = CreateGameObject("B", root).AddComponent<SkinnedMeshRenderer>();
            var c = CreateGameObject("C", root).AddComponent<MeshRenderer>();

            var merged = ColorSafeMergeMenu.CreateMergeObject(
                new List<SkinnedMeshRenderer> { a, b }, new List<MeshRenderer> { c });

            Assert.That(merged, Is.Not.Null);
            Assert.That(merged.transform.parent, Is.EqualTo(root.transform));

            var merge = merged.GetComponent<MergeSkinnedMesh>();
            Assert.That(merge, Is.Not.Null);

            var serialized = new SerializedObject(merge);
            Assert.That(ReadObjectArray(serialized, "renderersSet.mainSet"), Is.EqualTo(new Object[] { a, b }));
            Assert.That(ReadObjectArray(serialized, "staticRenderersSet.mainSet"), Is.EqualTo(new Object[] { c }));
            // No avatar root in the test scene, so the Setter/Swap analysis is skipped
            Assert.That(ReadObjectArray(serialized, "doNotMergeMaterials.mainSet"), Is.Empty);
        }

        [Test]
        public void ExcludeClothRenderers_RemovesClothDrivenRenderers()
        {
            var root = CreateGameObject("Root");
            var plain = CreateGameObject("Plain", root).AddComponent<SkinnedMeshRenderer>();
            var clothObject = CreateGameObject("ClothDriven", root);
            var clothRenderer = clothObject.AddComponent<SkinnedMeshRenderer>();
            clothObject.AddComponent<Cloth>();

            var result = ColorSafeMergeMenu.ExcludeClothRenderers(
                new List<SkinnedMeshRenderer> { plain, clothRenderer });

            Assert.That(result, Is.EqualTo(new[] { plain }));
        }

        [Test]
        public void ExcludeClothRenderers_NoCloth_KeepsAll()
        {
            var root = CreateGameObject("Root");
            var a = CreateGameObject("A", root).AddComponent<SkinnedMeshRenderer>();
            var b = CreateGameObject("B", root).AddComponent<SkinnedMeshRenderer>();

            var result = ColorSafeMergeMenu.ExcludeClothRenderers(
                new List<SkinnedMeshRenderer> { a, b });

            Assert.That(result, Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void CreateMergeObject_NoRenderers_ReturnsNull()
        {
            var merged = ColorSafeMergeMenu.CreateMergeObject(
                new List<SkinnedMeshRenderer>(), new List<MeshRenderer>());

            Assert.That(merged, Is.Null);
        }

        private static List<Object> ReadObjectArray(SerializedObject serialized, string propertyPath)
        {
            var property = serialized.FindProperty(propertyPath);
            Assert.That(property, Is.Not.Null, $"Serialized property '{propertyPath}' was not found.");

            var values = new List<Object>();
            for (var i = 0; i < property.arraySize; i++)
                values.Add(property.GetArrayElementAtIndex(i).objectReferenceValue);
            return values;
        }
    }
}
#endif
