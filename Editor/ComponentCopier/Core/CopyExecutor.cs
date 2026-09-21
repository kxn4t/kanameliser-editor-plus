using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal sealed class ExecutionResult
    {
        public int CreatedObjects;
        public int InstantiatedPrefabs;
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

                // Objects below an instantiated prefab are usually there already
                Transform transform = planned.PrefabRoot != null
                    ? NestedPrefabs.FindChild(parent, planned.Source.name, planned.SiblingOccurrence)
                    : null;

                if (transform == null && planned.PrefabAsset != null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(planned.PrefabAsset, parent) as GameObject;
                    if (instance != null)
                    {
                        instance.name = planned.Source.name;
                        Undo.RegisterCreatedObjectUndo(instance, UndoGroupName);
                        transform = instance.transform;
                        result.InstantiatedPrefabs++;
                    }
                }

                if (transform == null)
                {
                    var gameObject = new GameObject(planned.Source.name);
                    Undo.RegisterCreatedObjectUndo(gameObject, UndoGroupName);
                    transform = gameObject.transform;
                    transform.SetParent(parent, false);
                    result.CreatedObjects++;
                }

                CopyObjectState(planned.Source, transform);
                planned.Created = transform;
            }
        }

        /// <summary>
        /// Transforms are never copied with CopySerialized: that would also copy the parent and child links.
        /// </summary>
        private static void CopyObjectState(Transform source, Transform target)
        {
            var sourceObject = source.gameObject;
            var targetObject = target.gameObject;

            targetObject.layer = sourceObject.layer;
            targetObject.tag = sourceObject.tag;
            targetObject.SetActive(sourceObject.activeSelf);

            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;

            // Values equal to the prefab asset do not become overrides
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
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

            // A freshly created host can already carry the component: it came with an instantiated prefab,
            // or Unity added it to satisfy a RequireComponent. Adding another one would duplicate it.
            if (planned.HostToCreate != null)
            {
                var arrived = ComponentScanner.FindByTypeAndIndex(host, planned.Entry.Type, planned.Entry.Key.Index);
                if (arrived != null) return arrived;
            }

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
