using System;
using System.Collections.Generic;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Stable identifier of a component inside a hierarchy.
    /// Survives rescans, so UI state (checks, foldouts) and diff rows can be carried over by key.
    /// </summary>
    internal readonly struct ComponentKey : IEquatable<ComponentKey>
    {
        public readonly string RelativePath;
        public readonly string TypeFullName;
        /// <summary>Index among components of the same type on the same GameObject.</summary>
        public readonly int Index;

        public ComponentKey(string relativePath, string typeFullName, int index)
        {
            RelativePath = relativePath ?? "";
            TypeFullName = typeFullName ?? "";
            Index = index;
        }

        public bool Equals(ComponentKey other) =>
            RelativePath == other.RelativePath && TypeFullName == other.TypeFullName && Index == other.Index;

        public override bool Equals(object obj) => obj is ComponentKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RelativePath, TypeFullName, Index);

        public override string ToString() => $"{RelativePath}|{TypeFullName}|{Index}";
    }

    internal enum ComponentCategory
    {
        Other,
        PhysBone,
        Constraint,
        ModularAvatar,
        /// <summary>Not a copy target by default (renderers, Animator, avatar descriptor, ...).</summary>
        ExcludedByDefault,
    }

    internal sealed class ComponentEntry
    {
        public ComponentKey Key;
        public Component Component;
        public Type Type;
        public Transform Host;
        public ComponentCategory Category;
    }

    /// <summary>
    /// Lists the copyable components below a root. Works on scene objects and prefab assets alike.
    /// </summary>
    internal static class ComponentScanner
    {
        internal const string PipelineManagerTypeName = "VRC.Core.PipelineManager";

        private static readonly HashSet<string> PhysBoneTypeNames = new()
        {
            "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone",
            "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider",
        };

        private static readonly HashSet<string> ExcludedTypeNames = new()
        {
            "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor",
            PipelineManagerTypeName,
        };

        private const string VrcConstraintNamespace = "VRC.SDK3.Dynamics.Constraint.Components";
        private const string ModularAvatarNamespace = "nadena.dev.modular_avatar";

        public static List<ComponentEntry> Scan(Transform root)
        {
            var entries = new List<ComponentEntry>();
            if (root == null) return entries;

            var components = new List<Component>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                string path = ObjectMatcher.GetRelativePathFromRoot(transform, root);
                var indexByType = new Dictionary<Type, int>();

                transform.GetComponents(components);
                foreach (var component in components)
                {
                    // Missing scripts come back as null
                    if (component == null || component is Transform) continue;

                    var type = component.GetType();
                    indexByType.TryGetValue(type, out var index);
                    indexByType[type] = index + 1;

                    entries.Add(new ComponentEntry
                    {
                        Key = new ComponentKey(path, type.FullName, index),
                        Component = component,
                        Type = type,
                        Host = transform,
                        Category = Categorize(type),
                    });
                }
            }

            return entries;
        }

        public static ComponentCategory Categorize(Type type)
        {
            string fullName = type.FullName ?? "";
            string ns = type.Namespace ?? "";

            if (PhysBoneTypeNames.Contains(fullName)) return ComponentCategory.PhysBone;
            if (typeof(IConstraint).IsAssignableFrom(type) || ns == VrcConstraintNamespace)
                return ComponentCategory.Constraint;
            if (ns.StartsWith(ModularAvatarNamespace, StringComparison.Ordinal))
                return ComponentCategory.ModularAvatar;

            if (typeof(Renderer).IsAssignableFrom(type) || type == typeof(MeshFilter) || type == typeof(Animator) ||
                ExcludedTypeNames.Contains(fullName))
                return ComponentCategory.ExcludedByDefault;

            return ComponentCategory.Other;
        }

        /// <summary>
        /// Returns the index-th component of the given type on a GameObject, or null.
        /// Matches the exact type so that subclasses do not shift the index.
        /// </summary>
        public static Component FindByTypeAndIndex(Transform host, Type type, int index)
        {
            if (host == null) return null;

            int seen = 0;
            foreach (var component in host.GetComponents(type))
            {
                if (component == null || component.GetType() != type) continue;
                if (seen == index) return component;
                seen++;
            }

            return null;
        }

        /// <summary>Index of a component among same-type components on its GameObject.</summary>
        public static int IndexAmongSameType(Component component)
        {
            var type = component.GetType();
            int index = 0;
            foreach (var other in component.GetComponents(type))
            {
                if (other == component) return index;
                if (other != null && other.GetType() == type) index++;
            }

            return 0;
        }
    }
}
