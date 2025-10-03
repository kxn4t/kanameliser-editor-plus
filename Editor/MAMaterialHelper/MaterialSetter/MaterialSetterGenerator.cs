using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
#if MODULAR_AVATAR_INSTALLED
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.Editor.MAMaterialHelper.MaterialSetter
{
    /// <summary>
    /// Handles generation of Material Setter components
    /// </summary>
    public static class MaterialSetterGenerator
    {
        private const string COLOR_MENU_NAME = "Color Menu";
        private const string COLOR_PREFIX = "Color";
        public const string MENU_ITEM_PARAMETER = "KEP_MaterialSetter";

        /// <summary>
        /// Creates material setter setup on the target GameObject
        /// </summary>
        public static GenerationResult CreateMaterialSetter(GameObject targetRoot, CopiedMaterialData copiedData)
        {
            if (!ValidateParameters(targetRoot, copiedData, out var validationResult))
                return validationResult;

            try
            {
                // Set up Undo group for this operation
                Undo.SetCurrentGroupName("Create Material Setter");
                int undoGroup = Undo.GetCurrentGroup();

                var (colorMenu, isNewMenu) = EnsureColorMenu(targetRoot);
                var groups = MAMaterialHelperSession.GetCopiedDataGroups();

                if (groups.Count == 0)
                {
                    return new GenerationResult
                    {
                        success = false,
                        message = "No material setup groups found"
                    };
                }

                var createdVariations = new List<GameObject>();
                int totalSuccessfulMatches = 0;
                int startingColorNumber = DetermineNextColorNumber(colorMenu);

                // Create color variations for each group
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    int colorNumber = startingColorNumber + groupIndex;
                    string colorName = $"{COLOR_PREFIX}{colorNumber}";

                    var colorVariation = CreateColorVariation(colorMenu, colorName, colorNumber, targetRoot.name);
                    createdVariations.Add(colorVariation);

                    // Create material setters for this group
                    int groupMatches = SetupMaterialSettersForGroup(colorVariation, targetRoot, group);
                    totalSuccessfulMatches += groupMatches;

                    if (groupMatches == 0)
                    {
                        Debug.LogWarning($"[MA Material Helper] No matching objects found for group {groupIndex + 1}");
                    }
                }

                if (totalSuccessfulMatches == 0)
                {
                    // Clean up all created variations
                    foreach (var variation in createdVariations)
                    {
                        UnityEngine.Object.DestroyImmediate(variation);
                    }

                    if (isNewMenu)
                    {
                        UnityEngine.Object.DestroyImmediate(colorMenu.gameObject);
                    }

                    return new GenerationResult
                    {
                        success = false,
                        message = "No matching objects found between source and target"
                    };
                }

                EditorUtility.SetDirty(targetRoot);

                // Collapse all Undo operations into a single operation
                Undo.CollapseUndoOperations(undoGroup);

                string resultMessage = isNewMenu
                    ? $"Created Color Menu with {groups.Count} material setter variations (Color{startingColorNumber}-Color{startingColorNumber + groups.Count - 1})"
                    : $"Added {groups.Count} material setter variations (Color{startingColorNumber}-Color{startingColorNumber + groups.Count - 1})";

                return new GenerationResult
                {
                    success = true,
                    message = resultMessage,
                    createdObject = createdVariations.Count > 0 ? createdVariations[0] : null
                };
            }
            catch (Exception e)
            {
                return new GenerationResult
                {
                    success = false,
                    message = $"Failed to create material setter: {e.Message}"
                };
            }
        }

        #region Private Methods

        /// <summary>
        /// Validates input parameters for material setter generation
        /// </summary>
        private static bool ValidateParameters(GameObject targetRoot, CopiedMaterialData copiedData, out GenerationResult result)
        {
#if !MODULAR_AVATAR_INSTALLED
            result = new GenerationResult
            {
                success = false,
                message = "Modular Avatar is not installed"
            };
            return false;
#endif

            if (targetRoot == null || copiedData == null)
            {
                result = new GenerationResult
                {
                    success = false,
                    message = "Invalid parameters"
                };
                return false;
            }

            result = default;
            return true;
        }

        /// <summary>
        /// Ensures Color Menu exists and returns it with creation status
        /// </summary>
        private static (Transform colorMenu, bool isNewMenu) EnsureColorMenu(GameObject targetRoot)
        {
            Transform colorMenu = targetRoot.transform.Find(COLOR_MENU_NAME);
            bool isNewMenu = colorMenu == null;

            if (isNewMenu)
            {
                var colorMenuObj = ModularAvatarIntegration.CreateColorMenu(targetRoot, COLOR_MENU_NAME);
                colorMenu = colorMenuObj.transform;
            }

            return (colorMenu, isNewMenu);
        }

        /// <summary>
        /// Creates a color variation GameObject with components
        /// </summary>
        private static GameObject CreateColorVariation(Transform parent, string name, int colorNumber, string gameObjectName)
        {
#if MODULAR_AVATAR_INSTALLED
            var colorVariation = new GameObject(name);
            colorVariation.transform.SetParent(parent, false);

            Undo.RegisterCreatedObjectUndo(colorVariation, "Create Color Variation");

            var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorVariation);
            ModularAvatarIntegration.ConfigureMenuItemAsToggle(menuItem, name, colorNumber, gameObjectName, MENU_ITEM_PARAMETER);

            return colorVariation;
#else
            throw new InvalidOperationException("Modular Avatar is not installed");
#endif
        }

        /// <summary>
        /// Determines the next available color number
        /// </summary>
        private static int DetermineNextColorNumber(Transform colorMenu)
        {
            var existingNumbers = new HashSet<int>();
            var colorRegex = new Regex($@"^{COLOR_PREFIX}(\d+)$");

            foreach (Transform child in colorMenu)
            {
                var match = colorRegex.Match(child.name);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                {
                    existingNumbers.Add(number);
                }
            }

            // Find the smallest available number starting from 1
            int nextNumber = 1;
            while (existingNumbers.Contains(nextNumber))
            {
                nextNumber++;
            }

            return nextNumber;
        }

        /// <summary>
        /// Sets up material setters for a specific group
        /// </summary>
        private static int SetupMaterialSettersForGroup(GameObject colorVariation, GameObject targetRoot, List<MaterialSetupData> group)
        {
            int totalMatchCount = 0;

            // Process each material setup in the group
            foreach (var sourceSetup in group)
            {
                // Try to find matching object in target
                var matchedTransform = ObjectMatcher.FindMatchingObject(targetRoot.transform, sourceSetup.objectName, sourceSetup.relativePath, sourceSetup.hierarchyDepth, sourceSetup.rootObjectName);
                if (matchedTransform == null)
                {
                    Debug.LogWarning($"[MA Material Helper] Could not find match for '{sourceSetup.objectName}'");
                    continue;
                }

                // Get renderer from matched object
                var renderer = matchedTransform.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterials == null)
                {
                    continue;
                }

                // Create material setter for this object
                var materials = new List<Material>();
                var currentMaterials = renderer.sharedMaterials;
                int maxSlots = Mathf.Min(currentMaterials.Length, sourceSetup.materials.Length);

                for (int i = 0; i < maxSlots; i++)
                {
                    // Add the material from the source setup (skip null materials)
                    if (sourceSetup.materials[i] != null)
                    {
                        materials.Add(sourceSetup.materials[i]);
                    }
                }

                // Only create component if there are valid materials
                if (materials.Count > 0)
                {
                    AddMaterialSetterComponent(colorVariation, matchedTransform.gameObject, materials);
                    totalMatchCount += materials.Count;
                }
            }

            return totalMatchCount;
        }

        /// <summary>
        /// Adds a Material Setter component to the color variation
        /// </summary>
        private static void AddMaterialSetterComponent(GameObject colorVariation, GameObject targetObject, List<Material> materials)
        {
#if MODULAR_AVATAR_INSTALLED
            var materialSetter = Undo.AddComponent<ModularAvatarMaterialSetter>(colorVariation);

            // Create AvatarObjectReference for target object using Set() method
            var objectRef = new AvatarObjectReference();
            objectRef.Set(targetObject);

            // Add material switch objects
            materialSetter.Objects.Clear();
            for (int i = 0; i < materials.Count; i++)
            {
                var switchObject = new MaterialSwitchObject
                {
                    Object = objectRef.Clone(),
                    Material = materials[i],
                    MaterialIndex = i
                };
                materialSetter.Objects.Add(switchObject);
            }
#endif
        }

        #endregion
    }
}
