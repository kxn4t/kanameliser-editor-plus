using System.Collections.Generic;
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// Unity's constraints stand in for the VRChat components: they hold the same kinds of spatial values
    /// (offsets in the space of a source, axes in the space of the host, a roll, a world up vector).
    /// </summary>
    public class MirrorCopyTests : ComponentCopierTestBase
    {
        private const float Tolerance = 1e-4f;

        private static void AssertClose(Vector3 expected, Vector3 actual, string message = null)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(Tolerance), $"{message} expected {expected} but was {actual}");
        }

        private static ParentConstraint AddParentConstraint(Transform host, Transform source, Vector3 offset)
        {
            var constraint = host.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = source, weight = 1f });
            constraint.SetTranslationOffset(0, offset);
            return constraint;
        }

        /// <summary>Plans a mirror copy of the components of the given types on the left side of the root.</summary>
        private static CopyPlan BuildMirrorPlan(Transform root, params System.Type[] types) =>
            BuildMirrorPlan(root, new CopySettings(), types);

        private static CopyPlan BuildMirrorPlan(Transform root, CopySettings settings, params System.Type[] types)
        {
            var map = MirrorMapper.Build(root);
            var selected = Select(root, types).Where(e => map.Sides.Of(e.Host) == Side.Left);
            return CopyPlanBuilder.Build(selected, map, settings, mirrorRoot: root);
        }

        /// <summary>Two hands whose frames are mirror images of each other, and an item on the left one.</summary>
        private Transform CreateAvatarWithHands(out Transform handL, out Transform handR)
        {
            var root = CreateHierarchy("Avatar", "Hips/Hand_L/Item_L", "Hips/Hand_R");
            handL = root.Find("Hips/Hand_L");
            handR = root.Find("Hips/Hand_R");
            handL.localPosition = new Vector3(0.5f, 1f, 0.1f);
            handL.localRotation = Quaternion.Euler(10f, 20f, -30f);
            handR.localPosition = new Vector3(-0.5f, 1f, 0.1f);
            handR.localRotation = Quaternion.Euler(10f, -20f, 30f);
            root.Find("Hips/Hand_L/Item_L").localPosition = new Vector3(0.05f, 0.1f, 0f);
            return root;
        }

        [Test]
        public void ReflectRotation_MirrorsTheAxisAndNegatesTheAngle()
        {
            var root = CreateHierarchy("Avatar");
            var mirror = new MirrorContext(root);

            var reflected = mirror.ReflectRotation(Quaternion.AngleAxis(30f, Vector3.up));

            Assert.That(Quaternion.Angle(Quaternion.AngleAxis(-30f, Vector3.up), reflected), Is.LessThan(0.01f));
            AssertClose(new Vector3(-1f, 2f, 3f), mirror.ReflectPoint(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void ReflectPoint_UsesThePlaneOfTheRoot()
        {
            var root = CreateHierarchy("Avatar");
            root.position = new Vector3(5f, 0f, 0f);
            root.rotation = Quaternion.Euler(0f, 90f, 0f);
            var mirror = new MirrorContext(root);

            // The x axis of the root points along world -z, so its YZ plane is the world plane z = 0
            AssertClose(new Vector3(6f, 1f, 2f), mirror.ReflectPoint(new Vector3(6f, 1f, -2f)));
        }

        [Test]
        public void MirrorPoint_LandsOnTheMirrorImageInWorldSpace()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var mirror = new MirrorContext(root);
            var local = new Vector3(0.02f, 0.1f, -0.03f);

            var mirrored = mirror.MirrorPoint(local, Frame.Of(handL), Frame.Of(handR));

            AssertClose(mirror.ReflectPoint(handL.TransformPoint(local)), handR.TransformPoint(mirrored));
            // Frames that are mirror images of each other keep the local value up to the x sign
            AssertClose(new Vector3(-0.02f, 0.1f, -0.03f), mirrored);
        }

        [Test]
        public void MirrorCopy_CreatesTheCounterpartAtTheMirroredPose_WithTheFlippedName()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            AddParentConstraint(itemL, handL, new Vector3(0.1f, 0.2f, 0.3f));

            var plan = BuildMirrorPlan(root, typeof(ParentConstraint));
            Assert.AreEqual("Item_R", plan.ObjectsToCreate.Single().Name);
            CopyExecutor.Execute(plan);

            var itemR = handR.Find("Item_R");
            Assert.IsNotNull(itemR);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(itemL.position), itemR.position, "position");
            Assert.That(Quaternion.Angle(mirror.ReflectRotation(itemL.rotation), itemR.rotation), Is.LessThan(0.01f));
        }

        private static (float, float, float) Components(Vector3 v) => (v.x, v.y, v.z);

        [Test]
        public void MirrorCopy_OnMirrorImageFrames_GivesExactValues()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            // Away from the origin and turned, where the way through world space leaves float noise
            root.SetPositionAndRotation(new Vector3(3.2f, 0.4f, -1.7f), Quaternion.Euler(0f, 37f, 0f));
            var itemL = root.Find("Hips/Hand_L/Item_L");
            var constraint = AddParentConstraint(itemL, handL, new Vector3(0f, 0.19f, 0f));
            constraint.translationAtRest = itemL.localPosition;
            constraint.rotationAtRest = Vector3.zero;

            var plan = BuildMirrorPlan(root, typeof(ParentConstraint));
            var values = plan.Components.Single().Values.ToDictionary(v => v.PropertyPath, v => v.VectorValue);

            // Not 2.2e-08 where the source has 0
            Assert.AreEqual((0f, 0.19f, 0f), Components(values["m_TranslationOffsets.Array.data[0]"]));
            Assert.AreEqual((0f, 0f, 0f), Components(values["m_RotationOffsets.Array.data[0]"]));
            Assert.AreEqual((-0.05f, 0.1f, 0f), Components(values["m_TranslationAtRest"]));
            Assert.AreEqual((0f, 0f, 0f), Components(values["m_RotationAtRest"]));

            CopyExecutor.Execute(plan);

            var itemR = handR.Find("Item_R");
            Assert.AreEqual((-0.05f, 0.1f, 0f), Components(itemR.localPosition));
            // q and -q are the same rotation, so the angles are compared
            Assert.AreEqual((0f, 0f, 0f), Components(itemR.localEulerAngles));
        }

        [Test]
        public void MirrorCopy_ReadsTheRestPoseInTheParentOfTheCounterpart()
        {
            // The counterpart hangs below another parent than the source, and the two parents are posed apart
            var root = CreateHierarchy("Avatar", "Body/Ear_L", "Head/Ear_R");
            var body = root.Find("Body");
            var head = root.Find("Head");
            body.localPosition = new Vector3(0f, 1f, 0f);
            head.localPosition = new Vector3(0f, 1.5f, 0.1f);
            head.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var constraint = root.Find("Body/Ear_L").gameObject.AddComponent<ParentConstraint>();
            constraint.translationAtRest = new Vector3(0.1f, 0.4f, 0.05f);
            constraint.rotationAtRest = new Vector3(10f, 30f, -20f);

            var planned = BuildMirrorPlan(root, typeof(ParentConstraint)).Components.Single();
            Assert.AreSame(head.Find("Ear_R"), planned.TargetHost);
            var values = planned.Values.ToDictionary(v => v.PropertyPath, v => v.VectorValue);

            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(body.TransformPoint(constraint.translationAtRest)),
                head.TransformPoint(values["m_TranslationAtRest"]), "m_TranslationAtRest");
            var expected = mirror.ReflectRotation(body.rotation * Quaternion.Euler(constraint.rotationAtRest));
            Assert.That(Quaternion.Angle(expected, head.rotation * Quaternion.Euler(values["m_RotationAtRest"])),
                Is.LessThan(0.01f), "m_RotationAtRest");
        }

        [Test]
        public void MirrorCopy_MirrorsAConstraintOffsetInTheSpaceOfTheMirroredSource()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            var offset = new Vector3(0.1f, 0.2f, 0.3f);
            AddParentConstraint(itemL, handL, offset);

            var plan = BuildMirrorPlan(root, typeof(ParentConstraint));
            var planned = plan.Components.Single();
            CollectionAssert.Contains(planned.Values.Select(v => v.PropertyPath).ToList(), "m_TranslationOffsets.Array.data[0]");

            // The source side sits in the same avatar, but it is no "extra" of the target
            var before = CopyVerifier.Verify(plan);
            Assert.AreEqual(0, before.Count(DiffKind.ExtraOnTarget));
            Assert.AreEqual(1, before.Count(DiffKind.MissingOnTarget));

            CopyExecutor.Execute(plan);

            var copied = handR.Find("Item_R").GetComponent<ParentConstraint>();
            Assert.AreSame(handR, copied.GetSource(0).sourceTransform);
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(handL.TransformPoint(offset)), handR.TransformPoint(copied.translationOffsets[0]));

            var report = CopyVerifier.Verify(plan);
            Assert.IsTrue(report.Components.All(c => c.Kind == DiffKind.Match),
                string.Join("\n", report.Components.SelectMany(c => c.Properties).Select(p => $"{p.PropertyPath}: {p.Expected} vs {p.Actual}")));
        }

        [Test]
        public void MirrorCopy_MirrorsAxesAndWorldUpVector_AndNegatesRoll()
        {
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Target");
            var itemL = root.Find("Hips/Item_L");
            var aim = itemL.gameObject.AddComponent<AimConstraint>();
            aim.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            aim.aimVector = new Vector3(1f, 0f, 0f);
            aim.upVector = new Vector3(0f, 1f, 0.5f);
            aim.worldUpVector = new Vector3(1f, 1f, 0f);
            var lookAt = itemL.gameObject.AddComponent<LookAtConstraint>();
            lookAt.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            lookAt.roll = 15f;

            var plan = BuildMirrorPlan(root, typeof(AimConstraint), typeof(LookAtConstraint));
            CopyExecutor.Execute(plan);

            var itemR = root.Find("Hips/Item_R");
            var copiedAim = itemR.GetComponent<AimConstraint>();
            // Item_R is created with the identity rotation like Item_L, so local directions just flip their x
            AssertClose(new Vector3(-1f, 0f, 0f), copiedAim.aimVector, "aimVector");
            AssertClose(new Vector3(0f, 1f, 0.5f), copiedAim.upVector, "upVector");
            AssertClose(new Vector3(-1f, 1f, 0f), copiedAim.worldUpVector, "worldUpVector");
            Assert.AreEqual(-15f, itemR.GetComponent<LookAtConstraint>().roll, Tolerance);
        }

        [Test]
        public void MirrorCopy_KeepsARollOfZeroAtPlusZero()
        {
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Target");
            var lookAt = root.Find("Hips/Item_L").gameObject.AddComponent<LookAtConstraint>();
            lookAt.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            lookAt.roll = 0f;

            var roll = BuildMirrorPlan(root, typeof(LookAtConstraint)).Components.Single().Values
                .Single(v => v.PropertyPath == "m_Roll").FloatValue;

            // -0 equals 0 as a number, but shows as "-0" and differs from the 0 of a prefab
            Assert.That(1f / roll, Is.GreaterThan(0f), "roll is -0");
        }

        [Test]
        public void MirrorCopy_OntoAnExistingCounterpart_ReportsIdenticalOnceApplied()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            var itemR = new GameObject("Item_R").transform;
            itemR.SetParent(handR, false);
            AddParentConstraint(itemL, handL, new Vector3(0.1f, 0.2f, 0.3f));

            CopyExecutor.Execute(BuildMirrorPlan(root, typeof(ParentConstraint)));

            var again = BuildMirrorPlan(root, typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.SkipIdentical, again.Components.Single().Action);
        }

        [Test]
        public void MirrorCopy_SkippingAnExistingMirrorImage_ReportsItAsMatching()
        {
            var root = CreateAvatarWithHands(out var handL, out _);
            AddParentConstraint(root.Find("Hips/Hand_L/Item_L"), handL, new Vector3(0.1f, 0.2f, 0.3f));
            CopyExecutor.Execute(BuildMirrorPlan(root, typeof(ParentConstraint)));

            // The counterpart is kept as it is, and compared with the mirror image rather than the source values
            var plan = BuildMirrorPlan(root, new CopySettings { ExistingPolicy = ExistingComponentPolicy.Skip },
                typeof(ParentConstraint));
            Assert.AreEqual(ComponentAction.Skip, plan.Components.Single().Action);

            var report = CopyVerifier.Verify(plan);
            Assert.IsTrue(report.Components.All(c => c.Kind == DiffKind.Match),
                string.Join("\n", report.Components.SelectMany(c => c.Properties).Select(p => $"{p.PropertyPath}: {p.Expected} vs {p.Actual}")));
        }

        [Test]
        public void AxisDependentValues_AreNotedWhenTheyMatter()
        {
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Target");
            var constraint = root.Find("Hips/Item_L").gameObject.AddComponent<RotationConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            constraint.rotationOffset = new Vector3(0f, 45f, 0f);

            var plan = BuildMirrorPlan(root, typeof(RotationConstraint));

            CollectionAssert.AreEqual(new[] { "m_RotationOffset" }, plan.Components.Single().AxisDependentProperties);
        }

        [Test]
        public void AxisDependentValues_SkipZeroOffsets_AndNoteFrozenAxes()
        {
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Target");
            var constraint = root.Find("Hips/Item_L").gameObject.AddComponent<RotationConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            constraint.rotationOffset = Vector3.zero;
            constraint.rotationAxis = Axis.X | Axis.Y;

            var plan = BuildMirrorPlan(root, typeof(RotationConstraint));

            CollectionAssert.AreEqual(new[] { "m_AffectRotationZ" }, plan.Components.Single().AxisDependentProperties);
        }

        [Test]
        public void MirrorCopy_MirrorsTheWorldUpVectorInTheUpObject_ForObjectRotationUp()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            var aim = itemL.gameObject.AddComponent<AimConstraint>();
            aim.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips"), weight = 1f });
            aim.worldUpType = AimConstraint.WorldUpType.ObjectRotationUp;
            aim.worldUpObject = handL;
            aim.worldUpVector = new Vector3(1f, 0f, 0f);

            var values = BuildMirrorPlan(root, typeof(AimConstraint)).Components.Single().Values
                .ToDictionary(v => v.PropertyPath);

            // The vector is read in the up object, which is Hand_R on the other side
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectDirection(handL.rotation * aim.worldUpVector),
                handR.rotation * values["m_WorldUpVector"].VectorValue, "m_WorldUpVector");
        }

        [Test]
        public void MirrorCopy_MirrorsTheWorldUpVectorInWorldSpace_WhenTheUpObjectIsMissing()
        {
            var root = CreateAvatarWithHands(out _, out _);
            var aim = root.Find("Hips/Hand_L/Item_L").gameObject.AddComponent<AimConstraint>();
            aim.AddSource(new ConstraintSource { sourceTransform = root.Find("Hips"), weight = 1f });
            aim.worldUpType = AimConstraint.WorldUpType.ObjectRotationUp;
            aim.worldUpObject = null;
            aim.worldUpVector = new Vector3(1f, 1f, 0f);

            var values = BuildMirrorPlan(root, typeof(AimConstraint)).Components.Single().Values
                .ToDictionary(v => v.PropertyPath);

            // Without an up object to read it in, the vector falls back to the entry for world space
            Assert.IsTrue(values.TryGetValue("m_WorldUpVector", out var worldUp), "m_WorldUpVector is not mirrored");
            AssertClose(new Vector3(-1f, 1f, 0f), worldUp.VectorValue, "m_WorldUpVector");
        }

        /// <summary>
        /// The table names types and properties by string, so a typo silently copies a value unmirrored. Every
        /// entry of an installed package must name a loaded type and a property one of its components has.
        /// </summary>
        [Test]
        public void SpatialPropertyTable_EveryEntry_NamesARealTypeAndProperty()
        {
            var componentTypes = TypeCache.GetTypesDerivedFrom<Component>().Where(t => t.FullName != null).ToList();
            var holder = CreateHierarchy("Components");
            int checkedTypes = 0;

            foreach (var typeName in SpatialPropertyTable.RegisteredTypeNames)
            {
                var type = componentTypes.FirstOrDefault(t => t.FullName == typeName);
                if (type == null)
                {
                    // A package that is not installed is fine; a wrong name in one that is, is not
                    int dot = typeName.IndexOf('.');
                    string vendor = dot < 0 ? typeName : typeName.Substring(0, dot + 1);
                    Assert.IsFalse(componentTypes.Any(t => t.FullName.StartsWith(vendor, System.StringComparison.Ordinal)),
                        $"{typeName} is not a loaded type");
                    continue;
                }

                // Properties of a base type are often declared on the concrete types, one or another of them
                var components = componentTypes
                    .Where(t => !t.IsAbstract && !t.ContainsGenericParameters && type.IsAssignableFrom(t))
                    .Select(t =>
                    {
                        var gameObject = new GameObject(t.Name);
                        gameObject.transform.SetParent(holder, false);
                        return gameObject.AddComponent(t);
                    })
                    .Where(c => c != null)
                    .Select(c => new SerializedObject(c))
                    .ToList();
                Assert.IsNotEmpty(components, $"{typeName} has no component to try");

                foreach (var entry in SpatialPropertyTable.Registered(typeName))
                {
                    var owner = components.FirstOrDefault(o => PathExists(o, entry.Pattern));
                    Assert.IsNotNull(owner, $"{typeName}: {entry.Pattern}");
                    if (entry.ReferencePattern != null)
                        Assert.IsTrue(PathExists(owner, entry.ReferencePattern), $"{typeName}: {entry.ReferencePattern}");
                    foreach (var condition in entry.When)
                        Assert.IsNotNull(owner.FindProperty(condition.PropertyPath), $"{typeName}: {condition.PropertyPath}");
                }

                foreach (var component in components) component.Dispose();
                checkedTypes++;
            }

            // Unity's constraints at least are always there
            Assert.That(checkedTypes, Is.GreaterThanOrEqualTo(5));
        }

        /// <summary>
        /// "*" as the first array element or numbered field. An empty array has no element to look at, so the
        /// array itself has to exist then.
        /// </summary>
        private static bool PathExists(SerializedObject serializedObject, string pattern)
        {
            if (serializedObject.FindProperty(pattern.Replace("*", "0")) != null) return true;

            int element = pattern.IndexOf(".Array.data[*]", System.StringComparison.Ordinal);
            if (element < 0) return false;
            var array = serializedObject.FindProperty(pattern.Substring(0, element));
            return array != null && array.isArray;
        }

        private static System.Type FindComponentType(string fullName)
        {
            var type = TypeCache.GetTypesDerivedFrom<Component>().FirstOrDefault(t => t.FullName == fullName);
            if (type == null) Assert.Ignore($"{fullName} is not installed");
            return type;
        }

        private static void Set(Component component, System.Action<SerializedObject> edit)
        {
            using var serializedObject = new SerializedObject(component);
            edit(serializedObject);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void MirrorCopy_KeepsAZeroPhysBoneEndpointZero_AndFlipsTheParameter()
        {
            var type = FindComponentType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            // The tips are no mirror images of each other, as happens on real rigs
            var root = CreateHierarchy("Avatar", "Hips/Hair_L/Tip_L", "Hips/Hair_R/Tip_R");
            root.Find("Hips/Hair_L").localPosition = new Vector3(0.1f, 1f, 0f);
            root.Find("Hips/Hair_R").localPosition = new Vector3(-0.1f, 1f, 0f);
            root.Find("Hips/Hair_L/Tip_L").localPosition = new Vector3(0.02f, 0.1f, 0f);
            root.Find("Hips/Hair_R/Tip_R").localPosition = new Vector3(-0.03f, 0.12f, 0.01f);
            var physBone = root.Find("Hips/Hair_L").gameObject.AddComponent(type);
            Set(physBone, so =>
            {
                so.FindProperty("parameter").stringValue = "Hair_L";
                so.FindProperty("endpointPosition").vector3Value = Vector3.zero;
            });

            var values = BuildMirrorPlan(root, type).Components.Single().Values.ToDictionary(v => v.PropertyPath);

            Assert.AreEqual("Hair_R", values["parameter"].StringValue);
            // A non-zero endpoint would switch on an endpoint bone that the source does not have
            AssertClose(Vector3.zero, values["endpointPosition"].VectorValue, "endpointPosition");
        }

        [Test]
        public void MirrorCopy_MirrorsAVrcConstraintRelativeToItsTargetTransform()
        {
            var type = FindComponentType("VRC.SDK3.Dynamics.Constraint.Components.VRCParentConstraint");
            var root = CreateAvatarWithHands(out var handL, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            new GameObject("Item_R").transform.SetParent(handR, false);
            // The constraint sits on an object of its own, as constraints gathered in one place do
            var holder = new GameObject("Constraints_L").transform;
            holder.SetParent(root.Find("Hips"), false);
            var constraint = holder.gameObject.AddComponent(type);
            Set(constraint, so =>
            {
                so.FindProperty("TargetTransform").objectReferenceValue = itemL;
                so.FindProperty("PositionAtRest").vector3Value = itemL.localPosition;
                so.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = handL;
                var length = so.FindProperty("Sources.totalLength");
                if (length != null) length.intValue = 1;
            });

            var values = BuildMirrorPlan(root, type).Components.Single().Values.ToDictionary(v => v.PropertyPath);

            // The rest pose is the local pose of the target: below Hand_L, and below Hand_R once mirrored.
            // Read back, in case the SDK rebaked it in the meantime.
            using var source = new SerializedObject(constraint);
            var restL = source.FindProperty("PositionAtRest").vector3Value;
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(handL.TransformPoint(restL)),
                handR.TransformPoint(values["PositionAtRest"].VectorValue), "PositionAtRest");
            CollectionAssert.Contains(values.Keys, "Sources.source0.ParentPositionOffset");
        }

#if MODULAR_AVATAR_INSTALLED
        [Test]
        public void MirrorCopy_FlipsTheHijackedCollider_AndKeepsAClearedRootInPlace()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            // An anchor with a component of its own is not created bare, so the root is copied as None
            var anchor = new GameObject("Anchor_L").transform;
            anchor.SetParent(handL, false);
            anchor.localPosition = new Vector3(0.02f, 0.05f, 0.03f);
            anchor.gameObject.AddComponent<BoxCollider>();
            var offset = new Vector3(0.01f, 0.02f, 0.03f);
            var collider = handL.gameObject.AddComponent<nadena.dev.modular_avatar.core.ModularAvatarGlobalCollider>();
            Set(collider, so =>
            {
                so.FindProperty("m_colliderToHijack").enumValueIndex =
                    (int)nadena.dev.modular_avatar.core.GlobalCollider.HandLeft;
                so.FindProperty("m_rootTransform.targetObject").objectReferenceValue = anchor.gameObject;
                so.FindProperty("m_position").vector3Value = offset;
            });

            var values = BuildMirrorPlan(root, typeof(nadena.dev.modular_avatar.core.ModularAvatarGlobalCollider))
                .Components.Single().Values.ToDictionary(v => v.PropertyPath);

            Assert.AreEqual("HandRight", values["m_colliderToHijack"].StringValue);
            // Without its root, the copy is placed relative to Hand_R, and still at the mirror image
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(anchor.TransformPoint(offset)), handR.TransformPoint(values["m_position"].VectorValue),
                "m_position");
        }

        [Test]
        public void MirrorCopy_FlipsTheBoneAndPathOfABoneProxy()
        {
            var root = CreateAvatarWithHands(out _, out _);
            var proxy = root.Find("Hips/Hand_L/Item_L").gameObject
                .AddComponent<nadena.dev.modular_avatar.core.ModularAvatarBoneProxy>();
            Set(proxy, so =>
            {
                so.FindProperty("boneReference").intValue = (int)HumanBodyBones.LeftHand;
                so.FindProperty("subPath").stringValue = "Ring_L/Gem";
            });

            var values = BuildMirrorPlan(root, typeof(nadena.dev.modular_avatar.core.ModularAvatarBoneProxy))
                .Components.Single().Values.ToDictionary(v => v.PropertyPath);

            Assert.AreEqual("RightHand", values["boneReference"].StringValue);
            Assert.AreEqual("Ring_R/Gem", values["subPath"].StringValue);
        }

        [Test]
        public void MirrorCopy_LeavesMeshSettingsBoundsAlone_WhenTheyAreNotApplied()
        {
            var root = CreateAvatarWithHands(out var handL, out _);
            var settings = root.Find("Hips/Hand_L/Item_L").gameObject
                .AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMeshSettings>();
            Set(settings, so =>
            {
                so.FindProperty("InheritBounds").intValue =
                    (int)nadena.dev.modular_avatar.core.ModularAvatarMeshSettings.InheritMode.Inherit;
                so.FindProperty("RootBone.targetObject").objectReferenceValue = handL.gameObject;
                so.FindProperty("Bounds").boundsValue = new Bounds(new Vector3(0.1f, 0f, 0f), Vector3.one);
            });

            var planned = BuildMirrorPlan(root, typeof(nadena.dev.modular_avatar.core.ModularAvatarMeshSettings))
                .Components.Single();

            Assert.IsFalse(planned.Values.Any(v => v.PropertyPath == "Bounds"));
        }

        /// <summary>MA Mesh Settings on Item_L that set their bounds in the space of <paramref name="rootBone"/>.</summary>
        private static void AddMeshSettings(Transform root, Transform rootBone, Bounds bounds)
        {
            var settings = root.Find("Hips/Hand_L/Item_L").gameObject
                .AddComponent<nadena.dev.modular_avatar.core.ModularAvatarMeshSettings>();
            Set(settings, so =>
            {
                so.FindProperty("InheritBounds").intValue =
                    (int)nadena.dev.modular_avatar.core.ModularAvatarMeshSettings.InheritMode.Set;
                so.FindProperty("RootBone.targetObject").objectReferenceValue = rootBone.gameObject;
                so.FindProperty("Bounds").boundsValue = bounds;
            });
        }

        private static CopyPlan BuildMeshSettingsPlan(Transform root) =>
            BuildMirrorPlan(root, typeof(nadena.dev.modular_avatar.core.ModularAvatarMeshSettings));

        private static Bounds PlannedBounds(CopyPlan plan) =>
            plan.Components.Single().Values.Single(v => v.PropertyPath == "Bounds").BoundsValue;

        /// <summary>The eight corners of a box.</summary>
        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            var signs = new[] { -1f, 1f };
            return signs.SelectMany(x => signs.SelectMany(y => signs.Select(z =>
                bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)))));
        }

        [Test]
        public void MirrorCopy_KeepsTheSizeOfMeshSettingsBounds_OnMirrorImageFrames()
        {
            var root = CreateAvatarWithHands(out var handL, out _);
            // Away from the origin and turned, where the way through world space leaves float noise
            root.SetPositionAndRotation(new Vector3(3.2f, 0.4f, -1.7f), Quaternion.Euler(0f, 37f, 0f));
            // Off the bone, where the size worked out from the corners carries float noise too (0.39999998 for 0.4)
            AddMeshSettings(root, handL, new Bounds(new Vector3(0.3f, 0.5f, -0.2f), new Vector3(0.4f, 0.3f, 0.2f)));

            var plan = BuildMeshSettingsPlan(root);
            var bounds = PlannedBounds(plan);

            // Exactly what mirroring the center alone gave: the same size, and the center with its x flipped
            Assert.AreEqual((0.4f, 0.3f, 0.2f), Components(bounds.size), "size");
            Assert.AreEqual((-0.3f, 0.5f, -0.2f), Components(bounds.center), "center");

            CopyExecutor.Execute(plan);

            var report = CopyVerifier.Verify(plan);
            Assert.IsTrue(report.Components.All(c => c.Kind == DiffKind.Match),
                string.Join("\n", report.Components.SelectMany(c => c.Properties).Select(p => $"{p.PropertyPath}: {p.Expected} vs {p.Actual}")));
        }

        [Test]
        public void MirrorCopy_SwapsTheDimensionsOfMeshSettingsBounds_WithTheAxesOfTheRootBone()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            // Hand_R turned a quarter around its z axis on top of the mirror image of Hand_L: the mirror image of
            // the x axis of Hand_L is its y axis, and the other way around
            handR.localRotation *= Quaternion.AngleAxis(90f, Vector3.forward);
            var source = new Bounds(new Vector3(0.1f, 0.3f, -0.05f), new Vector3(2f, 1f, 1f));
            AddMeshSettings(root, handL, source);

            var plan = BuildMeshSettingsPlan(root);
            var bounds = PlannedBounds(plan);

            AssertClose(new Vector3(1f, 2f, 1f), bounds.size, "size (2, 1, 1) with x and y swapped");
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(handL.TransformPoint(source.center)), handR.TransformPoint(bounds.center),
                "center");

            CopyExecutor.Execute(plan);

            var report = CopyVerifier.Verify(plan);
            Assert.IsTrue(report.Components.All(c => c.Kind == DiffKind.Match),
                string.Join("\n", report.Components.SelectMany(c => c.Properties).Select(p => $"{p.PropertyPath}: {p.Expected} vs {p.Actual}")));
        }

        [Test]
        public void MirrorCopy_EnclosesTheMirrorImageOfMeshSettingsBounds_OnARootBoneTurnedAtAnAngle()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            // Hand_R turned 45 degrees around its y axis on top of the mirror image of Hand_L: the mirror image of
            // the box stands at an angle to its axes
            handR.localRotation *= Quaternion.AngleAxis(45f, Vector3.up);
            var source = new Bounds(new Vector3(0.1f, 0.3f, -0.05f), new Vector3(2f, 1f, 1f));
            AddMeshSettings(root, handL, source);

            var bounds = PlannedBounds(BuildMeshSettingsPlan(root));

            // Every corner, mirrored in world space and read in Hand_R, lies within the bounds, and the bounds are
            // no larger than the box around those corners
            var mirror = new MirrorContext(root);
            var loose = bounds;
            loose.Expand(Tolerance);
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var corner in Corners(source))
            {
                var mirrored = handR.InverseTransformPoint(mirror.ReflectPoint(handL.TransformPoint(corner)));
                Assert.IsTrue(loose.Contains(mirrored), $"corner {corner} is mirrored to {mirrored}, outside of {bounds}");
                min = Vector3.Min(min, mirrored);
                max = Vector3.Max(max, mirrored);
            }

            AssertClose(max - min, bounds.size, "size of the box around the mirrored corners");
        }

        [Test]
        public void MirrorCopy_ScalesMeshSettingsBounds_ToTheRootBoneOfTheOtherSide()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            // One unit of Hand_R is two of Hand_L
            handR.localScale = new Vector3(2f, 2f, 2f);
            var source = new Bounds(new Vector3(0.1f, 0.3f, -0.05f), new Vector3(2f, 1f, 1f));
            AddMeshSettings(root, handL, source);

            var bounds = PlannedBounds(BuildMeshSettingsPlan(root));

            AssertClose(new Vector3(1f, 0.5f, 0.5f), bounds.size, "size (2, 1, 1) at half the scale");
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(handL.TransformPoint(source.center)), handR.TransformPoint(bounds.center),
                "center");
        }
#endif

        [Test]
        public void MirrorCopy_OntoTheObjectItself_IsBlocked()
        {
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Target");
            var itemL = root.Find("Hips/Item_L");
            itemL.gameObject.AddComponent<AimConstraint>()
                .AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            var manual = new Dictionary<Transform, Transform> { [itemL] = itemL };

            var map = MirrorMapper.Build(root, manual);
            var plan = CopyPlanBuilder.Build(Select(root, typeof(AimConstraint)), map, new CopySettings(), mirrorRoot: root);

            var planned = plan.Components.Single();
            Assert.AreEqual(ComponentAction.Blocked, planned.Action);
            Assert.AreEqual(BlockReason.SameSide, planned.BlockReason);
        }

        [Test]
        public void MirrorCopy_OntoAnotherObjectOfTheCopiedSide_IsBlocked()
        {
            // Two left items copied at once, the first one mapped onto the second by hand: the second would be
            // overwritten before it is read
            var root = CreateHierarchy("Avatar", "Hips/Item_L", "Hips/Other_L", "Hips/Target");
            var itemL = root.Find("Hips/Item_L");
            var otherL = root.Find("Hips/Other_L");
            foreach (var item in new[] { itemL, otherL })
            {
                item.gameObject.AddComponent<AimConstraint>()
                    .AddSource(new ConstraintSource { sourceTransform = root.Find("Hips/Target"), weight = 1f });
            }

            var map = MirrorMapper.Build(root, new Dictionary<Transform, Transform> { [itemL] = otherL });
            var plan = CopyPlanBuilder.Build(Select(root, typeof(AimConstraint)), map, new CopySettings(), mirrorRoot: root);

            var planned = plan.Components.Single(c => c.Entry.Host == itemL);
            Assert.AreEqual(ComponentAction.Blocked, planned.Action);
            Assert.AreEqual(BlockReason.SameSide, planned.BlockReason);
        }

        [Test]
        public void MirrorCopy_MirrorsTheCenterOfAUnityCollider_AndNotesTheCapsuleAxis()
        {
            var root = CreateAvatarWithHands(out _, out var handR);
            var itemL = root.Find("Hips/Hand_L/Item_L");
            var collider = itemL.gameObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0.1f, 0.02f, -0.03f);
            collider.direction = 0;

            var plan = BuildMirrorPlan(root, typeof(CapsuleCollider));
            CollectionAssert.AreEqual(new[] { "m_Direction" }, plan.Components.Single().AxisDependentProperties);
            CopyExecutor.Execute(plan);

            var copied = handR.Find("Item_R").GetComponent<CapsuleCollider>();
            var mirror = new MirrorContext(root);
            AssertClose(mirror.ReflectPoint(itemL.TransformPoint(collider.center)),
                copied.transform.TransformPoint(copied.center), "m_Center");
        }

        [Test]
        public void DiffCheck_OfAMirrorCopy_ReportsAStaleComponentOnTheOtherSide()
        {
            var root = CreateAvatarWithHands(out var handL, out var handR);
            AddParentConstraint(root.Find("Hips/Hand_L/Item_L"), handL, new Vector3(0.1f, 0.2f, 0.3f));
            // Left over on the right hand from an earlier setup; the left hand has nothing like it
            var stale = handR.gameObject.AddComponent<ParentConstraint>();

            var report = CopyVerifier.Verify(BuildMirrorPlan(root, typeof(ParentConstraint)));

            Assert.AreEqual(1, report.Count(DiffKind.ExtraOnTarget));
            Assert.AreSame(stale, report.Components.Single(c => c.Kind == DiffKind.ExtraOnTarget).Actual);
        }

        [Test]
        public void MissingObjects_WithinAScope_StartAtTheScopeItself()
        {
            // A source that lacks its counterpart, below an organizing object that lacks one as well
            var root = CreateHierarchy("Avatar", "Hips/Anchors_L/Set_L/A", "Hips/Anchors_L/Set_L/B");
            var map = MirrorMapper.Build(root);
            var anchors = root.Find("Hips/Anchors_L");
            var set = anchors.Find("Set_L");

            CollectionAssert.AreEqual(new[] { anchors }, MissingObjects.FindRoots(map));
            CollectionAssert.AreEqual(new[] { anchors }, MissingObjects.FindRoots(map, anchors));
            CollectionAssert.AreEqual(new[] { set }, MissingObjects.FindRoots(map, set));
        }

        [TestCase("HandL", "HandR")]
        [TestCase("FingerIndexR", "FingerIndexL")]
        [TestCase("FootL", "FootR")]
        [TestCase("Head", "Head")]
        [TestCase("Finger", "Finger")]
        [TestCase("MyTag_L", "MyTag_R")]
        [TestCase("Bell", "Bell")]
        public void SideTags_FlipTheStandardAndMarkedTags(string tag, string expected)
        {
            Assert.AreEqual(expected, SideTags.Flip(tag));
        }

        [Test]
        public void SpatialProperty_FillsTheReferencePathFromTheMatch()
        {
            var entry = new SpatialProperty("Sources.source*.ParentPositionOffset", SpatialKind.Position,
                SpatialSpace.Reference, "Sources.source*.SourceTransform");

            Assert.IsTrue(entry.TryMatch("Sources.source12.ParentPositionOffset", out var reference));
            Assert.AreEqual("Sources.source12.SourceTransform", reference);
            Assert.IsFalse(entry.TryMatch("Sources.source12.ParentRotationOffset", out _));
        }
    }
}
