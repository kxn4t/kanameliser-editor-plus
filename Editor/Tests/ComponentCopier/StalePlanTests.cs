using System;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// The scene can change between planning and applying: the window plans again only after a delay. A plan that
    /// no longer fits must not be applied halfway, so the executor checks it first and changes nothing, and an error
    /// midway reverts what was done before it.
    /// </summary>
    public class StalePlanTests : ComponentCopierTestBase
    {
        private static LODGroup AddLodGroup(Transform host, Renderer renderer)
        {
            var lodGroup = host.gameObject.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[] { new LOD(0.5f, new[] { renderer }) });
            return lodGroup;
        }

        private static ParentConstraint AddParentConstraint(Transform host, Transform constraintSource)
        {
            var constraint = host.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = constraintSource, weight = 1f });
            return constraint;
        }

        private static (int objects, int components) Count(Transform root) => (
            root.GetComponentsInChildren<Transform>(true).Length,
            root.GetComponentsInChildren<Component>(true).Length);

        /// <summary>
        /// A session like the one the window keeps: every component that is not excluded by default is checked.
        /// </summary>
        private static CopySession StartSession(Transform source, Transform target)
        {
            var session = new CopySession(new CopySession.Inputs(), new CopySettings());
            session.SetSource(source.gameObject);
            session.SetTarget(target.gameObject);
            return session;
        }

        [Test]
        public void SourceComponentDeletedAfterPlanning_MakesThePlanStaleAndChangesNothing()
        {
            var source = CreateHierarchy("Source", "Bone", "Item", "Anchors/Anchor");
            var target = CreateHierarchy("Target", "Bone", "Item");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;
            var constraint = AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider), typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.Overwrite,
                plan.Components.Single(c => c.Entry.Type == typeof(SphereCollider)).Action);
            Assert.AreEqual(2, plan.ObjectsToCreate.Count, "Test setup: Anchors/Anchor is to be created");
            var before = Count(target);

            // Written last: without the check, the collider and the new objects would be in place by then
            Object.DestroyImmediate(constraint);
            var result = CopyExecutor.Execute(plan);

            Assert.IsTrue(result.Stale);
            StringAssert.Contains("ParentConstraint", result.StaleReason);
            Assert.AreEqual(0, result.WrittenComponents);
            Assert.AreEqual(before, Count(target));
            Assert.AreEqual(0.1f, existing.radius, "Nothing is written, not even what the deletion leaves alone");
            Assert.IsNull(target.Find("Anchors"));
        }

        [Test]
        public void SourceComponentDeletedAfterPlanning_LeavesWhatReplaceWouldRemove()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            var sourceCollider = source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            sourceCollider.radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            Assert.AreSame(existing, plan.ComponentsToRemove.Single());

            // Replace removes before it copies, so without the check the collider of the target would go first
            Object.DestroyImmediate(sourceCollider);
            var result = CopyExecutor.Execute(plan);

            Assert.IsTrue(result.Stale);
            Assert.AreEqual(0, result.RemovedComponents);
            Assert.AreSame(existing, target.Find("Bone").GetComponents<SphereCollider>().Single());
            Assert.AreEqual(0.1f, existing.radius);
        }

        [Test]
        public void ExistingObjectAReferenceExpects_MakesThePlanStaleOnceDeleted()
        {
            var source = CreateHierarchy("Source", "Armature/Bone", "Item");
            var target = CreateHierarchy("Target", "Armature/Bone", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Armature/Bone"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.InternalMapped, plan.Components.Single().References.Single().Kind);

            Object.DestroyImmediate(target.Find("Armature/Bone").gameObject);
            var result = CopyExecutor.Execute(plan);

            Assert.IsTrue(result.Stale, "Written, the reference would quietly be None");
            Assert.IsNull(target.Find("Item").GetComponent<ParentConstraint>());
        }

        [Test]
        public void ExistingComponentAReferenceExpects_MakesThePlanStaleOnceDeleted()
        {
            // The renderer is not copied, so the copy of the LODGroup refers to the one the target has
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());
            var targetRenderer = target.Find("Body").gameObject.AddComponent<MeshRenderer>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            Assert.AreEqual(ReferenceKind.InternalMapped, plan.Components.Single().References.Single().Kind);

            Object.DestroyImmediate(targetRenderer);

            Assert.IsTrue(CopyExecutor.Execute(plan).Stale);
            Assert.IsNull(target.GetComponent<LODGroup>());
        }

        [Test]
        public void KeptOutsideObject_MakesThePlanStaleOnceDeleted()
        {
            // The tail has no counterpart around the target, so the reference to it is kept as it is
            var avatarA = CreateHierarchy("AvatarA", "Armature/Tail", "Outfit/Item");
            var avatarB = CreateHierarchy("AvatarB", "Armature", "Outfit/Item");
            AddParentConstraint(avatarA.Find("Outfit/Item"), avatarA.Find("Armature/Tail"));

            var plan = BuildPlan(avatarA.Find("Outfit"), avatarB.Find("Outfit"), new CopySettings(),
                typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);

            Object.DestroyImmediate(avatarA.Find("Armature/Tail").gameObject);

            Assert.IsTrue(CopyExecutor.Execute(plan).Stale);
            Assert.IsNull(avatarB.Find("Outfit/Item").GetComponent<ParentConstraint>());
        }

        [Test]
        public void ExistingCounterpartOfASkippedComponent_MakesThePlanStaleOnceDeleted()
        {
            // The target has a Rigidbody on Anchor already, so the plan skips it, and the copy of the joint is to
            // refer to that one: through the planned component, not directly
            var source = CreateHierarchy("Source", "Anchor", "Door");
            var target = CreateHierarchy("Target", "Anchor", "Door");
            var sourceBody = source.Find("Anchor").gameObject.AddComponent<Rigidbody>();
            source.Find("Door").gameObject.AddComponent<HingeJoint>().connectedBody = sourceBody;
            var targetBody = target.Find("Anchor").gameObject.AddComponent<Rigidbody>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };
            var plan = BuildPlan(source, target, settings, typeof(Rigidbody), typeof(HingeJoint));
            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single(c => c.Entry.Component == sourceBody).Action);
            var joint = plan.Components.Single(c => c.Entry.Type == typeof(HingeJoint));
            Assert.AreEqual(ReferenceKind.NewComponent, joint.References.Single(r => r.SourceValue == sourceBody).Kind);

            Object.DestroyImmediate(targetBody);
            var result = CopyExecutor.Execute(plan);

            Assert.IsTrue(result.Stale, "The skipped Rigidbody is not written, but the joint is written against it");
            Assert.IsNull(target.Find("Door").GetComponent<HingeJoint>());
        }

        [Test]
        public void ObjectsAndComponentsThePlanCreates_DoNotMakeItStale()
        {
            // Neither exists before applying, as planned
            var source = CreateHierarchy("Source", "Body", "Item", "Anchors/Anchor");
            var target = CreateHierarchy("Target", "Body", "Item");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());
            AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(),
                typeof(LODGroup), typeof(MeshRenderer), typeof(ParentConstraint));
            Assert.IsNull(CopyExecutor.Validate(plan));

            var result = CopyExecutor.Execute(plan);

            Assert.IsFalse(result.Stale);
            Assert.AreSame(target.Find("Anchors/Anchor"),
                target.Find("Item").GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
            Assert.AreSame(target.Find("Body").GetComponent<MeshRenderer>(),
                target.GetComponent<LODGroup>().GetLODs()[0].renderers[0]);
        }

        [Test]
        public void BlockedComponent_DoesNotMakeThePlanStale()
        {
            // A blocked component has no host, as the plan says, rather than one that was deleted
            var source = CreateHierarchy("Source", "Bone", "Armature/Collider");
            var target = CreateHierarchy("Target", "Bone", "Armature");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            source.Find("Armature/Collider").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { CreateMissingObjects = false };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            var blocked = plan.Components.Single(c => c.Entry.Host == source.Find("Armature/Collider"));
            Assert.AreEqual(ComponentAction.Blocked, blocked.Action);
            Assert.IsNull(blocked.TargetHost);
            Assert.IsNull(blocked.HostToCreate);

            var result = CopyExecutor.Execute(plan);

            Assert.IsFalse(result.Stale);
            Assert.AreEqual(1, result.WrittenComponents);
            Assert.AreEqual(0.5f, target.Find("Bone").GetComponent<SphereCollider>().radius);
        }

        [Test]
        public void ComponentThatReplaceRemoves_MayBeGoneAlready()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            Object.DestroyImmediate(existing);

            var result = CopyExecutor.Execute(plan);

            Assert.IsFalse(result.Stale, "What was to be removed is gone, as the plan wanted");
            Assert.AreEqual(0.5f, target.Find("Bone").GetComponents<SphereCollider>().Single().radius);
        }

        [Test]
        public void ErrorMidway_RevertsEverythingDoneBeforeIt()
        {
            var source = CreateHierarchy("Source", "Door", "Bone", "Item", "Anchors/Anchor");
            var target = CreateHierarchy("Target", "Door", "Bone", "Item");
            source.Find("Door").gameObject.AddComponent<Rigidbody>().mass = 5f;
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"));

            // The joint keeps Replace from removing the Rigidbody, which is overwritten in place instead
            var targetBody = target.Find("Door").gameObject.AddComponent<Rigidbody>();
            targetBody.mass = 2f;
            target.Find("Door").gameObject.AddComponent<HingeJoint>();
            target.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.1f;

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings,
                typeof(Rigidbody), typeof(SphereCollider), typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.Overwrite,
                plan.Components.Single(c => c.Entry.Type == typeof(Rigidbody)).Action);
            Assert.AreEqual(ComponentAction.Replace,
                plan.Components.Single(c => c.Entry.Type == typeof(SphereCollider)).Action);
            Assert.AreEqual(1, plan.ComponentsToRemove.Count);
            Assert.AreEqual(2, plan.ObjectsToCreate.Count);
            var before = Count(target);

            // Breaks the plan on purpose to test the rollback, without a hook in the product code: pass 2 reads the
            // property path of every planned value, and this one is null. By then pass 1 has overwritten the
            // Rigidbody, replaced the collider and added the constraint, and the new objects are in place.
            plan.Components.Single(c => c.Entry.Type == typeof(ParentConstraint)).Values.Add(null);

            Assert.Throws<NullReferenceException>(() => CopyExecutor.Execute(plan));

            Assert.AreEqual(before, Count(target));
            Assert.IsNull(target.Find("Anchors"), "The objects created before the error are gone again");
            Assert.IsNull(target.Find("Item").GetComponent<ParentConstraint>());
            Assert.AreEqual(2f, targetBody.mass, "The value written in place is back");
            var colliders = target.Find("Bone").GetComponents<SphereCollider>();
            Assert.AreEqual(1, colliders.Length);
            Assert.AreEqual(0.1f, colliders[0].radius, "The collider that Replace removed is back");
        }

        [Test]
        public void Fingerprint_StaysTheSameWhileTheSceneDoesNotTouchThePlan()
        {
            var source = CreateHierarchy("Source", "Bone", "Item", "Anchors/Anchor", "Weight", "Door");
            var target = CreateHierarchy("Target", "Bone", "Item", "Weight", "Door");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.3f;
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"))
                .AddSource(new ConstraintSource { sourceTransform = source.Find("Bone"), weight = 1f });
            var weight = source.Find("Weight").gameObject.AddComponent<Rigidbody>();
            source.Find("Door").gameObject.AddComponent<HingeJoint>().connectedBody = weight;
            // Brings a Rigidbody along, which it keeps from being replaced under the Replace policy below
            target.Find("Weight").gameObject.AddComponent<FixedJoint>();

            var session = StartSession(source, target);
            var plan = session.Plan;
            Assert.AreEqual(ComponentAction.Overwrite,
                plan.Components.Single(c => c.Entry.Type == typeof(SphereCollider)).Action);
            Assert.AreEqual(2, plan.ObjectsToCreate.Count, "Test setup: Anchors/Anchor is to be created");
            CollectionAssert.IsSupersetOf(plan.Components.SelectMany(c => c.References).Select(r => r.Kind),
                new[] { ReferenceKind.InternalMapped, ReferenceKind.NewComponent });
            string shown = plan.Fingerprint();

            session.Rescan(resetSelection: false);
            Assert.AreEqual(shown, session.Plan.Fingerprint(), "Planned again from the same scene");

            // Changes that the plan does not use
            CreateHierarchy("Elsewhere", "Child");
            source.Find("Door").localPosition = new Vector3(0f, 1f, 0f);
            session.Rescan(resetSelection: false);
            Assert.AreEqual(shown, session.Plan.Fingerprint(), "The changes do not touch the plan");

            session.ChangeSettings(settings => settings.ExistingPolicy = ExistingComponentPolicy.Replace);
            Assert.IsNotEmpty(session.Plan.ComponentsToRemove);
            Assert.IsNotEmpty(session.Plan.KeptComponents);
            string replacing = session.Plan.Fingerprint();
            Assert.AreNotEqual(shown, replacing, "The actions changed");

            session.Rescan(resetSelection: false);
            Assert.AreEqual(replacing, session.Plan.Fingerprint());
        }

        [Test]
        public void Fingerprint_ChangesWhenAComponentAppears()
        {
            var source = CreateHierarchy("Source", "Bone", "Item");
            var target = CreateHierarchy("Target", "Bone", "Item");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            var session = StartSession(source, target);
            string shown = session.Plan.Fingerprint();

            // Checked by the rescan, as every new component is, although the user has not seen it yet
            var added = source.Find("Item").gameObject.AddComponent<BoxCollider>();
            session.Rescan(resetSelection: false);

            Assert.IsTrue(session.Plan.Components.Any(c => c.Entry.Component == added));
            Assert.AreNotEqual(shown, session.Plan.Fingerprint());
        }

        [Test]
        public void Fingerprint_ChangesWhenAnotherComponentTakesTheKeyOfADeletedOne()
        {
            // The rescan carries the choices over by key, which tells components of one type apart by their index.
            // Once the checked collider is deleted, the unchecked one takes its index, and with it the check.
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            var first = source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            first.radius = 0.3f;
            var second = source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            second.radius = 0.7f;
            var session = StartSession(source, target);
            session.SetChecked(session.Entries.Single(e => e.Component == second), false);
            Assert.AreSame(first, session.Plan.Components.Single().Entry.Component);
            string shown = session.Plan.Fingerprint();

            Object.DestroyImmediate(first);
            session.Rescan(resetSelection: false);

            Assert.AreSame(second, session.Plan.Components.Single().Entry.Component,
                "Test setup: the unchecked collider is planned in place of the deleted one");
            Assert.AreNotEqual(shown, session.Plan.Fingerprint(), "The user has not seen this collider checked");
        }

        [Test]
        public void Fingerprint_ChangesWhenAnObjectAReferenceExpectsIsDeleted()
        {
            var source = CreateHierarchy("Source", "Armature/Bone", "Item");
            var target = CreateHierarchy("Target", "Armature/Bone", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Armature/Bone"));
            var session = StartSession(source, target);
            var shownPlan = session.Plan;

            Object.DestroyImmediate(target.Find("Armature/Bone").gameObject);

            // Summed up after the change, as the window does, when the plan on screen holds a destroyed object
            string shown = shownPlan.Fingerprint();
            session.Rescan(resetSelection: false);

            Assert.AreNotEqual(shown, session.Plan.Fingerprint());
        }
    }
}
