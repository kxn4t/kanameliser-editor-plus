using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum ComponentAction
    {
        Add,
        Overwrite,
        /// <summary>Added after the existing same-type components on the host are removed.</summary>
        Replace,
        /// <summary>Existing component is kept because of <see cref="ExistingComponentPolicy.Skip"/>.</summary>
        Skip,
        /// <summary>Existing component already equals the expected result.</summary>
        SkipIdentical,
        Blocked,
        /// <summary>
        /// Arrives with a nested prefab although the user left it out, and is removed from the new instance.
        /// </summary>
        LeftOut,
    }

    internal enum BlockReason
    {
        None,
        /// <summary>The host object only has an unconfirmed suggestion.</summary>
        HostNeedsReview,
        /// <summary>The host object has no counterpart and object creation is disabled.</summary>
        HostUnmapped,
        /// <summary>The host is a skinning bone that does not exist in the target. Bones are never created.</summary>
        BoneMissing,
    }

    internal enum ReferenceKind
    {
        /// <summary>Points into the source hierarchy and has a counterpart in the target.</summary>
        InternalMapped,
        /// <summary>Points into the source hierarchy but no counterpart could be determined.</summary>
        InternalUnresolved,
        /// <summary>Points to a component that is produced by this plan.</summary>
        NewComponent,
        /// <summary>Points to a scene object outside of the source hierarchy. Kept as is.</summary>
        ExternalScene,
        /// <summary>
        /// Points to a scene object outside of the source hierarchy (typically the avatar the source outfit
        /// sits on) that has a counterpart around the target, see <see cref="CopyPlan.ExternalMap"/>.
        /// </summary>
        ExternalMapped,
    }

    /// <summary>A GameObject that does not exist in the target yet and will be created.</summary>
    internal sealed class PlannedObject
    {
        public Transform Source;
        public Transform ExistingParent;
        public PlannedObject ParentToCreate;

        /// <summary>
        /// Set when <see cref="Source"/> is the root of a nested prefab. The prefab is instantiated instead of
        /// rebuilding its objects, so the link to the prefab asset is kept.
        /// </summary>
        public GameObject PrefabAsset;

        /// <summary>
        /// The nested prefab root this object arrives with, or null. Objects below an instantiated prefab
        /// usually exist already once the prefab is in place; only the missing ones are created.
        /// </summary>
        public PlannedObject PrefabRoot;

        /// <summary>Index among same-name siblings, used to find the object inside an instantiated prefab.</summary>
        public int SiblingOccurrence;

        /// <summary>
        /// True for an object inside a nested prefab that is removed from the new instance, because every
        /// component on it was left out. See <see cref="PlannedComponent.LeftOut"/>.
        /// </summary>
        public bool LeftOut;

        /// <summary>Filled in by <see cref="CopyExecutor"/>.</summary>
        public Transform Created;

        public bool IsPrefabRoot => PrefabAsset != null;
    }

    /// <summary>
    /// Expected reference value expressed on the target side.
    /// Resolved late because objects and components created by the plan do not exist at planning time.
    /// </summary>
    internal sealed class TargetRef
    {
        public Transform Transform;
        public PlannedObject ObjectToCreate;
        public bool AsGameObject;

        public PlannedComponent PlannedComponent;
        public Component ExistingComponent;

        /// <summary>Value that is kept unchanged (external scene objects).</summary>
        public Object Fixed;

        public Object Resolve()
        {
            if (Fixed != null) return Fixed;
            if (PlannedComponent != null) return PlannedComponent.Actual;
            if (ExistingComponent != null) return ExistingComponent;

            var transform = Transform != null ? Transform : ObjectToCreate?.Created;
            if (transform == null) return null;
            return AsGameObject ? transform.gameObject : transform;
        }
    }

    internal sealed class PlannedReference
    {
        public string PropertyPath;
        public Object SourceValue;
        public ReferenceKind Kind;

        /// <summary>Null when <see cref="Kind"/> is <see cref="ReferenceKind.InternalUnresolved"/>.</summary>
        public TargetRef Expected;

        /// <summary>
        /// Set when the reference is unresolved only because the referenced component is not part of the copy.
        /// The UI can offer to add it.
        /// </summary>
        public ComponentKey? MissingDependency;

        /// <summary>
        /// For the object half of an MA AvatarObjectReference: the property path of the path half, and the
        /// avatar the target-side path is relative to. Null for ordinary references.
        /// </summary>
        public string PathPropertyPath;
        public Transform TargetAvatarRoot;

        /// <summary>
        /// True when the path half is written along with the object half. A reference that is kept as it is
        /// keeps its path too.
        /// </summary>
        public bool RewritesPath => PathPropertyPath != null && Kind != ReferenceKind.ExternalScene;

        /// <summary>
        /// The path half once the copy is applied: empty when the reference is cleared, and null while the
        /// referenced object does not exist yet.
        /// </summary>
        public string ExpectedPath()
        {
            if (Kind == ReferenceKind.InternalUnresolved) return "";

            var resolved = Expected?.Resolve();
            if (resolved == null) return null;
            return AvatarObjectReferences.PathFor(resolved, TargetAvatarRoot) ?? "";
        }

        /// <summary>Property path for display: an AvatarObjectReference is shown as one field.</summary>
        public string DisplayPath =>
            PathPropertyPath != null ? PropertyPath.Substring(0, PropertyPath.LastIndexOf('.')) : PropertyPath;
    }

    internal sealed class PlannedComponent
    {
        public ComponentEntry Entry;

        public Transform TargetHost;
        public PlannedObject HostToCreate;

        /// <summary>Counterpart that already exists in the target (same type, same index), if any.</summary>
        public Component Existing;

        public ComponentAction Action;
        public BlockReason BlockReason;
        public List<PlannedReference> References = new();

        /// <summary>
        /// True for components that were not selected but belong to a nested prefab that gets instantiated.
        /// A prefab is brought over as a whole, so everything in it is made to match the source.
        /// </summary>
        public bool Implicit;

        /// <summary>
        /// True for a component inside a nested prefab that the user unchecked on purpose. The prefab still
        /// arrives as a whole, so the component is removed from the new instance afterwards.
        /// </summary>
        public bool LeftOut;

        /// <summary>Component written by <see cref="CopyExecutor"/>.</summary>
        public Component Result;

        /// <summary>The component that represents this entry in the target right now.</summary>
        public Component Actual => Result != null ? Result : Existing;

        public bool WillWrite =>
            Action == ComponentAction.Add || Action == ComponentAction.Overwrite || Action == ComponentAction.Replace;
    }

    /// <summary>A nested prefab or empty object the user asked to add that cannot be added right now.</summary>
    internal sealed class BlockedObject
    {
        public Transform Source;
        public BlockReason Reason;
    }

    /// <summary>Reference held by an untouched target component to a component that Replace will remove.</summary>
    internal sealed class BrokenReferenceWarning
    {
        public Component Holder;
        public string PropertyPath;
        public Component Removed;
    }

    /// <summary>
    /// Everything a copy will do, including the expected end state of every component.
    /// Apply executes it and the diff check compares it against reality, so both always agree.
    /// </summary>
    internal sealed class CopyPlan
    {
        public TransformMap Map;
        public CopySettings Settings;

        /// <summary>
        /// Maps the surroundings of the source (the avatar it sits on) to the surroundings of the target.
        /// Null when no reference points outside, or when both sides share the same surroundings.
        /// </summary>
        public TransformMap ExternalMap;

        /// <summary>
        /// The avatars the source and the target sit on, as MA sees them (see
        /// <see cref="AvatarObjectReferences.FindAvatarRoot"/>). Null when a side is not on an avatar.
        /// </summary>
        public Transform SourceAvatarRoot;
        public Transform TargetAvatarRoot;

        public List<PlannedComponent> Components = new();
        public List<PlannedObject> ObjectsToCreate = new();
        public List<BlockedObject> BlockedObjects = new();
        public List<Component> ComponentsToRemove = new();
        public List<BrokenReferenceWarning> BrokenReferences = new();
    }
}
