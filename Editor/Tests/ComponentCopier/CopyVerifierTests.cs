using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// When an existing component already equals what a copy would write, the plan skips it as identical.
    /// A reference without a counterpart is cleared by the copy, so it only counts as identical once it is empty.
    /// </summary>
    public class CopyVerifierTests : ComponentCopierTestBase
    {
        [Test]
        public void ClearedUnresolvedReference_CountsAsIdentical()
        {
            var source = CreateHierarchy("Source", "Body");
            var target = CreateHierarchy("Target", "Body");
            var renderer = source.Find("Body").gameObject.AddComponent<MeshRenderer>();
            source.gameObject.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.5f, new Renderer[] { renderer }) });

            CopyExecutor.Execute(BuildPlan(source, target, new CopySettings(), typeof(LODGroup)));
            var replanned = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));

            Assert.AreEqual(ComponentAction.SkipIdentical, replanned.Components.Single().Action);
        }

        [Test]
        public void UnresolvedReference_ToAnObjectNamedNone_IsStillCleared()
        {
            // Anchor carries a component, so it is not created bare and the constraint source stays unresolved
            var source = CreateHierarchy("Source", "Item", "Anchor");
            var target = CreateHierarchy("Target", "Item", "None");
            source.Find("Anchor").gameObject.AddComponent<SphereCollider>();
            var sourceConstraint = source.Find("Item").gameObject.AddComponent<ParentConstraint>();
            sourceConstraint.AddSource(new ConstraintSource { sourceTransform = source.Find("Anchor"), weight = 1f });

            // Equal to the source but for the reference, which points at an object called "None"
            var targetConstraint = target.Find("Item").gameObject.AddComponent<ParentConstraint>();
            EditorUtility.CopySerialized(sourceConstraint, targetConstraint);
            targetConstraint.SetSource(0, new ConstraintSource { sourceTransform = target.Find("None"), weight = 1f });

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.Overwrite, plan.Components.Single().Action);

            CopyExecutor.Execute(plan);

            Assert.IsNull(targetConstraint.GetSource(0).sourceTransform);
        }

        [Test]
        public void ManyClearedReferences_DoNotHideALaterDifference()
        {
            var source = CreateHierarchy("Source");
            var target = CreateHierarchy("Target");
            var targetLodGroup = AddLodGroupWithClearedCopy(source, target);

            // Serialized after the renderers, so it is beyond the recorded differences
            targetLodGroup.enabled = false;

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));

            Assert.AreEqual(ComponentAction.Overwrite, plan.Components.Single().Action);
        }

        [Test]
        public void ManyClearedReferences_StillCountAsIdentical()
        {
            var source = CreateHierarchy("Source");
            var target = CreateHierarchy("Target");
            AddLodGroupWithClearedCopy(source, target);

            var plan = BuildPlan(source, target, new CopySettings(), typeof(LODGroup));

            Assert.AreEqual(ComponentAction.SkipIdentical, plan.Components.Single().Action);
        }

        /// <summary>
        /// A LODGroup on the source with more renderer references than the diff records, and its copy on the
        /// target with every reference cleared, as a copy leaves them (the renderers have no counterpart).
        /// </summary>
        private static LODGroup AddLodGroupWithClearedCopy(Transform source, Transform target)
        {
            const int rendererCount = 60;
            var renderers = new Renderer[rendererCount];
            for (int i = 0; i < rendererCount; i++)
            {
                var child = new GameObject("Renderer" + i).transform;
                child.SetParent(source, false);
                renderers[i] = child.gameObject.AddComponent<MeshRenderer>();
            }

            var sourceLodGroup = source.gameObject.AddComponent<LODGroup>();
            sourceLodGroup.SetLODs(new[] { new LOD(0.5f, renderers) });

            var targetLodGroup = target.gameObject.AddComponent<LODGroup>();
            EditorUtility.CopySerialized(sourceLodGroup, targetLodGroup);
            using (var serializedObject = new SerializedObject(targetLodGroup))
            {
                for (int i = 0; i < rendererCount; i++)
                {
                    serializedObject.FindProperty($"m_LODs.Array.data[0].renderers.Array.data[{i}].renderer")
                        .objectReferenceValue = null;
                }

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return targetLodGroup;
        }
    }
}
