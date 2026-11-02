using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// After a .unitypackage import, shows the imported folder in the Project window so a new outfit or tool
    /// does not have to be hunted down. The import is tracked through the package-import callbacks and the
    /// imported paths come from the asset postprocessor; the state in between lives in SessionState because
    /// a package that carries scripts reloads the domain in the middle.
    /// </summary>
    [InitializeOnLoad]
    internal static class ImportedFolderOpener
    {
        private const string MenuPath = "Tools/Kanameliser Editor Plus/[Settings]/Open Imported Folder";
        private const string PreferenceKey = "KanameliserEP.OpenImportedFolder";
        private const string PendingKey = "KanameliserEP.OpenImportedFolder.Pending";
        private const string SnapshotKey = "KanameliserEP.OpenImportedFolder.FoldersBeforeImport";

        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(PreferenceKey, true);
            private set => EditorPrefs.SetBool(PreferenceKey, value);
        }

        static ImportedFolderOpener()
        {
            AssetDatabase.importPackageStarted += OnImportStarted;
            // These fire once the package's assets have been postprocessed, after the domain reload when the
            // package has scripts, so whatever is still pending here was an import that added nothing.
            AssetDatabase.importPackageCompleted += _ => ClearPending();
            AssetDatabase.importPackageCancelled += _ => ClearPending();
            AssetDatabase.importPackageFailed += (_, __) => ClearPending();
        }

        [MenuItem(MenuPath)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnImportStarted(string packageName)
        {
            if (!Enabled) return;
            SessionState.SetString(SnapshotKey, string.Join("\n", FoldersUnderAssets()));
            SessionState.SetBool(PendingKey, true);
        }

        private static void ClearPending()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(SnapshotKey);
        }

        /// <summary>Called for every import batch; acts on the first one during a package import that touched Assets.</summary>
        internal static void OnAssetsImported(string[] importedAssets)
        {
            if (!Enabled || !SessionState.GetBool(PendingKey, false)) return;

            var foldersBefore = new HashSet<string>(SessionState.GetString(SnapshotKey, string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
            var target = ImportedFolderResolver.Resolve(importedAssets, foldersBefore, AssetDatabase.IsValidFolder);
            if (target == null) return; // a batch of something else, such as a plugin under Packages

            ClearPending();
            // The Project window picks the new folders up on its own refresh; navigate once that is done.
            EditorApplication.delayCall += () => ProjectWindowNavigator.Show(target);
        }

        private static IEnumerable<string> FoldersUnderAssets()
        {
            string assets = Application.dataPath;
            IEnumerable<string> directories;
            try
            {
                directories = Directory.GetDirectories(assets, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                yield break; // treated as "nothing existed", which still finds the common ancestor of the import
            }
            foreach (string directory in directories)
                yield return ImportedFolderResolver.AssetsRoot + directory.Substring(assets.Length).Replace('\\', '/');
        }
    }

    internal sealed class ImportedFolderPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (importedAssets.Length > 0) ImportedFolderOpener.OnAssetsImported(importedAssets);
        }
    }
}
