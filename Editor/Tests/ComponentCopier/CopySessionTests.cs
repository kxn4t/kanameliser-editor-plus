using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// What the choices in the window mean: which components are checked, what survives a rescan, and what a
    /// new source, target or mapping throws away.
    /// </summary>
    public class CopySessionTests : ComponentCopierTestBase
    {
        private static CopySession StartSession(Transform source, Transform target, bool mirror = false)
        {
            var session = new CopySession(new CopySession.Inputs { MirrorMode = mirror }, new CopySettings());
            session.SetSource(source.gameObject);
            if (target != null) session.SetTarget(target.gameObject);
            return session;
        }

        private static ComponentEntry EntryOf(CopySession session, Component component) =>
            session.Entries.Single(e => e.Component == component);

        [Test]
        public void Components_AreCheckedUnlessExcludedByDefault()
        {
            var source = CreateHierarchy("Source", "Body", "Anchor");
            var target = CreateHierarchy("Target", "Body", "Anchor");
            var collider = source.Find("Anchor").gameObject.AddComponent<SphereCollider>();
            var renderer = source.Find("Body").gameObject.AddComponent<MeshRenderer>();

            var session = StartSession(source, target);

            Assert.IsTrue(session.IsChecked(EntryOf(session, collider)));
            Assert.IsFalse(session.IsChecked(EntryOf(session, renderer)));
        }

        [Test]
        public void Rescan_KeepsTheChoices_AndChecksComponentsThatAreNew()
        {
            var source = CreateHierarchy("Source", "A", "B");
            var target = CreateHierarchy("Target", "A", "B");
            var deselected = source.Find("A").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            session.SetChecked(EntryOf(session, deselected), false);

            var added = source.Find("B").gameObject.AddComponent<BoxCollider>();
            session.Rescan(resetSelection: false);

            Assert.IsFalse(session.IsChecked(EntryOf(session, deselected)), "The user's choice survives the rescan");
            Assert.IsTrue(session.IsChecked(EntryOf(session, added)), "A new component follows the default");
        }

        [Test]
        public void AnotherSource_StartsTheChoicesOver()
        {
            var source = CreateHierarchy("Source", "A");
            var other = CreateHierarchy("Other", "A");
            var collider = source.Find("A").gameObject.AddComponent<SphereCollider>();
            var otherCollider = other.Find("A").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, null);
            var uncheckedKey = EntryOf(session, collider).Key;
            session.SetChecked(EntryOf(session, collider), false);

            session.SetSource(other.gameObject);

            // Keyed alike, so a rescan that kept the choices would keep this one unchecked
            Assert.AreEqual(uncheckedKey, EntryOf(session, otherCollider).Key);
            Assert.IsTrue(session.IsChecked(EntryOf(session, otherCollider)));
        }

        [Test]
        public void SourceWithOnlyOneComponent_ChecksJustThatOne()
        {
            var source = CreateHierarchy("Source", "A", "B");
            var wanted = source.Find("A").gameObject.AddComponent<SphereCollider>();
            var other = source.Find("B").gameObject.AddComponent<SphereCollider>();

            var session = new CopySession(new CopySession.Inputs(), new CopySettings());
            session.SetSource(source.gameObject, only: wanted);

            Assert.IsTrue(session.IsChecked(EntryOf(session, wanted)));
            Assert.IsFalse(session.IsChecked(EntryOf(session, other)));
        }

        [Test]
        public void Preset_ChecksEveryMatchingComponent_UntilAllAreChecked()
        {
            var source = CreateHierarchy("Source", "A", "B", "C");
            var target = CreateHierarchy("Target", "A", "B", "C");
            var first = source.Find("A").gameObject.AddComponent<ParentConstraint>();
            var second = source.Find("B").gameObject.AddComponent<ParentConstraint>();
            var collider = source.Find("C").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            session.SetChecked(EntryOf(session, first), false);

            session.TogglePreset(e => e.Category == ComponentCategory.Constraint);
            Assert.IsTrue(session.IsChecked(EntryOf(session, first)), "A partly checked preset checks the rest");
            Assert.IsTrue(session.IsChecked(EntryOf(session, second)));

            session.TogglePreset(e => e.Category == ComponentCategory.Constraint);
            Assert.IsFalse(session.IsChecked(EntryOf(session, first)), "A fully checked preset unchecks all");
            Assert.IsFalse(session.IsChecked(EntryOf(session, second)));
            Assert.IsTrue(session.IsChecked(EntryOf(session, collider)), "Other components are left alone");
        }

        [Test]
        public void UncheckingAComponentThatArrivesWithAPrefab_LeavesItOut()
        {
            var hatAsset = SavePrefab("Hat_Session", hat =>
            {
                hat.gameObject.AddComponent<ParentConstraint>();
                AddChild(hat, "Mesh").gameObject.AddComponent<MeshRenderer>();
            });
            var source = CreateHierarchy("Source", "Armature/Head");
            var target = CreateHierarchy("Target", "Armature/Head");
            var sourceHat = Instantiate(hatAsset, source.Find("Armature/Head"));
            var session = StartSession(source, target);
            var renderer = EntryOf(session, sourceHat.Find("Mesh").GetComponent<MeshRenderer>());
            Assert.IsTrue(session.IsChecked(renderer), "Not selected, but it arrives with the hat");

            session.SetChecked(renderer, false);
            Assert.IsFalse(session.IsChecked(renderer));
            Assert.IsTrue(session.TryGetPlanned(renderer.Key, out var planned) && planned.LeftOut);

            // Selecting it instead would copy it again whenever the hat is copied onto an existing one
            session.SetChecked(renderer, true);
            Assert.IsTrue(session.IsChecked(renderer));
            Assert.IsFalse(session.IsSelected(renderer.Key), "Back to arriving with the prefab");
        }

        [Test]
        public void ManualMapping_ReachesTheMirrorMapThatIsKept()
        {
            var avatar = CreateHierarchy("Avatar", "Hips/Hand_L/Collider", "Hips/Hand_R", "Hips/Glove_R");
            avatar.Find("Hips/Hand_L/Collider").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(avatar, null, mirror: true);
            var hand = avatar.Find("Hips/Hand_L");
            Assert.AreSame(avatar.Find("Hips/Hand_R"), session.Map.Get(hand).Target);

            session.SetManualMapping(hand, avatar.Find("Hips/Glove_R"));

            Assert.AreSame(avatar.Find("Hips/Glove_R"), session.Map.Get(hand).Target);
        }

        [Test]
        public void ManualMapping_ReachesTheMapOfTheSurroundingsThatIsKept()
        {
            var avatarA = CreateHierarchy("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature/Hips", "Armature/Spine", "Outfit/Item");
            var constraint = avatarA.Find("Outfit/Item").gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = avatarA.Find("Armature/Hips"), weight = 1f });
            var session = StartSession(avatarA.Find("Outfit"), avatarB.Find("Outfit"));

            Object Redirected() => session.Plan.Components.Single().References
                .Single(r => r.Kind == ReferenceKind.ExternalMapped).Expected.Resolve();
            Assert.AreSame(avatarB.Find("Armature/Hips"), Redirected());

            session.SetManualMapping(avatarA.Find("Armature/Hips"), avatarB.Find("Armature/Spine"));

            Assert.AreSame(avatarB.Find("Armature/Spine"), Redirected());
        }

        [Test]
        public void Rescan_DropsManualMappingsOfDeletedObjects_ButKeepsNoCounterpart()
        {
            var source = CreateHierarchy("Source", "A", "B");
            var target = CreateHierarchy("Target", "A", "Other");
            source.Find("A").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            session.SetManualMapping(source.Find("A"), target.Find("Other"));
            session.SetManualMapping(source.Find("B"), null);

            Object.DestroyImmediate(target.Find("Other").gameObject);
            session.Rescan(resetSelection: false);

            Assert.IsFalse(session.ManualMappings.ContainsKey(source.Find("A")));
            Assert.IsTrue(session.ManualMappings.ContainsKey(source.Find("B")), "\"No counterpart\" is a choice");
        }

        [Test]
        public void AnotherTarget_StartsTheMappingsOver()
        {
            var source = CreateHierarchy("Source", "A");
            var target = CreateHierarchy("Target", "A", "Other");
            var otherTarget = CreateHierarchy("OtherTarget", "A");
            source.Find("A").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            session.SetManualMapping(source.Find("A"), target.Find("Other"));

            session.SetTarget(otherTarget.gameObject);

            Assert.IsEmpty(session.ManualMappings);
            Assert.AreSame(otherTarget.Find("A"), session.Map.Get(source.Find("A")).Target);
        }

        [Test]
        public void TargetInsideTheSource_IsNotPlanned()
        {
            var source = CreateHierarchy("Source", "Inner");
            source.gameObject.AddComponent<SphereCollider>();

            var session = StartSession(source, source.Find("Inner"));

            Assert.AreEqual("componentCopier.warning.nested", session.TargetWarning(out bool isError));
            Assert.IsTrue(isError);
            Assert.IsNull(session.Plan);
        }

        [Test]
        public void TargetMovedIntoTheSource_IsNoLongerPlannedAfterARescan()
        {
            var source = CreateHierarchy("Source", "A");
            var target = CreateHierarchy("Target", "A");
            source.Find("A").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            Assert.IsNotNull(session.Plan);

            // Picked while it was fine; the Hierarchy changes afterwards
            target.SetParent(source, false);
            session.Rescan(resetSelection: false);

            Assert.AreEqual("componentCopier.warning.nested", session.TargetWarning(out bool isError));
            Assert.IsTrue(isError);
            Assert.IsNull(session.Plan);
        }
    }
}
