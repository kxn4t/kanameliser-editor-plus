using System.Collections.Generic;
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

            mirrorToggle = new Toggle("componentCopier.mirror") { value = mirrorMode };
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
                value = targetRoot,
            };
            targetField.AddToClassList("source-field");
            targetField.AddToClassList("ndmf-tr");
            targetField.RegisterValueChangedCallback(evt => OnTargetChanged(evt.newValue as GameObject));
            targetRow.Add(targetField);

            // A mirror copy needs no target: the source is its own. The direction takes the place of the target.
            mirrorDirectionField = new PopupField<Side>("componentCopier.target",
                new List<Side> { Side.Left, Side.Right }, mirrorSide, OtherSideLabel, OtherSideLabel);
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

            UpdateMirrorControls();
            ValidateTarget();
            UpdateSourceWarnings();
        }

        private void OnSourceChanged(GameObject newSource)
        {
            sourceRoot = newSource;
            if (mirrorMode && newSource != null) AdoptSideOf(newSource.transform);
            manualMappings.Clear();
            ClearDetailReport();
            ValidateTarget();
            UpdateSourceWarnings();
            Rescan(resetSelection: true);
        }

        private void OnTargetChanged(GameObject newTarget)
        {
            // Picking a target ("Use as Target" from the menu, ...) asks for an ordinary copy
            bool leavesMirror = newTarget != null && mirrorMode;
            if (leavesMirror) LeaveMirrorMode();

            targetRoot = newTarget;
            manualMappings.Clear();
            ClearDetailReport();
            ValidateTarget();
            UpdateSourceWarnings();
            // The list of a mirror copy holds one side only
            if (leavesMirror) Rescan(resetSelection: false);
            else Recompute();
        }

        /// <summary>Turns the mirror copy off from code, when the user asked for something it cannot do.</summary>
        private void LeaveMirrorMode()
        {
            mirrorMode = false;
            mirrorToggle?.SetValueWithoutNotify(false);
            UpdateMirrorControls();
        }

        /// <summary>
        /// Copies in the other direction. The component list and the manual mappings belong to the old source,
        /// so they start over just like after picking a new source by hand.
        /// </summary>
        private void SwapRoots()
        {
            (sourceRoot, targetRoot) = (targetRoot, sourceRoot);
            sourceField.SetValueWithoutNotify(sourceRoot);
            targetField.SetValueWithoutNotify(targetRoot);
            OnSourceChanged(sourceRoot);
        }

        /// <summary>
        /// The source stays, so the checks of the components that are still listed stay too. The mirror map
        /// pairs other objects than the map to a target, so the manual mappings start over.
        /// </summary>
        private void OnMirrorChanged(bool value)
        {
            mirrorMode = value;
            if (mirrorMode && sourceRoot != null) AdoptSideOf(sourceRoot.transform);
            manualMappings.Clear();
            ClearDetailReport();
            UpdateMirrorControls();
            ValidateTarget();
            UpdateSourceWarnings();
            Rescan(resetSelection: false);
        }

        private void OnMirrorSideChanged(Side side)
        {
            mirrorSide = side;
            ClearDetailReport();
            Rescan(resetSelection: false);
        }

        /// <summary>
        /// A source on one side (a hand, a component picked from its context menu, ...) only has something to
        /// copy in one direction, so that direction is chosen. The avatar itself leaves the choice alone.
        /// </summary>
        /// <param name="sides">Those of the mirror map when it is at hand; read from the avatar otherwise.</param>
        private void AdoptSideOf(Transform transform, MirrorSides sides = null)
        {
            var side = (sides ?? new MirrorSides(MirrorRoot())).Of(transform);
            if (side == Side.None || side == mirrorSide) return;

            mirrorSide = side;
            mirrorDirectionField?.SetValueWithoutNotify(side);
        }

        /// <summary>A mirror copy has no target of its own: the target field makes way for the direction.</summary>
        private void UpdateMirrorControls()
        {
            if (targetField == null) return;

            targetField.style.display = mirrorMode ? DisplayStyle.None : DisplayStyle.Flex;
            mirrorDirectionField.style.display = mirrorMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The direction as a target: "Other side (L → R)".</summary>
        private static string OtherSideLabel(Side side)
        {
            return Localization.S("componentCopier.mirror.otherSide", Localization.S(side == Side.Left
                ? "componentCopier.mirror.leftToRight"
                : "componentCopier.mirror.rightToLeft"));
        }

        /// <summary>
        /// Assets are accepted as a target for diff checks only: applying to an asset (e.g. a Prefab Variant)
        /// cannot be undone, so Apply stays disabled for them.
        /// </summary>
        private void ValidateTarget()
        {
            targetWarningKey = null;
            targetWarningIsError = false;

            if (mirrorMode)
            {
                // The source is its own target, and an asset can take a copy no more than as a target
                if (IsTargetAsset()) targetWarningKey = "componentCopier.warning.mirrorSourceIsAsset";
                return;
            }

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

        private bool IsTargetAsset() => EffectiveTarget != null && EditorUtility.IsPersistent(EffectiveTarget);

        private bool IsTargetUsable() => targetRoot != null && !targetWarningIsError;

        private void UpdateSourceWarnings()
        {
            if (targetWarningLabel == null) return;

            bool visible = targetWarningKey != null;
            targetWarningLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            targetWarningLabel.text = visible ? Localization.S(targetWarningKey) : "";
            targetWarningLabel.EnableInClassList("inline-warning--error", targetWarningIsError);

            refreshButton.tooltip = Localization.S("componentCopier.refresh:tooltip");
            swapButton.tooltip = Localization.S("componentCopier.swap:tooltip");
            // A mirror copy has no target to swap with; the direction does that job
            swapButton.SetEnabled(!mirrorMode && (sourceRoot != null || targetRoot != null));
            mirrorToggle.tooltip = Localization.S("componentCopier.mirror:tooltip");
            // PopupField caches its formatted text
            mirrorDirectionField.SetValueWithoutNotify(mirrorDirectionField.value);
        }
    }
}
