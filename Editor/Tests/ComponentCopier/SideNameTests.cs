using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class SideNameTests
    {
        [TestCase("Hand_L", "Hand_R")]
        [TestCase("Hand.R", "Hand.L")]
        [TestCase("hand-l", "hand-r")]
        [TestCase("L_Hand", "R_Hand")]
        [TestCase("r.hand", "l.hand")]
        [TestCase("LeftArm", "RightArm")]
        [TestCase("leftArm", "rightArm")]
        [TestCase("ArmLeft", "ArmRight")]
        [TestCase("left_arm", "right_arm")]
        [TestCase("LEFT_ARM", "RIGHT_ARM")]
        [TestCase("Right arm", "Left arm")]
        [TestCase("Skirt_L_01", "Skirt_R_01")]
        [TestCase("Skirt_Left_01", "Skirt_Right_01")]
        [TestCase("Foot.L.001", "Foot.R.001")]
        [TestCase("Hand_L (1)", "Hand_R (1)")]
        [TestCase("Left", "Right")]
        [TestCase("左手", "右手")]
        [TestCase("リボン右", "リボン左")]
        [TestCase("髪_左_01", "髪_右_01")]
        [TestCase("左耳.001", "右耳.001")]
        public void Marker_IsFlipped(string name, string expected)
        {
            Assert.IsTrue(SideName.TryFlip(name, out var flipped));
            Assert.AreEqual(expected, flipped);
        }

        [TestCase("Hand_L")]
        [TestCase("LeftArm")]
        [TestCase("Skirt_Left_01")]
        [TestCase("Foot.L.001")]
        public void FlippingTwice_RestoresTheName(string name)
        {
            Assert.IsTrue(SideName.TryFlip(name, out var flipped));
            Assert.IsTrue(SideName.TryFlip(flipped, out var restored));
            Assert.AreEqual(name, restored);
        }

        [TestCase("Hips")]
        [TestCase("Collider")]
        [TestCase("Cleft")]
        [TestCase("Leftarm")]
        [TestCase("LEFTARM")]
        [TestCase("Bell")]
        [TestCase("Skirt_01")]
        [TestCase("Hips.001")]
        [TestCase("A_L_B_R_C")]
        [TestCase("leftover")]
        [TestCase("左右対称")]
        [TestCase("髪飾り")]
        public void NamesWithoutMarker_AreUnchanged(string name)
        {
            Assert.IsFalse(SideName.TryFlip(name, out var flipped));
            Assert.AreEqual(name, flipped);
        }

        [TestCase("Hand_L", "Left")]
        [TestCase("RightArm", "Right")]
        [TestCase("Skirt_Left_01", "Left")]
        [TestCase("r.hand", "Right")]
        [TestCase("右手", "Right")]
        [TestCase("Hips", "None")]
        public void Side_IsReadFromTheMarker(string name, string expected)
        {
            // Compared as strings: the enum is internal, and NUnit needs the test method to be public
            Assert.AreEqual(expected, SideName.GetSide(name).ToString());
        }
    }
}
