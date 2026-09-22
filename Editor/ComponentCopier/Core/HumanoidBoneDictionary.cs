// The bone name list is ported from Merge Armatures Tool (core/bone_mappings.py) by kxn4t,
// which is derived from the following sources:
//
// 1. modular-avatar by bdunderscore
//    https://github.com/bdunderscore/modular-avatar/blob/80d17f82846266957324db71045ae4b68f742f0a/Editor/HeuristicBoneMapper.cs
//    Copyright (c) 2022 bdunderscore
//    Licensed under the MIT License
//
// 2. AvatarModifyTools by @HhotateA_xR
//    https://github.com/HhotateA/AvatarModifyTools/blob/d8ae75fed8577707253d6b63a64d6053eebbe78b/Assets/HhotateA/AvatarModifyTool/Editor/EnvironmentVariable.cs#L81-L139
//    Copyright (c) 2021 @HhotateA_xR
//    Licensed under the MIT License
//
// 3. BoneRenamer by Azukimochi
//    https://github.com/Azukimochi/BoneRenamer/blob/6ec12b848830f467e35ddf7ff105aaa72be02908/BoneNames.xml
//    Copyright (c) 2023 Azukimochi
//    Licensed under the MIT License

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Synonym dictionary that resolves humanoid bone names written in various naming conventions
    /// to <see cref="HumanBodyBones"/>.
    /// </summary>
    internal static class HumanoidBoneDictionary
    {
        // "Bust" is intentionally left out of the Chest group: it is just as often a breast
        // dynamics bone, and a wrong reference target is worse than an unresolved one.
        private static readonly (HumanBodyBones bone, string[] names)[] Groups =
        {
            (HumanBodyBones.Hips, new[] { "Hips", "Hip", "pelvis" }),
            (HumanBodyBones.Spine, new[] { "Spine", "spine01" }),
            (HumanBodyBones.Chest, new[] { "Chest", "spine02" }),
            (HumanBodyBones.UpperChest, new[] { "UpperChest", "UChest" }),
            (HumanBodyBones.Neck, new[] { "Neck" }),
            (HumanBodyBones.Head, new[] { "Head" }),
            (HumanBodyBones.Jaw, new[] { "Jaw" }),
            (HumanBodyBones.LeftEye, new[] { "LeftEye", "Eye_Left", "Eye_L" }),
            (HumanBodyBones.RightEye, new[] { "RightEye", "Eye_Right", "Eye_R" }),

            (HumanBodyBones.LeftUpperLeg, new[]
            {
                "LeftUpperLeg", "UpperLeg_Left", "UpperLeg_L", "Leg_Left", "Leg_L", "ULeg_L", "Left leg",
                "LeftUpLeg", "UpLeg.L", "Thigh_L"
            }),
            (HumanBodyBones.LeftLowerLeg, new[]
            {
                "LeftLowerLeg", "LowerLeg_Left", "LowerLeg_L", "Knee_Left", "Knee_L", "LLeg_L", "Left knee",
                "LeftLeg", "leg_L", "shin.L"
            }),
            (HumanBodyBones.LeftFoot, new[]
            {
                "LeftFoot", "Foot_Left", "Foot_L", "Ankle_L", "Foot.L.001", "Left ankle", "heel.L"
            }),
            (HumanBodyBones.LeftToes, new[]
            {
                "LeftToes", "Toes_Left", "Toe_Left", "ToeIK_L", "Toes_L", "Toe_L", "Foot.L.002", "Left Toe",
                "LeftToeBase"
            }),
            (HumanBodyBones.RightUpperLeg, new[]
            {
                "RightUpperLeg", "UpperLeg_Right", "UpperLeg_R", "Leg_Right", "Leg_R", "ULeg_R", "Right leg",
                "RightUpLeg", "UpLeg.R", "Thigh_R"
            }),
            (HumanBodyBones.RightLowerLeg, new[]
            {
                "RightLowerLeg", "LowerLeg_Right", "LowerLeg_R", "Knee_Right", "Knee_R", "LLeg_R", "Right knee",
                "RightLeg", "leg_R", "shin.R"
            }),
            (HumanBodyBones.RightFoot, new[]
            {
                "RightFoot", "Foot_Right", "Foot_R", "Ankle_R", "Foot.R.001", "Right ankle", "heel.R"
            }),
            (HumanBodyBones.RightToes, new[]
            {
                "RightToes", "Toes_Right", "Toe_Right", "ToeIK_R", "Toes_R", "Toe_R", "Foot.R.002", "Right Toe",
                "RightToeBase"
            }),

            (HumanBodyBones.LeftShoulder, new[] { "LeftShoulder", "Shoulder_Left", "Shoulder_L" }),
            (HumanBodyBones.LeftUpperArm, new[]
            {
                "LeftUpperArm", "UpperArm_Left", "UpperArm_L", "Arm_Left", "Arm_L", "UArm_L", "Left arm",
                "UpperLeftArm"
            }),
            (HumanBodyBones.LeftLowerArm, new[]
            {
                "LeftLowerArm", "LowerArm_Left", "LowerArm_L", "LArm_L", "Left elbow", "LeftForeArm", "Elbow_L",
                "forearm_L", "ForArm_L"
            }),
            (HumanBodyBones.LeftHand, new[] { "LeftHand", "Hand_Left", "Hand_L", "Left wrist", "Wrist_L" }),
            (HumanBodyBones.RightShoulder, new[] { "RightShoulder", "Shoulder_Right", "Shoulder_R" }),
            (HumanBodyBones.RightUpperArm, new[]
            {
                "RightUpperArm", "UpperArm_Right", "UpperArm_R", "Arm_Right", "Arm_R", "UArm_R", "Right arm",
                "UpperRightArm"
            }),
            (HumanBodyBones.RightLowerArm, new[]
            {
                "RightLowerArm", "LowerArm_Right", "LowerArm_R", "LArm_R", "Right elbow", "RightForeArm", "Elbow_R",
                "forearm_R", "ForArm_R"
            }),
            (HumanBodyBones.RightHand, new[] { "RightHand", "Hand_Right", "Hand_R", "Right wrist", "Wrist_R" }),

            (HumanBodyBones.LeftThumbProximal, Finger("Thumb", "Proximal", 1, "Left", "L", "finger01_01", null, "Thunb1")),
            (HumanBodyBones.LeftThumbIntermediate, Finger("Thumb", "Intermediate", 2, "Left", "L", "finger01_02", null, "Thunb2")),
            (HumanBodyBones.LeftThumbDistal, Finger("Thumb", "Distal", 3, "Left", "L", "finger01_03", null, "Thunb3")),
            (HumanBodyBones.LeftIndexProximal, Finger("Index", "Proximal", 1, "Left", "L", "finger02_01", "f_index.01")),
            (HumanBodyBones.LeftIndexIntermediate, Finger("Index", "Intermediate", 2, "Left", "L", "finger02_02", "f_index.02")),
            (HumanBodyBones.LeftIndexDistal, Finger("Index", "Distal", 3, "Left", "L", "finger02_03", "f_index.03")),
            (HumanBodyBones.LeftMiddleProximal, Finger("Middle", "Proximal", 1, "Left", "L", "finger03_01", "f_middle.01")),
            (HumanBodyBones.LeftMiddleIntermediate, Finger("Middle", "Intermediate", 2, "Left", "L", "finger03_02", "f_middle.02")),
            (HumanBodyBones.LeftMiddleDistal, Finger("Middle", "Distal", 3, "Left", "L", "finger03_03", "f_middle.03")),
            (HumanBodyBones.LeftRingProximal, Finger("Ring", "Proximal", 1, "Left", "L", "finger04_01", "f_ring.01")),
            (HumanBodyBones.LeftRingIntermediate, Finger("Ring", "Intermediate", 2, "Left", "L", "finger04_02", "f_ring.02")),
            (HumanBodyBones.LeftRingDistal, Finger("Ring", "Distal", 3, "Left", "L", "finger04_03", "f_ring.03")),
            (HumanBodyBones.LeftLittleProximal, Finger("Little", "Proximal", 1, "Left", "L", "finger05_01", "f_pinky.01", null, "Pinky")),
            (HumanBodyBones.LeftLittleIntermediate, Finger("Little", "Intermediate", 2, "Left", "L", "finger05_02", "f_pinky.02", null, "Pinky")),
            (HumanBodyBones.LeftLittleDistal, Finger("Little", "Distal", 3, "Left", "L", "finger05_03", "f_pinky.03", null, "Pinky")),

            (HumanBodyBones.RightThumbProximal, Finger("Thumb", "Proximal", 1, "Right", "R", "finger01_01", null, "Thunb1")),
            (HumanBodyBones.RightThumbIntermediate, Finger("Thumb", "Intermediate", 2, "Right", "R", "finger01_02", null, "Thunb2")),
            (HumanBodyBones.RightThumbDistal, Finger("Thumb", "Distal", 3, "Right", "R", "finger01_03", null, "Thunb3")),
            (HumanBodyBones.RightIndexProximal, Finger("Index", "Proximal", 1, "Right", "R", "finger02_01", "f_index.01")),
            (HumanBodyBones.RightIndexIntermediate, Finger("Index", "Intermediate", 2, "Right", "R", "finger02_02", "f_index.02")),
            (HumanBodyBones.RightIndexDistal, Finger("Index", "Distal", 3, "Right", "R", "finger02_03", "f_index.03")),
            (HumanBodyBones.RightMiddleProximal, Finger("Middle", "Proximal", 1, "Right", "R", "finger03_01", "f_middle.01")),
            (HumanBodyBones.RightMiddleIntermediate, Finger("Middle", "Intermediate", 2, "Right", "R", "finger03_02", "f_middle.02")),
            (HumanBodyBones.RightMiddleDistal, Finger("Middle", "Distal", 3, "Right", "R", "finger03_03", "f_middle.03")),
            (HumanBodyBones.RightRingProximal, Finger("Ring", "Proximal", 1, "Right", "R", "finger04_01", "f_ring.01")),
            (HumanBodyBones.RightRingIntermediate, Finger("Ring", "Intermediate", 2, "Right", "R", "finger04_02", "f_ring.02")),
            (HumanBodyBones.RightRingDistal, Finger("Ring", "Distal", 3, "Right", "R", "finger04_03", "f_ring.03")),
            (HumanBodyBones.RightLittleProximal, Finger("Little", "Proximal", 1, "Right", "R", "finger05_01", "f_pinky.01", null, "Pinky")),
            (HumanBodyBones.RightLittleIntermediate, Finger("Little", "Intermediate", 2, "Right", "R", "finger05_02", "f_pinky.02", null, "Pinky")),
            (HumanBodyBones.RightLittleDistal, Finger("Little", "Distal", 3, "Right", "R", "finger05_03", "f_pinky.03", null, "Pinky")),
        };

        private static readonly Dictionary<string, HumanBodyBones> ExactIndex = new();
        private static readonly Dictionary<string, HumanBodyBones> NormalizedIndex = new();

        static HumanoidBoneDictionary()
        {
            var ambiguous = new HashSet<string>();

            foreach (var (bone, names) in Groups)
            {
                foreach (var name in names)
                {
                    if (!ExactIndex.ContainsKey(name))
                        ExactIndex[name] = bone;

                    // Names from different groups can collapse into the same normalized key
                    // (e.g. "Left leg" (UpperLeg) and "LeftLeg" (LowerLeg) both become "leftleg").
                    // Resolving those first-come would produce wrong mappings, so they resolve to nothing.
                    var normalized = NormalizeName(name);
                    if (NormalizedIndex.TryGetValue(normalized, out var existing))
                    {
                        if (existing != bone) ambiguous.Add(normalized);
                    }
                    else
                    {
                        NormalizedIndex[normalized] = bone;
                    }
                }
            }

            foreach (var key in ambiguous)
                NormalizedIndex.Remove(key);
        }

        /// <summary>
        /// Absorbs notation differences: "Left.Hand", "Left Hand" and "left_hand" all become "lefthand".
        /// </summary>
        internal static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.ToLowerInvariant().Replace(".", "").Replace(" ", "").Replace("_", "");
        }

        /// <summary>
        /// Resolves a bone name to a humanoid bone. Exact dictionary entries win over normalized ones.
        /// </summary>
        internal static bool TryFindBone(string boneName, out HumanBodyBones bone)
        {
            bone = HumanBodyBones.LastBone;
            if (string.IsNullOrEmpty(boneName)) return false;

            if (ExactIndex.TryGetValue(boneName, out bone)) return true;
            return NormalizedIndex.TryGetValue(NormalizeName(boneName), out bone);
        }

        /// <summary>The same bone on the other side, e.g. LeftHand → RightHand. False on the middle line.</summary>
        internal static bool TryMirror(HumanBodyBones bone, out HumanBodyBones mirrored)
        {
            string name = bone.ToString();
            string other = name.StartsWith("Left", StringComparison.Ordinal) ? "Right" + name.Substring(4)
                : name.StartsWith("Right", StringComparison.Ordinal) ? "Left" + name.Substring(5)
                : null;

            if (other != null && Enum.TryParse(other, out mirrored)) return true;

            mirrored = bone;
            return false;
        }

        internal static Side SideOf(HumanBodyBones bone)
        {
            string name = bone.ToString();
            if (name.StartsWith("Left", StringComparison.Ordinal)) return Side.Left;
            if (name.StartsWith("Right", StringComparison.Ordinal)) return Side.Right;
            return Side.None;
        }

        private static string[] Finger(
            string finger, string segment, int number, string side, string sideShort,
            string fingerNumbered, string rigify, string typo = null, string alias = null)
        {
            var names = new List<string>
            {
                $"{side}{finger}{segment}",
                $"{segment}{finger}_{side}",
                $"{segment}{finger}_{sideShort}",
                $"{finger}{number}_{sideShort}",
                $"{finger}Finger{number}_{sideShort}",
                $"{side}Hand{alias ?? finger}{number}",
                $"{finger} {segment}.{sideShort}",
                $"{fingerNumbered}_{sideShort}",
            };

            if (rigify != null) names.Add($"{rigify}.{sideShort}");
            if (typo != null) names.Add($"{typo}_{sideShort}");

            return names.ToArray();
        }
    }
}
