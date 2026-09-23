using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// The window builds localization keys and USS classes from enum names. A value without its key shows the
    /// raw key, and one without its class loses its color, so every value that can reach the screen is checked.
    /// </summary>
    public class ComponentCopierStringsTests
    {
        private const string LocalizationFolder = "Packages/net.kanameliser.editor-plus/Editor/Localization";

        private static readonly Regex MessageId = new Regex(@"^msgid ""(.+)""\s*$", RegexOptions.Multiline);

        private static IEnumerable<T> Values<T>() where T : Enum => Enum.GetValues(typeof(T)).Cast<T>();

        private static IEnumerable<string> EnumDerivedKeys()
        {
            var keys = new List<string>();
            keys.AddRange(Values<ComponentAction>().Select(ComponentCopierStrings.ActionKey));
            keys.AddRange(new[] { ComponentAction.Skip, ComponentAction.SkipIdentical, ComponentAction.LeftOut }
                .Select(ComponentCopierStrings.ActionTooltipKey));
            keys.AddRange(Values<BlockReason>()
                .Where(r => r != BlockReason.None)
                .Select(ComponentCopierStrings.BlockReasonKey));
            // Matching components are not listed
            keys.AddRange(Values<DiffKind>()
                .Where(k => k != DiffKind.Match)
                .Select(ComponentCopierStrings.DiffKindKey));
            // Unmapped rows (None) and manual ones have notes of their own
            keys.AddRange(Values<MappingReason>()
                .Where(r => r != MappingReason.None && r != MappingReason.Manual)
                .Select(ComponentCopierStrings.MappingReasonKey));
            keys.AddRange(Values<ExistingComponentPolicy>().Select(ComponentCopierStrings.PolicyKey));
            keys.AddRange(Values<UnresolvedReferencePolicy>().Select(ComponentCopierStrings.UnresolvedPolicyKey));
            return keys;
        }

        [Test]
        public void EveryLanguage_HasTheKeysBuiltFromEnums()
        {
            var files = Directory.GetFiles(LocalizationFolder, "*.po");
            Assert.IsNotEmpty(files);

            var missing = new List<string>();
            foreach (var file in files)
            {
                var ids = new HashSet<string>(
                    MessageId.Matches(File.ReadAllText(file)).Cast<Match>().Select(m => m.Groups[1].Value));
                missing.AddRange(EnumDerivedKeys().Where(k => !ids.Contains(k)).Select(k => $"{Path.GetFileName(file)}: {k}"));
            }

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void StyleSheet_HasTheClassesBuiltFromEnums()
        {
            string styleSheet = File.ReadAllText(ComponentCopierWindow.UssPath);

            var classes = Values<ComponentAction>().Select(ComponentCopierStrings.StatusChipClass)
                .Concat(Values<MappingState>().Select(ComponentCopierStrings.MappingRowClass))
                .Concat(Values<DiffKind>().Where(k => k != DiffKind.Match).Select(ComponentCopierStrings.DiffRowClass));
            var missing = classes
                .Where(c => !Regex.IsMatch(styleSheet, @"\." + Regex.Escape(c) + @"(?![\w-])"))
                .ToList();

            Assert.IsEmpty(missing, string.Join("\n", missing));
        }
    }
}
