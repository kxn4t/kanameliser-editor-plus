using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus
{
    /// <summary>
    /// Shows a folder in the Project window the way a double-click on it does: the two-column layout lists the
    /// folder's contents, the one-column layout reveals the folder in the tree.
    /// </summary>
    internal static class ProjectWindowNavigator
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Show(ImportedFolderTarget target)
        {
            var folderToOpen = AssetDatabase.LoadAssetAtPath<Object>(target.FolderToOpen);
            var selection = target.FoldersToSelect
                .Select(AssetDatabase.LoadAssetAtPath<Object>)
                .Where(folder => folder != null)
                .ToArray();
            if (folderToOpen == null && selection.Length == 0) return;

            EditorUtility.FocusProjectWindow();
            if (selection.Length > 0) Selection.objects = selection;

            // Selecting a folder makes the two-column Project window show the folder's parent. Listing the
            // folder's own contents has to come a tick later, once the window has handled that selection.
            if (folderToOpen != null)
                EditorApplication.delayCall += () => ShowFolderContents(folderToOpen);
        }

        private static void ShowFolderContents(Object folder)
        {
            if (folder == null) return;
            if (!TryShowFolderContentsInTwoColumns(folder))
                EditorGUIUtility.PingObject(folder);
        }

        /// <summary>
        /// Uses the Project window's internal ShowFolderContents (what a double-click runs). Fails quietly,
        /// so that a change to Unity's internals degrades to a ping instead of an exception.
        /// </summary>
        private static bool TryShowFolderContentsInTwoColumns(Object folder)
        {
            try
            {
                var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                var browser = FindBrowser(browserType);
                if (browser == null) return false;

                var isTwoColumns = browserType.GetMethod("IsTwoColumns", AnyInstance, null, Type.EmptyTypes, null);
                if (isTwoColumns == null || !(bool)isTwoColumns.Invoke(browser, null)) return false;

                var showFolderContents = browserType.GetMethod(
                    "ShowFolderContents", AnyInstance, null, new[] { typeof(int), typeof(bool) }, null);
                if (showFolderContents == null) return false;

                showFolderContents.Invoke(browser, new object[] { folder.GetInstanceID(), true });
                browser.Repaint();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static EditorWindow FindBrowser(Type browserType)
        {
            if (browserType == null) return null;
            var lastInteracted = browserType.GetField("s_LastInteractedProjectBrowser", AnyStatic);
            if (lastInteracted?.GetValue(null) is EditorWindow window && window != null) return window;
            return Resources.FindObjectsOfTypeAll(browserType).OfType<EditorWindow>().FirstOrDefault();
        }
    }
}
