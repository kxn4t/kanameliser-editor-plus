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
    }
}
