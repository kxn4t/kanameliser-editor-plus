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
        public static CopyPlan Build(IEnumerable<ComponentEntry> selected, TransformMap map, CopySettings settings)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            settings ??= new CopySettings();

            var plan = new CopyPlan { Map = map, Settings = settings };
            var objectsBySource = new Dictionary<Transform, PlannedObject>();

            foreach (var entry in selected)
            {
                var planned = new PlannedComponent { Entry = entry };
                ResolveHost(plan, objectsBySource, entry.Host, out planned.TargetHost, out planned.HostToCreate,
                    out planned.BlockReason);
                plan.Components.Add(planned);
            }

            DecideActions(plan);
            CollectReferences(plan, objectsBySource);

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

        private static void ResolveHost(
            CopyPlan plan, Dictionary<Transform, PlannedObject> objectsBySource, Transform sourceHost,
            out Transform existing, out PlannedObject toCreate, out BlockReason blockReason)
        {
            existing = null;
            toCreate = null;
            blockReason = BlockReason.None;

            if (plan.Map.TryResolve(sourceHost, out existing)) return;

            if (objectsBySource.TryGetValue(sourceHost, out toCreate)) return;

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

            ResolveHost(plan, objectsBySource, sourceHost.parent, out var parent, out var parentToCreate,
                out blockReason);
            if (blockReason != BlockReason.None) return;

            toCreate = new PlannedObject
            {
                Source = sourceHost,
                ExistingParent = parent,
                ParentToCreate = parentToCreate,
            };
            objectsBySource[sourceHost] = toCreate;
            // Parents are resolved first, so this list is already in creation order
            plan.ObjectsToCreate.Add(toCreate);
        }

        private static void DecideActions(CopyPlan plan)
        {
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

        private static void CollectReferences(CopyPlan plan, Dictionary<Transform, PlannedObject> objectsBySource)
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

                    var reference = ClassifyReference(plan, objectsBySource, plannedBySource, value);
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
                return new PlannedReference
                {
                    Kind = ReferenceKind.ExternalScene,
                    Expected = new TargetRef { Fixed = value },
                };
            }

            if (value is GameObject || value is Transform)
            {
                var targetRef = new TargetRef { AsGameObject = value is GameObject };

                if (plan.Map.TryResolve(transform, out targetRef.Transform) ||
                    objectsBySource.TryGetValue(transform, out targetRef.ObjectToCreate))
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
}
