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
        /// <summary>
        /// Set when nothing was done because the plan no longer fits the scene (see
        /// <see cref="CopyExecutor.Validate"/>). It is to be made again, and shown before anything is applied.
        /// </summary>
        public bool Stale;

        /// <summary>What made the plan stale, for the console. Null otherwise.</summary>
        public string StaleReason;

        public int CreatedObjects;
        /// <summary>Existing objects that were given a pose of the plan.</summary>
        public int PosedObjects;
        public int InstantiatedPrefabs;
        public int RemovedComponents;
        public int WrittenComponents;

        /// <summary>Left-out components and objects that were removed from an instantiated prefab.</summary>
        public int LeftOutComponents;
        public int LeftOutObjects;

        /// <summary>
        /// Components that could not be added (e.g. rejected by DisallowMultipleComponent), including the ones
        /// Replace did not add because a component they replace could not be removed.
        /// </summary>
        public List<PlannedComponent> Failed = new();

        /// <summary>Left-out components that had to stay because another component requires them.</summary>
        public List<PlannedComponent> FailedRemovals = new();

        /// <summary>
        /// Components that Replace was to remove but that had to stay, because another component requires them.
        /// The plan keeps the ones it knows of, so these come from changes made after planning.
        /// </summary>
        public List<Component> NotRemoved = new();
    }

    /// <summary>
    /// Applies a <see cref="CopyPlan"/> to the target hierarchy as a single Undo step.
    /// </summary>
    internal static class CopyExecutor
    {
        private const string UndoGroupName = "Copy Components";

        /// <summary>
        /// Applies the plan, unless it no longer fits the scene: nothing is changed then, and the result is
        /// <see cref="ExecutionResult.Stale"/>. An exception midway puts the target back as it was before it is
        /// passed on, since a copy that stops halfway leaves references pointing into the source hierarchy.
        /// </summary>
        public static ExecutionResult Execute(CopyPlan plan)
        {
            // Before the Undo group is opened, so that a stale plan leaves nothing behind, not even an Undo step
            string problem = Validate(plan);
            if (problem != null) return new ExecutionResult { Stale = true, StaleReason = problem };

            var result = new ExecutionResult();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoGroupName);
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                Apply(plan, result);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        /// <summary>
        /// Checks that the plan still fits the scene, which may have changed since planning: the window plans again
        /// only after a delay. Everything that is written, and every existing object or component it is written
        /// against, must still exist, and every component that Replace removes must still be removable. Nothing
        /// more: changed values or a changed hierarchy are not looked for. What is not written does not count, so
        /// a blocked component, which has no host, is no problem. Returns the first problem found, for the console,
        /// or null when the plan can be applied.
        /// </summary>
        internal static string Validate(CopyPlan plan)
        {
            foreach (var planned in plan.Components)
            {
                if (!planned.WillWrite) continue;

                if (planned.Entry.Component == null) return $"the source {Describe(planned)} was deleted";
                if (planned.Action == ComponentAction.Overwrite && planned.Existing == null)
                    return $"the component that {Describe(planned)} overwrites was deleted";
                if (planned.HostToCreate == null && planned.TargetHost == null)
                    return $"the target object of {Describe(planned)} was deleted";

                foreach (var reference in planned.References)
                {
                    if (reference.Expected == null || !reference.Expected.RefersToDestroyedObject) continue;

                    string property = ReferenceWalker.DisplayName(reference.DisplayPath);
                    return $"the object that {property} of {Describe(planned)} refers to was deleted";
                }
            }

            foreach (var pose in plan.Poses)
            {
                if (!pose.WillWrite) continue;

                // The values were read from the source when planning; one that is gone may have been replaced
                if (pose.Source == null) return "the object whose pose is copied was deleted";
                if (pose.Target == null) return "the object that the pose is copied to was deleted";
            }

            foreach (var planned in plan.ObjectsToCreate)
            {
                if (planned.Source == null) return $"the source of the new object '{planned.Name}' was deleted";
                if (planned.ParentToCreate == null && planned.ExistingParent == null)
                    return $"the parent of the new object '{planned.Name}' was deleted";

                // Asked of the field itself: IsPrefabRoot turns false once the asset is gone
                if (!ReferenceEquals(planned.PrefabAsset, null) && planned.PrefabAsset == null)
                    return $"the prefab asset of the new object '{planned.Name}' was deleted";
            }

            foreach (var component in plan.ComponentsToRemove)
            {
                // Gone already, which leaves nothing to remove
                if (component == null) continue;

                // A component that requires it may have been added since planning. Planned again, the one that has
                // to stay is overwritten in place instead.
                var requirer = ComponentDependencies.FindRequirer(component, plan.ComponentsToRemove);
                if (requirer != null)
                {
                    return $"{requirer.GetType().Name} on '{component.name}' requires the " +
                           $"{component.GetType().Name} that Replace removes";
                }
            }

            return null;
        }

        /// <summary>Only what the plan noted down: the objects themselves may be gone.</summary>
        private static string Describe(PlannedComponent planned)
        {
            string path = planned.Entry.Key.RelativePath;
            return $"{planned.Entry.Type.Name} on '{(path.Length > 0 ? path : "/")}'";
        }

        /// <summary>Everything <see cref="Execute"/> does to the target, inside the Undo group it opened.</summary>
        private static void Apply(CopyPlan plan, ExecutionResult result)
        {
            // First: an object created below a posed one is placed in world space, which assumes its parent is there
            WritePoses(plan, result);
            CreateObjects(plan, result);
            RemoveReplacedComponents(plan, result);

            // Replace adds its copies once the components they replace are gone. Next to one that stayed, a copy
            // would be rejected, or duplicate it.
            var notReplaced = new HashSet<(Transform host, Type type)>(
                result.NotRemoved.Select(component => (component.transform, component.GetType())));

            // Looked up before anything is added: components are found by their index among the same type,
            // which shifts as soon as one is added or removed
            var leftOut = FindLeftOutComponents(plan);
            ClaimArrivedComponents(plan);

            // The components that Unity added along with a copy to satisfy its RequireComponent, by object,
            // until the copy of their type takes them
            var autoAdded = new Dictionary<Transform, List<Component>>();

            // Pass 1: make every component exist and carry the source values.
            // References still point into the source hierarchy after this pass.
            foreach (var planned in InSourceOrder(plan.Components))
            {
                if (!planned.WillWrite) continue;

                // Fails like a component that cannot be added: nothing else is undone, and the references to it
                // are cleared, which the diff check reports
                if (planned.Action == ComponentAction.Replace &&
                    notReplaced.Contains((planned.TargetHost, planned.Entry.Type)))
                {
                    result.Failed.Add(planned);
                    continue;
                }

                var target = PrepareTargetComponent(planned, autoAdded);
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
        }

        /// <summary>Moves the existing counterparts to their poses, parents first like the plan lists them.</summary>
        private static void WritePoses(CopyPlan plan, ExecutionResult result)
        {
            foreach (var pose in plan.Poses)
            {
                if (!pose.WillWrite) continue;

                Undo.RecordObject(pose.Target, UndoGroupName);
                pose.Write(pose.Target);
                PrefabUtility.RecordPrefabInstancePropertyModifications(pose.Target);
                RevertPoseEqualToPrefab(pose.Target);
                result.PosedObjects++;
            }
        }

        // Compared axis by axis: a counterpart nudged along y before keeps that override when the pose puts y back
        // but moves x
        private static readonly string[] PosePropertyPaths =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
        };

        /// <summary>
        /// A pose written back to the values of the prefab would stay an override, on a counterpart that was nudged
        /// before for instance. Like <see cref="RevertOverridesEqualToPrefab"/>, but for the values of the pose
        /// alone: the other properties of a Transform link it into the hierarchy.
        /// </summary>
        private static void RevertPoseEqualToPrefab(Transform transform)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(transform)) return;

            var original = PrefabUtility.GetCorrespondingObjectFromSource(transform);
            if (original == null) return;

            using var instance = new SerializedObject(transform);
            using var prefab = new SerializedObject(original);
            foreach (string path in PosePropertyPaths)
            {
                // Each revert changes the transform under the SerializedObject
                instance.Update();
                var property = instance.FindProperty(path);
                var prefabProperty = prefab.FindProperty(path);
                if (property == null || prefabProperty == null || !property.prefabOverride) continue;

                if (SerializedProperty.DataEquals(property, prefabProperty))
                    PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
            }
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
                // Use the same pose calculation as an existing counterpart, including its world size.
                mirror.Place(target, source);
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

        /// <summary>
        /// Removes what Replace replaces, each component once nothing requires it any more. The plan keeps the
        /// components that another one requires, so all of these should go. One that has to stay all the same,
        /// because a component that requires it was added after planning, is noted as not removed.
        /// </summary>
        private static void RemoveReplacedComponents(CopyPlan plan, ExecutionResult result)
        {
            var components = plan.ComponentsToRemove.Where(component => component != null).ToList();
            var stayed = DestroyComponents(components);
            result.RemovedComponents += components.Count - stayed.Count;
            result.NotRemoved.AddRange(stayed);
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
        /// Notes, for every copy written onto an object of an instantiated prefab, the component that arrived with
        /// the prefab for it: the one of its type and index there. Each one belongs to that copy alone, since no
        /// two copies share a host, a type and an index. Looked up before pass 1 adds anything: a lookup by index
        /// while adding would also find what was added in the meantime (the copy of another component, or one
        /// that Unity added to satisfy a RequireComponent), and could hand one component to two copies.
        /// </summary>
        private static void ClaimArrivedComponents(CopyPlan plan)
        {
            foreach (var planned in plan.Components)
            {
                if (!planned.WillWrite || planned.HostToCreate == null) continue;

                // Null on an object created from scratch, and when the component was added to the source instance
                planned.Arrived = ComponentScanner.FindByTypeAndIndex(
                    planned.HostToCreate.Created, planned.Entry.Type, planned.Entry.Key.Index);
            }
        }

        /// <summary>
        /// The components in the order pass 1 writes them: the ones for one object together, in their order on
        /// the source object. The copies of one type are then placed in the order of their indices, whatever
        /// order the plan lists them in (the components that arrive with a nested prefab come after the selected
        /// ones): a copy added ahead of one with a lower index would take that index. Components of different
        /// types can still end up in another order, when Unity adds a required one along with a copy.
        /// </summary>
        private static IEnumerable<PlannedComponent> InSourceOrder(List<PlannedComponent> components)
        {
            // GroupBy keeps the objects in the order they first appear, and OrderBy is stable: ties keep the
            // order of the plan
            return components
                .GroupBy(planned => (planned.TargetHost, planned.HostToCreate))
                .SelectMany(byHost => byHost.OrderBy(planned => planned.Entry.Ordinal));
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
        /// pair are removed). Returns the ones that had to stay, including any that Unity refused to destroy.
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
                    if (ComponentDependencies.IsRequiredByAnother(component)) continue;

                    // A requirement the check above cannot see (one of a missing script, ...) makes Unity refuse
                    // with an error, and the component is still there then
                    Undo.DestroyObjectImmediate(component);
                    if (component != null) continue;

                    pending.Remove(component);
                    progress = true;
                }
            }

            return pending;
        }

        /// <summary>
        /// The component a copy is written to: the existing one it overwrites, the one that arrived with an
        /// instantiated prefab, one that Unity added along with an earlier copy to satisfy its RequireComponent,
        /// or a new one, in that order. Null when the component cannot be added.
        /// </summary>
        private static Component PrepareTargetComponent(
            PlannedComponent planned, Dictionary<Transform, List<Component>> autoAdded)
        {
            if (planned.Action == ComponentAction.Overwrite && planned.Existing != null)
            {
                Undo.RegisterCompleteObjectUndo(planned.Existing, UndoGroupName);
                return planned.Existing;
            }

            var host = planned.TargetHost != null ? planned.TargetHost : planned.HostToCreate?.Created;
            if (host == null) return null;

            // Adding another one would duplicate it
            if (planned.Arrived != null) return planned.Arrived;

            // Unity may have added one already, along with an earlier copy that requires it, on an existing object
            // as much as on a new one. Adding another would be rejected where only one is allowed, or leave a
            // second one while the component that requires it keeps using the first.
            var type = planned.Entry.Type;
            if (autoAdded.TryGetValue(host, out var unclaimed))
            {
                int index = unclaimed.FindIndex(component => component != null && component.GetType() == type);
                if (index >= 0)
                {
                    var reused = unclaimed[index];
                    unclaimed.RemoveAt(index);
                    return reused;
                }
            }

            return AddComponent(host, type, autoAdded);
        }

        /// <summary>
        /// Adds a component, and notes in <paramref name="autoAdded"/> the ones that Unity adds along with it to
        /// satisfy its RequireComponent. Only those: whatever was there before stays out (the existing components,
        /// the ones that arrived with a prefab, the copies written so far), so that the Add policy still adds next
        /// to an existing component, and no component is handed to two copies.
        /// </summary>
        private static Component AddComponent(
            Transform host, Type type, Dictionary<Transform, List<Component>> autoAdded)
        {
            var before = new HashSet<Component>(host.GetComponents<Component>());
            var added = Undo.AddComponent(host.gameObject, type);

            foreach (var component in host.GetComponents<Component>())
            {
                // Missing scripts come back as null
                if (component == null || component == added || before.Contains(component)) continue;

                if (!autoAdded.TryGetValue(host, out var unclaimed))
                {
                    unclaimed = new List<Component>();
                    autoAdded[host] = unclaimed;
                }

                unclaimed.Add(component);
            }

            return added;
        }

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
