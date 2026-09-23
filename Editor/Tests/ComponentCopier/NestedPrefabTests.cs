using System;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// Nested prefabs (a prefab bundling PhysBone settings, a hat below the Head bone, ...) must be added to the
    /// target as prefab instances instead of being rebuilt object by object.
    /// </summary>
    public class NestedPrefabTests : ComponentCopierTestBase
    {
        private const string TempFolderName = "__ComponentCopierTests";
        private const string TempFolder = "Assets/" + TempFolderName;

        [OneTimeSetUp]
        public void CreateTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", TempFolderName);
        }

        // One-time, so that the assets outlive the instances destroyed in the per-test TearDown
        [OneTimeTearDown]
        public void DeleteTempFolder()
        {
            AssetDatabase.DeleteAsset(TempFolder);
        }

        private static GameObject SavePrefab(string name, Action<Transform> build)
        {
            var temp = new GameObject(name);
            try
            {
                build(temp.transform);
                return PrefabUtility.SaveAsPrefabAsset(temp, $"{TempFolder}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static Transform AddChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform Instantiate(GameObject asset, Transform parent)
        {
            return ((GameObject)PrefabUtility.InstantiatePrefab(asset, parent)).transform;
        }

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

            var plan = CopyPlanBuilder.Build(nothingSelected, map, new CopySettings(), new[] { sourceHat });
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
                Enumerable.Empty<ComponentEntry>(), map, new CopySettings(), new[] { sourceHat });

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
                Enumerable.Empty<ComponentEntry>(), map, new CopySettings(), new[] { sourceHat.Find("Ribbon_Inner") });
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
