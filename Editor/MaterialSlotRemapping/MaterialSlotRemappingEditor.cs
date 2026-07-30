using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.EditorPlus;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.Editor.MAMaterialHelper.SlotRemapping
{
    [CustomEditor(typeof(MaterialSlotRemapping))]
    public class MaterialSlotRemappingEditor : UnityEditor.Editor
    {
        private const int MaxAmbiguityDetailsInDialog = 5;

        private RemapGenerationResult _lastResult;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            var component = (MaterialSlotRemapping)target;

            EditorGUILayout.HelpBox(Localization.S("slotRemap.description"), MessageType.Info);

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            var newRef = (GameObject)EditorGUILayout.ObjectField(
                Localization.S("slotRemap.referencePrefab"), component.referencePrefab, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Set Reference Prefab");
                component.referencePrefab = newRef;
                CommitChange(component);
            }

            using (new EditorGUI.DisabledScope(component.referencePrefab == null))
            {
                if (GUILayout.Button(Localization.S("slotRemap.generateMapping")))
                {
                    GenerateMapping(component);
                }
            }

            DrawResultMessages();

            EditorGUILayout.Space();
            DrawRemaps(component);
        }

        private void GenerateMapping(MaterialSlotRemapping component)
        {
            var result = MaterialSlotRemapGenerator.Generate(component);
            _lastResult = result;
            if (result.success)
            {
                if (result.hasAmbiguousMappings &&
                    !EditorUtility.DisplayDialog(
                        Localization.S("slotRemap.ambiguousDialog.title"),
                        BuildAmbiguityConfirmationMessage(result),
                        Localization.S("slotRemap.ambiguousDialog.useEstimated"),
                        Localization.S("common.cancel")))
                {
                    _lastResult = null;
                    return;
                }

                Undo.RecordObject(component, "Generate Slot Remapping");
                component.remaps = result.remaps;
                CommitChange(component);
                Debug.Log($"[MA Material Helper] Generated slot remapping for {result.matchedRendererCount} renderer(s).");
            }
        }

        internal static string BuildAmbiguityConfirmationMessage(RemapGenerationResult result)
        {
            var displayedDetails = new List<string>();
            int displayedCount = result.ambiguousMappingDetails.Count;
            if (displayedCount > MaxAmbiguityDetailsInDialog)
                displayedCount = MaxAmbiguityDetailsInDialog;

            for (int i = 0; i < displayedCount; i++)
                displayedDetails.Add(result.ambiguousMappingDetails[i]);

            string details = string.Join("\n\n", displayedDetails);
            if (result.ambiguousMappingDetails.Count > displayedCount)
            {
                details += "\n\n" + Localization.S("slotRemap.ambiguousDialog.more",
                    result.ambiguousMappingDetails.Count - displayedCount);
            }

            return Localization.S("slotRemap.ambiguousDialog.message", details);
        }

        private void DrawResultMessages()
        {
            if (_lastResult == null) return;

            foreach (var error in _lastResult.errors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var warning in _lastResult.warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            if (_lastResult.success && _lastResult.warnings.Count == 0 && _lastResult.errors.Count == 0)
                EditorGUILayout.HelpBox(Localization.S("slotRemap.mappingGenerated", _lastResult.matchedRendererCount), MessageType.Info);
        }

        private void DrawRemaps(MaterialSlotRemapping component)
        {
            if (component.remaps == null || component.remaps.Count == 0)
            {
                EditorGUILayout.LabelField(Localization.S("slotRemap.noMapping"), EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(Localization.S("slotRemap.slotMappings", component.remaps.Count), EditorStyles.boldLabel);

            foreach (var remap in component.remaps)
            {
                if (remap == null) continue;

                string key = remap.rendererPath ?? "";
                if (!_foldouts.ContainsKey(key)) _foldouts[key] = false;

                string title = DisplayPath(component, remap);
                _foldouts[key] = EditorGUILayout.Foldout(_foldouts[key], title, true);
                if (!_foldouts[key]) continue;

                EditorGUI.indentLevel++;
                DrawRemapEntry(component, remap);
                if (GUILayout.Button(Localization.S("slotRemap.resetToIdentity"), GUILayout.Width(260)))
                {
                    ResetToIdentity(component, remap);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawRemapEntry(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            var map = remap.referenceSlotForHostSlot;
            if (map == null) return;

            var hostMaterials = GetHostMaterials(component, remap);
            int refCount = map.Length;

            var options = new string[refCount + 1];
            options[0] = "(none)";
            for (int i = 0; i < refCount; i++) options[i + 1] = $"Ref slot {i}";

            for (int hostSlot = 0; hostSlot < map.Length; hostSlot++)
            {
                string hostName = (hostMaterials != null && hostSlot < hostMaterials.Length && hostMaterials[hostSlot] != null)
                    ? hostMaterials[hostSlot].name
                    : "(none)";

                int current = map[hostSlot];
                int popupIndex = (current >= 0 && current < refCount) ? current + 1 : 0;

                int newPopup = EditorGUILayout.Popup($"Host slot {hostSlot} [{hostName}]", popupIndex, options);
                int newValue = newPopup == 0 ? -1 : newPopup - 1;
                if (newValue != current)
                {
                    Undo.RecordObject(component, "Edit Slot Remapping");
                    map[hostSlot] = newValue;
                    CommitChange(component);
                }
            }
        }

        private void ResetToIdentity(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            if (remap.referenceSlotForHostSlot == null) return;

            Undo.RecordObject(component, "Reset Slot Remapping");
            for (int i = 0; i < remap.referenceSlotForHostSlot.Length; i++)
                remap.referenceSlotForHostSlot[i] = i;
            CommitChange(component);
        }

        /// <summary>
        /// Persists a direct field mutation made after <see cref="Undo.RecordObject"/>. On a prefab
        /// instance, <see cref="EditorUtility.SetDirty"/> alone does not register the change as a
        /// property override, so regenerated or hand-edited mappings would be lost on reload.
        /// </summary>
        private static void CommitChange(MaterialSlotRemapping component)
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }

        private static Material[] GetHostMaterials(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            Renderer renderer = remap.renderer;
            if (renderer == null)
            {
                Transform t = string.IsNullOrEmpty(remap.rendererPath)
                    ? component.transform
                    : component.transform.Find(remap.rendererPath);
                renderer = t != null ? t.GetComponent<Renderer>() : null;
            }
            return renderer != null ? renderer.sharedMaterials : null;
        }

        private static string DisplayPath(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            if (remap.renderer != null)
            {
                string live = ObjectMatcher.GetRelativePathFromRoot(remap.renderer.transform, component.transform);
                return string.IsNullOrEmpty(live) ? "(root)" : live;
            }
            return string.IsNullOrEmpty(remap.rendererPath) ? "(root)" : remap.rendererPath;
        }
    }
}
