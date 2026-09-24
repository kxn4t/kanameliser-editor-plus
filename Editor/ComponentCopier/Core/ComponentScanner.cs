using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEngine;
using UnityEngine.Animations;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Stable identifier of a component inside a hierarchy.
    /// Survives rescans, so UI state (checks, foldouts) and diff rows can be carried over by key.
    /// Two components are the same when they sit on the same object, told apart from same-name siblings by
    /// <see cref="ObjectPath"/>, and have the same type and index there.
    /// </summary>
    internal readonly struct ComponentKey : IEquatable<ComponentKey>
    {
        /// <summary>The host as it is shown: its plain path below the root ("Hips/Chain").</summary>
        public readonly string RelativePath;
        /// <summary>The host as it is identified, see <see cref="Hierarchy.IdentityPath"/> ("Hips#0/Chain#1").</summary>
        public readonly string ObjectPath;
        public readonly string TypeFullName;
        /// <summary>Index among components of the same type on the same GameObject.</summary>
        public readonly int Index;

        public ComponentKey(string objectPath, string relativePath, string typeFullName, int index)
        {
            ObjectPath = objectPath ?? "";
            RelativePath = relativePath ?? "";
            TypeFullName = typeFullName ?? "";
            Index = index;
        }

        /// <summary>The key of the index-th component of <paramref name="type"/> on <paramref name="host"/>.</summary>
        public static ComponentKey For(Transform host, Transform root, Type type, int index)
        {
            return new ComponentKey(
                Hierarchy.IdentityPath(host, root), ObjectMatcher.GetRelativePathFromRoot(host, root),
                type.FullName, index);
        }

        public bool Equals(ComponentKey other) =>
            ObjectPath == other.ObjectPath && TypeFullName == other.TypeFullName && Index == other.Index;

        public override bool Equals(object obj) => obj is ComponentKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ObjectPath, TypeFullName, Index);

        public override string ToString() => $"{ObjectPath}|{TypeFullName}|{Index}";
    }

    /// <summary>Declared in the order the categories are listed in the window.</summary>
    internal enum ComponentCategory
    {
        PhysBone,
        Contact,
        Constraint,
        ModularAvatar,
        /// <summary>Component of another non-destructive tool; <see cref="ComponentEntry.Tool"/> says which one.</summary>
        Tool,
        Other,
        /// <summary>Not a copy target by default (renderers, Animator, avatar descriptor, ...).</summary>
        ExcludedByDefault,
    }

    /// <summary>
    /// A non-destructive tool (AAO, VRCFury, TexTransTool, ...) that components of the source belong to.
    /// Found at scan time from the package that owns the component, so unknown tools are covered as well.
    /// </summary>
    internal sealed class ToolInfo
    {
        /// <summary>Package name, or the assembly name for tools that are not installed as a package.</summary>
        public string Id;
        public string DisplayName;
        /// <summary>Label of the preset chip; differs from the display name only for common abbreviations.</summary>
        public string ShortName;
    }

    internal sealed class ComponentEntry
    {
        public ComponentKey Key;
        public Component Component;
        public Type Type;
        public Transform Host;
        public ComponentCategory Category;
        /// <summary>Set only when <see cref="Category"/> is <see cref="ComponentCategory.Tool"/>.</summary>
        public ToolInfo Tool;
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
        private const string VrcContactNamespace = "VRC.SDK3.Dynamics.Contact.Components";
        private const string VrcNamespacePrefix = "VRC.";
        private const string ModularAvatarNamespace = "nadena.dev.modular_avatar";

        // Non-destructive tools mark their components with this interface so that the VRChat SDK ignores them.
        // Compared by name because the SDK is not referenced from this assembly.
        private const string EditorOnlyInterfaceName = "VRC.SDKBase.IEditorOnly";

        private static readonly Dictionary<string, string> ToolShortNames = new()
        {
            { "com.anatawa12.avatar-optimizer", "AAO" },
            { "nadena.dev.ndmf", "NDMF" },
        };

        private static readonly Dictionary<Type, (ComponentCategory category, ToolInfo tool)> CategoryCache = new();
        private static readonly Dictionary<Assembly, ToolInfo> ToolCache = new();

        public static List<ComponentEntry> Scan(Transform root)
        {
            var entries = new List<ComponentEntry>();
            if (root == null) return entries;

            var components = new List<Component>();
            var objectPaths = Hierarchy.IdentityPaths(root);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                AddEntries(transform, root, objectPaths[transform], entries, components);

            return entries;
        }

        /// <summary>The components of one object, keyed as a <see cref="Scan"/> of <paramref name="root"/> keys them.</summary>
        public static List<ComponentEntry> ScanObject(Transform transform, Transform root)
        {
            var entries = new List<ComponentEntry>();
            if (transform != null)
                AddEntries(transform, root, Hierarchy.IdentityPath(transform, root), entries, new List<Component>());
            return entries;
        }

        private static void AddEntries(
            Transform transform, Transform root, string objectPath, List<ComponentEntry> entries,
            List<Component> components)
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

                var category = Categorize(type, out var tool);
                entries.Add(new ComponentEntry
                {
                    Key = new ComponentKey(objectPath, path, type.FullName, index),
                    Component = component,
                    Type = type,
                    Host = transform,
                    Category = category,
                    Tool = tool,
                });
            }
        }

        public static ComponentCategory Categorize(Type type) => Categorize(type, out _);

        public static ComponentCategory Categorize(Type type, out ToolInfo tool)
        {
            if (!CategoryCache.TryGetValue(type, out var cached))
            {
                var category = CategorizeUncached(type, out var found);
                cached = (category, found);
                CategoryCache[type] = cached;
            }

            tool = cached.tool;
            return cached.category;
        }

        private static ComponentCategory CategorizeUncached(Type type, out ToolInfo tool)
        {
            tool = null;
            string fullName = type.FullName ?? "";
            string ns = type.Namespace ?? "";

            if (PhysBoneTypeNames.Contains(fullName)) return ComponentCategory.PhysBone;
            if (ns == VrcContactNamespace) return ComponentCategory.Contact;
            if (typeof(IConstraint).IsAssignableFrom(type) || ns == VrcConstraintNamespace)
                return ComponentCategory.Constraint;
            if (ns.StartsWith(ModularAvatarNamespace, StringComparison.Ordinal))
                return ComponentCategory.ModularAvatar;

            if (typeof(Renderer).IsAssignableFrom(type) || type == typeof(MeshFilter) || type == typeof(Animator) ||
                ExcludedTypeNames.Contains(fullName))
                return ComponentCategory.ExcludedByDefault;

            // The SDK's own editor-only components are not a tool of their own
            if (!ns.StartsWith(VrcNamespacePrefix, StringComparison.Ordinal) && IsEditorOnly(type))
            {
                tool = FindTool(type.Assembly);
                if (tool != null) return ComponentCategory.Tool;
            }

            return ComponentCategory.Other;
        }

        private static bool IsEditorOnly(Type type)
        {
            return type.GetInterfaces().Any(i => i.FullName == EditorOnlyInterfaceName);
        }

        /// <summary>
        /// Names the tool after the package that owns the assembly. Scripts imported into Assets fall back to
        /// the assembly name; the predefined assemblies say nothing about the tool and yield none.
        /// </summary>
        private static ToolInfo FindTool(Assembly assembly)
        {
            if (ToolCache.TryGetValue(assembly, out var tool)) return tool;

            var package = PackageInfo.FindForAssembly(assembly);
            if (package != null)
            {
                string displayName = string.IsNullOrEmpty(package.displayName) ? package.name : package.displayName;
                tool = new ToolInfo
                {
                    Id = package.name,
                    DisplayName = displayName,
                    ShortName = ToolShortNames.TryGetValue(package.name, out var shortName) ? shortName : displayName,
                };
            }
            else
            {
                string assemblyName = assembly.GetName().Name;
                if (!assemblyName.StartsWith("Assembly-CSharp", StringComparison.Ordinal))
                    tool = new ToolInfo { Id = assemblyName, DisplayName = assemblyName, ShortName = assemblyName };
            }

            ToolCache[assembly] = tool;
            return tool;
        }

        /// <summary>
        /// The components of exactly the given type on a GameObject, in order; empty for a null host.
        /// Subclasses are left out, so that they do not shift the indices of <see cref="ComponentKey"/>.
        /// </summary>
        public static List<Component> ExactTypeComponents(Transform host, Type type)
        {
            var result = new List<Component>();
            if (host == null) return result;

            foreach (var component in host.GetComponents(type))
            {
                if (component != null && component.GetType() == type) result.Add(component);
            }

            return result;
        }

        /// <summary>Returns the index-th component of exactly the given type on a GameObject, or null.</summary>
        public static Component FindByTypeAndIndex(Transform host, Type type, int index)
        {
            var components = ExactTypeComponents(host, type);
            return index >= 0 && index < components.Count ? components[index] : null;
        }

        /// <summary>Index of a component among same-type components on its GameObject.</summary>
        public static int IndexAmongSameType(Component component)
        {
            return Math.Max(0, ExactTypeComponents(component.transform, component.GetType()).IndexOf(component));
        }
    }
}
