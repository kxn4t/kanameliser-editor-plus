using NUnit.Framework;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.Editor.MAMaterialHelper.SlotRemapping;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.EditorPlus.Tests
{
    public class MaterialSlotRemapGeneratorTests : MatchingRegressionTestBase
    {
        [Test]
        public void BuildSlotMap_ReversedOrder_ProducesPermutation()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var map = MaterialSlotRemapGenerator.BuildSlotMap(
                new[] { b, a }, new[] { a, b }, out var unresolved);

            Assert.That(map, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void BuildSlotMap_DuplicateMaterials_ConsumesEachReferenceSlotOnce()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var map = MaterialSlotRemapGenerator.BuildSlotMap(
                new[] { a, a, b }, new[] { b, a, a }, out var unresolved);

            Assert.That(map, Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void BuildSlotMap_SameOrder_IsIdentity()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var map = MaterialSlotRemapGenerator.BuildSlotMap(
                new[] { a, b }, new[] { a, b }, out var unresolved);

            Assert.That(map, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void BuildSlotMap_MissingMaterial_MapsToMinusOne()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");
            var c = CreateMaterial("C");

            var map = MaterialSlotRemapGenerator.BuildSlotMap(
                new[] { a, c }, new[] { a, b }, out var unresolved);

            Assert.That(map, Is.EqualTo(new[] { 0, -1 }));
            Assert.That(unresolved, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Generate_ReorderedSlots_BuildsRemap()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var host = CreateGameObject("Host");
            var hostBody = CreateGameObject("Body", host).AddComponent<MeshRenderer>();
            hostBody.sharedMaterials = new[] { b, a };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new[] { a, b };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);

            Assert.That(result.success, Is.True);
            Assert.That(result.remaps, Has.Count.EqualTo(1));
            Assert.That(result.remaps[0].rendererPath, Is.EqualTo("Body"));
            Assert.That(result.remaps[0].referenceSlotForHostSlot, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Generate_DuplicateMaterials_MarksEstimatedMappingAmbiguous()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var host = CreateGameObject("Host");
            var hostBody = CreateGameObject("Body", host).AddComponent<MeshRenderer>();
            hostBody.sharedMaterials = new[] { a, a, b };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new[] { b, a, a };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);

            Assert.That(result.success, Is.True);
            Assert.That(result.hasAmbiguousMappings, Is.True);
            Assert.That(result.remaps[0].referenceSlotForHostSlot, Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(result.ambiguousMappingDetails, Has.Count.EqualTo(1));
            Assert.That(result.ambiguousMappings, Has.Count.EqualTo(1));
            var ambiguity = result.ambiguousMappings[0];
            Assert.That(ambiguity.hostSlots, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(ambiguity.referenceSlots, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ambiguity.estimatedReferenceSlots, Is.EqualTo(new[] { 1, 2 }));
            StringAssert.Contains(
                result.ambiguousMappingDetails[0],
                string.Join("\n", result.warnings));
            StringAssert.Contains(
                result.ambiguousMappingDetails[0],
                MaterialSlotRemappingEditor.BuildAmbiguityConfirmationMessage(result));
        }

        [Test]
        public void Generate_MultipleEmptySlots_MarksEstimatedMappingAmbiguous()
        {
            var a = CreateMaterial("A");

            var host = CreateGameObject("Host");
            var hostBody = CreateGameObject("Body", host).AddComponent<MeshRenderer>();
            hostBody.sharedMaterials = new Material[] { null, null, a };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new Material[] { a, null, null };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);

            Assert.That(result.success, Is.True);
            Assert.That(result.hasAmbiguousMappings, Is.True);
            Assert.That(result.remaps[0].referenceSlotForHostSlot, Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(result.ambiguousMappings, Has.Count.EqualTo(1));
            Assert.That(result.ambiguousMappings[0].material, Is.Null);
        }

        [Test]
        public void Generate_SingleEmptySlot_DoesNotMarkMappingAmbiguous()
        {
            var a = CreateMaterial("A");

            var host = CreateGameObject("Host");
            var hostBody = CreateGameObject("Body", host).AddComponent<MeshRenderer>();
            hostBody.sharedMaterials = new Material[] { null, a };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new Material[] { a, null };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);

            Assert.That(result.success, Is.True);
            Assert.That(result.hasAmbiguousMappings, Is.False);
            Assert.That(result.remaps[0].referenceSlotForHostSlot, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Generate_SlotCountMismatch_FailsWithoutRemaps()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");
            var c = CreateMaterial("C");

            var host = CreateGameObject("Host");
            var hostBody = CreateGameObject("Body", host).AddComponent<MeshRenderer>();
            hostBody.sharedMaterials = new[] { a, b, c };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new[] { a, b };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);

            Assert.That(result.success, Is.False);
            Assert.That(result.errors, Is.Not.Empty);
            Assert.That(result.remaps, Is.Empty);
        }

        [Test]
        public void ResolveSlotMap_FollowsRendererReferenceAfterRename()
        {
            var a = CreateMaterial("A");
            var b = CreateMaterial("B");

            var host = CreateGameObject("Host");
            var bodyGo = CreateGameObject("Body", host);
            var body = bodyGo.AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { b, a };
            var component = host.AddComponent<MaterialSlotRemapping>();

            var reference = CreateGameObject("Reference");
            var refBody = CreateGameObject("Body", reference).AddComponent<MeshRenderer>();
            refBody.sharedMaterials = new[] { a, b };
            component.referencePrefab = reference;

            var result = MaterialSlotRemapGenerator.Generate(component);
            Assert.That(result.success, Is.True);
            component.remaps = result.remaps;

            // Rename the renderer object after the mapping was generated.
            bodyGo.name = "Body_Renamed";

            var map = MaterialSlotRemapUtil.ResolveSlotMap(body.transform);
            Assert.That(map, Is.EqualTo(new[] { 1, 0 }));
        }
    }
}
