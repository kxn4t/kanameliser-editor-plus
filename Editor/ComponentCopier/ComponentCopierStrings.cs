using System;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Localization keys and USS classes that are named after enum values. Built in one place so that a test can
    /// check every value against the .po files and the style sheet: a renamed or added value would otherwise
    /// show its raw key, or lose its style, without any error.
    /// </summary>
    internal static class ComponentCopierStrings
    {
        public static string ActionKey(ComponentAction action) => "componentCopier.action." + Camel(action);

        /// <summary>Only <see cref="ComponentAction.Skip"/>, <see cref="ComponentAction.SkipIdentical"/> and <see cref="ComponentAction.LeftOut"/> explain themselves.</summary>
        public static string ActionTooltipKey(ComponentAction action) => ActionKey(action) + ":tooltip";

        public static string BlockReasonKey(BlockReason reason) => "componentCopier.blocked." + Camel(reason);

        public static string DiffKindKey(DiffKind kind) => "componentCopier.diff." + Camel(kind);

        public static string MappingReasonKey(MappingReason reason) => "componentCopier.mapping.reason." + Camel(reason);

        public static string PolicyKey(ExistingComponentPolicy policy) => "componentCopier.policy." + Camel(policy);

        public static string UnresolvedPolicyKey(UnresolvedReferencePolicy policy) =>
            "componentCopier.unresolvedPolicy." + Camel(policy);

        public static string StatusChipClass(ComponentAction action) => "status-chip--" + Lower(action);

        public static string MappingRowClass(MappingState state) => "mapping-row--" + Lower(state);

        public static string DiffRowClass(DiffKind kind) => "diff-row--" + Lower(kind);

        /// <summary>Only <see cref="DiffKind.UnresolvedReference"/> is styled; the other kinds share the row's look.</summary>
        public static string DiffPropertyClass(DiffKind kind) => "diff-property--" + Lower(kind);

        /// <summary>Enum value as a localization key segment ("SkipIdentical" becomes "skipIdentical").</summary>
        private static string Camel(Enum value)
        {
            string name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static string Lower(Enum value) => value.ToString().ToLowerInvariant();
    }
}
