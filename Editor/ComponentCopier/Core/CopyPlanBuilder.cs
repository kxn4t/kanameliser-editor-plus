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
        /// <param name="mirrorRoot">
        /// Set for a mirror copy: the transform whose YZ plane is the mirror (the avatar). Positions, rotations
        /// and side tags are then mirrored, and created objects get the name of the other side.
        /// </param>
        /// <param name="leftOut">
        /// Components the user unchecked on purpose. Only matters inside a nested prefab that gets added:
        /// anywhere else an unchecked component simply is not copied.
        /// </param>
        /// <param name="keyRoot">
        /// The root that the keys of <paramref name="selected"/> and <paramref name="leftOut"/> are relative to,
        /// i.e. the one that was scanned. Defaults to the source root of the map. A mirror copy scans the source
        /// while its map spans the whole avatar.
        /// </param>
        public static CopyPlan Build(
            IEnumerable<ComponentEntry> selected, TransformMap map, CopySettings settings,
            IEnumerable<Transform> objectsToAdd = null,
            Func<IReadOnlyCollection<Transform>, TransformMap> externalMapProvider = null,
            IEnumerable<ComponentKey> leftOut = null,
            Transform mirrorRoot = null,
            Transform keyRoot = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            settings ??= new CopySettings();

            var selectedList = selected.ToList();
            var objectsToAddList = (objectsToAdd ?? Enumerable.Empty<Transform>()).ToList();
            var leftOutKeys = new HashSet<ComponentKey>(leftOut ?? Enumerable.Empty<ComponentKey>());
            var heldBack = new Dictionary<Component, List<PlannedReference>>();

            while (true)
            {
                var plan = BuildOnce(
                    selectedList, map, settings, objectsToAddList, externalMapProvider, leftOutKeys, heldBack,
                    mirrorRoot, keyRoot != null ? keyRoot : map.SourceRoot);
                if (settings.UnresolvedPolicy != UnresolvedReferencePolicy.SkipComponent) return plan;

                // Whether a reference is resolved is only known once the plan is complete, and holding a
                // component back leaves the ones that refer to it without their counterpart. So the plan is
                // rebuilt, with the held-back components taken out of the way, until nothing new comes up.
                bool changed = false;
                foreach (var planned in plan.Components)
                {
                    // Components that arrive with a prefab are not the user's pick, and the prefab comes as a whole
                    if (!planned.WillWrite || planned.Implicit) continue;

                    var unresolved = planned.References
                        .Where(r => r.Kind == ReferenceKind.InternalUnresolved)
                        .ToList();
                    if (unresolved.Count == 0) continue;

                    heldBack[planned.Entry.Component] = unresolved;
                    changed = true;
                }

                if (!changed) return plan;
            }
        }

        /// <param name="heldBack">
        /// Components that are left out because of their unresolved references, with those references. They
        /// are planned as blocked without touching the target: no object is created for them, and references
        /// to them count as unresolved.
        /// </param>
        private static CopyPlan BuildOnce(
            List<ComponentEntry> selected, TransformMap map, CopySettings settings,
            List<Transform> objectsToAdd, Func<IReadOnlyCollection<Transform>, TransformMap> externalMapProvider,
            HashSet<ComponentKey> leftOutKeys, Dictionary<Component, List<PlannedReference>> heldBack,
            Transform mirrorRoot, Transform keyRoot)
        {
            var plan = new CopyPlan
            {
                Map = map,
                Settings = settings,
                SourceAvatarRoot = AvatarObjectReferences.FindAvatarRoot(map.SourceRoot),
                TargetAvatarRoot = AvatarObjectReferences.FindAvatarRoot(map.TargetRoot),
                Mirror = mirrorRoot != null ? new MirrorContext(mirrorRoot) : null,
                KeyRoot = keyRoot,
            };
            var context = new HostResolver(plan);

            foreach (var entry in selected)
            {
                if (heldBack.TryGetValue(entry.Component, out var unresolved))
                {
                    plan.Components.Add(HoldBack(plan, entry, unresolved));
                    continue;
                }

                var planned = new PlannedComponent { Entry = entry };
                context.Resolve(entry.Host, out planned.TargetHost, out planned.HostToCreate,
                    out planned.BlockReason);
                if (IsOnCopiedSide(plan, entry, planned.TargetHost))
                {
                    // The "existing" component would be the source itself or another one being copied,
                    // overwritten or even replaced before it is read
                    planned.TargetHost = null;
                    planned.BlockReason = BlockReason.SameSide;
                }

                plan.Components.Add(planned);
            }

            foreach (var source in objectsToAdd)
            {
                var blockReason = context.RequestObject(source);
                if (blockReason != BlockReason.None)
                    plan.BlockedObjects.Add(new BlockedObject { Source = source, Reason = blockReason });
            }

            var externalTransforms = new HashSet<Transform>();
            PlanDependencies(plan, context, externalTransforms, leftOutKeys);
            if (externalTransforms.Count > 0 && externalMapProvider != null && settings.RedirectExternalReferences)
                plan.ExternalMap = externalMapProvider(externalTransforms);

            CollectReferences(plan, context);
            PlanLeftOutObjects(plan);
            MirrorValuePlanner.Plan(plan, context.ObjectsBySource);

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
        /// A held-back component is not placed, so its host is looked up but never created. The existing
        /// counterpart is noted all the same: the diff check then compares against it instead of reporting the
        /// component as missing.
        /// </summary>
        private static PlannedComponent HoldBack(CopyPlan plan, ComponentEntry entry, List<PlannedReference> unresolved)
        {
            var planned = new PlannedComponent
            {
                Entry = entry,
                BlockReason = BlockReason.UnresolvedReference,
                UnresolvedReferences = unresolved,
            };

            if (plan.Map.TryResolve(entry.Host, out planned.TargetHost) &&
                !IsOnCopiedSide(plan, entry, planned.TargetHost))
            {
                planned.Existing = ComponentScanner.FindByTypeAndIndex(planned.TargetHost, entry.Type, entry.Key.Index);
            }

            return planned;
        }

        /// <summary>
        /// A mirror copy has to land on the other side. The objects on the middle line map to themselves, so a
        /// component there has nowhere to go: its counterpart would be the component itself. An object mapped to
        /// one on its own side (a manual mapping, ...) would take the copy onto the side being copied from.
        /// </summary>
        private static bool IsOnCopiedSide(CopyPlan plan, ComponentEntry entry, Transform targetHost)
        {
            if (plan.Mirror == null || targetHost == null) return false;
            if (targetHost == entry.Host) return true;

            return plan.Map.Sides.Of(targetHost) == plan.Map.Sides.Of(entry.Host);
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
                // A mirror copy creates "Skirt_L" as "Skirt_R"
                planned.Name = plan.Mirror != null && SideName.TryFlip(planned.Source.name, out var flipped)
                    ? flipped
                    : planned.Source.name;
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
        private static void PlanDependencies(
            CopyPlan plan, HostResolver context, HashSet<Transform> externalTransforms,
            HashSet<ComponentKey> leftOutKeys)
        {
            var walked = new HashSet<Component>();
            int objectCount;
            do
            {
                objectCount = plan.ObjectsToCreate.Count;
                AddImplicitComponents(plan, leftOutKeys);
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
                foreach (var slot in ReferenceWalker.References(serializedObject, plan.SourceAvatarRoot))
                {
                    var value = slot.Value;
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
        /// outfit's bones) carry over. The ones the user left out arrive with the prefab all the same, so they
        /// are planned too, for removal.
        /// </summary>
        private static void AddImplicitComponents(CopyPlan plan, HashSet<ComponentKey> leftOutKeys)
        {
            var prefabObjects = plan.ObjectsToCreate.Where(o => o.IsPrefabRoot || o.PrefabRoot != null).ToList();
            if (prefabObjects.Count == 0) return;

            var alreadyPlanned = new HashSet<Component>(plan.Components.Select(c => c.Entry.Component));

            foreach (var host in prefabObjects)
            {
                // Keyed like the selection, so that the caller recognizes them. A mirror copy can also bring a
                // prefab from elsewhere in the avatar (the counterpart of a referenced object), keyed from the
                // map. Such a key can read like one of the source, so the user's choices do not apply to it.
                bool inKeyRoot = ReferenceWalker.IsInside(host.Source, plan.KeyRoot);
                foreach (var entry in ComponentScanner.ScanObject(
                             host.Source, inKeyRoot ? plan.KeyRoot : plan.Map.SourceRoot))
                {
                    if (alreadyPlanned.Contains(entry.Component)) continue;

                    bool leftOut = inKeyRoot && leftOutKeys.Contains(entry.Key);
                    plan.Components.Add(new PlannedComponent
                    {
                        Entry = entry,
                        HostToCreate = host,
                        Implicit = !leftOut,
                        LeftOut = leftOut,
                    });
                }
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

                if (planned.LeftOut)
                {
                    planned.Action = ComponentAction.LeftOut;
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
            // A reference to a component that is blocked or left out has nothing to point at
            static bool HasResult(PlannedComponent planned) =>
                planned.Action != ComponentAction.Blocked && planned.Action != ComponentAction.LeftOut;

            var plannedBySource = new Dictionary<Component, PlannedComponent>();
            foreach (var planned in plan.Components)
            {
                if (HasResult(planned)) plannedBySource[planned.Entry.Component] = planned;
            }

            foreach (var planned in plan.Components)
            {
                if (!HasResult(planned)) continue;

                using var serializedObject = new SerializedObject(planned.Entry.Component);
                foreach (var slot in ReferenceWalker.References(serializedObject, plan.SourceAvatarRoot))
                {
                    var reference = ClassifyReference(plan, context.ObjectsBySource, plannedBySource, slot.Value);
                    if (reference == null) continue;

                    reference.PropertyPath = slot.PropertyPath;
                    reference.SourceValue = slot.Value;
                    reference.PathPropertyPath = slot.PathPropertyPath;
                    reference.TargetAvatarRoot = plan.TargetAvatarRoot;
                    planned.References.Add(reference);
                }
            }
        }

        /// <summary>
        /// An object inside an arriving prefab whose components were all left out would stay behind as an empty
        /// shell. It is removed as well, as long as nothing else is lost with it: it has no children, and no
        /// copied component refers to it.
        /// </summary>
        private static void PlanLeftOutObjects(CopyPlan plan)
        {
            var leftOutByHost = plan.Components
                .Where(c => c.LeftOut && c.HostToCreate != null)
                .GroupBy(c => c.HostToCreate)
                .ToList();
            if (leftOutByHost.Count == 0) return;

            var referenced = new HashSet<PlannedObject>(plan.Components
                .Where(c => c.WillWrite)
                .SelectMany(c => c.References)
                .Select(r => r.Expected?.ObjectToCreate)
                .Where(o => o != null));

            foreach (var group in leftOutByHost)
            {
                var host = group.Key;
                // The prefab root is the prefab itself; it stays
                if (host.PrefabRoot == null) continue;
                if (host.Source.childCount > 0 || referenced.Contains(host)) continue;
                // Missing scripts come back as null entries and still count as something worth keeping
                if (host.Source.GetComponents<Component>().Length - 1 != group.Count()) continue;

                host.LeftOut = true;
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

            // Only a component of the scanned source can be offered for selection
            return new PlannedReference
            {
                Kind = ReferenceKind.InternalUnresolved,
                MissingDependency = ReferenceWalker.IsInside(transform, plan.KeyRoot)
                    ? new ComponentKey(
                        ObjectMatcher.GetRelativePathFromRoot(transform, plan.KeyRoot),
                        component.GetType().FullName, index)
                    : (ComponentKey?)null,
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
        /// <param name="scope">
        /// Limits the search to this part of the source (itself included), such as the source of a mirror copy
        /// within the avatar that the map spans.
        /// </param>
        public static List<Transform> FindMissingRoots(TransformMap map, Transform scope = null)
        {
            var result = new List<Transform>();
            if (scope == null || scope == map.SourceRoot) Visit(map.SourceRoot);
            // Inside a missing prefab, the outer prefab is what arrives
            else if (!AnyAncestorBelow(scope, map.SourceRoot, IsMissing)) VisitChild(scope);
            return result;

            bool IsMissing(Transform transform) =>
                GetPrefabAsset(transform, map.SourceRoot) != null && !map.TryResolve(transform, out _);

            void Visit(Transform parent)
            {
                foreach (Transform child in parent)
                    VisitChild(child);
            }

            void VisitChild(Transform child)
            {
                if (IsMissing(child)) result.Add(child);
                else Visit(child);
            }
        }

        /// <summary>True when an object between <paramref name="transform"/> and <paramref name="root"/> matches.</summary>
        public static bool AnyAncestorBelow(Transform transform, Transform root, Func<Transform, bool> predicate)
        {
            for (var ancestor = transform.parent; ancestor != null && ancestor != root; ancestor = ancestor.parent)
            {
                if (predicate(ancestor)) return true;
            }

            return false;
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

        /// <param name="skip">Children that do not count, such as the ones a copy has just created.</param>
        public static Transform FindChild(Transform parent, string name, int occurrence, HashSet<Transform> skip = null)
        {
            int seen = 0;
            foreach (Transform child in parent)
            {
                if (child.name != name || (skip != null && skip.Contains(child))) continue;
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
        /// <param name="scope">
        /// Limits the search to this part of the source (itself included), such as the source of a mirror copy
        /// within the avatar that the map spans. The topmost missing objects within it are returned, even when
        /// the objects above it are missing too: those are created on the way.
        /// </param>
        public static List<Transform> FindRoots(TransformMap map, Transform scope = null)
        {
            var result = new List<Transform>();
            if (scope == null || scope == map.SourceRoot) Visit(map.SourceRoot, false);
            else if (!NestedPrefabs.AnyAncestorBelow(scope, map.SourceRoot, IsOutOfReach)) VisitChild(scope, false);
            return result;

            // A missing prefab arrives as a whole, and nothing can be created below a missing bone
            bool IsOutOfReach(Transform transform) =>
                !map.TryResolve(transform, out _) &&
                (NestedPrefabs.GetPrefabAsset(transform, map.SourceRoot) != null ||
                 map.SourceSkeleton.IsBone(transform));

            void Visit(Transform parent, bool parentComesAlong)
            {
                foreach (Transform child in parent)
                    VisitChild(child, parentComesAlong);
            }

            void VisitChild(Transform child, bool parentComesAlong)
            {
                if (IsOutOfReach(child)) return;

                bool missingEmpty = IsMissingEmpty(child, map);
                if (missingEmpty && !parentComesAlong) result.Add(child);

                Visit(child, missingEmpty);
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
