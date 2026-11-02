using System;
using System.Collections.Generic;
using System.Linq;

namespace Kanameliser.EditorPlus
{
    /// <summary>Where the Project window should go after a package import.</summary>
    internal sealed class ImportedFolderTarget
    {
        /// <summary>Folder whose contents are shown; <c>Assets</c> when the import spans several top-level folders.</summary>
        public string FolderToOpen { get; }

        /// <summary>Folders to select: the opened folder itself, or its touched children when it is <c>Assets</c>.</summary>
        public IReadOnlyList<string> FoldersToSelect { get; }

        public ImportedFolderTarget(string folderToOpen, IReadOnlyList<string> foldersToSelect)
        {
            FolderToOpen = folderToOpen;
            FoldersToSelect = foldersToSelect;
        }
    }

    /// <summary>
    /// Picks the folder that represents what a package import added to the project: the common ancestor of the
    /// imported files, ignoring the ones that landed in folders which already existed (a shop's shared folder, say).
    /// </summary>
    internal static class ImportedFolderResolver
    {
        public const string AssetsRoot = "Assets";
        private const string AssetsPrefix = AssetsRoot + "/";

        /// <param name="importedAssets">Asset paths reported by <c>OnPostprocessAllAssets</c>, files and folders alike.</param>
        /// <param name="existingFolders">
        /// Folders (as <c>Assets/...</c> paths) that existed before the import, or null when unknown; then every
        /// imported asset below a top-level folder counts as new content.
        /// </param>
        /// <param name="isFolder">Tells folder paths apart from file paths.</param>
        /// <returns>The target, or null when nothing under <c>Assets</c> was imported.</returns>
        public static ImportedFolderTarget Resolve(
            IEnumerable<string> importedAssets, ICollection<string> existingFolders, Func<string, bool> isFolder)
        {
            // The folders the imported files occupy. A package lists every folder it carries as well, but those
            // would always pull the answer up to the package's top-most folder, so they only count when the
            // package holds nothing else.
            var fileFolders = new List<string>();
            var folders = new List<string>();
            foreach (string path in importedAssets)
            {
                if (path == null || !path.StartsWith(AssetsPrefix, StringComparison.Ordinal)) continue;
                if (isFolder(path)) folders.Add(path);
                else fileFolders.Add(ParentOf(path));
            }
            var occupied = fileFolders.Count > 0 ? fileFolders : folders;
            if (occupied.Count == 0) return null;

            // Prefer what went into folders created by this import; a file dropped straight into Assets is not
            // the content either. An update that only overwrites files has none, and then the whole import is
            // the content.
            var content = occupied
                .Where(folder => folder != AssetsRoot && (existingFolders == null || !existingFolders.Contains(folder)))
                .ToList();
            if (content.Count == 0) content = occupied;

            string common = CommonAncestor(content);
            if (common != AssetsRoot)
                return new ImportedFolderTarget(common, new[] { common });

            var topLevel = content.Select(TopLevelFolder).Where(folder => folder != null).Distinct().ToList();
            if (topLevel.Count == 0) topLevel.Add(AssetsRoot);
            return new ImportedFolderTarget(AssetsRoot, topLevel);
        }

        /// <summary>Deepest folder that contains every given folder. Each path must be <c>Assets</c> or below it.</summary>
        public static string CommonAncestor(IReadOnlyList<string> folders)
        {
            string[] common = folders[0].Split('/');
            int length = common.Length;
            for (int i = 1; i < folders.Count; i++)
            {
                string[] parts = folders[i].Split('/');
                int shared = 0;
                while (shared < length && shared < parts.Length && common[shared] == parts[shared]) shared++;
                length = shared;
            }
            return string.Join("/", common, 0, length);
        }

        private static string ParentOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(0, slash);
        }

        /// <summary>The <c>Assets/X</c> folder a path is under, or null for <c>Assets</c> itself.</summary>
        private static string TopLevelFolder(string folder)
        {
            if (!folder.StartsWith(AssetsPrefix, StringComparison.Ordinal)) return null;
            int slash = folder.IndexOf('/', AssetsPrefix.Length);
            return slash < 0 ? folder : folder.Substring(0, slash);
        }
    }
}
