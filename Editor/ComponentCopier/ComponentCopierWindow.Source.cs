using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    public partial class ComponentCopierWindow
    {
        private ObjectField sourceField;
        private ObjectField targetField;
        private Label targetWarningLabel;
        private Button refreshButton;
        private string targetWarningKey;
        private bool targetWarningIsError;

        private void CreateSourceSection(VisualElement root)
        {
            var section = new VisualElement();
            section.AddToClassList("source-section");
            root.Add(section);

            // Source and target are stacked vertically so long localized labels never squeeze the fields
            var sourceRow = new VisualElement();
            sourceRow.AddToClassList("source-row");
            section.Add(sourceRow);

            sourceField = new ObjectField("componentCopier.source")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = sourceRoot,
            };
            sourceField.AddToClassList("source-field");
            sourceField.AddToClassList("ndmf-tr");
            sourceField.RegisterValueChangedCallback(evt => OnSourceChanged(evt.newValue as GameObject));
            sourceRow.Add(sourceField);

            refreshButton = new Button(() => Rescan(resetSelection: false));
            refreshButton.AddToClassList("refresh-button");

            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            if (refreshIcon?.image != null)
            {
                var icon = new Image { image = refreshIcon.image };
                icon.AddToClassList("refresh-icon");
                refreshButton.Add(icon);
            }
            else
            {
                refreshButton.text = "↻";
            }

            sourceRow.Add(refreshButton);

            var arrow = new Label("↓");
            arrow.AddToClassList("source-arrow");
            section.Add(arrow);

            targetField = new ObjectField("componentCopier.target")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = targetRoot,
            };
            targetField.AddToClassList("target-field");
            targetField.AddToClassList("ndmf-tr");
            targetField.RegisterValueChangedCallback(evt => OnTargetChanged(evt.newValue as GameObject));
            section.Add(targetField);

            targetWarningLabel = new Label();
            targetWarningLabel.AddToClassList("inline-warning");
            section.Add(targetWarningLabel);

            ValidateTarget();
            UpdateSourceWarnings();
        }

        private void OnSourceChanged(GameObject newSource)
        {
            sourceRoot = newSource;
            manualMappings.Clear();
            ClearDetailReport();
            ValidateTarget();
            UpdateSourceWarnings();
            Rescan(resetSelection: true);
        }

        private void OnTargetChanged(GameObject newTarget)
        {
            targetRoot = newTarget;
            manualMappings.Clear();
            ClearDetailReport();
            ValidateTarget();
            UpdateSourceWarnings();
            Recompute();
        }

        /// <summary>
        /// Assets are accepted as a target for diff checks only: applying to an asset (e.g. a Prefab Variant)
        /// cannot be undone, so Apply stays disabled for them.
        /// </summary>
        private void ValidateTarget()
        {
            targetWarningKey = null;
            targetWarningIsError = false;

            if (targetRoot == null) return;

            if (sourceRoot != null)
            {
                if (targetRoot == sourceRoot)
                {
                    targetWarningKey = "componentCopier.warning.sameObject";
                    targetWarningIsError = true;
                    return;
                }

                if (targetRoot.transform.IsChildOf(sourceRoot.transform) ||
                    sourceRoot.transform.IsChildOf(targetRoot.transform))
                {
                    targetWarningKey = "componentCopier.warning.nested";
                    targetWarningIsError = true;
                    return;
                }
            }

            if (IsTargetAsset()) targetWarningKey = "componentCopier.warning.targetIsAsset";
        }

        private bool IsTargetAsset() => targetRoot != null && EditorUtility.IsPersistent(targetRoot);

        private bool IsTargetUsable() => targetRoot != null && !targetWarningIsError;

        private void UpdateSourceWarnings()
        {
            if (targetWarningLabel == null) return;

            bool visible = targetWarningKey != null;
            targetWarningLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            targetWarningLabel.text = visible ? Localization.S(targetWarningKey) : "";
            targetWarningLabel.EnableInClassList("inline-warning--error", targetWarningIsError);

            refreshButton.tooltip = Localization.S("componentCopier.refresh:tooltip");
        }
    }
}
