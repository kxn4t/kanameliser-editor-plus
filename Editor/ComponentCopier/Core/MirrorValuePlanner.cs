using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Works out the mirrored values of the spatial properties of every component a mirror copy writes or
    /// compares with an existing counterpart (<see cref="SpatialPropertyTable"/>). The values end up in the
    /// plan, so that applying and the diff check agree on them. Axis-dependent values are only noted, see
    /// <see cref="SpatialKind.AxisDependent"/>.
    /// </summary>
    internal static class MirrorValuePlanner
    {
        public static void Plan(CopyPlan plan, IReadOnlyDictionary<Transform, PlannedObject> objectsBySource)
        {
            if (plan.Mirror == null) return;

            foreach (var planned in plan.Components)
            {
                planned.Values.Clear();
                planned.AxisDependentProperties.Clear();
                // A component that is kept (Skip, held back) is not written, but the diff check still compares
                // its counterpart with the mirror image
                if (!planned.WillWrite && planned.Existing == null) continue;

                var entries = SpatialPropertyTable.For(planned.Entry.Type);
                if (entries.Count == 0) continue;

                using var serializedObject = new SerializedObject(planned.Entry.Component);
                foreach (var property in ReferenceWalker.Leaves(serializedObject))
                {
                    foreach (var entry in entries)
                    {
                        // Entries of one pattern tell apart by their conditions, e.g. the up type of an Aim
                        if (!entry.TryMatch(property.propertyPath, out var referencePath)) continue;
                        if (!entry.Applies(serializedObject)) continue;

                        if (entry.Kind == SpatialKind.AxisDependent)
                        {
                            // Only what gets copied as it is needs checking
                            if (planned.WillWrite && (entry.NoteWhenNeutral || !IsNeutral(property)))
                                planned.AxisDependentProperties.Add(property.propertyPath);
                            break;
                        }

                        // An entry without a value (an up object that is not set, ...) leaves the property to
                        // the next one of its pattern, such as the world space fallback
                        var value = Mirror(plan, objectsBySource, planned, serializedObject, property, entry, referencePath);
                        if (value == null) continue;

                        planned.Values.Add(value);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// A value that leaves every axis alone: a zero offset, or an axis that is still affected (a freeze
        /// toggle is true while the axis is not frozen).
        /// </summary>
        private static bool IsNeutral(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector3: return property.vector3Value == Vector3.zero;
                case SerializedPropertyType.Float: return property.floatValue == 0f;
                case SerializedPropertyType.Boolean: return property.boolValue;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum: return property.intValue == 0;
                default: return false;
            }
        }

        private static PlannedValue Mirror(
            CopyPlan plan, IReadOnlyDictionary<Transform, PlannedObject> objectsBySource, PlannedComponent planned,
            SerializedObject serializedObject, SerializedProperty property, SpatialProperty entry, string referencePath)
        {
            var mirror = plan.Mirror;
            string path = property.propertyPath;

            switch (entry.Kind)
            {
                case SpatialKind.WorldDirection:
                    return property.propertyType == SerializedPropertyType.Vector3
                        ? PlannedValue.OfVector(path, mirror.MirrorWorldDirection(property.vector3Value))
                        : null;
                // Not -value: a roll of 0 would become -0, shown as such and no longer equal to a prefab's 0
                case SpatialKind.NegatedScalar:
                    return property.propertyType == SerializedPropertyType.Float
                        ? PlannedValue.OfFloat(path, 0f - property.floatValue)
                        : null;
                // Names without a side ("Head", "Hair") stay as they are and are no mirrored value
                case SpatialKind.SideTag:
                    return property.propertyType == SerializedPropertyType.String
                        ? ChangedString(path, property.stringValue, SideTags.Flip(property.stringValue))
                        : null;
                case SpatialKind.SidedName:
                    return property.propertyType == SerializedPropertyType.String &&
                           SideName.TryFlip(property.stringValue, out var flippedName)
                        ? PlannedValue.OfString(path, flippedName)
                        : null;
                case SpatialKind.SidedPath:
                    return property.propertyType == SerializedPropertyType.String
                        ? ChangedString(path, property.stringValue, string.Join("/", property.stringValue.Split('/')
                            .Select(name => SideName.TryFlip(name, out var flipped) ? flipped : name)))
                        : null;
                case SpatialKind.SidedEnum:
                    return FlipEnum(path, property);
            }

            if (!TryGetFrames(plan, objectsBySource, planned, serializedObject, entry, referencePath, out var from, out var to))
                return null;

            switch (entry.Kind)
            {
                case SpatialKind.Position when property.propertyType == SerializedPropertyType.Vector3:
                    return PlannedValue.OfVector(path, mirror.MirrorPoint(property.vector3Value, from, to));
                case SpatialKind.Direction when property.propertyType == SerializedPropertyType.Vector3:
                    return PlannedValue.OfVector(path, mirror.MirrorDirection(property.vector3Value, from, to));
                case SpatialKind.Vector when property.propertyType == SerializedPropertyType.Vector3:
                    return PlannedValue.OfVector(path, mirror.MirrorVector(property.vector3Value, from, to));
                case SpatialKind.Rotation when property.propertyType == SerializedPropertyType.Quaternion:
                    return PlannedValue.OfQuaternion(path, mirror.MirrorRotation(property.quaternionValue, from, to));
                case SpatialKind.Euler when property.propertyType == SerializedPropertyType.Vector3:
                    return PlannedValue.OfEuler(path, mirror.MirrorEuler(property.vector3Value, from, to));
                case SpatialKind.Bounds when property.propertyType == SerializedPropertyType.Bounds:
                    return PlannedValue.OfBounds(path, mirror.MirrorBounds(property.boundsValue, from, to));
                default:
                    return null;
            }
        }

        private static PlannedValue ChangedString(string path, string value, string flipped) =>
            flipped != value ? PlannedValue.OfString(path, flipped) : null;

        /// <summary>The enum value named after the other side ("HandLeft" → "HandRight"), when there is one.</summary>
        private static PlannedValue FlipEnum(string path, SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Enum) return null;

            var names = property.enumNames;
            int index = property.enumValueIndex;
            if (index < 0 || index >= names.Length || !SideName.TryFlip(names[index], out var flipped)) return null;

            int flippedIndex = Array.IndexOf(names, flipped);
            return flippedIndex >= 0 ? PlannedValue.OfEnum(path, flippedIndex, flipped) : null;
        }

        /// <summary>
        /// The frame a value is expressed in on the source side, and the frame of the counterpart on the
        /// target side. False when there is none, e.g. an empty constraint source slot.
        /// </summary>
        private static bool TryGetFrames(
            CopyPlan plan, IReadOnlyDictionary<Transform, PlannedObject> objectsBySource, PlannedComponent planned,
            SerializedObject serializedObject, SpatialProperty entry, string referencePath,
            out Frame from, out Frame to)
        {
            from = default;
            to = default;

            var host = planned.Entry.Host;
            var referenced = referencePath != null ? ReferencedTransform(plan, serializedObject, referencePath) : null;
            var anchor = Anchor(entry.Space, host, referenced, serializedObject);
            if (anchor == null) return false;

            // A reference without a counterpart is copied as None, and the other side falls back to the host
            // (a collider without a root transform sits on its own object). The value is expressed there, so
            // that it still lands on the mirror image. An empty constraint source slot has no such fallback.
            var targetAnchor = anchor;
            bool cleared = referenced != null && entry.Space != SpatialSpace.Reference &&
                           IsCleared(planned, referencePath);
            if (cleared)
            {
                targetAnchor = Anchor(entry.Space, host, null, serializedObject);
                if (targetAnchor == null) return false;
            }

            from = Frame.Of(anchor);
            // A local pose is read in the parent the counterpart actually sits below, which need not be the
            // counterpart of the source's parent: "Body/Ear_L" can pair with "Head/Ear_R"
            var poseOwner = PoseOwner(entry.Space, host, cleared ? null : referenced);
            to = poseOwner != null && TryGetCounterpartParentFrame(plan, objectsBySource, poseOwner, out var parentFrame)
                ? parentFrame
                : TargetFrame(plan, objectsBySource, targetAnchor);
            if (entry.IgnoreScale)
            {
                from = from.WithoutScale();
                to = to.WithoutScale();
            }

            return true;
        }

        private static Transform Anchor(
            SpatialSpace space, Transform host, Transform referenced, SerializedObject serializedObject)
        {
            var rootOrHost = referenced != null ? referenced : host;
            switch (space)
            {
                case SpatialSpace.Host: return host;
                case SpatialSpace.Parent: return host.parent;
                case SpatialSpace.Reference: return referenced;
                case SpatialSpace.RootOrHost: return rootOrHost;
                case SpatialSpace.ParentOfRootOrHost: return rootOrHost.parent;
                case SpatialSpace.ChainEnd: return ChainEnd(rootOrHost, IgnoredTransforms(serializedObject));
                default: return null;
            }
        }

        /// <summary>
        /// The object whose local pose a value in a parent space is: the host, or the Target Transform of a
        /// VRChat constraint while one is set. Null for the other spaces.
        /// </summary>
        private static Transform PoseOwner(SpatialSpace space, Transform host, Transform referenced)
        {
            switch (space)
            {
                case SpatialSpace.Parent: return host;
                case SpatialSpace.ParentOfRootOrHost: return referenced != null ? referenced : host;
                default: return null;
            }
        }

        private static bool IsCleared(PlannedComponent planned, string referencePath) =>
            planned.References.Any(r => r.PropertyPath == referencePath && r.Kind == ReferenceKind.InternalUnresolved);

        /// <summary>
        /// The frame of the parent the counterpart of a source transform sits below: the parent of the existing
        /// counterpart, or the parent a new one is created below. False when the counterpart is not known.
        /// </summary>
        private static bool TryGetCounterpartParentFrame(
            CopyPlan plan, IReadOnlyDictionary<Transform, PlannedObject> objectsBySource, Transform source,
            out Frame frame)
        {
            frame = default;
            if (objectsBySource.TryGetValue(source, out var toCreate))
            {
                if (toCreate.ExistingParent != null)
                    frame = plan.FrameAfter(toCreate.ExistingParent);
                else if (toCreate.ParentToCreate != null)
                    frame = TargetFrame(plan, objectsBySource, toCreate.ParentToCreate.Source);
                else
                    return false;
                return true;
            }

            if (!plan.Map.TryResolve(source, out var target) || target.parent == null) return false;
            frame = plan.FrameAfter(target.parent);
            return true;
        }

        /// <summary>
        /// The frame of the counterpart of a source transform once the plan is applied: the existing counterpart
        /// (where a pose of the plan moves it), or the pose the counterpart is created with. An object outside of
        /// the map is not going anywhere.
        /// </summary>
        internal static Frame TargetFrame(
            CopyPlan plan, IReadOnlyDictionary<Transform, PlannedObject> objectsBySource, Transform source)
        {
            if (objectsBySource.TryGetValue(source, out var toCreate))
                return toCreate.Created != null ? Frame.Of(toCreate.Created) : plan.Mirror.MirroredFrame(source);

            if (plan.Map.TryResolve(source, out var target)) return plan.FrameAfter(target);
            if (!Hierarchy.IsInside(source, plan.Map.SourceRoot)) return Frame.Of(source);

            return plan.Mirror.MirroredFrame(source);
        }

        private static Transform ReferencedTransform(CopyPlan plan, SerializedObject serializedObject, string path)
        {
            var property = serializedObject.FindProperty(path);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) return null;

            var value = property.objectReferenceValue;
            // An MA AvatarObjectReference may only carry the path half
            if (value == null && AvatarObjectReferences.TryGetPathProperty(property, out var pathProperty))
                value = AvatarObjectReferences.ResolvePath(pathProperty.stringValue, plan.SourceAvatarRoot);

            return ReferenceWalker.GetTransform(value);
        }

        /// <summary>
        /// The end of a bone chain as PhysBone follows it: the first child that is not ignored, all the way
        /// down. PhysBone applies the endpoint to every leaf; the first one stands in for the others.
        /// </summary>
        private static Transform ChainEnd(Transform bone, HashSet<Transform> ignored)
        {
            var current = bone;
            while (true)
            {
                Transform next = null;
                foreach (Transform child in current)
                {
                    if (ignored.Contains(child)) continue;
                    next = child;
                    break;
                }

                if (next == null) return current;
                current = next;
            }
        }

        /// <summary>The PhysBone Ignore Transforms. Their children are left out along with them.</summary>
        private static HashSet<Transform> IgnoredTransforms(SerializedObject serializedObject)
        {
            var ignored = new HashSet<Transform>();
            var list = serializedObject.FindProperty("ignoreTransforms");
            if (list == null || !list.isArray) return ignored;

            for (int i = 0; i < list.arraySize; i++)
            {
                var transform = ReferenceWalker.GetTransform(list.GetArrayElementAtIndex(i).objectReferenceValue);
                if (transform != null) ignored.Add(transform);
            }

            return ignored;
        }
    }
}
