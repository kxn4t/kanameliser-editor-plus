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

                WriteValues(planned);
                RedirectReferences(planned);
                PrefabUtility.RecordPrefabInstancePropertyModifications(planned.Result);
            }

            RemoveLeftOut(plan, leftOut, result);

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        private static void CreateObjects(CopyPlan plan, ExecutionResult result)
        {
            // Objects inside an instantiated prefab carry the names of the asset, and a mirror copy renames
            // them. Only once all of them are found: a sibling renamed early ("Chain_L" → "Chain_R") would be
            // found again under its new name.
            var renames = new List<(Transform transform, string name)>();
            // Objects made here are not the ones that came with a prefab, whatever their name
            var madeHere = new HashSet<Transform>();

            // ObjectsToCreate is ordered parents first
            foreach (var planned in plan.ObjectsToCreate)
            {
                var parent = planned.ExistingParent != null ? planned.ExistingParent : planned.ParentToCreate?.Created;
                if (parent == null) continue;

                // Objects below an instantiated prefab are usually there already
                Transform transform = planned.PrefabRoot != null
                    ? NestedPrefabs.FindChild(parent, planned.Source.name, planned.SiblingOccurrence, madeHere)
                    : null;

                if (transform != null && transform.name != planned.Name) renames.Add((transform, planned.Name));

                if (transform == null && planned.PrefabAsset != null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(planned.PrefabAsset, parent) as GameObject;
                    if (instance != null)
                    {
                        instance.name = planned.Name;
                        Undo.RegisterCreatedObjectUndo(instance, UndoGroupName);
                        transform = instance.transform;
                        madeHere.Add(transform);
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
                    var gameObject = new GameObject(planned.Name);
                    Undo.RegisterCreatedObjectUndo(gameObject, UndoGroupName);
                    transform = gameObject.transform;
                    transform.SetParent(parent, false);
                    madeHere.Add(transform);
                    result.CreatedObjects++;
                }

                CopyObjectState(planned.Source, transform, plan.Mirror);
                planned.Created = transform;
            }

            // Recorded again: CopyObjectState recorded the old name, and the new one would be lost on reload
            foreach (var (transform, name) in renames)
            {
                transform.name = name;
                PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
            }
        }

        /// <summary>
        /// Transforms are never copied with CopySerialized: that would also copy the parent and child links.
        /// </summary>
        private static void CopyObjectState(Transform source, Transform target, MirrorContext mirror)
        {
            var sourceObject = source.gameObject;
            var targetObject = target.gameObject;

            targetObject.layer = sourceObject.layer;
            targetObject.tag = sourceObject.tag;
            targetObject.SetActive(sourceObject.activeSelf);

            if (mirror != null)
            {
                // The mirror image of the pose, whatever the frame of the new parent looks like. The size is
                // kept in world space too, which is what the plan assumed (MirrorContext.MirroredFrame).
                mirror.Place(target, source);
                target.localScale = WorldSizeScale(source, target.parent);
            }
            else
            {
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
            }

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

        /// <summary>
        /// The local scale that gives the source's size below <paramref name="parent"/>: the source scale where
        /// the parents are equally scaled, as on the two sides of an avatar.
        /// </summary>
        private static Vector3 WorldSizeScale(Transform source, Transform parent)
        {
            var scale = source.localScale;
            if (source.parent == null || parent == null) return scale;

            var from = source.parent.lossyScale;
            var to = parent.lossyScale;
            return new Vector3(Rescale(scale.x, from.x, to.x), Rescale(scale.y, from.y, to.y), Rescale(scale.z, from.z, to.z));
        }

        private static float Rescale(float scale, float from, float to) =>
            Mathf.Approximately(to, 0f) || Mathf.Approximately(from, to) ? scale : scale * from / to;

        private static void WriteValues(PlannedComponent planned)
        {
            if (planned.Values.Count == 0) return;

            using var serializedObject = new SerializedObject(planned.Result);
            foreach (var value in planned.Values)
            {
                var property = serializedObject.FindProperty(value.PropertyPath);
                if (property != null) value.Write(property);
            }

            // Registered for Undo in pass 1, like the references
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
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
