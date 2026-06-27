using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.EditorPlus.Runtime;

namespace Kanameliser.Editor.MAMaterialHelper.SlotRemapping
{
    [CustomEditor(typeof(MaterialSlotRemapping))]
    public class MaterialSlotRemappingEditor : UnityEditor.Editor
    {
        private RemapGenerationResult _lastResult;
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            var component = (MaterialSlotRemapping)target;

            EditorGUILayout.HelpBox(
                "Maps this (converted) outfit's material slots back to a reference (original) outfit, " +
                "so Material Setter/Swap color setups land on the correct slots.\n" +
                "Generate the mapping right after conversion, before recoloring the outfit.",
                MessageType.Info);

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            var newRef = (GameObject)EditorGUILayout.ObjectField(
                "Reference Prefab", component.referencePrefab, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(component, "Set Reference Prefab");
                component.referencePrefab = newRef;
                EditorUtility.SetDirty(component);
            }

            using (new EditorGUI.DisabledScope(component.referencePrefab == null))
            {
                if (GUILayout.Button("Generate Mapping"))
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
                Undo.RecordObject(component, "Generate Slot Remapping");
                component.remaps = result.remaps;
                EditorUtility.SetDirty(component);
                Debug.Log($"[MA Material Helper] Generated slot remapping for {result.matchedRendererCount} renderer(s).");
            }
        }

        private void DrawResultMessages()
        {
            if (_lastResult == null) return;

            foreach (var error in _lastResult.errors)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (var warning in _lastResult.warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            if (_lastResult.success && _lastResult.warnings.Count == 0 && _lastResult.errors.Count == 0)
                EditorGUILayout.HelpBox($"Mapping generated for {_lastResult.matchedRendererCount} renderer(s).", MessageType.Info);
        }

        private void DrawRemaps(MaterialSlotRemapping component)
        {
            if (component.remaps == null || component.remaps.Count == 0)
            {
                EditorGUILayout.LabelField("No mapping generated yet.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"Slot Mappings ({component.remaps.Count} renderers)", EditorStyles.boldLabel);

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
                if (GUILayout.Button("Reset This Renderer To Identity", GUILayout.Width(260)))
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
                    EditorUtility.SetDirty(component);
                }
            }
        }

        private void ResetToIdentity(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            if (remap.referenceSlotForHostSlot == null) return;

            Undo.RecordObject(component, "Reset Slot Remapping");
            for (int i = 0; i < remap.referenceSlotForHostSlot.Length; i++)
                remap.referenceSlotForHostSlot[i] = i;
            EditorUtility.SetDirty(component);
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
