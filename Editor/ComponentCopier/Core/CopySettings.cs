using UnityEditor;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    internal enum ExistingComponentPolicy
    {
        /// <summary>Copy values onto the existing component. Keeps references held by others intact.</summary>
        Overwrite,
        /// <summary>Always add a new component next to the existing ones.</summary>
        Add,
        /// <summary>Leave the existing component untouched.</summary>
        Skip,
        /// <summary>Remove existing components of the same type on the receiving object, then add.</summary>
        Replace,
    }

    /// <summary>
    /// Global copy options. Deliberately not per-component: per-item choices get tedious quickly.
    /// </summary>
    internal sealed class CopySettings
    {
        private const string PrefsPrefix = "Kanameliser.EditorPlus.ComponentCopier.";

        public ExistingComponentPolicy ExistingPolicy = ExistingComponentPolicy.Overwrite;
        public bool CreateMissingObjects = true;

        public static CopySettings Load()
        {
            return new CopySettings
            {
                ExistingPolicy = (ExistingComponentPolicy)EditorPrefs.GetInt(
                    PrefsPrefix + nameof(ExistingPolicy), (int)ExistingComponentPolicy.Overwrite),
                CreateMissingObjects = EditorPrefs.GetBool(PrefsPrefix + nameof(CreateMissingObjects), true),
            };
        }

        public void Save()
        {
            EditorPrefs.SetInt(PrefsPrefix + nameof(ExistingPolicy), (int)ExistingPolicy);
            EditorPrefs.SetBool(PrefsPrefix + nameof(CreateMissingObjects), CreateMissingObjects);
        }
    }
}
