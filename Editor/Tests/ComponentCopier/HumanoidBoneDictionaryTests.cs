using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class HumanoidBoneDictionaryTests
    {
        [TestCase("Hips", HumanBodyBones.Hips)]
        [TestCase("pelvis", HumanBodyBones.Hips)]
        [TestCase("Leg_L", HumanBodyBones.LeftUpperLeg)]
        [TestCase("UpLeg.R", HumanBodyBones.RightUpperLeg)]
        [TestCase("LeftLeg", HumanBodyBones.LeftLowerLeg)]
        [TestCase("Left leg", HumanBodyBones.LeftUpperLeg)]
        [TestCase("Index Proximal.L", HumanBodyBones.LeftIndexProximal)]
        [TestCase("f_index.01.L", HumanBodyBones.LeftIndexProximal)]
        [TestCase("LeftHandPinky1", HumanBodyBones.LeftLittleProximal)]
        [TestCase("Thunb1_R", HumanBodyBones.RightThumbProximal)]
        public void ExactNames_Resolve(string name, HumanBodyBones expected)
        {
            Assert.IsTrue(HumanoidBoneDictionary.TryFindBone(name, out var bone));
            Assert.AreEqual(expected, bone);
        }

        [TestCase("upper_leg.l", HumanBodyBones.LeftUpperLeg)]
        [TestCase("SHOULDER L", HumanBodyBones.LeftShoulder)]
        public void NotationDifferences_ResolveThroughNormalization(string name, HumanBodyBones expected)
        {
            Assert.IsTrue(HumanoidBoneDictionary.TryFindBone(name, out var bone));
            Assert.AreEqual(expected, bone);
        }

        [Test]
        public void NamesCollapsingIntoSeveralGroups_DoNotResolve()
        {
            // "Left leg" (UpperLeg) and "LeftLeg" (LowerLeg) both normalize to "leftleg"
            Assert.IsFalse(HumanoidBoneDictionary.TryFindBone("left_leg", out _));
        }

        [TestCase("Bust")]
        [TestCase("Skirt_F_01")]
        [TestCase("")]
        [TestCase(null)]
        public void NonHumanoidNames_DoNotResolve(string name)
        {
            Assert.IsFalse(HumanoidBoneDictionary.TryFindBone(name, out _));
        }
    }
}
