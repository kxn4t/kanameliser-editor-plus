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

            if (result.Failed.Count > 0)
                detailMessage += "\n" + Localization.S("componentCopier.report.failed", result.Failed.Count);

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
                selectedKeys.Add(key);
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
            // Components that merely arrive with a prefab are reported on the prefab line, not as selected work
            int Count(ComponentAction action) => plan.Components.Count(c => c.Action == action && !c.Implicit);

            // Same number as "to be created" in the mapping section: every object that appears in the target,
            // an added prefab counting as one. The prefab line below only says how some of them arrive.
            // "0 objects to create" next to "1 prefab is added" would contradict itself.
            int objectsToCreate = plan.ObjectsToCreate.Count(o => o.PrefabRoot == null);
            int prefabs = plan.ObjectsToCreate.Count(o => o.IsPrefabRoot && o.PrefabRoot == null);

            // "Identical" is counted apart from "Skip": lumped together, a target that is already up to date
            // looks as if the Overwrite policy had been ignored
            var summary = new Label(Localization.S("componentCopier.report.summary",
                Count(ComponentAction.Add), Count(ComponentAction.Overwrite), Count(ComponentAction.Replace),
                Count(ComponentAction.Skip), Count(ComponentAction.SkipIdentical), objectsToCreate));
            summary.AddToClassList("report-summary");
            reportContainer.Add(summary);

            if (prefabs > 0)
            {
                // Only the components that were not selected are news here; when everything inside the prefabs
                // is selected anyway, "0 components are copied too" would read like nothing is copied
                int implicitComponents = plan.Components.Count(c => c.Implicit);
                var prefabLabel = new Label(implicitComponents > 0
                    ? Localization.S("componentCopier.report.prefabs", prefabs, implicitComponents)
                    : Localization.S("componentCopier.report.prefabsOnly", prefabs));
                prefabLabel.AddToClassList("report-summary");
                reportContainer.Add(prefabLabel);
            }

            int redirected = plan.Components.SelectMany(c => c.References)
                .Count(r => r.Kind == ReferenceKind.ExternalMapped);
            if (redirected > 0)
            {
                // No avatar name here: replacements picked by hand work without a map of the surroundings
                // (plan.ExternalMap is null then), and they can point anywhere
                var redirectedLabel = new Label(Localization.S("componentCopier.report.externalMapped", redirected));
                redirectedLabel.AddToClassList("report-summary");
                reportContainer.Add(redirectedLabel);
            }

            if (IsTargetAsset()) AddWarning("componentCopier.warning.targetIsAsset");

            int blocked = Count(ComponentAction.Blocked);
            if (blocked > 0) AddWarning("componentCopier.report.blocked", blocked);

            var unresolved = plan.Components
                .SelectMany(c => c.References)
                .Where(r => r.Kind == ReferenceKind.InternalUnresolved)
                .ToList();
            if (unresolved.Count > 0)
            {
                var warning = AddWarning("componentCopier.report.unresolved", unresolved.Count);

                // References that only fail because the referenced component was left unselected
                var available = new HashSet<ComponentKey>(entries.Select(e => e.Key));
                var addable = unresolved
                    .Where(r => r.MissingDependency.HasValue && available.Contains(r.MissingDependency.Value))
                    .Select(r => r.MissingDependency.Value)
                    .Distinct()
                    .ToList();
                if (addable.Count > 0)
                {
                    var addButton = new Button(() => AddMissingDependencies(addable))
                    {
                        text = Localization.S("componentCopier.report.addDependencies", addable.Count),
                    };
                    addButton.AddToClassList("warning-action");
                    warning.Add(addButton);
                }
            }

            int external = plan.Components.SelectMany(c => c.References)
                .Count(r => r.Kind == ReferenceKind.ExternalScene);
            if (external > 0) AddWarning("componentCopier.report.external", external);

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
            if (string.IsNullOrEmpty(path)) path = "/";

            string kindText = Localization.S("componentCopier.diff." + Camel(diff.Kind));
            var foldout = new Foldout { text = $"[{kindText}] {path} — {typeName}", value = false };
            foldout.AddToClassList("diff-row");
            foldout.AddToClassList("diff-row--" + diff.Kind.ToString().ToLowerInvariant());

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
