using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Left-out components and objects that were removed from an instantiated prefab.</summary>
        public int LeftOutComponents;
        public int LeftOutObjects;

        /// <summary>Components that could not be added (e.g. rejected by DisallowMultipleComponent).</summary>
        public List<PlannedComponent> Failed = new();

        /// <summary>Left-out components that had to stay because another component requires them.</summary>
        public List<PlannedComponent> FailedRemovals = new();
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

            // Looked up before anything is added: components are found by their index among the same type,
            // which shifts as soon as one is added or removed
            var leftOut = FindLeftOutComponents(plan);

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

            RemoveLeftOut(plan, leftOut, result);

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

                if (planned.LeftOut)
                {
                    // Removed again at the end if it came with the prefab, and not created otherwise
                    planned.Created = transform;
                    continue;
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

        private static List<(PlannedComponent planned, Component component)> FindLeftOutComponents(CopyPlan plan)
        {
            var found = new List<(PlannedComponent, Component)>();
            foreach (var planned in plan.Components)
            {
                if (!planned.LeftOut) continue;

                var host = planned.HostToCreate?.Created;
                if (host == null) continue;

                // Null when the component was added to the source instance and is not part of the prefab asset
                var arrived = ComponentScanner.FindByTypeAndIndex(host, planned.Entry.Type, planned.Entry.Key.Index);
                if (arrived != null) found.Add((planned, arrived));
            }

            return found;
        }

        /// <summary>
        /// Removes what the user left out from the instantiated prefabs. On a prefab instance this becomes a
        /// "removed component" / "removed GameObject" override, which can be reverted from the Overrides menu.
        /// </summary>
        private static void RemoveLeftOut(
            CopyPlan plan, List<(PlannedComponent planned, Component component)> leftOut, ExecutionResult result)
        {
            foreach (var planned in plan.ObjectsToCreate)
            {
                if (!planned.LeftOut || planned.Created == null) continue;

                try
                {
                    Undo.DestroyObjectImmediate(planned.Created.gameObject);
                    result.LeftOutObjects++;
                }
                catch (InvalidOperationException)
                {
                    // Unity versions without removed-GameObject overrides: only the components go
                }
            }

            var pending = leftOut.Where(p => p.component != null).ToList();
            result.LeftOutComponents = leftOut.Count - pending.Count;

            // A component that another one requires cannot be removed, and Unity logs an error for the attempt.
            // So the ones that are free go first, which may free others (both halves of a pair were left out).
            bool progress = true;
            while (progress && pending.Count > 0)
            {
                progress = false;
                foreach (var item in pending.ToList())
                {
                    if (IsRequiredByAnother(item.component)) continue;

                    Undo.DestroyObjectImmediate(item.component);
                    pending.Remove(item);
                    result.LeftOutComponents++;
                    progress = true;
                }
            }

            result.FailedRemovals.AddRange(pending.Select(p => p.planned));
        }

        private static bool IsRequiredByAnother(Component component)
        {
            var type = component.GetType();
            foreach (var other in component.GetComponents<Component>())
            {
                if (other == null || other == component) continue;

                var attributes = other.GetType().GetCustomAttributes(typeof(RequireComponent), true);
                foreach (RequireComponent attribute in attributes)
                {
                    if (Requires(attribute.m_Type0, type) || Requires(attribute.m_Type1, type) ||
                        Requires(attribute.m_Type2, type))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);

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

                // MA falls back to the path half when the object half is empty, so a stale path would
                // silently pick up whatever sits at that path on the new avatar
                if (reference.RewritesPath)
                {
                    var pathProperty = serializedObject.FindProperty(reference.PathPropertyPath);
                    if (pathProperty != null) pathProperty.stringValue = reference.ExpectedPath() ?? "";
                }
            }

            // The component was already registered for Undo in pass 1
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
