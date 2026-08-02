using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Kanameliser.Editor.AAOMergeHelper;

namespace Kanameliser.EditorPlus.Tests
{
    public class ColorSafeMergeAnalyzerTests : MatchingRegressionTestBase
    {
        private SkinnedMeshRenderer CreateRenderer(string name, params Material[] materials)
        {
            var renderer = CreateGameObject(name).AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        private static ColorSafeMergeAnalyzer.ComponentChanges Changes(
            params (Renderer renderer, int slotIndex, Material target)[] entries)
        {
            var changes = new ColorSafeMergeAnalyzer.ComponentChanges();
            foreach (var (renderer, slotIndex, target) in entries)
                changes.Add(renderer, slotIndex, target);
            return changes;
        }

        private static List<Material> Analyze(
            List<Renderer> renderers, params ColorSafeMergeAnalyzer.ComponentChanges[] changes)
        {
            return ColorSafeMergeAnalyzer.ComputeDoNotMergeMaterials(
                ColorSafeMergeAnalyzer.CollectSlots(renderers),
                new List<ColorSafeMergeAnalyzer.ComponentChanges>(changes));
        }

        [Test]
        public void DifferentTargetsPerRenderer_ExcludesSharedMaterialOnly()
        {
            var gray = CreateMaterial("Gray");
            var gem = CreateMaterial("Gem");
            var white = CreateMaterial("White");
            var blue = CreateMaterial("Blue");
            var meshA = CreateRenderer("MeshA", gray, gem);
            var meshB = CreateRenderer("MeshB", gray, gem);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white)),
                Changes((meshB, 0, blue)));

            Assert.That(result, Is.EquivalentTo(new[] { gray }));
        }

        [Test]
        public void SameChangeAcrossAllSlots_SingleComponent_NoExclusion()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white), (meshB, 0, white)));

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void PartialChange_SingleComponent_ExcludesSharedMaterial()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white)));

            Assert.That(result, Is.EquivalentTo(new[] { gray }));
        }

        [Test]
        public void SameComponent_DifferentTargets_ExcludesSharedMaterial()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var blue = CreateMaterial("Blue");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white), (meshB, 0, blue)));

            Assert.That(result, Is.EquivalentTo(new[] { gray }));
        }

        [Test]
        public void ChangeOnUniqueMaterial_NoExclusion()
        {
            var unique = CreateMaterial("Unique");
            var gem = CreateMaterial("Gem");
            var white = CreateMaterial("White");
            var meshA = CreateRenderer("MeshA", unique, gem);
            var meshB = CreateRenderer("MeshB", gem);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white)));

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void PartialChangeWithinSingleRenderer_ExcludesSharedMaterial()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var meshA = CreateRenderer("MeshA", gray, gray);

            var result = Analyze(
                new List<Renderer> { meshA },
                Changes((meshA, 0, white)));

            Assert.That(result, Is.EquivalentTo(new[] { gray }));
        }

        [Test]
        public void TwoUniformComponents_NoExclusion()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var blue = CreateMaterial("Blue");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white), (meshB, 0, white)),
                Changes((meshA, 0, blue), (meshB, 0, blue)));

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void NoChanges_NoExclusion()
        {
            var gray = CreateMaterial("Gray");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(new List<Renderer> { meshA, meshB });

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void CollectSlots_SkipsEmptySlots()
        {
            var gray = CreateMaterial("Gray");
            var meshA = CreateRenderer("MeshA", gray, null);

            var slots = ColorSafeMergeAnalyzer.CollectSlots(new List<Renderer> { meshA });

            Assert.That(slots, Has.Count.EqualTo(1));
            Assert.That(slots[0].SlotIndex, Is.EqualTo(0));
            Assert.That(slots[0].Material, Is.EqualTo(gray));
        }

        [Test]
        public void MultipleEntriesOnSameSlot_SameSequence_NoExclusion()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var blue = CreateMaterial("Blue");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white), (meshA, 0, blue), (meshB, 0, white), (meshB, 0, blue)));

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void MultipleEntriesOnSameSlot_DifferentOrder_ExcludesSharedMaterial()
        {
            var gray = CreateMaterial("Gray");
            var white = CreateMaterial("White");
            var blue = CreateMaterial("Blue");
            var meshA = CreateRenderer("MeshA", gray);
            var meshB = CreateRenderer("MeshB", gray);

            var result = Analyze(
                new List<Renderer> { meshA, meshB },
                Changes((meshA, 0, white), (meshA, 0, blue), (meshB, 0, blue), (meshB, 0, white)));

            Assert.That(result, Is.EquivalentTo(new[] { gray }));
        }
    }
}
