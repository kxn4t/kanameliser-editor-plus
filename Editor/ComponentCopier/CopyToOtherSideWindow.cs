using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// The small window behind "Copy to Other Side": copies one object, its pose and its components to the other
    /// side of the avatar, see <see cref="OtherSideCopy"/>. It shows the counterpart, whether to create it, and what
    /// to copy; anything more is left to the main window.
    /// </summary>
    internal sealed class CopyToOtherSideWindow : EditorWindow
    {
        private const string Title = "Copy to Other Side";
        private const long RefreshDebounceMs = 400;

        // Kept across domain reloads, so that the copy starts over as it was asked for. The choices made in the
        // window start over then, but not the objects an apply created: those stay pinned, see KeepCreated.
        [SerializeField] private Transform source;
        [SerializeField] private Component only;
        private OtherSideCopy copy;

        // What the last Apply could not do. Cleared by the next choice.
        private string message;

        private VisualElement body;
        private Button cancelButton;
        private Button applyButton;
        private bool refreshScheduled;
        private IVisualElementScheduledItem scheduledRefresh;

        /// <param name="only">The component the menu was used on, which starts out as the only one checked.</param>
        internal static void Open(Transform source, Component only = null)
        {
            var window = GetWindow<CopyToOtherSideWindow>(true, Title, true);
            window.minSize = new Vector2(360, 280);
            window.CancelScheduledRefresh();
            window.source = source;
            window.only = only;
            window.copy = new OtherSideCopy(source, only);
            window.message = null;
            window.Render();
        }

        private void OnEnable()
        {
            // Also for a source that is gone after a domain reload: the window says so instead of staying blank
            copy ??= new OtherSideCopy(source, only);

            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            EditorApplication.hierarchyChanged += ScheduleRefresh;
            Undo.undoRedoPerformed += ScheduleRefresh;
        }

        // A closed window may still be called back (on a language change) until it is collected
        private void OnDestroy() => copy = null;

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            EditorApplication.hierarchyChanged -= ScheduleRefresh;
            Undo.undoRedoPerformed -= ScheduleRefresh;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ComponentCopierWindow.UssPath);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList("component-copier");
            root.AddToClassList("other-side");

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("main-scroll");
            root.Add(scrollView);

            body = new VisualElement();
            scrollView.Add(body);

            var footer = new VisualElement();
            footer.AddToClassList("footer");
            root.Add(footer);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("button-row");
            footer.Add(buttonRow);

            cancelButton = new Button(Close);
            buttonRow.Add(cancelButton);

            applyButton = new Button(Apply);
            applyButton.AddToClassList("apply-button");
            buttonRow.Add(applyButton);

            Localization.RegisterLanguageChangeCallback(this, w => w.Render());
            Render();
        }

        #region Model

        private void OnObjectChangesPublished(ref ObjectChangeEventStream stream) => ScheduleRefresh();

        /// <summary>Scene edits arrive in bursts, so refreshes are debounced like in the main window.</summary>
        private void ScheduleRefresh()
        {
            // Right away, not with the refresh: a checkbox ticked in the meantime must not plan with a map that
            // still holds deleted objects
            copy?.InvalidateMap();
            if (refreshScheduled || body == null || copy == null) return;

            refreshScheduled = true;
            scheduledRefresh = rootVisualElement.schedule.Execute(() =>
            {
                refreshScheduled = false;
                copy?.Rescan();
                Render();
            }).StartingIn(RefreshDebounceMs);
        }

        private void CancelScheduledRefresh()
        {
            scheduledRefresh?.Pause();
            refreshScheduled = false;
        }

        /// <summary>Passes a choice on and shows the plan that follows from it.</summary>
        private void Choose(Action change)
        {
            message = null;
            change();
            Render();
        }

        private void Apply()
        {
            if (copy?.Plan == null || !copy.HasWork) return;

            // Planned again first if a scene change is still waiting for its refresh, as in the main window: a plan
            // that comes out the same is the one on screen and is applied, one that changed is shown instead
            if (refreshScheduled)
            {
                CancelScheduledRefresh();
                string shown = copy.Plan.Fingerprint();
                copy.Rescan();
                if (copy.Plan == null || copy.Plan.Fingerprint() != shown)
                {
                    ShowMessage(Localization.S("componentCopier.report.stale"));
                    return;
                }
            }

            var plan = copy.Plan;
            ExecutionResult result;
            try
            {
                result = CopyExecutor.Execute(plan);
            }
            catch (Exception exception)
            {
                // The executor has put everything back already
                Debug.LogException(exception);
                copy.Rescan();
                ShowMessage(Localization.S("componentCopier.report.applyFailed", exception.Message));
                return;
            }

            if (result.Stale)
            {
                Debug.Log("[Component Copier] Nothing was applied because the plan was out of date: " +
                          $"{result.StaleReason}.");
                copy.Rescan();
                ShowMessage(Localization.S("componentCopier.report.stale"));
                return;
            }

            var counterpart = CounterpartAfter(plan);
            Debug.Log($"[Component Copier] Copied '{copy.Source.name}' to the other side" +
                      (counterpart != null ? $" ('{counterpart.name}')" : "") +
                      $" with {result.WrittenComponents} component(s)" +
                      (result.PosedObjects > 0 ? " and the pose." : "."));

            // What was created is the counterpart from now on, whichever object the copy is asked for next
            copy.KeepCreated(plan);

            // The rest was copied; the window stays so that the failures can be read
            if (result.Failed.Count > 0)
            {
                foreach (var failed in result.Failed)
                {
                    Debug.LogWarning($"[Component Copier] {failed.Entry.Type.Name} of '{failed.Entry.Host.name}' " +
                                     "could not be added on the other side.");
                }

                // Applying again copies what failed, into the counterpart that exists now
                copy.Rescan();
                ShowMessage(Localization.S("componentCopier.otherSide.partiallyApplied", result.Failed.Count));
                return;
            }

            if (counterpart != null) EditorGUIUtility.PingObject(counterpart.gameObject);
            Close();
        }

        /// <summary>The object that took the copy: the counterpart that was posed or written to, or the one created.</summary>
        private Transform CounterpartAfter(CopyPlan plan)
        {
            var pose = plan.Poses.FirstOrDefault();
            if (pose != null && pose.Target != null) return pose.Target;

            var created = plan.ObjectsToCreate.FirstOrDefault(o => o.Source == copy.Source);
            if (created != null && created.Created != null) return created.Created;

            var written = plan.Components.FirstOrDefault(c => c.Entry.Host == copy.Source && c.Actual != null);
            return written != null ? written.Actual.transform : null;
        }

        private void ShowMessage(string text)
        {
            message = text;
            Render();
        }

        #endregion

        #region Rendering

        private void Render()
        {
            if (body == null) return;

            body.Clear();
            cancelButton.text = Localization.S("common.cancel");
            applyButton.text = Localization.S("componentCopier.apply");
            applyButton.SetEnabled(false);
            if (copy == null) return;

            var sourceField = new ObjectField(Localization.S("componentCopier.source"))
            {
                objectType = typeof(GameObject),
                value = copy.Source != null ? copy.Source.gameObject : null,
            };
            sourceField.AddToClassList("other-side__field");
            sourceField.SetEnabled(false);
            body.Add(sourceField);

            string problem = copy.Problem;
            if (problem != null)
            {
                body.Add(InfoLabel(Localization.S(problem)));
                return;
            }

            RenderCounterpart();
            RenderContents();
            RenderNotes();
            applyButton.SetEnabled(copy.HasWork);
        }

        private void RenderCounterpart()
        {
            var suggestion = copy.Suggestion;

            var row = new VisualElement();
            row.AddToClassList("source-row");
            body.Add(row);

            var counterpartField = new ObjectField(OtherSideLabel(copy.Side))
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = copy.Counterpart != null ? copy.Counterpart.gameObject : null,
            };
            counterpartField.AddToClassList("source-field");
            counterpartField.AddToClassList("other-side__field");
            counterpartField.RegisterValueChangedCallback(evt => OnCounterpartPicked(evt.newValue as GameObject));
            row.Add(counterpartField);

            if (suggestion != null)
            {
                var confirmButton = new Button(() => Choose(() => copy.SetCounterpart(suggestion)))
                {
                    text = Localization.S("componentCopier.otherSide.confirm"),
                    tooltip = Localization.S("componentCopier.otherSide.confirm:tooltip", suggestion.name),
                };
                confirmButton.AddToClassList("mapping-button");
                row.Add(confirmButton);
            }

            // A suggestion found by name can be another object altogether (the avatar's own, next to an outfit's, or
            // one on the middle line), so creating one is offered as plainly as taking it
            bool waitsForSuggestion = suggestion != null || copy.UnusableSuggestion != null;
            if (waitsForSuggestion && copy.Created == null && copy.CanCreate)
            {
                var createButton = new Button(() => Choose(() => copy.CreateNew()))
                {
                    text = Localization.S("componentCopier.otherSide.createNew"),
                    tooltip = Localization.S("componentCopier.otherSide.createNew:tooltip"),
                };
                createButton.AddToClassList("mapping-button");
                row.Add(createButton);
            }

            if (copy.HasManualMappings)
            {
                var resetButton = new Button(() => Choose(copy.ResetMappings))
                {
                    text = Localization.S("componentCopier.otherSide.reset"),
                    tooltip = Localization.S("componentCopier.otherSide.reset:tooltip"),
                };
                resetButton.AddToClassList("mapping-button");
                row.Add(resetButton);
            }

            body.Add(NoteLabel(CounterpartNote()));
            // Objects of the same name are told apart by where they are
            if (copy.Counterpart != null)
                body.Add(NoteLabel(Localization.S("componentCopier.otherSide.location", copy.PathOf(copy.Counterpart))));

            // Without a counterpart the copy creates one, unless told not to. Other reasons keep it from being
            // created whatever the toggle says. It also governs the other objects the copy creates, so it is in view
            // with those, and while it is off.
            var blockReason = copy.BlockReason;
            if (copy.Created != null || blockReason == BlockReason.HostUnmapped || !copy.CreateMissing ||
                copy.OtherCreated.Any())
            {
                var createToggle = new Toggle(Localization.S("componentCopier.otherSide.create"))
                {
                    value = copy.CreateMissing,
                };
                createToggle.AddToClassList("other-side__create");
                createToggle.RegisterValueChangedCallback(evt => Choose(() => copy.SetCreateMissing(evt.newValue)));
                body.Add(createToggle);

                string path = copy.CreatedPath();
                if (path != null) body.Add(NoteLabel(Localization.S("componentCopier.otherSide.createdAt", path)));
                if (HasSameNameSibling(copy.Created))
                    body.Add(WarningLabel(Localization.S("componentCopier.otherSide.sameName")));
            }

            if (blockReason != BlockReason.None) body.Add(WarningLabel(BlockMessage()));

            // A parent keeps the counterpart from being created: its suggestion can be taken here, or a new one
            // created in its place, or else the counterpart of the source named instead
            var blockingParent = copy.BlockingParent;
            if (blockingParent != null && blockReason == BlockReason.HostNeedsReview)
            {
                AddConfirmSuggestionButton(blockingParent);
                if (copy.CanCreateAt(blockingParent))
                {
                    var createButton = new Button(() => Choose(() => copy.CreateNew(blockingParent)))
                    {
                        text = Localization.S("componentCopier.otherSide.createNewParent", blockingParent.name),
                        tooltip = Localization.S("componentCopier.otherSide.createNew:tooltip"),
                    };
                    createButton.AddToClassList("other-side__parent-confirm");
                    body.Add(createButton);
                }
            }
        }

        /// <summary>Why the counterpart cannot take the copy, naming the parent that is the reason if it is one.</summary>
        private string BlockMessage()
        {
            var blockReason = copy.BlockReason;
            var blockingParent = copy.BlockingParent;
            if (blockingParent == null)
                return Localization.S(ComponentCopierStrings.OtherSideBlockReasonKey(blockReason));
            if (blockReason != BlockReason.HostNeedsReview)
                return Localization.S("componentCopier.otherSide.blocked.parentBoneMissing", blockingParent.name);

            // A suggestion that cannot be the counterpart (on the middle line, ...) cannot be confirmed either
            return copy.SuggestionFor(blockingParent) != null
                ? Localization.S("componentCopier.otherSide.blocked.parentNeedsReview", blockingParent.name)
                : Localization.S("componentCopier.otherSide.blocked.parentSuggestionInvalid", blockingParent.name);
        }

        private string CounterpartNote()
        {
            var mapping = copy.Mapping;
            string reason = Localization.S(ComponentCopierStrings.MappingReasonKey(mapping.Reason));

            // A nested prefab around the source brings its own, whatever the map says
            if (copy.Created != null)
            {
                return copy.Suggestion != null
                    ? Localization.S("componentCopier.otherSide.note.suggestionUnused", copy.Suggestion.name)
                    : Localization.S("componentCopier.otherSide.note.missing");
            }

            if (copy.UnusableSuggestion != null)
                return Localization.S("componentCopier.otherSide.note.suggestionInvalid", copy.UnusableSuggestion.name);

            switch (mapping.State)
            {
                case MappingState.Confirmed:
                    return reason;
                case MappingState.NeedsReview when mapping.Target != null:
                    return Localization.S("componentCopier.otherSide.note.needsReview", reason);
                case MappingState.Manual when mapping.Target != null:
                    return Localization.S(copy.IsKept(copy.Source)
                        ? "componentCopier.otherSide.note.paired"
                        : "componentCopier.otherSide.note.manual");
                default:
                    return Localization.S("componentCopier.otherSide.note.missing");
            }
        }

        /// <summary>
        /// A button that takes the suggestion for an object that keeps the copy waiting, named with both objects and
        /// with where the suggested one is. None for a suggestion that cannot be a counterpart.
        /// </summary>
        private void AddConfirmSuggestionButton(Transform source)
        {
            var suggestion = copy.SuggestionFor(source);
            if (suggestion == null) return;

            var confirmButton = new Button(() => Choose(() => copy.ConfirmSuggestion(source)))
            {
                text = Localization.S("componentCopier.otherSide.confirmParent", source.name, suggestion.name),
                tooltip = Localization.S("componentCopier.otherSide.location", copy.PathOf(suggestion)),
            };
            confirmButton.AddToClassList("other-side__parent-confirm");
            body.Add(confirmButton);
        }

        /// <summary>True when the object would be created next to one of the same name, which it is not taken for.</summary>
        private static bool HasSameNameSibling(PlannedObject created)
        {
            return created != null && created.ExistingParent != null &&
                   Hierarchy.FindChild(created.ExistingParent, created.Name, 0) != null;
        }

        private void OnCounterpartPicked(GameObject picked)
        {
            // An object that cannot be the counterpart is not taken, and the field goes back to the one in use
            message = null;
            if (!copy.SetCounterpart(picked != null ? picked.transform : null))
                message = Localization.S("componentCopier.otherSide.invalidCounterpart");
            Render();
        }

        private void RenderContents()
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            section.AddToClassList("other-side__contents");
            body.Add(section);

            var title = new Label(Localization.S("componentCopier.otherSide.contents"));
            title.AddToClassList("section-title");
            section.Add(title);

            section.Add(PoseRow());
            foreach (var entry in copy.Entries)
                section.Add(ComponentRow(entry));
        }

        /// <summary>
        /// The Transform: optional for a counterpart that exists, and part of creating one that does not.
        /// </summary>
        private VisualElement PoseRow()
        {
            var row = new VisualElement();
            row.AddToClassList("component-row");

            // Checked for good only when the counterpart is created; one that waits for the user may still be a bone
            var toggle = new Toggle { value = copy.CopyPose || copy.Created != null };
            toggle.SetEnabled(copy.CounterpartExists);
            if (copy.Created != null)
                toggle.tooltip = Localization.S("componentCopier.otherSide.pose.created:tooltip");
            else if (copy.IsBone && !copy.CopyPose && copy.CounterpartExists)
                toggle.tooltip = Localization.S("componentCopier.otherSide.pose.bone:tooltip");
            toggle.RegisterValueChangedCallback(evt => Choose(() => copy.SetCopyPose(evt.newValue)));
            row.Add(toggle);

            var icon = new Image { image = EditorGUIUtility.ObjectContent(copy.Source, typeof(Transform)).image };
            icon.AddToClassList("row-icon");
            row.Add(icon);

            var label = new Label(Localization.S("componentCopier.otherSide.pose"))
            {
                tooltip = Localization.S("componentCopier.otherSide.pose:tooltip"),
            };
            label.AddToClassList("row-path");
            row.Add(label);

            var pose = copy.Pose;
            if (pose != null) row.Add(StatusChip(pose.Action, ActionName(pose.Action), ChipTooltip(pose.Action)));
            return row;
        }

        private VisualElement ComponentRow(ComponentEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("component-row");
            row.EnableInClassList("component-row--excluded", entry.Category == ComponentCategory.ExcludedByDefault);

            bool withPrefab = copy.ArrivesWithPrefab(entry);
            var toggle = new Toggle { value = copy.IsChecked(entry) };
            // The prefab arrives as a whole; leaving a part of it out takes the main window
            toggle.SetEnabled(!withPrefab);
            if (withPrefab) toggle.tooltip = Localization.S("componentCopier.otherSide.withPrefab:tooltip");
            toggle.RegisterValueChangedCallback(evt => Choose(() => copy.SetChecked(entry, evt.newValue)));
            row.Add(toggle);

            var icon = new Image { image = EditorGUIUtility.ObjectContent(entry.Component, entry.Type).image };
            icon.AddToClassList("row-icon");
            row.Add(icon);

            string typeName = entry.Key.Index > 0 ? $"{entry.Type.Name} ({entry.Key.Index + 1})" : entry.Type.Name;
            var label = new Label(typeName) { tooltip = entry.Type.FullName };
            label.AddToClassList("row-path");
            row.Add(label);

            if (!copy.TryGetPlanned(entry, out var planned)) return row;

            int unresolved = planned.References.Count(r => r.Kind == ReferenceKind.InternalUnresolved);
            if (unresolved > 0)
            {
                var badge = new Label("⚠ " + unresolved)
                {
                    tooltip = Localization.S("componentCopier.row.unresolved:tooltip", unresolved),
                };
                badge.AddToClassList("warning-badge");
                row.Add(badge);
            }

            string chipText = planned.WillWrite && planned.ArrivesWithPrefab
                ? Localization.S("componentCopier.action.withPrefab")
                : ActionName(planned.Action);
            row.Add(StatusChip(planned.Action, chipText, ChipTooltip(planned.Action)));
            return row;
        }

        /// <summary>
        /// What else the copy does: the prefab that comes along, the other objects it creates, the references that end
        /// up empty and the settings kept as they are. The components of a prefab that comes along count as well.
        /// </summary>
        private void RenderNotes()
        {
            var plan = copy.Plan;

            // The outermost prefab is the one instantiated, also when the counterpart is the root of one inside it
            var created = copy.Created;
            var prefab = created?.PrefabRoot ?? (created != null && created.IsPrefabRoot ? created : null);
            if (prefab != null) body.Add(NoteLabel(Localization.S("componentCopier.otherSide.prefab", prefab.Name)));

            foreach (var other in copy.OtherCreated)
            {
                body.Add(NoteLabel(Localization.S(other.IsPrefabRoot
                    ? "componentCopier.otherSide.alsoCreatedPrefab"
                    : "componentCopier.otherSide.alsoCreated", copy.PathOf(other))));
            }

            // A reference that waits for a suggestion to be confirmed says so, with a button for it below the list
            var pending = new List<Transform>();
            var written = plan.Components.Where(c => c.WillWrite).ToList();
            foreach (var planned in written)
            {
                // The components of other objects are named with their object
                string component = planned.Entry.Host == copy.Source
                    ? planned.Entry.Type.Name
                    : $"{planned.Entry.Host.name}/{planned.Entry.Type.Name}";
                foreach (var reference in planned.References.Where(r => r.Kind == ReferenceKind.InternalUnresolved))
                {
                    string property = ReferenceWalker.DisplayName(reference.DisplayPath);
                    var waitsFor = copy.PendingSuggestionAt(reference.SourceValue);
                    if (waitsFor != null)
                    {
                        body.Add(WarningLabel(Localization.S("componentCopier.otherSide.unresolvedPending",
                            component, property, waitsFor.name)));
                        // The parent that blocks the counterpart has its button already
                        if (!pending.Contains(waitsFor) && waitsFor != copy.BlockingParent) pending.Add(waitsFor);
                        continue;
                    }

                    body.Add(WarningLabel(Localization.S("componentCopier.otherSide.unresolved", component, property,
                        reference.SourceValue != null ? reference.SourceValue.name : CopyVerifier.NoneText)));
                }
            }

            foreach (var waitsFor in pending)
                AddConfirmSuggestionButton(waitsFor);

            int axisDependent = written.Count(c => c.AxisDependentProperties.Count > 0);
            if (axisDependent > 0)
                body.Add(WarningLabel(Localization.S("componentCopier.report.axisDependent", axisDependent)));

            if (!copy.HasWork && copy.BlockReason == BlockReason.None)
            {
                // Something was asked for, and the other side has it already
                bool asked = plan.Poses.Count > 0 || plan.Components.Count > 0;
                body.Add(InfoLabel(Localization.S(asked
                    ? "componentCopier.otherSide.upToDate"
                    : "componentCopier.otherSide.nothingSelected")));
            }

            if (message != null) body.Add(WarningLabel(message));
        }

        #endregion

        #region Helpers

        /// <summary>The counterpart's field is labeled with the direction: "Other side (L → R)".</summary>
        private static string OtherSideLabel(Side side)
        {
            return Localization.S("componentCopier.mirror.otherSide", Localization.S(side == Side.Left
                ? "componentCopier.mirror.leftToRight"
                : "componentCopier.mirror.rightToLeft"));
        }

        private static string ActionName(ComponentAction action) =>
            Localization.S(ComponentCopierStrings.ActionKey(action));

        /// <summary>Only a blocked row and an identical one need explaining; a blocked one says it next to the counterpart too.</summary>
        private string ChipTooltip(ComponentAction action)
        {
            switch (action)
            {
                case ComponentAction.Blocked:
                    return BlockMessage();
                case ComponentAction.SkipIdentical:
                    return Localization.S(ComponentCopierStrings.ActionTooltipKey(action));
                default:
                    return "";
            }
        }

        private static Label StatusChip(ComponentAction action, string text, string tooltip)
        {
            var chip = new Label(text) { tooltip = tooltip };
            chip.AddToClassList("status-chip");
            chip.AddToClassList(ComponentCopierStrings.StatusChipClass(action));
            return chip;
        }

        private static Label NoteLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("other-side__note");
            return label;
        }

        private static Label WarningLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("inline-warning");
            return label;
        }

        private static Label InfoLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("info-label");
            return label;
        }

        #endregion
    }
}
