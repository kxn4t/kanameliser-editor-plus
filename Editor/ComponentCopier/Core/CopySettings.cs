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

    internal enum UnresolvedReferencePolicy
    {
        /// <summary>Copy the component and clear every reference that has no counterpart.</summary>
        Clear,
        /// <summary>
        /// Leave the component out as long as one of its references has no counterpart. A constraint
        /// without its source or a PhysBone without its colliders is only half of a component.
        /// </summary>
        SkipComponent,
    }

    /// <summary>
    /// Global copy options. Deliberately not per-component: per-item choices get tedious quickly.
    /// </summary>
    internal sealed class CopySettings
    {
        private const string PrefsPrefix = "Kanameliser.EditorPlus.ComponentCopier.";

        public ExistingComponentPolicy ExistingPolicy = ExistingComponentPolicy.Overwrite;
        public bool CreateMissingObjects = true;
        public UnresolvedReferencePolicy UnresolvedPolicy = UnresolvedReferencePolicy.Clear;

        /// <summary>
        /// Redirect references to the avatar around the source to the avatar around the target.
        /// Off keeps all of them as they are, e.g. for a gimmick that is meant to follow the other avatar.
        /// </summary>
        public bool RedirectExternalReferences = true;

        /// <summary>
        /// An independent copy, for a variation of the settings (the diff check always compares as Overwrite).
        /// Copies every field, so a new setting cannot be forgotten.
        /// </summary>
        public CopySettings Clone() => (CopySettings)MemberwiseClone();

        public static CopySettings Load()
        {
            // Unsaved settings keep the defaults of the field initializers
            var defaults = new CopySettings();
            return new CopySettings
            {
                ExistingPolicy = (ExistingComponentPolicy)EditorPrefs.GetInt(
                    PrefsPrefix + nameof(ExistingPolicy), (int)defaults.ExistingPolicy),
                CreateMissingObjects = EditorPrefs.GetBool(
                    PrefsPrefix + nameof(CreateMissingObjects), defaults.CreateMissingObjects),
                UnresolvedPolicy = (UnresolvedReferencePolicy)EditorPrefs.GetInt(
                    PrefsPrefix + nameof(UnresolvedPolicy), (int)defaults.UnresolvedPolicy),
                RedirectExternalReferences = EditorPrefs.GetBool(
                    PrefsPrefix + nameof(RedirectExternalReferences), defaults.RedirectExternalReferences),
            };
        }

        public void Save()
        {
            EditorPrefs.SetInt(PrefsPrefix + nameof(ExistingPolicy), (int)ExistingPolicy);
            EditorPrefs.SetBool(PrefsPrefix + nameof(CreateMissingObjects), CreateMissingObjects);
            EditorPrefs.SetInt(PrefsPrefix + nameof(UnresolvedPolicy), (int)UnresolvedPolicy);
            EditorPrefs.SetBool(PrefsPrefix + nameof(RedirectExternalReferences), RedirectExternalReferences);
        }
    }
}
