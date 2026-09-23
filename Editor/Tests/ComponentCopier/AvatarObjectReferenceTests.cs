#if MODULAR_AVATAR_INSTALLED
using System.Linq;
using Kanameliser.EditorPlus.ComponentCopier;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.runtime.components;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    /// <summary>
    /// MA Move To stands in for every MA component that holds an AvatarObjectReference.
    /// NDMFAvatarRoot marks the avatars, so the tests run without the VRChat SDK.
    /// </summary>
    public class AvatarObjectReferenceTests : ComponentCopierTestBase
    {
        private Transform CreateAvatar(string name, params string[] paths)
        {
            var avatar = CreateHierarchy(name, paths);
            avatar.gameObject.AddComponent<NDMFAvatarRoot>();
            return avatar;
        }

        private static ModularAvatarMoveTo AddMoveTo(Transform host, string referencePath)
        {
            var moveTo = host.gameObject.AddComponent<ModularAvatarMoveTo>();
            moveTo.target.referencePath = referencePath;
            return moveTo;
        }

        private static (GameObject targetObject, string referencePath) ReadReference(ModularAvatarMoveTo moveTo)
        {
            using var serializedObject = new UnityEditor.SerializedObject(moveTo);
            var reference = serializedObject.FindProperty("target");
            return (reference.FindPropertyRelative("targetObject").objectReferenceValue as GameObject,
                reference.FindPropertyRelative("referencePath").stringValue);
        }

        private static CopyPlan BuildPlanWithSurroundings(
            Transform source, Transform target, CopySettings settings = null)
        {
            var map = TransformMapper.Build(source, target);
            return CopyPlanBuilder.Build(Select(source, typeof(ModularAvatarMoveTo)), map,
                settings ?? new CopySettings(),
                externalMapProvider: referenced => ExternalContext.BuildMap(source, target, referenced));
        }

        [Test]
        public void PathOnlyReference_IsResolvedThroughTheAvatarAndRedirected()
        {
            var avatarA = CreateAvatar("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateAvatar("AvatarB", "Armature/Hips", "Outfit/Item");
            AddMoveTo(avatarA.Find("Outfit/Item"), "Armature/Hips");

            var plan = BuildPlanWithSurroundings(avatarA.Find("Outfit"), avatarB.Find("Outfit"));
            var reference = plan.Components.Single().References.Single();
            Assert.AreEqual(ReferenceKind.ExternalMapped, reference.Kind);
            Assert.AreSame(avatarA.Find("Armature/Hips").gameObject, reference.SourceValue);
            Assert.AreEqual("target", reference.DisplayPath);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(avatarB.Find("Outfit/Item").GetComponent<ModularAvatarMoveTo>());
            Assert.AreSame(avatarB.Find("Armature/Hips").gameObject, targetObject);
            Assert.AreEqual("Armature/Hips", referencePath);
        }

        [Test]
        public void ObjectReference_GetsThePathOfItsCounterpartOnTheTargetAvatar()
        {
            var avatarA = CreateAvatar("AvatarA", "Outfit/Item", "Outfit/Anchor");
            var avatarB = CreateAvatar("AvatarB", "Wear/Item", "Wear/Anchor");
            var moveTo = AddMoveTo(avatarA.Find("Outfit/Item"), "Outfit/Anchor");
            moveTo.target.Set(avatarA.Find("Outfit/Anchor").gameObject);

            var plan = BuildPlanWithSurroundings(avatarA.Find("Outfit"), avatarB.Find("Wear"));
            Assert.AreEqual(ReferenceKind.InternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(avatarB.Find("Wear/Item").GetComponent<ModularAvatarMoveTo>());
            Assert.AreSame(avatarB.Find("Wear/Anchor").gameObject, targetObject);
            Assert.AreEqual("Wear/Anchor", referencePath);

            // The rewritten path is what the plan expects, not a deviation from the source
            var report = CopyVerifier.Verify(plan);
            Assert.AreEqual(DiffKind.Match, report.Components.Single().Kind);
        }

        [Test]
        public void UnresolvedReference_IsClearedTogetherWithItsPath()
        {
            var avatarA = CreateAvatar("AvatarA", "Outfit/Item", "Outfit/Anchor");
            var avatarB = CreateAvatar("AvatarB", "Outfit/Item");
            // Not an empty object, so it is not created in the target
            avatarA.Find("Outfit/Anchor").gameObject.AddComponent<MeshRenderer>();
            var moveTo = AddMoveTo(avatarA.Find("Outfit/Item"), "Outfit/Anchor");
            moveTo.target.Set(avatarA.Find("Outfit/Anchor").gameObject);

            var plan = BuildPlanWithSurroundings(avatarA.Find("Outfit"), avatarB.Find("Outfit"));
            Assert.AreEqual(ReferenceKind.InternalUnresolved, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(avatarB.Find("Outfit/Item").GetComponent<ModularAvatarMoveTo>());
            Assert.IsNull(targetObject);
            Assert.AreEqual("", referencePath);
        }

        [Test]
        public void KeptReference_KeepsItsPath()
        {
            var avatarA = CreateAvatar("AvatarA", "Armature/Hips", "Outfit/Item");
            var avatarB = CreateAvatar("AvatarB", "Armature/Hips", "Outfit/Item");
            var moveTo = AddMoveTo(avatarA.Find("Outfit/Item"), "Armature/Hips");
            moveTo.target.Set(avatarA.Find("Armature/Hips").gameObject);

            var plan = BuildPlanWithSurroundings(avatarA.Find("Outfit"), avatarB.Find("Outfit"),
                new CopySettings { RedirectExternalReferences = false });
            Assert.AreEqual(ReferenceKind.ExternalScene, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(avatarB.Find("Outfit/Item").GetComponent<ModularAvatarMoveTo>());
            Assert.AreSame(avatarA.Find("Armature/Hips").gameObject, targetObject);
            Assert.AreEqual("Armature/Hips", referencePath);
        }

        [Test]
        public void AvatarRootReference_PointsAtTheTargetAvatar()
        {
            var avatarA = CreateAvatar("AvatarA", "Outfit/Item");
            var avatarB = CreateAvatar("AvatarB", "Outfit/Item");
            AddMoveTo(avatarA.Find("Outfit/Item"), AvatarObjectReferences.AvatarRootPath);

            var plan = BuildPlanWithSurroundings(avatarA.Find("Outfit"), avatarB.Find("Outfit"));
            Assert.AreEqual(ReferenceKind.ExternalMapped, plan.Components.Single().References.Single().Kind);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(avatarB.Find("Outfit/Item").GetComponent<ModularAvatarMoveTo>());
            Assert.AreSame(avatarB.gameObject, targetObject);
            Assert.AreEqual(AvatarObjectReferences.AvatarRootPath, referencePath);
        }

        [Test]
        public void PathOnlyReference_WithoutAnAvatar_IsCopiedAsItIs()
        {
            var source = CreateHierarchy("Source", "Item");
            var target = CreateHierarchy("Target", "Item");
            AddMoveTo(source.Find("Item"), "Armature/Hips");

            var plan = BuildPlan(source, target, new CopySettings(), typeof(ModularAvatarMoveTo));
            Assert.IsEmpty(plan.Components.Single().References);

            CopyExecutor.Execute(plan);

            var (targetObject, referencePath) = ReadReference(target.Find("Item").GetComponent<ModularAvatarMoveTo>());
            Assert.IsNull(targetObject);
            Assert.AreEqual("Armature/Hips", referencePath);
        }

        [Test]
        public void ObjectReference_WithoutATargetAvatar_LeavesThePathEmpty()
        {
            var avatarA = CreateAvatar("AvatarA", "Outfit/Item", "Outfit/Anchor");
            var loose = CreateHierarchy("Outfit", "Item", "Anchor");
            var moveTo = AddMoveTo(avatarA.Find("Outfit/Item"), "Outfit/Anchor");
            moveTo.target.Set(avatarA.Find("Outfit/Anchor").gameObject);

            var plan = BuildPlan(avatarA.Find("Outfit"), loose, new CopySettings(), typeof(ModularAvatarMoveTo));
            CopyExecutor.Execute(plan);

            // MA fills the path in from the object once the outfit is placed on an avatar
            var (targetObject, referencePath) = ReadReference(loose.Find("Item").GetComponent<ModularAvatarMoveTo>());
            Assert.AreSame(loose.Find("Anchor").gameObject, targetObject);
            Assert.AreEqual("", referencePath);
        }
    }
}
#endif
