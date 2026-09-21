using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum DiffKind
    {
        Match,
        ValueMismatch,
        ReferenceMismatch,
        /// <summary>The source reference has no counterpart in the target, so it cannot be carried over.</summary>
        UnresolvedReference,
        MissingOnTarget,
        ExtraOnTarget,
    }

    internal sealed class PropertyDiff
    {
        public string PropertyPath;
        public string DisplayName;
        public DiffKind Kind;
        public string Expected;
        public string Actual;
    }

    internal sealed class ComponentDiff
    {
        /// <summary>Null for <see cref="DiffKind.ExtraOnTarget"/>.</summary>
        public PlannedComponent Planned;
        /// <summary>The target component that was compared, or the extra component.</summary>
        public Component Actual;
        public DiffKind Kind;
        public List<PropertyDiff> Properties = new();
        /// <summary>True when more property differences exist than were recorded.</summary>
        public bool Truncated;
    }

    internal sealed class DiffReport
    {
        public List<ComponentDiff> Components = new();

        public int Count(DiffKind kind) => Components.Count(c => c.Kind == kind);
    }

    /// <summary>
    /// Compares the expected end state described by a <see cref="CopyPlan"/> with the actual target.
    /// Used as a preview before applying, as a verification after applying, and standalone.
    /// </summary>
    internal static class CopyVerifier
    {
        private const int MaxPropertyDiffs = 50;
        private const float RelativeTolerance = 1e-5f;

        public static DiffReport Verify(CopyPlan plan)
        {
            var report = new DiffReport();

            foreach (var planned in plan.Components)
                report.Components.Add(Compare(planned, planned.Actual));

            foreach (var extra in FindExtraComponents(plan))
                report.Components.Add(new ComponentDiff { Actual = extra, Kind = DiffKind.ExtraOnTarget });

            return report;
        }

        public static ComponentDiff Compare(PlannedComponent planned, Component actual)
        {
            var diff = new ComponentDiff { Planned = planned, Actual = actual, Kind = DiffKind.Match };

            if (actual == null)
            {
                diff.Kind = DiffKind.MissingOnTarget;
                return diff;
            }

            var references = new Dictionary<string, PlannedReference>();
            foreach (var reference in planned.References)
                references[reference.PropertyPath] = reference;

            using var sourceObject = new SerializedObject(planned.Entry.Component);
            using var actualObject = new SerializedObject(actual);

            foreach (var sourceProperty in ReferenceWalker.Leaves(sourceObject))
            {
                var propertyDiff = CompareProperty(sourceProperty, actualObject, references);
                if (propertyDiff == null) continue;

                if (diff.Properties.Count >= MaxPropertyDiffs)
                {
                    diff.Truncated = true;
                    break;
                }

                diff.Properties.Add(propertyDiff);
            }

            if (diff.Properties.Any(p => p.Kind == DiffKind.ValueMismatch))
                diff.Kind = DiffKind.ValueMismatch;
            else if (diff.Properties.Any(p => p.Kind == DiffKind.ReferenceMismatch))
                diff.Kind = DiffKind.ReferenceMismatch;
            else if (diff.Properties.Count > 0)
                diff.Kind = DiffKind.UnresolvedReference;

            return diff;
        }

        /// <summary>
        /// True when writing the plan would not change the existing component at all.
        /// Unresolved references do not count: clearing them is still a no-op only if they are already empty.
        /// </summary>
        public static bool IsIdentical(PlannedComponent planned, Component existing)
        {
            var diff = Compare(planned, existing);
            return diff.Properties.All(p => p.Kind == DiffKind.UnresolvedReference && p.Actual == NoneText);
        }

        private const string NoneText = "None";

        private static PropertyDiff CompareProperty(
            SerializedProperty sourceProperty, SerializedObject actualObject,
            Dictionary<string, PlannedReference> references)
        {
            string path = sourceProperty.propertyPath;
            var actualProperty = actualObject.FindProperty(path);

            if (actualProperty == null)
            {
                // Elements beyond the target's array length. The array size leaf already reports the cause.
                if (path.Contains(".Array.data[")) return null;
                return Diff(sourceProperty, DiffKind.ValueMismatch, ValueToString(sourceProperty), "-");
            }

            if (sourceProperty.propertyType == SerializedPropertyType.ObjectReference)
            {
                var actualValue = actualProperty.objectReferenceValue;

                if (references.TryGetValue(path, out var reference))
                {
                    if (reference.Kind == ReferenceKind.InternalUnresolved)
                    {
                        return Diff(sourceProperty, DiffKind.UnresolvedReference,
                            ObjectToString(reference.SourceValue), ObjectToString(actualValue));
                    }

                    var expected = reference.Expected.Resolve();
                    return expected == actualValue
                        ? null
                        : Diff(sourceProperty, DiffKind.ReferenceMismatch,
                            ExpectedToString(reference, expected), ObjectToString(actualValue));
                }

                var sourceValue = sourceProperty.objectReferenceValue;
                return sourceValue == actualValue
                    ? null
                    : Diff(sourceProperty, DiffKind.ReferenceMismatch,
                        ObjectToString(sourceValue), ObjectToString(actualValue));
            }

            if (SerializedProperty.DataEquals(sourceProperty, actualProperty)) return null;
            if (ApproximatelyEqual(sourceProperty, actualProperty)) return null;

            return Diff(sourceProperty, DiffKind.ValueMismatch,
                ValueToString(sourceProperty), ValueToString(actualProperty));
        }

        private static PropertyDiff Diff(SerializedProperty property, DiffKind kind, string expected, string actual)
        {
            return new PropertyDiff
            {
                PropertyPath = property.propertyPath,
                DisplayName = property.propertyPath.Replace(".Array.data[", "["),
                Kind = kind,
                Expected = expected,
                Actual = actual,
            };
        }

        /// <summary>
        /// Components of the copied types that exist only in the target: either on an object without a source
        /// counterpart, or where the source counterpart has no such component. Components the user merely
        /// left unselected are not reported.
        /// </summary>
        private static IEnumerable<Component> FindExtraComponents(CopyPlan plan)
        {
            var types = new HashSet<Type>(plan.Components.Select(c => c.Entry.Type));
            if (types.Count == 0 || plan.Map.TargetRoot == null) yield break;

            var accounted = new HashSet<Component>(plan.Components.Select(c => c.Actual).Where(c => c != null));
            var removed = new HashSet<Component>(plan.ComponentsToRemove.Where(c => c != null));

            var sourceByTarget = new Dictionary<Transform, Transform>();
            foreach (var mapping in plan.Map.All)
            {
                if (mapping.IsUsable && !sourceByTarget.ContainsKey(mapping.Target))
                    sourceByTarget[mapping.Target] = mapping.Source;
            }

            foreach (var transform in plan.Map.TargetRoot.GetComponentsInChildren<Transform>(true))
            {
                foreach (var type in types)
                {
                    int index = 0;
                    foreach (var component in transform.GetComponents(type))
                    {
                        if (component == null || component.GetType() != type) continue;
                        int currentIndex = index++;

                        if (accounted.Contains(component) || removed.Contains(component)) continue;

                        sourceByTarget.TryGetValue(transform, out var source);
                        if (ComponentScanner.FindByTypeAndIndex(source, type, currentIndex) != null) continue;

                        yield return component;
                    }
                }
            }
        }

        #region Value helpers

        private static bool ApproximatelyEqual(SerializedProperty a, SerializedProperty b)
        {
            if (a.propertyType != b.propertyType) return false;

            switch (a.propertyType)
            {
                case SerializedPropertyType.Float:
                    return Approximately(a.floatValue, b.floatValue);
                case SerializedPropertyType.Vector2:
                    return Approximately(a.vector2Value, b.vector2Value);
                case SerializedPropertyType.Vector3:
                    return Approximately(a.vector3Value, b.vector3Value);
                case SerializedPropertyType.Vector4:
                    return Approximately(a.vector4Value, b.vector4Value);
                case SerializedPropertyType.Quaternion:
                    var qa = a.quaternionValue;
                    var qb = b.quaternionValue;
                    return Approximately(new Vector4(qa.x, qa.y, qa.z, qa.w), new Vector4(qb.x, qb.y, qb.z, qb.w));
                default:
                    return false;
            }
        }

        private static bool Approximately(float a, float b)
        {
            float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)));
            return Mathf.Abs(a - b) <= RelativeTolerance * scale;
        }

        private static bool Approximately(Vector4 a, Vector4 b)
        {
            return Approximately(a.x, b.x) && Approximately(a.y, b.y) &&
                   Approximately(a.z, b.z) && Approximately(a.w, b.w);
        }

        private static string ValueToString(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return property.longValue.ToString();
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return property.doubleValue.ToString("G6");
                case SerializedPropertyType.String:
                    return "\"" + property.stringValue + "\"";
                case SerializedPropertyType.Enum:
                    int index = property.enumValueIndex;
                    var names = property.enumDisplayNames;
                    return index >= 0 && index < names.Length ? names[index] : property.intValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return ObjectToString(property.objectReferenceValue);
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString("G4");
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString("G4");
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString("G4");
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString("G4");
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                default:
                    return property.propertyType.ToString();
            }
        }

        private static string ExpectedToString(PlannedReference reference, Object resolved)
        {
            if (resolved != null) return ObjectToString(resolved);

            // Not created yet (preview before applying): describe it by its source
            return ObjectToString(reference.SourceValue);
        }

        private static string ObjectToString(Object value)
        {
            if (value == null) return NoneText;
            return value is GameObject || value is Transform
                ? value.name
                : $"{value.name} ({value.GetType().Name})";
        }

        #endregion
    }
}
