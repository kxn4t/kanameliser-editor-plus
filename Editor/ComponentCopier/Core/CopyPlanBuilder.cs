using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <param name="poses">
        /// Objects of the source whose pose (local position, rotation and scale) their counterpart is given: the
        /// mirror image of it in a mirror copy. A counterpart that is missing is created like the host of a
        /// component, which places it at the pose.
        /// </param>
        public static CopyPlan Build(
            IEnumerable<ComponentEntry> selected, TransformMap map, CopySettings settings,
            IEnumerable<Transform> objectsToAdd = null,
            Func<IReadOnlyCollection<Transform>, TransformMap> externalMapProvider = null,
            IEnumerable<ComponentKey> leftOut = null,
            Transform mirrorRoot = null,
            Transform keyRoot = null,
            IEnumerable<Transform> poses = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var request = new Request
            {
                Selected = selected.ToList(),
                Map = map,
                Settings = settings ?? new CopySettings(),
                ObjectsToAdd = (objectsToAdd ?? Enumerable.Empty<Transform>()).ToList(),
                ExternalMapProvider = externalMapProvider,
                LeftOutKeys = new HashSet<ComponentKey>(leftOut ?? Enumerable.Empty<ComponentKey>()),
                MirrorRoot = mirrorRoot,
                KeyRoot = keyRoot != null ? keyRoot : map.SourceRoot,
                Poses = (poses ?? Enumerable.Empty<Transform>()).Where(t => t != null).Distinct().ToList(),
            };
            var heldBack = new Dictionary<Component, List<PlannedReference>>();

            while (true)
            {
                var plan = BuildOnce(request, heldBack);
                if (request.Settings.UnresolvedPolicy != UnresolvedReferencePolicy.SkipComponent) return plan;

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

        /// <summary>The arguments of <see cref="Build"/>, normalized once for every round of <see cref="BuildOnce"/>.</summary>
        private sealed class Request
        {
            public List<ComponentEntry> Selected;
            public TransformMap Map;
            public CopySettings Settings;
            public List<Transform> ObjectsToAdd;
            public Func<IReadOnlyCollection<Transform>, TransformMap> ExternalMapProvider;
            public HashSet<ComponentKey> LeftOutKeys;
            public Transform MirrorRoot;
            public Transform KeyRoot;
            public List<Transform> Poses;
        }

        /// <param name="heldBack">
        /// Components that are left out because of their unresolved references, with those references. They
        /// are planned as blocked without touching the target: no object is created for them, and references
        /// to them count as unresolved.
        /// </param>
        private static CopyPlan BuildOnce(Request request, Dictionary<Component, List<PlannedReference>> heldBack)
        {
            var map = request.Map;
            var plan = new CopyPlan
            {
                Map = map,
                Settings = request.Settings,
                SourceAvatarRoot = AvatarRoots.Find(map.SourceRoot),
                TargetAvatarRoot = AvatarRoots.Find(map.TargetRoot),
                Mirror = request.MirrorRoot != null ? new MirrorContext(request.MirrorRoot) : null,
                KeyRoot = request.KeyRoot,
            };
            var context = new HostResolver(plan);

            foreach (var entry in request.Selected)
            {
                if (heldBack.TryGetValue(entry.Component, out var unresolved))
                {
                    plan.Components.Add(HoldBack(plan, entry, unresolved));
                    continue;
                }

                var planned = new PlannedComponent { Entry = entry };
                context.Resolve(entry.Host, out planned.TargetHost, out planned.HostToCreate,
                    out planned.BlockReason);
                if (planned.BlockReason != BlockReason.None) planned.BlockedAt = context.BlockedAt;
                if (IsOnCopiedSide(plan, entry.Host, planned.TargetHost))
                {
                    // The "existing" component would be the source itself or another one being copied,
                    // overwritten or even replaced before it is read
                    planned.TargetHost = null;
                    planned.BlockReason = BlockReason.SameSide;
                    planned.BlockedAt = entry.Host;
                }

                plan.Components.Add(planned);
            }

            foreach (var source in request.ObjectsToAdd)
            {
                var blockReason = context.RequestObject(source);
                if (blockReason != BlockReason.None)
                    plan.BlockedObjects.Add(new BlockedObject { Source = source, Reason = blockReason });
            }

            // Resolved with the hosts, so that a counterpart to create is planned before what depends on the objects
            // the plan creates. The values follow once everything is placed, see PlanPoses.
            foreach (var source in request.Poses)
            {
                var pose = new PlannedPose { Source = source };
                context.Resolve(source, out pose.Target, out pose.HostToCreate, out pose.BlockReason);
                if (pose.BlockReason != BlockReason.None) pose.BlockedAt = context.BlockedAt;
                if (IsOnCopiedSide(plan, source, pose.Target))
                {
                    pose.Target = null;
                    pose.BlockReason = BlockReason.SameSide;
                    pose.BlockedAt = source;
                }

                plan.Poses.Add(pose);
            }

            var externalTransforms = new HashSet<Transform>();
            PlanDependencies(plan, context, externalTransforms, request.LeftOutKeys);
            if (externalTransforms.Count > 0 && request.ExternalMapProvider != null &&
                request.Settings.RedirectExternalReferences)
            {
                plan.ExternalMap = request.ExternalMapProvider(externalTransforms);
            }

            // "Skip the component" means leaving the component out. A held-back one inside a nested prefab that the
            // plan adds arrives with the prefab all the same, with the values of the asset rather than those of the
            // source instance, and removing it from the new instance is the only way to leave it out. So it is
            // planned like an unchecked one, while it stays blocked. The host is taken from the objects the plan
            // creates anyway: none is planned for a component that is not copied.
            foreach (var planned in plan.Components)
            {
                if (!planned.IsHeldBack || planned.HostToCreate != null) continue;
                if (!context.ObjectsBySource.TryGetValue(planned.Entry.Host, out var host)) continue;
                if (!host.IsPrefabRoot && host.PrefabRoot == null) continue;

                planned.HostToCreate = host;
                planned.Origin = ComponentOrigin.LeftOut;
            }

            CollectReferences(plan, context);
            PlanLeftOutObjects(plan);
            // Before the mirrored values: those of a posed object are expressed in the frame it is moved to
            PlanPoses(plan);
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
                // Set here as well, not only by DecideActions: the default action is Add
                Action = ComponentAction.Blocked,
                BlockReason = BlockReason.UnresolvedReference,
                UnresolvedReferences = unresolved,
            };

            if (plan.Map.TryResolve(entry.Host, out planned.TargetHost) &&
                !IsOnCopiedSide(plan, entry.Host, planned.TargetHost))
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
        private static bool IsOnCopiedSide(CopyPlan plan, Transform sourceHost, Transform targetHost)
        {
            if (plan.Mirror == null || targetHost == null) return false;
            if (targetHost == sourceHost) return true;

            return plan.Map.Sides.Of(targetHost) == plan.Map.Sides.Of(sourceHost);
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

            /// <summary>
            /// The object the last blocked resolution stopped at: the host itself, or a parent (or the prefab around
            /// it) that it would be created below. Only meaningful right after a call that returned a block reason.
            /// </summary>
            public Transform BlockedAt { get; private set; }

            private BlockReason Block(BlockReason reason, Transform at)
            {
                BlockedAt = at;
                return reason;
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
                    blockReason = Block(BlockReason.HostNeedsReview, sourceHost);
                    return;
                }

                // A bone without skin weights would be an empty object that merely looks like the bone
                if (plan.Map.SourceSkeleton.IsBone(sourceHost))
                {
                    blockReason = Block(BlockReason.BoneMissing, sourceHost);
                    return;
                }

                if (!plan.Settings.CreateMissingObjects || sourceHost.parent == null)
                {
                    blockReason = Block(BlockReason.HostUnmapped, sourceHost);
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
                if (source == null || !Hierarchy.IsInside(source, plan.Map.SourceRoot))
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
            /// <paramref name="transform"/> that the target lacks, see <see cref="NestedPrefabs.IsMissing"/>. Decided
            /// for the prefab as a whole, so that every object in it goes the same way, whichever is resolved first.
            /// </summary>
            private Transform FindMissingNestedPrefabRoot(Transform transform)
            {
                var path = new List<Transform>();
                for (var current = transform; current != null && current != plan.Map.SourceRoot; current = current.parent)
                    path.Add(current);

                for (int i = path.Count - 1; i >= 0; i--)
                {
                    if (NestedPrefabs.IsMissing(path[i], plan.Map)) return path[i];
                }

                return null;
            }

            private BlockReason PlanNestedPrefab(Transform prefabRoot)
            {
                if (ObjectsBySource.ContainsKey(prefabRoot)) return BlockReason.None;

                var mapping = plan.Map.Get(prefabRoot);
                if (mapping != null && mapping.State == MappingState.NeedsReview)
                    return Block(BlockReason.HostNeedsReview, prefabRoot);
                if (!plan.Settings.CreateMissingObjects) return Block(BlockReason.HostUnmapped, prefabRoot);

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

                    if (!Hierarchy.IsInside(transform, plan.Map.SourceRoot))
                    {
                        if (!EditorUtility.IsPersistent(value)) externalTransforms.Add(transform);
                        continue;
                    }

                    if (!(value is GameObject) && !(value is Transform)) continue;
                    if (!MissingObjects.IsEmpty(transform)) continue;

                    // Does nothing for mapped objects; bones, unconfirmed matches and the "create missing
                    // objects" setting are respected, in which case the reference stays unresolved, and where the
                    // resolution stopped is noted for the user
                    context.Resolve(transform, out _, out _, out var blockReason);
                    if (blockReason != BlockReason.None)
                        plan.ReferenceBlocks[transform] = new ReferenceBlock(blockReason, context.BlockedAt);
                    else
                        plan.ReferenceBlocks.Remove(transform);
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
                bool inKeyRoot = Hierarchy.IsInside(host.Source, plan.KeyRoot);
                foreach (var entry in ComponentScanner.ScanObject(
                             host.Source, inKeyRoot ? plan.KeyRoot : plan.Map.SourceRoot))
                {
                    if (alreadyPlanned.Contains(entry.Component)) continue;

                    bool leftOut = inKeyRoot && leftOutKeys.Contains(entry.Key);
                    plan.Components.Add(new PlannedComponent
                    {
                        Entry = entry,
                        HostToCreate = host,
                        Origin = leftOut ? ComponentOrigin.LeftOut : ComponentOrigin.Implicit,
                    });
                }
            }
        }

        /// <summary>Decides every component from scratch, so it can run again after components were added.</summary>
        private static void DecideActions(CopyPlan plan)
        {
            plan.ComponentsToRemove.Clear();
            plan.KeptComponents.Clear();

            foreach (var planned in plan.Components)
            {
                planned.KeptBy = null;

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
                        // What is removed and what stays is decided once every replaced type is known
                        planned.Existing = null;
                        planned.Action = ComponentScanner.ExactTypeComponents(planned.TargetHost, type).Count > 0
                            ? ComponentAction.Replace
                            : ComponentAction.Add;
                        break;

                    default:
                        planned.Action = ComponentAction.Add;
                        break;
                }
            }

            if (plan.Settings.ExistingPolicy == ExistingComponentPolicy.Replace) PlanRemovals(plan);
        }

        /// <summary>
        /// Replace removes the existing components of the replaced types from each receiving host, and adds the
        /// copies afterwards. Unity refuses to remove a component that another one requires, so such a component
        /// stays: the copy of the same index overwrites it in place, and the ones beyond the copies stay as they
        /// are. Decided here rather than while applying, so that the pre-check shows it.
        /// </summary>
        private static void PlanRemovals(CopyPlan plan)
        {
            // The copies that replace something are those whose host exists and has components of their type
            var replacing = plan.Components
                .Where(c => c.Action == ComponentAction.Replace)
                .GroupBy(c => (host: c.TargetHost, type: c.Entry.Type))
                .ToList();

            // A RequireComponent only reaches the components of its own GameObject, so each host is decided alone
            foreach (var byHost in replacing.GroupBy(copies => copies.Key.host))
            {
                var host = byHost.Key;
                var candidates = byHost
                    .SelectMany(copies => ComponentScanner.ExactTypeComponents(host, copies.Key.type))
                    .ToList();
                var kept = ComponentDependencies.FindKept(candidates);
                plan.ComponentsToRemove.AddRange(candidates.Where(c => !kept.ContainsKey(c)));

                foreach (var copies in byHost)
                {
                    // Paired by index, like the copies and the existing components under the Overwrite policy
                    var copyList = copies.ToList();
                    var stays = ComponentScanner.ExactTypeComponents(host, copies.Key.type)
                        .Where(kept.ContainsKey)
                        .ToList();
                    for (int i = 0; i < stays.Count; i++)
                    {
                        var component = stays[i];
                        string requiredBy = kept[component].GetType().Name;
                        var copy = i < copyList.Count ? copyList[i] : null;
                        if (copy != null)
                        {
                            copy.Action = ComponentAction.Overwrite;
                            copy.Existing = component;
                            copy.KeptBy = requiredBy;
                        }

                        plan.KeptComponents.Add(new KeptComponent
                        {
                            Component = component,
                            RequiredBy = requiredBy,
                            OverwrittenBy = copy,
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Works out the pose of every counterpart that exists, parents first, since a pose is expressed in the parent
        /// of its target. A mirror copy puts it at the mirror image of the source, with the source's size: the local
        /// values are carried through world space like the other mirrored values. Any other copy takes the local
        /// values as they are.
        /// </summary>
        private static void PlanPoses(CopyPlan plan)
        {
            // By the depth of the targets, which need not follow that of the sources: "Body/Ear_L" can pair with
            // "Head/Ear_R". Created and blocked ones have no values to plan and go first.
            plan.Poses = plan.Poses.OrderBy(p => p.Target != null ? Hierarchy.Depth(p.Target) : -1).ToList();

            foreach (var pose in plan.Poses)
            {
                if (pose.BlockReason != BlockReason.None)
                {
                    pose.Action = ComponentAction.Blocked;
                    continue;
                }

                // Placed at the pose when it is created
                if (pose.HostToCreate != null)
                {
                    pose.Action = ComponentAction.Add;
                    continue;
                }

                var source = pose.Source;
                var target = pose.Target;
                var targetParent = target.parent != null ? plan.FrameAfter(target.parent) : Frame.World;
                if (plan.Mirror != null)
                {
                    plan.Mirror.MirrorPose(source, targetParent, out pose.LocalPosition, out pose.LocalRotation,
                        out pose.LocalScale);
                }
                else
                {
                    pose.LocalPosition = source.localPosition;
                    pose.LocalRotation = source.localRotation;
                    pose.LocalScale = source.localScale;
                }

                pose.WorldFrame = targetParent.Child(pose.LocalPosition, pose.LocalRotation, pose.LocalScale);
                pose.Action = pose.Matches(target) ? ComponentAction.SkipIdentical : ComponentAction.Overwrite;
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

            if (!Hierarchy.IsInside(transform, plan.Map.SourceRoot))
            {
                if (EditorUtility.IsPersistent(value)) return null;
                return ClassifyExternalReference(plan, transform, value);
            }

            if (value is GameObject || value is Transform)
            {
                bool asGameObject = value is GameObject;

                // Planned objects first, for the same reason the host resolution checks nested prefabs first
                if (objectsBySource.TryGetValue(transform, out var toCreate))
                {
                    return new PlannedReference
                    {
                        Kind = ReferenceKind.InternalMapped,
                        Expected = TargetRef.ToObjectToCreate(toCreate, asGameObject),
                    };
                }

                if (plan.Map.TryResolve(transform, out var mapped))
                {
                    return new PlannedReference
                    {
                        Kind = ReferenceKind.InternalMapped,
                        Expected = TargetRef.ToObject(mapped, asGameObject),
                    };
                }

                return new PlannedReference { Kind = ReferenceKind.InternalUnresolved };
            }

            var component = (Component)value;

            if (plannedBySource.TryGetValue(component, out var plannedComponent))
            {
                return new PlannedReference
                {
                    Kind = ReferenceKind.NewComponent,
                    Expected = TargetRef.ToPlannedComponent(plannedComponent),
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
                        Expected = TargetRef.ToComponent(existing),
                    };
                }
            }

            // Only a component of the scanned source can be offered for selection
            return new PlannedReference
            {
                Kind = ReferenceKind.InternalUnresolved,
                MissingDependency = Hierarchy.IsInside(transform, plan.KeyRoot)
                    ? ComponentKey.For(transform, plan.KeyRoot, component.GetType(), index)
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
                Expected = TargetRef.Keep(value),
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
                    Expected = TargetRef.ToObject(counterpart, value is GameObject),
                };
            }

            var component = (Component)value;
            var existing = ComponentScanner.FindByTypeAndIndex(
                counterpart, component.GetType(), ComponentScanner.IndexAmongSameType(component));
            if (existing == null) return kept;

            return new PlannedReference
            {
                Kind = ReferenceKind.ExternalMapped,
                Expected = TargetRef.ToComponent(existing),
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
    }
}
