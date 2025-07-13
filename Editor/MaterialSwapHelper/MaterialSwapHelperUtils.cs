using UnityEditor;
using UnityEngine;

namespace Kanameliser.Editor.MaterialSwapHelper
{
    /// <summary>
    /// Utility functions for Material Swap Helper
    /// </summary>
    public static class MaterialSwapHelperUtils
    {
        /// <summary>
        /// Validates if all requirements are met for Material Swap functionality
        /// </summary>
        public static ValidationResult ValidateRequirements()
        {
#if !MODULAR_AVATAR_INSTALLED
            return new ValidationResult
            {
                isValid = false,
                message = "Modular Avatar package is not installed or available"
            };
#else
            return new ValidationResult
            {
                isValid = true,
                message = "All requirements met"
            };
#endif
        }

        /// <summary>
        /// Shows an error dialog with consistent styling
        /// </summary>
        public static void ShowErrorDialog(string message, string title = "Material Swap Helper - Error")
        {
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        /// <summary>
        /// Shows a warning dialog with OK/Cancel options
        /// </summary>
        public static bool ShowWarningDialog(string message, string title = "Material Swap Helper - Warning")
        {
            return EditorUtility.DisplayDialog(title, message, "Continue", "Cancel");
        }

        /// <summary>
        /// Shows an info dialog
        /// </summary>
        public static void ShowInfoDialog(string message, string title = "Material Swap Helper")
        {
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        /// <summary>
        /// Logs a success message to console
        /// </summary>
        public static void LogSuccess(string message)
        {
            Debug.Log($"[Material Swap Helper] {message}");
        }

        /// <summary>
        /// Logs a warning message to console
        /// </summary>
        public static void LogWarning(string message)
        {
            Debug.LogWarning($"[Material Swap Helper] {message}");
        }

        /// <summary>
        /// Logs an error message to console
        /// </summary>
        public static void LogError(string message)
        {
            Debug.LogError($"[Material Swap Helper] {message}");
        }

        /// <summary>
        /// Validates a GameObject for material copying
        /// </summary>
        public static ValidationResult ValidateForCopy(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return new ValidationResult
                {
                    isValid = false,
                    message = "No GameObject selected"
                };
            }

            if (!MaterialSetupCopier.HasMaterials(gameObject))
            {
                return new ValidationResult
                {
                    isValid = false,
                    message = $"'{gameObject.name}' has no materials to copy"
                };
            }

            return new ValidationResult
            {
                isValid = true,
                message = "Valid for copying"
            };
        }

        /// <summary>
        /// Validates a GameObject for material swap creation
        /// </summary>
        public static ValidationResult ValidateForSwapCreation(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return new ValidationResult
                {
                    isValid = false,
                    message = "No GameObject selected"
                };
            }

            if (!MaterialSwapHelperSession.HasCopiedData)
            {
                return new ValidationResult
                {
                    isValid = false,
                    message = "No material setup has been copied"
                };
            }

            return new ValidationResult
            {
                isValid = true,
                message = "Valid for swap creation"
            };
        }

        /// <summary>
        /// Safe execution wrapper that handles exceptions
        /// </summary>
        public static bool TryExecute(System.Action action, string operationName)
        {
            try
            {
                action();
                return true;
            }
            catch (System.Exception e)
            {
                LogError($"Failed to {operationName}: {e.Message}");
                ShowErrorDialog($"Failed to {operationName}:\n{e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe execution wrapper with return value
        /// </summary>
        public static T TryExecute<T>(System.Func<T> func, string operationName, T defaultValue = default(T))
        {
            try
            {
                return func();
            }
            catch (System.Exception e)
            {
                LogError($"Failed to {operationName}: {e.Message}");
                ShowErrorDialog($"Failed to {operationName}:\n{e.Message}");
                return defaultValue;
            }
        }
    }

    /// <summary>
    /// Result of validation operations
    /// </summary>
    public struct ValidationResult
    {
        public bool isValid;
        public string message;
    }
}