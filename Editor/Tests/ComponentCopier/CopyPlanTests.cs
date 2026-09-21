using System.Collections.Generic;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// LODGroup (holds Renderer references) and ParentConstraint (holds Transform references) stand in for
    /// PhysBone / collider style components so the tests run without the VRChat SDK.
    /// </summary>
    public class CopyPlanTests : ComponentCopierTestBase
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

        [Test]
        public void TransformReference_IsRedirectedToTargetHierarchy()
        {
            var source = CreateHierarchy("Source", "Armature/Bone", "Item");
            var target = CreateHierarchy("Target", "Armature/Bone", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Armature/Bone"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            CopyExecutor.Execute(plan);

            var copied = target.Find("Item").GetComponent<ParentConstraint>();
            Assert.IsNotNull(copied);
            Assert.AreSame(target.Find("Armature/Bone"), copied.GetSource(0).sourceTransform);
        }

        [Test]
        public void ReferenceBetweenCopiedComponents_PointsToTheNewComponent()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            var sourceRenderer = source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            AddLodGroup(source, sourceRenderer);

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup), typeof(MeshRenderer));
            Assert.AreEqual(ReferenceKind.NewComponent,
                plan.Components.Single(c => c.Entry.Type == typeof(LODGroup)).References.Single().Kind);

            CopyExecutor.Execute(plan);

            var copiedRenderer = target.Find("Body").GetComponent<MeshRenderer>();
            Assert.IsNotNull(copiedRenderer);
            Assert.AreSame(copiedRenderer, target.GetComponent<LODGroup>().GetLODs()[0].renderers[0]);
        }

        [Test]
        public void ReferenceToUnselectedComponent_UsesExistingCounterpart()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());
            var targetRenderer = target.Find("Body").gameObject.AddComponent<MeshRenderer>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            CopyExecutor.Execute(plan);

            Assert.AreSame(targetRenderer, target.GetComponent<LODGroup>().GetLODs()[0].renderers[0]);
        }

        [Test]
        public void UnresolvedReference_IsClearedAndReported()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            AddLodGroup(source, source.Find("Body").gameObject.AddComponent<MeshRenderer>());

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));
            var reference = plan.Components.Single().References.Single();
            Assert.AreEqual(ReferenceKind.InternalUnresolved, reference.Kind);
            Assert.AreEqual(new ComponentKey("Body", typeof(MeshRenderer).FullName, 0), reference.MissingDependency);

            CopyExecutor.Execute(plan);

            Assert.IsNull(target.GetComponent<LODGroup>().GetLODs()[0].renderers[0],
                "A reference must never be left pointing into the source hierarchy");

            var diff = CopyVerifier.Verify(plan).Components.Single();
            Assert.AreEqual(DiffKind.UnresolvedReference, diff.Kind);
        }

        [Test]
        public void ReferencedEmptyObject_IsCreatedAlongWithTheComponent()
        {
            var source = CreateHierarchy("Source", "Item", "Anchors/Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchors/Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            Assert.AreEqual(ReferenceKind.InternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var anchor = target.Find("Anchors/Anchor");
            Assert.IsNotNull(anchor);
            Assert.AreSame(anchor, target.Find("Item").GetComponent<ParentConstraint>().GetSource(0).sourceTransform);
        }

        [Test]
        public void ReferencedObjectWithComponents_IsNotCreatedBare()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            source.Find("Anchor").gameObject.AddComponent<SphereCollider>();
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));

            Assert.AreEqual(ReferenceKind.InternalUnresolved, plan.Components.Single().References.Single().Kind);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void ReferencedEmptyObject_IsNotCreatedWhenCreatingIsOff()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));

            var settings = new CopySettings { CreateMissingObjects = false };
            var plan = BuildPlan(source, target, settings, typeof(ParentConstraint));

            Assert.AreEqual(ReferenceKind.InternalUnresolved, plan.Components.Single().References.Single().Kind);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void ReferencedEmptyObject_IsNotCreatedForASkippedComponent()
        {
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item");
            AddParentConstraint(source.Find("Item"), source.Find("Anchor"));
            target.Find("Item").gameObject.AddComponent<ParentConstraint>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };
            var plan = BuildPlan(source, target, settings, typeof(ParentConstraint));

            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single().Action);
            Assert.IsEmpty(plan.ObjectsToCreate);
        }

        [Test]
        public void MissingEmptyObjects_AreListedByTheirTopmostObject()
        {
            var source = CreateHierarchy("Source", "Anchors/A", "Anchors/B/Deep", "Anchors/WithCollider", "Kept");
            var target = CreateHierarchy("Target", "Kept");
            source.Find("Anchors/WithCollider").gameObject.AddComponent<SphereCollider>();
            var map = TransformMapper.Build(source, target);

            var roots = MissingObjects.FindRoots(map);

            Assert.AreEqual(new[] { source.Find("Anchors") }, roots);
            Assert.AreEqual(3, MissingObjects.CountBelow(source.Find("Anchors"), map));
        }

        [Test]
        public void RequestedEmptyObject_BringsTheEmptyObjectsBelowIt()
        {
            var source = CreateHierarchy("Source", "Anchors/A", "Anchors/B/Deep", "Anchors/WithCollider");
            var target = CreateHierarchy("Target");
            source.Find("Anchors/WithCollider").gameObject.AddComponent<SphereCollider>();
            var map = TransformMapper.Build(source, target);

            var plan = CopyPlanBuilder.Build(
                new List<ComponentEntry>(), map, new CopySettings(), new[] { source.Find("Anchors") });
            CopyExecutor.Execute(plan);

            Assert.IsNotNull(target.Find("Anchors/A"));
            Assert.IsNotNull(target.Find("Anchors/B/Deep"));
            Assert.IsNull(target.Find("Anchors/WithCollider"),
                "Objects with components are left to the component list");
        }

        [Test]
        public void MissingEmptyObjects_SkipBonesAndWhatHangsBelowAMissingBone()
        {
            var source = CreateHierarchy("Source", "Armature/Hips/Tail/Tail_end", "Mesh");
            var target = CreateHierarchy("Target", "Armature/Hips", "Mesh");
            AddSkinnedMesh(source.Find("Mesh"), source.Find("Armature/Hips"), source.Find("Armature/Hips/Tail"));
            AddSkinnedMesh(target.Find("Mesh"), target.Find("Armature/Hips"));

            Assert.IsEmpty(MissingObjects.FindRoots(TransformMapper.Build(source, target)));
        }

        [Test]
        public void MissingHostObject_IsCreatedBelowMappedParent()
        {
            var source = CreateHierarchy("Source", "Armature/Bone/Collider");
            var target = CreateHierarchy("Target", "Armature/Bone");
            var sourceCollider = source.Find("Armature/Bone/Collider");
            sourceCollider.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            sourceCollider.gameObject.AddComponent<SphereCollider>().radius = 0.25f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(1, plan.ObjectsToCreate.Count);

            var result = CopyExecutor.Execute(plan);

            Assert.AreEqual(1, result.CreatedObjects);
            var created = target.Find("Armature/Bone/Collider");
            Assert.IsNotNull(created);
            Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), created.localPosition);
            Assert.AreEqual(0.25f, created.GetComponent<SphereCollider>().radius);
        }

        [Test]
        public void MissingHostObject_IsBlockedWhenCreationIsDisabled()
        {
            var source = CreateHierarchy("Source", "Armature/Collider");
            var target = CreateHierarchy("Target", "Armature");
            source.Find("Armature/Collider").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { CreateMissingObjects = false };
            var planned = BuildPlan(source, target, settings, typeof(SphereCollider)).Components.Single();

            Assert.AreEqual(ComponentAction.Blocked, planned.Action);
            Assert.AreEqual(BlockReason.HostUnmapped, planned.BlockReason);
        }

        [Test]
        public void HostWithUnconfirmedSuggestion_IsBlockedInsteadOfCreated()
        {
            var source = CreateHierarchy("Source", "Armature/Skirt_01");
            var target = CreateHierarchy("Target", "Armature/Skirt");
            source.Find("Armature/Skirt_01").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));

            Assert.AreEqual(BlockReason.HostNeedsReview, plan.Components.Single().BlockReason);
            Assert.AreEqual(0, plan.ObjectsToCreate.Count);
        }

        [Test]
        public void MissingBone_IsNeverCreated()
        {
            var source = CreateHierarchy("Source", "Body", "Armature/Hips/Tail");
            var target = CreateHierarchy("Target", "Body", "Armature/Hips");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Hips/Tail"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Hips"));
            source.Find("Armature/Hips/Tail").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));

            Assert.AreEqual(ComponentAction.Blocked, plan.Components.Single().Action);
            Assert.AreEqual(BlockReason.BoneMissing, plan.Components.Single().BlockReason);
            Assert.AreEqual(0, plan.ObjectsToCreate.Count);
        }

        [Test]
        public void MissingObjectBelowABone_IsStillCreated()
        {
            var source = CreateHierarchy("Source", "Body", "Armature/Hips/Collider");
            var target = CreateHierarchy("Target", "Body", "Armature/Hips");
            AddSkinnedMesh(source.Find("Body"), source.Find("Armature/Hips"));
            AddSkinnedMesh(target.Find("Body"), target.Find("Armature/Hips"));
            source.Find("Armature/Hips/Collider").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            Assert.IsNotNull(target.Find("Armature/Hips/Collider"));
            Assert.IsNotNull(target.Find("Armature/Hips/Collider").GetComponent<SphereCollider>());
        }

        [Test]
        public void OverwritePolicy_WritesOntoExistingComponentAndThenReportsIdentical()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(ComponentAction.Overwrite, plan.Components.Single().Action);
            Assert.AreEqual(DiffKind.ValueMismatch, CopyVerifier.Verify(plan).Components.Single().Kind);

            CopyExecutor.Execute(plan);

            Assert.AreEqual(0.5f, existing.radius);
            Assert.AreEqual(1, target.Find("Bone").GetComponents<SphereCollider>().Length);
            Assert.AreEqual(DiffKind.Match, CopyVerifier.Verify(plan).Components.Single().Kind);

            var replanned = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            Assert.AreEqual(ComponentAction.SkipIdentical, replanned.Components.Single().Action);
        }

        [Test]
        public void SkipPolicy_LeavesExistingComponentUntouched()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            var existing = target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            existing.radius = 0.1f;

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single().Action);
            Assert.AreEqual(0.1f, existing.radius);
        }

        [Test]
        public void AddPolicy_AddsNextToExistingComponent()
        {
            var source = CreateHierarchy("Source", "Bone");
            var target = CreateHierarchy("Target", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Add };
            CopyExecutor.Execute(BuildPlan(source, target, settings, typeof(SphereCollider)));

            Assert.AreEqual(2, target.Find("Bone").GetComponents<SphereCollider>().Length);
        }

        [Test]
        public void ReplacePolicy_RemovesExistingComponentsOnReceivingHostOnly()
        {
            var source = CreateHierarchy("Source", "Bone", "Untouched");
            var target = CreateHierarchy("Target", "Bone", "Untouched");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>().radius = 0.5f;
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Bone").gameObject.AddComponent<SphereCollider>();
            target.Find("Untouched").gameObject.AddComponent<SphereCollider>();

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(SphereCollider));
            Assert.AreEqual(2, plan.ComponentsToRemove.Count);

            CopyExecutor.Execute(plan);

            var remaining = target.Find("Bone").GetComponents<SphereCollider>();
            Assert.AreEqual(1, remaining.Length);
            Assert.AreEqual(0.5f, remaining[0].radius);
            Assert.AreEqual(1, target.Find("Untouched").GetComponents<SphereCollider>().Length);
        }

        [Test]
        public void ReplacePolicy_WarnsAboutReferencesFromUntouchedComponents()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            var targetRenderer = target.Find("Body").gameObject.AddComponent<MeshRenderer>();
            var holder = AddLodGroup(target, targetRenderer);

            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Replace };
            var plan = BuildPlan(source, target, settings, typeof(MeshRenderer));

            var warning = plan.BrokenReferences.Single();
            Assert.AreSame(holder, warning.Holder);
            Assert.AreSame(targetRenderer, warning.Removed);
        }

        [Test]
        public void ExtraComponentOnTarget_IsReported()
        {
            var source = CreateHierarchy("Source", "Bone", "Leftover");
            var target = CreateHierarchy("Target", "Bone", "Leftover");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            var leftover = target.Find("Leftover").gameObject.AddComponent<SphereCollider>();

            var plan = BuildPlan(source, target, new CopySettings(), typeof(SphereCollider));
            CopyExecutor.Execute(plan);

            var extra = CopyVerifier.Verify(plan).Components.Single(c => c.Kind == DiffKind.ExtraOnTarget);
            Assert.AreSame(leftover, extra.Actual);
        }

        [Test]
        public void Execute_IsRevertedByASingleUndo()
        {
            var source = CreateHierarchy("Source", "Armature/Bone/Collider");
            var target = CreateHierarchy("Target", "Armature/Bone");
            source.Find("Armature/Bone/Collider").gameObject.AddComponent<SphereCollider>();
            source.Find("Armature/Bone").gameObject.AddComponent<SphereCollider>();

            CopyExecutor.Execute(BuildPlan(source, target, new CopySettings(), typeof(SphereCollider)));
            Assert.IsNotNull(target.Find("Armature/Bone/Collider"));

            Undo.PerformUndo();

            Assert.IsNull(target.Find("Armature/Bone/Collider"));
            Assert.IsNull(target.Find("Armature/Bone").GetComponent<SphereCollider>());
        }

        [Test]
        public void Scanner_SkipsTransformsAndCategorizesTypes()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.Find("Bone").gameObject.AddComponent<SphereCollider>();
            source.Find("Bone").gameObject.AddComponent<ParentConstraint>();
            source.Find("Bone").gameObject.AddComponent<MeshRenderer>();

            var entries = ComponentScanner.Scan(source);

            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual(ComponentCategory.Other, entries.Single(e => e.Type == typeof(SphereCollider)).Category);
            Assert.AreEqual(ComponentCategory.Constraint,
                entries.Single(e => e.Type == typeof(ParentConstraint)).Category);
            Assert.AreEqual(ComponentCategory.ExcludedByDefault,
                entries.Single(e => e.Type == typeof(MeshRenderer)).Category);
            Assert.IsTrue(entries.All(e => e.Tool == null));
        }

#if MODULAR_AVATAR_INSTALLED
        [Test]
        public void Scanner_KeepsModularAvatarAsItsOwnCategory()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.Find("Bone").gameObject.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarVisibleHeadAccessory>();

            var entry = ComponentScanner.Scan(source).Single();

            Assert.AreEqual(ComponentCategory.ModularAvatar, entry.Category);
            Assert.IsNull(entry.Tool);
        }
#endif

#if AVATAR_OPTIMIZER_INSTALLED
        [Test]
        public void Scanner_NamesOtherToolsAfterTheirPackage()
        {
            var source = CreateHierarchy("Source", "Bone");
            source.gameObject.AddComponent<Anatawa12.AvatarOptimizer.TraceAndOptimize>();

            var entry = ComponentScanner.Scan(source).Single();

            Assert.AreEqual(ComponentCategory.Tool, entry.Category);
            Assert.AreEqual("com.anatawa12.avatar-optimizer", entry.Tool.Id);
            Assert.AreEqual("AAO", entry.Tool.ShortName);
        }
#endif
    }
}
