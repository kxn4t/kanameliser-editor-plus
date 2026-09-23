using System;
using System.Reflection;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class CopySettingsTests
    {
        private static FieldInfo[] Fields => typeof(CopySettings).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// The diff check works on a copy of the settings. A setting the copy leaves behind makes the check compare
        /// against something else than Apply would write, which is how the unresolved-reference policy once got lost.
        /// </summary>
        [Test]
        public void Clone_CarriesEverySetting()
        {
            var settings = new CopySettings();
            foreach (var field in Fields)
                field.SetValue(settings, OtherValue(field, field.GetValue(settings)));

            var clone = settings.Clone();

            Assert.AreNotSame(settings, clone);
            foreach (var field in Fields)
                Assert.AreEqual(field.GetValue(settings), field.GetValue(clone), field.Name);
        }

        [Test]
        public void Clone_IsIndependentOfTheOriginal()
        {
            var settings = new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip };

            var clone = settings.Clone();
            clone.ExistingPolicy = ExistingComponentPolicy.Overwrite;

            Assert.AreEqual(ExistingComponentPolicy.Skip, settings.ExistingPolicy);
        }

        /// <summary>A value other than the default, so that a field the copy misses shows up.</summary>
        private static object OtherValue(FieldInfo field, object current)
        {
            if (field.FieldType == typeof(bool)) return !(bool)current;

            if (field.FieldType.IsEnum)
            {
                foreach (var value in Enum.GetValues(field.FieldType))
                {
                    if (!value.Equals(current)) return value;
                }
            }

            Assert.Fail($"No test value for {field.Name} ({field.FieldType.Name}); add one to {nameof(OtherValue)}");
            return null;
        }
    }
}
