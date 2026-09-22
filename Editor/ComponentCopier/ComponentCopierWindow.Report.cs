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
        // so the verification of an Apply stays on screen while the preview moves on.
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
            if (plan == null || IsTargetAsset()) return;

            var executedPlan = plan;
            var result = CopyExecutor.Execute(executedPlan);

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

            Debug.Log($"[Component Copier] Copied {result.WrittenComponents} component(s) " +
                      $"from '{sourceRoot.name}' to '{targetRoot.name}'.");

            Rescan(resetSelection: false);
        }

        /// <summary>
        /// Compares against the existing components regardless of the configured policy: with Add or Replace
        /// the plan has no existing counterparts to compare with.
        /// </summary>
        private void RunDiffCheck()
        {
            if (map == null) return;

            var diffSettings = new CopySettings
            {
                ExistingPolicy = ExistingComponentPolicy.Overwrite,
                CreateMissingObjects = settings.CreateMissingObjects,
                RedirectExternalReferences = settings.RedirectExternalReferences,
                UnresolvedPolicy = settings.UnresolvedPolicy,
            };
            var diffPlan = BuildPlan(diffSettings);

            detailReport = CopyVerifier.Verify(diffPlan);
            detailTitleKey = "componentCopier.report.diffCheck";
            detailMessage = null;
            RenderReport();
        }

        private void AddMissingDependencies(List<ComponentKey> keys)
        {
            foreach (var key in keys)
            {
                selectedKeys.Add(key);
                leftOutKeys.Remove(key);
            }

            Recompute();
        }

        #endregion

        #region Rendering

        private void RenderReport()
        {
            reportContainer.Clear();

            if (plan == null)
            {
                reportContainer.Add(InfoLabel("componentCopier.info.noPlan"));
                return;
            }

            RenderPreCheck();
            if (detailReport != null) RenderDetailReport();
        }

        private void RenderPreCheck()
        {
            // Components that arrive with a prefab are reported on the prefab line, like their label in the list
            int Count(ComponentAction action) =>
                plan.Components.Count(c => c.Action == action && !ArrivesWithPrefab(c));

            // Same number as "to be created" in the mapping section: every object that appears in the target,
            // an added prefab counting as one. The prefab line below only says how some of them arrive.
            // "0 objects to create" next to "1 prefab is added" would contradict itself.
            int objectsToCreate = plan.ObjectsToCreate.Count(o => o.PrefabRoot == null);
            int prefabs = plan.ObjectsToCreate.Count(o => o.IsPrefabRoot && o.PrefabRoot == null);

            // "Identical" is counted apart from "Skip": lumped together, a target that is already up to date
            // looks as if the Overwrite policy had been ignored
            // Components held back by the unresolved-reference setting are skips too, as far as the user is concerned
            int skipped = Count(ComponentAction.Skip) +
                          plan.Components.Count(c => c.BlockReason == BlockReason.UnresolvedReference);
            var summary = new Label(Localization.S("componentCopier.report.summary",
                Count(ComponentAction.Add), Count(ComponentAction.Overwrite), Count(ComponentAction.Replace),
                skipped, Count(ComponentAction.SkipIdentical), objectsToCreate));
            summary.AddToClassList("report-summary");
            reportContainer.Add(summary);

            if (prefabs > 0)
            {
                // A prefab without components (a mesh-only hat with its renderers left out, ...) gets the short
                // form; "0 components are copied" would read like something went wrong
                int prefabComponents = plan.Components.Count(c => c.WillWrite && ArrivesWithPrefab(c));
                var prefabLabel = new Label(prefabComponents > 0
                    ? Localization.S("componentCopier.report.prefabs", prefabs, prefabComponents)
                    : Localization.S("componentCopier.report.prefabsOnly", prefabs));
                prefabLabel.AddToClassList("report-summary");
                reportContainer.Add(prefabLabel);
            }

            int leftOutComponents = plan.Components.Count(c => c.LeftOut);
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

            if (IsTargetAsset()) AddWarning("componentCopier.warning.targetIsAsset");

            var blocked = plan.Components
                .Where(c => c.Action == ComponentAction.Blocked && !c.Implicit &&
                            c.BlockReason != BlockReason.UnresolvedReference)
                .ToList();
            if (blocked.Count > 0)
            {
                AddWarning("componentCopier.report.blocked", blocked.Count);
                AddIssueRows(blocked, CreateBlockedRow);
            }

            var available = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            string DescribeUnresolved(PlannedReference reference) =>
                $"{DescribeReference(reference.SourceValue)} → {CopyVerifier.NoneText}" +
                $" · {UnresolvedCause(reference, available)}";
            // Nothing is written for a held-back component, so there is no "cleared" value to show
            string DescribeHeldBack(PlannedReference reference) =>
                DescribeReference(reference.SourceValue) + $" · {UnresolvedCause(reference, available)}";

            // Held back as the setting says. Nothing of them is written, so their references are not cleared
            // and are listed apart from the ones below.
            var heldBack = plan.Components.Where(c => c.BlockReason == BlockReason.UnresolvedReference).ToList();
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
                    planned, ReferenceKind.InternalUnresolved, "componentCopier.diff.unresolvedReference",
                    DescribeUnresolved));
            }

            var (keptObjects, keptPlaces) = CountExternalReferences(ReferenceKind.ExternalScene);
            if (keptPlaces > 0)
            {
                AddWarning("componentCopier.report.external", keptObjects, keptPlaces);
                AddIssueRows(WithReferences(ReferenceKind.ExternalScene), planned => CreateReferenceIssueRow(
                    planned, ReferenceKind.ExternalScene, "componentCopier.report.kind.externalKept",
                    reference => DescribeReference(reference.SourceValue)));
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
            var references = plan.Components
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
            return plan.Components.Where(c => c.WillWrite && c.References.Any(r => r.Kind == kind)).ToList();
        }

        /// <summary>
        /// Lists the components a warning is about, in the same form as the rows of the detail report:
        /// a count alone does not tell where to look.
        /// </summary>
        private void AddIssueRows(List<PlannedComponent> components, Func<PlannedComponent, VisualElement> createRow)
        {
            var container = new VisualElement();
            container.AddToClassList("warning-details");
            reportContainer.Add(container);

            foreach (var planned in components.Take(MaxIssueRows))
                container.Add(createRow(planned));

            if (components.Count > MaxIssueRows)
                container.Add(MoreLabel(components.Count - MaxIssueRows));
        }

        private VisualElement CreateBlockedRow(PlannedComponent planned)
        {
            var foldout = CreateIssueFoldout(planned, "blocked", "componentCopier.action.blocked");

            var reason = new Label(Localization.S("componentCopier.blocked." + Camel(planned.BlockReason)));
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
                .Where(key => !selectedKeys.Contains(key))
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
            var foldout = CreateIssueFoldout(planned, "heldBack", "componentCopier.diff.unresolvedReference");

            var reason = new Label(Localization.S("componentCopier.blocked." + Camel(planned.BlockReason)));
            reason.AddToClassList("diff-property");
            foldout.Add(reason);

            AddReferenceLines(foldout, planned.UnresolvedReferences, describe);
            AddSelectSourceButton(foldout, planned);
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
                string name = reference.DisplayPath.Replace(".Array.data[", "[");
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
                Localization.S(kindKey), planned.Entry.Key.RelativePath, planned.Entry.Type.Name, "warning");
            foldout.value = expandedIssues.Contains(id);
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout) return;
                if (evt.newValue) expandedIssues.Add(id);
                else expandedIssues.Remove(id);
            });
            return foldout;
        }

        private static Foldout CreateReportFoldout(string kindText, string path, string typeName, string kindClass)
        {
            if (string.IsNullOrEmpty(path)) path = "/";

            var foldout = new Foldout { text = $"[{kindText}] {path} — {typeName}", value = false };
            foldout.AddToClassList("diff-row");
            foldout.AddToClassList("diff-row--" + kindClass);
            return foldout;
        }

        /// <summary>Nothing exists in the target before applying, so the pre-check points at the source.</summary>
        private static void AddSelectSourceButton(Foldout foldout, PlannedComponent planned)
        {
            var actions = new VisualElement();
            actions.AddToClassList("diff-actions");
            foldout.Add(actions);

            var source = planned.Entry.Component;
            actions.Add(new Button(() => Reveal(source)) { text = Localization.S("componentCopier.diff.select") });
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
            if (sourceRoot != null && transform != sourceRoot.transform &&
                ReferenceWalker.IsInside(transform, sourceRoot.transform))
            {
                name = ObjectMatcher.GetRelativePathFromRoot(transform, sourceRoot.transform);
            }

            return value is GameObject || value is Transform ? name : $"{name} ({value.GetType().Name})";
        }

        private string UnresolvedCause(PlannedReference reference, HashSet<ComponentKey> available)
        {
            if (reference.MissingDependency is { } dependency)
            {
                if (plannedByKey.TryGetValue(dependency, out var referenced) &&
                    referenced.BlockReason == BlockReason.UnresolvedReference)
                {
                    return Localization.S("componentCopier.report.cause.heldBack");
                }

                if (available.Contains(dependency) && !selectedKeys.Contains(dependency))
                    return Localization.S("componentCopier.report.cause.notSelected");
            }

            var mapping = map?.Get(ReferenceWalker.GetTransform(reference.SourceValue));
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
                message.AddToClassList("detail-message");
                box.Add(message);
            }

            var counts = new Label(Localization.S("componentCopier.report.diffSummary",
                detailReport.Count(DiffKind.Match),
                detailReport.Count(DiffKind.ValueMismatch) + detailReport.Count(DiffKind.ReferenceMismatch),
                detailReport.Count(DiffKind.UnresolvedReference),
                detailReport.Count(DiffKind.MissingOnTarget),
                detailReport.Count(DiffKind.ExtraOnTarget)));
            counts.AddToClassList("detail-counts");
            box.Add(counts);

            foreach (var diff in detailReport.Components.Where(d => d.Kind != DiffKind.Match))
                box.Add(CreateDiffRow(diff));
        }

        private VisualElement CreateDiffRow(ComponentDiff diff)
        {
            string typeName = diff.Planned != null
                ? diff.Planned.Entry.Type.Name
                : diff.Actual != null ? diff.Actual.GetType().Name : "?";
            string path = diff.Planned != null
                ? diff.Planned.Entry.Key.RelativePath
                : diff.Actual != null && targetRoot != null
                    ? ObjectMatcher.GetRelativePathFromRoot(diff.Actual.transform, targetRoot.transform)
                    : "";

            var foldout = CreateReportFoldout(Localization.S("componentCopier.diff." + Camel(diff.Kind)),
                path, typeName, diff.Kind.ToString().ToLowerInvariant());

            foreach (var property in diff.Properties)
            {
                var line = new Label($"{property.DisplayName}: {property.Expected} → {property.Actual}");
                line.AddToClassList("diff-property");
                line.AddToClassList("diff-property--" + property.Kind.ToString().ToLowerInvariant());
                foldout.Add(line);
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

        #endregion
    }
}
