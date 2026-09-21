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
        public void StructuralSuffixDifference_IsOnlySuggested()
        {
            var source = CreateHierarchy("Source", "Armature/Skirt_F.001");
            var target = CreateHierarchy("Target", "Armature/Skirt_F");

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("Armature/Skirt_F.001"));
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
        public void CommonSuffix_IsSuggestedAsAffixRule()
        {
            var source = CreateHierarchy("Source", "Root_Bone/Tail_A/Tail_B/Tail_C");
            var target = CreateHierarchy("Target", "Root_Bone_v2/Tail_A_v2/Tail_B_v2/Tail_C_v2");

            var map = TransformMapper.Build(source, target);

            var mapping = map.Get(source.Find("Root_Bone/Tail_A/Tail_B"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreEqual(MappingReason.AffixStripped, mapping.Reason);
            Assert.AreSame(target.Find("Root_Bone_v2/Tail_A_v2/Tail_B_v2"), mapping.Target);
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
