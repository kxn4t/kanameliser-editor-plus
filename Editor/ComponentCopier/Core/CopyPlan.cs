using System.Collections.Generic;
using UnityEditor;
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
        /// Set exactly for the components whose <see cref="PlannedComponent.Origin"/> is
        /// <see cref="ComponentOrigin.LeftOut"/>.
        /// </summary>
        LeftOut,
    }

    /// <summary>Why a component is part of the plan.</summary>
    internal enum ComponentOrigin
    {
        /// <summary>The user selected it.</summary>
        Selected,
        /// <summary>
        /// Not selected, but inside a nested prefab that gets instantiated. A prefab is brought over as a whole,
        /// so everything in it is made to match the source.
        /// </summary>
        Implicit,
        /// <summary>
        /// Inside such a prefab and unchecked on purpose. It still arrives with the prefab, so it is removed from
        /// the new instance afterwards.
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
        /// <summary>
        /// A reference has no counterpart and <see cref="UnresolvedReferencePolicy.SkipComponent"/> is on.
        /// Unlike the other reasons, the host may well exist.
        /// </summary>
        UnresolvedReference,
        /// <summary>
        /// A mirror copy would write the component onto its own side: the object is mapped to itself, which
        /// objects on the middle line are, or (by a manual mapping, ...) to another object on its side.
        /// </summary>
        SameSide,
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
        /// <summary>Name the object is created with: the source name, or its mirror image in a mirror copy.</summary>
        public string Name;
        public Transform ExistingParent;
        public PlannedObject ParentToCreate;

        /// <summary>
        /// Set when <see cref="Source"/> is the root of a nested prefab. The prefab is instantiated instead of
        /// rebuilding its objects, so the link to the prefab asset is kept.
        /// </summary>
        public GameObject PrefabAsset;

        /// <summary>
        /// The outermost nested prefab root this object arrives with, or null. Objects below an instantiated
        /// prefab usually exist already once the prefab is in place, found by the object of the asset they
        /// correspond to (the source may have renamed them); only the missing ones are created.
        /// </summary>
        public PlannedObject PrefabRoot;

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
    /// Each factory names one kind of value; a reference holds exactly one of them.
    /// </summary>
    internal sealed class TargetRef
    {
        private Transform existingObject;
        private bool asGameObject;
        private PlannedComponent plannedComponent;
        private Component existingComponent;
        private Object kept;

        /// <summary>The object the plan creates, when the reference points at one.</summary>
        public PlannedObject ObjectToCreate { get; private set; }

        private TargetRef()
        {
        }

        /// <summary>An object that exists in the target.</summary>
        public static TargetRef ToObject(Transform transform, bool asGameObject) =>
            new TargetRef { existingObject = transform, asGameObject = asGameObject };

        /// <summary>An object the plan creates (or instantiates with a prefab).</summary>
        public static TargetRef ToObjectToCreate(PlannedObject planned, bool asGameObject) =>
            new TargetRef { ObjectToCreate = planned, asGameObject = asGameObject };

        /// <summary>A component the plan writes.</summary>
        public static TargetRef ToPlannedComponent(PlannedComponent planned) =>
            new TargetRef { plannedComponent = planned };

        /// <summary>A component that exists in the target and is left alone.</summary>
        public static TargetRef ToComponent(Component component) =>
            new TargetRef { existingComponent = component };

        /// <summary>A value kept as it is (a scene object outside of the source).</summary>
        public static TargetRef Keep(Object value) => new TargetRef { kept = value };

        public Object Resolve()
        {
            if (kept != null) return kept;
            if (plannedComponent != null) return plannedComponent.Actual;
            if (existingComponent != null) return existingComponent;

            var transform = existingObject != null ? existingObject : ObjectToCreate?.Created;
            if (transform == null) return null;
            return asGameObject ? transform.gameObject : transform;
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

    /// <summary>
    /// A value a property gets instead of the source value: the mirrored positions, rotations and tags of a
    /// mirror copy. Only what the plan decided is stored, so that <see cref="CopyExecutor"/> writes and
    /// <see cref="CopyVerifier"/> checks the very same thing.
    /// </summary>
    internal sealed class PlannedValue
    {
        private const float PositionTolerance = 1e-4f;
        private const float AngleTolerance = 0.01f;

        public string PropertyPath;
        public SerializedPropertyType Type;
        public Vector3 VectorValue;
        public Quaternion QuaternionValue;
        public float FloatValue;
        /// <summary>A string, or the name of <see cref="EnumIndex"/> for display.</summary>
        public string StringValue;
        public int EnumIndex;
        public Bounds BoundsValue;
        /// <summary>A Vector3 of Euler angles: compared as a rotation, since several triples mean the same one.</summary>
        public bool IsEuler;

        public static PlannedValue OfVector(string path, Vector3 value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Vector3, VectorValue = value };

        public static PlannedValue OfEuler(string path, Vector3 value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Vector3, VectorValue = value, IsEuler = true };

        public static PlannedValue OfQuaternion(string path, Quaternion value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Quaternion, QuaternionValue = value };

        public static PlannedValue OfFloat(string path, float value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Float, FloatValue = value };

        public static PlannedValue OfString(string path, string value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.String, StringValue = value };

        public static PlannedValue OfBounds(string path, Bounds value) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Bounds, BoundsValue = value };

        public static PlannedValue OfEnum(string path, int index, string name) =>
            new PlannedValue { PropertyPath = path, Type = SerializedPropertyType.Enum, EnumIndex = index, StringValue = name };

        public void Write(SerializedProperty property)
        {
            switch (Type)
            {
                case SerializedPropertyType.Vector3: property.vector3Value = VectorValue; break;
                case SerializedPropertyType.Quaternion: property.quaternionValue = QuaternionValue; break;
                case SerializedPropertyType.Float: property.floatValue = FloatValue; break;
                case SerializedPropertyType.String: property.stringValue = StringValue; break;
                case SerializedPropertyType.Enum: property.enumValueIndex = EnumIndex; break;
                case SerializedPropertyType.Bounds: property.boundsValue = BoundsValue; break;
            }
        }

        /// <summary>Compared with a tolerance: the values went through world space and back.</summary>
        public bool Matches(SerializedProperty property)
        {
            if (property.propertyType != Type) return false;

            switch (Type)
            {
                case SerializedPropertyType.Vector3:
                    return IsEuler
                        ? AnglesMatch(Quaternion.Euler(VectorValue), Quaternion.Euler(property.vector3Value))
                        : PointsMatch(VectorValue, property.vector3Value);
                case SerializedPropertyType.Quaternion:
                    return AnglesMatch(QuaternionValue, property.quaternionValue);
                case SerializedPropertyType.Float:
                    return Mathf.Abs(FloatValue - property.floatValue) <=
                           PositionTolerance * Mathf.Max(1f, Mathf.Abs(FloatValue));
                case SerializedPropertyType.String:
                    return StringValue == property.stringValue;
                case SerializedPropertyType.Enum:
                    return EnumIndex == property.enumValueIndex;
                case SerializedPropertyType.Bounds:
                    return PointsMatch(BoundsValue.center, property.boundsValue.center) &&
                           PointsMatch(BoundsValue.size, property.boundsValue.size);
                default:
                    return false;
            }
        }

        public string Display()
        {
            switch (Type)
            {
                case SerializedPropertyType.Vector3: return VectorValue.ToString("G4");
                case SerializedPropertyType.Quaternion: return QuaternionValue.eulerAngles.ToString("G4");
                case SerializedPropertyType.Float: return FloatValue.ToString("G6");
                case SerializedPropertyType.String: return "\"" + StringValue + "\"";
                case SerializedPropertyType.Enum: return StringValue;
                case SerializedPropertyType.Bounds: return BoundsValue.ToString();
                default: return Type.ToString();
            }
        }

        private static bool PointsMatch(Vector3 a, Vector3 b) =>
            (a - b).magnitude <= PositionTolerance * Mathf.Max(1f, Mathf.Max(a.magnitude, b.magnitude));

        private static bool AnglesMatch(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) <= AngleTolerance;
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
        /// Property values that differ from the source on purpose: the mirrored positions, rotations and tags
        /// of a mirror copy. Written after the source values and compared by the diff check, which compares
        /// a counterpart that is kept (Skip) with them too.
        /// </summary>
        public List<PlannedValue> Values = new();

        /// <summary>
        /// Properties that depend on the axes of the bone (PhysBone limits, ...) and were copied as they are
        /// by a mirror copy, for the user to check. Empty outside of a mirror copy.
        /// </summary>
        public List<string> AxisDependentProperties = new();

        /// <summary>
        /// The references that hold the component back when <see cref="BlockReason"/> is
        /// <see cref="BlockReason.UnresolvedReference"/>. Kept apart from <see cref="References"/>, which
        /// describe what gets written: nothing does.
        /// </summary>
        public List<PlannedReference> UnresolvedReferences = new();

        public ComponentOrigin Origin;

        /// <summary>See <see cref="ComponentOrigin.Implicit"/>.</summary>
        public bool Implicit => Origin == ComponentOrigin.Implicit;

        /// <summary>See <see cref="ComponentOrigin.LeftOut"/>.</summary>
        public bool LeftOut => Origin == ComponentOrigin.LeftOut;

        /// <summary>
        /// Held back by <see cref="UnresolvedReferencePolicy.SkipComponent"/>: blocked like the others, but shown
        /// as a skip, since the host may well exist.
        /// </summary>
        public bool IsHeldBack => BlockReason == BlockReason.UnresolvedReference;

        /// <summary>
        /// Inside a nested prefab that gets added: the component arrives with the prefab, selected or not, and can
        /// only be kept from arriving by leaving it out.
        /// </summary>
        public bool ArrivesWithPrefab =>
            HostToCreate != null && (HostToCreate.IsPrefabRoot || HostToCreate.PrefabRoot != null);

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
        /// <see cref="AvatarRoots.Find"/>). Null when a side is not on an avatar.
        /// </summary>
        public Transform SourceAvatarRoot;
        public Transform TargetAvatarRoot;

        /// <summary>Set for a mirror copy: the plane the values are mirrored across. Null otherwise.</summary>
        public MirrorContext Mirror;

        /// <summary>
        /// The root the component keys are relative to: the source that was scanned. The source root of the map,
        /// except in a mirror copy, whose map spans the avatar around the source.
        /// </summary>
        public Transform KeyRoot;

        public List<PlannedComponent> Components = new();
        public List<PlannedObject> ObjectsToCreate = new();
        public List<BlockedObject> BlockedObjects = new();
        public List<Component> ComponentsToRemove = new();
        public List<BrokenReferenceWarning> BrokenReferences = new();
    }
}
