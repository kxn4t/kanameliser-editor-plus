using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal sealed class ExecutionResult
    {
        public int CreatedObjects;
        public int RemovedComponents;
        public int WrittenComponents;

        /// <summary>Components that could not be added (e.g. rejected by DisallowMultipleComponent).</summary>
        public List<PlannedComponent> Failed = new();
    }

    /// <summary>
    /// Applies a <see cref="CopyPlan"/> to the target hierarchy as a single Undo step.
    /// </summary>
    internal static class CopyExecutor
    {
        private const string UndoGroupName = "Copy Components";

        public static ExecutionResult Execute(CopyPlan plan)
        {
            var result = new ExecutionResult();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoGroupName);
            int undoGroup = Undo.GetCurrentGroup();

            CreateObjects(plan, result);
            RemoveReplacedComponents(plan, result);

            // Pass 1: make every component exist and carry the source values.
            // References still point into the source hierarchy after this pass.
            foreach (var planned in plan.Components)
            {
                if (!planned.WillWrite) continue;

                var target = PrepareTargetComponent(planned);
                if (target == null)
                {
                    result.Failed.Add(planned);
                    continue;
                }

                EditorUtility.CopySerialized(planned.Entry.Component, target);
                planned.Result = target;
                result.WrittenComponents++;
            }

            // Pass 2: redirect references. Runs after pass 1 so that references between copied components
            // can point to the newly created ones.
            foreach (var planned in plan.Components)
            {
                if (planned.Result == null) continue;

                RedirectReferences(planned);
                PrefabUtility.RecordPrefabInstancePropertyModifications(planned.Result);
            }

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        private static void CreateObjects(CopyPlan plan, ExecutionResult result)
        {
            // ObjectsToCreate is ordered parents first
            foreach (var planned in plan.ObjectsToCreate)
            {
                var parent = planned.ExistingParent != null ? planned.ExistingParent : planned.ParentToCreate?.Created;
                if (parent == null) continue;

                var source = planned.Source;
                var gameObject = new GameObject(source.name)
                {
                    layer = source.gameObject.layer,
                    tag = source.gameObject.tag,
                };
                gameObject.SetActive(source.gameObject.activeSelf);
                Undo.RegisterCreatedObjectUndo(gameObject, UndoGroupName);

                var transform = gameObject.transform;
                transform.SetParent(parent, false);
                transform.localPosition = source.localPosition;
                transform.localRotation = source.localRotation;
                transform.localScale = source.localScale;

                planned.Created = transform;
                result.CreatedObjects++;
            }
        }

        private static void RemoveReplacedComponents(CopyPlan plan, ExecutionResult result)
        {
            foreach (var component in plan.ComponentsToRemove)
            {
                if (component == null) continue;
                Undo.DestroyObjectImmediate(component);
                result.RemovedComponents++;
            }
        }

        private static Component PrepareTargetComponent(PlannedComponent planned)
        {
            if (planned.Action == ComponentAction.Overwrite && planned.Existing != null)
            {
                Undo.RegisterCompleteObjectUndo(planned.Existing, UndoGroupName);
                return planned.Existing;
            }

            var host = planned.TargetHost != null ? planned.TargetHost : planned.HostToCreate?.Created;
            if (host == null) return null;

            return Undo.AddComponent(host.gameObject, planned.Entry.Type);
        }

        private static void RedirectReferences(PlannedComponent planned)
        {
            if (planned.References.Count == 0) return;

            using var serializedObject = new SerializedObject(planned.Result);

            foreach (var reference in planned.References)
            {
                var property = serializedObject.FindProperty(reference.PropertyPath);
                if (property == null) continue;

                // Unresolved references are cleared: a reference left pointing into the source hierarchy
                // looks fine in the Inspector but breaks as soon as the source is removed.
                property.objectReferenceValue = reference.Expected?.Resolve();
            }

            // The component was already registered for Undo in pass 1
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
