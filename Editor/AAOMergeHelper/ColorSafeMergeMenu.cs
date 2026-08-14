using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if AVATAR_OPTIMIZER_INSTALLED && MODULAR_AVATAR_INSTALLED
using Anatawa12.AvatarOptimizer;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.runtime;
#endif

namespace Kanameliser.Editor.AAOMergeHelper
{
    /// <summary>
    /// Hierarchy context menus that create an AAO Merge Skinned Mesh pre-configured so
    /// that MA Material Setter / Material Swap color menus keep working after the merge:
    /// materials changed non-uniformly across the merged renderers are excluded from
    /// material slot merging.
    ///
    /// Three source-selection variants are provided:
    /// - Color Menu Safe: merge the selected renderers as-is
    /// - From Object Toggle: merge the renderers targeted by the clicked MA Object
    ///   Toggle, grouped by the active state the toggle sets, and keep the toggle
    ///   working by adding the merged object to it when needed
    /// - Exclude Object Toggle: merge the selected renderers except those targeted by
    ///   any MA Object Toggle in the avatar
    ///
    /// All variants skip renderers driven by a Cloth component: AAO does not merge them
    /// and fails the build when one is included.
    /// </summary>
    public static class ColorSafeMergeMenu
    {
        private const string MENU_PATH = "GameObject/Kanameliser Editor Plus/Create Merge Skinned Mesh (Color Menu Safe)";
        private const string MENU_PATH_FROM_TOGGLE = "GameObject/Kanameliser Editor Plus/Create Merge Skinned Mesh (From Object Toggle)";
        private const string MENU_PATH_EXCLUDE_TOGGLE = "GameObject/Kanameliser Editor Plus/Create Merge Skinned Mesh (Exclude Object Toggle)";
        // Gap of >= 11 from the previous group draws a separator above this item
        private const int MENU_PRIORITY = 140;

#if AVATAR_OPTIMIZER_INSTALLED && MODULAR_AVATAR_INSTALLED
        // GameObject/ menu handlers run once per selected object; collapse into one execution
        private static bool _executed;

        private static void RunOnce(Action action)
        {
            if (_executed) return;
            _executed = true;
            EditorApplication.delayCall += () => _executed = false;
            action();
        }

        [MenuItem(MENU_PATH, true, MENU_PRIORITY)]
        public static bool ValidateCreateColorSafeMerge()
        {
            var objects = Selection.objects;
            if (objects.Length == 0) return false;
            var gameObjects = objects.OfType<GameObject>().ToArray();
            if (gameObjects.Length != objects.Length) return false;
            return gameObjects.Any(x => (x.TryGetComponent<SkinnedMeshRenderer>(out _)
                                         || x.TryGetComponent<MeshRenderer>(out _))
                                        && !x.TryGetComponent<Cloth>(out _));
        }

        [MenuItem(MENU_PATH, false, MENU_PRIORITY)]
        public static void CreateColorSafeMerge() => RunOnce(() =>
        {
            var (skinnedRenderers, basicRenderers) = CollectSelectedRenderers();
            CreateMergeObject(skinnedRenderers, basicRenderers);
        });

        [MenuItem(MENU_PATH_FROM_TOGGLE, true, MENU_PRIORITY + 1)]
        public static bool ValidateCreateFromObjectToggle()
        {
            var selected = Selection.activeGameObject;
            return selected != null && selected.TryGetComponent<ModularAvatarObjectToggle>(out _);
        }

        [MenuItem(MENU_PATH_FROM_TOGGLE, false, MENU_PRIORITY + 1)]
        public static void CreateFromObjectToggle() => RunOnce(() =>
        {
            var selected = Selection.activeGameObject;
            if (selected == null || !selected.TryGetComponent<ModularAvatarObjectToggle>(out var toggle)) return;

            var avatarRoot = RuntimeUtil.FindAvatarInParents(selected.transform);
            if (avatarRoot == null)
            {
                Debug.LogWarning("[Kanameliser Editor Plus] The MA Object Toggle is not inside an avatar; " +
                                 "cannot resolve its targets.");
                return;
            }

            var allEntries = CollectToggleEntries(avatarRoot.gameObject);
            var ownEntries = allEntries.Where(x => ReferenceEquals(x.Toggle, toggle)).ToList();

            var candidates = new List<Renderer>();
            var seen = new HashSet<Renderer>();
            foreach (var entry in ownEntries)
                foreach (var renderer in entry.Root.GetComponentsInChildren<Renderer>(true))
                    if ((renderer is SkinnedMeshRenderer || renderer is MeshRenderer)
                        // a merged object added to the toggle by a previous run is not a source
                        && !renderer.TryGetComponent<MergeSkinnedMesh>(out _)
                        && seen.Add(renderer))
                        candidates.Add(renderer);
            candidates = ExcludeClothRenderers(candidates);

            var createdCount = 0;
            foreach (var (setActive, renderers) in
                     ObjectToggleMergeAnalyzer.GroupByEntryValue(candidates, toggle, allEntries))
            {
                if (renderers.Count < 2) continue; // merging a single renderer only adds overhead

                var merged = CreateMergeObject(
                    renderers.OfType<SkinnedMeshRenderer>().ToList(),
                    renderers.OfType<MeshRenderer>().ToList());
                if (merged == null) continue;
                createdCount++;

                // The toggle must reach the merged geometry: it already does when the merged
                // object was created under a toggled subtree; otherwise add it to the toggle.
                if (!ownEntries.Any(e => merged.transform.IsChildOf(e.Root)))
                {
                    Undo.RecordObject(toggle, "Add Merged Object to MA Object Toggle");
                    toggle.Objects.Add(new ToggledObject
                    {
                        Object = new AvatarObjectReference(merged),
                        Active = setActive,
                    });
                    PrefabUtility.RecordPrefabInstancePropertyModifications(toggle);
                    Debug.Log($"[Kanameliser Editor Plus] Added '{merged.name}' to the MA Object Toggle on " +
                              $"'{selected.name}' so the toggle keeps working.");
                }
            }

            if (createdCount == 0)
                Debug.LogWarning("[Kanameliser Editor Plus] No mergeable renderers found on the MA Object Toggle: " +
                                 "each toggle state needs at least 2 renderers not targeted by another Object Toggle.");
        });

        [MenuItem(MENU_PATH_EXCLUDE_TOGGLE, true, MENU_PRIORITY + 2)]
        public static bool ValidateCreateExcludingObjectToggle() => ValidateCreateColorSafeMerge();

        [MenuItem(MENU_PATH_EXCLUDE_TOGGLE, false, MENU_PRIORITY + 2)]
        public static void CreateExcludingObjectToggle() => RunOnce(() =>
        {
            var (skinnedRenderers, basicRenderers) = CollectSelectedRenderers();
            var allRenderers = skinnedRenderers.Cast<Renderer>().Concat(basicRenderers).ToList();
            if (allRenderers.Count == 0) return;

            var avatarRoot = RuntimeUtil.FindAvatarInParents(allRenderers[0].transform);
            var entries = avatarRoot != null
                ? CollectToggleEntries(avatarRoot.gameObject)
                : new List<ObjectToggleMergeAnalyzer.ToggleEntry>();

            var filtered = ObjectToggleMergeAnalyzer.ExcludeToggled(allRenderers, entries);
            if (filtered.Count == 0)
            {
                Debug.LogWarning("[Kanameliser Editor Plus] All selected renderers are targeted by " +
                                 "MA Object Toggle; nothing to merge.");
                return;
            }

            var excluded = allRenderers.Except(filtered).ToList();
            if (excluded.Count > 0)
                Debug.Log("[Kanameliser Editor Plus] Excluded renderer(s) targeted by MA Object Toggle " +
                          "from the merge: " + string.Join(", ", excluded.Select(x => x.name)));

            CreateMergeObject(
                filtered.OfType<SkinnedMeshRenderer>().ToList(),
                filtered.OfType<MeshRenderer>().ToList());
        });

        private static (List<SkinnedMeshRenderer> skinned, List<MeshRenderer> basic) CollectSelectedRenderers()
        {
            var gameObjects = Selection.gameObjects;
            // Cloth requires a SkinnedMeshRenderer, so only the skinned list can contain cloth
            var skinned = ExcludeClothRenderers(gameObjects
                .Select(x => x.GetComponent<SkinnedMeshRenderer>())
                .Where(x => x != null)
                .ToList());
            var basic = gameObjects
                .Select(x => x.GetComponent<MeshRenderer>())
                .Where(x => x != null)
                .ToList();
            return (skinned, basic);
        }

        // AAO Merge Skinned Mesh does not merge cloth-driven renderers and fails the build
        // when one is included, so they are dropped from the merge sources up front
        internal static List<T> ExcludeClothRenderers<T>(List<T> renderers) where T : Renderer
        {
            var cloth = renderers.Where(x => x.TryGetComponent<Cloth>(out _)).ToList();
            if (cloth.Count == 0) return renderers;

            Debug.Log("[Kanameliser Editor Plus] Excluded renderer(s) with a Cloth component " +
                      "from the merge: " + string.Join(", ", cloth.Select(x => x.name)));
            return renderers.Except(cloth).ToList();
        }

        private static List<ObjectToggleMergeAnalyzer.ToggleEntry> CollectToggleEntries(GameObject avatarRoot)
        {
            var entries = new List<ObjectToggleMergeAnalyzer.ToggleEntry>();
            foreach (var toggle in avatarRoot.GetComponentsInChildren<ModularAvatarObjectToggle>(true))
            {
                if (toggle.Objects == null) continue;
                foreach (var entry in toggle.Objects)
                {
                    var target = entry.Object?.Get(toggle);
                    if (target == null) continue;
                    entries.Add(new ObjectToggleMergeAnalyzer.ToggleEntry(toggle, target.transform, entry.Active));
                }
            }
            return entries;
        }

        internal static GameObject CreateMergeObject(
            List<SkinnedMeshRenderer> skinnedRenderers, List<MeshRenderer> basicRenderers)
        {
            var allRenderers = skinnedRenderers.Cast<Renderer>().Concat(basicRenderers).ToList();
            if (allRenderers.Count == 0) return null;

            var newObject = new GameObject("Merge Skinned Mesh");
            newObject.transform.SetParent(FindCommonParent(allRenderers.Select(x => x.transform).ToList()), false);

            var merge = newObject.AddComponent<MergeSkinnedMesh>();

            var doNotMerge = new List<Material>();
            var avatarRoot = RuntimeUtil.FindAvatarInParents(allRenderers[0].transform);
            if (avatarRoot != null)
            {
                doNotMerge = ColorSafeMergeAnalyzer.CollectDoNotMergeMaterials(allRenderers, avatarRoot.gameObject);
            }
            else
            {
                Debug.LogWarning("[Kanameliser Editor Plus] Selected renderers are not inside an avatar; " +
                                 "skipped MA Material Setter/Swap analysis.");
            }

            Configure(merge, skinnedRenderers, basicRenderers, doNotMerge);

            Undo.RegisterCreatedObjectUndo(newObject, "Create Merge Skinned Mesh (Color Menu Safe)");
            Selection.activeGameObject = newObject;
            EditorGUIUtility.PingObject(newObject);

            var message = $"Created Merge Skinned Mesh with {allRenderers.Count} renderer(s).";
            if (doNotMerge.Count > 0)
                message += " Excluded material(s) changed by MA Material Setter/Swap from slot merging: " +
                           string.Join(", ", doNotMerge.Select(x => x.name));
            Debug.Log($"[Kanameliser Editor Plus] {message}");

            return newObject;
        }

        // MergeSkinnedMesh exposes no public API for doNotMergeMaterials, so all sets are
        // written through their serialized representation (PrefabSafeSet.mainSet), the same
        // data the AAO inspector edits. The component is freshly created, so writing mainSet
        // directly is safe (no prefab layers exist yet).
        private static void Configure(
            MergeSkinnedMesh merge,
            IReadOnlyList<SkinnedMeshRenderer> skinnedRenderers,
            IReadOnlyList<MeshRenderer> basicRenderers,
            IReadOnlyList<Material> doNotMergeMaterials)
        {
            var serialized = new SerializedObject(merge);
            SetObjectArray(serialized, "renderersSet.mainSet", skinnedRenderers);
            SetObjectArray(serialized, "staticRenderersSet.mainSet", basicRenderers);
            SetObjectArray(serialized, "doNotMergeMaterials.mainSet", doNotMergeMaterials);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray<T>(SerializedObject serialized, string propertyPath, IReadOnlyList<T> values)
            where T : UnityEngine.Object
        {
            var property = serialized.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogWarning($"[Kanameliser Editor Plus] Property '{propertyPath}' was not found on " +
                                 "Merge Skinned Mesh; the AAO serialization layout may have changed. " +
                                 "Please configure the component manually.");
                return;
            }

            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static Transform FindCommonParent(IReadOnlyList<Transform> transforms)
        {
            List<Transform> common = null;
            foreach (var transform in transforms)
            {
                var chain = new List<Transform>();
                for (var current = transform.parent; current != null; current = current.parent)
                    chain.Add(current);
                chain.Reverse(); // root first

                if (common == null)
                {
                    common = chain;
                    continue;
                }

                var matched = 0;
                while (matched < common.Count && matched < chain.Count && common[matched] == chain[matched])
                    matched++;
                common.RemoveRange(matched, common.Count - matched);
                if (common.Count == 0) return null;
            }

            return common == null || common.Count == 0 ? null : common[common.Count - 1];
        }
#endif
    }
}
