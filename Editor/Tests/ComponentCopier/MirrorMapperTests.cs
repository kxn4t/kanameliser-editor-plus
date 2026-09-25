using System.Collections.Generic;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class MirrorMapperTests : ComponentCopierTestBase
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
        public void SidedSiblings_PairBothWays()
        {
            var root = CreateHierarchy("Avatar", "Armature/Hips/Skirt_L", "Armature/Hips/Skirt_R");

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, root, root, MappingReason.Root);
            AssertConfirmed(map, root.Find("Armature"), root.Find("Armature"), MappingReason.SelfCenter);
            AssertConfirmed(map, root.Find("Armature/Hips"), root.Find("Armature/Hips"), MappingReason.SelfCenter);
            AssertConfirmed(map, root.Find("Armature/Hips/Skirt_L"), root.Find("Armature/Hips/Skirt_R"),
                MappingReason.MirroredName);
            AssertConfirmed(map, root.Find("Armature/Hips/Skirt_R"), root.Find("Armature/Hips/Skirt_L"),
                MappingReason.MirroredName);
        }

        [Test]
        public void ChildrenBelowSidedObjects_PairBelowTheCounterpart()
        {
            var root = CreateHierarchy("Avatar",
                "Armature/Hips/Skirt_L/Frill_L/Collider", "Armature/Hips/Skirt_R/Frill_R/Collider");

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, root.Find("Armature/Hips/Skirt_L/Frill_L"), root.Find("Armature/Hips/Skirt_R/Frill_R"),
                MappingReason.MirroredName);
            AssertConfirmed(map, root.Find("Armature/Hips/Skirt_L/Frill_L/Collider"),
                root.Find("Armature/Hips/Skirt_R/Frill_R/Collider"), MappingReason.ChildOfMappedParent);
        }

        [Test]
        public void SameNameSiblings_ArePairedInOrder()
        {
            var root = CreateHierarchy("Avatar", "Hips/Skirt_L", "Hips/Skirt_L", "Hips/Skirt_R", "Hips/Skirt_R");
            var hips = root.Find("Hips");

            var map = MirrorMapper.Build(root);

            Assert.AreSame(hips.GetChild(2), map.Get(hips.GetChild(0)).Target);
            Assert.AreSame(hips.GetChild(3), map.Get(hips.GetChild(1)).Target);
            Assert.AreSame(hips.GetChild(0), map.Get(hips.GetChild(2)).Target);
        }

        [Test]
        public void CreatedCounterparts_RemainExclusiveWhenSameNameObjectsAreCopiedOutOfOrder()
        {
            OtherSideCopy.ForgetPins();
            try
            {
                var root = CreateHierarchy("Avatar", "Hips/Hand_L/Collider", "Hips/Hand_L/Collider", "Hips/Hand_R");
                var left = root.Find("Hips/Hand_L");
                var right = root.Find("Hips/Hand_R");
                var first = left.GetChild(0);
                var second = left.GetChild(1);
                first.gameObject.AddComponent<SphereCollider>().radius = 1f;
                second.gameObject.AddComponent<SphereCollider>().radius = 2f;

                var secondCopy = new OtherSideCopy(second);
                CopyExecutor.Execute(secondCopy.Plan);
                secondCopy.KeepCreated(secondCopy.Plan);
                var secondCounterpart = right.GetChild(0);

                var reverseCopy = new OtherSideCopy(secondCounterpart);
                Assert.AreSame(second, reverseCopy.Counterpart,
                    "a copy back must use the original source, despite its different sibling index");
                Assert.IsTrue(reverseCopy.IsKept(secondCounterpart), "the reverse pair was not picked by hand");
                Assert.IsFalse(reverseCopy.HasManualMappings);
                reverseCopy.ResetMappings();
                Assert.IsTrue(reverseCopy.IsKept(secondCounterpart), "Auto preserves the reverse pair too");

                var firstCopy = new OtherSideCopy(first);
                Assert.IsNotNull(firstCopy.Created, "the first source must not overwrite the second one's counterpart");
                CopyExecutor.Execute(firstCopy.Plan);
                firstCopy.KeepCreated(firstCopy.Plan);
                var firstCounterpart = right.GetChild(1);

                Assert.AreEqual(2, right.childCount);
                Assert.AreEqual(2f, secondCounterpart.GetComponent<SphereCollider>().radius);
                Assert.AreEqual(1f, firstCounterpart.GetComponent<SphereCollider>().radius);
                OtherSideCopy.ReloadPins();
                Assert.AreSame(firstCounterpart, new OtherSideCopy(first).Counterpart);
                Assert.AreSame(secondCounterpart, new OtherSideCopy(second).Counterpart);
                Assert.AreSame(first, new OtherSideCopy(firstCounterpart).Counterpart);
                Assert.AreSame(second, new OtherSideCopy(secondCounterpart).Counterpart);
            }
            finally
            {
                OtherSideCopy.ForgetPins();
            }
        }

        [Test]
        public void RememberedPairs_StillAllowExplicitManualMappings()
        {
            var root = CreateHierarchy("Avatar", "Hips/Hand_L/Collider", "Hips/Hand_L/Collider", "Hips/Hand_R/Collider");
            var first = root.Find("Hips/Hand_L").GetChild(0);
            var second = root.Find("Hips/Hand_L").GetChild(1);
            var created = root.Find("Hips/Hand_R/Collider");
            var manual = new Dictionary<Transform, Transform>
            {
                [second] = created,
                [first] = created,
                [created] = first,
            };

            var map = MirrorMapper.Build(root, manual, new[] { second });

            Assert.AreSame(created, map.Get(first).Target, "manual choices may still share a target");
            Assert.AreEqual(MappingState.Manual, map.Get(first).State);
            Assert.AreSame(first, map.Get(created).Target, "a manual choice wins over the remembered reverse pair");
            Assert.AreEqual(MappingState.Manual, map.Get(created).State);
        }

        [Test]
        public void RememberedPair_CanBeReplacedFromTheCreatedSide()
        {
            OtherSideCopy.ForgetPins();
            try
            {
                var root = CreateHierarchy("Avatar", "Hips/Hand_L/Collider", "Hips/Hand_L/Collider", "Hips/Hand_R");
                var left = root.Find("Hips/Hand_L");
                var right = root.Find("Hips/Hand_R");
                var source = left.GetChild(1);
                var copy = new OtherSideCopy(source);
                CopyExecutor.Execute(copy.Plan);
                copy.KeepCreated(copy.Plan);
                var created = right.GetChild(0);

                var reverse = new OtherSideCopy(created);
                reverse.CreateNew();
                Assert.IsFalse(reverse.IsKept(source), "changing either end releases the old pair immediately");
                Assert.IsFalse(reverse.IsKept(created), "the reverse side no longer uses the pair either");
                Assert.IsNotNull(reverse.Created);
                reverse.Rescan();
                Assert.IsNotNull(reverse.Created, "the explicit choice survives a rescan");
                CopyExecutor.Execute(reverse.Plan);
                reverse.KeepCreated(reverse.Plan);
                var replacement = left.GetChild(2);

                OtherSideCopy.ReloadPins();
                Assert.AreSame(replacement, new OtherSideCopy(created).Counterpart);
                Assert.AreSame(created, new OtherSideCopy(replacement).Counterpart);
                Assert.IsNotNull(new OtherSideCopy(source).Created,
                    "the old source must not share the new pair's counterpart");
            }
            finally
            {
                OtherSideCopy.ForgetPins();
            }
        }

        [Test]
        public void HumanoidBonesWithoutMarker_PairThroughTheDictionary()
        {
            var root = CreateHierarchy("Avatar", "Armature/Hips/UpperLeftArm", "Armature/Hips/UpperRightArm", "Body");
            var hips = root.Find("Armature/Hips");
            var left = root.Find("Armature/Hips/UpperLeftArm");
            var right = root.Find("Armature/Hips/UpperRightArm");
            AddSkinnedMesh(root.Find("Body"), hips, left, right);

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, hips, hips, MappingReason.SelfCenter);
            AssertConfirmed(map, left, right, MappingReason.MirroredBone);
            AssertConfirmed(map, right, left, MappingReason.MirroredBone);
        }

        [Test]
        public void OutfitOnTheAvatar_PairsItsOwnBones()
        {
            // The outfit repeats the bone names of the avatar, so the dictionary alone sees two of each
            var root = CreateHierarchy("Avatar",
                "Armature/Hips/UpperLeg_L", "Armature/Hips/UpperLeg_R",
                "Armature/Hips/UpperLeftArm", "Armature/Hips/UpperRightArm",
                "Outfit/Armature/Hips/UpperLeg_L/Frill", "Outfit/Armature/Hips/UpperLeg_R/Frill",
                "Outfit/Armature/Hips/UpperLeftArm", "Outfit/Armature/Hips/UpperRightArm",
                "Body", "Outfit/Mesh");
            var avatarHips = root.Find("Armature/Hips");
            var outfitHips = root.Find("Outfit/Armature/Hips");
            AddSkinnedMesh(root.Find("Body"), avatarHips, avatarHips.Find("UpperLeg_L"), avatarHips.Find("UpperLeg_R"),
                avatarHips.Find("UpperLeftArm"), avatarHips.Find("UpperRightArm"));
            AddSkinnedMesh(root.Find("Outfit/Mesh"), outfitHips, outfitHips.Find("UpperLeg_L"),
                outfitHips.Find("UpperLeg_R"), outfitHips.Find("UpperLeftArm"), outfitHips.Find("UpperRightArm"));

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, outfitHips.Find("UpperLeg_L"), outfitHips.Find("UpperLeg_R"), MappingReason.MirroredName);
            AssertConfirmed(map, outfitHips.Find("UpperLeg_L/Frill"), outfitHips.Find("UpperLeg_R/Frill"),
                MappingReason.ChildOfMappedParent);
            AssertConfirmed(map, outfitHips.Find("UpperLeftArm"), outfitHips.Find("UpperRightArm"),
                MappingReason.MirroredBone);
            AssertConfirmed(map, avatarHips.Find("UpperLeftArm"), avatarHips.Find("UpperRightArm"),
                MappingReason.MirroredBone);
        }

        [Test]
        public void OneSidedOutfitBone_OnlySuggestsTheBoneOfTheAvatar()
        {
            var root = CreateHierarchy("Avatar",
                "Armature/Hips/UpperLeftArm", "Armature/Hips/UpperRightArm", "Glove/Armature/Hips/UpperLeftArm",
                "Body", "Glove/Mesh");
            AddSkinnedMesh(root.Find("Body"), root.Find("Armature/Hips"), root.Find("Armature/Hips/UpperLeftArm"),
                root.Find("Armature/Hips/UpperRightArm"));
            AddSkinnedMesh(root.Find("Glove/Mesh"), root.Find("Glove/Armature/Hips"),
                root.Find("Glove/Armature/Hips/UpperLeftArm"));

            var map = MirrorMapper.Build(root);

            var mapping = map.Get(root.Find("Glove/Armature/Hips/UpperLeftArm"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreSame(root.Find("Armature/Hips/UpperRightArm"), mapping.Target);
        }

        [Test]
        public void PlainNameBelowAnUnmappedParent_IsOnlySuggested()
        {
            var root = CreateHierarchy("Avatar", "Hips/Pouch_L/Strap", "Hips/Strap");

            var map = MirrorMapper.Build(root);

            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Pouch_L")).State);
            var mapping = map.Get(root.Find("Hips/Pouch_L/Strap"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreSame(root.Find("Hips/Strap"), mapping.Target);
        }

        [Test]
        public void FlippedSiblingBelowAnUnmappedParent_IsOnlySuggested()
        {
            var root = CreateHierarchy("Avatar", "Hips/Pouch_L/Strap_L", "Hips/Strap_R");

            var map = MirrorMapper.Build(root);

            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Pouch_L")).State);
            var mapping = map.Get(root.Find("Hips/Pouch_L/Strap_L"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreSame(root.Find("Hips/Strap_R"), mapping.Target);
        }

        [Test]
        public void FlippedNameInTheSameUnmappedBranch_IsNoCounterpart()
        {
            // An earring with chains of its own on both sides, and no earring on the other hand yet
            var root = CreateHierarchy("Avatar",
                "Hips/Hand_L/Earring_L/Chain_L", "Hips/Hand_L/Earring_L/Chain_R", "Hips/Hand_R");

            var map = MirrorMapper.Build(root);

            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Hand_L/Earring_L")).State);
            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Hand_L/Earring_L/Chain_L")).State);
            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Hand_L/Earring_L/Chain_R")).State);
        }

        [Test]
        public void ChildMissingBelowTheCounterpart_IsUnmapped()
        {
            var root = CreateHierarchy("Avatar", "Hips/Skirt_L/Frill", "Hips/Skirt_R");

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, root.Find("Hips/Skirt_L"), root.Find("Hips/Skirt_R"), MappingReason.MirroredName);
            // The frill is not on the middle line just because its own name carries no marker
            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Skirt_L/Frill")).State);
        }

        [Test]
        public void ObjectWithoutAnyCounterpart_IsUnmapped()
        {
            var root = CreateHierarchy("Avatar", "Hips/Skirt_L", "Hips/Belt");

            var map = MirrorMapper.Build(root);

            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Skirt_L")).State);
            AssertConfirmed(map, root.Find("Hips/Belt"), root.Find("Hips/Belt"), MappingReason.SelfCenter);
        }

        [Test]
        public void FlippedNameElsewhere_IsConfirmedWhenUnique()
        {
            var root = CreateHierarchy("Avatar", "Body/Ear_L", "Head/Ear_R");

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, root.Find("Body/Ear_L"), root.Find("Head/Ear_R"), MappingReason.MirroredName);
        }

        [Test]
        public void FlippedNameElsewhere_IsSuggestedWhenTheSourceNameIsNotUnique()
        {
            // The ribbon of the hat has no other side; the right ribbon belongs to the skirt's left one
            var root = CreateHierarchy("Avatar", "Skirt/Ribbon_L", "Skirt/Ribbon_R", "Hat/Ribbon_L");

            var map = MirrorMapper.Build(root);

            AssertConfirmed(map, root.Find("Skirt/Ribbon_L"), root.Find("Skirt/Ribbon_R"), MappingReason.MirroredName);
            var mapping = map.Get(root.Find("Hat/Ribbon_L"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreSame(root.Find("Skirt/Ribbon_R"), mapping.Target);
        }

        [Test]
        public void FlippedNameElsewhere_IsSuggestedWhenAmbiguous()
        {
            var root = CreateHierarchy("Avatar", "A/Skirt_L", "B/Skirt_R", "C/Skirt_R");

            var map = MirrorMapper.Build(root);

            var mapping = map.Get(root.Find("A/Skirt_L"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreEqual(MappingReason.MirroredName, mapping.Reason);
            CollectionAssert.AreEquivalent(
                new[] { root.Find("B/Skirt_R"), root.Find("C/Skirt_R") },
                mapping.Candidates.Select(c => c.Target).ToList());
        }

        [Test]
        public void ManualMapping_Wins_AndKeepsTheAutomaticAnswerAsCandidate()
        {
            var root = CreateHierarchy("Avatar", "Hips/Skirt_L", "Hips/Skirt_R", "Hips/Other");
            var manual = new Dictionary<Transform, Transform>
            {
                [root.Find("Hips/Skirt_L")] = root.Find("Hips/Other"),
            };

            var map = MirrorMapper.Build(root, manual);

            var mapping = map.Get(root.Find("Hips/Skirt_L"));
            Assert.AreEqual(MappingState.Manual, mapping.State);
            Assert.AreSame(root.Find("Hips/Other"), mapping.Target);
            Assert.AreSame(root.Find("Hips/Skirt_R"), mapping.Candidates.Single().Target);
        }

        [Test]
        public void Sides_UseTheOutermostMarkedAncestor()
        {
            var root = CreateHierarchy("Avatar",
                "Armature/Hips/Hand_L/Collider", "Armature/Hips/UpperRightArm", "Armature/Hips/Spine",
                "Armature/Head/Earring_L/Chain_L", "Armature/Head/Earring_L/Chain_R",
                "Armature/Head/Earring_R/Chain_L", "Armature/Head/Earring_R/Chain_R");
            var sides = new MirrorSides(root);

            Assert.AreEqual(Side.Left, sides.Of(root.Find("Armature/Hips/Hand_L/Collider")));
            Assert.AreEqual(Side.Right, sides.Of(root.Find("Armature/Hips/UpperRightArm")));
            Assert.AreEqual(Side.None, sides.Of(root.Find("Armature/Hips/Spine")));
            Assert.AreEqual(Side.None, sides.Of(root));
            // The chains of an earring go with the earring, whatever side of it they hang on
            Assert.AreEqual(Side.Left, sides.Of(root.Find("Armature/Head/Earring_L/Chain_R")));
            Assert.AreEqual(Side.Right, sides.Of(root.Find("Armature/Head/Earring_R/Chain_L")));
        }

        [Test]
        public void Sides_ReadHumanoidNamesInTheArmatureOnly()
        {
            var root = CreateHierarchy("Avatar", "Armature/Hips/UpperLeftArm", "Body", "Accessories/UpperLeftArm");
            var bone = root.Find("Armature/Hips/UpperLeftArm");
            var accessory = root.Find("Accessories/UpperLeftArm");
            AddSkinnedMesh(root.Find("Body"), root.Find("Armature/Hips"), bone);
            var map = MirrorMapper.Build(root);

            Assert.AreEqual(Side.Left, map.Sides.Of(bone));
            // The map pairs it with itself like any object without a marker, so it is on the middle line
            Assert.AreEqual(Side.None, map.Sides.Of(accessory));
            AssertConfirmed(map, accessory, accessory, MappingReason.SelfCenter);
        }

        [Test]
        public void FlippedNameOnTheSameSide_IsNoCounterpart()
        {
            // The left hand holds a chain on each of its own sides, the right hand holds none
            var root = CreateHierarchy("Avatar", "Hips/Hand_L/Chain_L", "Hips/Hand_L/Chain_R", "Hips/Hand_R");

            var map = MirrorMapper.Build(root);

            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Hand_L/Chain_L")).State);
            Assert.AreEqual(MappingState.Unmapped, map.Get(root.Find("Hips/Hand_L/Chain_R")).State);
        }

        [Test]
        public void FlippedNameOnTheOtherSide_IsOnlySuggested_WhenTheSameSideHasItToo()
        {
            // Of the two "Chain_R", the one of the hat is on the other side, but the name is not unique
            var root = CreateHierarchy("Avatar",
                "Hips/Hand_L/Chain_L", "Hips/Hand_L/Chain_R", "Hips/Hand_R", "Hips/Hat/Chain_R");

            var map = MirrorMapper.Build(root);

            var mapping = map.Get(root.Find("Hips/Hand_L/Chain_L"));
            Assert.AreEqual(MappingState.NeedsReview, mapping.State);
            Assert.AreSame(root.Find("Hips/Hat/Chain_R"), mapping.Target);
        }
    }
}
