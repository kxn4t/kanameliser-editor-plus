using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Kanameliser.Editor.AAOMergeHelper;

namespace Kanameliser.EditorPlus.Tests
{
    public class ObjectToggleMergeAnalyzerTests : MatchingRegressionTestBase
    {
        private SkinnedMeshRenderer CreateRenderer(string name, GameObject parent = null)
        {
            return CreateGameObject(name, parent).AddComponent<SkinnedMeshRenderer>();
        }

        private static ObjectToggleMergeAnalyzer.ToggleEntry Entry(object toggle, GameObject root, bool setActive)
        {
            return new ObjectToggleMergeAnalyzer.ToggleEntry(toggle, root.transform, setActive);
        }

        [Test]
        public void ExcludeToggled_RemovesTargetsAndDescendants()
        {
            var toggle = new object();
            var bag = CreateGameObject("Bag");
            var bagMesh = CreateRenderer("BagMesh", bag);
            var charm = CreateRenderer("Charm", bag);
            var body = CreateRenderer("Body");

            var result = ObjectToggleMergeAnalyzer.ExcludeToggled(
                new List<Renderer> { bagMesh, charm, body },
                new[] { Entry(toggle, bag, false) });

            Assert.That(result, Is.EqualTo(new Renderer[] { body }));
        }

        [Test]
        public void ExcludeToggled_TargetItselfIsExcluded()
        {
            var toggle = new object();
            var bagMesh = CreateRenderer("BagMesh");
            var body = CreateRenderer("Body");

            var result = ObjectToggleMergeAnalyzer.ExcludeToggled(
                new List<Renderer> { bagMesh, body },
                new[] { Entry(toggle, bagMesh.gameObject, false) });

            Assert.That(result, Is.EqualTo(new Renderer[] { body }));
        }

        [Test]
        public void ExcludeToggled_NoEntries_ReturnsAll()
        {
            var a = CreateRenderer("A");
            var b = CreateRenderer("B");

            var result = ObjectToggleMergeAnalyzer.ExcludeToggled(
                new List<Renderer> { a, b },
                new ObjectToggleMergeAnalyzer.ToggleEntry[0]);

            Assert.That(result, Is.EqualTo(new Renderer[] { a, b }));
        }

        [Test]
        public void GroupByEntryValue_SameValue_SingleGroup()
        {
            var toggle = new object();
            var bagMesh = CreateRenderer("BagMesh");
            var charm = CreateRenderer("Charm");
            var entries = new[]
            {
                Entry(toggle, bagMesh.gameObject, false),
                Entry(toggle, charm.gameObject, false),
            };

            var groups = ObjectToggleMergeAnalyzer.GroupByEntryValue(
                new List<Renderer> { bagMesh, charm }, toggle, entries);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].setActive, Is.False);
            Assert.That(groups[0].renderers, Is.EqualTo(new Renderer[] { bagMesh, charm }));
        }

        [Test]
        public void GroupByEntryValue_MixedValues_TwoGroups()
        {
            var toggle = new object();
            var shown = CreateRenderer("Shown");
            var hidden = CreateRenderer("Hidden");
            var entries = new[]
            {
                Entry(toggle, shown.gameObject, true),
                Entry(toggle, hidden.gameObject, false),
            };

            var groups = ObjectToggleMergeAnalyzer.GroupByEntryValue(
                new List<Renderer> { shown, hidden }, toggle, entries);

            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].setActive, Is.True);
            Assert.That(groups[0].renderers, Is.EqualTo(new Renderer[] { shown }));
            Assert.That(groups[1].setActive, Is.False);
            Assert.That(groups[1].renderers, Is.EqualTo(new Renderer[] { hidden }));
        }

        [Test]
        public void GroupByEntryValue_TargetOfAnotherToggle_IsDropped()
        {
            var toggle = new object();
            var otherToggle = new object();
            var bag = CreateGameObject("Bag");
            var bagMesh = CreateRenderer("BagMesh", bag);
            var charm = CreateRenderer("Charm", bag);
            var entries = new[]
            {
                Entry(toggle, bag, false),
                Entry(otherToggle, charm.gameObject, false),
            };

            var groups = ObjectToggleMergeAnalyzer.GroupByEntryValue(
                new List<Renderer> { bagMesh, charm }, toggle, entries);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].renderers, Is.EqualTo(new Renderer[] { bagMesh }));
        }

        [Test]
        public void GroupByEntryValue_NestedEntries_DeepestGoverns()
        {
            var toggle = new object();
            var bag = CreateGameObject("Bag");
            var bagMesh = CreateRenderer("BagMesh", bag);
            var charm = CreateRenderer("Charm", bag);
            var entries = new[]
            {
                Entry(toggle, bag, true),
                Entry(toggle, charm.gameObject, false),
            };

            var groups = ObjectToggleMergeAnalyzer.GroupByEntryValue(
                new List<Renderer> { bagMesh, charm }, toggle, entries);

            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].setActive, Is.True);
            Assert.That(groups[0].renderers, Is.EqualTo(new Renderer[] { bagMesh }));
            Assert.That(groups[1].setActive, Is.False);
            Assert.That(groups[1].renderers, Is.EqualTo(new Renderer[] { charm }));
        }

        [Test]
        public void GroupByEntryValue_RendererOutsideEntries_IsIgnored()
        {
            var toggle = new object();
            var bagMesh = CreateRenderer("BagMesh");
            var outsider = CreateRenderer("Outsider");
            var entries = new[] { Entry(toggle, bagMesh.gameObject, false) };

            var groups = ObjectToggleMergeAnalyzer.GroupByEntryValue(
                new List<Renderer> { bagMesh, outsider }, toggle, entries);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].renderers, Is.EqualTo(new Renderer[] { bagMesh }));
        }
    }
}
