using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Kanameliser.EditorPlus.Tests
{
    public class ImportedFolderResolverTests
    {
        // Folder paths end with "/" in these tests so one list can carry files and folders, like a package does.
        private static ImportedFolderTarget Resolve(IEnumerable<string> imported, params string[] existingFolders)
        {
            var paths = imported.ToList();
            var existing = existingFolders.Length == 0 ? null : new HashSet<string>(existingFolders);
            return ImportedFolderResolver.Resolve(
                paths.Select(p => p.TrimEnd('/')),
                existing,
                p => paths.Contains(p + "/"));
        }

        private static readonly string[] Outfit =
        {
            "Assets/Shop/", "Assets/Shop/Outfit/", "Assets/Shop/Outfit/Outfit.prefab",
            "Assets/Shop/Outfit/Materials/", "Assets/Shop/Outfit/Materials/Cloth.mat",
            "Assets/Shop/Outfit/Textures/", "Assets/Shop/Outfit/Textures/Cloth.png",
        };

        private static readonly string[] LilToon =
        {
            "Assets/lilToon/", "Assets/lilToon/Shader/", "Assets/lilToon/Shader/lilToon.shader",
            "Assets/lilToon/Editor/", "Assets/lilToon/Editor/lilToonInspector.cs",
        };

        [Test]
        public void OutfitAlone_OpensTheOutfitFolder()
        {
            var target = Resolve(Outfit);

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Outfit"));
            Assert.That(target.FoldersToSelect, Is.EqualTo(new[] { "Assets/Shop/Outfit" }));
        }

        [Test]
        public void BundledShaderIntoExistingFolder_IsIgnored()
        {
            var target = Resolve(Outfit.Concat(LilToon),
                "Assets/lilToon", "Assets/lilToon/Shader", "Assets/lilToon/Editor");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Outfit"));
        }

        [Test]
        public void SecondOutfitOfTheSameShop_OpensOnlyTheNewOutfit()
        {
            var imported = Outfit.Select(p => p.Replace("/Outfit", "/OutfitB")).Concat(LilToon);
            var target = Resolve(imported,
                "Assets/Shop", "Assets/Shop/Outfit", "Assets/Shop/Outfit/Materials", "Assets/Shop/Outfit/Textures",
                "Assets/lilToon", "Assets/lilToon/Shader", "Assets/lilToon/Editor");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/OutfitB"));
            Assert.That(target.FoldersToSelect, Is.EqualTo(new[] { "Assets/Shop/OutfitB" }));
        }

        [Test]
        public void SeveralNewTopLevelFolders_OpensAssetsAndSelectsThem()
        {
            var target = Resolve(Outfit.Concat(LilToon), "Assets/Other");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets"));
            Assert.That(target.FoldersToSelect, Is.EquivalentTo(new[] { "Assets/Shop", "Assets/lilToon" }));
        }

        [Test]
        public void OverwriteUpdateWithoutNewFolders_OpensTheCommonAncestorOfEverything()
        {
            var imported = new[] { "Assets/Shop/Outfit/Outfit.prefab", "Assets/Shop/Outfit/Materials/Cloth.mat" };
            var target = Resolve(imported, "Assets/Shop", "Assets/Shop/Outfit", "Assets/Shop/Outfit/Materials");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Outfit"));
        }

        [Test]
        public void FileDroppedDirectlyIntoAssets_DoesNotPullTheTargetUpToAssets()
        {
            var target = Resolve(Outfit.Append("Assets/readme.txt"));

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Outfit"));
        }

        [Test]
        public void OnlyFilesDirectlyInAssets_OpensAssetsItself()
        {
            var target = Resolve(new[] { "Assets/readme.txt", "Assets/license.txt" });

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets"));
            Assert.That(target.FoldersToSelect, Is.EqualTo(new[] { "Assets" }));
        }

        [Test]
        public void NoSnapshot_TreatsEverythingAsNewContent()
        {
            var target = Resolve(Outfit.Concat(LilToon));

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets"));
            Assert.That(target.FoldersToSelect, Is.EquivalentTo(new[] { "Assets/Shop", "Assets/lilToon" }));
        }

        [Test]
        public void PathsOutsideAssets_AreIgnored()
        {
            Assert.That(Resolve(new[] { "Packages/com.example/Runtime/Thing.cs", "ProjectSettings/Foo.asset" }), Is.Null);
            Assert.That(Resolve(new string[0]), Is.Null);
            Assert.That(Resolve(new[] { "AssetsBackup/Thing.prefab" }), Is.Null);
        }

        [Test]
        public void FoldersOnly_UsesTheFoldersThemselves()
        {
            var target = Resolve(new[] { "Assets/Shop/", "Assets/Shop/Empty/" }, "Assets/Shop");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Empty"));
        }

        [Test]
        public void FolderEntries_DoNotPullTheTargetAboveTheFiles()
        {
            // A package lists "Assets/Shop" as a folder entry even though every file sits under Assets/Shop/Outfit.
            var target = Resolve(Outfit, "Assets/Other");

            Assert.That(target.FolderToOpen, Is.EqualTo("Assets/Shop/Outfit"));
        }

        [Test]
        public void CommonAncestor_StopsAtWholeSegments()
        {
            Assert.That(ImportedFolderResolver.CommonAncestor(new[] { "Assets/Shop/OutfitA", "Assets/Shop/OutfitAB" }),
                Is.EqualTo("Assets/Shop"));
            Assert.That(ImportedFolderResolver.CommonAncestor(new[] { "Assets/Shop/Outfit", "Assets/Shop/Outfit/Materials" }),
                Is.EqualTo("Assets/Shop/Outfit"));
            Assert.That(ImportedFolderResolver.CommonAncestor(new[] { "Assets/Shop", "Assets" }), Is.EqualTo("Assets"));
        }
    }
}
