using System.Collections.Generic;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class TransformMapperTests : ComponentCopierTestBase
    {
        private static void AssertConfirmed(
            TransformMap map, Transform source, Transform expectedTarget, MappingReason expectedReason)
        {
            var mapping = map.Get(source);
            Assert.IsNotNull(mapping, $"No mapping for {source.name}");
            Assert.AreEqual(MappingState.Confirmed, mapping.State, source.name);
            Assert.AreEqual(expectedReason, mapping.Reason, source.name);
            Assert.AreSame(expectedTarget, mapping.Target, source.name);
        }

        [Test]
        public void IdenticalHierarchies_MapByExactPath()
        {
            var source = CreateHierarchy("Costume_v1", "Armature/Hips/Skirt_F", "Body");
            var target = CreateHierarchy("Costume_v2", "Armature/Hips/Skirt_F", "Body");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source, target, MappingReason.Root);
            AssertConfirmed(map, source.Find("Armature/Hips/Skirt_F"), target.Find("Armature/Hips/Skirt_F"),
                MappingReason.ExactPath);
            AssertConfirmed(map, source.Find("Body"), target.Find("Body"), MappingReason.ExactPath);
        }

        [Test]
        public void SameNameSiblings_ArePairedInOrder()
        {
            var source = CreateHierarchy("Source", "Colliders/Collider", "Colliders/Collider");
            var target = CreateHierarchy("Target", "Colliders/Collider", "Colliders/Collider");

            var map = TransformMapper.Build(source, target);

            var sourceColliders = source.Find("Colliders");
            var targetColliders = target.Find("Colliders");
            Assert.AreSame(targetColliders.GetChild(0), map.Get(sourceColliders.GetChild(0)).Target);
            Assert.AreSame(targetColliders.GetChild(1), map.Get(sourceColliders.GetChild(1)).Target);
        }

        [Test]
        public void HumanoidBonesWithDifferentNamingConventions_MapThroughDictionary()
        {
            var source = CreateHierarchy("Source", "Armature/pelvis/Leg_L", "Armature/pelvis/Skirt");
            var target = CreateHierarchy("Target", "Armature/Hips/LeftUpperLeg", "Armature/Hips/Skirt");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source.Find("Armature/pelvis"), target.Find("Armature/Hips"),
                MappingReason.HumanoidDictionary);
            AssertConfirmed(map, source.Find("Armature/pelvis/Leg_L"), target.Find("Armature/Hips/LeftUpperLeg"),
                MappingReason.HumanoidDictionary);
            // Costume-specific bones resolve below the humanoid anchor
            AssertConfirmed(map, source.Find("Armature/pelvis/Skirt"), target.Find("Armature/Hips/Skirt"),
                MappingReason.ChildOfMappedParent);
        }

        [Test]
        public void UniqueNameUnderDifferentParent_IsConfirmed()
        {
            var source = CreateHierarchy("Source", "Old/Thing");
            var target = CreateHierarchy("Target", "New/Thing");

            var map = TransformMapper.Build(source, target);

            Assert.AreEqual(MappingState.Unmapped, map.Get(source.Find("Old")).State);
            AssertConfirmed(map, source.Find("Old/Thing"), target.Find("New/Thing"), MappingReason.UniqueName);
        }

        [Test]
        public void ExtraIntermediateObjectOnSource_ChildrenStillMapBelowTheSameParent()
        {
            var source = CreateHierarchy("Source", "Armature/Extra/Bone", "Other/Bone");
            var target = CreateHierarchy("Target", "Armature/Bone", "Other/Bone");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source.Find("Armature/Extra/Bone"), target.Find("Armature/Bone"),
                MappingReason.ChildOfMappedParent);
        }

        [Test]
        public void ChainNumberDifference_IsOnlySuggested()
        {
            // "_01" usually numbers the links of a chain, so it is not treated as a mere rename
            var source = CreateHierarchy("Source", "Armature/Skirt_F_01");
            var target = CreateHierarchy("Target", "Armature/Skirt_F");

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("Armature/Skirt_F_01"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreEqual(MappingReason.NormalizedName, mapping.Reason);
            Assert.AreSame(target.Find("Armature/Skirt_F"), mapping.Target);
            Assert.IsFalse(map.TryResolve(mapping.Source, out _), "Suggestions must not be used automatically");
        }

        [Test]
        public void FuzzyNames_AreListedAsCandidatesWithoutPreselection()
        {
            var source = CreateHierarchy("Source", "Armature/Hair_front");
            var target = CreateHierarchy("Target", "Armature/Hair_back");

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("Armature/Hair_front"));
            Assert.AreEqual(MappingState.Unmapped, mapping.State);
            Assert.IsNull(mapping.Target);
            Assert.AreSame(target.Find("Armature/Hair_back"), mapping.Candidates.Single().Target);
        }

        [Test]
        public void CommonSuffix_BelowMappedParent_IsConfirmed()
        {
            var source = CreateHierarchy("Source", "Root_Bone/Tail_A/Tail_B/Tail_C");
            var target = CreateHierarchy("Target", "Root_Bone_v2/Tail_A_v2/Tail_B_v2/Tail_C_v2");

            var map = TransformMapper.Build(source, target);

            // Every level is backed up by its already mapped parent, so the whole chain resolves by itself
            AssertConfirmed(map, source.Find("Root_Bone"), target.Find("Root_Bone_v2"), MappingReason.RenamedChild);
            AssertConfirmed(map, source.Find("Root_Bone/Tail_A/Tail_B/Tail_C"),
                target.Find("Root_Bone_v2/Tail_A_v2/Tail_B_v2/Tail_C_v2"), MappingReason.RenamedChild);
        }

        [Test]
        public void CommonSuffix_AwayFromMappedParent_IsOnlySuggested()
        {
            var source = CreateHierarchy("Source", "X/Tail_A/Tail_B/Tail_C");
            var target = CreateHierarchy("Target", "Y/Tail_A_v2/Tail_B_v2/Tail_C_v2");

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("X/Tail_A"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreEqual(MappingReason.AffixStripped, mapping.Reason);
            Assert.AreSame(target.Find("Y/Tail_A_v2"), mapping.Target);
        }

        [Test]
        public void RenamedArmature_IsConfirmedAndItsBonesFollow()
        {
            var source = CreateHierarchy("Source", "Armature/Hips/Spine");
            var target = CreateHierarchy("Target", "Armature.1/Hips/Spine");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source.Find("Armature"), target.Find("Armature.1"), MappingReason.RenamedChild);
            AssertConfirmed(map, source.Find("Armature/Hips/Spine"), target.Find("Armature.1/Hips/Spine"),
                MappingReason.ChildOfMappedParent);
        }

        [Test]
        public void RenameSuffixOnEveryBone_IsConfirmed()
        {
            var source = CreateHierarchy("Source", "Armature/Hips.001/Spine.001");
            var target = CreateHierarchy("Target", "Armature/Hips/Spine");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source.Find("Armature/Hips.001/Spine.001"), target.Find("Armature/Hips/Spine"),
                MappingReason.RenamedChild);
        }

        [Test]
        public void AmbiguousRename_IsNotConfirmed()
        {
            var source = CreateHierarchy("Source", "Armature/Skirt.001", "Armature/Skirt.002");
            var target = CreateHierarchy("Target", "Armature/Skirt");

            var map = TransformMapper.Build(source, target);

            Assert.IsFalse(map.TryResolve(source.Find("Armature/Skirt.001"), out _));
            Assert.IsFalse(map.TryResolve(source.Find("Armature/Skirt.002"), out _));
        }

        [TestCase("Armature.1", "Armature")]
        [TestCase("Hips.001", "Hips")]
        [TestCase("Collider (1)", "Collider")]
        [TestCase("Skirt_F_01", "Skirt_F_01")]
        [TestCase(".001", ".001")]
        public void StripRenameSuffix_RemovesOnlyRenameMarkers(string name, string expected)
        {
            Assert.AreEqual(expected, RenameSuffix.Strip(name));
        }

        [Test]
        public void MeshAndBoneSharingAName_AreMatchedWithinTheirOwnRegion()
        {
            var source = CreateHierarchy("Source", "Meshes/Skirt", "Armature/Hips/Skirt");
            var target = CreateHierarchy("Target", "Models/Skirt", "Armature/Hips/Skirt");
            AddSkinnedMesh(source.Find("Meshes/Skirt"), source.Find("Armature/Hips/Skirt"));
            AddSkinnedMesh(target.Find("Models/Skirt"), target.Find("Armature/Hips/Skirt"));

            var map = TransformMapper.Build(source, target);

            // Without regions "Skirt" exists twice on both sides and could not be confirmed
            AssertConfirmed(map, source.Find("Meshes/Skirt"), target.Find("Models/Skirt"), MappingReason.UniqueName);
            AssertConfirmed(map, source.Find("Armature/Hips/Skirt"), target.Find("Armature/Hips/Skirt"),
                MappingReason.ExactPath);
        }

        [Test]
        public void Bone_NeverMatchesAnObjectOutsideOfTheArmature()
        {
            var source = CreateHierarchy("Source", "Body", "Armature/Hips/Tail");
            var target = CreateHierarchy("Target", "Tail", "Armature/Hips");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Hips/Tail"));
            AddSkinnedMesh(target.Find("Tail"), target.Find("Armature/Hips"));

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("Armature/Hips/Tail"));
            Assert.AreEqual(MappingState.Unmapped, mapping.State);
            Assert.IsEmpty(mapping.Candidates);
        }

        [Test]
        public void SkeletonInfo_TreatsSkinningBonesAndTheirAncestorsAsBones()
        {
            var root = CreateHierarchy("Root", "Body", "Armature/Hips/Collider", "Armature/Hips/Leaf_end");
            AddSkinnedMesh(root.Find("Body"), root.Find("Armature/Hips"));

            var skeleton = SkeletonInfo.Analyze(root);

            Assert.IsTrue(skeleton.IsBone(root.Find("Armature")), "Ancestors of bones count as bones");
            Assert.IsTrue(skeleton.IsBone(root.Find("Armature/Hips")));
            Assert.IsFalse(skeleton.IsBone(root.Find("Armature/Hips/Collider")));
            Assert.IsTrue(skeleton.IsInArmature(root.Find("Armature/Hips/Collider")));
            Assert.IsFalse(skeleton.IsInArmature(root.Find("Body")));
        }

        [Test]
        public void ManualParentCorrection_ResolvesItsSubtree()
        {
            var source = CreateHierarchy("Source", "A/Leaf");
            var target = CreateHierarchy("Target", "B/Leaf", "C/Leaf");

            var automatic = TransformMapper.Build(source, target);
            Assert.AreEqual(MappingState.NeedsReview, automatic.Get(source.Find("A/Leaf")).State);
            Assert.AreEqual(MappingReason.AmbiguousName, automatic.Get(source.Find("A/Leaf")).Reason);

            var manual = new Dictionary<Transform, Transform> { { source.Find("A"), target.Find("C") } };
            var corrected = TransformMapper.Build(source, target, manual);

            Assert.AreEqual(MappingState.Manual, corrected.Get(source.Find("A")).State);
            AssertConfirmed(corrected, source.Find("A/Leaf"), target.Find("C/Leaf"),
                MappingReason.ChildOfMappedParent);
        }

        [Test]
        public void ManualNullTarget_MarksSourceAsHavingNoCounterpart()
        {
            var source = CreateHierarchy("Source", "Thing");
            var target = CreateHierarchy("Target", "Thing");

            var manual = new Dictionary<Transform, Transform> { { source.Find("Thing"), null } };
            var map = TransformMapper.Build(source, target, manual);

            Assert.AreEqual(MappingState.Manual, map.Get(source.Find("Thing")).State);
            Assert.IsFalse(map.TryResolve(source.Find("Thing"), out _));
        }

        [Test]
        public void ManualMapping_StillOffersWhatAutomaticWouldHaveChosen()
        {
            var source = CreateHierarchy("Source", "Thing", "Other");
            var target = CreateHierarchy("Target", "Thing", "Other");

            // "No counterpart" must stay reversible from the menu
            var manual = new Dictionary<Transform, Transform> { { source.Find("Thing"), null } };
            var map = TransformMapper.Build(source, target, manual);

            var mapping = map.Get(source.Find("Thing"));
            Assert.AreEqual(MappingState.Manual, mapping.State);
            Assert.IsNull(mapping.Target);
            Assert.AreEqual(new[] { target.Find("Thing") }, mapping.Candidates.Select(c => c.Target).ToArray());

            // Offering a target does not take it away from anything else
            Assert.IsTrue(map.TryResolve(source.Find("Other"), out var other));
            Assert.AreSame(target.Find("Other"), other);
        }

        [Test]
        public void ManualMapping_OffersSuggestionsToo()
        {
            var source = CreateHierarchy("Source", "Group/Item");
            var target = CreateHierarchy("Target", "A/Item", "B/Item");

            var manual = new Dictionary<Transform, Transform> { { source.Find("Group/Item"), null } };
            var map = TransformMapper.Build(source, target, manual);

            var candidates = map.Get(source.Find("Group/Item")).Candidates.Select(c => c.Target).ToList();
            CollectionAssert.AreEquivalent(new[] { target.Find("A/Item"), target.Find("B/Item") }, candidates);
        }

        [Test]
        public void TargetsAreNotAssignedTwice()
        {
            var source = CreateHierarchy("Source", "Armature/Hips", "Armature/pelvis");
            var target = CreateHierarchy("Target", "Armature/Hips");

            var map = TransformMapper.Build(source, target);

            AssertConfirmed(map, source.Find("Armature/Hips"), target.Find("Armature/Hips"),
                MappingReason.ExactPath);
            Assert.IsFalse(map.TryResolve(source.Find("Armature/pelvis"), out _));
        }
    }
}
