using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.Editor.MAMaterialHelper.MaterialSwap;
using Kanameliser.EditorPlus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests
{
    public abstract class MatchingRegressionTestBase
    {
        private readonly List<UnityEngine.Object> createdObjects = new();
        private CopiedMaterialData originalCopiedData;
        private bool originalVerboseMatchingLogs;

        [SetUp]
        public void SetUp()
        {
            originalCopiedData = MAMaterialHelperSession.CopiedData;
            originalVerboseMatchingLogs = MAMaterialHelperDebug.VerboseMatchingLogs;
            MAMaterialHelperDebug.VerboseMatchingLogs = false;
        }

        [TearDown]
        public void TearDown()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = originalVerboseMatchingLogs;
            MAMaterialHelperSession.StoreCopiedData(originalCopiedData);

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
        }

        protected GameObject CreateGameObject(string name, GameObject parent = null)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent.transform, false);
            }

            createdObjects.Add(gameObject);
            return gameObject;
        }

        protected Material CreateMaterial(string name)
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null, "The built-in error shader is required by this EditMode fixture.");

            var material = new Material(shader) { name = name };
            createdObjects.Add(material);
            return material;
        }

        protected (Transform match, List<string> logs) CaptureMatchLogs(Func<Transform> action)
        {
            var logs = new List<string>();
            Application.LogCallback callback = (condition, _, type) =>
            {
                if (condition.StartsWith("[MA Material Helper] MATCH:", StringComparison.Ordinal))
                {
                    logs.Add($"{type}: {condition}");
                }
            };

            Application.logMessageReceived += callback;
            try
            {
                return (action(), logs);
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }
        }

        protected List<string> CaptureLogs(string prefix, Action action)
        {
            var logs = new List<string>();
            Application.LogCallback callback = (condition, _, type) =>
            {
                if (condition.StartsWith(prefix, StringComparison.Ordinal))
                {
                    logs.Add($"{type}: {condition}");
                }
            };

            Application.logMessageReceived += callback;
            try
            {
                action();
                return logs;
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }
        }

        protected static void AddMeshRenderer(GameObject gameObject)
        {
            gameObject.AddComponent<MeshRenderer>();
        }

        protected static string MatcherLog(LogType type, string message)
        {
            return $"{type}: [MA Material Helper] MATCH: {message}";
        }
    }

    public class ObjectMatcherRegressionTests : MatchingRegressionTestBase
    {
        [Test]
        public void FindMatchingObject_FixesP1ThroughP5SelectionsAndLogs()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = true;

            var p1Root = CreateGameObject("P1Root");
            var p1Candidate = CreateGameObject("Body", CreateGameObject("Exact", p1Root));
            AddMeshRenderer(p1Candidate);
            AssertMatch(
                p1Root.transform,
                p1Candidate,
                "Exact/Body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(p1Root.transform, "Body", "Exact/Body", 2, "Source")),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Exact/Body', depth: 2, root: 'Source') in 'P1Root'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1"),
                MatcherLog(LogType.Log, "P1 - Exact path: 'Body' at 'P1Root/Exact/Body'"));

            var p2Root = CreateGameObject("P2Root");
            var p2Candidates = CreateGameObject("Candidates", p2Root);
            var p2First = CreateGameObject("Body", p2Candidates);
            var p2Second = CreateGameObject("Body", p2Candidates);
            AddMeshRenderer(p2First);
            AddMeshRenderer(p2Second);
            AssertMatch(
                p2Root.transform,
                p2First,
                "Candidates/Body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(p2Root.transform, "Body", "Source/Body", 2, "Source")),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Source/Body', depth: 2, root: 'Source') in 'P2Root'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 2"),
                MatcherLog(LogType.Log, "P2 - Depth+name: 'Body' at 'P2Root/Candidates/Body'"));

            var p3Root = CreateGameObject("P3Root");
            var p3Candidate = CreateGameObject("Body", CreateGameObject("Different", p3Root));
            AddMeshRenderer(p3Candidate);
            AssertMatch(
                p3Root.transform,
                p3Candidate,
                "Different/Body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(p3Root.transform, "Body", "Source/Path/Body", 3, "Source")),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Source/Path/Body', depth: 3, root: 'Source') in 'P3Root'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1"),
                MatcherLog(LogType.Log, "P3 - Name: 'Body' at 'P3Root/Different/Body'"));

            var p4Root = CreateGameObject("P4Root");
            var p4Candidate = CreateGameObject("body", CreateGameObject("Different", p4Root));
            AddMeshRenderer(p4Candidate);
            AssertMatch(
                p4Root.transform,
                p4Candidate,
                "Different/body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(p4Root.transform, "Body", "Source/Body", 2, "Source")),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Source/Body', depth: 2, root: 'Source') in 'P4Root'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1"),
                MatcherLog(LogType.Log, "P4 - Case-insensitive: 'body' at 'P4Root/Different/body'"));

            var p5Root = CreateGameObject("P5Root");
            var p5Candidate = CreateGameObject("Hair_v2", CreateGameObject("Different", p5Root));
            AddMeshRenderer(p5Candidate);
            AssertMatch(
                p5Root.transform,
                p5Candidate,
                "Different/Hair_v2",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(p5Root.transform, "Hair", "Source/Hair", 2, "Source")),
                MatcherLog(LogType.Log, "Looking for 'Hair' (path: 'Source/Hair', depth: 2, root: 'Source') in 'P5Root'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1"),
                MatcherLog(LogType.Log, "P5 - Fuzzy: 'Hair_v2' at 'P5Root/Different/Hair_v2'"));
        }

        [Test]
        public void FindMatchingObject_PreservesSiblingOrderAndMatchedTargetExclusion()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = true;

            var root = CreateGameObject("TieRoot");
            var candidates = CreateGameObject("Candidates", root);
            var first = CreateGameObject("Body", candidates);
            var second = CreateGameObject("Body", candidates);
            AddMeshRenderer(first);
            AddMeshRenderer(second);
            var matchedTargets = new HashSet<Transform>();

            AssertMatch(
                root.transform,
                first,
                "Candidates/Body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Body", "Source/Body", 2, "Source", matchedTargets)),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Source/Body', depth: 2, root: 'Source') in 'TieRoot'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 2 (excluded 0 already matched)"),
                MatcherLog(LogType.Log, "P2 - Depth+name: 'Body' at 'TieRoot/Candidates/Body'"));

            AssertMatch(
                root.transform,
                second,
                "Candidates/Body",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Body", "Source/Body", 2, "Source", matchedTargets)),
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Source/Body', depth: 2, root: 'Source') in 'TieRoot'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1 (excluded 1 already matched)"),
                MatcherLog(LogType.Log, "P2 - Depth+name: 'Body' at 'TieRoot/Candidates/Body'"));

            Assert.That(matchedTargets, Is.EquivalentTo(new[] { first.transform, second.transform }));
        }

        [Test]
        public void FindMatchingObject_UsesCrossTypeFallbackAndRecordsItsLog()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = true;

            var root = CreateGameObject("CrossTypeRoot");
            var candidate = CreateGameObject("Hair_v2", root);
            AddMeshRenderer(candidate);

            AssertMatch(
                root.transform,
                candidate,
                "Hair_v2",
                CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Hair", "Hair", 1, "Source", null, "SkinnedMeshRenderer")),
                MatcherLog(LogType.Log, "Looking for 'Hair' (path: 'Hair', depth: 1, root: 'Source') in 'CrossTypeRoot'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 0"),
                MatcherLog(LogType.Log, "P5 - Fuzzy (cross-type): 'Hair_v2' at 'CrossTypeRoot/Hair_v2'"));
        }

        [Test]
        public void FindMatchingObject_ReportsRendererlessCandidatesWithoutSelectingThem()
        {
            var root = CreateGameObject("RendererlessRoot");
            CreateGameObject("Body", root);

            var captured = CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Body", "Body", 1, "Source"));

            Assert.That(captured.match, Is.Null);
            Assert.That(captured.logs, Is.EqualTo(new[]
            {
                MatcherLog(LogType.Warning, "Found 1 matching objects without Renderer (excluded):"),
                MatcherLog(LogType.Log, "No match found for 'Body'")
            }));
        }

        [Test]
        public void FindMatchingObject_VerboseModeAddsObjectCountAndRendererlessDetails()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = true;

            var root = CreateGameObject("VerboseRoot");
            CreateGameObject("Body", root);

            var captured = CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Body", "Body", 1, "Source"));

            Assert.That(captured.match, Is.Null);
            Assert.That(captured.logs, Is.EqualTo(new[]
            {
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Body', depth: 1, root: 'Source') in 'VerboseRoot'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 0"),
                MatcherLog(LogType.Warning, "Found 1 matching objects without Renderer (excluded):"),
                MatcherLog(LogType.Warning, "  - 'Body' at 'VerboseRoot/Body'"),
                MatcherLog(LogType.Log, "No match found for 'Body'")
            }));
        }

        [Test]
        public void FindMatchingObject_VerboseModeLogsObjectCountOnSuccessfulMatch()
        {
            MAMaterialHelperDebug.VerboseMatchingLogs = true;

            var root = CreateGameObject("VerboseMatchRoot");
            var candidate = CreateGameObject("Body", root);
            AddMeshRenderer(candidate);

            var captured = CaptureMatchLogs(() => ObjectMatcher.FindMatchingObject(root.transform, "Body", "Body", 1, "Source"));

            Assert.That(captured.match, Is.SameAs(candidate.transform));
            Assert.That(captured.logs, Is.EqualTo(new[]
            {
                MatcherLog(LogType.Log, "Looking for 'Body' (path: 'Body', depth: 1, root: 'Source') in 'VerboseMatchRoot'"),
                MatcherLog(LogType.Log, "Objects with Renderer: 1"),
                MatcherLog(LogType.Log, "P1 - Exact path: 'Body' at 'VerboseMatchRoot/Body'")
            }));
        }

        private static void AssertMatch(Transform root, GameObject expected, string expectedRelativePath, (Transform match, List<string> logs) captured, params string[] expectedLogs)
        {
            Assert.That(captured.match, Is.SameAs(expected.transform));
            Assert.That(ObjectMatcher.GetRelativePathFromRoot(captured.match, root), Is.EqualTo(expectedRelativePath));
            Assert.That(captured.logs, Is.EqualTo(expectedLogs));
        }
    }

    public class MaterialCopierRegressionTests : MatchingRegressionTestBase
    {
        [Test]
        public void FindMatchingMaterialData_FixesP1ThroughP5Selections()
        {
            AssertMaterialMatch(
                "Body|Exact/Body|2|Source|MeshRenderer",
                new[] { Data("Body", "Exact/Body", 2) },
                "Body", "Exact/Body", 2);
            AssertMaterialMatch(
                "Body|Candidate/Body|2|Source|MeshRenderer",
                new[] { Data("Body", "Candidate/Body", 2) },
                "Body", "Source/Body", 2);
            AssertMaterialMatch(
                "Body|Candidate/Body|2|Source|MeshRenderer",
                new[] { Data("Body", "Candidate/Body", 2) },
                "Body", "Source/Path/Body", 3);
            AssertMaterialMatch(
                "body|Candidate/body|2|Source|MeshRenderer",
                new[] { Data("body", "Candidate/body", 2) },
                "Body", "Source/Body", 2);
            AssertMaterialMatch(
                "Hair_v2|Candidate/Hair_v2|2|Source|MeshRenderer",
                new[] { Data("Hair_v2", "Candidate/Hair_v2", 2) },
                "Hair", "Source/Hair", 2);
        }

        [Test]
        public void FindMatchingMaterialData_PreservesRootBonusAndRendererFiltering()
        {
            var rootBonusMatch = MaterialCopier.FindMatchingMaterialDataForTests(
                new[]
                {
                    Data("Body", "Path/Body", 2, "AvatarA"),
                    Data("Body", "Path/Body", 2, "AvatarB")
                },
                "Body", "Path/Body", 2, "MeshRenderer", "AvatarB");
            Assert.That(Describe(rootBonusMatch), Is.EqualTo("Body|Path/Body|2|AvatarB|MeshRenderer"));

            var looseTypeMatch = MaterialCopier.FindMatchingMaterialDataForTests(
                new[] { Data("Body", "Body", 1, "Source", "") },
                "Body", "Body", 1, "MeshRenderer", "Target");
            Assert.That(Describe(looseTypeMatch), Is.EqualTo("Body|Body|1|Source|"));

            var crossTypeMatch = MaterialCopier.FindMatchingMaterialDataForTests(
                new[] { Data("Hair_v2", "Hair_v2", 1, "Source", "SkinnedMeshRenderer") },
                "Hair", "Hair", 1, "MeshRenderer", "Target");
            Assert.That(Describe(crossTypeMatch), Is.EqualTo("Hair_v2|Hair_v2|1|Source|SkinnedMeshRenderer"));
        }

        [Test]
        public void PasteMaterials_PreservesSupportedRendererApplicationAndUndoContract()
        {
            var targetRoot = CreateGameObject("PasteTarget");
            var sourceMaterial = CreateMaterial("Source");
            var beforeMesh = CreateMaterial("BeforeMesh");
            var beforeSkin = CreateMaterial("BeforeSkin");
            var beforeLine = CreateMaterial("BeforeLine");
            var beforeTrail = CreateMaterial("BeforeTrail");
            var beforeParticles = CreateMaterial("BeforeParticles");

            var mesh = CreateGameObject("Mesh", targetRoot).AddComponent<MeshRenderer>();
            mesh.sharedMaterial = beforeMesh;
            var skinned = CreateGameObject("Skin", targetRoot).AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMaterial = beforeSkin;
            var line = CreateGameObject("Line", targetRoot).AddComponent<LineRenderer>();
            line.sharedMaterial = beforeLine;
            var trail = CreateGameObject("Trail", targetRoot).AddComponent<TrailRenderer>();
            trail.sharedMaterial = beforeTrail;
            var particleObject = CreateGameObject("Particles", targetRoot);
            particleObject.AddComponent<ParticleSystem>();
            var particles = particleObject.GetComponent<ParticleSystemRenderer>();
            particles.sharedMaterial = beforeParticles;

            var copiedData = new[]
            {
                Data("Mesh", "Mesh", 1, targetRoot.name, "MeshRenderer", sourceMaterial),
                Data("Skin", "Skin", 1, targetRoot.name, "SkinnedMeshRenderer", sourceMaterial),
                Data("Line", "Line", 1, targetRoot.name, "LineRenderer", sourceMaterial),
                Data("Trail", "Trail", 1, targetRoot.name, "TrailRenderer", sourceMaterial),
                Data("Particles", "Particles", 1, targetRoot.name, "ParticleSystemRenderer", sourceMaterial)
            };

            foreach (var rendererType in new[] { "LineRenderer", "TrailRenderer", "ParticleSystemRenderer" })
            {
                var name = rendererType == "ParticleSystemRenderer" ? "Particles" : rendererType.Replace("Renderer", "");
                var selected = MaterialCopier.FindMatchingMaterialDataForTests(copiedData, name, name, 1, rendererType, targetRoot.name);
                Assert.That(selected, Is.Not.Null, $"{rendererType} must remain a searchable candidate.");
            }

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var logs = CaptureLogs("[MaterialCopier]", () => MaterialCopier.PasteMaterialsForTests(new[] { targetRoot }, copiedData));

                Assert.That(logs, Is.EqualTo(new[] { "Log: [MaterialCopier] Applied materials to 2 objects." }));
                Assert.That(Undo.GetCurrentGroupName(), Is.EqualTo("Paste Materials"));
                Assert.That(mesh.sharedMaterial, Is.SameAs(sourceMaterial));
                Assert.That(skinned.sharedMaterial, Is.SameAs(sourceMaterial));
                Assert.That(line.sharedMaterial, Is.SameAs(beforeLine));
                Assert.That(trail.sharedMaterial, Is.SameAs(beforeTrail));
                Assert.That(particles.sharedMaterial, Is.SameAs(beforeParticles));

                Undo.PerformUndo();
                undoPerformed = true;
                Assert.That(mesh.sharedMaterial, Is.SameAs(beforeMesh));
                Assert.That(skinned.sharedMaterial, Is.SameAs(beforeSkin));
            }
            finally
            {
                if (!undoPerformed)
                {
                    Undo.PerformUndo();
                }
            }
        }

        [Test]
        public void PasteMaterials_UsesNestedRelativePathForDuplicateNames()
        {
            var targetRoot = CreateGameObject("NestedTarget");
            var materialA = CreateMaterial("MaterialA");
            var materialB = CreateMaterial("MaterialB");
            var beforeA = CreateMaterial("BeforeA");
            var beforeB = CreateMaterial("BeforeB");

            var bodyA = CreateGameObject("Body", CreateGameObject("VariantA", targetRoot)).AddComponent<MeshRenderer>();
            bodyA.sharedMaterial = beforeA;
            var bodyB = CreateGameObject("Body", CreateGameObject("VariantB", targetRoot)).AddComponent<MeshRenderer>();
            bodyB.sharedMaterial = beforeB;

            var copiedData = new[]
            {
                Data("Body", "VariantA/Body", 2, targetRoot.name, "MeshRenderer", materialA),
                Data("Body", "VariantB/Body", 2, targetRoot.name, "MeshRenderer", materialB)
            };

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var logs = CaptureLogs("[MaterialCopier]", () => MaterialCopier.PasteMaterialsForTests(new[] { targetRoot }, copiedData));

                Assert.That(logs, Is.EqualTo(new[] { "Log: [MaterialCopier] Applied materials to 2 objects." }));
                Assert.That(bodyA.sharedMaterial, Is.SameAs(materialA));
                Assert.That(bodyB.sharedMaterial, Is.SameAs(materialB));

                Undo.PerformUndo();
                undoPerformed = true;
                Assert.That(bodyA.sharedMaterial, Is.SameAs(beforeA));
                Assert.That(bodyB.sharedMaterial, Is.SameAs(beforeB));
            }
            finally
            {
                if (!undoPerformed)
                {
                    Undo.PerformUndo();
                }
            }
        }

        private static MaterialData Data(string name, string path, int depth, string rootName = "Source", string rendererType = "MeshRenderer", params Material[] materials)
        {
            return new MaterialData(name, materials ?? Array.Empty<Material>(), depth, path, rootName, rendererType);
        }

        private static void AssertMaterialMatch(string expected, IEnumerable<MaterialData> copiedData, string targetName, string targetPath, int targetDepth)
        {
            var match = MaterialCopier.FindMatchingMaterialDataForTests(copiedData, targetName, targetPath, targetDepth, "MeshRenderer", "Target");
            Assert.That(Describe(match), Is.EqualTo(expected));
        }

        private static string Describe(MaterialData data)
        {
            return data == null
                ? "<null>"
                : $"{data.objectName}|{data.relativePath}|{data.hierarchyDepth}|{data.rootObjectName}|{data.rendererType}";
        }
    }

    public class MaterialSwapRegressionTests : MatchingRegressionTestBase
    {
#if MODULAR_AVATAR_INSTALLED
        [Test]
        public void CreateMaterialSwap_KeepsFirstGroupLimitationAfterLaterSuccessfulGroupAndUndoesAsOneOperation()
        {
            var targetRoot = CreateGameObject("SwapTarget");
            var fromBody = CreateMaterial("FromBody");
            var toBodyA = CreateMaterial("ToBodyA");
            var toBodyB = CreateMaterial("ToBodyB");
            var fromHat = CreateMaterial("FromHat");
            var toHat = CreateMaterial("ToHat");

            var body = CreateGameObject("Body", targetRoot).AddComponent<MeshRenderer>();
            body.sharedMaterials = new[] { fromBody, fromBody };
            var hat = CreateGameObject("Hat", targetRoot).AddComponent<MeshRenderer>();
            hat.sharedMaterial = fromHat;

            var copiedData = new CopiedMaterialData();
            copiedData.materialSetups.Add(GroupMarker(0));
            copiedData.materialSetups.Add(new MaterialSetupData("Body", "Body", new[] { toBodyA, toBodyB }, 1, targetRoot.name, "MeshRenderer"));
            copiedData.materialSetups.Add(GroupMarker(1));
            copiedData.materialSetups.Add(new MaterialSetupData("Hat", "Hat", new[] { toHat }, 1, targetRoot.name, "MeshRenderer"));
            MAMaterialHelperSession.StoreCopiedData(copiedData);

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var result = MaterialSwapGenerator.CreateMaterialSwap(targetRoot, copiedData);

                Assert.That(result.success, Is.False);
                Assert.That(result.message, Is.EqualTo(LimitationMessage(fromBody, "Body", "Slot0→ToBodyA, Slot1→ToBodyB")));
                Assert.That(targetRoot.transform.Find("Color Menu/Color1"), Is.Not.Null);
                Assert.That(targetRoot.transform.Find("Color Menu/Color2"), Is.Not.Null);

                Undo.PerformUndo();
                undoPerformed = true;
                Assert.That(targetRoot.transform.Find("Color Menu"), Is.Null);
            }
            finally
            {
                if (!undoPerformed)
                {
                    Undo.PerformUndo();
                }
            }
        }

        [Test]
        public void CreateMaterialSwap_AccumulatesLimitationMessagesInGroupOrder()
        {
            var targetRoot = CreateGameObject("MultiGroupTarget");
            var firstFrom = CreateMaterial("FirstFrom");
            var firstToA = CreateMaterial("FirstToA");
            var firstToB = CreateMaterial("FirstToB");
            var normalFrom = CreateMaterial("NormalFrom");
            var normalTo = CreateMaterial("NormalTo");
            var lastFrom = CreateMaterial("LastFrom");
            var lastToA = CreateMaterial("LastToA");
            var lastToB = CreateMaterial("LastToB");

            CreateConflictRenderer(targetRoot, "First", firstFrom, firstToA, firstToB);
            var normal = CreateGameObject("Normal", targetRoot).AddComponent<MeshRenderer>();
            normal.sharedMaterial = normalFrom;
            CreateConflictRenderer(targetRoot, "Last", lastFrom, lastToA, lastToB);

            var copiedData = new CopiedMaterialData();
            copiedData.materialSetups.Add(GroupMarker(0));
            copiedData.materialSetups.Add(new MaterialSetupData("First", "First", new[] { firstToA, firstToB }, 1, targetRoot.name, "MeshRenderer"));
            copiedData.materialSetups.Add(GroupMarker(1));
            copiedData.materialSetups.Add(new MaterialSetupData("Normal", "Normal", new[] { normalTo }, 1, targetRoot.name, "MeshRenderer"));
            copiedData.materialSetups.Add(GroupMarker(2));
            copiedData.materialSetups.Add(new MaterialSetupData("Last", "Last", new[] { lastToA, lastToB }, 1, targetRoot.name, "MeshRenderer"));
            MAMaterialHelperSession.StoreCopiedData(copiedData);

            var undoPerformed = false;
            try
            {
                Undo.IncrementCurrentGroup();
                var result = MaterialSwapGenerator.CreateMaterialSwap(targetRoot, copiedData);

                Assert.That(result.success, Is.False);
                Assert.That(result.message, Is.EqualTo(
                    LimitationMessage(firstFrom, "First", "Slot0→FirstToA, Slot1→FirstToB") +
                    "\n\n" +
                    LimitationMessage(lastFrom, "Last", "Slot0→LastToA, Slot1→LastToB")));

                Undo.PerformUndo();
                undoPerformed = true;
                Assert.That(targetRoot.transform.Find("Color Menu"), Is.Null);
            }
            finally
            {
                if (!undoPerformed)
                {
                    Undo.PerformUndo();
                }
            }
        }

        private static MaterialSetupData GroupMarker(int index)
        {
            return new MaterialSetupData($"__GROUP_START_{index}__", "", Array.Empty<Material>());
        }

        private MeshRenderer CreateConflictRenderer(GameObject parent, string name, Material from, Material toA, Material toB)
        {
            var renderer = CreateGameObject(name, parent).AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { from, from };
            return renderer;
        }

        private static string LimitationMessage(Material from, string objectName, string slotDetails)
        {
            return "Material Swap Limitation Detected\n\n" +
                   "Material Swapでは、同じメッシュ内の同じマテリアルを複数の異なるマテリアルに変更できません。\n" +
                   "Material Swap cannot replace the same material in a mesh with multiple different materials.\n\n" +
                   "Detected conflicts / 検出された競合:\n\n" +
                   $"Material '{from.name}' has the following conflicts:\n" +
                   $"  - '{objectName}': {slotDetails}\n\n" +
                   "解決方法 / Solution:\n" +
                   "代わりに「Create Material Setter」を使用してください。\n" +
                   "Please use 'Create Material Setter' instead.";
        }
#else
        [Test, Ignore("Material Swap generation requires Modular Avatar.")]
        public void CreateMaterialSwap_KeepsFirstGroupLimitationAfterLaterSuccessfulGroupAndUndoesAsOneOperation()
        {
        }

        [Test, Ignore("Material Swap generation requires Modular Avatar.")]
        public void CreateMaterialSwap_AccumulatesLimitationMessagesInGroupOrder()
        {
        }
#endif
    }
}
