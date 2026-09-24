using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public abstract class ComponentCopierTestBase
    {
        private readonly List<GameObject> roots = new();
        private int undoGroupAtSetUp;

        [SetUp]
        public void RememberUndoGroup()
        {
            Undo.IncrementCurrentGroup();
            undoGroupAtSetUp = Undo.GetCurrentGroup();
        }

        [TearDown]
        public void DestroyCreatedObjects()
        {
            // The test framework reverts every Undo record once the run is over. CopyExecutor records Undo
            // operations, and reverting them after their objects were destroyed brings those objects back as
            // orphans in the open scene. So the records are reverted here, while the objects still exist.
            Undo.RevertAllDownToGroup(undoGroupAtSetUp);

            foreach (var root in roots)
            {
                if (root == null) continue;

                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;
                    Undo.ClearUndo(component);
                    Undo.ClearUndo(component.gameObject);
                }

                Object.DestroyImmediate(root);
            }

            roots.Clear();
        }

        /// <summary>
        /// Builds a hierarchy from slash-separated paths. Repeating a path creates a same-name sibling.
        /// </summary>
        protected Transform CreateHierarchy(string rootName, params string[] paths)
        {
            var root = new GameObject(rootName);
            roots.Add(root);

            foreach (var path in paths)
            {
                var current = root.transform;
                var segments = path.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    bool isLeaf = i == segments.Length - 1;
                    var next = isLeaf ? null : current.Find(segments[i]);
                    if (next == null)
                    {
                        next = new GameObject(segments[i]).transform;
                        next.SetParent(current, false);
                    }

                    current = next;
                }
            }

            return root.transform;
        }

        private const string TempFolderName = "__ComponentCopierTests";
        private const string TempFolder = "Assets/" + TempFolderName;

        // Once per fixture, so that the assets outlive the instances destroyed in the per-test TearDown
        [OneTimeTearDown]
        public void DeleteTempAssets()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        /// <summary>Saves a prefab asset for the fixture. The name has to be unique among its tests.</summary>
        protected static GameObject SavePrefab(string name, Action<Transform> build)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", TempFolderName);

            var temp = new GameObject(name);
            try
            {
                build(temp.transform);
                return PrefabUtility.SaveAsPrefabAsset(temp, $"{TempFolder}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        protected static Transform AddChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        protected static Transform Instantiate(GameObject asset, Transform parent)
        {
            return ((GameObject)PrefabUtility.InstantiatePrefab(asset, parent)).transform;
        }

        /// <summary>Marks transforms as skinning bones. No mesh is needed for that.</summary>
        protected static SkinnedMeshRenderer AddSkinnedMesh(Transform meshObject, params Transform[] bones)
        {
            var renderer = meshObject.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            return renderer;
        }

        internal static List<ComponentEntry> Select(Transform root, params System.Type[] types)
        {
            return ComponentScanner.Scan(root).Where(e => types.Contains(e.Type)).ToList();
        }

        /// <summary>
        /// The entries of exactly these components. <see cref="Select"/> takes every component of a type, also the
        /// ones Unity added to satisfy a RequireComponent.
        /// </summary>
        internal static List<ComponentEntry> SelectComponents(Transform root, params Component[] components)
        {
            return ComponentScanner.Scan(root).Where(e => components.Contains(e.Component)).ToList();
        }

        internal static CopyPlan BuildPlan(
            Transform source, Transform target, CopySettings settings, params System.Type[] types)
        {
            var map = TransformMapper.Build(source, target);
            return CopyPlanBuilder.Build(Select(source, types), map, settings);
        }
    }
}
