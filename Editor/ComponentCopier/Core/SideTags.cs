using System.Collections.Generic;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Contact collision tags with a side: the standard tags ("HandL", "FingerIndexR", ...) and custom tags
    /// that carry a side marker.
    /// </summary>
    internal static class SideTags
    {
        private static readonly HashSet<string> SidedBases = new()
        {
            "Hand", "Foot", "Finger", "FingerIndex", "FingerMiddle", "FingerRing", "FingerLittle",
        };

        public static string Flip(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;

            char last = tag[tag.Length - 1];
            string stem = tag.Substring(0, tag.Length - 1);
            if ((last == 'L' || last == 'R') && SidedBases.Contains(stem))
                return stem + (last == 'L' ? 'R' : 'L');

            return SideName.TryFlip(tag, out var flipped) ? flipped : tag;
        }
    }
}
