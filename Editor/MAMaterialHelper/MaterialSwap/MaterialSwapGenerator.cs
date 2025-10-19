using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
#if MODULAR_AVATAR_INSTALLED
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.Editor.MAMaterialHelper.MaterialSwap
{
    /// <summary>
    /// Handles generation of Material Swap components
    /// </summary>
    public static class MaterialSwapGenerator
    {
        private const string COLOR_MENU_NAME = "Color Menu";
        private const string COLOR_PREFIX = "Color";
        public const string MENU_ITEM_PARAMETER = "KEP/MaterialSwap";

        /// <summary>
        /// Creates material swap setup on the target GameObject
        /// </summary>
        public static GenerationResult CreateMaterialSwap(GameObject targetRoot, CopiedMaterialData copiedData)
        {
            if (!ValidateParameters(targetRoot, copiedData, out var validationResult))
                return validationResult;

            try
            {
                // Set up Undo group for this operation
                Undo.SetCurrentGroupName("Create Material Swap");
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
                int startingColorNumber = MAMaterialHelperUtils.DetermineNextColorNumber(colorMenu, COLOR_PREFIX);
                string uniqueParameterName = ModularAvatarIntegration.DetermineParameterNameForColorMenu(colorMenu, targetRoot, MENU_ITEM_PARAMETER);
                string limitationError = null;

                // Create color variations for each group
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    int colorNumber = startingColorNumber + groupIndex;
                    string colorName = $"{COLOR_PREFIX}{colorNumber}";

                    var colorVariation = ModularAvatarIntegration.CreateColorVariation(colorMenu, colorName, colorNumber, targetRoot.name, uniqueParameterName);
                    createdVariations.Add(colorVariation);

                    // Create material swaps for this group
                    int groupMatches = SetupMaterialSwapsForGroup(colorVariation, targetRoot, group, out limitationError);

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
                    ? $"Created Color Menu with {groups.Count} color variations (Color{startingColorNumber}-Color{startingColorNumber + groups.Count - 1})"
                    : $"Added {groups.Count} color variations (Color{startingColorNumber}-Color{startingColorNumber + groups.Count - 1})";

                // If there were limitation errors, return them as warning
                if (!string.IsNullOrEmpty(limitationError))
                {
                    return new GenerationResult
                    {
                        success = false,
                        message = limitationError,
                        createdObject = createdVariations.Count > 0 ? createdVariations[0] : null
                    };
                }

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
                    message = $"Failed to create material swap: {e.Message}"
                };
            }
        }

        /// <summary>
        /// Creates material swap setup with individual components per object
        /// </summary>
        public static GenerationResult CreateMaterialSwapPerObject(GameObject targetRoot, CopiedMaterialData copiedData)
        {
            if (!ValidateParameters(targetRoot, copiedData, out var validationResult))
                return validationResult;

            try
            {
                // Set up Undo group for this operation
                Undo.SetCurrentGroupName("Create Material Swap Per Object");
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
                int startingColorNumber = MAMaterialHelperUtils.DetermineNextColorNumber(colorMenu, COLOR_PREFIX);
                string uniqueParameterName = ModularAvatarIntegration.DetermineParameterNameForColorMenu(colorMenu, targetRoot, MENU_ITEM_PARAMETER);
                string limitationError = null;

                // Create color variations for each group
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    var group = groups[groupIndex];
                    int colorNumber = startingColorNumber + groupIndex;
                    string colorName = $"{COLOR_PREFIX}{colorNumber}";

                    var colorVariation = new GameObject(colorName);
                    colorVariation.transform.SetParent(colorMenu, false);

                    // Register the created GameObject with Undo system
                    Undo.RegisterCreatedObjectUndo(colorVariation, "Create Color Variation");

#if MODULAR_AVATAR_INSTALLED
                    var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorVariation);
                    ModularAvatarIntegration.ConfigureMenuItemAsToggle(menuItem, colorName, colorNumber, targetRoot.name, uniqueParameterName);
#endif
                    createdVariations.Add(colorVariation);

                    // Create material swaps for this group (per-object mode)
                    int groupMatches = SetupMaterialSwapsPerObjectForGroup(colorVariation, targetRoot, group, out limitationError);

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
                    ? $"Created Color Menu with {groups.Count} color variations (per-object components)"
                    : $"Added {groups.Count} color variations with per-object components";

                // If there were limitation errors, return them as warning
                if (!string.IsNullOrEmpty(limitationError))
                {
                    return new GenerationResult
                    {
                        success = false,
                        message = limitationError,
                        createdObject = createdVariations.Count > 0 ? createdVariations[0] : null
                    };
                }

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
                    message = $"Failed to create material swap per object: {e.Message}"
                };
            }
        }

        #region Private Classes

        /// <summary>
        /// Manages material swap information for conflict detection and resolution
        /// </summary>
        private class MaterialSwapInfo
        {
            private class SlotMapping
            {
                public GameObject Object { get; set; }
                public int SlotIndex { get; set; }
            }

            private readonly Dictionary<Material, List<SlotMapping>> _targetMappings = new();

            public void AddMapping(Material targetMaterial, GameObject sourceObject, int slotIndex)
            {
                if (!_targetMappings.ContainsKey(targetMaterial))
                {
                    _targetMappings[targetMaterial] = new List<SlotMapping>();
                }
                _targetMappings[targetMaterial].Add(new SlotMapping
                {
                    Object = sourceObject,
                    SlotIndex = slotIndex
                });
            }

            public bool HasConflicts => _targetMappings.Count > 1;

            public IEnumerable<Material> TargetMaterials => _targetMappings.Keys;

            public HashSet<GameObject> GetObjectsForTarget(Material targetMaterial)
            {
                if (_targetMappings.TryGetValue(targetMaterial, out var mappings))
                {
                    return new HashSet<GameObject>(mappings.Select(m => m.Object));
                }
                return new HashSet<GameObject>();
            }

            public Material GetMostCommonTarget()
            {
                return _targetMappings.OrderByDescending(kvp => kvp.Value.Count).First().Key;
            }

            /// <summary>
            /// Checks if the same object has multiple slots with different target materials
            /// This indicates a Material Swap limitation where the same source material
            /// needs to be swapped to different materials in different slots
            /// </summary>
            public bool HasSameObjectMultipleTargets(out List<string> conflictDetails)
            {
                conflictDetails = new List<string>();

                // Group mappings by object
                var objectMappings = new Dictionary<GameObject, List<(Material targetMaterial, int slotIndex)>>();

                foreach (var kvp in _targetMappings)
                {
                    var targetMaterial = kvp.Key;
                    foreach (var mapping in kvp.Value)
                    {
                        if (!objectMappings.ContainsKey(mapping.Object))
                        {
                            objectMappings[mapping.Object] = new List<(Material, int)>();
                        }
                        objectMappings[mapping.Object].Add((targetMaterial, mapping.SlotIndex));
                    }
                }

                bool hasConflict = false;
                foreach (var kvp in objectMappings)
                {
                    var obj = kvp.Key;
                    var targets = kvp.Value;

                    // Check if this object has multiple different target materials
                    var uniqueTargets = targets.Select(t => t.targetMaterial).Distinct().ToList();
                    if (uniqueTargets.Count > 1)
                    {
                        hasConflict = true;
                        var slotInfo = string.Join(", ", targets.Select(t => $"Slot{t.slotIndex}→{t.targetMaterial.name}"));
                        conflictDetails.Add($"  - '{obj.name}': {slotInfo}");
                    }
                }

                return hasConflict;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Builds error message for Material Swap limitation
        /// </summary>
        private static string BuildLimitationErrorMessage(List<string> conflictDetails)
        {
            var errorMessage = "Material Swap Limitation Detected\n\n" +
                               "Material Swapでは、同じメッシュ内の同じマテリアルを複数の異なるマテリアルに変更できません。\n" +
                               "Material Swap cannot replace the same material in a mesh with multiple different materials.\n\n" +
                               "Detected conflicts / 検出された競合:\n" +
                               string.Join("\n", conflictDetails) +
                               "\n\n" +
                               "解決方法 / Solution:\n" +
                               "代わりに「Create Material Setter」を使用してください。\n" +
                               "Please use 'Create Material Setter' instead.";
            return errorMessage;
        }

        /// <summary>
        /// Validates input parameters for material swap generation
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
        /// Sets up material swaps for a specific group
        /// Returns the number of successful matches (errorMessage will be set if limitation was detected)
        /// </summary>
        private static int SetupMaterialSwapsForGroup(GameObject colorVariation, GameObject targetRoot, List<MaterialSetupData> group, out string errorMessage)
        {
            errorMessage = null;
            var materialSwapData = new Dictionary<Material, MaterialSwapInfo>();
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

                // Create material swaps
                var currentMaterials = renderer.sharedMaterials;
                int maxSlots = Mathf.Min(currentMaterials.Length, sourceSetup.materials.Length);

                for (int i = 0; i < maxSlots; i++)
                {
                    if (currentMaterials[i] != null && sourceSetup.materials[i] != null)
                    {
                        var fromMaterial = currentMaterials[i];
                        var toMaterial = sourceSetup.materials[i];

                        // Group material swaps by source material
                        if (!materialSwapData.ContainsKey(fromMaterial))
                        {
                            materialSwapData[fromMaterial] = new MaterialSwapInfo();
                        }

                        materialSwapData[fromMaterial].AddMapping(toMaterial, matchedTransform.gameObject, i);
                        totalMatchCount++;
                    }
                }
            }

            // Process grouped material swaps and handle conflicts
            ProcessMaterialSwapGroups(colorVariation, targetRoot, materialSwapData, out errorMessage);

            return totalMatchCount;
        }

        /// <summary>
        /// Processes grouped material swaps and handles conflicts
        /// Returns error message if Material Swap limitation was detected, null otherwise
        /// </summary>
        private static void ProcessMaterialSwapGroups(GameObject colorVariation, GameObject targetRoot, Dictionary<Material, MaterialSwapInfo> materialSwapData, out string errorMessage)
        {
            errorMessage = null;

            // First, check for Material Swap limitations
            var limitationErrors = new List<string>();
            foreach (var kvp in materialSwapData)
            {
                var fromMaterial = kvp.Key;
                var swapInfo = kvp.Value;

                if (swapInfo.HasSameObjectMultipleTargets(out var conflictDetails))
                {
                    limitationErrors.Add($"\nMaterial '{fromMaterial.name}' has the following conflicts:");
                    limitationErrors.AddRange(conflictDetails);
                }
            }

            // If limitations detected, build error message but continue processing
            if (limitationErrors.Count > 0)
            {
                errorMessage = BuildLimitationErrorMessage(limitationErrors);
                Debug.LogWarning($"[MA Material Helper] {errorMessage}");
            }

            var mainSwaps = new List<(Material from, Material to)>();

            foreach (var kvp in materialSwapData)
            {
                var fromMaterial = kvp.Key;
                var swapInfo = kvp.Value;

                if (!swapInfo.HasConflicts)
                {
                    // No conflicts - add to main component
                    var targetMaterial = swapInfo.TargetMaterials.First();
                    mainSwaps.Add((fromMaterial, targetMaterial));
                }
                else
                {
                    // Handle conflicts by creating separate components
                    var mostCommonTarget = swapInfo.GetMostCommonTarget();
                    mainSwaps.Add((fromMaterial, mostCommonTarget));

                    // Create separate components for conflicting mappings
                    foreach (var targetMaterial in swapInfo.TargetMaterials)
                    {
                        if (targetMaterial != mostCommonTarget)
                        {
                            var affectedObjects = swapInfo.GetObjectsForTarget(targetMaterial);
                            CreateConflictResolutionComponent(colorVariation, fromMaterial, targetMaterial, affectedObjects);
                        }
                    }
                }
            }

            // Apply main swaps to the primary component
            if (mainSwaps.Count > 0)
            {
                ModularAvatarIntegration.SetupMaterialSwap(colorVariation, targetRoot, mainSwaps);
            }
        }

        /// <summary>
        /// Creates a separate MaterialSwap component for conflict resolution
        /// </summary>
        private static void CreateConflictResolutionComponent(GameObject colorVariation, Material fromMaterial, Material toMaterial, HashSet<GameObject> targetObjects)
        {
            var swaps = new List<(Material from, Material to)> { (fromMaterial, toMaterial) };

            // Create a separate MaterialSwap component for each conflicting target object
            foreach (var targetObject in targetObjects)
            {
                ModularAvatarIntegration.AddMaterialSwapComponentToObject(colorVariation, targetObject, swaps);
            }
        }

        /// <summary>
        /// Sets up material swaps with individual components per object for a specific group
        /// Returns the number of successful matches (errorMessage will be set if limitation was detected)
        /// </summary>
        private static int SetupMaterialSwapsPerObjectForGroup(GameObject colorVariation, GameObject targetRoot, List<MaterialSetupData> group, out string errorMessage)
        {
            errorMessage = null;
            int totalMatchCount = 0;
            var limitationErrors = new List<string>();

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

                // Create material swaps for this specific object
                var objectSwaps = new List<(Material from, Material to)>();
                var currentMaterials = renderer.sharedMaterials;
                int maxSlots = Mathf.Min(currentMaterials.Length, sourceSetup.materials.Length);

                // Track material mappings for this object to detect conflicts
                var materialMappings = new Dictionary<Material, List<(int slotIndex, Material targetMaterial)>>();

                for (int i = 0; i < maxSlots; i++)
                {
                    if (currentMaterials[i] != null && sourceSetup.materials[i] != null)
                    {
                        var fromMaterial = currentMaterials[i];
                        var toMaterial = sourceSetup.materials[i];

                        objectSwaps.Add((fromMaterial, toMaterial));

                        // Track this mapping
                        if (!materialMappings.ContainsKey(fromMaterial))
                        {
                            materialMappings[fromMaterial] = new List<(int, Material)>();
                        }
                        materialMappings[fromMaterial].Add((i, toMaterial));
                    }
                }

                // Check for Material Swap limitations in this object
                foreach (var kvp in materialMappings)
                {
                    var fromMaterial = kvp.Key;
                    var mappings = kvp.Value;

                    // Check if same material maps to different targets
                    var uniqueTargets = mappings.Select(m => m.targetMaterial).Distinct().ToList();
                    if (uniqueTargets.Count > 1)
                    {
                        var slotInfo = string.Join(", ", mappings.Select(m => $"Slot{m.slotIndex}→{m.targetMaterial.name}"));
                        limitationErrors.Add($"  - '{matchedTransform.name}': Material '{fromMaterial.name}' → {slotInfo}");
                    }
                }

                // Create component if there are valid swaps
                if (objectSwaps.Count > 0)
                {
                    ModularAvatarIntegration.AddMaterialSwapComponentToObject(
                        colorVariation,
                        matchedTransform.gameObject,
                        objectSwaps
                    );
                    totalMatchCount += objectSwaps.Count;
                }
            }

            // If limitations detected, build error message but continue
            if (limitationErrors.Count > 0)
            {
                errorMessage = BuildLimitationErrorMessage(limitationErrors);
                Debug.LogWarning($"[MA Material Helper] {errorMessage}");
            }

            return totalMatchCount;
        }

        #endregion
    }
}
