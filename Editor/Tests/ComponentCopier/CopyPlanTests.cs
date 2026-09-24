using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.TestTools;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// LODGroup (holds Renderer references) and ParentConstraint (holds Transform references) stand in for
    /// PhysBone / collider style components so the tests run without the VRChat SDK.
    /// </summary>
    public class CopyPlanTests : ComponentCopierTestBase
    {
        private static LODGroup AddLodGroup(Transform host, Renderer renderer)
        {
            var lodGroup = host.gameObject.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[] { new LOD(0.5f, new[] { renderer }) });
            return lodGroup;
        }

        private static ParentConstraint AddParentConstraint(Transform host, Transform constraintSource)
        {
            var constraint = host.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = constraintSource, weight = 1f });
            return constraint;
        }

        [Test]
        public void TransformReference_IsRedirectedToTargetHierarchy()
        {
            var source = CreateHierarchy("Source", "Armature/Bone", "Item");
            var target = CreateHierarchy("Target", "Armature/Bone", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Armature/Bone"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            CopyExecutor.Execute(plan);

            var copied = target.Find("Item").GetComponent<ParentConstraint>();
            Assert.IsNotNull(copied);
            Assert.AreSame(target.Find("Armature/Bone"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ReferenceBetweenCopiedComponents_PointsToTheNewComponent()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            var sourceRenderer = source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            AddLodGroup(source, sourceRenderer);

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup), typeof(MeshRenderer));
            Assert.AreEqual(ReferenceKind.NewComponent,
                plan.Components.Single(c => c.Entry.Type == typeof(LODGroup)).References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copiedRenderer = target.Find("Body").GetComponent<MeshRenderer>();
            Assert.IsNotNull(copiedRenderer);
            Assert.AreSame(copiedRenderer, target.GetComponent<LODGroup>().GetLODs()[0].renderers[0]);
        }

        [Test]
        public void ReferenceToUnselectedComponent_UsesExistingCounterpart()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());
            var targetRenderer = target.Find("Body").gameObject.AddComponent<MeshRenderer>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            CopyExecutor.Execute(plan);

            Assert.AreSame(targetRenderer, target.GetComponent<LODGroup>().GetLODs()[0].renderers[0]);
        }

        [Test]
        public void UnresolvedReference_IsClearedAndReported()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            var reference = plan.Components.Single().References.Single();
            Assert.AreEqual(ReferenceKind.InternalUnresolved, reference.Kind);
            Assert.AreEqual(ComponentKey.For(source.Find("Body"), source, typeof(MeshRenderer), 0),
                reference.MissingDependency);

            CopyExecutor.Execute(plan);

            Assert.IsNull(target.GetComponent<LODGroup>().GetLODs()[0].renderers[0],
                "A reference must never be left pointing into the source hierarchy");

            var diff = CopyVerifier.Verify(plan).Components.Single();
            Assert.AreEqual(DiffKind.UnresolvedReference, diff.Kind);
        }

        [Test]
        public void ReferencedEmptyObject_IsCreatedAlongWithTheComponent()
        {
            var source = CreateHierarchy("Source", "Item", "Anchors/Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.InternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var anchor = target.Find("Anchors/Anchor");
            Assert.IsNotNull(anchor);
            Assert.AreSame(anchor, target.Find("Item").GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
        }

        [Test]
        public void ReferencedObjectWithComponents_IsNotCreatedBare()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            source.Find("Anchor").gameObject.AddComponent<SphereCollider>();
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));

            Assert.AreEqual(ReferenceKind.InternalUnresolved, plan.Components.Single().References.Single().Kind);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void ReferencedEmptyObject_IsNotCreatedWhenCreatingIsOff()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));

            var settings = new CopySettings { CreateMissingObjects = false };
            var plan = BuildPlan(source, target, settings, typeof(ParentConstraint));

            Assert.AreEqual(ReferenceKind.InternalUnresolved, plan.Components.Single().References.Single().Kind);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void ReferencedEmptyObject_IsNotCreatedForASkippedComponent()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));
            target.Find("Item").gameObject.AddComponent<ParentConstraint>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };
            var plan = BuildPlan(source, target, settings, typeof(ParentConstraint));

            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single().Action);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void UnresolvedReference_HoldsTheComponentBackWhenTheSettingSaysSo()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());

            var settings = new CopySettings { UnresolvedPolicy = UnresolvedReferencePolicy.SkipComponent };
            var plan = BuildPlan(source, target, settings, typeof(LODGroup));
            var planned = plan.Components.Single();
            Assert.AreEqual(ComponentAction.Blocked, planned.Action);
            Assert.AreEqual(BlockReason.UnresolvedReference, planned.BlockReason);
            Assert.AreEqual(1, planned.UnresolvedReferences.Count);
            Assert.IsEmpty(planned.References);

            CopyExecutor.Execute(plan);

            Assert.IsNull(target.GetComponent<LODGroup>(), "A held-back component must not be written at all");
        }

        [Test]
        public void HoldingAComponentBack_HoldsBackTheOnesThatReferToIt()
        {
            // Anchor carries a component, so it is not created bare and the renderer's anchor stays unresolved
            var source = CreateHierarchy("Source", "Body", "Anchor");
            var target = CreateHierarchy("Target", "Body");
            source.Find("Anchor").gameObject.AddComponent<BoxCollider>();
            var renderer = source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            renderer.probeAnchor = source.Find("Anchor");
            AddLodGroup(source, renderer);

            var settings = new CopySettings { UnresolvedPolicy = UnresolvedReferencePolicy.SkipComponent };
            var plan = BuildPlan(source, target, settings, typeof(LODGroup), typeof(MeshRenderer));

            Assert.AreEqual(2, plan.Components.Count);
            Assert.That(plan.Components.Select(c => c.BlockReason),
                Is.All.EqualTo(BlockReason.UnresolvedReference),
                "The LODGroup loses its renderer once the renderer is held back");
            Assert.IsEmpty(plan.ObjectsToCreate);

            var lodGroup = plan.Components.Single(c => c.Entry.Type == typeof(LODGroup));
            Assert.AreEqual(ComponentKey.For(source.Find("Body"), source, typeof(MeshRenderer), 0),
                lodGroup.UnresolvedReferences.Single().MissingDependency);
        }

        [Test]
        public void UnresolvedReference_IsClearedByDefault()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            Assert.AreEqual(ComponentAction.Add, plan.Components.Single().Action);
        }

        private static CopyPlan BuildPlanWithSurroundings(Transform source, Transform target, params System.Type[] types)
        {
            var map = TransformMapper.Build(source, target);
            return CopyPlanBuilder.Build(Select(source, types), map, new CopySettings(),
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced));
        }

        [Test]
        public void ExternalReference_IsRedirectedToTheSurroundingsOfTheTarget()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Hips"));

            var plan = BuildPlanWithSurroundings(
                avatarA.Find("Outfit"), avatarB.Find("Outfit"), typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.ExternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copied = avatarB.Find("Outfit/Item").GetComponent<ParentConstraint>();
            Assert.AreSame(avatarB.Find("Armature/Hips"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ExternalReference_IsKeptWhenRedirectingIsOff()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Hips"));
            var source = avatarA.Find("Outfit");
            var target = avatarB.Find("Outfit");

            var plan = CopyPlanBuilder.Build(
                Select(source, typeof(ParentConstraint)), TransformMapper.Build(source, target),
                new CopySettings { RedirectExternalReferences = false },
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced));

            Assert.IsNull(plan.ExternalMap);
            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);
        }

        [Test]
        public void ExternalReference_FollowsAManualChoiceWhileRedirectingIsOff()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Armature/Chest", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips", "Armature/Chest", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Hips"));
            var source = avatarA.Find("Outfit");
            var target = avatarB.Find("Outfit");

            var manual = new Dictionary<Transform, Transform>
            {
                { avatarA.Find("Armature/Hips"), avatarB.Find("Armature/Chest") },
            };
            var plan = CopyPlanBuilder.Build(
                Select(source, typeof(ParentConstraint)), TransformMapper.Build(source, target, manual),
                new CopySettings { RedirectExternalReferences = false },
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced, manual));

            // Consumers must not assume a map behind a redirected reference
            Assert.IsNull(plan.ExternalMap);
            Assert.AreEqual(ReferenceKind.ExternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copied = target.Find("Item").GetComponent<ParentConstraint>();
            Assert.AreSame(avatarB.Find("Armature/Chest"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ExternalReference_IsKeptWhenTheUserSaysSo()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Hips"));
            var source = avatarA.Find("Outfit");
            var target = avatarB.Find("Outfit");

            // What "Keep as is" in the row menu stores
            var manual = new Dictionary<Transform, Transform> { { avatarA.Find("Armature/Hips"), null } };
            var plan = CopyPlanBuilder.Build(
                Select(source, typeof(ParentConstraint)), TransformMapper.Build(source, target, manual),
                new CopySettings(),
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced, manual));

            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);
        }

        [Test]
        public void ExternalReference_FollowsAManualChoiceWithoutSurroundings()
        {
            // The target is not placed on its avatar yet, so there is nothing to compare the source's avatar with
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Outfit/Item");
            var target = CreateHierarchy("Outfit", "Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Hips"));
            var source = avatarA.Find("Outfit");

            var manual = new Dictionary<Transform, Transform>
            {
                { avatarA.Find("Armature/Hips"), avatarB.Find("Armature/Hips") },
            };
            var plan = CopyPlanBuilder.Build(
                Select(source, typeof(ParentConstraint)), TransformMapper.Build(source, target, manual),
                new CopySettings(),
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced, manual));
            Assert.IsNull(plan.ExternalMap);

            CopyExecutor.Execute(plan);

            var copied = target.Find("Item").GetComponent<ParentConstraint>();
            Assert.AreSame(avatarB.Find("Armature/Hips"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ExternalReference_IsKeptWhenBothSidesShareTheirSurroundings()
        {
            var avatar = CreateHierarchy("Avatar", "Armature/Hips", "OutfitV1/Item", "OutfitV2/Item");
            AddParentConstraint(avatar.Find("OutfitV1/Item"), avatar.Find("Armature/Hips"));

            var plan = BuildPlanWithSurroundings(
                avatar.Find("OutfitV1"), avatar.Find("OutfitV2"), typeof(ParentConstraint));
            Assert.IsNull(plan.ExternalMap);
            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copied = avatar.Find("OutfitV2/Item").GetComponent<ParentConstraint>();
            Assert.AreSame(avatar.Find("Armature/Hips"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ExternalReference_IsKeptWithoutACounterpart()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Tail", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Tail"));

            var plan = BuildPlanWithSurroundings(
                avatarA.Find("Outfit"), avatarB.Find("Outfit"), typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copied = avatarB.Find("Outfit/Item").GetComponent<ParentConstraint>();
            Assert.AreSame(avatarA.Find("Armature/Tail"), copied.GetSource(0).sourceTransform,
                "The object stays where it is, so the reference is kept rather than cleared");
        }

        [Test]
        public void Mapper_ResolvesOnlyTheScopeBeyondExactMatches()
        {
            var source = CreateHierarchy("Source", "A/Wanted_old", "B/Ignored_old", "C");
            var target = CreateHierarchy("Target", "A/Wanted", "B/Ignored", "C");

            var map = TransformMapper.Build(source, target, null, new[] { source.Find("A/Wanted_old") });

            Assert.IsNotNull(map.Get(source.Find("A/Wanted_old")));
            Assert.IsNull(map.Get(source.Find("B/Ignored_old")));
            Assert.IsTrue(map.TryResolve(source.Find("C"), out _), "Exact matches are cheap and always made");
        }

        [Test]
        public void MissingEmptyObjects_AreListedByTheirTopmostObject()
        {
            var source = CreateHierarchy("Source", "Anchors/A", "Anchors/B/Deep", "Anchors/WithCollider", "Kept");
            var target = CreateHierarchy("Target", "Kept");
            source.Find("Anchors/WithCollider").gameObject.AddComponent<SphereCollider>();
            var map = TransformMapper.Build(source, target);

            var roots = MissingObjects.FindRoots(map);

            Assert.AreEqual(new[] { source.Find("Anchors") }, roots);
            Assert.AreEqual(3, MissingObjects.CountBelow(source.Find("Anchors"), map));
        }

        [Test]
        public void RequestedEmptyObject_BringsTheEmptyObjectsBelowIt()
        {
            var source = CreateHierarchy("Source", "Anchors/A", "Anchors/B/Deep", "Anchors/WithCollider");
            var target = CreateHierarchy("Target");
            source.Find("Anchors/WithCollider").gameObject.AddComponent<SphereCollider>();
            var map = TransformMapper.Build(source, target);

            var plan = CopyPlanBuilder.Build(
                new List<ComponentEntry>(), map, new CopySettings(), objectsToAdd: new[] { source.Find("Anchors") });
            CopyExecutor.Execute(plan);

            Assert.IsNotNull(target.Find("Anchors/A"));
            Assert.IsNotNull(target.Find("Anchors/B/Deep"));
            Assert.IsNull(target.Find("Anchors/WithCollider"),
                "Objects with components are left to the component list");
        }

        [Test]
        public void MissingEmptyObjects_SkipBonesAndWhatHangsBelowAMissingBone()
        {
            var source = CreateHierarchy("Source", "Armature/Hips/Tail/Tail_end", "Mesh");
            var target = CreateHierarchy("Target", "Armature/Hips", "Mesh");
            AddSkinnedMesh(source.Find("Mesh"), source.Find("Armature/Hips"), source.Find("Armature/Hips/Tail"));
            AddSkinnedMesh(target.Find("Mesh"), target.Find("Armature/Hips"));

            Assert.IsEmpty(MissingObjects.FindRoots(TransformMapper.Build(source, target)));
        }

        [Test]
        public void MissingHostObject_IsCreatedBelowMappedParent()
        {
            var source = CreateHierarchy("Source", "Armature/Bone/Collider");
            var target = CreateHierarchy("Target", "Armature/Bone");
            var sourceCollider = source.Find("Armature/Bone/Collider");
            sourceCollider.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            sourceCollider.gameObject.AddComponent<SphereCollider>().radius = 0.25f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(1, plan.ObjectsToCreate.Count);

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.CreatedObjects);
            var created = target.Find("Armature/Bone/Collider");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), created.localPosition);
            Assert.AreEqual(0.25f, created.GetComponent<SphereCollider>().radius);
        }

        [Test]
        public void MissingHostObject_IsBlockedWhenCreationIsDisabled()
        {
            var source = CreateHierarchy("Source", "Armature/Collider");
            var target = CreateHierarchy("Target", "Armature");
            source.Find("Armature/Collider").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { CreateMissingObjects = false };
            var planned = BuildPlan(source, target, settings, typeof(SphereCollider)).Components.Single();

            Assert.AreEqual(ComponentAction.Blocked, planned.Action);
            Assert.AreEqual(BlockReason.HostUnmapped, planned.BlockReason);
        }

        [Test]
        public void HostWithUnconfirmedSuggestion_IsBlockedInsteadOfCreated()
        {
            var source = CreateHierarchy("Source", "Armature/Skirt_01");
            var target = CreateHierarchy("Target", "Armature/Skirt");
            source.Find("Armature/Skirt_01").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));

            Assert.AreEqual(BlockReason.HostNeedsReview, plan.Components.Single().BlockReason);
            Assert.AreEqual(0, plan.ObjectsToCreate.Count);
        }

        [Test]
        public void MissingBone_IsNeverCreated()
        {
            var source = CreateHierarchy("Source", "Body", "Armature/Hips/Tail");
            var target = CreateHierarchy("Target", "Body", "Armature/Hips");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Hips/Tail"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Hips"));
            source.Find("Armature/Hips/Tail").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));

            Assert.AreEqual(ComponentAction.Blocked, plan.Components.Single().Action);
            Assert.AreEqual(BlockReason.BoneMissing, plan.Components.Single().BlockReason);
            Assert.AreEqual(0, plan.ObjectsToCreate.Count);
        }

        [Test]
        public void MissingObjectBelowABone_IsStillCreated()
        {
            var source = CreateHierarchy("Source", "Body", "Armature/Hips/Collider");
            var target = CreateHierarchy("Target", "Body", "Armature/Hips");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Hips"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Hips"));
            source.Find("Armature/Hips/Collider").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            Assert.IsNotNull(target.Find("Armature/Hips/Collider"));
            Assert.IsNotNull(target.Find("Armature/Hips/Collider").GetComponent<SphereCollider>());
        }

        [Test]
        public void OverwritePolicy_WritesOntoExistingComponentAndThenReportsIdentical()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(ComponentAction.Overwrite, plan.Components.Single().Action);
            Assert.AreEqual(DiffKind.ValueMismatch, CopyVerifier.Verify(plan).Components.Single().Kind);

            CopyExecutor.Execute(plan);

            Assert.AreEqual(0.5f, existing.radius);
            Assert.AreEqual(1, target.Find("Bone").GetComponents<SphereCollider>().Length);
            Assert.AreEqual(DiffKind.Match, CopyVerifier.Verify(plan).Components.Single().Kind);

            var replanned = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(ComponentAction.SkipIdentical, replanned.Components.Single().Action);
        }

        [Test]
        public void SkipPolicy_LeavesExistingComponentUntouched()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single().Action);
            Assert.AreEqual(0.1f, existing.radius);
        }

        [Test]
        public void AddPolicy_AddsNextToExistingComponent()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Add };
            CopyExecutor.Execute(BuildPlan(source, target, settings, typeof(SphereCollider)));

            Assert.AreEqual(2, target.Find("Bone").GetComponents<SphereCollider>().Length);
        }

        [Test]
        public void ReplacePolicy_RemovesExistingComponentsOnReceivingHostOnly()
        {
            var source = CreateHierarchy("Source", "Bone", "Untouched");
            var target = CreateHierarchy("Target", "Bone", "Untouched");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Untouched").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            Assert.AreEqual(2, plan.ComponentsToRemove.Count);

            CopyExecutor.Execute(plan);

            var remaining = target.Find("Bone").GetComponents<SphereCollider>();
            Assert.AreEqual(1, remaining.Length);
            Assert.AreEqual(0.5f, remaining[0].radius);
            Assert.AreEqual(1, target.Find("Untouched").GetComponents<SphereCollider>().Length);
        }

        [Test]
        public void ReplacePolicy_WarnsAboutReferencesFromUntouchedComponents()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            var targetRenderer = target.Find("Body").gameObject.AddComponent<MeshRenderer>();
            var holder = AddLodGroup(target, targetRenderer);

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(MeshRenderer));

            var warning = plan.BrokenReferences.Single();
            Assert.AreSame(holder, warning.Holder);
            Assert.AreSame(targetRenderer, warning.Removed);
        }

        [Test]
        public void ReplacePolicy_RemovesAComponentAfterTheOneThatRequiresIt()
        {
            // HingeJoint requires the Rigidbody next to it, so the Rigidbody, which comes first, has to go last
            var source = CreateHierarchy("Source", "Door");
            var target = CreateHierarchy("Target", "Door");
            source.Find("Door").gameObject.AddComponent<Rigidbody>().mass = 5f;
            source.Find("Door").gameObject.AddComponent<HingeJoint>().useSpring = true;
            target.Find("Door").gameObject.AddComponent<Rigidbody>().mass = 2f;
            target.Find("Door").gameObject.AddComponent<HingeJoint>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(Rigidbody), typeof(HingeJoint));
            Assert.That(plan.Components.Select(c => c.Action), Is.All.EqualTo(ComponentAction.Replace));
            Assert.AreEqual(2, plan.ComponentsToRemove.Count);
            Assert.IsEmpty(plan.KeptComponents, "The joint is removed as well, so it holds nothing back");

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(2, result.RemovedComponents);
            Assert.IsEmpty(result.NotRemoved);
            Assert.IsEmpty(result.Failed);
            var door = target.Find("Door");
            Assert.AreEqual(1, door.GetComponents<Rigidbody>().Length);
            Assert.AreEqual(5f, door.GetComponent<Rigidbody>().mass);
            Assert.AreEqual(1, door.GetComponents<HingeJoint>().Length);
            Assert.IsTrue(door.GetComponent<HingeJoint>().useSpring);
        }

        [Test]
        public void ReplacePolicy_OverwritesInPlaceAComponentThatAnotherOneRequires()
        {
            // The target's HingeJoint is not copied, and the Rigidbody cannot be removed from under it
            var source = CreateHierarchy("Source", "Door");
            var target = CreateHierarchy("Target", "Door");
            source.Find("Door").gameObject.AddComponent<Rigidbody>().mass = 5f;
            var targetBody = target.Find("Door").gameObject.AddComponent<Rigidbody>();
            targetBody.mass = 2f;
            target.Find("Door").gameObject.AddComponent<HingeJoint>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(Rigidbody));
            var planned = plan.Components.Single();
            Assert.AreEqual(ComponentAction.Overwrite, planned.Action);
            Assert.AreSame(targetBody, planned.Existing);
            Assert.AreEqual("HingeJoint", planned.KeptBy);
            Assert.IsEmpty(plan.ComponentsToRemove);
            var kept = plan.KeptComponents.Single();
            Assert.AreSame(targetBody, kept.Component);
            Assert.AreEqual("HingeJoint", kept.RequiredBy);
            Assert.AreSame(planned, kept.OverwrittenBy);

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(0, result.RemovedComponents);
            Assert.IsEmpty(result.Failed);
            Assert.AreSame(targetBody, target.Find("Door").GetComponent<Rigidbody>());
            Assert.AreEqual(5f, targetBody.mass);
        }

        [Test]
        public void ReplacePolicy_LeavesTheComponentsBeyondTheCopiesInPlace()
        {
            // AudioLowPassFilter requires an AudioBehaviour: every AudioSource satisfies it, and several can coexist
            var source = CreateHierarchy("Source", "Speaker");
            var target = CreateHierarchy("Target", "Speaker");
            source.Find("Speaker").gameObject.AddComponent<AudioSource>().volume = 0.3f;
            var speaker = target.Find("Speaker").gameObject;
            var first = speaker.AddComponent<AudioSource>();
            first.volume = 0.9f;
            var second = speaker.AddComponent<AudioSource>();
            second.volume = 0.8f;
            speaker.AddComponent<AudioLowPassFilter>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(AudioSource));
            var planned = plan.Components.Single();
            Assert.AreEqual(ComponentAction.Overwrite, planned.Action);
            Assert.AreSame(first, planned.Existing);
            Assert.AreEqual("AudioLowPassFilter", planned.KeptBy);
            Assert.IsEmpty(plan.ComponentsToRemove);
            Assert.AreEqual(2, plan.KeptComponents.Count);
            Assert.AreSame(planned, plan.KeptComponents[0].OverwrittenBy);
            Assert.AreSame(second, plan.KeptComponents[1].Component);
            Assert.IsNull(plan.KeptComponents[1].OverwrittenBy, "There is only one copy to overwrite with");

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(0, result.RemovedComponents);
            Assert.IsEmpty(result.Failed);
            Assert.AreEqual(2, speaker.GetComponents<AudioSource>().Length);
            Assert.AreEqual(0.3f, first.volume);
            Assert.AreEqual(0.8f, second.volume);
            var extra = CopyVerifier.Verify(plan).Components.Single(c => c.Kind == DiffKind.ExtraOnTarget);
            Assert.AreSame(second, extra.Actual);
        }

        [Test]
        public void KeptComponents_FollowAChainOfRequirements()
        {
            // C requires B, which requires A. No built-in components chain three deep, so the links are made up.
            var requirers = new Dictionary<string, string[]>
            {
                { "A", new[] { "B" } },
                { "B", new[] { "C" } },
                { "C", new string[0] },
            };
            string FindRequirer(string component, ICollection<string> removed) =>
                requirers[component].FirstOrDefault(requirer => !removed.Contains(requirer));

            // C stays, so B has to stay for it, and then A for B
            var kept = ComponentDependencies.FindKept(new[] { "A", "B" }, FindRequirer);
            Assert.AreEqual(2, kept.Count);
            Assert.AreEqual("B", kept["A"]);
            Assert.AreEqual("C", kept["B"]);

            Assert.IsEmpty(ComponentDependencies.FindKept(new[] { "A", "B", "C" }, FindRequirer),
                "A requirer that is removed as well holds nothing back");
        }

        [Test]
        public void ReplacePolicy_DoesNotAddNextToAComponentThatCouldNotBeRemoved()
        {
            var source = CreateHierarchy("Source", "Door");
            var target = CreateHierarchy("Target", "Door");
            source.Find("Door").gameObject.AddComponent<Rigidbody>().mass = 5f;
            var targetBody = target.Find("Door").gameObject.AddComponent<Rigidbody>();
            targetBody.mass = 2f;

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(Rigidbody));
            Assert.AreEqual(ComponentAction.Replace, plan.Components.Single().Action);
            Assert.AreSame(targetBody, plan.ComponentsToRemove.Single());

            // Added after planning, so the plan still removes the Rigidbody. Validating the plan right before
            // applying is meant to stop this case earlier; this is the executor's own safety net behind that.
            target.Find("Door").gameObject.AddComponent<HingeJoint>();

            // The removal is not even tried, so Unity logs nothing
            var result = CopyExecutor.Execute(plan);

            Assert.AreSame(targetBody, result.NotRemoved.Single());
            Assert.AreEqual(0, result.RemovedComponents);
            Assert.AreSame(plan.Components.Single(), result.Failed.Single());
            Assert.AreEqual(0, result.WrittenComponents);
            var bodies = target.Find("Door").GetComponents<Rigidbody>();
            Assert.AreEqual(1, bodies.Length, "No second Rigidbody may be added next to the one that stayed");
            Assert.AreEqual(2f, bodies[0].mass);
        }

        [Test]
        public void ExtraComponentOnTarget_IsReported()
        {
            var source = CreateHierarchy("Source", "Bone", "Leftover");
            var target = CreateHierarchy("Target", "Bone", "Leftover");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            var leftover = target.Find("Leftover").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            var extra = CopyVerifier.Verify(plan).Components.Single(c => c.Kind == DiffKind.ExtraOnTarget);
            Assert.AreSame(leftover, extra.Actual);
        }

        [Test]
        public void Execute_IsRevertedByASingleUndo()
        {
            var source = CreateHierarchy("Source", "Armature/Bone/Collider");
            var target = CreateHierarchy("Target", "Armature/Bone");
            source.Find("Armature/Bone/Collider").gameObject.AddComponent<SphereCollider>();
            source.Find("Armature/Bone").gameObject.AddComponent<SphereCollider>();

            CopyExecutor.Execute(BuildPlan(source, target, new CopySettings(), typeof(SphereCollider)));
            Assert.IsNotNull(target.Find("Armature/Bone/Collider"));

            Undo.PerformUndo();

            Assert.IsNull(target.Find("Armature/Bone/Collider"));
            Assert.IsNull(target.Find("Armature/Bone").GetComponent<SphereCollider>());
        }

        [Test]
        public void FailedComponent_LeavesTheRestAppliedAndIsRevertedByASingleUndo()
        {
            AssertPartialApplication(UnresolvedReferencePolicy.Clear);
        }

        [Test]
        public void FailedComponent_DoesNotHoldBackTheOnesThatReferToIt()
        {
            // The setting holds back what the plan cannot resolve; a failure while applying is no such case
            AssertPartialApplication(UnresolvedReferencePolicy.SkipComponent);
        }

        /// <summary>
        /// A cannot be added, B refers to A, and C has nothing to do with A. B and C are written all the same,
        /// B without the reference that has nothing to point at, and a single Undo takes everything back.
        /// </summary>
        private void AssertPartialApplication(UnresolvedReferencePolicy unresolvedPolicy)
        {
            var source = CreateHierarchy("Source", "A", "B", "C");
            var target = CreateHierarchy("Target", "A", "B", "C");

            // The target's A has a Rigidbody already, and there can only be one
            var sourceBody = source.Find("A").gameObject.AddComponent<Rigidbody>();
            sourceBody.mass = 9f;
            var targetBody = target.Find("A").gameObject.AddComponent<Rigidbody>();
            targetBody.mass = 2f;

            // The joint brings the Rigidbody it requires along; the target's B has only that Rigidbody
            var sourceJoint = source.Find("B").gameObject.AddComponent<HingeJoint>();
            sourceJoint.connectedBody = sourceBody;
            target.Find("B").gameObject.AddComponent<Rigidbody>();

            var sourceCollider = source.Find("C").gameObject.AddComponent<SphereCollider>();
            sourceCollider.radius = 0.7f;

            var settings = new CopySettings
            {
                ExistingPolicy = ExistingComponentPolicy.Add,
                UnresolvedPolicy = unresolvedPolicy,
            };
            // By instance: the Rigidbody of B would fail to be added as well
            var plan = CopyPlanBuilder.Build(
                SelectComponents(source, sourceBody, sourceJoint, sourceCollider),
                TransformMapper.Build(source, target), settings);
            Assert.That(plan.Components.Select(c => c.Action), Is.All.EqualTo(ComponentAction.Add));

            // Unity turns the second Rigidbody down with a plain log message, not an error
            LogAssert.Expect(LogType.Log, new Regex("Can't add component 'Rigidbody' to A because"));
            var result = CopyExecutor.Execute(plan);

            Assert.AreSame(sourceBody, result.Failed.Single().Entry.Component);
            Assert.AreEqual(2, result.WrittenComponents);
            Assert.AreEqual(1, target.Find("A").GetComponents<Rigidbody>().Length);
            Assert.AreEqual(2f, targetBody.mass);

            // The copy of A does not exist, so B's reference to it is cleared, which the diff check reports
            var targetJoint = target.Find("B").GetComponent<HingeJoint>();
            Assert.IsNotNull(targetJoint);
            Assert.IsNull(targetJoint.connectedBody);
            Assert.AreEqual(DiffKind.ReferenceMismatch,
                CopyVerifier.Verify(plan).Components.Single(d => d.Actual == targetJoint).Kind);

            Assert.AreEqual(0.7f, target.Find("C").GetComponent<SphereCollider>().radius);

            Undo.PerformUndo();

            Assert.AreEqual(1, target.Find("A").GetComponents<Rigidbody>().Length);
            Assert.AreEqual(2f, targetBody.mass);
            Assert.IsNull(target.Find("B").GetComponent<HingeJoint>());
            Assert.IsNotNull(target.Find("B").GetComponent<Rigidbody>());
            Assert.IsNull(target.Find("C").GetComponent<SphereCollider>());
        }

        [Test]
        public void Scanner_SkipsTransformsAndCategorizesTypes()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            source.Find("Bone").gameObject.AddComponent<ParentConstraint>();
            source.Find("Bone").gameObject.AddComponent<MeshRenderer>();

            var entries = ComponentScanner.Scan(source);

            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual(ComponentCategory.Other, entries.Single(e => e.Type == typeof(SphereCollider)).Category);
            Assert.AreEqual(ComponentCategory.Constraint,
                entries.Single(e => e.Type == typeof(ParentConstraint)).Category);
            Assert.AreEqual(ComponentCategory.ExcludedByDefault,
                entries.Single(e => e.Type == typeof(MeshRenderer)).Category);
            Assert.IsTrue(entries.All(e => e.Tool == null));
        }

#if MODULAR_AVATAR_INSTALLED
        [Test]
        public void Scanner_KeepsModularAvatarAsItsOwnCategory()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.Find("Bone").gameObject.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarVisibleHeadAccessory>();

            var entry = ComponentScanner.Scan(source).Single();

            Assert.AreEqual(ComponentCategory.ModularAvatar, entry.Category);
            Assert.IsNull(entry.Tool);
        }
#endif

#if AVATAR_OPTIMIZER_INSTALLED
        [Test]
        public void Scanner_NamesOtherToolsAfterTheirPackage()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.gameObject.AddComponent<Anatawa12.AvatarOptimizer.TraceAndOptimize>();

            var entry = ComponentScanner.Scan(source).Single();

            Assert.AreEqual(ComponentCategory.Tool, entry.Category);
            Assert.AreEqual("com.anatawa12.avatar-optimizer", entry.Tool.Id);
            Assert.AreEqual("AAO", entry.Tool.ShortName);
        }
#endif
    }
}
