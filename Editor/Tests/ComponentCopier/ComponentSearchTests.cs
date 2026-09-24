using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// The search field of the component list. A regular expression that backtracks badly must not freeze the
    /// window, so a match that takes too long drops the pattern for the whole list, as an invalid pattern does.
    /// </summary>
    public class ComponentSearchTests : ComponentCopierTestBase
    {
        [Test]
        public void PatternThatTakesTooLong_IsIgnoredForTheWholeList()
        {
            // Hair is rejected before the long name is tried, and Skirt is never reached
            var source = CreateHierarchy("Source", "Hair", new string('a', 26), "Skirt");
            foreach (Transform child in source)
                child.gameObject.AddComponent<SphereCollider>();
            var entries = ComponentScanner.Scan(source);

            // Tries every split of the a's before it fails at the space after them. With 26 of them that takes
            // seconds, far beyond the limit, yet it would still end if the limit were lost instead of hanging the run.
            var visible = ComponentCopierWindow.FilterBySearch(entries, "^(a+)+$", useRegex: true,
                ComponentCopierWindow.SearchMatchTimeout, out bool invalidPattern, out bool timedOut);

            Assert.IsTrue(timedOut);
            Assert.IsFalse(invalidPattern);
            CollectionAssert.AreEqual(entries, visible, "Every row is listed, as with an empty search");
        }
    }
}
