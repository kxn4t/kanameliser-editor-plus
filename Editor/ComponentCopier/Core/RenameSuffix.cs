using System.Text.RegularExpressions;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Suffixes added when an object is renamed to avoid a clash: "Armature.1" (Modular Avatar setups),
    /// "Hips.001" (Blender), "Collider (1)" (Unity). "_01" is deliberately not one of them: it usually numbers
    /// the links of a chain and is part of the real name.
    /// </summary>
    internal static class RenameSuffix
    {
        private static readonly Regex Pattern = new Regex(@"(\.\d+|\s\(\d+\))$", RegexOptions.Compiled);

        /// <summary>The name without its rename suffix. A name that is nothing but a suffix is kept.</summary>
        public static string Strip(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            return TrySplit(name, out var stem, out _) ? stem : name;
        }

        /// <summary>
        /// Splits "Hand_L.001" into "Hand_L" and ".001". False, with the whole name as the stem, when there is no
        /// suffix or nothing in front of it.
        /// </summary>
        public static bool TrySplit(string name, out string stem, out string suffix)
        {
            stem = name;
            suffix = "";
            if (string.IsNullOrEmpty(name)) return false;

            var match = Pattern.Match(name);
            if (!match.Success || match.Index == 0) return false;

            stem = name.Substring(0, match.Index);
            suffix = match.Value;
            return true;
        }
    }
}
