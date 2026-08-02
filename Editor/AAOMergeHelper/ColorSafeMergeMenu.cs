using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if AVATAR_OPTIMIZER_INSTALLED && MODULAR_AVATAR_INSTALLED
using Anatawa12.AvatarOptimizer;
using nadena.dev.ndmf.runtime;
#endif

namespace Kanameliser.Editor.AAOMergeHelper
{
    /// <summary>
    /// Hierarchy context menu that creates an AAO Merge Skinned Mesh from the selected
    /// renderers, pre-configured so that MA Material Setter / Material Swap color menus
    /// keep working after the merge: materials changed non-uniformly across the merged
    /// renderers are excluded from material slot merging.
    /// </summary>
    public static class ColorSafeMergeMenu
    {
        private const string MENU_PATH = "GameObject/Kanameliser Editor Plus/Create Merge Skinned Mesh (Color Menu Safe)";
        // Gap of >= 11 from the previous group draws a separator above this item
        private const int MENU_PRIORITY = 140;

#if AVATAR_OPTIMIZER_INSTALLED && MODULAR_AVATAR_INSTALLED
        // GameObject/ menu handlers run once per selected object; collapse into one execution
        private static bool _executed;

        [MenuItem(MENU_PATH, true, MENU_PRIORITY)]
        public static bool ValidateCreateColorSafeMerge()
        {
            var objects = Selection.objects;
            if (objects.Length == 0) return false;
            var gameObjects = objects.OfType<GameObject>().ToArray();
            if (gameObjects.Length != objects.Length) return false;
            return gameObjects.Any(x => x.TryGetComponent<SkinnedMeshRenderer>(out _)
                                        || x.TryGetComponent<MeshRenderer>(out _));
        }

        [MenuItem(MENU_PATH, false, MENU_PRIORITY)]
        public static void CreateColorSafeMerge()
        {
            if (_executed) return;
            _executed = true;
            EditorApplication.delayCall += () => _executed = false;

            var gameObjects = Selection.gameObjects;
            var skinnedRenderers = gameObjects
                .Select(x => x.GetComponent<SkinnedMeshRenderer>())
                .Where(x => x != null)
                .ToList();
            var basicRenderers = gameObjects
                .Select(x => x.GetComponent<MeshRenderer>())
                .Where(x => x != null)
                .ToList();
            var allRenderers = skinnedRenderers.Cast<Renderer>().Concat(basicRenderers).ToList();
            if (allRenderers.Count == 0) return;

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
            where T : Object
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

        private static Transform FindCommonParent(IReadOnlyList<Transform> transforms)
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
