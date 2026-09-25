using System;
using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    public partial class ComponentCopierWindow
    {
        // Snapshot shown after "Diff check only" or Apply. Unlike the live preview it is not recomputed,
        // so the verification of an Apply stays on screen while the preview moves on. An Apply that changed
        // nothing (the plan was out of date, or applying failed) leaves a message without a report.
        private DiffReport detailReport;
        private string detailTitleKey;
        private string detailMessage;

        private const int MaxIssueRows = 20;
        private const int MaxIssueLines = 50;

        // Pre-check rows the user opened. The report is rebuilt on every scene edit, and fixing a mapping
        // is such an edit; without this the rows would close while they are being worked on.
        private readonly HashSet<(string kind, ComponentKey key)> expandedIssues = new();

        private void CreateReportSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            parent.Add(section);

            var title = new Label("componentCopier.report");
            title.AddToClassList("section-title");
            title.AddToClassList("ndmf-tr");
            section.Add(title);

            reportContainer = new VisualElement();
            reportContainer.AddToClassList("report");
            section.Add(reportContainer);
        }

        private void ClearDetailReport()
        {
            detailReport = null;
            detailTitleKey = null;
            detailMessage = null;
        }

        #region Actions

        private void Apply()
        {
            if (session.Plan == null || session.IsTargetAsset) return;

            // A scene change that the refresh has not picked up yet may have touched the plan on screen, so it is
            // planned again first. A plan that comes out the same is the one the user has seen, and is applied on this
            // click. One that changed is shown instead of applied: it may also have selected components that have
            // just appeared. Tools that change the scene all the time (a clip previewed in the Animation window, ...)
            // keep a refresh pending, so waiting for none would never apply.
            if (refreshScheduled)
            {
                CancelScheduledRefresh();
                string shown = session.Plan.Fingerprint();
                Rescan();
                if (session.Plan == null || session.Plan.Fingerprint() != shown)
                {
                    ShowNotApplied(Localization.S("componentCopier.report.stale"));
                    return;
                }
            }

            var executedPlan = session.Plan;
            ExecutionResult result;
            try
            {
                result = CopyExecutor.Execute(executedPlan);
            }
            catch (Exception exception)
            {
                // The executor has put the target back already
                Debug.LogException(exception);
                Rescan();
                ShowNotApplied(Localization.S("componentCopier.report.applyFailed", exception.Message));
                return;
            }

            // Something the plan needs was deleted since it was made, and nothing was changed
            if (result.Stale)
            {
                Debug.Log("[Component Copier] Nothing was applied because the plan was out of date: " +
                          $"{result.StaleReason}.");
                Rescan();
                ShowNotApplied(Localization.S("componentCopier.report.stale"));
                return;
            }

            detailReport = CopyVerifier.Verify(executedPlan);
            detailTitleKey = "componentCopier.report.afterApply";
            detailMessage = Localization.S("componentCopier.report.applied",
                result.WrittenComponents, result.CreatedObjects, result.RemovedComponents);
            if (result.InstantiatedPrefabs > 0)
            {
                detailMessage += "\n" +
                    Localization.S("componentCopier.report.appliedPrefabs", result.InstantiatedPrefabs);
            }

            if (result.LeftOutComponents + result.LeftOutObjects > 0)
            {
                detailMessage += "\n" + Localization.S("componentCopier.report.appliedLeftOut",
                    result.LeftOutComponents, result.LeftOutObjects);
            }

            if (result.Failed.Count > 0)
                detailMessage += "\n" + Localization.S("componentCopier.report.failed", result.Failed.Count);
            if (result.FailedRemovals.Count > 0)
            {
                detailMessage += "\n" +
                    Localization.S("componentCopier.report.removeFailed", result.FailedRemovals.Count);
            }

            if (result.NotRemoved.Count > 0)
                detailMessage += "\n" + Localization.S("componentCopier.report.notRemoved", result.NotRemoved.Count);

            string targetName = session.MirrorMode ? "the other side" : session.TargetRoot.name;
            Debug.Log($"[Component Copier] Copied {result.WrittenComponents} component(s) " +
                      $"from '{session.SourceRoot.name}' to '{targetName}'.");

            Rescan();
        }

        /// <summary>
        /// Says why an Apply changed nothing, in place of its report. The plan on screen has been made again for the
        /// scene as it is now, and is left for the user to check and apply.
        /// </summary>
        private void ShowNotApplied(string message)
        {
            detailReport = null;
            detailTitleKey = "componentCopier.apply";
            detailMessage = message;
            RenderReport();
        }

        private void RunDiffCheck()
        {
            var diffPlan = session.BuildDiffCheckPlan();
            if (diffPlan == null) return;

            detailReport = CopyVerifier.Verify(diffPlan);
            detailTitleKey = "componentCopier.report.diffCheck";
            detailMessage = null;
            RenderReport();
        }

        private void AddMissingDependencies(List<ComponentKey> keys)
        {
            session.SelectDependencies(keys);
            RenderAll();
        }

        #endregion

        #region Rendering

        private void RenderReport()
        {
            reportContainer.Clear();

            if (session.Plan == null)
            {
                // A mirror copy needs nothing but the source
                reportContainer.Add(InfoLabel(session.MirrorMode
                    ? "componentCopier.info.selectSource"
                    : "componentCopier.info.noPlan"));
                return;
            }

            RenderPreCheck();
            if (detailTitleKey != null) RenderDetailReport();
        }

        private void RenderPreCheck()
        {
            var plan = session.Plan;

            // Components that arrive with a prefab are reported on the prefab line, like their label in the list
            int Count(ComponentAction action) =>
                plan.Components.Count(c => c.Action == action && !c.ArrivesWithPrefab);

            // Same number as "to be created" in the mapping section: every object that appears in the target,
            // an added prefab counting as one. The prefab line below only says how some of them arrive.
            // "0 objects to create" next to "1 prefab is added" would contradict itself.
            int objectsToCreate = plan.ObjectsToCreate.Count(o => o.PrefabRoot == null);
            int prefabs = plan.ObjectsToCreate.Count(o => o.IsPrefabRoot && o.PrefabRoot == null);

            // "Identical" is counted apart from "Skip": lumped together, a target that is already up to date
            // looks as if the Overwrite policy had been ignored
            // Components held back by the unresolved-reference setting are skips too, as far as the user is concerned
            int skipped = Count(ComponentAction.Skip) + plan.Components.Count(c => c.IsHeldBack);
            var summary = new Label(Localization.S("componentCopier.report.summary",
                Count(ComponentAction.Add), Count(ComponentAction.Overwrite), Count(ComponentAction.Replace),
                skipped, Count(ComponentAction.SkipIdentical), objectsToCreate));
            summary.AddToClassList("report-summary");
            reportContainer.Add(summary);

            int mirroredValues = plan.Mirror != null ? plan.Components.Where(c => c.WillWrite).Sum(c => c.Values.Count) : 0;
            if (mirroredValues > 0)
            {
                var mirrorLabel = new Label(Localization.S("componentCopier.report.mirror",
                    mirroredValues, plan.Mirror.Root.name));
                mirrorLabel.AddToClassList("report-summary");
                reportContainer.Add(mirrorLabel);
            }

            if (prefabs > 0)
            {
                // A prefab without components (a mesh-only hat with its renderers left out, ...) gets the short
                // form; "0 components are copied" would read like something went wrong
                int prefabComponents = plan.Components.Count(c => c.WillWrite && c.ArrivesWithPrefab);
                var prefabLabel = new Label(prefabComponents > 0
                    ? Localization.S("componentCopier.report.prefabs", prefabs, prefabComponents)
                    : Localization.S("componentCopier.report.prefabsOnly", prefabs));
                prefabLabel.AddToClassList("report-summary");
                reportContainer.Add(prefabLabel);
            }

            // Held-back components inside an added prefab are removed the same way, but the warning about held-back
            // components below counts them already
            int leftOutComponents = plan.Components.Count(c => c.LeftOut && !c.IsHeldBack);
            if (leftOutComponents > 0)
            {
                var leftOutLabel = new Label(Localization.S("componentCopier.report.leftOut",
                    leftOutComponents, plan.ObjectsToCreate.Count(o => o.LeftOut)));
                leftOutLabel.AddToClassList("report-summary");
                reportContainer.Add(leftOutLabel);
            }

            var (redirectedObjects, redirectedPlaces) = CountExternalReferences(ReferenceKind.ExternalMapped);
            if (redirectedPlaces > 0)
            {
                // The avatar is named when there is one. Replacements picked by hand also work without a map
                // of the surroundings (plan.ExternalMap is null then), and those can point anywhere.
                var redirectedLabel = new Label(plan.ExternalMap != null
                    ? Localization.S("componentCopier.report.externalMappedTo",
                        redirectedObjects, redirectedPlaces, plan.ExternalMap.TargetRoot.name)
                    : Localization.S("componentCopier.report.externalMapped", redirectedObjects, redirectedPlaces));
                redirectedLabel.AddToClassList("report-summary");
                reportContainer.Add(redirectedLabel);
            }

            if (session.IsTargetAsset)
            {
                AddWarning(session.MirrorMode
                    ? "componentCopier.warning.mirrorSourceIsAsset"
                    : "componentCopier.warning.targetIsAsset");
            }

            var blocked = plan.Components
                .Where(c => c.Action == ComponentAction.Blocked && !c.Implicit && !c.IsHeldBack)
                .ToList();
            if (blocked.Count > 0)
            {
                AddWarning("componentCopier.report.blocked", blocked.Count);
                AddIssueRows(blocked, CreateBlockedRow);
            }

            var available = new HashSet<ComponentKey>(session.Entries.Select(e => e.Key));
            string DescribeUnresolved(PlannedReference reference) =>
                $"{DescribeReference(reference.SourceValue)} → {CopyVerifier.NoneText}" +
                $" · {UnresolvedCause(reference, available)}";
            // Nothing is written for a held-back component, so there is no "cleared" value to show
            string DescribeHeldBack(PlannedReference reference) =>
                DescribeReference(reference.SourceValue) + $" · {UnresolvedCause(reference, available)}";

            // Held back as the setting says. Nothing of them is written, so their references are not cleared
            // and are listed apart from the ones below.
            var heldBack = plan.Components.Where(c => c.IsHeldBack).ToList();
            if (heldBack.Count > 0)
            {
                var warning = AddWarning("componentCopier.report.heldBack", heldBack.Count);
                AddDependencyButton(warning, heldBack.SelectMany(c => c.UnresolvedReferences), available);
                AddIssueRows(heldBack, planned => CreateHeldBackRow(planned, DescribeHeldBack));
            }

            // Only what gets written: a component that is skipped or already identical clears nothing
            var unresolved = plan.Components
                .Where(c => c.WillWrite)
                .SelectMany(c => c.References)
                .Where(r => r.Kind == ReferenceKind.InternalUnresolved)
                .ToList();
            if (unresolved.Count > 0)
            {
                var warning = AddWarning("componentCopier.report.unresolved", unresolved.Count);
                AddDependencyButton(warning, unresolved, available);
                AddIssueRows(WithReferences(ReferenceKind.InternalUnresolved), planned => CreateReferenceIssueRow(
                    planned, ReferenceKind.InternalUnresolved,
                    ComponentCopierStrings.DiffKindKey(DiffKind.UnresolvedReference), DescribeUnresolved));
            }

            // Mirrored as far as the rules go; the rest depends on the rig and is left to the user
            var axisDependent = plan.Components
                .Where(c => c.WillWrite && c.AxisDependentProperties.Count > 0)
                .ToList();
            if (axisDependent.Count > 0)
            {
                AddWarning("componentCopier.report.axisDependent", axisDependent.Count);
                AddIssueRows(axisDependent, CreateAxisDependentRow);
            }

            var (keptObjects, keptPlaces) = CountExternalReferences(ReferenceKind.ExternalScene);
            if (keptPlaces > 0)
            {
                AddWarning("componentCopier.report.external", keptObjects, keptPlaces);
                AddIssueRows(WithReferences(ReferenceKind.ExternalScene), planned => CreateReferenceIssueRow(
                    planned, ReferenceKind.ExternalScene, "componentCopier.report.kind.externalKept",
                    reference => DescribeReference(reference.SourceValue)));
            }

            // Replace cannot remove what another component requires. A copy overwrites such a component in place;
            // the ones beyond the copies stay as they are, and show up as extra after applying.
            if (plan.KeptComponents.Count > 0)
            {
                AddWarning("componentCopier.report.kept", plan.KeptComponents.Count);
                var surplus = plan.KeptComponents.Where(k => k.OverwrittenBy == null && k.Component != null).ToList();
                if (surplus.Count > 0) AddIssueRows(surplus, CreateKeptRow);
            }

            foreach (var broken in plan.BrokenReferences.Take(10))
            {
                if (broken.Holder == null) continue;
                var warning = AddWarning("componentCopier.report.brokenReference",
                    broken.Holder.name, broken.Holder.GetType().Name, broken.PropertyPath);
                var holder = broken.Holder;
                warning.RegisterCallback<ClickEvent>(_ => Reveal(holder));
            }

            if (plan.Components.Any(c => c.Entry.Type.FullName == ComponentScanner.PipelineManagerTypeName))
                AddWarning("componentCopier.warning.pipelineManager");
        }

        /// <summary>
        /// Counts references to the outside by the object they point at, like the rows of the mapping section,
        /// and by place. A single Blendshape Sync refers to the body mesh once per binding: "25 references"
        /// next to one row for that mesh looks like a miscount.
        /// </summary>
        private (int objects, int places) CountExternalReferences(ReferenceKind kind)
        {
            var references = session.Plan.Components
                .Where(c => c.WillWrite)
                .SelectMany(c => c.References)
                .Where(r => r.Kind == kind)
                .ToList();
            int objects = references
                .Select(r => ReferenceWalker.GetTransform(r.SourceValue))
                .Where(t => t != null)
                .Distinct()
                .Count();
            return (objects, references.Count);
        }

        private List<PlannedComponent> WithReferences(ReferenceKind kind)
        {
            return session.Plan.Components.Where(c => c.WillWrite && c.References.Any(r => r.Kind == kind)).ToList();
        }

        /// <summary>
        /// Lists the components a warning is about, in the same form as the rows of the detail report:
        /// a count alone does not tell where to look.
        /// </summary>
        private void AddIssueRows<T>(List<T> components, Func<T, VisualElement> createRow)
        {
            var container = new VisualElement();
            container.AddToClassList("warning-details");
            reportContainer.Add(container);

            foreach (var component in components.Take(MaxIssueRows))
                container.Add(createRow(component));

            if (components.Count > MaxIssueRows)
                container.Add(MoreLabel(components.Count - MaxIssueRows));
        }

        private VisualElement CreateBlockedRow(PlannedComponent planned)
        {
            var foldout = CreateIssueFoldout(
                planned, "blocked", ComponentCopierStrings.ActionKey(ComponentAction.Blocked));

            var reason = new Label(Localization.S(ComponentCopierStrings.BlockReasonKey(planned.BlockReason)));
            reason.AddToClassList("diff-property");
            foldout.Add(reason);

            AddSelectSourceButton(foldout, planned);
            return foldout;
        }

        /// <summary>
        /// Offers to select the components that references fail on only because they are left unselected.
        /// </summary>
        private void AddDependencyButton(
            VisualElement warning, IEnumerable<PlannedReference> references, HashSet<ComponentKey> available)
        {
            var addable = references
                .Where(r => r.MissingDependency.HasValue && available.Contains(r.MissingDependency.Value))
                .Select(r => r.MissingDependency.Value)
                // A selected one that is held back or blocked cannot be fixed by selecting it
                .Where(key => !session.IsSelected(key))
                .Distinct()
                .ToList();
            if (addable.Count == 0) return;

            var addButton = new Button(() => AddMissingDependencies(addable))
            {
                text = Localization.S("componentCopier.report.addDependencies", addable.Count),
            };
            addButton.AddToClassList("warning-action");
            warning.Add(addButton);
        }

        /// <summary>
        /// Shown as an unresolved-reference row, like it would be without the setting, so that switching the
        /// setting does not make the problem disappear; the reason line says that the component is skipped.
        /// </summary>
        private VisualElement CreateHeldBackRow(PlannedComponent planned, Func<PlannedReference, string> describe)
        {
            var foldout = CreateIssueFoldout(
                planned, "heldBack", ComponentCopierStrings.DiffKindKey(DiffKind.UnresolvedReference));

            var reason = new Label(Localization.S(ComponentCopierStrings.BlockReasonKey(planned.BlockReason)));
            reason.AddToClassList("diff-property");
            foldout.Add(reason);

            AddReferenceLines(foldout, planned.UnresolvedReferences, describe);
            AddSelectSourceButton(foldout, planned);
            return foldout;
        }

        /// <summary>Lists the values a mirror copy left as they are, for the user to check on the other side.</summary>
        private VisualElement CreateAxisDependentRow(PlannedComponent planned)
        {
            var foldout = CreateIssueFoldout(planned, "axisDependent", "componentCopier.report.kind.axisDependent");

            foreach (var propertyPath in planned.AxisDependentProperties.Take(MaxIssueLines))
            {
                var line = new Label(ReferenceWalker.DisplayName(propertyPath));
                line.AddToClassList("diff-property");
                foldout.Add(line);
            }

            if (planned.AxisDependentProperties.Count > MaxIssueLines)
                foldout.Add(MoreLabel(planned.AxisDependentProperties.Count - MaxIssueLines));

            AddSelectSourceButton(foldout, planned);
            return foldout;
        }

        /// <summary>
        /// A component that Replace leaves as it is. Unlike the other rows, it is a target component that exists
        /// already, so it is shown by its target path and selected there.
        /// </summary>
        private VisualElement CreateKeptRow(KeptComponent kept)
        {
            var component = kept.Component;
            // Named as the diff check will report it after applying
            string kind = Localization.S(ComponentCopierStrings.DiffKindKey(DiffKind.ExtraOnTarget));
            var foldout = CreateReportFoldout(
                kind, TargetPath(component.transform), component.GetType().Name, "diff-row--warning");

            var reason = new Label(Localization.S("componentCopier.report.kept.requiredBy", kept.RequiredBy));
            reason.AddToClassList("diff-property");
            foldout.Add(reason);

            AddSelectButton(foldout, component);
            return foldout;
        }

        private VisualElement CreateReferenceIssueRow(
            PlannedComponent planned, ReferenceKind kind, string kindKey, Func<PlannedReference, string> describe)
        {
            var foldout = CreateIssueFoldout(planned, kind.ToString().ToLowerInvariant(), kindKey);
            AddReferenceLines(foldout, planned.References.Where(r => r.Kind == kind).ToList(), describe);
            AddSelectSourceButton(foldout, planned);
            return foldout;
        }

        private static void AddReferenceLines(
            Foldout foldout, List<PlannedReference> references, Func<PlannedReference, string> describe)
        {
            foreach (var reference in references.Take(MaxIssueLines))
            {
                string name = ReferenceWalker.DisplayName(reference.DisplayPath);
                var line = new Label($"{name}: {describe(reference)}");
                line.AddToClassList("diff-property");
                line.AddToClassList("diff-property--link");
                var referenced = reference.SourceValue;
                line.RegisterCallback<ClickEvent>(_ => Reveal(referenced));
                foldout.Add(line);
            }

            if (references.Count > MaxIssueLines)
                foldout.Add(MoreLabel(references.Count - MaxIssueLines));
        }

        private Foldout CreateIssueFoldout(PlannedComponent planned, string kind, string kindKey)
        {
            var id = (kind, planned.Entry.Key);
            var foldout = CreateReportFoldout(
                Localization.S(kindKey), planned.Entry.Key.RelativePath, planned.Entry.Type.Name, "diff-row--warning");
            foldout.value = expandedIssues.Contains(id);
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout) return;
                if (evt.newValue) expandedIssues.Add(id);
                else expandedIssues.Remove(id);
            });
            return foldout;
        }

        private static Foldout CreateReportFoldout(string kindText, string path, string typeName, string rowClass)
        {
            if (string.IsNullOrEmpty(path)) path = "/";

            var foldout = new Foldout { text = $"[{kindText}] {path} — {typeName}", value = false };
            foldout.AddToClassList("diff-row");
            foldout.AddToClassList(rowClass);
            return foldout;
        }

        /// <summary>Nothing exists in the target before applying, so the pre-check points at the source.</summary>
        private static void AddSelectSourceButton(Foldout foldout, PlannedComponent planned)
        {
            AddSelectButton(foldout, planned.Entry.Component);
        }

        private static void AddSelectButton(Foldout foldout, UnityEngine.Object target)
        {
            var actions = new VisualElement();
            actions.AddToClassList("diff-actions");
            foldout.Add(actions);

            actions.Add(new Button(() => Reveal(target)) { text = Localization.S("componentCopier.diff.select") });
        }

        private static Label MoreLabel(int count)
        {
            var label = new Label(Localization.S("componentCopier.report.more", count));
            label.AddToClassList("diff-property");
            return label;
        }

        /// <summary>
        /// Objects of the source are shown by path, like the rows of the mapping section where the missing
        /// counterpart gets fixed.
        /// </summary>
        private string DescribeReference(UnityEngine.Object value)
        {
            if (value == null) return CopyVerifier.NoneText;

            string name = value.name;
            var transform = ReferenceWalker.GetTransform(value);
            if (session.SourceRoot != null && transform != session.SourceRoot.transform &&
                Hierarchy.IsInside(transform, session.SourceRoot.transform))
            {
                name = ObjectMatcher.GetRelativePathFromRoot(transform, session.SourceRoot.transform);
            }

            return value is GameObject || value is Transform ? name : $"{name} ({value.GetType().Name})";
        }

        private string UnresolvedCause(PlannedReference reference, HashSet<ComponentKey> available)
        {
            if (reference.MissingDependency is { } dependency)
            {
                if (session.TryGetPlanned(dependency, out var referenced) && referenced.IsHeldBack)
                    return Localization.S("componentCopier.report.cause.heldBack");

                if (available.Contains(dependency) && !session.IsSelected(dependency))
                    return Localization.S("componentCopier.report.cause.notSelected");
            }

            var mapping = session.Map?.Get(ReferenceWalker.GetTransform(reference.SourceValue));
            return mapping != null && mapping.State == MappingState.NeedsReview
                ? Localization.S("componentCopier.report.cause.needsReview")
                : Localization.S("componentCopier.report.cause.noCounterpart");
        }

        private VisualElement AddWarning(string key, params object[] args)
        {
            var row = new VisualElement();
            row.AddToClassList("warning-row");

            var label = new Label("⚠ " + Localization.S(key, args));
            label.AddToClassList("warning-text");
            row.Add(label);

            reportContainer.Add(row);
            return row;
        }

        private void RenderDetailReport()
        {
            var box = new VisualElement();
            box.AddToClassList("detail-report");
            reportContainer.Add(box);

            var headerRow = new VisualElement();
            headerRow.AddToClassList("detail-header");
            box.Add(headerRow);

            var title = new Label(Localization.S(detailTitleKey));
            title.AddToClassList("detail-title");
            headerRow.Add(title);

            var closeButton = new Button(() =>
            {
                ClearDetailReport();
                RenderReport();
            }) { text = "×" };
            closeButton.AddToClassList("detail-close");
            headerRow.Add(closeButton);

            if (!string.IsNullOrEmpty(detailMessage))
            {
                var message = new Label(detailMessage);
                // Without a report, the message says why nothing was applied
                message.AddToClassList(detailReport != null ? "detail-message" : "warning-text");
                box.Add(message);
            }

            if (detailReport == null) return;

            var counts = new Label(Localization.S("componentCopier.report.diffSummary",
                detailReport.Count(DiffKind.Match),
                detailReport.Count(DiffKind.ValueMismatch) + detailReport.Count(DiffKind.ReferenceMismatch),
                detailReport.Count(DiffKind.OutsideReference),
                detailReport.Count(DiffKind.UnresolvedReference),
                detailReport.Count(DiffKind.MissingOnTarget),
                detailReport.Count(DiffKind.ExtraOnTarget)));
            counts.AddToClassList("detail-counts");
            box.Add(counts);

            // Said in the open: such a reference looks fine in the Inspector, and a component can have one while it
            // is counted as different
            int outsidePlaces = detailReport.Components
                .Sum(c => c.Properties.Count(p => p.Kind == DiffKind.OutsideReference));
            if (outsidePlaces > 0)
            {
                var warning = new Label("⚠ " + Localization.S("componentCopier.report.outsideReferences", outsidePlaces));
                warning.AddToClassList("warning-text");
                box.Add(warning);
            }

            foreach (var diff in detailReport.Components.Where(d => d.Kind != DiffKind.Match))
                box.Add(CreateDiffRow(diff));
        }

        /// <summary>
        /// The path of a target object, read like the source paths of the other rows: from the counterpart of the
        /// source. The other side of a mirror copy can lie outside of it, and is read from the avatar then.
        /// </summary>
        private string TargetPath(Transform transform)
        {
            var map = session.Map;
            if (map == null) return "";

            var source = session.SourceRoot;
            var from = source != null && map.TryResolve(source.transform, out var counterpart) &&
                       Hierarchy.IsInside(transform, counterpart)
                ? counterpart
                : map.TargetRoot;
            string path = ObjectMatcher.GetRelativePathFromRoot(transform, from);
            return path.Length > 0 ? path : transform.name;
        }

        private VisualElement CreateDiffRow(ComponentDiff diff)
        {
            string typeName = diff.Planned != null
                ? diff.Planned.Entry.Type.Name
                : diff.Actual != null ? diff.Actual.GetType().Name : "?";
            string path = diff.Planned != null
                ? diff.Planned.Entry.Key.RelativePath
                : diff.Actual != null ? TargetPath(diff.Actual.transform) : "";

            var foldout = CreateReportFoldout(Localization.S(ComponentCopierStrings.DiffKindKey(diff.Kind)),
                path, typeName, ComponentCopierStrings.DiffRowClass(diff.Kind));

            foreach (var property in diff.Properties)
            {
                foldout.Add(property.Kind == DiffKind.OutsideReference
                    ? CreateOutsideReferenceLine(property)
                    : CreatePropertyLine(property));
            }

            if (diff.Truncated)
                foldout.Add(new Label(Localization.S("componentCopier.diff.truncated")));

            var actual = diff.Actual;
            if (actual != null)
            {
                var actions = new VisualElement();
                actions.AddToClassList("diff-actions");
                foldout.Add(actions);

                var selectButton = new Button(() =>
                {
                    Reveal(actual);
                }) { text = Localization.S("componentCopier.diff.select") };
                actions.Add(selectButton);

                if (diff.Kind == DiffKind.ExtraOnTarget && !EditorUtility.IsPersistent(actual))
                {
                    var removeButton = new Button(() =>
                    {
                        if (actual != null) Undo.DestroyObjectImmediate(actual);
                        detailReport.Components.Remove(diff);
                        RenderReport();
                    }) { text = Localization.S("componentCopier.diff.remove") };
                    actions.Add(removeButton);
                }
            }

            return foldout;
        }

        private static Label CreatePropertyLine(PropertyDiff property)
        {
            var line = new Label($"{property.DisplayName}: {property.Expected} → {property.Actual}");
            line.AddToClassList("diff-property");
            line.AddToClassList(ComponentCopierStrings.DiffPropertyClass(property.Kind));
            return line;
        }

        /// <summary>
        /// Names where the object lies, since its name alone reads like the counterpart that was meant ("Hips" is
        /// on every avatar), and reveals it on a click.
        /// </summary>
        private static Label CreateOutsideReferenceLine(PropertyDiff property)
        {
            string place = Localization.S("componentCopier.diff.outsideReference.in", PlaceOf(property.Referenced));
            var line = new Label($"{property.DisplayName}: {property.Actual} · {place}");
            line.AddToClassList("diff-property");
            line.AddToClassList(ComponentCopierStrings.DiffPropertyClass(property.Kind));
            line.AddToClassList("diff-property--link");
            var referenced = property.Referenced;
            line.RegisterCallback<ClickEvent>(_ => Reveal(referenced));
            return line;
        }

        /// <summary>The avatar or hierarchy an object belongs to, or the asset it is part of.</summary>
        private static string PlaceOf(UnityEngine.Object value)
        {
            if (value == null) return "-";
            if (EditorUtility.IsPersistent(value)) return AssetDatabase.GetAssetPath(value);

            var transform = ReferenceWalker.GetTransform(value);
            var avatar = AvatarRoots.Find(transform);
            return (avatar != null ? avatar : transform.root).name;
        }

        #endregion
    }
}
