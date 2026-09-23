using System.Collections.Generic;
using System.Linq;
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
        private Button swapButton;
        private Toggle mirrorToggle;
        private PopupField<Side> mirrorDirectionField;

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
            };
            sourceField.AddToClassList("source-field");
            sourceField.AddToClassList("ndmf-tr");
            sourceField.RegisterValueChangedCallback(evt => SetSource(evt.newValue as GameObject));
            sourceRow.Add(sourceField);

            refreshButton = new Button(Rescan);
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

            // The mirror toggle decides where the copy goes, so it sits on the way from the source to the target.
            // The two sides of the row share the rest of the width equally, which keeps the arrow centered.
            var arrowRow = new VisualElement();
            arrowRow.AddToClassList("arrow-row");
            section.Add(arrowRow);

            var arrowRowStart = new VisualElement();
            arrowRowStart.AddToClassList("arrow-row__side");
            arrowRow.Add(arrowRowStart);

            var targetArrow = new Label("↓");
            targetArrow.AddToClassList("source-arrow");
            arrowRow.Add(targetArrow);

            var arrowRowEnd = new VisualElement();
            arrowRowEnd.AddToClassList("arrow-row__side");
            arrowRowEnd.AddToClassList("arrow-row__side--end");
            arrowRow.Add(arrowRowEnd);

            mirrorToggle = new Toggle("componentCopier.mirror");
            mirrorToggle.AddToClassList("mirror-toggle");
            mirrorToggle.AddToClassList("ndmf-tr");
            mirrorToggle.RegisterValueChangedCallback(evt => OnMirrorChanged(evt.newValue));
            arrowRowEnd.Add(mirrorToggle);

            var targetRow = new VisualElement();
            targetRow.AddToClassList("source-row");
            section.Add(targetRow);

            targetField = new ObjectField("componentCopier.target")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
            };
            targetField.AddToClassList("source-field");
            targetField.AddToClassList("ndmf-tr");
            targetField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as GameObject));
            targetRow.Add(targetField);

            // A mirror copy needs no target: the source is its own. The direction takes the place of the target.
            mirrorDirectionField = new PopupField<Side>("componentCopier.target",
                new List<Side> { Side.Left, Side.Right }, session.MirrorSide, OtherSideLabel, OtherSideLabel);
            mirrorDirectionField.AddToClassList("source-field");
            mirrorDirectionField.AddToClassList("ndmf-tr");
            mirrorDirectionField.RegisterValueChangedCallback(evt => OnMirrorSideChanged(evt.newValue));
            targetRow.Add(mirrorDirectionField);

            // Stays in mirror mode, disabled, so that the fields keep the right edge of the source field
            swapButton = new Button(SwapRoots) { text = "⇅" };
            swapButton.AddToClassList("swap-button");
            targetRow.Add(swapButton);

            targetWarningLabel = new Label();
            targetWarningLabel.AddToClassList("inline-warning");
            section.Add(targetWarningLabel);

            SyncSourceControls();
            UpdateSourceWarnings();
        }

        /// <summary>
        /// Takes a new source, from the field or from <see cref="Open"/>. The component list starts over, see
        /// <see cref="CopySession.SetSource"/>.
        /// </summary>
        private void SetSource(GameObject source, Component only = null)
        {
            session.SetSource(source, only);
            expandedIssues.Clear();
            // The component asked for is in view right away
            var picked = only != null ? session.Entries.FirstOrDefault(e => e.Component == only) : null;
            if (picked != null) expandedTypes.Add(GroupId(picked));
            OnInputsChanged();
        }

        private void SetTarget(GameObject target)
        {
            session.SetTarget(target);
            OnInputsChanged();
        }

        private void SwapRoots()
        {
            session.Swap();
            expandedIssues.Clear();
            OnInputsChanged();
        }

        private void OnMirrorChanged(bool value)
        {
            session.SetMirrorMode(value);
            OnInputsChanged();
        }

        private void OnMirrorSideChanged(Side side)
        {
            session.SetMirrorSide(side);
            OnInputsChanged();
        }

        /// <summary>
        /// After the roots or the mode changed. The report snapshot belongs to the old setup, and the session may
        /// have changed more than the control that was used: a target leaves the mirror copy, a source on one
        /// side picks the direction.
        /// </summary>
        private void OnInputsChanged()
        {
            ClearDetailReport();
            SyncSourceControls();
            RenderAll();
        }

        /// <summary>
        /// Shows the roots and the mode of the session without calling back. The controls may not exist yet:
        /// <see cref="Open"/> fills in the roots before the window has its GUI.
        /// </summary>
        private void SyncSourceControls()
        {
            if (sourceField == null) return;

            sourceField.SetValueWithoutNotify(session.SourceRoot);
            targetField.SetValueWithoutNotify(session.TargetRoot);
            mirrorToggle.SetValueWithoutNotify(session.MirrorMode);
            mirrorDirectionField.SetValueWithoutNotify(session.MirrorSide);

            // A mirror copy has no target of its own: the target field makes way for the direction
            targetField.style.display = session.MirrorMode ? DisplayStyle.None : DisplayStyle.Flex;
            mirrorDirectionField.style.display = session.MirrorMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The direction as a target: "Other side (L → R)".</summary>
        private static string OtherSideLabel(Side side)
        {
            return Localization.S("componentCopier.mirror.otherSide", Localization.S(side == Side.Left
                ? "componentCopier.mirror.leftToRight"
                : "componentCopier.mirror.rightToLeft"));
        }

        private void UpdateSourceWarnings()
        {
            if (targetWarningLabel == null) return;

            string warningKey = session.TargetWarning(out bool isError);
            targetWarningLabel.style.display = warningKey != null ? DisplayStyle.Flex : DisplayStyle.None;
            targetWarningLabel.text = warningKey != null ? Localization.S(warningKey) : "";
            targetWarningLabel.EnableInClassList("inline-warning--error", isError);

            refreshButton.tooltip = Localization.S("componentCopier.refresh:tooltip");
            swapButton.tooltip = Localization.S("componentCopier.swap:tooltip");
            // A mirror copy has no target to swap with; the direction does that job
            swapButton.SetEnabled(!session.MirrorMode && (session.SourceRoot != null || session.TargetRoot != null));
            mirrorToggle.tooltip = Localization.S("componentCopier.mirror:tooltip");
            // PopupField caches its formatted text
            mirrorDirectionField.SetValueWithoutNotify(mirrorDirectionField.value);
        }
    }
}
