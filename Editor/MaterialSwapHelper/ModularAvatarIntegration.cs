using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

#if MODULAR_AVATAR_INSTALLED
using System.Reflection;
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.Editor.MaterialSwapHelper
{
    /// <summary>
    /// Unified integration layer for Modular Avatar functionality
    /// Combines detection, reflection-based operations, and high-level component creation
    /// </summary>
    public static class ModularAvatarIntegration
    {
        #region Detection & Availability

#if MODULAR_AVATAR_INSTALLED
        private const string MODULAR_AVATAR_ASSEMBLY = "nadena.dev.modular-avatar.core";

        /// <summary>
        /// Returns true if Modular Avatar package is available
        /// </summary>
        public static bool IsAvailable => true;

        /// <summary>
        /// Checks if Modular Avatar runtime components are accessible
        /// </summary>
        public static bool HasRequiredComponents => true;

        /// <summary>
        /// Checks if the Modular Avatar assembly is loaded (runtime check)
        /// </summary>
        public static bool IsAssemblyLoaded()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                return assemblies.Any(a => a.GetName().Name == MODULAR_AVATAR_ASSEMBLY);
            }
            catch
            {
                return false;
            }
        }
#else
        public static bool IsAvailable => false;
        public static bool HasRequiredComponents => false;
        public static bool IsAssemblyLoaded() => false;
#endif

        /// <summary>
        /// Indicates if the integration layer is ready for use
        /// </summary>
        public static bool IsReady => IsAvailable;

        #endregion

        #region Reflection Utilities

#if MODULAR_AVATAR_INSTALLED
        /// <summary>
        /// Calls InitSettings on a ModularAvatarMenuItem using reflection for safety
        /// </summary>
        private static bool TryInitializeMenuItem(ModularAvatarMenuItem menuItem)
        {
            try
            {
                var initMethod = typeof(ModularAvatarMenuItem).GetMethod("InitSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (initMethod != null)
                {
                    initMethod.Invoke(menuItem, null);
                    return true;
                }
                else
                {
                    Debug.LogWarning("[Material Swap Helper] InitSettings method not found, using fallback initialization");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Material Swap Helper] Failed to call InitSettings via reflection: {e.Message}");
                return false;
            }
        }

#endif

        #endregion

        #region Component Creation

#if MODULAR_AVATAR_INSTALLED
        /// <summary>
        /// Creates a Color Menu GameObject with required components
        /// </summary>
        public static GameObject CreateColorMenu(GameObject parent, string menuName)
        {
            var colorMenu = new GameObject(menuName);
            colorMenu.transform.SetParent(parent.transform, false);

            // Register the created GameObject with Undo system
            Undo.RegisterCreatedObjectUndo(colorMenu, "Create Color Menu");

            // Add Menu Installer
            var menuInstaller = Undo.AddComponent<ModularAvatarMenuInstaller>(colorMenu);

            // Add Menu Item
            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorMenu);
            ConfigureMenuItemAsSubmenu(menuItem, menuName);

            return colorMenu;
        }

        /// <summary>
        /// Creates a color variation GameObject with components
        /// </summary>
        public static GameObject CreateColorVariation(Transform parent, string name, int colorNumber, string gameObjectName)
        {
            var colorVariation = new GameObject(name);
            colorVariation.transform.SetParent(parent, false);

            // Register the created GameObject with Undo system
            Undo.RegisterCreatedObjectUndo(colorVariation, "Create Color Variation");

            // Add Material Swap component
            var materialSwap = Undo.AddComponent<ModularAvatarMaterialSwap>(colorVariation);

            // Add Menu Item component
            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorVariation);
            ConfigureMenuItemAsToggle(menuItem, name, colorNumber, gameObjectName);

            return colorVariation;
        }

        /// <summary>
        /// Sets up the Material Swap component
        /// </summary>
        public static void SetupMaterialSwap(GameObject swapObject, GameObject rootObject, List<(Material from, Material to)> swaps)
        {
            var materialSwap = swapObject.GetComponent<ModularAvatarMaterialSwap>();
            if (materialSwap == null) return;

            // Record the object before modifying it
            Undo.RecordObject(materialSwap, "Setup Material Swap");

            // Set root reference
            materialSwap.Root.Set(rootObject);

            // Set swaps
            materialSwap.Swaps.Clear();
            foreach (var (from, to) in swaps)
            {
                materialSwap.Swaps.Add(new MatSwap { From = from, To = to });
            }
        }

        /// <summary>
        /// Adds a Material Swap component directly to the specified GameObject
        /// </summary>
        public static ModularAvatarMaterialSwap AddMaterialSwapComponentToObject(GameObject colorVariation, GameObject targetObject, List<(Material from, Material to)> swaps)
        {
            // Add Material Swap component directly to the colorVariation object
            var materialSwap = Undo.AddComponent<ModularAvatarMaterialSwap>(colorVariation);

            // Set target object reference
            materialSwap.Root.Set(targetObject);

            // Set swaps
            materialSwap.Swaps.Clear();
            foreach (var (from, to) in swaps)
            {
                materialSwap.Swaps.Add(new MatSwap { From = from, To = to });
            }

            return materialSwap;
        }
#else
        public static GameObject CreateColorMenu(GameObject parent, string menuName)
        {
            throw new InvalidOperationException("Modular Avatar is not available");
        }

        public static GameObject CreateColorVariation(Transform parent, string name, int colorNumber, string gameObjectName)
        {
            throw new InvalidOperationException("Modular Avatar is not available");
        }

        public static void SetupMaterialSwap(GameObject swapObject, GameObject rootObject, List<(Material from, Material to)> swaps)
        {
            throw new InvalidOperationException("Modular Avatar is not available");
        }

        public static object AddMaterialSwapComponentToObject(GameObject colorVariation, GameObject targetObject, List<(Material from, Material to)> swaps)
        {
            throw new InvalidOperationException("Modular Avatar is not available");
        }
#endif

        #endregion

        /// <summary>
        /// Configures a MenuItem as a toggle (public access for MaterialSwapGenerator)
        /// </summary>
        public static void ConfigureMenuItemAsToggle(object menuItem, string name, int colorNumber, string gameObjectName)
        {
#if MODULAR_AVATAR_INSTALLED
            if (menuItem is ModularAvatarMenuItem item)
            {
                ConfigureMenuItemAsToggle(item, name, colorNumber, gameObjectName);
            }
#endif
        }

        #region Private Helpers

#if MODULAR_AVATAR_INSTALLED
        /// <summary>
        /// Configures a MenuItem as a submenu using InitSettings + custom overrides
        /// </summary>
        private static void ConfigureMenuItemAsSubmenu(ModularAvatarMenuItem menuItem, string name)
        {
            // Try to use InitSettings first for proper initialization
            bool initSuccess = TryInitializeMenuItem(menuItem);

            if (!initSuccess)
            {
                // Fallback manual initialization
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Value = 1;
                menuItem.PortableControl.Parameter = "";
                menuItem.PortableControl.Icon = null;
                menuItem.isSaved = true;
                menuItem.isSynced = true;
                menuItem.isDefault = false;
                menuItem.automaticValue = true;
            }

            // Override specific settings for submenu
            menuItem.label = "";
            menuItem.PortableControl.Type = PortableControlType.SubMenu;
            SetSubmenuSource(menuItem);
        }

        /// <summary>
        /// Configures a MenuItem as a toggle using InitSettings + custom overrides
        /// </summary>
        private static void ConfigureMenuItemAsToggle(ModularAvatarMenuItem menuItem, string name, int colorNumber, string gameObjectName)
        {
            // Try to use InitSettings first for proper initialization
            bool initSuccess = TryInitializeMenuItem(menuItem);

            if (!initSuccess)
            {
                // Fallback manual initialization
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Value = 1;
                menuItem.PortableControl.Parameter = "";
                menuItem.PortableControl.Icon = null;
                menuItem.isSaved = true;
                menuItem.isSynced = true;
                menuItem.isDefault = false;
                menuItem.automaticValue = true;
            }

            // Override specific settings for toggle
            menuItem.label = "";
            menuItem.PortableControl.Type = PortableControlType.Toggle;
            menuItem.PortableControl.Parameter = MaterialSwapGenerator.MENU_ITEM_PARAMETER;
            menuItem.PortableControl.Value = colorNumber;
            menuItem.automaticValue = true;
        }

        /// <summary>
        /// Sets the MenuSource to Children for submenu configuration
        /// </summary>
        private static void SetSubmenuSource(ModularAvatarMenuItem menuItem)
        {
            menuItem.MenuSource = SubmenuSource.Children;
        }
#endif

        #endregion
    }
}