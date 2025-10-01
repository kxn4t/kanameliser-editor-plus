using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
#if MODULAR_AVATAR_INSTALLED
using nadena.dev.modular_avatar.core;
#endif

namespace Kanameliser.Editor.MaterialSwapHelper
{
    /// <summary>
    /// Result of material swap generation
    /// </summary>
    public struct GenerationResult
    {
        public bool success;
        public string message;
        public GameObject createdObject;
    }

    /// <summary>
    /// Handles generation of Material Swap components
    /// </summary>
    public static class MaterialSwapGenerator
    {
        private const string COLOR_MENU_NAME = "Color Menu";
        private const string COLOR_PREFIX = "Color";
        public const string MENU_ITEM_PARAMETER = "KEP_MaterialSwap";

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
                var groups = MaterialSwapHelperSession.GetCopiedDataGroups();

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

                    var colorVariation = ModularAvatarIntegration.CreateColorVariation(colorMenu, colorName, colorNumber, targetRoot.name);
                    createdVariations.Add(colorVariation);

                    // Create material swaps for this group
                    int groupMatches = SetupMaterialSwapsForGroup(colorVariation, targetRoot, group);
                    totalSuccessfulMatches += groupMatches;

                    if (groupMatches == 0)
                    {
                        Debug.LogWarning($"[Material Swap Helper] No matching objects found for group {groupIndex + 1}");
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
                var groups = MaterialSwapHelperSession.GetCopiedDataGroups();

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

                    var colorVariation = new GameObject(colorName);
                    colorVariation.transform.SetParent(colorMenu, false);

                    // Register the created GameObject with Undo system
                    Undo.RegisterCreatedObjectUndo(colorVariation, "Create Color Variation");

#if MODULAR_AVATAR_INSTALLED
                    var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorVariation);
                    ModularAvatarIntegration.ConfigureMenuItemAsToggle(menuItem, colorName, colorNumber, targetRoot.name);
#endif
                    createdVariations.Add(colorVariation);

                    // Create material swaps for this group (per-object mode)
                    int groupMatches = SetupMaterialSwapsPerObjectForGroup(colorVariation, targetRoot, group);
                    totalSuccessfulMatches += groupMatches;

                    if (groupMatches == 0)
                    {
                        Debug.LogWarning($"[Material Swap Helper] No matching objects found for group {groupIndex + 1}");
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
            private Dictionary<Material, HashSet<GameObject>> _targetMappings = new Dictionary<Material, HashSet<GameObject>>();

            public void AddMapping(Material targetMaterial, GameObject sourceObject)
            {
                if (!_targetMappings.ContainsKey(targetMaterial))
                {
                    _targetMappings[targetMaterial] = new HashSet<GameObject>();
                }
                _targetMappings[targetMaterial].Add(sourceObject);
            }

            public bool HasConflicts => _targetMappings.Count > 1;

            public IEnumerable<Material> TargetMaterials => _targetMappings.Keys;

            public HashSet<GameObject> GetObjectsForTarget(Material targetMaterial)
            {
                return _targetMappings.TryGetValue(targetMaterial, out var objects) ? objects : new HashSet<GameObject>();
            }

            public Material GetMostCommonTarget()
            {
                return _targetMappings.OrderByDescending(kvp => kvp.Value.Count).First().Key;
            }
        }

        #endregion

        #region Private Methods

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
        /// Sets up a color variation with the appropriate configuration
        /// </summary>
        private static (GameObject colorVariation, int nextColorNumber, string newColorName) SetupColorVariation(Transform colorMenu, string gameObjectName, bool isPerObject)
        {
            int nextColorNumber = DetermineNextColorNumber(colorMenu);
            string newColorName = $"{COLOR_PREFIX}{nextColorNumber}";

            GameObject colorVariation;
            if (isPerObject)
            {
                // Create variation without MaterialSwap component for per-object mode
                colorVariation = new GameObject(newColorName);
                colorVariation.transform.SetParent(colorMenu, false);

                // Register the created GameObject with Undo system
                Undo.RegisterCreatedObjectUndo(colorVariation, "Create Color Variation");

#if MODULAR_AVATAR_INSTALLED
                var menuItem = Undo.AddComponent<ModularAvatarMenuItem>(colorVariation);
                ModularAvatarIntegration.ConfigureMenuItemAsToggle(menuItem, newColorName, nextColorNumber, gameObjectName);
#endif
            }
            else
            {
                // Use the standard creation method
                colorVariation = ModularAvatarIntegration.CreateColorVariation(colorMenu, newColorName, nextColorNumber, gameObjectName);
            }

            return (colorVariation, nextColorNumber, newColorName);
        }

        /// <summary>
        /// Handles cleanup when generation fails
        /// </summary>
        private static GenerationResult HandleGenerationFailure(GameObject colorVariation, Transform colorMenu, bool isNewMenu, string message)
        {
            UnityEngine.Object.DestroyImmediate(colorVariation);
            if (isNewMenu)
            {
                UnityEngine.Object.DestroyImmediate(colorMenu.gameObject);
            }

            return new GenerationResult
            {
                success = false,
                message = message
            };
        }

        /// <summary>
        /// Processes grouped material swaps and handles conflicts
        /// </summary>
        private static void ProcessMaterialSwapGroups(GameObject colorVariation, GameObject targetRoot, Dictionary<Material, MaterialSwapInfo> materialSwapData)
        {
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
        /// Sets up material swaps for a specific group
        /// </summary>
        private static int SetupMaterialSwapsForGroup(GameObject colorVariation, GameObject targetRoot, List<MaterialSetupData> group)
        {
            var materialSwapData = new Dictionary<Material, MaterialSwapInfo>();
            int totalMatchCount = 0;

            // Process each material setup in the group
            foreach (var sourceSetup in group)
            {
                // Try to find matching object in target
                var matchedTransform = FindMatchingObject(targetRoot.transform, sourceSetup.objectName, sourceSetup.relativePath, sourceSetup.hierarchyDepth, sourceSetup.rootObjectName);
                if (matchedTransform == null)
                {
                    Debug.LogWarning($"[Material Swap Helper] Could not find match for '{sourceSetup.objectName}'");
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

                        materialSwapData[fromMaterial].AddMapping(toMaterial, matchedTransform.gameObject);
                        totalMatchCount++;
                    }
                }
            }

            // Process grouped material swaps and handle conflicts
            ProcessMaterialSwapGroups(colorVariation, targetRoot, materialSwapData);

            return totalMatchCount;
        }

        /// <summary>
        /// Sets up material swaps by matching objects between source and target
        /// </summary>
        private static int SetupMaterialSwaps(GameObject colorVariation, GameObject targetRoot, CopiedMaterialData copiedData)
        {
            var materialSwapData = new Dictionary<Material, MaterialSwapInfo>();
            int totalMatchCount = 0;

            // Process each copied material setup
            foreach (var sourceSetup in copiedData.materialSetups)
            {
                // Try to find matching object in target
                var matchedTransform = FindMatchingObject(targetRoot.transform, sourceSetup.objectName, sourceSetup.relativePath, sourceSetup.hierarchyDepth, sourceSetup.rootObjectName);
                if (matchedTransform == null)
                {
                    Debug.LogWarning($"[Material Swap Helper] Could not find match for '{sourceSetup.objectName}'");
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

                        materialSwapData[fromMaterial].AddMapping(toMaterial, matchedTransform.gameObject);
                        totalMatchCount++;
                    }
                }
            }

            // Process grouped material swaps and handle conflicts
            ProcessMaterialSwapGroups(colorVariation, targetRoot, materialSwapData);

            return totalMatchCount;
        }

        /// <summary>
        /// Sets up material swaps with individual components per object for a specific group
        /// </summary>
        private static int SetupMaterialSwapsPerObjectForGroup(GameObject colorVariation, GameObject targetRoot, List<MaterialSetupData> group)
        {
            int totalMatchCount = 0;

            // Process each material setup in the group
            foreach (var sourceSetup in group)
            {
                // Try to find matching object in target
                var matchedTransform = FindMatchingObject(targetRoot.transform, sourceSetup.objectName, sourceSetup.relativePath, sourceSetup.hierarchyDepth, sourceSetup.rootObjectName);
                if (matchedTransform == null)
                {
                    Debug.LogWarning($"[Material Swap Helper] Could not find match for '{sourceSetup.objectName}'");
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

                for (int i = 0; i < maxSlots; i++)
                {
                    if (currentMaterials[i] != null && sourceSetup.materials[i] != null)
                    {
                        objectSwaps.Add((currentMaterials[i], sourceSetup.materials[i]));
                    }
                }

                // Only create component if there are valid swaps
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

            return totalMatchCount;
        }

        /// <summary>
        /// Sets up material swaps with individual components per object
        /// </summary>
        private static int SetupMaterialSwapsPerObject(GameObject colorVariation, GameObject targetRoot, CopiedMaterialData copiedData)
        {
            int totalMatchCount = 0;

            // Process each copied material setup
            foreach (var sourceSetup in copiedData.materialSetups)
            {
                // Try to find matching object in target
                var matchedTransform = FindMatchingObject(targetRoot.transform, sourceSetup.objectName, sourceSetup.relativePath, sourceSetup.hierarchyDepth, sourceSetup.rootObjectName);
                if (matchedTransform == null)
                {
                    Debug.LogWarning($"[Material Swap Helper] Could not find match for '{sourceSetup.objectName}'");
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

                for (int i = 0; i < maxSlots; i++)
                {
                    if (currentMaterials[i] != null && sourceSetup.materials[i] != null)
                    {
                        objectSwaps.Add((currentMaterials[i], sourceSetup.materials[i]));
                    }
                }

                // Only create component if there are valid swaps
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

            return totalMatchCount;
        }


        /// <summary>
        /// Finds a matching object in the target hierarchy with multi-stage matching
        /// </summary>
        private static Transform FindMatchingObject(Transform root, string objectName, string sourceRelativePath, int sourceDepth = 0, string sourceRootName = "")
        {
            if (root == null) return null;

            Debug.Log($"[Material Swap Helper] MATCH: Looking for '{objectName}' (path: '{sourceRelativePath}', depth: {sourceDepth}, root: '{sourceRootName}') in '{root.name}'");

            // Get all transforms with Renderer components
            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            var transformsWithRenderer = allTransforms
                .Where(t => t.GetComponent<Renderer>() != null)
                .ToList();
            Debug.Log($"[Material Swap Helper] MATCH: Objects with Renderer: {transformsWithRenderer.Count}");

            string targetRelativePath = sourceRelativePath;
            int targetDepth = sourceDepth;

            // Priority 1: Exact relative path AND object name match
            var exactPathMatches = transformsWithRenderer
                .Where(t => GetRelativePathFromRoot(t, root) == targetRelativePath &&
                           t.name == objectName)
                .ToList();
            if (exactPathMatches.Count > 0)
            {
                // If multiple matches, prefer those with matching rootObjectName
                if (exactPathMatches.Count > 1 && !string.IsNullOrEmpty(sourceRootName))
                {
                    var rootNameMatches = exactPathMatches
                        .Where(t => root.name == sourceRootName)
                        .ToList();
                    if (rootNameMatches.Count > 0)
                    {
                        Debug.Log($"[Material Swap Helper] MATCH: ✓ Priority 1 - Exact path + root match: '{rootNameMatches.First().name}' at '{GetFullPath(rootNameMatches.First())}'");
                        return rootNameMatches.First();
                    }
                }
                Debug.Log($"[Material Swap Helper] MATCH: ✓ Priority 1 - Exact path match: '{exactPathMatches.First().name}' at '{GetFullPath(exactPathMatches.First())}'");
                return exactPathMatches.First();
            }

            // Priority 2: Same depth + exact name match
            var sameDepthExactNameMatches = transformsWithRenderer
                .Where(t =>
                {
                    string path = GetRelativePathFromRoot(t, root);
                    int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                    return depth == targetDepth && t.name == objectName;
                })
                .ToList();
            if (sameDepthExactNameMatches.Count > 0)
            {
                Debug.Log($"[Material Swap Helper] MATCH: Priority 2 - Same depth ({targetDepth}) + exact name matches: {sameDepthExactNameMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (sameDepthExactNameMatches.Count > 1)
                {
                    sameDepthExactNameMatches = FilterByParentHierarchy(
                        sameDepthExactNameMatches, targetRelativePath, root);
                    Debug.Log($"[Material Swap Helper] MATCH: After hierarchy filter: {sameDepthExactNameMatches.Count}");
                }
                var selected = GetBestDepthMatch(sameDepthExactNameMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[Material Swap Helper] MATCH: ✓ Priority 2 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
            }

            // Priority 3: Exact name match (any depth, closest depth preferred)
            var exactNameMatches = transformsWithRenderer.Where(t => t.name == objectName).ToList();
            if (exactNameMatches.Count > 0)
            {
                Debug.Log($"[Material Swap Helper] MATCH: Priority 3 - Exact name matches: {exactNameMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (exactNameMatches.Count > 1)
                {
                    exactNameMatches = FilterByParentHierarchy(
                        exactNameMatches, targetRelativePath, root);
                    Debug.Log($"[Material Swap Helper] MATCH: After hierarchy filter: {exactNameMatches.Count}");
                }
                var selected = GetBestDepthMatch(exactNameMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[Material Swap Helper] MATCH: ✓ Priority 3 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
            }

            // Priority 4: Case-insensitive name match
            var caseInsensitiveMatches = transformsWithRenderer
                .Where(t => string.Equals(t.name, objectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (caseInsensitiveMatches.Count > 0)
            {
                Debug.Log($"[Material Swap Helper] MATCH: Priority 4 - Case-insensitive matches: {caseInsensitiveMatches.Count}");
                // Filter by parent hierarchy if multiple matches
                if (caseInsensitiveMatches.Count > 1)
                {
                    caseInsensitiveMatches = FilterByParentHierarchy(
                        caseInsensitiveMatches, targetRelativePath, root);
                    Debug.Log($"[Material Swap Helper] MATCH: After hierarchy filter: {caseInsensitiveMatches.Count}");
                }
                var selected = GetBestDepthMatch(caseInsensitiveMatches, targetDepth, root, targetRelativePath);
                Debug.Log($"[Material Swap Helper] MATCH: ✓ Priority 4 - Selected: '{selected.name}' at '{GetFullPath(selected)}'");
                return selected;
            }

            // Log objects that were excluded due to no Renderer
            var excludedNoRenderer = allTransforms
                .Where(t => t.GetComponent<Renderer>() == null && t.name == objectName)
                .ToList();
            if (excludedNoRenderer.Count > 0)
            {
                Debug.LogWarning($"[Material Swap Helper] MATCH: Found {excludedNoRenderer.Count} matching objects without Renderer (excluded):");
                foreach (var excluded in excludedNoRenderer.Take(5))
                {
                    Debug.LogWarning($"[Material Swap Helper] MATCH:   - '{excluded.name}' at '{GetFullPath(excluded)}'");
                }
            }

            Debug.Log($"[Material Swap Helper] MATCH: ✗ No match found for '{objectName}'");
            return null;
        }

        /// <summary>
        /// Gets the relative path from a specific root
        /// </summary>
        private static string GetRelativePathFromRoot(Transform transform, Transform root)
        {
            if (transform == null || root == null) return "";

            var path = new List<string>();
            var current = transform;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        /// <summary>
        /// Removes the root object name from a path
        /// </summary>
        private static string GetPathWithoutRoot(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";

            int firstSlash = fullPath.IndexOf('/');
            if (firstSlash < 0) return fullPath;

            return fullPath.Substring(firstSlash + 1);
        }

        /// <summary>
        /// Gets the full hierarchy path of a transform
        /// </summary>
        private static string GetFullPath(Transform transform)
        {
            if (transform == null) return "";

            var path = transform.name;
            var parent = transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>
        /// Filters candidates by matching parent hierarchy from bottom to top
        /// </summary>
        private static List<Transform> FilterByParentHierarchy(
            List<Transform> candidates,
            string targetRelativePath,
            Transform rootObject)
        {
            if (candidates.Count <= 1) return candidates;

            string rootName = rootObject.name;
            string[] targetParts = string.IsNullOrEmpty(targetRelativePath)
                ? new string[0]
                : targetRelativePath.Split('/');

            // Get maximum depth among candidates
            int maxDepth = candidates.Max(c =>
            {
                string path = GetRelativePathFromRoot(c, rootObject);
                return string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
            });

            // Start from parent level (one level up from the object itself)
            for (int level = 1; level < maxDepth; level++)
            {
                var filtered = candidates.Where(t =>
                {
                    string path = GetRelativePathFromRoot(t, rootObject);
                    string[] sourceParts = string.IsNullOrEmpty(path) ? new string[0] : path.Split('/');
                    if (sourceParts.Length <= level) return false;

                    int sourceIndex = sourceParts.Length - 1 - level;
                    string sourceParent = sourceParts[sourceIndex];

                    int targetIndex = targetParts.Length - 1 - level;

                    if (targetIndex >= 0)
                    {
                        string targetParent = targetParts[targetIndex];
                        return sourceParent == targetParent;
                    }
                    else
                    {
                        return sourceParent == rootName;
                    }
                }).ToList();

                if (filtered.Count > 0)
                {
                    candidates = filtered;
                    if (candidates.Count == 1) break;
                }
                else
                {
                    break;
                }
            }

            return candidates;
        }

        /// <summary>
        /// Calculates Levenshtein distance (edit distance) between two strings
        /// </summary>
        private static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int[,] d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(
                        d[i - 1, j] + 1,      // deletion
                        d[i, j - 1] + 1),     // insertion
                        d[i - 1, j - 1] + cost); // substitution
                }
            }

            return d[s1.Length, s2.Length];
        }

        /// <summary>
        /// Selects the best candidate from multiple matches based on name similarity
        /// </summary>
        private static Transform SelectBySimilarity(List<Transform> candidates, Transform rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates.First();

            return candidates
                .OrderBy(t => LevenshteinDistance(rootObject.name, rootObject.name))  // rootObject name similarity
                .ThenBy(t => LevenshteinDistance(GetRelativePathFromRoot(t, rootObject), targetRelativePath))  // path similarity
                .First();
        }

        /// <summary>
        /// Select best match based on hierarchy depth - prioritize same depth, then closest depth
        /// </summary>
        private static Transform GetBestDepthMatch(List<Transform> candidates, int targetDepth, Transform rootObject, string targetRelativePath)
        {
            if (candidates == null || candidates.Count == 0) return null;

            // Prefer same depth matches
            var sameDepthMatches = candidates.Where(t =>
            {
                string path = GetRelativePathFromRoot(t, rootObject);
                int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                return depth == targetDepth;
            }).ToList();

            if (sameDepthMatches.Count > 0)
            {
                // If multiple with same depth, use similarity scoring
                if (sameDepthMatches.Count > 1)
                {
                    return SelectBySimilarity(sameDepthMatches, rootObject, targetRelativePath);
                }
                return sameDepthMatches.First();
            }

            // Select by closest depth, then by similarity
            var groupedByDepth = candidates
                .GroupBy(t =>
                {
                    string path = GetRelativePathFromRoot(t, rootObject);
                    int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;
                    return Math.Abs(depth - targetDepth);
                })
                .OrderBy(g => g.Key)
                .First();

            var closestDepthCandidates = groupedByDepth.ToList();
            if (closestDepthCandidates.Count > 1)
            {
                return SelectBySimilarity(closestDepthCandidates, rootObject, targetRelativePath);
            }

            return closestDepthCandidates.First();
        }

        /// <summary>
        /// Cleans object name for matching
        /// </summary>
        private static string CleanObjectName(string name)
        {
            string originalName = name;

            // Remove common suffixes
            name = Regex.Replace(name, @"\.\d+$", ""); // Remove .001, .002, etc.
            if (originalName != name)
            {
                Debug.Log($"[Material Swap Helper] CLEAN: Removed number suffix: '{originalName}' -> '{name}'");
            }

            string afterSuffix = name;
            name = Regex.Replace(name, @"_\d+$", "");  // Remove _1, _2, etc.
            if (afterSuffix != name)
            {
                Debug.Log($"[Material Swap Helper] CLEAN: Removed underscore number: '{afterSuffix}' -> '{name}'");
            }

            // Remove common prefixes - THIS MIGHT BE TOO AGGRESSIVE
            // Temporarily disabled due to overly aggressive matching
            /*
            string beforePrefix = name;
            if (name.Contains("_"))
            {
                var parts = name.Split('_');
                if (parts.Length > 1)
                {
                    // Try without first part (prefix)
                    string withoutPrefix = string.Join("_", parts.Skip(1));
                    Debug.Log($"[Material Swap Helper] CLEAN: WARNING - Aggressive prefix removal: '{beforePrefix}' -> '{withoutPrefix}'");
                    return withoutPrefix;
                }
            }
            */

            if (originalName != name)
            {
                Debug.Log($"[Material Swap Helper] CLEAN: Final result: '{originalName}' -> '{name}'");
            }

            return name;
        }

        #endregion
    }
}