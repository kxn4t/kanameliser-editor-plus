using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
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

        private HelpBox _descriptionBox;
        private ObjectField _referencePrefabField;
        private Button _generateButton;
        private VisualElement _messagesContainer;
        private VisualElement _remapsContainer;

        public override VisualElement CreateInspectorGUI()
        {
            var component = (MaterialSlotRemapping)target;
            var root = new VisualElement();

            _descriptionBox = new HelpBox(Localization.S("slotRemap.description"), HelpBoxMessageType.Info);
            root.Add(_descriptionBox);

            _referencePrefabField = new ObjectField(Localization.S("slotRemap.referencePrefab"))
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true
            };
            _referencePrefabField.AddToClassList(ObjectField.alignedFieldUssClassName);
            _referencePrefabField.style.marginTop = 8;
            _referencePrefabField.SetValueWithoutNotify(component.referencePrefab);
            _referencePrefabField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(component, "Set Reference Prefab");
                component.referencePrefab = evt.newValue as GameObject;
                CommitChange(component);
                UpdateGenerateButtonState();
            });
            root.Add(_referencePrefabField);

            _generateButton = new Button(() => GenerateMapping(component))
            {
                text = Localization.S("slotRemap.generateMapping")
            };
            root.Add(_generateButton);

            _messagesContainer = new VisualElement();
            root.Add(_messagesContainer);

            _remapsContainer = new VisualElement();
            _remapsContainer.style.marginTop = 8;
            root.Add(_remapsContainer);

            UpdateGenerateButtonState();
            RebuildRemaps();

            // Keep the UI in sync with undo/redo and external modifications
            root.TrackSerializedObjectValue(serializedObject, so => SyncFromComponent());

            Localization.RegisterLanguageChangeCallback(this, e => e.RefreshLocalizedTexts());

            return root;
        }

        private void SyncFromComponent()
        {
            if (target == null) return;

            var component = (MaterialSlotRemapping)target;
            _referencePrefabField.SetValueWithoutNotify(component.referencePrefab);
            UpdateGenerateButtonState();
            RebuildRemaps();
        }

        private void RefreshLocalizedTexts()
        {
            if (_descriptionBox == null || target == null) return;

            _descriptionBox.text = Localization.S("slotRemap.description");
            _referencePrefabField.label = Localization.S("slotRemap.referencePrefab");
            _generateButton.text = Localization.S("slotRemap.generateMapping");
            UpdateMessages();
            RebuildRemaps();
        }

        private void UpdateGenerateButtonState()
        {
            var component = (MaterialSlotRemapping)target;
            _generateButton.SetEnabled(component.referencePrefab != null);
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
                    UpdateMessages();
                    return;
                }

                Undo.RecordObject(component, "Generate Slot Remapping");
                component.remaps = result.remaps;
                CommitChange(component);
                Debug.Log($"[MA Material Helper] Generated slot remapping for {result.matchedRendererCount} renderer(s).");
            }

            UpdateMessages();
            RebuildRemaps();
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

        private void UpdateMessages()
        {
            _messagesContainer.Clear();

            if (_lastResult == null) return;

            foreach (var error in _lastResult.errors)
                _messagesContainer.Add(new HelpBox(error, HelpBoxMessageType.Error));
            foreach (var warning in _lastResult.warnings)
                _messagesContainer.Add(new HelpBox(warning, HelpBoxMessageType.Warning));

            if (_lastResult.success && _lastResult.warnings.Count == 0 && _lastResult.errors.Count == 0)
            {
                _messagesContainer.Add(new HelpBox(
                    Localization.S("slotRemap.mappingGenerated", _lastResult.matchedRendererCount),
                    HelpBoxMessageType.Info));
            }
        }

        private void RebuildRemaps()
        {
            _remapsContainer.Clear();

            var component = (MaterialSlotRemapping)target;
            if (component.remaps == null || component.remaps.Count == 0)
            {
                var noMappingLabel = new Label(Localization.S("slotRemap.noMapping"));
                noMappingLabel.style.fontSize = 10;
                noMappingLabel.style.opacity = 0.8f;
                _remapsContainer.Add(noMappingLabel);
                return;
            }

            var headerLabel = new Label(Localization.S("slotRemap.slotMappings", component.remaps.Count));
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _remapsContainer.Add(headerLabel);

            foreach (var remap in component.remaps)
            {
                if (remap == null) continue;

                string key = remap.rendererPath ?? "";
                if (!_foldouts.ContainsKey(key)) _foldouts[key] = false;

                var foldout = new Foldout
                {
                    text = DisplayPath(component, remap),
                    value = _foldouts[key]
                };
                foldout.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == foldout)
                        _foldouts[key] = evt.newValue;
                });

                BuildRemapEntry(foldout.contentContainer, component, remap);

                var resetButton = new Button(() => ResetToIdentity(component, remap))
                {
                    text = Localization.S("slotRemap.resetToIdentity")
                };
                resetButton.style.width = 260;
                resetButton.style.alignSelf = Align.FlexStart;
                foldout.contentContainer.Add(resetButton);

                _remapsContainer.Add(foldout);
            }
        }

        private void BuildRemapEntry(VisualElement container, MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            var map = remap.referenceSlotForHostSlot;
            if (map == null) return;

            var hostMaterials = GetHostMaterials(component, remap);
            int refCount = map.Length;

            var options = new List<string>(refCount + 1) { "(none)" };
            for (int i = 0; i < refCount; i++) options.Add($"Ref slot {i}");

            for (int hostSlot = 0; hostSlot < map.Length; hostSlot++)
            {
                string hostName = (hostMaterials != null && hostSlot < hostMaterials.Length && hostMaterials[hostSlot] != null)
                    ? hostMaterials[hostSlot].name
                    : "(none)";

                int current = map[hostSlot];
                int popupIndex = (current >= 0 && current < refCount) ? current + 1 : 0;

                var dropdown = new DropdownField($"Host slot {hostSlot} [{hostName}]", options, popupIndex);
                dropdown.AddToClassList(DropdownField.alignedFieldUssClassName);

                int capturedHostSlot = hostSlot;
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    int newValue = dropdown.index <= 0 ? -1 : dropdown.index - 1;
                    if (newValue != map[capturedHostSlot])
                    {
                        Undo.RecordObject(component, "Edit Slot Remapping");
                        map[capturedHostSlot] = newValue;
                        CommitChange(component);
                    }
                });

                container.Add(dropdown);
            }
        }

        private void ResetToIdentity(MaterialSlotRemapping component, RendererSlotRemap remap)
        {
            if (remap.referenceSlotForHostSlot == null) return;

            Undo.RecordObject(component, "Reset Slot Remapping");
            for (int i = 0; i < remap.referenceSlotForHostSlot.Length; i++)
                remap.referenceSlotForHostSlot[i] = i;
            CommitChange(component);
            RebuildRemaps();
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
