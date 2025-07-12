#if NDMF_INSTALLED
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Kanameliser.EditorPlus
{
    internal static class NDMFPreviewHelper
    {
        // Reflection-based members to access NDMF's internal preview system
        private static Type previewSessionType;
        private static PropertyInfo currentProperty;
        private static PropertyInfo originalToProxyRendererProperty;
        private static MethodInfo isProxyObjectMethod;
        private static bool initialized = false;
        private static bool initializationFailed = false;

        private static void EnsureInitialized()
        {
            if (initialized || initializationFailed) return;

            try
            {
                // Dynamically find NDMF assembly to avoid hard dependencies
                var ndmfAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "nadena.dev.ndmf");

                if (ndmfAssembly == null)
                {
                    initializationFailed = true;
                    return;
                }

                InitializeTypes(ndmfAssembly);
                initialized = true;
            }
            catch (Exception ex)
            {
                initializationFailed = true;
                Debug.LogWarning($"[MeshInfoDisplay] Failed to initialize NDMF integration: {ex.Message}");
            }
        }

        private static void InitializeTypes(Assembly ndmfAssembly)
        {
            // Get required types from NDMF assembly using reflection
            previewSessionType = ndmfAssembly.GetType("nadena.dev.ndmf.preview.PreviewSession");
            var proxyControllerType = ndmfAssembly.GetType("nadena.dev.ndmf.preview.ProxyObjectController");

            if (previewSessionType != null)
            {
                // Access static Current property to get active preview session
                currentProperty = previewSessionType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                // Access internal mapping from original to proxy renderers
                originalToProxyRendererProperty = previewSessionType.GetProperty("OriginalToProxyRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            if (proxyControllerType != null)
            {
                // Method to check if a GameObject is a proxy object
                isProxyObjectMethod = proxyControllerType.GetMethod("IsProxyObject", BindingFlags.Public | BindingFlags.Static);
            }
        }

        /// <summary>
        /// Checks if NDMF preview mode is currently active
        /// </summary>
        public static bool IsPreviewActive()
        {
            try
            {
                EnsureInitialized();
                if (currentProperty == null) return false;

                var currentSession = currentProperty.GetValue(null);
                return currentSession != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MeshInfoDisplay] Error checking preview status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the proxy renderer corresponding to the original renderer during preview
        /// </summary>
        public static Renderer GetProxyRenderer(Renderer original)
        {
            try
            {
                EnsureInitialized();
                if (!IsPreviewActive() || originalToProxyRendererProperty == null) return null;

                var currentSession = currentProperty.GetValue(null);
                // Access the internal dictionary mapping original to proxy renderers
                var dict = originalToProxyRendererProperty.GetValue(currentSession) as IDictionary;

                if (dict != null && dict.Contains(original))
                    return dict[original] as Renderer;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MeshInfoDisplay] Error getting proxy renderer: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Determines if a GameObject is a proxy object created by NDMF
        /// </summary>
        public static bool IsProxyObject(GameObject obj)
        {
            try
            {
                EnsureInitialized();
                if (isProxyObjectMethod == null) return false;
                return (bool)isProxyObjectMethod.Invoke(null, new object[] { obj });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MeshInfoDisplay] Error checking proxy object: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if any of the selected objects have corresponding proxy objects
        /// </summary>
        public static bool HasProxyForSelection(GameObject[] selection)
        {
            try
            {
                EnsureInitialized();
                if (!IsPreviewActive()) return false;

                // Check each selected object and its children for proxy renderers
                foreach (var obj in selection)
                {
                    if (HasProxyInObject(obj))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MeshInfoDisplay] Error checking proxy for selection: {ex.Message}");
            }

            return false;
        }

        private static bool HasProxyInObject(GameObject obj)
        {
            // Check all renderers in the object hierarchy, including inactive ones
            var renderers = obj.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (GetProxyRenderer(renderer) != null)
                    return true;
            }

            return false;
        }
    }
}
#endif