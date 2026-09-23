using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum SpatialKind
    {
        /// <summary>A point in the space given by <see cref="SpatialSpace"/>.</summary>
        Position,
        /// <summary>A direction (unit axis) in that space.</summary>
        Direction,
        /// <summary>
        /// A displacement in that space: turned like a direction and scaled like a point, so zero stays zero.
        /// </summary>
        Vector,
        /// <summary>A Quaternion rotation in that space.</summary>
        Rotation,
        /// <summary>A rotation stored as Euler angles (degrees) in that space.</summary>
        Euler,
        /// <summary>A direction in world space.</summary>
        WorldDirection,
        /// <summary>A scalar that changes sign: a roll around an axis.</summary>
        NegatedScalar,
        /// <summary>A Bounds whose center is a point in that space; the size stays.</summary>
        Bounds,
        /// <summary>A Contact collision tag: "HandL" becomes "HandR".</summary>
        SideTag,
        /// <summary>A name with a side marker, such as a PhysBone or Contact parameter: "Ear_L" becomes "Ear_R".</summary>
        SidedName,
        /// <summary>A path of such names: "Hand_L/Ring_L" becomes "Hand_R/Ring_R".</summary>
        SidedPath,
        /// <summary>An enum value named after a side: "HandLeft" becomes "HandRight".</summary>
        SidedEnum,
        /// <summary>
        /// A value that depends on the bone's own axes (PhysBone limits, frozen axes). Its mirror image depends
        /// on the rig, so it is copied as it is and pointed out to the user, unless it is neutral (a zero
        /// offset, an axis that is not frozen).
        /// </summary>
        AxisDependent,
    }

    internal enum SpatialSpace
    {
        None,
        /// <summary>The object the component sits on.</summary>
        Host,
        /// <summary>The parent of the host: local position / rotation values.</summary>
        Parent,
        /// <summary>The transform referred to by <see cref="SpatialProperty.ReferencePattern"/>, e.g. a constraint source.</summary>
        Reference,
        /// <summary>
        /// That transform when set, else the host: PhysBone collider and Contact root transforms, the Target
        /// Transform of a VRChat constraint.
        /// </summary>
        RootOrHost,
        /// <summary>
        /// The parent of that transform (or of the host): the rest pose of a VRChat constraint, which is the local
        /// pose of its Target Transform when one is set.
        /// </summary>
        ParentOfRootOrHost,
        /// <summary>The last bone of the chain below that transform (or the host): PhysBone endpoints.</summary>
        ChainEnd,
    }

    /// <summary>
    /// A requirement on another property of the component: an int, enum or bool that holds one of the given
    /// values, or any non-zero value. Lets the space of a value follow a mode, such as the up type of an Aim.
    /// </summary>
    internal sealed class SpatialCondition
    {
        private readonly int[] values;

        public string PropertyPath { get; }

        private SpatialCondition(string propertyPath, int[] values)
        {
            PropertyPath = propertyPath;
            this.values = values;
        }

        public static SpatialCondition NonZero(string propertyPath) => new SpatialCondition(propertyPath, null);

        public static SpatialCondition Is(string propertyPath, params int[] values) =>
            new SpatialCondition(propertyPath, values);

        /// <summary>False when the property does not exist.</summary>
        public bool Holds(SerializedObject serializedObject)
        {
            var property = serializedObject.FindProperty(PropertyPath);
            if (property == null) return false;

            int value = property.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue ? 1 : 0
                : property.intValue;
            return values == null ? value != 0 : Array.IndexOf(values, value) >= 0;
        }
    }

    internal sealed class SpatialProperty
    {
        private static readonly Regex Wildcard = new Regex(@"\\\*", RegexOptions.Compiled);
        private static readonly SpatialCondition[] NoConditions = Array.Empty<SpatialCondition>();

        /// <summary>Property path; "*" stands for a run of digits (an array index or a numbered field).</summary>
        public string Pattern { get; }
        public SpatialKind Kind { get; }
        public SpatialSpace Space { get; }
        /// <summary>Path of the object reference that defines the space, with the same "*" as <see cref="Pattern"/>.</summary>
        public string ReferencePattern { get; }
        /// <summary>The entry applies only when all of these hold. Entries of one pattern are tried in order.</summary>
        public IReadOnlyList<SpatialCondition> When { get; }
        /// <summary>The frames count without scale: VRChat multiplies constraint source offsets by position and rotation only.</summary>
        public bool IgnoreScale { get; }
        /// <summary>
        /// For <see cref="SpatialKind.AxisDependent"/>: pointed out even at its neutral value. A PhysBone limit
        /// rotation of zero still lines the limit up with the axes of the bone.
        /// </summary>
        public bool NoteWhenNeutral { get; }

        private readonly Regex regex;

        public SpatialProperty(
            string pattern, SpatialKind kind, SpatialSpace space = SpatialSpace.None,
            string referencePattern = null, IReadOnlyList<SpatialCondition> when = null,
            bool ignoreScale = false, bool noteWhenNeutral = false)
        {
            Pattern = pattern;
            Kind = kind;
            Space = space;
            ReferencePattern = referencePattern;
            When = when ?? NoConditions;
            IgnoreScale = ignoreScale;
            NoteWhenNeutral = noteWhenNeutral;
            regex = new Regex("^" + Wildcard.Replace(Regex.Escape(pattern), "([0-9]+)") + "$", RegexOptions.Compiled);
        }

        /// <summary>
        /// True when <paramref name="propertyPath"/> is an instance of the pattern. Returns the reference path
        /// with the digits of the match filled in, or null when the space needs none.
        /// </summary>
        public bool TryMatch(string propertyPath, out string referencePath)
        {
            referencePath = null;
            var match = regex.Match(propertyPath);
            if (!match.Success) return false;

            if (ReferencePattern != null)
            {
                int group = 1;
                referencePath = Regex.Replace(ReferencePattern, @"\*", _ => match.Groups[group++].Value);
            }

            return true;
        }

        public bool Applies(SerializedObject serializedObject) => When.All(c => c.Holds(serializedObject));
    }

    /// <summary>
    /// Which properties of which component types hold positions, rotations and directions, and in which
    /// space. Kept as type names and property paths so that the assembly needs no reference to the SDKs.
    /// Used by the mirror copy; see issue #65 for the axis correction between avatars.
    /// </summary>
    internal static class SpatialPropertyTable
    {
        // WorldUpType of VRChat and Unity constraints alike
        private const int UpObjectRotation = 2;
        private const int UpVector = 3;

        // InheritMode of MA Mesh Settings
        private const int MeshSettingsSet = 1;
        private const int MeshSettingsSetOrInherit = 3;

        private static SpatialProperty P(
            string pattern, SpatialKind kind, SpatialSpace space = SpatialSpace.None, string reference = null,
            SpatialCondition[] when = null, bool ignoreScale = false, bool noteWhenNeutral = false) =>
            new SpatialProperty(pattern, kind, space, reference, when, ignoreScale, noteWhenNeutral);

        private static SpatialCondition[] If(params SpatialCondition[] conditions) => conditions;
        private static SpatialCondition NonZero(string path) => SpatialCondition.NonZero(path);
        private static SpatialCondition Is(string path, params int[] values) => SpatialCondition.Is(path, values);

        /// <summary>The freeze toggles of the three axes: "AffectsPosition" gives AffectsPositionX, Y and Z.</summary>
        private static IEnumerable<SpatialProperty> Frozen(string prefix) =>
            new[] { "X", "Y", "Z" }.Select(axis => P(prefix + axis, SpatialKind.AxisDependent));

        private static SpatialProperty[] Join(params IEnumerable<SpatialProperty>[] groups) =>
            groups.SelectMany(g => g).ToArray();

        private static SpatialProperty[] DynamicBoneCollider() => new[]
        {
            P("m_Center", SpatialKind.Position, SpatialSpace.Host),
            // The capsule axis: X, Y or Z of the host, so even X (0) depends on the host's axes
            P("m_Direction", SpatialKind.AxisDependent, noteWhenNeutral: true),
        };

        // The offsets that the Position / Rotation / Aim / LookAt constraints apply on top of their result are
        // axis dependent: the space they are applied in differs per constraint type, so they are pointed out
        // instead of guessed.
        private static readonly Dictionary<string, SpatialProperty[]> ByTypeName = new()
        {
            ["VRC.Dynamics.VRCPhysBoneBase"] = new[]
            {
                // An offset from each end bone in its own frame. Zero turns the endpoint off, so it has to stay zero.
                P("endpointPosition", SpatialKind.Vector, SpatialSpace.ChainEnd, "rootTransform"),
                P("limitRotation", SpatialKind.AxisDependent, when: If(NonZero("limitType")), noteWhenNeutral: true),
                P("parameter", SpatialKind.SidedName),
            },
            ["VRC.Dynamics.VRCPhysBoneColliderBase"] = new[]
            {
                P("position", SpatialKind.Position, SpatialSpace.RootOrHost, "rootTransform"),
                P("rotation", SpatialKind.Rotation, SpatialSpace.RootOrHost, "rootTransform"),
            },
            ["VRC.Dynamics.ContactBase"] = new[]
            {
                P("position", SpatialKind.Position, SpatialSpace.RootOrHost, "rootTransform"),
                P("rotation", SpatialKind.Rotation, SpatialSpace.RootOrHost, "rootTransform"),
                P("collisionTags.Array.data[*]", SpatialKind.SideTag),
            },
            ["VRC.Dynamics.ContactReceiver"] = new[]
            {
                P("parameter", SpatialKind.SidedName),
            },
            // The rest pose and the freeze toggles are declared per constraint type; the paths are the same
            ["VRC.Dynamics.VRCConstraintBase"] = Join(
                new[]
                {
                    P("PositionAtRest", SpatialKind.Position, SpatialSpace.ParentOfRootOrHost, "TargetTransform"),
                    P("RotationAtRest", SpatialKind.Euler, SpatialSpace.ParentOfRootOrHost, "TargetTransform"),
                },
                Frozen("AffectsPosition"), Frozen("AffectsRotation"), Frozen("AffectsScale")),
            ["VRC.Dynamics.ManagedTypes.VRCParentConstraintBase"] = new[]
            {
                P("Sources.source*.ParentPositionOffset", SpatialKind.Position, SpatialSpace.Reference,
                    "Sources.source*.SourceTransform", ignoreScale: true),
                P("Sources.source*.ParentRotationOffset", SpatialKind.Euler, SpatialSpace.Reference,
                    "Sources.source*.SourceTransform"),
                // Sources beyond the 16 fixed slots
                P("Sources.overflowList.Array.data[*].ParentPositionOffset", SpatialKind.Position,
                    SpatialSpace.Reference, "Sources.overflowList.Array.data[*].SourceTransform", ignoreScale: true),
                P("Sources.overflowList.Array.data[*].ParentRotationOffset", SpatialKind.Euler,
                    SpatialSpace.Reference, "Sources.overflowList.Array.data[*].SourceTransform"),
            },
            ["VRC.Dynamics.ManagedTypes.VRCPositionConstraintBase"] = new[]
            {
                P("PositionOffset", SpatialKind.AxisDependent),
            },
            ["VRC.Dynamics.ManagedTypes.VRCRotationConstraintBase"] = new[]
            {
                P("RotationOffset", SpatialKind.AxisDependent),
            },
            ["VRC.Dynamics.ManagedTypes.VRCWorldUpConstraintBase"] = new[]
            {
                P("RotationOffset", SpatialKind.AxisDependent),
            },
            ["VRC.Dynamics.ManagedTypes.VRCAimConstraintBase"] = new[]
            {
                P("AimAxis", SpatialKind.Direction, SpatialSpace.RootOrHost, "TargetTransform"),
                P("UpAxis", SpatialKind.Direction, SpatialSpace.RootOrHost, "TargetTransform"),
                // The world up vector lives where the up type says: in the up object, in the parent of the
                // target when solved in local space, else in world space
                P("WorldUpVector", SpatialKind.Direction, SpatialSpace.Reference, "WorldUpTransform",
                    when: If(Is("WorldUp", UpObjectRotation))),
                P("WorldUpVector", SpatialKind.Direction, SpatialSpace.ParentOfRootOrHost, "TargetTransform",
                    when: If(Is("WorldUp", UpVector), NonZero("SolveInLocalSpace"))),
                P("WorldUpVector", SpatialKind.WorldDirection),
            },
            ["VRC.Dynamics.ManagedTypes.VRCLookAtConstraintBase"] = new[]
            {
                P("Roll", SpatialKind.NegatedScalar),
            },
            ["UnityEngine.Animations.ParentConstraint"] = Join(
                new[]
                {
                    P("m_TranslationAtRest", SpatialKind.Position, SpatialSpace.Parent),
                    P("m_RotationAtRest", SpatialKind.Euler, SpatialSpace.Parent),
                    P("m_TranslationOffsets.Array.data[*]", SpatialKind.Position, SpatialSpace.Reference,
                        "m_Sources.Array.data[*].sourceTransform"),
                    P("m_RotationOffsets.Array.data[*]", SpatialKind.Euler, SpatialSpace.Reference,
                        "m_Sources.Array.data[*].sourceTransform"),
                },
                Frozen("m_AffectTranslation"), Frozen("m_AffectRotation")),
            ["UnityEngine.Animations.PositionConstraint"] = Join(
                new[]
                {
                    P("m_TranslationAtRest", SpatialKind.Position, SpatialSpace.Parent),
                    P("m_TranslationOffset", SpatialKind.AxisDependent),
                },
                Frozen("m_AffectTranslation")),
            ["UnityEngine.Animations.RotationConstraint"] = Join(
                new[]
                {
                    P("m_RotationAtRest", SpatialKind.Euler, SpatialSpace.Parent),
                    P("m_RotationOffset", SpatialKind.AxisDependent),
                },
                Frozen("m_AffectRotation")),
            ["UnityEngine.Animations.AimConstraint"] = Join(
                new[]
                {
                    P("m_RotationAtRest", SpatialKind.Euler, SpatialSpace.Parent),
                    P("m_AimVector", SpatialKind.Direction, SpatialSpace.Host),
                    P("m_UpVector", SpatialKind.Direction, SpatialSpace.Host),
                    P("m_WorldUpVector", SpatialKind.Direction, SpatialSpace.Reference, "m_WorldUpObject",
                        when: If(Is("m_UpType", UpObjectRotation))),
                    P("m_WorldUpVector", SpatialKind.WorldDirection),
                    P("m_RotationOffset", SpatialKind.AxisDependent),
                },
                Frozen("m_AffectRotation")),
            ["UnityEngine.Animations.LookAtConstraint"] = new[]
            {
                P("m_RotationAtRest", SpatialKind.Euler, SpatialSpace.Parent),
                P("m_Roll", SpatialKind.NegatedScalar),
                P("m_RotationOffset", SpatialKind.AxisDependent),
            },
            ["nadena.dev.modular_avatar.core.ModularAvatarMeshSettings"] = new[]
            {
                // Without a root bone MA takes the transform of each renderer, so there is no one frame to use.
                // Only Set and SetOrInherit apply the bounds; otherwise they are a leftover that stays as it is.
                P("Bounds", SpatialKind.Bounds, SpatialSpace.Reference, "RootBone.targetObject",
                    when: If(Is("InheritBounds", MeshSettingsSet, MeshSettingsSetOrInherit))),
            },
            ["nadena.dev.modular_avatar.core.ModularAvatarBoneProxy"] = new[]
            {
                // The bone to follow is kept as a humanoid bone and a path below it (or below the avatar)
                P("boneReference", SpatialKind.SidedEnum),
                P("subPath", SpatialKind.SidedPath),
            },
            ["nadena.dev.modular_avatar.core.ModularAvatarGlobalCollider"] = new[]
            {
                P("m_position", SpatialKind.Position, SpatialSpace.RootOrHost, "m_rootTransform.targetObject"),
                P("m_rotation", SpatialKind.Rotation, SpatialSpace.RootOrHost, "m_rootTransform.targetObject"),
                // Both sides hijacking the same collider of the avatar would overwrite each other
                P("m_colliderToHijack", SpatialKind.SidedEnum),
            },
            ["DynamicBone"] = new[]
            {
                // Added at every leaf, turned by the transform the component sits on
                P("m_EndOffset", SpatialKind.Direction, SpatialSpace.Host),
                P("m_Gravity", SpatialKind.WorldDirection),
                P("m_Force", SpatialKind.WorldDirection),
                P("m_FreezeAxis", SpatialKind.AxisDependent),
            },
            // Newer versions share a base type with the plane collider; older ones have the collider only
            ["DynamicBoneColliderBase"] = DynamicBoneCollider(),
            ["DynamicBoneCollider"] = DynamicBoneCollider(),
            // Unity physics colliders. Like the axis of a capsule, the size of a box lies along the axes of the
            // host. Joints are not covered: their limits turn around axes that depend on the rig.
            ["UnityEngine.SphereCollider"] = new[]
            {
                P("m_Center", SpatialKind.Position, SpatialSpace.Host),
            },
            ["UnityEngine.CapsuleCollider"] = new[]
            {
                P("m_Center", SpatialKind.Position, SpatialSpace.Host),
                P("m_Direction", SpatialKind.AxisDependent, noteWhenNeutral: true),
            },
            ["UnityEngine.BoxCollider"] = new[]
            {
                P("m_Center", SpatialKind.Position, SpatialSpace.Host),
                P("m_Size", SpatialKind.AxisDependent, noteWhenNeutral: true),
            },
        };

        private static readonly Dictionary<Type, IReadOnlyList<SpatialProperty>> Cache = new();

        /// <summary>The spatial properties of a component type, including those of its base types.</summary>
        public static IReadOnlyList<SpatialProperty> For(Type type)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;

            var result = new List<SpatialProperty>();
            for (var current = type; current != null; current = current.BaseType)
            {
                if (!ByTypeName.TryGetValue(current.FullName ?? "", out var entries)) continue;

                // A type registered along with its base (DynamicBoneCollider) repeats the entries of the base
                var derived = new HashSet<string>(result.Select(e => e.Pattern));
                result.AddRange(entries.Where(e => !derived.Contains(e.Pattern)));
            }

            Cache[type] = result;
            return result;
        }

        /// <summary>For tests: the type names that have entries.</summary>
        internal static IEnumerable<string> RegisteredTypeNames => ByTypeName.Keys;

        /// <summary>For tests: the entries registered under a type name.</summary>
        internal static IEnumerable<SpatialProperty> Registered(string typeName) =>
            ByTypeName.TryGetValue(typeName, out var entries) ? entries : Enumerable.Empty<SpatialProperty>();
    }
}
