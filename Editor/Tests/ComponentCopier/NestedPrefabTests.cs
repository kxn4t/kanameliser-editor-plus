using System;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// Nested prefabs (a prefab bundling PhysBone settings, a hat below the Head bone, ...) must be added to the
    /// target as prefab instances instead of being rebuilt object by object.
    /// </summary>
    public class NestedPrefabTests : ComponentCopierTestBase
    {
        private static GameObject SaveHatWithCollider(string name)
        {
            return SavePrefab(name, hat => AddChild(hat, "Ribbon").gameObject.AddComponent<SphereCollider>().radius = 0.1f);
        }

        [Test]
        public void MissingNestedPrefab_IsInstantiatedInsteadOfRebuilt()
        {
            var hatAsset = SaveHatWithCollider("Hat_Instantiate");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            sourceHat.localPosition = new Vector3(0f, 0.2f, 0f);
            sourceHat.Find("Ribbon").GetComponent<SphereCollider>().radius = 0.4f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.IsTrue(plan.ObjectsToCreate.Single(o => o.Source == sourceHat).IsPrefabRoot);

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.InstantiatedPrefabs);
            Assert.AreEqual(0, result.CreatedObjects, "Objects inside the prefab must not be rebuilt");

            var targetHat = target.Find("Armature/Head/Hat_Instantiate");
            Assert.IsNotNull(targetHat);
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(targetHat.gameObject));
            Assert.AreEqual(AssetDatabase.GetAssetPath(hatAsset),
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(targetHat.gameObject));
            Assert.AreEqual(new Vector3(0f, 0.2f, 0f), targetHat.localPosition);

            var colliders = targetHat.Find("Ribbon").GetComponents<SphereCollider>();
            Assert.AreEqual(1, colliders.Length, "The collider that came with the prefab must be reused");
            Assert.AreEqual(0.4f, colliders[0].radius, "Overrides made on the source instance must carry over");
        }

        [Test]
        public void PrefabWithAnObjectMappedByHand_IsNotBroughtOverAsAWhole()
        {
            var hatAsset = SavePrefab("Hat_Manual", hat =>
            {
                hat.gameObject.AddComponent<ParentConstraint>();
                AddChild(hat, "Ribbon").gameObject.AddComponent<SphereCollider>();
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head/Cap/Ribbon");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var ribbon = target.Find("Armature/Head/Cap/Ribbon");

            var map = TransformMapper.Build(source, target,
                new System.Collections.Generic.Dictionary<Transform, Transform> { [sourceHat.Find("Ribbon")] = ribbon });
            // The hat's own component is planned first, and must not bring the hat along for the ribbon
            var plan = CopyPlanBuilder.Build(Select(source, typeof(ParentConstraint), typeof(SphereCollider)), map,
                new CopySettings());

            // The user's word says the hat is there, in part at least: its objects go where the map says, and the
            // ones without a counterpart are created one by one
            var createdHat = plan.ObjectsToCreate.Single();
            Assert.AreSame(sourceHat, createdHat.Source);
            Assert.IsNull(createdHat.PrefabAsset);
            Assert.AreSame(createdHat, plan.Components.Single(c => c.Entry.Type == typeof(ParentConstraint)).HostToCreate);
            Assert.AreSame(ribbon, plan.Components.Single(c => c.Entry.Type == typeof(SphereCollider)).TargetHost);
            Assert.IsEmpty(NestedPrefabs.FindMissingRoots(map));
        }

        [Test]
        public void ComponentsInsideThePrefab_AreCopiedEvenWhenNotSelected()
        {
            var hatAsset = SavePrefab("Hat_Implicit", hat =>
            {
                hat.gameObject.AddComponent<ParentConstraint>();
                AddChild(hat, "Ribbon").gameObject.AddComponent<SphereCollider>();
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            // A reference from inside the prefab to the outfit's bone: only possible as an instance override
            sourceHat.GetComponent<ParentConstraint>().AddSource(
                new ConstraintSource { sourceTransform = source.Find("Armature/Head"), weight = 1f });

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.IsTrue(plan.Components.Single(c => c.Entry.Type == typeof(ParentConstraint)).Implicit);
            Assert.IsFalse(plan.Components.Single(c => c.Entry.Type == typeof(SphereCollider)).Implicit);

            CopyExecutor.Execute(plan);

            var constraint = target.Find("Armature/Head/Hat_Implicit").GetComponent<ParentConstraint>();
            Assert.AreEqual(1, constraint.sourceCount);
            Assert.AreSame(target.Find("Armature/Head"), constraint.GetSource(0).sourceTransform);
        }

        [Test]
        public void PrefabWithItsOwnBones_IsNotBlockedByTheBoneRule()
        {
            var hatAsset = SavePrefab("Hat_Bones", hat =>
            {
                var bone = AddChild(hat, "HatBone");
                bone.gameObject.AddComponent<SphereCollider>();
                AddSkinnedMesh(AddChild(hat, "HatMesh"), bone);
            });
            var source = CreateHierarchy("Source", "Body", "Armature/Head");
            var target = CreateHierarchy("Target", "Body", "Armature/Head");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Head"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Head"));
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));

            var map = TransformMapper.Build(source, target);
            Assert.IsTrue(map.SourceSkeleton.IsBone(sourceHat.Find("HatBone")), "Test setup: HatBone must count as a bone");

            var plan = CopyPlanBuilder.Build(Select(source, typeof(SphereCollider)), map, new CopySettings());
            Assert.AreEqual(BlockReason.None, plan.Components.First(c => !c.Implicit).BlockReason);

            CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_Bones");
            Assert.IsNotNull(targetHat.Find("HatBone").GetComponent<SphereCollider>());
            Assert.AreSame(targetHat.Find("HatBone"),
                targetHat.Find("HatMesh").GetComponent<SkinnedMeshRenderer>().bones[0],
                "References inside the prefab must point into the new instance");
        }

        [Test]
        public void PrefabThatAlreadyExistsInTheTarget_IsUpdatedInPlace()
        {
            var hatAsset = SaveHatWithCollider("Hat_Existing");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head")).Find("Ribbon").GetComponent<SphereCollider>().radius = 0.4f;
            Instantiate(hatAsset, target.Find("Armature/Head"));

            var result = CopyExecutor.Execute(BuildPlan(source, target, new CopySettings(), typeof(SphereCollider)));

            Assert.AreEqual(0, result.InstantiatedPrefabs);
            var head = target.Find("Armature/Head");
            Assert.AreEqual(1, head.childCount);
            Assert.AreEqual(0.4f, head.Find("Hat_Existing/Ribbon").GetComponent<SphereCollider>().radius);
        }

        [Test]
        public void CopyBetweenInstancesOfOnePrefab_KeepsAReferenceIntoTheOtherInstance()
        {
            // An outfit whose hat follows the outfit's own hips
            var outfitAsset = SavePrefab("Outfit_Twins", outfit =>
            {
                var hips = AddChild(AddChild(outfit, "Armature"), "Hips");
                AddChild(AddChild(outfit, "Hat"), "Ribbon").gameObject.AddComponent<ParentConstraint>()
                    .AddSource(new ConstraintSource { sourceTransform = hips, weight = 1f });
            });
            var outfitA = Instantiate(outfitAsset, CreateHierarchy("AvatarA"));
            var outfitB = Instantiate(outfitAsset, CreateHierarchy("AvatarB"));
            var hipsA = outfitA.Find("Armature/Hips");

            // Only the hat is copied, so the hips of A lie outside the source and are kept as they are
            var plan = BuildPlan(outfitA.Find("Hat"), outfitB.Find("Hat"), new CopySettings(), typeof(ParentConstraint));
            var planned = plan.Components.Single();
            Assert.AreEqual(ComponentAction.Overwrite, planned.Action);
            Assert.AreEqual(ReferenceKind.ExternalScene, planned.References.Single(r => r.SourceValue == hipsA).Kind);

            CopyExecutor.Execute(plan);

            // Both hips stand for the same object of the prefab, yet pointing at A's is a real change
            Assert.AreSame(hipsA, outfitB.Find("Hat/Ribbon").GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
        }

        [Test]
        public void ObjectCreationTurnedOff_DoesNotInstantiatePrefabs()
        {
            var hatAsset = SaveHatWithCollider("Hat_Disabled");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            var settings = new CopySettings { CreateMissingObjects = false };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));

            Assert.AreEqual(BlockReason.HostUnmapped, plan.Components.Single().BlockReason);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void NestedPrefabInsideAPrefabAssetSource_IsInstantiated()
        {
            var hatAsset = SaveHatWithCollider("Hat_InAsset");
            var costumeAsset = SavePrefab("Costume_WithHat", costume =>
            {
                var head = AddChild(AddChild(costume, "Armature"), "Head");
                Instantiate(hatAsset, head);
            });
            var target = CreateHierarchy("Target", "Armature/Head");

            var plan = BuildPlan(costumeAsset.transform, target, new CopySettings(), typeof(SphereCollider));
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.InstantiatedPrefabs);
            var targetHat = target.Find("Armature/Head/Hat_InAsset");
            Assert.IsNotNull(targetHat);
            Assert.AreEqual(AssetDatabase.GetAssetPath(hatAsset),
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(targetHat.gameObject));
        }

        [Test]
        public void RequestedPrefab_IsAddedWithoutAnySelectedComponent()
        {
            var hatAsset = SaveHatWithCollider("Hat_Requested");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            sourceHat.Find("Ribbon").GetComponent<SphereCollider>().radius = 0.4f;

            var map = TransformMapper.Build(source, target);
            CollectionAssert.AreEqual(new[] { sourceHat }, NestedPrefabs.FindMissingRoots(map));

            var nothingSelected = Enumerable.Empty<ComponentEntry>();
            Assert.IsEmpty(CopyPlanBuilder.Build(nothingSelected, map, new CopySettings()).ObjectsToCreate,
                "Without a request or a selected component the prefab is left alone");

            var plan = CopyPlanBuilder.Build(nothingSelected, map, new CopySettings(), objectsToAdd: new[] { sourceHat });
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.InstantiatedPrefabs);
            var targetHat = target.Find("Armature/Head/Hat_Requested");
            Assert.IsNotNull(targetHat);
            Assert.AreEqual(0.4f, targetHat.Find("Ribbon").GetComponent<SphereCollider>().radius,
                "The contents are made to match the source even though nothing was selected");

            Assert.IsEmpty(NestedPrefabs.FindMissingRoots(TransformMapper.Build(source, target)),
                "Once added, the prefab is no longer missing");
        }

        [Test]
        public void RequestedPrefabBelowAMissingBone_IsReportedAsBlocked()
        {
            var hatAsset = SaveHatWithCollider("Hat_Blocked");
            var source = CreateHierarchy("Source", "Body", "Armature/Head/Ear");
            var target = CreateHierarchy("Target", "Body", "Armature/Head");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Head/Ear"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Head"));
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head/Ear"));

            var map = TransformMapper.Build(source, target);
            var plan = CopyPlanBuilder.Build(
                Enumerable.Empty<ComponentEntry>(), map, new CopySettings(), objectsToAdd: new[] { sourceHat });

            Assert.AreEqual(BlockReason.BoneMissing, plan.BlockedObjects.Single().Reason);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void PrefabInsideAMissingPrefab_IsNotListedOnItsOwn()
        {
            var ribbonAsset = SavePrefab("Ribbon_Inner", ribbon => ribbon.gameObject.AddComponent<SphereCollider>());
            var hatAsset = SavePrefab("Hat_Outer", hat => Instantiate(ribbonAsset, hat));
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));

            var map = TransformMapper.Build(source, target);

            // The inner prefab arrives with the outer one
            CollectionAssert.AreEqual(new[] { sourceHat }, NestedPrefabs.FindMissingRoots(map));

            var plan = CopyPlanBuilder.Build(
                Enumerable.Empty<ComponentEntry>(), map, new CopySettings(),
                objectsToAdd: new[] { sourceHat.Find("Ribbon_Inner") });
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.InstantiatedPrefabs, "Requesting the inner prefab adds the outer one, once");
            Assert.IsNotNull(target.Find("Armature/Head/Hat_Outer/Ribbon_Inner"));
        }

        [Test]
        public void InstantiatedPrefab_IsRemovedByASingleUndo()
        {
            var hatAsset = SaveHatWithCollider("Hat_Undo");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            CopyExecutor.Execute(BuildPlan(source, target, new CopySettings(), typeof(SphereCollider)));
            Assert.AreEqual(1, target.Find("Armature/Head").childCount);

            Undo.PerformUndo();

            Assert.AreEqual(0, target.Find("Armature/Head").childCount);
        }

        #region Overrides of the source instance

        [Test]
        public void ObjectRenamedOnTheSourceInstance_IsFoundUnderItsNewName()
        {
            var hatAsset = SaveHatWithCollider("Hat_Renamed");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            sourceHat.Find("Ribbon").name = "Ribbon_Front";

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            var result = CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_Renamed");
            Assert.AreEqual(1, targetHat.childCount, "The renamed object must not be created next to the asset's");
            Assert.AreEqual("Ribbon_Front", targetHat.GetChild(0).name);
            Assert.AreEqual(1, targetHat.GetChild(0).GetComponents<SphereCollider>().Length);
            Assert.AreEqual(0, result.CreatedObjects);
            Assert.AreEqual(0, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void ObjectRemovedFromTheSourceInstance_IsRemovedFromTheNewOne()
        {
            var hatAsset = SaveHatWithAnchor("Hat_RemovedObject");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            Undo.DestroyObjectImmediate(sourceHat.Find("Anchor").gameObject);

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_RemovedObject");
            Assert.IsNull(targetHat.Find("Anchor"), "The asset's object must not come back");
            Assert.IsNotNull(targetHat.Find("Ribbon"));
            Assert.AreEqual(1, PrefabUtility.GetRemovedGameObjects(targetHat.gameObject).Count,
                "Removed as an override, like on the source");
            Assert.AreEqual(0, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void ComponentRemovedFromTheSourceInstance_IsRemovedFromTheNewOne()
        {
            var hatAsset = SaveHatWithAnchor("Hat_RemovedComponent");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            Undo.DestroyObjectImmediate(sourceHat.GetComponent<ParentConstraint>());

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.IsFalse(plan.Components.Any(c => c.Entry.Type == typeof(ParentConstraint)));

            var result = CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_RemovedComponent");
            Assert.IsNull(targetHat.GetComponent<ParentConstraint>(), "The asset's component must not come back");
            Assert.AreEqual(1, PrefabUtility.GetRemovedComponents(targetHat.gameObject).Count,
                "Removed as an override, like on the source");
            Assert.AreEqual(1, result.RemovedComponents);
            Assert.AreEqual(0, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void ObjectInsideAPrefabNestedInTheAsset_IsFoundThroughTheInnerPrefab()
        {
            var ribbonAsset = SaveHatWithCollider("Ribbon_InAsset");
            var hatAsset = SavePrefab("Hat_WithInner", hat => Instantiate(ribbonAsset, hat));
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            sourceHat.Find("Ribbon_InAsset/Ribbon").name = "Ribbon_Front";

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.InstantiatedPrefabs, "The inner prefab arrives with the outer one");
            Assert.AreEqual(0, result.CreatedObjects);
            var inner = target.Find("Armature/Head/Hat_WithInner/Ribbon_InAsset");
            Assert.IsNotNull(inner);
            Assert.AreEqual(1, inner.childCount, "Looked up against the inner asset, not created a second time");
            Assert.AreEqual("Ribbon_Front", inner.GetChild(0).name);
        }

        [Test]
        public void PrefabAddedInsideTheSourceInstance_IsInstantiatedOnItsOwn()
        {
            var ribbonAsset = SaveHatWithCollider("Ribbon_Added");
            var hatAsset = SavePrefab("Hat_TakingAnAddedPrefab", hat => AddChild(hat, "Top"));
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var added = Instantiate(ribbonAsset, sourceHat);
            added.Find("Ribbon").name = "Ribbon_Front";

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(2, result.InstantiatedPrefabs, "The added prefab does not correspond to the outer asset");
            Assert.AreEqual(0, result.CreatedObjects);
            var addedInTarget = target.Find("Armature/Head/Hat_TakingAnAddedPrefab/Ribbon_Added");
            Assert.IsNotNull(addedInTarget);
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(addedInTarget.gameObject));
            Assert.AreEqual(1, addedInTarget.childCount, "Looked up against the added prefab's own asset");
            Assert.AreEqual("Ribbon_Front", addedInTarget.GetChild(0).name);
            Assert.AreEqual(1, addedInTarget.GetChild(0).GetComponents<SphereCollider>().Length);
        }

        [Test]
        public void ObjectTheOuterPrefabAddedInsideTheInnerOne_ArrivesWithTheOuterPrefab()
        {
            var ribbonAsset = SaveHatWithCollider("Ribbon_Extended");
            var hatAsset = SavePrefab("Hat_ExtendingItsInner", hat =>
            {
                var inner = Instantiate(ribbonAsset, hat);
                AddChild(inner, "Extra").gameObject.AddComponent<SphereCollider>();
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            var result = CopyExecutor.Execute(plan);

            // "Extra" is not part of the inner asset, but it is part of the outer one and comes with it
            Assert.AreEqual(1, result.InstantiatedPrefabs);
            Assert.AreEqual(0, result.CreatedObjects, "The object the outer prefab brings must not be created again");
            var inner = target.Find("Armature/Head/Hat_ExtendingItsInner/Ribbon_Extended");
            Assert.AreEqual(2, inner.childCount);
            Assert.AreEqual(1, inner.Cast<Transform>().Count(t => t.name == "Extra"));
            Assert.AreEqual(1, inner.Find("Extra").GetComponents<SphereCollider>().Length);
            Assert.AreEqual(0, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void ComponentRemovedBeforeAnotherOfItsType_KeepsTheIndicesInLine()
        {
            var hatAsset = SavePrefab("Hat_RemovedFirst", hat =>
            {
                var ribbon = AddChild(hat, "Ribbon").gameObject;
                ribbon.AddComponent<SphereCollider>().radius = 0.1f;
                ribbon.AddComponent<SphereCollider>().radius = 0.2f;
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var sourceColliders = sourceHat.Find("Ribbon").GetComponents<SphereCollider>();
            Undo.DestroyObjectImmediate(sourceColliders[0]);
            sourceColliders[1].radius = 0.5f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            // The one collider of the source is index 0 there, and must land on the one that stays, not on the
            // one the source removed
            var colliders = target.Find("Armature/Head/Hat_RemovedFirst/Ribbon").GetComponents<SphereCollider>();
            Assert.AreEqual(1, colliders.Length);
            Assert.AreEqual(0.5f, colliders[0].radius);
        }

        [Test]
        public void ImplicitComponentListedAfterASelectedOneOfItsType_KeepsItsIndex()
        {
            // Both colliders were added to the source instance, so neither arrives with the prefab. Only the second
            // one is selected; the first comes along with the prefab and is listed after it.
            var hatAsset = SavePrefab("Hat_AddedColliders", _ => { });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head")).gameObject;
            sourceHat.AddComponent<SphereCollider>().radius = 0.1f;
            var second = sourceHat.AddComponent<SphereCollider>();
            second.radius = 0.2f;

            var plan = CopyPlanBuilder.Build(
                SelectComponents(source, second), TransformMapper.Build(source, target), new CopySettings());
            CollectionAssert.AreEqual(new[] { ComponentOrigin.Selected, ComponentOrigin.Implicit },
                plan.Components.Select(c => c.Origin));

            CopyExecutor.Execute(plan);

            // Each gets a copy of its own, in the order of the source: written in the order of the plan, the copy
            // of the second one would come first and take index 0
            var colliders = target.Find("Armature/Head/Hat_AddedColliders").GetComponents<SphereCollider>();
            Assert.AreEqual(2, colliders.Length);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f }, colliders.Select(c => c.radius));

            var replanned = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.That(replanned.Components.Select(c => c.Action), Is.All.EqualTo(ComponentAction.SkipIdentical));
        }

        #endregion

        #region Left-out components

        /// <summary>A hat with a constraint on its root, a collider on "Ribbon" and another on the leaf "Anchor".</summary>
        private static GameObject SaveHatWithAnchor(string name)
        {
            return SavePrefab(name, hat =>
            {
                hat.gameObject.AddComponent<ParentConstraint>();
                AddChild(hat, "Ribbon").gameObject.AddComponent<SphereCollider>();
                AddChild(hat, "Anchor").gameObject.AddComponent<BoxCollider>();
            });
        }

        private static CopyPlan BuildPlanLeavingOut(Transform source, Transform target, Type selected, Type leftOut)
        {
            var map = TransformMapper.Build(source, target);
            return CopyPlanBuilder.Build(Select(source, selected), map, new CopySettings(),
                leftOut: Select(source, leftOut).Select(e => e.Key));
        }

        [Test]
        public void LeftOutComponent_IsRemovedFromTheInstantiatedPrefab()
        {
            var hatAsset = SaveHatWithAnchor("Hat_LeftOutComponent");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            var plan = BuildPlanLeavingOut(source, target, typeof(SphereCollider), typeof(ParentConstraint));
            var leftOut = plan.Components.Single(c => c.Entry.Type == typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.LeftOut, leftOut.Action);
            Assert.IsFalse(leftOut.Implicit);
            Assert.IsTrue(plan.Components.Single(c => c.Entry.Type == typeof(BoxCollider)).Implicit);

            var result = CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_LeftOutComponent");
            Assert.IsNotNull(targetHat, "The root of the prefab stays even when its components are left out");
            Assert.IsNull(targetHat.GetComponent<ParentConstraint>());
            Assert.AreEqual(1, PrefabUtility.GetRemovedComponents(targetHat.gameObject).Count,
                "Leaving out is a prefab override that can be reverted");
            Assert.IsNotNull(targetHat.Find("Anchor").GetComponent<BoxCollider>());
            Assert.AreEqual(1, result.LeftOutComponents);
            Assert.AreEqual(0, result.LeftOutObjects);

            var report = CopyVerifier.Verify(plan);
            Assert.AreEqual(0, report.Count(DiffKind.ExtraOnTarget));
            Assert.AreEqual(0, report.Count(DiffKind.MissingOnTarget));
        }

        [Test]
        public void LeafObjectWithOnlyLeftOutComponents_IsRemovedAsWell()
        {
            var hatAsset = SaveHatWithAnchor("Hat_LeftOutObject");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            var plan = BuildPlanLeavingOut(source, target, typeof(SphereCollider), typeof(BoxCollider));
            Assert.IsTrue(plan.ObjectsToCreate.Single(o => o.Source.name == "Anchor").LeftOut);
            Assert.IsFalse(plan.ObjectsToCreate.Single(o => o.Source.name == "Ribbon").LeftOut);

            var result = CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_LeftOutObject");
            Assert.IsNull(targetHat.Find("Anchor"), "An empty shell must not stay behind");
            Assert.IsNotNull(targetHat.Find("Ribbon"));
            Assert.AreEqual(1, result.LeftOutObjects);
            Assert.AreEqual(1, result.LeftOutComponents);
            Assert.AreEqual(0, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void ObjectThatACopiedComponentRefersTo_IsKept()
        {
            var hatAsset = SaveHatWithAnchor("Hat_LeftOutReferenced");
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            sourceHat.GetComponent<ParentConstraint>().AddSource(
                new ConstraintSource { sourceTransform = sourceHat.Find("Anchor"), weight = 1f });

            var plan = BuildPlanLeavingOut(source, target, typeof(ParentConstraint), typeof(BoxCollider));
            Assert.IsFalse(plan.ObjectsToCreate.Single(o => o.Source.name == "Anchor").LeftOut);

            CopyExecutor.Execute(plan);

            var targetHat = target.Find("Armature/Head/Hat_LeftOutReferenced");
            var anchor = targetHat.Find("Anchor");
            Assert.IsNotNull(anchor);
            Assert.IsNull(anchor.GetComponent<BoxCollider>());
            Assert.AreSame(anchor, targetHat.GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
        }

        [Test]
        public void LeftOutComponentThatAnotherOneRequires_IsKeptAndReported()
        {
            var hatAsset = SavePrefab("Hat_LeftOutRequired", hat =>
            {
                // HingeJoint requires the Rigidbody next to it
                AddChild(hat, "Ribbon").gameObject.AddComponent<HingeJoint>();
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            Instantiate(hatAsset, source.Find("Armature/Head"));

            var plan = BuildPlanLeavingOut(source, target, typeof(HingeJoint), typeof(Rigidbody));
            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.FailedRemovals.Count);
            Assert.IsNotNull(target.Find("Armature/Head/Hat_LeftOutRequired/Ribbon").GetComponent<Rigidbody>());
            Assert.AreEqual(1, CopyVerifier.Verify(plan).Count(DiffKind.ExtraOnTarget));
        }

        [Test]
        public void LeavingOut_OnlyMattersInsideAPrefabThatIsAdded()
        {
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            source.Find("Armature/Head").gameObject.AddComponent<SphereCollider>();
            source.Find("Armature/Head").gameObject.AddComponent<BoxCollider>();

            var plan = BuildPlanLeavingOut(source, target, typeof(SphereCollider), typeof(BoxCollider));

            Assert.AreEqual(typeof(SphereCollider), plan.Components.Single().Entry.Type);
        }

        [Test]
        public void HeldBackComponent_IsRemovedFromTheInstantiatedPrefab()
        {
            var hatAsset = SavePrefab("Hat_HeldBack", hat =>
            {
                hat.gameObject.AddComponent<SphereCollider>();
                hat.gameObject.AddComponent<ParentConstraint>();
            });
            var source = CreateHierarchy("Source", "Armature/Head", "UnmappedAnchor");
            var target = CreateHierarchy("Target", "Armature/Head");
            // With a component of its own the anchor is not created bare, so a reference to it stays unresolved
            var anchor = source.Find("UnmappedAnchor");
            anchor.gameObject.AddComponent<BoxCollider>();
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var sourceConstraint = sourceHat.GetComponent<ParentConstraint>();
            sourceConstraint.weight = 0.3f;
            sourceConstraint.AddSource(new ConstraintSource { sourceTransform = anchor, weight = 1f });

            var settings = new CopySettings { UnresolvedPolicy = UnresolvedReferencePolicy.SkipComponent };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider), typeof(ParentConstraint));
            var hatToCreate = plan.ObjectsToCreate.Single(o => o.Source == sourceHat);
            Assert.IsTrue(hatToCreate.IsPrefabRoot, "The collider brings the prefab");
            var heldBack = plan.Components.Single(c => c.Entry.Type == typeof(ParentConstraint));
            Assert.IsTrue(heldBack.IsHeldBack);
            Assert.AreEqual(ComponentAction.Blocked, heldBack.Action);
            Assert.IsTrue(heldBack.LeftOut);
            Assert.IsTrue(heldBack.ArrivesWithPrefab);
            Assert.AreSame(hatToCreate, heldBack.HostToCreate);

            // Only the setting leaves it out: clearing the reference instead writes it like the rest of the prefab
            var clearing = BuildPlan(source, target,
                new CopySettings { UnresolvedPolicy = UnresolvedReferencePolicy.Clear },
                typeof(SphereCollider), typeof(ParentConstraint));
            var cleared = clearing.Components.Single(c => c.Entry.Type == typeof(ParentConstraint));
            Assert.IsFalse(cleared.LeftOut);
            Assert.IsTrue(cleared.WillWrite);
            Assert.AreEqual(ReferenceKind.InternalUnresolved, cleared.References.Single().Kind);

            var result = CopyExecutor.Execute(plan);

            // Left in place, it would carry the values of the asset, and the diff check would report it as missing
            Assert.IsNull(target.Find("Armature/Head/Hat_HeldBack").GetComponent<ParentConstraint>());
            Assert.AreEqual(1, result.LeftOutComponents);
            Assert.IsEmpty(result.FailedRemovals);
            Assert.That(CopyVerifier.Verify(plan).Components.Select(c => c.Kind), Is.All.EqualTo(DiffKind.Match));
        }

        [Test]
        public void HeldBackComponentThatAnotherOneRequires_IsKeptAndReported()
        {
            var hatAsset = SavePrefab("Hat_HeldBackRequired", hat =>
            {
                // Cloth requires the SkinnedMeshRenderer next to it
                hat.gameObject.AddComponent<SkinnedMeshRenderer>();
                hat.gameObject.AddComponent<Cloth>();
                hat.gameObject.AddComponent<SphereCollider>();
            });
            var source = CreateHierarchy("Source", "Armature/Head", "UnmappedAnchor");
            var target = CreateHierarchy("Target", "Armature/Head");
            var anchor = source.Find("UnmappedAnchor");
            anchor.gameObject.AddComponent<BoxCollider>();
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var sourceRenderer = sourceHat.GetComponent<SkinnedMeshRenderer>();
            sourceRenderer.rootBone = anchor;

            // The Cloth is not selected; it comes along with the prefab that the collider brings
            var settings = new CopySettings { UnresolvedPolicy = UnresolvedReferencePolicy.SkipComponent };
            var plan = CopyPlanBuilder.Build(
                SelectComponents(source, sourceHat.GetComponent<SphereCollider>(), sourceRenderer),
                TransformMapper.Build(source, target), settings);
            var hatToCreate = plan.ObjectsToCreate.Single(o => o.Source == sourceHat);
            Assert.IsTrue(hatToCreate.IsPrefabRoot);
            var heldBack = plan.Components.Single(c => c.Entry.Type == typeof(SkinnedMeshRenderer));
            Assert.IsTrue(heldBack.IsHeldBack);
            Assert.IsTrue(heldBack.LeftOut);
            Assert.AreSame(hatToCreate, heldBack.HostToCreate);
            Assert.IsTrue(plan.Components.Single(c => c.Entry.Type == typeof(Cloth)).Implicit);

            var result = CopyExecutor.Execute(plan);

            Assert.IsNotNull(target.Find("Armature/Head/Hat_HeldBackRequired").GetComponent<SkinnedMeshRenderer>());
            CollectionAssert.AreEqual(new[] { heldBack }, result.FailedRemovals);
            Assert.AreEqual(DiffKind.ExtraOnTarget,
                CopyVerifier.Verify(plan).Components.Single(c => c.Planned == heldBack).Kind);
        }

        #endregion

        #region Mirror copy

        /// <summary>An earring with a chain on each of its own sides, on the left hand of an outfit.</summary>
        private Transform CreateOutfitWithEarring(string assetName, out Transform outfit, out Transform earringL)
        {
            var asset = SavePrefab(assetName, earring =>
            {
                AddChild(earring, "Chain_L").localPosition = new Vector3(0.1f, 0f, 0f);
                AddChild(earring, "Chain_R").localPosition = new Vector3(-0.1f, 0f, 0f);
                foreach (Transform chain in earring) chain.gameObject.AddComponent<SphereCollider>();
            });
            var avatar = CreateHierarchy("Avatar", "Outfit/Hips/Hand_L", "Outfit/Hips/Hand_R");
            outfit = avatar.Find("Outfit");
            outfit.Find("Hips/Hand_L").localPosition = new Vector3(0.5f, 1f, 0f);
            outfit.Find("Hips/Hand_R").localPosition = new Vector3(-0.5f, 1f, 0f);
            earringL = Instantiate(asset, outfit.Find("Hips/Hand_L"));
            earringL.name = "Earring_L";
            earringL.localPosition = new Vector3(0f, 0.05f, 0.02f);
            return avatar;
        }

        [Test]
        public void MirrorCopy_OfAPrefabWithBothSidesInside_KeepsItsChildrenApart()
        {
            var avatar = CreateOutfitWithEarring("Earring_Mirror", out var outfit, out var earringL);

            var plan = CopyPlanBuilder.Build(Select(outfit, typeof(SphereCollider)), MirrorMapper.Build(avatar),
                new CopySettings(), mirrorRoot: avatar, keyRoot: outfit);
            CopyExecutor.Execute(plan);

            var earringR = outfit.Find("Hips/Hand_R/Earring_R");
            Assert.IsNotNull(earringR);
            Assert.AreEqual(2, earringR.childCount);
            // Each chain lands at the mirror image of its source, under the flipped name: a chain renamed early
            // must not be found again as its sibling
            var mirror = new MirrorContext(avatar);
            Assert.That((mirror.ReflectPoint(earringL.Find("Chain_L").position) - earringR.Find("Chain_R").position).magnitude,
                Is.LessThan(1e-4f));
            Assert.That((mirror.ReflectPoint(earringL.Find("Chain_R").position) - earringR.Find("Chain_L").position).magnitude,
                Is.LessThan(1e-4f));
            // The new names are overrides of the instance, or they would be gone once the scene is reloaded
            foreach (Transform chain in earringR)
            {
                using var chainObject = new SerializedObject(chain.gameObject);
                Assert.IsTrue(chainObject.FindProperty("m_Name").prefabOverride, chain.name);
            }
        }

        [Test]
        public void MirrorCopy_OntoAPrefabInstance_KeepsNoOverrideThatEqualsThePrefab()
        {
            // An avatar prefab with a constraint on each side, both following their own hand
            var asset = SavePrefab("Avatar_Overrides", avatar =>
            {
                var hips = AddChild(avatar, "Hips");
                foreach (var side in new[] { "L", "R" })
                {
                    var hand = AddChild(hips, "Hand_" + side);
                    hand.localPosition = new Vector3(side == "L" ? 0.5f : -0.5f, 1f, 0f);
                    AddChild(hand, "Item_" + side).gameObject.AddComponent<ParentConstraint>()
                        .AddSource(new ConstraintSource { sourceTransform = hand, weight = 1f });
                }
            });
            var avatarInstance = Instantiate(asset, CreateHierarchy("Scene"));
            avatarInstance.Find("Hips/Hand_L/Item_L").GetComponent<ParentConstraint>()
                .SetTranslationOffset(0, new Vector3(0f, 0.1f, 0f));

            var map = MirrorMapper.Build(avatarInstance);
            var left = Select(avatarInstance, typeof(ParentConstraint)).Where(e => map.Sides.Of(e.Host) == Side.Left);
            var plan = CopyPlanBuilder.Build(left, map, new CopySettings(), mirrorRoot: avatarInstance);
            Assert.AreEqual(ComponentAction.Overwrite, plan.Components.Single().Action);
            CopyExecutor.Execute(plan);

            // "Hand_L" is copied and then redirected to the "Hand_R" the prefab has anyway: no override
            using var copied = new SerializedObject(avatarInstance.Find("Hips/Hand_R/Item_R").GetComponent<ParentConstraint>());
            Assert.IsFalse(copied.FindProperty("m_Sources.Array.data[0].sourceTransform").prefabOverride, "source");
            Assert.IsTrue(copied.FindProperty("m_TranslationOffsets.Array.data[0]").prefabOverride, "offset");
        }

        [Test]
        public void MirrorCopy_KeysTheComponentsOfAnAddedPrefabLikeTheScannedSource()
        {
            var avatar = CreateOutfitWithEarring("Earring_MirrorKeys", out var outfit, out var earringL);
            var chainL = Select(outfit, typeof(SphereCollider)).Where(e => e.Host == earringL.Find("Chain_L"));

            var plan = CopyPlanBuilder.Build(chainL, MirrorMapper.Build(avatar), new CopySettings(),
                mirrorRoot: avatar, keyRoot: outfit);

            // The chain on the other side comes with the prefab, keyed from the outfit the window scanned
            var implicitChain = plan.Components.Single(c => c.Implicit);
            Assert.AreEqual("Hips/Hand_L/Earring_L/Chain_R", implicitChain.Entry.Key.RelativePath);
        }

        [Test]
        public void MissingPrefabs_WithinAScope_AreTheOnesAtOrBelowIt()
        {
            var avatar = CreateOutfitWithEarring("Earring_MirrorScope", out var outfit, out var earringL);
            var map = MirrorMapper.Build(avatar);

            CollectionAssert.AreEqual(new[] { earringL }, NestedPrefabs.FindMissingRoots(map, outfit));
            CollectionAssert.AreEqual(new[] { earringL }, NestedPrefabs.FindMissingRoots(map, earringL));
            // Inside a missing prefab, the prefab is what arrives, and it lies outside the scope
            CollectionAssert.IsEmpty(NestedPrefabs.FindMissingRoots(map, earringL.Find("Chain_L")));
        }

        #endregion
    }
}
