using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// "Copy to Other Side": one object, its pose and its components, mirrored onto its counterpart. Unity's colliders
    /// and constraints stand in for the VRChat components, as in <see cref="MirrorCopyTests"/>.
    /// </summary>
    public class OtherSideCopyTests : ComponentCopierTestBase
    {
        private const float Tolerance = 1e-4f;

        // The pins of what an apply created are shared by every copy, and kept in the session state
        [SetUp]
        [TearDown]
        public void ForgetPins() => OtherSideCopy.ForgetPins();

        private static void AssertClose(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(Tolerance),
                $"{message} expected {expected} but was {actual}");
        }

        private static void AssertSameRotation(Quaternion expected, Quaternion actual, string message = null)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.01f),
                $"{message} expected {expected.eulerAngles} but was {actual.eulerAngles}");
        }

        private static (float, float, float) Components(Vector3 v) => (v.x, v.y, v.z);

        /// <summary>
        /// Two hands whose frames are mirror images of each other, each with the given objects below it. The objects
        /// on the right start out at the hand's origin, away from the mirror image of the left ones.
        /// </summary>
        private Transform CreateAvatar(out Transform handL, out Transform handR, params string[] paths)
        {
            var root = CreateHierarchy("Avatar", new[] { "Hips/Hand_L", "Hips/Hand_R" }.Concat(paths).ToArray());
            handL = root.Find("Hips/Hand_L");
            handR = root.Find("Hips/Hand_R");
            handL.localPosition = new Vector3(0.5f, 1f, 0.1f);
            handL.localRotation = Quaternion.Euler(10f, 20f, -30f);
            handR.localPosition = new Vector3(-0.5f, 1f, 0.1f);
            handR.localRotation = Quaternion.Euler(10f, -20f, 30f);
            return root;
        }

        private static void Pose(Transform transform, Vector3 position, Vector3 euler)
        {
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(euler);
        }

        [Test]
        public void Pose_MovesTheCounterpartToTheMirrorImage_AndUndoMovesItBack()
        {
            var root = CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            colliderL.localScale = new Vector3(1f, 2f, 1f);

            var copy = new OtherSideCopy(colliderL);
            Assert.IsNull(copy.Problem);
            Assert.AreEqual(Side.Left, copy.Side);
            Assert.AreSame(colliderR, copy.Counterpart);
            Assert.AreEqual(ComponentAction.Overwrite, copy.Pose.Action);
            Assert.IsTrue(copy.HasWork);

            var result = CopyExecutor.Execute(copy.Plan);

            Assert.AreEqual(1, result.PosedObjects);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(colliderL.position), colliderR.position, "position");
            AssertSameRotation(mirror.ReflectRotation(colliderL.rotation), colliderR.rotation, "rotation");
            AssertClose(colliderL.localScale, colliderR.localScale, "scale");
            // Hands that mirror each other keep the local values up to the sign of x, without float noise
            Assert.AreEqual((-0.05f, 0.1f, -0.02f), Components(colliderR.localPosition));

            Undo.PerformUndo();

            AssertClose(Vector3.zero, colliderR.localPosition, "after Undo");
            AssertClose(Vector3.one, colliderR.localScale, "scale after Undo");
        }

        [Test]
        public void Pose_AtTheMirrorImageAlready_IsIdentical_AndLeavesNothingToDo()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            CopyExecutor.Execute(new OtherSideCopy(colliderL).Plan);

            var again = new OtherSideCopy(colliderL);

            Assert.AreEqual(ComponentAction.SkipIdentical, again.Pose.Action);
            Assert.IsFalse(again.HasWork);
        }

        [Test]
        public void Pose_BackAtThePrefabValueAlongOneAxis_LeavesNoOverrideOnThatAxis()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Charm_L/Collider_L");
            var asset = SavePrefab("Charm_R", charm => AddChild(charm, "Collider_R"));
            var colliderR = Instantiate(asset, handR).Find("Collider_R");
            // Nudged along y before, which overrides y alone
            colliderR.localPosition = new Vector3(0f, 0.3f, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(colliderR);
            Assert.IsTrue(IsOverridden(colliderR, "m_LocalPosition.y"));
            Assert.IsFalse(IsOverridden(colliderR, "m_LocalPosition.x"));

            // The mirror image is back at the prefab's y, but off along x
            var colliderL = handL.Find("Charm_L/Collider_L");
            colliderL.localPosition = new Vector3(0.05f, 0f, 0f);
            CopyExecutor.Execute(new OtherSideCopy(colliderL).Plan);

            Assert.AreEqual((-0.05f, 0f, 0f), Components(colliderR.localPosition));
            Assert.IsTrue(IsOverridden(colliderR, "m_LocalPosition.x"));
            Assert.IsFalse(IsOverridden(colliderR, "m_LocalPosition.y"), "back at the value of the prefab");
        }

        private static bool IsOverridden(Transform transform, string propertyPath)
        {
            using var serialized = new SerializedObject(transform);
            return serialized.FindProperty(propertyPath).prefabOverride;
        }

        [Test]
        public void FrameLossyScale_IsThatOfTheTransform_BelowMirroredAndTurnedParents()
        {
            var root = CreateHierarchy("Root", "A/B/C");
            var a = root.Find("A");
            var b = root.Find("A/B");
            var c = root.Find("A/B/C");
            a.localScale = new Vector3(1f, 1f, -1f);
            b.localRotation = Quaternion.Euler(0f, 0f, 30f);
            b.localScale = new Vector3(2f, 1f, 1f);
            c.localRotation = Quaternion.Euler(20f, 40f, 0f);
            c.localScale = new Vector3(1f, -1f, 3f);

            foreach (var transform in new[] { a, b, c })
                AssertClose(transform.lossyScale, Frame.Of(transform).LossyScale, transform.name);

            // Composed the way a pose of the plan is. Below a mirrored parent Unity mirrors the rotation of the child
            // as well, which Frame.Child does not (see there).
            a.localScale = Vector3.one;
            AssertClose(c.lossyScale, Frame.Of(b).Child(c.localPosition, c.localRotation, c.localScale).LossyScale,
                "child frame");
        }

        [Test]
        public void Components_AreMirroredInTheFrameTheCounterpartIsMovedTo()
        {
            var root = CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            // Nowhere near the mirror image: values mirrored into this frame would be off once it moves
            Pose(colliderR, new Vector3(0.3f, -0.2f, 0.1f), new Vector3(-40f, 70f, 10f));
            var sphereL = colliderL.gameObject.AddComponent<SphereCollider>();
            sphereL.center = new Vector3(0.01f, 0.02f, 0.03f);

            CopyExecutor.Execute(new OtherSideCopy(colliderL).Plan);

            var sphereR = colliderR.GetComponent<SphereCollider>();
            Assert.IsNotNull(sphereR);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(colliderL.TransformPoint(sphereL.center)),
                colliderR.TransformPoint(sphereR.center), "sphere center");
            // What was planned is what the other side has now
            Assert.IsFalse(new OtherSideCopy(colliderL).HasWork);
        }

        [Test]
        public void Components_ReferringBelowTheMovedCounterpart_UseWhereThatEndsUp()
        {
            var root = CreateAvatar(out var handL, out var handR,
                "Hips/Hand_L/Collider_L/Tip_L", "Hips/Hand_R/Collider_R/Tip_R");
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            var tipL = colliderL.Find("Tip_L");
            var tipR = colliderR.Find("Tip_R");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            Pose(colliderR, new Vector3(0.3f, -0.2f, 0.1f), new Vector3(-40f, 70f, 10f));
            tipL.localPosition = new Vector3(0f, 0.1f, 0f);
            tipR.localPosition = new Vector3(0f, 0.1f, 0f);
            var constraint = colliderL.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = tipL, weight = 1f });
            var offset = new Vector3(0.02f, 0.03f, 0.04f);
            constraint.SetTranslationOffset(0, offset);

            CopyExecutor.Execute(new OtherSideCopy(colliderL).Plan);

            var copied = colliderR.GetComponent<ParentConstraint>();
            Assert.AreSame(tipR, copied.GetSource(0).sourceTransform);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(tipL.TransformPoint(offset)), tipR.TransformPoint(copied.GetTranslationOffset(0)),
                "offset in the space of the source");
            Assert.IsFalse(new OtherSideCopy(colliderL).HasWork);
        }

        [Test]
        public void MissingCounterpart_IsCreatedBelowTheOtherSide_WithTheFlippedName()
        {
            var root = CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L");
            var colliderL = handL.Find("Collider_L");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            var sphereL = colliderL.gameObject.AddComponent<SphereCollider>();
            sphereL.center = new Vector3(0.01f, 0.02f, 0.03f);

            var copy = new OtherSideCopy(colliderL);
            Assert.IsFalse(copy.CounterpartExists);
            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreEqual("Hips/Hand_R/Collider_R", copy.CreatedPath());

            CopyExecutor.Execute(copy.Plan);

            var colliderR = handR.Find("Collider_R");
            Assert.IsNotNull(colliderR);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(colliderL.position), colliderR.position, "position");
            AssertSameRotation(mirror.ReflectRotation(colliderL.rotation), colliderR.rotation, "rotation");
            AssertClose(mirror.ReflectPoint(colliderL.TransformPoint(sphereL.center)),
                colliderR.TransformPoint(colliderR.GetComponent<SphereCollider>().center), "sphere center");
        }

        [Test]
        public void MissingCounterpart_WithoutComponents_IsCreatedForThePose()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Anchor_L");
            var anchorL = handL.Find("Anchor_L");
            anchorL.localPosition = new Vector3(0.05f, 0.1f, -0.02f);

            var copy = new OtherSideCopy(anchorL);
            // Unchecking the Transform does not stop a counterpart that is created from being placed
            copy.SetCopyPose(false);
            Assert.IsTrue(copy.HasWork);

            CopyExecutor.Execute(copy.Plan);

            Assert.AreEqual((-0.05f, 0.1f, -0.02f), Components(handR.Find("Anchor_R").localPosition));
        }

        [Test]
        public void MissingCounterpart_WithCreationOff_IsBlocked()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L");
            var colliderL = handL.Find("Collider_L");
            colliderL.gameObject.AddComponent<SphereCollider>();

            var copy = new OtherSideCopy(colliderL);
            copy.SetCreateMissing(false);

            Assert.AreEqual(BlockReason.HostUnmapped, copy.BlockReason);
            Assert.AreEqual(ComponentAction.Blocked, copy.Pose.Action);
            Assert.IsTrue(copy.Plan.Components.All(c => c.Action == ComponentAction.Blocked));
            Assert.IsFalse(copy.HasWork);
        }

        [Test]
        public void PoseOff_CopiesTheComponents_AndLeavesTheCounterpartWhereItIs()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            var rest = new Vector3(0.3f, -0.2f, 0.1f);
            colliderR.localPosition = rest;
            colliderL.gameObject.AddComponent<SphereCollider>();

            var copy = new OtherSideCopy(colliderL);
            copy.SetCopyPose(false);
            Assert.IsNull(copy.Pose);

            CopyExecutor.Execute(copy.Plan);

            Assert.IsNotNull(colliderR.GetComponent<SphereCollider>());
            AssertClose(rest, colliderR.localPosition);
        }

        [Test]
        public void ObjectsOnTheMiddleLine_HaveNoOtherSide()
        {
            var root = CreateAvatar(out _, out _);

            foreach (var middle in new[] { root, root.Find("Hips") })
            {
                var copy = new OtherSideCopy(middle);
                Assert.AreEqual("componentCopier.otherSide.noSide", copy.Problem, middle.name);
                Assert.IsNull(copy.Plan, middle.name);
            }
        }

        [Test]
        public void Counterpart_PickedByHand_IsUsed_UntilReset()
        {
            CreateAvatar(out var handL, out var handR,
                "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R", "Hips/Hand_R/Other_R");
            var colliderL = handL.Find("Collider_L");
            colliderL.localPosition = new Vector3(0.05f, 0.1f, -0.02f);
            var otherR = handR.Find("Other_R");

            var copy = new OtherSideCopy(colliderL);
            copy.SetCounterpart(otherR);

            Assert.IsTrue(copy.HasManualMappings);
            Assert.AreSame(otherR, copy.Counterpart);
            Assert.AreSame(otherR, copy.Pose.Target);

            copy.ResetMappings();

            Assert.IsFalse(copy.HasManualMappings);
            Assert.AreSame(handR.Find("Collider_R"), copy.Pose.Target);
        }

        [Test]
        public void Counterpart_SetToNone_IsCreatedNextToTheExistingOne()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var copy = new OtherSideCopy(handL.Find("Collider_L"));

            copy.SetCounterpart(null);

            Assert.IsFalse(copy.CounterpartExists);
            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreEqual("Collider_R", copy.Created.Name);
        }

        [Test]
        public void Counterpart_OnTheSameSide_IsBlocked()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L", "Hips/Hand_L/Other_L");
            var colliderL = handL.Find("Collider_L");
            colliderL.gameObject.AddComponent<SphereCollider>();
            var copy = new OtherSideCopy(colliderL);

            foreach (var sameSide in new[] { handL.Find("Other_L"), colliderL })
            {
                copy.SetCounterpart(sameSide);

                Assert.AreEqual(BlockReason.SameSide, copy.BlockReason, sameSide.name);
                Assert.IsFalse(copy.HasWork, sameSide.name);
            }
        }

        [Test]
        public void Selection_FollowsTheDefaultRule_OrTheComponentAskedFrom()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L");
            var colliderL = handL.Find("Collider_L");
            var sphere = colliderL.gameObject.AddComponent<SphereCollider>();
            var renderer = colliderL.gameObject.AddComponent<MeshRenderer>();

            var byDefault = new OtherSideCopy(colliderL);
            var asked = new OtherSideCopy(colliderL, renderer);
            var fromTransform = new OtherSideCopy(colliderL, colliderL);

            ComponentEntry Entry(OtherSideCopy copy, Component component) =>
                copy.Entries.Single(e => e.Component == component);
            Assert.IsTrue(byDefault.IsChecked(Entry(byDefault, sphere)));
            Assert.IsFalse(byDefault.IsChecked(Entry(byDefault, renderer)));
            // The rest of the object, pose included, may differ between the sides on purpose
            Assert.IsFalse(asked.IsChecked(Entry(asked, sphere)));
            Assert.IsTrue(asked.IsChecked(Entry(asked, renderer)));
            Assert.IsFalse(asked.CopyPose);
            Assert.IsTrue(byDefault.CopyPose);
            Assert.IsFalse(fromTransform.IsChecked(Entry(fromTransform, sphere)));
            Assert.IsTrue(fromTransform.CopyPose);
        }

        [Test]
        public void SuggestionOnTheMiddleLine_CannotBeConfirmed_ButANewOneCanBeCreated()
        {
            // No right pouch: the strap of the left one is suggested the belt's strap, which is on no side
            var root = CreateAvatar(out _, out _, "Hips/Pouch_L/Strap", "Hips/Strap");
            var copy = new OtherSideCopy(root.Find("Hips/Pouch_L/Strap"));

            Assert.IsNull(copy.Suggestion);
            Assert.AreSame(root.Find("Hips/Strap"), copy.UnusableSuggestion);
            Assert.IsNull(copy.Counterpart);
            Assert.AreEqual(BlockReason.HostNeedsReview, copy.BlockReason);

            copy.SetCreateMissing(false);
            copy.CreateNew();

            // Asking for a new one turns creation on
            Assert.IsTrue(copy.CreateMissing);
            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreEqual("Hips/Pouch_R/Strap", copy.CreatedPath());
        }

        [Test]
        public void PoseDefault_FollowsTheCounterpart_UntilThePoseIsTicked()
        {
            var root = CreateAvatar(out var handL, out var handR, "Body", "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            AddSkinnedMesh(root.Find("Body"), handL, handR);
            var copy = new OtherSideCopy(handL.Find("Collider_L"));
            Assert.IsTrue(copy.CopyPose);

            // Picking a bone for the counterpart would move the bone
            copy.SetCounterpart(handR);
            Assert.IsFalse(copy.CopyPose);

            copy.SetCopyPose(true);
            copy.ResetMappings();
            Assert.IsTrue(copy.CopyPose, "ticked by the user");
        }

        [Test]
        public void CounterpartCreatedByApplying_IsNotCreatedAgain()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var copy = new OtherSideCopy(handL.Find("Collider_L"));
            copy.SetCounterpart(null);
            var plan = copy.Plan;
            CopyExecutor.Execute(plan);
            var created = handR.GetChild(1);

            copy.KeepCreated(plan);
            copy.Rescan();

            Assert.IsNull(copy.Created);
            Assert.AreSame(created, copy.Counterpart);

            // Undone, the user's "there is none" holds again rather than the automatic counterpart
            Undo.PerformUndo();
            copy.Rescan();

            Assert.IsNotNull(copy.Created);
            Assert.IsNull(copy.Counterpart);

            // Redone, the created object is the counterpart again
            Undo.PerformRedo();
            copy.Rescan();

            Assert.IsNull(copy.Created);
            Assert.AreEqual(created.GetInstanceID(), copy.Counterpart.GetInstanceID());
            Assert.IsFalse(copy.HasManualMappings, "nothing for Auto to take back while the pin holds");

            // Undone again, "none" is what Auto takes back
            Undo.PerformUndo();
            copy.Rescan();
            Assert.IsTrue(copy.HasManualMappings);

            copy.ResetMappings();

            Assert.IsNull(copy.Created);
            Assert.AreSame(handR.Find("Collider_R"), copy.Counterpart);
        }

        [Test]
        public void CounterpartCreatedForTheSecondOfTwoSameNameObjects_IsNotCreatedAgain()
        {
            // The automatic rules look for the second "Collider" as the second one, which the right hand lacks
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider", "Hips/Hand_L/Collider");
            var copy = new OtherSideCopy(handL.GetChild(1));
            var plan = copy.Plan;
            CopyExecutor.Execute(plan);
            var created = handR.Find("Collider");

            copy.KeepCreated(plan);
            copy.Rescan();

            Assert.IsNull(copy.Created);
            Assert.AreSame(created, copy.Counterpart);
            Assert.IsTrue(copy.IsKept(copy.Source));
            // Not a choice of the user's, which "Auto" would take back
            Assert.IsFalse(copy.HasManualMappings);
            copy.ResetMappings();
            Assert.AreSame(created, copy.Counterpart, "kept through Auto");

            // Kept for the next copy, whichever window asks for it, and across a domain reload
            Assert.AreSame(created, new OtherSideCopy(handL.GetChild(1)).Counterpart, "kept for the next copy");
            OtherSideCopy.ReloadPins();
            Assert.AreSame(created, new OtherSideCopy(handL.GetChild(1)).Counterpart, "kept through a reload");

            // Undone, the automatic rules decide again; redone, it is pinned again
            Undo.PerformUndo();
            copy.Rescan();

            Assert.IsFalse(copy.IsKept(copy.Source));
            Assert.IsNotNull(copy.Created);

            Undo.PerformRedo();
            copy.Rescan();

            Assert.IsNull(copy.Created);
            Assert.AreEqual(created.GetInstanceID(), copy.Counterpart.GetInstanceID());

            // Moved where it cannot be a counterpart (the source's own side, the middle line), it is not taken for one
            var redone = copy.Counterpart;
            redone.SetParent(handL, true);
            copy.Rescan();

            Assert.IsFalse(copy.IsKept(copy.Source), "on the source's side");

            redone.SetParent(handL.parent, true);
            copy.Rescan();

            Assert.IsFalse(copy.IsKept(copy.Source), "on the middle line");
        }

        [Test]
        public void Pins_OnlyBearOnTheCopiesTheyAreAbout()
        {
            CreateAvatar(out var handL, out _,
                "Hips/Hand_L/X_L", "Hips/Hand_R/X_R", "Hips/Hand_L/Z_L", "Hips/Hand_R/Z_R");
            var otherAvatar = CreateAvatar(out var otherHandL, out _, "Hips/Hand_L/X_L", "Hips/Hand_R/X_R");
            var copy = new OtherSideCopy(handL.Find("X_L"));
            copy.SetCounterpart(null);
            var plan = copy.Plan;
            CopyExecutor.Execute(plan);
            copy.KeepCreated(plan);

            // Undone, the "none" is back for this copy only: for another object it was no choice of the user's
            Undo.PerformUndo();
            copy.Rescan();
            Assert.IsTrue(copy.HasManualMappings);

            var sibling = new OtherSideCopy(handL.Find("Z_L"));
            Assert.IsFalse(sibling.HasManualMappings);

            // "Auto" there leaves it alone: redone and undone again, X_L still has "none"
            Undo.PerformRedo();
            sibling.Rescan();
            sibling.SetCounterpart(null);
            sibling.ResetMappings();
            Undo.PerformUndo();
            copy.Rescan();
            Assert.IsNull(copy.Counterpart);
            Assert.IsNotNull(copy.Created);

            // Nor does anything of one avatar bear on another
            var elsewhere = new OtherSideCopy(otherHandL.Find("X_L"));
            Assert.IsFalse(elsewhere.HasManualMappings);
            Assert.AreSame(otherAvatar.Find("Hips/Hand_R/X_R"), elsewhere.Counterpart);
        }

        [Test]
        public void OnlyTheLatestPins_AreKept()
        {
            var names = Enumerable.Range(0, OtherSideCopy.MaxPins + 2).Select(i => $"Item{i}_L").ToList();
            CreateAvatar(out var handL, out _, names.Select(name => "Hips/Hand_L/" + name).ToArray());
            var items = names.Select(name => handL.Find(name)).ToList();

            OtherSideCopy first = null;
            foreach (var item in items)
            {
                var copy = new OtherSideCopy(item);
                var plan = copy.Plan;
                CopyExecutor.Execute(plan);
                copy.KeepCreated(plan);
                first ??= copy;
            }

            // The oldest make way. A copy still open for one of them lets go of it too, rather than taking it for a
            // choice of the user's.
            first.Rescan();
            Assert.IsFalse(first.IsKept(items[0]));
            Assert.IsFalse(first.HasManualMappings);
            Assert.IsTrue(new OtherSideCopy(items.Last()).IsKept(items.Last()));
        }

        [Test]
        public void BoneIsNotCreated_UnlessItIsTheRootOfANestedPrefab()
        {
            var root = CreateAvatar(out var handL, out var handR, "Body");
            var asset = SavePrefab("Earring_L", earring => AddChild(earring, "Chain"));
            var earringL = Instantiate(asset, handL);
            // The chain is skinned, which makes the earring an ancestor of a bone
            AddSkinnedMesh(root.Find("Body"), handL, handR, earringL.Find("Chain"));

            var copy = new OtherSideCopy(earringL.Find("Chain"));

            Assert.IsTrue(copy.CanCreateAt(earringL), "instantiated with its bones");
            Assert.IsFalse(copy.CanCreateAt(earringL.Find("Chain")));
            Assert.IsFalse(copy.CanCreateAt(handL));
        }

        [Test]
        public void PrefabRoot_IsNotCreated_OnceAnObjectInItIsMappedByHand()
        {
            var root = CreateAvatar(out var handL, out var handR, "Body", "Hips/Hand_R/Chain_R");
            var asset = SavePrefab("Pendant_L", pendant => AddChild(pendant, "Chain"));
            var pendantL = Instantiate(asset, handL);
            var chain = pendantL.Find("Chain");
            AddSkinnedMesh(root.Find("Body"), handL, handR, chain);
            var chainR = handR.Find("Chain_R");

            // The pendant is in the target then, in part at least: its root would be created as the ancestor of a
            // bone, not instantiated
            var copy = new OtherSideCopy(chain);
            Assert.IsTrue(copy.SetCounterpart(chainR));
            Assert.IsFalse(copy.CanCreateAt(pendantL));

            // The root's own mapping is the one that creating it replaces
            var rootCopy = new OtherSideCopy(pendantL);
            Assert.IsTrue(rootCopy.SetCounterpart(chainR));
            Assert.IsTrue(rootCopy.CanCreate);
        }

        [Test]
        public void PrefabRoot_IsNotCreated_WhenItContainsTheReverseOfAKeptPair()
        {
            var root = CreateAvatar(out var handL, out var handR, "Body", "Hips/Hand_L/Collider_L");
            var asset = SavePrefab("Pendant_R", pendant => AddChild(pendant, "Chain"));
            var pendantR = Instantiate(asset, handR);
            AddSkinnedMesh(root.Find("Body"), handL, handR, pendantR.Find("Chain"));

            var colliderL = handL.Find("Collider_L");
            var copy = new OtherSideCopy(colliderL);
            var plan = copy.Plan;
            CopyExecutor.Execute(plan);
            copy.KeepCreated(plan);
            var colliderR = handR.Find("Collider_R");
            Assert.IsNotNull(colliderR);
            colliderR.SetParent(pendantR, true);

            // The remembered pair also maps the created object back to its source. From this side the prefab
            // is present in part, so its skinned root cannot be created as an ordinary empty object.
            var reverse = new OtherSideCopy(pendantR);
            Assert.IsTrue(reverse.Plan.Map.SourceSkeleton.IsBone(pendantR));
            Assert.AreEqual(MappingState.Manual, reverse.Plan.Map.Get(colliderR).State);
            Assert.AreSame(colliderL, reverse.Plan.Map.Get(colliderR).Target);
            Assert.IsFalse(NestedPrefabs.IsMissing(pendantR, reverse.Plan.Map));
            Assert.IsFalse(reverse.CanCreate);
            Assert.AreEqual(BlockReason.BoneMissing, reverse.BlockReason);
            Assert.IsEmpty(reverse.Plan.ObjectsToCreate);
        }

        [Test]
        public void ParentCreatedAnewByApplying_IsNotCreatedAgain()
        {
            var root = CreateAvatar(out _, out _, "Hips/Pouch_L/Strap/Ring_L", "Hips/Strap");
            var copy = new OtherSideCopy(root.Find("Hips/Pouch_L/Strap/Ring_L"));
            copy.CreateNew(root.Find("Hips/Pouch_L/Strap"));
            var plan = copy.Plan;
            CopyExecutor.Execute(plan);

            copy.KeepCreated(plan);
            copy.Rescan();

            // The new strap below the new pouch is the parent now, and the ring is there already
            Assert.IsEmpty(copy.Plan.ObjectsToCreate);
            Assert.AreSame(root.Find("Hips/Pouch_R/Strap/Ring_R"), copy.Counterpart);
        }

        [Test]
        public void ParentWithASuggestionThatCannotBeUsed_CanBeCreatedAnew()
        {
            var root = CreateAvatar(out _, out _, "Hips/Pouch_L/Strap/Ring_L", "Hips/Strap");
            var copy = new OtherSideCopy(root.Find("Hips/Pouch_L/Strap/Ring_L"));
            var strap = root.Find("Hips/Pouch_L/Strap");

            Assert.AreSame(strap, copy.BlockingParent);
            Assert.IsNull(copy.SuggestionFor(strap), "the belt's strap is on no side");
            Assert.IsTrue(copy.CanCreateAt(strap));

            copy.CreateNew(strap);

            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreEqual("Hips/Pouch_R/Strap/Ring_R", copy.CreatedPath());
        }

        [Test]
        public void ReferenceWaitingForASuggestion_CanBeResolvedByConfirmingIt()
        {
            // The constraint follows an empty anchor of a pouch that has two candidates on the right
            var root = CreateAvatar(out var handL, out _,
                "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R", "Hips/Pouch_L/Anchor", "Hips/A/Pouch_R",
                "Hips/B/Pouch_R");
            var anchor = root.Find("Hips/Pouch_L/Anchor");
            var constraint = handL.Find("Collider_L").gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = anchor, weight = 1f });
            var copy = new OtherSideCopy(handL.Find("Collider_L"));

            var reference = copy.Plan.Components.Single().References.Single();
            Assert.AreEqual(ReferenceKind.InternalUnresolved, reference.Kind);
            Assert.AreSame(anchor.parent, copy.PendingSuggestionAt(anchor));

            // With creation off the anchor is not created whatever its parent says, so that is not what it waits for
            copy.SetCreateMissing(false);
            Assert.IsNull(copy.PendingSuggestionAt(anchor));
            copy.SetCreateMissing(true);

            copy.ConfirmSuggestion(anchor.parent);

            Assert.AreEqual(ReferenceKind.InternalMapped, copy.Plan.Components.Single().References.Single().Kind);
            Assert.IsNull(copy.PendingSuggestionAt(anchor));
        }

        [Test]
        public void BoneThatNoMeshUses_StillStartsWithThePoseUnchecked()
        {
            // An outfit that only covers the upper body still brings leg bones, which MA merges onto the avatar's
            var outfit = CreateHierarchy("Outfit",
                "Body", "Armature/Hips/Spine", "Armature/Hips/UpperLeg_L", "Armature/Hips/UpperLeg_R");
            AddSkinnedMesh(outfit.Find("Body"), outfit.Find("Armature/Hips/Spine"));
            var upperLegL = outfit.Find("Armature/Hips/UpperLeg_L");
            upperLegL.localPosition = new Vector3(0.1f, -0.05f, 0f);

            var copy = new OtherSideCopy(upperLegL);

            Assert.IsTrue(copy.IsBone);
            Assert.IsFalse(copy.CopyPose);
        }

        [Test]
        public void Pose_BelowAScaledParent_IsComparedInWorldUnits()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            // Like the armature of an FBX: a local unit is a hundred world units
            handL.localScale = Vector3.one * 100f;
            handR.localScale = Vector3.one * 100f;
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            colliderL.localPosition = new Vector3(0.0005f, 0.001f, -0.0002f);
            CopyExecutor.Execute(new OtherSideCopy(colliderL).Plan);
            Assert.IsFalse(new OtherSideCopy(colliderL).HasWork, "at the pose");

            // Half a centimeter off in the world
            colliderR.localPosition += new Vector3(0.00005f, 0f, 0f);

            Assert.AreEqual(ComponentAction.Overwrite, new OtherSideCopy(colliderL).Pose.Action);
        }

        [Test]
        public void Apply_WithTheCounterpartDeleted_ChangesNothing()
        {
            CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            colliderL.localPosition = new Vector3(0.05f, 0.1f, -0.02f);
            var copy = new OtherSideCopy(colliderL);

            Object.DestroyImmediate(handR.Find("Collider_R").gameObject);
            var result = CopyExecutor.Execute(copy.Plan);

            Assert.IsTrue(result.Stale, "stale");
            Assert.AreEqual(0, handR.childCount);
        }

        [Test]
        public void Fingerprint_ChangesWhenTheSourceMoves()
        {
            CreateAvatar(out var handL, out _, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            string before = new OtherSideCopy(colliderL).Plan.Fingerprint();

            colliderL.localPosition = new Vector3(0.05f, 0.1f, -0.02f);

            Assert.AreNotEqual(before, new OtherSideCopy(colliderL).Plan.Fingerprint());
        }

        [Test]
        public void ObjectCreatedBelowTheMovedCounterpart_LandsOnTheMirrorImage()
        {
            // The constraint follows an empty child that the other side lacks, so the child is created below the
            // counterpart, which the pose moves first
            var root = CreateAvatar(out var handL, out var handR,
                "Hips/Hand_L/Collider_L/Anchor", "Hips/Hand_R/Collider_R");
            var colliderL = handL.Find("Collider_L");
            var colliderR = handR.Find("Collider_R");
            var anchorL = colliderL.Find("Anchor");
            Pose(colliderL, new Vector3(0.05f, 0.1f, -0.02f), new Vector3(15f, 30f, 45f));
            Pose(colliderR, new Vector3(0.3f, -0.2f, 0.1f), new Vector3(-40f, 70f, 10f));
            anchorL.localPosition = new Vector3(0f, 0.2f, 0.1f);
            var constraint = colliderL.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = anchorL, weight = 1f });

            var copy = new OtherSideCopy(colliderL);
            // Not the counterpart, and still shown
            Assert.AreEqual("Hips/Hand_R/Collider_R/Anchor", copy.PathOf(copy.OtherCreated.Single()));
            CopyExecutor.Execute(copy.Plan);

            var anchorR = colliderR.Find("Anchor");
            Assert.IsNotNull(anchorR);
            Assert.AreSame(anchorR, colliderR.GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(anchorL.position), anchorR.position, "anchor");
        }

        [Test]
        public void Counterpart_OutsideTheAvatarOrOnTheMiddleLine_IsNotTaken()
        {
            var root = CreateAvatar(out var handL, out var handR, "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            var elsewhere = CreateHierarchy("OtherAvatar", "Hips/Hand_R");
            var copy = new OtherSideCopy(handL.Find("Collider_L"));

            foreach (var refused in new[] { elsewhere.Find("Hips/Hand_R"), root.Find("Hips"), root })
            {
                Assert.IsFalse(copy.SetCounterpart(refused), refused.name);
                Assert.IsFalse(copy.HasManualMappings, refused.name);
                Assert.AreSame(handR.Find("Collider_R"), copy.Counterpart, refused.name);
            }
        }

        [Test]
        public void Bone_StartsWithThePoseUnchecked()
        {
            var root = CreateAvatar(out var handL, out var handR, "Body", "Hips/Hand_L/Collider_L", "Hips/Hand_R/Collider_R");
            AddSkinnedMesh(root.Find("Body"), handL, handR);
            handL.gameObject.AddComponent<SphereCollider>();
            handR.localPosition += new Vector3(0f, 0.1f, 0f);

            var bone = new OtherSideCopy(handL);
            var notBone = new OtherSideCopy(handL.Find("Collider_L"));

            Assert.IsTrue(bone.IsBone);
            Assert.IsFalse(bone.CopyPose);
            Assert.IsNull(bone.Pose);
            Assert.IsTrue(bone.HasWork, "the components are still copied");
            Assert.IsTrue(notBone.CopyPose);
            // Asked for from the Transform header, the pose is what the user wants
            Assert.IsTrue(new OtherSideCopy(handL, handL).CopyPose);

            bone.SetCopyPose(true);

            Assert.AreEqual(ComponentAction.Overwrite, bone.Pose.Action);
        }

        [Test]
        public void PrefabAroundTheSourceWithOnlyASuggestion_IsNamed_AndCanBeConfirmed()
        {
            var root = CreateAvatar(out _, out _, "Hips/A/Earring_R", "Hips/B/Earring_R");
            var asset = SavePrefab("Earring_L", earring => AddChild(earring, "Collider"));
            var earringL = Instantiate(asset, root.Find("Hips"));
            var copy = new OtherSideCopy(earringL.Find("Collider"));

            // The prefab is what would be brought over, and it has two candidates
            Assert.AreEqual(BlockReason.HostNeedsReview, copy.BlockReason);
            Assert.AreSame(earringL, copy.BlockingParent);

            var suggestion = copy.Plan.Map.Get(earringL).Target;
            copy.ConfirmSuggestion(earringL);

            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreSame(suggestion, copy.Created.ExistingParent);
        }

        [Test]
        public void ParentWithOnlyASuggestion_BlocksTheCreation_UntilItIsConfirmed()
        {
            // Two pouches on the right, neither below the counterpart of Hips: the left one only gets a suggestion
            var root = CreateAvatar(out _, out _, "Hips/Pouch_L/Strap", "Hips/Bag/Pouch_R", "Hips/Box/Pouch_R");
            var copy = new OtherSideCopy(root.Find("Hips/Pouch_L/Strap"));
            var pouchL = copy.Source.parent;

            Assert.AreEqual(BlockReason.HostNeedsReview, copy.BlockReason);
            Assert.AreSame(pouchL, copy.BlockingParent);
            Assert.IsFalse(copy.HasWork);

            var suggestion = copy.Plan.Map.Get(pouchL).Target;
            copy.ConfirmSuggestion(pouchL);

            Assert.AreEqual(BlockReason.None, copy.BlockReason);
            Assert.AreEqual(ComponentAction.Add, copy.Pose.Action);
            Assert.AreSame(suggestion, copy.Created.ExistingParent);
        }

        [Test]
        public void SourceInsideAMissingPrefab_GoesToTheCounterpartPickedByHand()
        {
            var root = CreateAvatar(out _, out _, "Head/Hairpin_Right/Bone_R");
            var asset = SavePrefab("Hairpin_L", hairpin => AddChild(hairpin, "Bone_L"));
            var hairpinL = Instantiate(asset, root.Find("Head"));
            var boneL = hairpinL.Find("Bone_L");
            var boneR = root.Find("Head/Hairpin_Right/Bone_R");
            boneL.gameObject.AddComponent<SphereCollider>();

            var copy = new OtherSideCopy(boneL);
            // The prefab has no counterpart, so it would be brought over as a whole
            Assert.IsNotNull(copy.Created);
            Assert.IsNull(copy.Counterpart);
            Assert.AreSame(boneR, copy.Suggestion);

            Assert.IsTrue(copy.SetCounterpart(boneR));

            Assert.IsEmpty(copy.Plan.ObjectsToCreate);
            Assert.AreSame(boneR, copy.Pose.Target);
            Assert.AreSame(boneR, copy.Plan.Components.Single().TargetHost);
        }

        [Test]
        public void PlainCopy_TakesTheLocalPoseAsItIs()
        {
            var source = CreateHierarchy("Source", "Hips/Bone");
            var target = CreateHierarchy("Target", "Hips/Bone");
            var bone = source.Find("Hips/Bone");
            Pose(bone, new Vector3(0.1f, 0.2f, 0.3f), new Vector3(10f, 20f, 30f));
            bone.localScale = new Vector3(1f, 1.5f, 2f);

            var plan = CopyPlanBuilder.Build(Enumerable.Empty<ComponentEntry>(), TransformMapper.Build(source, target),
                new CopySettings(), poses: new[] { bone });
            Assert.AreEqual(ComponentAction.Overwrite, plan.Poses.Single().Action);
            CopyExecutor.Execute(plan);

            var copied = target.Find("Hips/Bone");
            AssertClose(bone.localPosition, copied.localPosition, "position");
            AssertSameRotation(bone.localRotation, copied.localRotation, "rotation");
            AssertClose(bone.localScale, copied.localScale, "scale");
        }
    }
}
