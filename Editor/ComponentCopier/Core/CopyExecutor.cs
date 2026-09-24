using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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
                RevertOverridesEqualToPrefab(planned.Result);
            }

            RemoveLeftOut(plan, leftOut, result);

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        private static void CreateObjects(CopyPlan plan, ExecutionResult result)
        {
            var arrivals = new Dictionary<PlannedObject, Dictionary<Object, Object>>();

            // ObjectsToCreate is ordered parents first
            foreach (var planned in plan.ObjectsToCreate)
            {
                var parent = planned.ExistingParent != null ? planned.ExistingParent : planned.ParentToCreate?.Created;
                if (parent == null) continue;

                // Objects below an instantiated prefab are usually there already
                var transform = planned.PrefabRoot != null ? FindArrived(planned, arrivals) : null;

                if (transform == null && planned.PrefabAsset != null)
                {
                    var instance = PrefabUtility.InstantiatePrefab(planned.PrefabAsset, parent) as GameObject;
                    if (instance != null)
                    {
                        Undo.RegisterCreatedObjectUndo(instance, UndoGroupName);
                        transform = instance.transform;
                        result.InstantiatedPrefabs++;
                        // Known already: the objects below are looked up in it
                        planned.Created = transform;
                        RemoveLikeTheSource(planned, Arrivals(planned, arrivals), result);
                    }
                }

                // An object that came with a prefab carries the name of the asset; the source may have renamed
                // it, and a mirror copy renames it. Set before the state is recorded, so that it survives a reload.
                if (transform != null) transform.name = planned.Name;

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
                    result.CreatedObjects++;
                }

                CopyObjectState(planned.Source, transform, plan.Mirror);
                planned.Created = transform;
            }
        }

        /// <summary>
        /// The object of an instantiated prefab that stands for <paramref name="planned"/>: the one that
        /// corresponds to the same object of the asset, whatever it is called. Null for an object that was added
        /// to the source instance; that one is created.
        /// </summary>
        private static Transform FindArrived(
            PlannedObject planned, Dictionary<PlannedObject, Dictionary<Object, Object>> arrivals)
        {
            // Every prefab above it is asked, the nearest first. An object of a prefab that was added inside
            // the prefab corresponds to that inner asset only, since the inner prefab is instantiated on its
            // own. An object that the outer prefab added inside the inner one corresponds to nothing in the
            // inner asset, but to an object of the outer asset, and arrives with the outer prefab.
            for (var prefabRoot = planned.ParentToCreate; prefabRoot != null; prefabRoot = prefabRoot.ParentToCreate)
            {
                if (!prefabRoot.IsPrefabRoot || prefabRoot.Created == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(prefabRoot.PrefabAsset);
                var assetObject = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(planned.Source.gameObject, assetPath);
                if (assetObject == null) continue;

                if (Arrivals(prefabRoot, arrivals).TryGetValue(assetObject, out var arrived) && arrived != null)
                    return ((GameObject)arrived).transform;
            }

            return null;
        }

        /// <summary>
        /// The objects and components of the instance of <paramref name="prefabRoot"/>, by the object of the
        /// asset each one corresponds to. Built once per prefab, when it is in place.
        /// </summary>
        private static Dictionary<Object, Object> Arrivals(
            PlannedObject prefabRoot, Dictionary<PlannedObject, Dictionary<Object, Object>> arrivals)
        {
            if (arrivals.TryGetValue(prefabRoot, out var byAssetObject)) return byAssetObject;

            byAssetObject = NestedPrefabs.CorrespondingObjects(
                prefabRoot.Created, AssetDatabase.GetAssetPath(prefabRoot.PrefabAsset));
            arrivals[prefabRoot] = byAssetObject;
            return byAssetObject;
        }

        /// <summary>
        /// Removes from a new prefab instance what the source instance had removed from the asset, as the same
        /// "removed GameObject" / "removed component" overrides. Runs right after the prefab is instantiated,
        /// before anything is looked up by its index among same-type components, which the removals shift.
        /// Worked out here rather than in the plan: nothing else needs it, and the plan is rebuilt on every click.
        /// </summary>
        private static void RemoveLikeTheSource(
            PlannedObject prefabRoot, Dictionary<Object, Object> arrivals, ExecutionResult result)
        {
            var (removedObjects, removedComponents) = NestedPrefabs.FindRemoved(prefabRoot.Source, prefabRoot.PrefabAsset);
            string assetName = prefabRoot.PrefabAsset.name;

            foreach (var assetObject in removedObjects)
            {
                if (!arrivals.TryGetValue(assetObject, out var arrived) || !(arrived is GameObject gameObject)) continue;
                if (!DestroyWithinPrefab(gameObject))
                {
                    Debug.LogWarning($"[Component Copier] '{gameObject.name}' was removed from the source instance " +
                                     $"of '{assetName}', but cannot be removed from the new instance on this Unity version.");
                }
            }

            var components = removedComponents
                .Select(assetComponent => arrivals.TryGetValue(assetComponent, out var arrived) ? arrived as Component : null)
                .Where(component => component != null)
                .ToList();
            var stayed = DestroyComponents(components);
            result.RemovedComponents += components.Count - stayed.Count;
            foreach (var component in stayed)
            {
                Debug.LogWarning($"[Component Copier] {component.GetType().Name} on '{component.gameObject.name}' was " +
                                 $"removed from the source instance of '{assetName}', but has to stay in the new " +
                                 "instance: another component requires it.");
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
                if (DestroyWithinPrefab(planned.Created.gameObject)) result.LeftOutObjects++;
            }

            var pending = leftOut.Where(p => p.component != null).ToList();
            result.LeftOutComponents = leftOut.Count - pending.Count;

            var stayed = DestroyComponents(pending.Select(p => p.component).ToList());
            result.LeftOutComponents += pending.Count - stayed.Count;
            result.FailedRemovals.AddRange(pending.Where(p => stayed.Contains(p.component)).Select(p => p.planned));
        }

        /// <summary>False on Unity versions without removed-GameObject overrides, where the object has to stay.</summary>
        private static bool DestroyWithinPrefab(GameObject gameObject)
        {
            try
            {
                Undo.DestroyObjectImmediate(gameObject);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Destroys the components. A component that another one requires cannot be removed, and Unity logs an
        /// error for the attempt, so the ones that are free go first, which may free others (both halves of a
        /// pair are removed). Returns the ones that had to stay.
        /// </summary>
        private static List<Component> DestroyComponents(List<Component> components)
        {
            var pending = components.ToList();
            bool progress = true;
            while (progress && pending.Count > 0)
            {
                progress = false;
                foreach (var component in pending.ToList())
                {
                    if (IsRequiredByAnother(component)) continue;

                    Undo.DestroyObjectImmediate(component);
                    pending.Remove(component);
                    progress = true;
                }
            }

            return pending;
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

        /// <summary>
        /// A property of a prefab instance that is written through a SerializedObject stays an override even
        /// when it ends up with the value of the prefab. A redirected reference usually does: the source's
        /// "Upper_arm.L" is copied first, then replaced by the "Upper_arm.R" the prefab has anyway. Such
        /// overrides are reverted, so that only real changes show up as overrides.
        /// </summary>
        private static void RevertOverridesEqualToPrefab(Component component)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(component)) return;

            // A component added to the instance has no counterpart, and no overrides of its own
            var original = PrefabUtility.GetCorrespondingObjectFromSource(component);
            if (original == null) return;
            string assetPath = AssetDatabase.GetAssetPath(original);
            var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(component);

            using var instance = new SerializedObject(component);
            using var prefab = new SerializedObject(original);

            var unchanged = new List<string>();
            foreach (var property in ReferenceWalker.Leaves(instance))
            {
                if (!property.prefabOverride) continue;

                var prefabProperty = prefab.FindProperty(property.propertyPath);
                if (prefabProperty != null && EqualsPrefabValue(property, prefabProperty, assetPath, instanceRoot))
                    unchanged.Add(property.propertyPath);
            }

            foreach (var path in unchanged)
            {
                // Each revert changes the component under the SerializedObject
                instance.Update();
                var property = instance.FindProperty(path);
                if (property != null) PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
            }
        }

        /// <param name="instanceRoot">The outermost prefab instance the property's component belongs to.</param>
        private static bool EqualsPrefabValue(
            SerializedProperty property, SerializedProperty prefabProperty, string assetPath, GameObject instanceRoot)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return SerializedProperty.DataEquals(property, prefabProperty);

            var value = property.objectReferenceValue;
            var prefabValue = prefabProperty.objectReferenceValue;
            if (value == null || prefabValue == null) return value == null && prefabValue == null;

            // The instance points at objects of the instance where the prefab points at its own. Another
            // instance of the same prefab has the same corresponding objects, but pointing there is a change.
            if (!PrefabUtility.IsPartOfPrefabInstance(value)) return value == prefabValue;
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(value) != instanceRoot) return false;
            return PrefabUtility.GetCorrespondingObjectFromSourceAtPath(value, assetPath) == prefabValue;
        }
    }
}
