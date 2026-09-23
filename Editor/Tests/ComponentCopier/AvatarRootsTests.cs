using Kanameliser.EditorPlus.ComponentCopier;
using NUnit.Framework;
using UnityEngine;
#if MODULAR_AVATAR_INSTALLED
using nadena.dev.ndmf.runtime.components;
#endif

namespace Kanameliser.EditorPlus.Tests.ComponentCopierTests
{
    public class AvatarRootsTests : ComponentCopierTestBase
    {
#if MODULAR_AVATAR_INSTALLED
        // NDMFAvatarRoot marks the avatars, so the tests run without the VRChat SDK (NDMF comes with MA)

        [Test]
        public void Find_TakesTheOutermostMarker()
        {
            var avatar = CreateHierarchy("Avatar", "Preview/Outfit/Bone");
            avatar.gameObject.AddComponent<NDMFAvatarRoot>();
            avatar.Find("Preview").gameObject.AddComponent<NDMFAvatarRoot>();

            Assert.AreSame(avatar, AvatarRoots.Find(avatar.Find("Preview/Outfit/Bone")));
            Assert.AreSame(avatar, AvatarRoots.Find(avatar.Find("Preview")),
                "Like NDMF, a marker inside an avatar does not start an avatar of its own");
            Assert.IsNull(AvatarRoots.Find(CreateHierarchy("Loose", "Child").Find("Child")));
        }

        [Test]
        public void Surroundings_AreTheAvatarTheHierarchySitsOn()
        {
            var avatar = CreateHierarchy("Avatar", "Preview/Outfit");
            avatar.gameObject.AddComponent<NDMFAvatarRoot>();
            avatar.Find("Preview").gameObject.AddComponent<NDMFAvatarRoot>();

            Assert.AreSame(avatar, AvatarRoots.Surroundings(avatar.Find("Preview/Outfit")));
            Assert.IsNull(AvatarRoots.Surroundings(avatar), "An avatar at the top of the scene has no surroundings");
        }

        [Test]
        public void MirrorRoot_IsTheAvatar()
        {
            var avatars = CreateHierarchy("Avatars", "Avatar/Armature/Hand_L");
            var avatar = avatars.Find("Avatar");
            avatar.gameObject.AddComponent<NDMFAvatarRoot>();

            Assert.AreSame(avatar, AvatarRoots.MirrorRoot(avatar.Find("Armature/Hand_L")));
        }
#endif

        [Test]
        public void Surroundings_WithoutAnAvatar_AreTheTopmostAncestor()
        {
            var folder = CreateHierarchy("Folder", "Avatar/Outfit");

            Assert.AreSame(folder, AvatarRoots.Surroundings(folder.Find("Avatar/Outfit")));
            Assert.IsNull(AvatarRoots.Surroundings(folder));
        }

        [Test]
        public void MirrorRoot_WithoutAnAvatar_IsTheOutermostModel()
        {
            // An outfit FBX on the model has an Animator of its own
            var avatars = CreateHierarchy("Avatars", "Model/Outfit/Armature/UpperLeg_L");
            var model = avatars.Find("Model");
            model.gameObject.AddComponent<Animator>();
            model.Find("Outfit").gameObject.AddComponent<Animator>();

            Assert.AreSame(model, AvatarRoots.MirrorRoot(model.Find("Outfit/Armature/UpperLeg_L")));
        }

        [Test]
        public void MirrorRoot_WithoutAModel_IsTheTopmostParent()
        {
            var parts = CreateHierarchy("Parts", "UpperLeg_L/Collider");

            Assert.AreSame(parts, AvatarRoots.MirrorRoot(parts.Find("UpperLeg_L/Collider")));
        }
    }
}
