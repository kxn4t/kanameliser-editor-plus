using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

#if KEP_NDMF_LOCALIZATION
using nadena.dev.ndmf.localization;
using nadena.dev.ndmf.ui;
#endif

namespace Kanameliser.EditorPlus
{
    internal static class Localization
    {
#if KEP_NDMF_LOCALIZATION
        // ── NDMF path: full localization with language switching ──

        private static readonly Localizer L = new Localizer("en-us", () => new List<LocalizationAsset>
        {
            AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                "Packages/net.kanameliser.editor-plus/Editor/Localization/en-us.po"),
            AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                "Packages/net.kanameliser.editor-plus/Editor/Localization/ja-jp.po"),
            AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                "Packages/net.kanameliser.editor-plus/Editor/Localization/ko-kr.po"),
            AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                "Packages/net.kanameliser.editor-plus/Editor/Localization/zh-hans.po"),
            AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                "Packages/net.kanameliser.editor-plus/Editor/Localization/zh-hant.po"),
        });

        public static string S(string key) =>
            L.TryGetLocalizedString(key, out var val) ? val : key;

        public static void ShowLanguageUI() => LanguageSwitcher.DrawImmediate();

        public static void LocalizeUIElements(VisualElement root) =>
            L.LocalizeUIElements(root);

        public static void RegisterLanguageChangeCallback<T>(T owner, Action<T> callback)
            where T : class =>
            LanguagePrefs.RegisterLanguageChangeCallback(owner, callback);
#else
        // ── Fallback path: English only, no language switching ──

        private static LocalizationAsset _enAsset;

        private static LocalizationAsset EnAsset
        {
            get
            {
                if (_enAsset == null)
                {
                    _enAsset = AssetDatabase.LoadAssetAtPath<LocalizationAsset>(
                        "Packages/net.kanameliser.editor-plus/Editor/Localization/en-us.po");
                }
                return _enAsset;
            }
        }

        public static string S(string key)
        {
            if (EnAsset == null) return key;
            var val = EnAsset.GetLocalizedString(key);
            return string.IsNullOrEmpty(val) ? key : val;
        }

        public static void ShowLanguageUI() { }

        public static void LocalizeUIElements(VisualElement root)
        {
            foreach (var element in root.Query(className: "ndmf-tr").Build())
            {
                var prop = element.GetType().GetProperty("label")
                        ?? element.GetType().GetProperty("text");
                if (prop == null) continue;

                var key = prop.GetValue(element) as string;
                if (string.IsNullOrEmpty(key)) continue;

                prop.SetValue(element, S(key));

                var tooltipKey = key + ":tooltip";
                var tooltipVal = S(tooltipKey);
                if (tooltipVal != tooltipKey)
                    element.tooltip = tooltipVal;
            }
        }

        public static void RegisterLanguageChangeCallback<T>(T owner, Action<T> callback)
            where T : class
        {
        }
#endif

        public static string S(string key, params object[] args)
        {
            var template = S(key);
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
    }
}
