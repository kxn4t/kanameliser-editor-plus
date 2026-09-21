using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Builds a <see cref="CopyPlan"/> from the selected components, the transform map and the settings.
    /// Does not modify anything.
    /// </summary>
    internal static class CopyPlanBuilder
    {
        /// <param name="objectsToAdd">
        /// Objects of the source that should be added to the target even though no selected component needs
        /// them: nested prefab roots (e.g. a hat that only consists of meshes) and empty objects, which bring
        /// the empty objects below them along.
        /// </param>
        /// <param name="externalMapProvider">
        /// Called with the scene objects outside of the source hierarchy that the copied components refer to,
        /// if there are any. Returns the map that redirects them (see <see cref="ExternalContext"/>), or null
        /// to keep all of them as they are. A callback because the caller may want to cache the map.
        /// </param>
        public static CopyPlan Build(
            IEnumerable<ComponentEntry> selected, TransformMap map, CopySettings settings,
            IEnumerable<Transform> objectsToAdd = null,
            Func<IReadOnlyCollection<Transform>, TransformMap> externalMapProvider = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            settings ??= new CopySettings();

            var plan = new CopyPlan { Map = map, Settings = settings };
            var context = new HostResolver(plan);

            foreach (var entry in selected)
            {
                var planned = new PlannedComponent { Entry = entry };
                context.Resolve(entry.Host, out planned.TargetHost, out planned.HostToCreate,
                    out planned.BlockReason);
                plan.Components.Add(planned);
            }

            foreach (var source in objectsToAdd ?? Enumerable.Empty<Transform>())
            {
                var blockReason = context.RequestObject(source);
                if (blockReason != BlockReason.None)
                    plan.BlockedObjects.Add(new BlockedObject { Source = source, Reason = blockReason });
            }

            var externalTransforms = new HashSet<Transform>();
            PlanDependencies(plan, context, externalTransforms);
            if (externalTransforms.Count > 0 && externalMapProvider != null && settings.RedirectExternalReferences)
                plan.ExternalMap = externalMapProvider(externalTransforms);

            CollectReferences(plan, context);

            // Needs the references, so it runs last
            foreach (var planned in plan.Components)
            {
                if (planned.Action == ComponentAction.Overwrite &&
                    CopyVerifier.IsIdentical(planned, planned.Existing))
                {
                    planned.Action = ComponentAction.SkipIdentical;
                }
            }

            if (plan.ComponentsToRemove.Count > 0) CollectBrokenReferences(plan);

            return plan;
        }

        /// <summary>
        /// Decides where the object a component sits on ends up in the target: an existing object,
        /// an object to create, or nowhere (blocked).
        /// </summary>
        private sealed class HostResolver
        {
            private readonly CopyPlan plan;
            public readonly Dictionary<Transform, PlannedObject> ObjectsBySource = new();

            public HostResolver(CopyPlan plan)
            {
                this.plan = plan;
            }

            public void Resolve(
                Transform sourceHost, out Transform existing, out PlannedObject toCreate, out BlockReason blockReason)
            {
                existing = null;
                blockReason = BlockReason.None;

                if (ObjectsBySource.TryGetValue(sourceHost, out toCreate)) return;

                // Checked before the map: once a nested prefab is brought over as a whole, everything inside it
                // belongs to the new instance, even if a stray same-name object exists elsewhere in the target.
                // It also has to precede the bone rule, because a prefab (e.g. a hat below Head) can carry
                // bones of its own, and those arrive with the prefab instead of being created.
                var prefabRoot = FindMissingNestedPrefabRoot(sourceHost);
                if (prefabRoot != null)
                {
                    blockReason = PlanNestedPrefab(prefabRoot);
                    if (blockReason == BlockReason.None) toCreate = ObjectsBySource[sourceHost];
                    return;
                }

                if (plan.Map.TryResolve(sourceHost, out existing)) return;

                // An unconfirmed suggestion may well be the right object, so nothing is created in its place
                var mapping = plan.Map.Get(sourceHost);
                if (mapping != null && mapping.State == MappingState.NeedsReview)
                {
                    blockReason = BlockReason.HostNeedsReview;
                    return;
                }

                // A bone without skin weights would be an empty object that merely looks like the bone
                if (plan.Map.SourceSkeleton.IsBone(sourceHost))
                {
                    blockReason = BlockReason.BoneMissing;
                    return;
                }

                if (!plan.Settings.CreateMissingObjects || sourceHost.parent == null)
                {
                    blockReason = BlockReason.HostUnmapped;
                    return;
                }

                Resolve(sourceHost.parent, out var parent, out var parentToCreate, out blockReason);
                if (blockReason != BlockReason.None) return;

                toCreate = Register(new PlannedObject
                {
                    Source = sourceHost,
                    ExistingParent = parent,
                    ParentToCreate = parentToCreate,
                });
            }

            /// <summary>
            /// Plans an object on request. A nested prefab is planned as a whole; when it sits inside another
            /// missing prefab, the outer one is planned, because that is the unit that gets instantiated.
            /// Any other object is created together with the empty objects below it.
            /// </summary>
            public BlockReason RequestObject(Transform source)
            {
                if (source == null || !ReferenceWalker.IsInside(source, plan.Map.SourceRoot))
                    return BlockReason.None;

                var missingRoot = FindMissingNestedPrefabRoot(source);
                if (missingRoot != null) return PlanNestedPrefab(missingRoot);

                Resolve(source, out _, out var toCreate, out var blockReason);
                if (toCreate == null) return blockReason;

                // Objects with components are left to the component list; they are created once one of
                // their components is selected
                foreach (Transform child in source)
                {
                    if (MissingObjects.IsMissingEmpty(child, plan.Map)) RequestObject(child);
                }

                return BlockReason.None;
            }

            /// <summary>
            /// Returns the outermost nested prefab root on the way from the source root down to
            /// <paramref name="transform"/> that has no counterpart in the target.
            /// </summary>
            private Transform FindMissingNestedPrefabRoot(Transform transform)
            {
                var path = new List<Transform>();
                for (var current = transform; current != null && current != plan.Map.SourceRoot; current = current.parent)
                    path.Add(current);

                for (int i = path.Count - 1; i >= 0; i--)
                {
                    var candidate = path[i];
                    if (NestedPrefabs.GetPrefabAsset(candidate, plan.Map.SourceRoot) == null) continue;
                    if (plan.Map.TryResolve(candidate, out _)) continue;
                    return candidate;
                }

                return null;
            }

            private BlockReason PlanNestedPrefab(Transform prefabRoot)
            {
                if (ObjectsBySource.ContainsKey(prefabRoot)) return BlockReason.None;

                var mapping = plan.Map.Get(prefabRoot);
                if (mapping != null && mapping.State == MappingState.NeedsReview) return BlockReason.HostNeedsReview;
                if (!plan.Settings.CreateMissingObjects) return BlockReason.HostUnmapped;

                Resolve(prefabRoot.parent, out var parent, out var parentToCreate, out var blockReason);
                if (blockReason != BlockReason.None) return blockReason;

                var root = Register(new PlannedObject
                {
                    Source = prefabRoot,
                    ExistingParent = parent,
                    ParentToCreate = parentToCreate,
                    PrefabAsset = NestedPrefabs.GetPrefabAsset(prefabRoot, plan.Map.SourceRoot),
                });
                PlanPrefabContents(prefabRoot, root, root);
                return BlockReason.None;
            }

            /// <summary>
            /// Plans every object below a nested prefab root. Most of them exist as soon as the prefab is
            /// instantiated; objects that were added to the source instance are created on top.
            /// </summary>
            private void PlanPrefabContents(Transform source, PlannedObject planned, PlannedObject prefabRoot)
            {
                foreach (Transform child in source)
                {
                    var plannedChild = Register(new PlannedObject
                    {
                        Source = child,
                        ParentToCreate = planned,
                        PrefabRoot = prefabRoot,
                        // A deeper nested prefab that was added to the source instance is instantiated as well
                        PrefabAsset = NestedPrefabs.GetPrefabAsset(child, plan.Map.SourceRoot),
                    });
                    PlanPrefabContents(child, plannedChild, prefabRoot);
                }
            }

            private PlannedObject Register(PlannedObject planned)
            {
                planned.SiblingOccurrence = NestedPrefabs.SiblingOccurrence(planned.Source);
                ObjectsBySource[planned.Source] = planned;
                // Parents are always registered before their children, so this list is in creation order
                plan.ObjectsToCreate.Add(planned);
                return planned;
            }
        }

        /// <summary>
        /// Plans what the selection depends on and decides what happens to each component. The steps feed each
        /// other: a referenced object can sit in a nested prefab, whose components then come along and hold
        /// references of their own.
        /// </summary>
        private static void PlanDependencies(CopyPlan plan, HostResolver context, HashSet<Transform> externalTransforms)
        {
            var walked = new HashSet<Component>();
            int objectCount;
            do
            {
                objectCount = plan.ObjectsToCreate.Count;
                AddImplicitComponents(plan, context);
                DecideActions(plan);
                PlanReferencedObjects(plan, context, walked, externalTransforms);
            } while (plan.ObjectsToCreate.Count != objectCount);
        }

        /// <summary>
        /// An empty object that a copied component points at (a constraint source, an anchor, ...) is created
        /// like the object a component sits on; without it the reference would be cleared and the component
        /// would not work. Objects with components of their own are not created bare: the reference stays
        /// unresolved until the user selects those components.
        /// The same walk notes the scene objects outside of the source that are referred to.
        /// </summary>
        private static void PlanReferencedObjects(
            CopyPlan plan, HostResolver context, HashSet<Component> walked, HashSet<Transform> externalTransforms)
        {
            foreach (var planned in plan.Components)
            {
                // A component that is kept as it is (Skip) needs nothing new
                if (!planned.WillWrite) continue;
                if (!walked.Add(planned.Entry.Component)) continue;

                using var serializedObject = new SerializedObject(planned.Entry.Component);
                foreach (var property in ReferenceWalker.Leaves(serializedObject))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var value = property.objectReferenceValue;
                    var transform = ReferenceWalker.GetTransform(value);
                    if (transform == null || transform == plan.Map.SourceRoot) continue;

                    if (!ReferenceWalker.IsInside(transform, plan.Map.SourceRoot))
                    {
                        if (!EditorUtility.IsPersistent(value)) externalTransforms.Add(transform);
                        continue;
                    }

                    if (!(value is GameObject) && !(value is Transform)) continue;
                    if (!MissingObjects.IsEmpty(transform)) continue;

                    // Does nothing for mapped objects; bones, unconfirmed matches and the "create missing
                    // objects" setting are respected, in which case the reference stays unresolved
                    context.Resolve(transform, out _, out _, out _);
                }
            }
        }

        /// <summary>
        /// A nested prefab is brought over as a whole: every component in it is copied, selected or not,
        /// so that overrides made on the source instance (materials, tweaked values, references to the
        /// outfit's bones) carry over.
        /// </summary>
        private static void AddImplicitComponents(CopyPlan plan, HostResolver context)
        {
            if (!plan.ObjectsToCreate.Any(o => o.IsPrefabRoot || o.PrefabRoot != null)) return;

            var alreadyPlanned = new HashSet<Component>(plan.Components.Select(c => c.Entry.Component));

            foreach (var entry in ComponentScanner.Scan(plan.Map.SourceRoot))
            {
                if (alreadyPlanned.Contains(entry.Component)) continue;
                if (!context.ObjectsBySource.TryGetValue(entry.Host, out var host)) continue;
                if (!host.IsPrefabRoot && host.PrefabRoot == null) continue;

                plan.Components.Add(new PlannedComponent { Entry = entry, HostToCreate = host, Implicit = true });
            }
        }

        /// <summary>Decides every component from scratch, so it can run again after components were added.</summary>
        private static void DecideActions(CopyPlan plan)
        {
            plan.ComponentsToRemove.Clear();
            var replacedHosts = new HashSet<(Transform host, Type type)>();

            foreach (var planned in plan.Components)
            {
                if (planned.BlockReason != BlockReason.None)
                {
                    planned.Action = ComponentAction.Blocked;
                    continue;
                }

                var type = planned.Entry.Type;
                var existing = ComponentScanner.FindByTypeAndIndex(planned.TargetHost, type, planned.Entry.Key.Index);

                switch (plan.Settings.ExistingPolicy)
                {
                    case ExistingComponentPolicy.Overwrite:
                        planned.Existing = existing;
                        planned.Action = existing != null ? ComponentAction.Overwrite : ComponentAction.Add;
                        break;

                    case ExistingComponentPolicy.Skip:
                        planned.Existing = existing;
                        planned.Action = existing != null ? ComponentAction.Skip : ComponentAction.Add;
                        break;

                    case ExistingComponentPolicy.Replace:
                        bool hasExisting = planned.TargetHost != null &&
                                           ExactTypeComponents(planned.TargetHost, type).Any();
                        planned.Action = hasExisting ? ComponentAction.Replace : ComponentAction.Add;
                        if (hasExisting && replacedHosts.Add((planned.TargetHost, type)))
                            plan.ComponentsToRemove.AddRange(ExactTypeComponents(planned.TargetHost, type));
                        break;

                    default:
                        planned.Action = ComponentAction.Add;
                        break;
                }
            }
        }

        private static void CollectReferences(CopyPlan plan, HostResolver context)
        {
            var plannedBySource = new Dictionary<Component, PlannedComponent>();
            foreach (var planned in plan.Components)
            {
                if (planned.Action != ComponentAction.Blocked)
                    plannedBySource[planned.Entry.Component] = planned;
            }

            foreach (var planned in plan.Components)
            {
                if (planned.Action == ComponentAction.Blocked) continue;

                using var serializedObject = new SerializedObject(planned.Entry.Component);
                foreach (var property in ReferenceWalker.Leaves(serializedObject))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var value = property.objectReferenceValue;
                    if (value == null) continue;

                    var reference = ClassifyReference(plan, context.ObjectsBySource, plannedBySource, value);
                    if (reference == null) continue;

                    reference.PropertyPath = property.propertyPath;
                    reference.SourceValue = value;
                    planned.References.Add(reference);
                }
            }
        }

        /// <summary>Returns null for references that are simply kept (assets).</summary>
        private static PlannedReference ClassifyReference(
            CopyPlan plan, Dictionary<Transform, PlannedObject> objectsBySource,
            Dictionary<Component, PlannedComponent> plannedBySource, UnityEngine.Object value)
        {
            var transform = ReferenceWalker.GetTransform(value);
            if (transform == null) return null;

            if (!ReferenceWalker.IsInside(transform, plan.Map.SourceRoot))
            {
                if (EditorUtility.IsPersistent(value)) return null;
                return ClassifyExternalReference(plan, transform, value);
            }

            if (value is GameObject || value is Transform)
            {
                var targetRef = new TargetRef { AsGameObject = value is GameObject };

                // Planned objects first, for the same reason the host resolution checks nested prefabs first
                if (objectsBySource.TryGetValue(transform, out targetRef.ObjectToCreate) ||
                    plan.Map.TryResolve(transform, out targetRef.Transform))
                {
                    return new PlannedReference { Kind = ReferenceKind.InternalMapped, Expected = targetRef };
                }

                return new PlannedReference { Kind = ReferenceKind.InternalUnresolved };
            }

            var component = (Component)value;

            if (plannedBySource.TryGetValue(component, out var plannedComponent))
            {
                return new PlannedReference
                {
                    Kind = ReferenceKind.NewComponent,
                    Expected = new TargetRef { PlannedComponent = plannedComponent },
                };
            }

            int index = ComponentScanner.IndexAmongSameType(component);

            if (plan.Map.TryResolve(transform, out var targetTransform))
            {
                var existing = ComponentScanner.FindByTypeAndIndex(targetTransform, component.GetType(), index);
                if (existing != null && !plan.ComponentsToRemove.Contains(existing))
                {
                    return new PlannedReference
                    {
                        Kind = ReferenceKind.InternalMapped,
                        Expected = new TargetRef { ExistingComponent = existing },
                    };
                }
            }

            return new PlannedReference
            {
                Kind = ReferenceKind.InternalUnresolved,
                MissingDependency = new ComponentKey(
                    ObjectMatcher.GetRelativePathFromRoot(transform, plan.Map.SourceRoot),
                    component.GetType().FullName, index),
            };
        }

        /// <summary>
        /// A reference to the avatar around the source (a bone a constraint follows, a collider on the body, ...)
        /// is redirected to the avatar around the target when the counterpart is known. Anything else is kept:
        /// unlike a reference into the source hierarchy, the object it points at is not going away.
        /// </summary>
        private static PlannedReference ClassifyExternalReference(
            CopyPlan plan, Transform transform, UnityEngine.Object value)
        {
            var kept = new PlannedReference
            {
                Kind = ReferenceKind.ExternalScene,
                Expected = new TargetRef { Fixed = value },
            };

            // Without surroundings to map there is still the user's word: manual mappings are part of every
            // map, whatever object they are about
            Transform counterpart = null;
            bool resolved = (plan.ExternalMap != null && plan.ExternalMap.TryResolve(transform, out counterpart)) ||
                            plan.Map.TryResolve(transform, out counterpart);
            if (!resolved) return kept;

            if (value is GameObject || value is Transform)
            {
                return new PlannedReference
                {
                    Kind = ReferenceKind.ExternalMapped,
                    Expected = new TargetRef { Transform = counterpart, AsGameObject = value is GameObject },
                };
            }

            var component = (Component)value;
            var existing = ComponentScanner.FindByTypeAndIndex(
                counterpart, component.GetType(), ComponentScanner.IndexAmongSameType(component));
            if (existing == null) return kept;

            return new PlannedReference
            {
                Kind = ReferenceKind.ExternalMapped,
                Expected = new TargetRef { ExistingComponent = existing },
            };
        }

        /// <summary>
        /// Finds references from untouched target components to components that Replace is about to remove.
        /// </summary>
        private static void CollectBrokenReferences(CopyPlan plan)
        {
            var removed = new HashSet<Component>(plan.ComponentsToRemove);
            var rewritten = new HashSet<Component>(
                plan.Components.Where(c => c.WillWrite && c.Existing != null).Select(c => c.Existing));

            foreach (var holder in plan.Map.TargetRoot.GetComponentsInChildren<Component>(true))
            {
                if (holder == null || holder is Transform) continue;
                if (removed.Contains(holder) || rewritten.Contains(holder)) continue;

                using var serializedObject = new SerializedObject(holder);
                foreach (var property in ReferenceWalker.Leaves(serializedObject))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.objectReferenceValue is Component component && removed.Contains(component))
                    {
                        plan.BrokenReferences.Add(new BrokenReferenceWarning
                        {
                            Holder = holder,
                            PropertyPath = property.propertyPath,
                            Removed = component,
                        });
                    }
                }
            }
        }

        private static IEnumerable<Component> ExactTypeComponents(Transform host, Type type)
        {
            return host.GetComponents(type).Where(c => c != null && c.GetType() == type);
        }
    }

    /// <summary>
    /// Nested prefabs inside the source hierarchy, e.g. a prefab that bundles PhysBone settings, or a hat
    /// placed below the Head bone.
    /// </summary>
    internal static class NestedPrefabs
    {
        /// <summary>
        /// Returns the prefab asset when <paramref name="transform"/> is the root of a nested prefab instance
        /// below <paramref name="sourceRoot"/>, otherwise null.
        /// </summary>
        public static GameObject GetPrefabAsset(Transform transform, Transform sourceRoot)
        {
            if (transform == null || transform == sourceRoot) return null;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject)) return null;

            // Path based on purpose: GetCorrespondingObjectFromSource returns the object inside the outer
            // prefab, and ...FromOriginalSource skips variants and returns their base.
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Lists the nested prefabs of the source that have no counterpart in the target.
        /// Only the outermost ones are returned: a prefab inside a missing prefab arrives with it.
        /// </summary>
        public static List<Transform> FindMissingRoots(TransformMap map)
        {
            var result = new List<Transform>();
            Visit(map.SourceRoot);
            return result;

            void Visit(Transform parent)
            {
                foreach (Transform child in parent)
                {
                    if (GetPrefabAsset(child, map.SourceRoot) != null && !map.TryResolve(child, out _))
                    {
                        result.Add(child);
                        continue;
                    }

                    Visit(child);
                }
            }
        }

        /// <summary>Index of a transform among its same-name siblings.</summary>
        public static int SiblingOccurrence(Transform transform)
        {
            if (transform.parent == null) return 0;

            int occurrence = 0;
            foreach (Transform sibling in transform.parent)
            {
                if (sibling == transform) break;
                if (sibling.name == transform.name) occurrence++;
            }

            return occurrence;
        }

        public static Transform FindChild(Transform parent, string name, int occurrence)
        {
            int seen = 0;
            foreach (Transform child in parent)
            {
                if (child.name != name) continue;
                if (seen == occurrence) return child;
                seen++;
            }

            return null;
        }
    }

    /// <summary>
    /// Empty objects of the source (anchors, organizing folders, leaf objects) that the target lacks.
    /// No component ever asks for them, so the component list cannot bring them over.
    /// </summary>
    internal static class MissingObjects
    {
        /// <summary>True for an object that has nothing but its Transform.</summary>
        public static bool IsEmpty(Transform transform)
        {
            // Missing scripts come back as null entries and still count as something
            return transform != null && transform.GetComponents<Component>().Length == 1;
        }

        /// <summary>
        /// True for an empty object that can be offered for creation. Bones are never created, nested prefabs
        /// are listed on their own, and an unconfirmed match may well be the counterpart.
        /// </summary>
        public static bool IsMissingEmpty(Transform transform, TransformMap map)
        {
            if (!IsEmpty(transform) || map.TryResolve(transform, out _)) return false;
            if (map.SourceSkeleton.IsBone(transform)) return false;
            if (NestedPrefabs.GetPrefabAsset(transform, map.SourceRoot) != null) return false;

            var mapping = map.Get(transform);
            return mapping == null || mapping.State != MappingState.NeedsReview;
        }

        /// <summary>
        /// Lists the missing empty objects. Only the topmost ones are returned: the empty objects below them
        /// are created along with them.
        /// </summary>
        public static List<Transform> FindRoots(TransformMap map)
        {
            var result = new List<Transform>();
            Visit(map.SourceRoot, false);
            return result;

            void Visit(Transform parent, bool parentComesAlong)
            {
                foreach (Transform child in parent)
                {
                    bool resolved = map.TryResolve(child, out _);

                    // A missing prefab arrives as a whole, and nothing can be created below a missing bone
                    if (!resolved && NestedPrefabs.GetPrefabAsset(child, map.SourceRoot) != null) continue;
                    if (!resolved && map.SourceSkeleton.IsBone(child)) continue;

                    bool missingEmpty = IsMissingEmpty(child, map);
                    if (missingEmpty && !parentComesAlong) result.Add(child);

                    Visit(child, missingEmpty);
                }
            }
        }

        /// <summary>Number of empty objects below <paramref name="root"/> that are created along with it.</summary>
        public static int CountBelow(Transform root, TransformMap map)
        {
            int count = 0;
            foreach (Transform child in root)
            {
                if (IsMissingEmpty(child, map)) count += 1 + CountBelow(child, map);
            }

            return count;
        }
    }
}
