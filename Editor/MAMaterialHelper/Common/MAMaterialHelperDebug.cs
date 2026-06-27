using UnityEditor;

namespace Kanameliser.Editor.MAMaterialHelper.Common
{
    internal static class MAMaterialHelperDebug
    {
        private const string PrefKey = "KanameliserEP.VerboseMatchingLogs";

        [InitializeOnLoadMethod]
        private static void LoadFromPrefs()
        {
            VerboseMatchingLogs = EditorPrefs.GetBool(PrefKey, false);
        }

        internal static bool VerboseMatchingLogs { get; set; }

        [MenuItem("Tools/Kanameliser Editor Plus/[Settings]/Verbose Matching Logs")]
        private static void Toggle()
        {
            VerboseMatchingLogs = !VerboseMatchingLogs;
            EditorPrefs.SetBool(PrefKey, VerboseMatchingLogs);
        }

        [MenuItem("Tools/Kanameliser Editor Plus/[Settings]/Verbose Matching Logs", true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/Kanameliser Editor Plus/[Settings]/Verbose Matching Logs", VerboseMatchingLogs);
            return true;
        }
    }
}
