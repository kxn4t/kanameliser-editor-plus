using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    public partial class ComponentCopierWindow
    {
        private string searchText = "";
        private bool searchUsesRegex;
        private bool showExcluded;

        private ToolbarSearchField searchField;
        private readonly Dictionary<ComponentCategory, Button> presetButtons = new();
        private Button presetAllButton;

        private void CreateComponentListSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            parent.Add(section);

            var title = new Label("componentCopier.components");
            title.AddToClassList("section-title");
            title.AddToClassList("ndmf-tr");
            section.Add(title);

            var filterRow = new VisualElement();
            filterRow.AddToClassList("filter-row");
            section.Add(filterRow);

            searchField = new ToolbarSearchField();
            searchField.AddToClassList("search-field");
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue ?? "";
                RenderComponentList();
            });
            filterRow.Add(searchField);

            var regexToggle = new ToolbarToggle { text = ".*" };
            regexToggle.AddToClassList("regex-toggle");
            regexToggle.RegisterValueChangedCallback(evt =>
            {
                searchUsesRegex = evt.newValue;
                RenderComponentList();
            });
            filterRow.Add(regexToggle);

            var presetRow = new VisualElement();
            presetRow.AddToClassList("preset-row");
            section.Add(presetRow);

            // Category names are product terms and stay in English
            AddPresetButton(presetRow, ComponentCategory.PhysBone, "PhysBone");
            AddPresetButton(presetRow, ComponentCategory.Constraint, "Constraint");
            AddPresetButton(presetRow, ComponentCategory.ModularAvatar, "MA");

            presetAllButton = new Button(ToggleAllPreset) { text = "componentCopier.preset.all" };
            presetAllButton.AddToClassList("preset-chip");
            presetAllButton.AddToClassList("ndmf-tr");
            presetRow.Add(presetAllButton);

            var showExcludedToggle = new Toggle("componentCopier.showExcluded") { value = showExcluded };
            showExcludedToggle.AddToClassList("show-excluded-toggle");
            showExcludedToggle.AddToClassList("ndmf-tr");
            showExcludedToggle.RegisterValueChangedCallback(evt =>
            {
                showExcluded = evt.newValue;
                RenderComponentList();
            });
            presetRow.Add(showExcludedToggle);

            listContainer = new VisualElement();
            listContainer.AddToClassList("component-list");
            section.Add(listContainer);
        }

        #region Presets

        private void AddPresetButton(VisualElement row, ComponentCategory category, string text)
        {
            var button = new Button(() => TogglePreset(e => e.Category == category)) { text = text };
            button.AddToClassList("preset-chip");
            presetButtons[category] = button;
            row.Add(button);
        }

        private void ToggleAllPreset() => TogglePreset(e => e.Category != ComponentCategory.ExcludedByDefault);

        /// <summary>
        /// A chip is "on" when every matching component is selected, so it always reflects the real selection
        /// even after individual checkboxes were changed.
        /// </summary>
        private void TogglePreset(Func<ComponentEntry, bool> filter)
        {
            var matching = entries.Where(filter).ToList();
            if (matching.Count == 0) return;

            bool allSelected = matching.All(e => selectedKeys.Contains(e.Key));
            foreach (var entry in matching)
            {
                if (allSelected) selectedKeys.Remove(entry.Key);
                else selectedKeys.Add(entry.Key);
            }

            Recompute();
        }

        private void UpdatePresetButtons()
        {
            foreach (var pair in presetButtons)
            {
                var matching = entries.Where(e => e.Category == pair.Key).ToList();
                pair.Value.SetEnabled(matching.Count > 0);
                pair.Value.EnableInClassList("preset-chip--active",
                    matching.Count > 0 && matching.All(e => selectedKeys.Contains(e.Key)));
            }

            var all = entries.Where(e => e.Category != ComponentCategory.ExcludedByDefault).ToList();
            presetAllButton.SetEnabled(all.Count > 0);
            presetAllButton.EnableInClassList("preset-chip--active",
                all.Count > 0 && all.All(e => selectedKeys.Contains(e.Key)));
        }

        #endregion

        #region Rendering

        private void RenderComponentList()
        {
            listContainer.Clear();
            UpdatePresetButtons();

            if (sourceRoot == null)
            {
                listContainer.Add(InfoLabel("componentCopier.info.selectSource"));
                return;
            }

            var filter = BuildSearchFilter();
            var visible = entries.Where(e => filter(e)).ToList();

            var normal = visible.Where(e => e.Category != ComponentCategory.ExcludedByDefault).ToList();
            var excluded = visible.Where(e => e.Category == ComponentCategory.ExcludedByDefault).ToList();

            if (normal.Count == 0 && (!showExcluded || excluded.Count == 0))
            {
                listContainer.Add(InfoLabel("componentCopier.info.noComponents"));
                return;
            }

            AddGroups(normal, false);

            if (showExcluded && excluded.Count > 0)
            {
                var divider = new Label(Localization.S("componentCopier.excludedByDefault"));
                divider.AddToClassList("excluded-divider");
                listContainer.Add(divider);

                AddGroups(excluded, true);
            }
        }

        private void AddGroups(List<ComponentEntry> groupEntries, bool excluded)
        {
            var groups = groupEntries
                .GroupBy(e => e.Type)
                .OrderBy(g => g.First().Category)
                .ThenBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
                listContainer.Add(CreateGroup(group.Key, group.ToList(), excluded));
        }

        private VisualElement CreateGroup(Type type, List<ComponentEntry> groupEntries, bool excluded)
        {
            string typeId = type.FullName;

            var group = new VisualElement();
            group.AddToClassList("component-group");
            group.EnableInClassList("component-group--excluded", excluded);

            var header = new VisualElement();
            header.AddToClassList("group-header");
            group.Add(header);

            var content = new VisualElement();
            content.AddToClassList("group-content");
            group.Add(content);

            var arrow = new Label("▶");
            arrow.AddToClassList("collapsible-arrow");
            header.Add(arrow);

            void ApplyExpanded(bool expanded)
            {
                arrow.EnableInClassList("collapsible-arrow--open", expanded);
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                // Rows are only built when first shown; avatars can carry hundreds of components
                if (expanded && content.childCount == 0)
                {
                    foreach (var entry in groupEntries)
                        content.Add(CreateRow(entry));
                }
            }

            int selectedCount = groupEntries.Count(e => selectedKeys.Contains(e.Key));
            var toggle = new Toggle
            {
                value = selectedCount == groupEntries.Count,
                showMixedValue = selectedCount > 0 && selectedCount < groupEntries.Count,
            };
            toggle.AddToClassList("group-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                foreach (var entry in groupEntries)
                {
                    if (evt.newValue) selectedKeys.Add(entry.Key);
                    else selectedKeys.Remove(entry.Key);
                }

                Recompute();
            });
            header.Add(toggle);

            var icon = new Image { image = EditorGUIUtility.ObjectContent(groupEntries[0].Component, type).image };
            icon.AddToClassList("group-icon");
            header.Add(icon);

            var nameLabel = new Label(type.Name) { tooltip = typeId };
            nameLabel.AddToClassList("group-name");
            header.Add(nameLabel);

            var countLabel = new Label(groupEntries.Count.ToString());
            countLabel.AddToClassList("count-badge");
            header.Add(countLabel);

            var summaryLabel = new Label(BuildGroupSummary(groupEntries));
            summaryLabel.AddToClassList("group-summary");
            header.Add(summaryLabel);

            int warnings = groupEntries.Count(HasWarning);
            if (warnings > 0)
            {
                var warningLabel = new Label("⚠ " + warnings);
                warningLabel.AddToClassList("warning-badge");
                header.Add(warningLabel);
            }

            header.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement element && (element == toggle || toggle.Contains(element))) return;

                bool expanded = !expandedTypes.Contains(typeId);

                // Alt+click expands or collapses every group at once
                if (evt.altKey)
                {
                    if (expanded) expandedTypes.UnionWith(entries.Select(e => e.Type.FullName));
                    else expandedTypes.Clear();
                    RenderComponentList();
                    return;
                }

                if (expanded) expandedTypes.Add(typeId);
                else expandedTypes.Remove(typeId);
                ApplyExpanded(expanded);
            });

            ApplyExpanded(expandedTypes.Contains(typeId));
            return group;
        }

        private VisualElement CreateRow(ComponentEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("component-row");

            var toggle = new Toggle { value = selectedKeys.Contains(entry.Key) };
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) selectedKeys.Add(entry.Key);
                else selectedKeys.Remove(entry.Key);
                Recompute();
            });
            row.Add(toggle);

            string path = string.IsNullOrEmpty(entry.Key.RelativePath) ? sourceRoot.name : entry.Key.RelativePath;
            var pathLabel = new Label(path) { tooltip = path };
            pathLabel.AddToClassList("row-path");
            pathLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (entry.Host != null) EditorGUIUtility.PingObject(entry.Host.gameObject);
            });
            row.Add(pathLabel);

            if (plannedByKey.TryGetValue(entry.Key, out var planned))
            {
                int unresolved = planned.References.Count(r => r.Kind == ReferenceKind.InternalUnresolved);
                if (unresolved > 0)
                {
                    var unresolvedLabel = new Label("⚠ " + unresolved)
                    {
                        tooltip = Localization.S("componentCopier.row.unresolved:tooltip", unresolved),
                    };
                    unresolvedLabel.AddToClassList("warning-badge");
                    row.Add(unresolvedLabel);
                }

                var chip = new Label(ActionLabel(planned)) { tooltip = ActionTooltip(planned) };
                chip.AddToClassList("status-chip");
                chip.AddToClassList("status-chip--" + planned.Action.ToString().ToLowerInvariant());
                row.Add(chip);
            }

            return row;
        }

        #endregion

        #region Helpers

        private bool HasWarning(ComponentEntry entry)
        {
            if (!plannedByKey.TryGetValue(entry.Key, out var planned)) return false;
            return planned.Action == ComponentAction.Blocked ||
                   planned.References.Any(r => r.Kind == ReferenceKind.InternalUnresolved);
        }

        private string BuildGroupSummary(List<ComponentEntry> groupEntries)
        {
            var counts = new Dictionary<ComponentAction, int>();
            foreach (var entry in groupEntries)
            {
                if (!plannedByKey.TryGetValue(entry.Key, out var planned)) continue;
                counts.TryGetValue(planned.Action, out var count);
                counts[planned.Action] = count + 1;
            }

            return string.Join(" · ", counts
                .OrderBy(p => p.Key)
                .Select(p => $"{ActionName(p.Key)} {p.Value}"));
        }

        private static string ActionName(ComponentAction action)
        {
            return Localization.S("componentCopier.action." + Camel(action));
        }

        private static string ActionLabel(PlannedComponent planned) => ActionName(planned.Action);

        private static string ActionTooltip(PlannedComponent planned)
        {
            if (planned.Action != ComponentAction.Blocked) return "";
            return Localization.S(planned.BlockReason == BlockReason.HostNeedsReview
                ? "componentCopier.blocked.needsReview"
                : "componentCopier.blocked.unmapped");
        }

        private Func<ComponentEntry, bool> BuildSearchFilter()
        {
            searchField.EnableInClassList("search-field--invalid", false);

            bool MatchesCategory(ComponentEntry e) =>
                showExcluded || e.Category != ComponentCategory.ExcludedByDefault;

            if (string.IsNullOrWhiteSpace(searchText)) return MatchesCategory;

            if (searchUsesRegex)
            {
                try
                {
                    var regex = new Regex(searchText, RegexOptions.IgnoreCase);
                    return e => MatchesCategory(e) && regex.IsMatch(SearchTarget(e));
                }
                catch (ArgumentException)
                {
                    // Keep the list usable while the pattern is still being typed
                    searchField.EnableInClassList("search-field--invalid", true);
                    return MatchesCategory;
                }
            }

            return e => MatchesCategory(e) &&
                        SearchTarget(e).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SearchTarget(ComponentEntry entry) => entry.Key.RelativePath + " " + entry.Type.Name;

        private static Label InfoLabel(string key)
        {
            var label = new Label(Localization.S(key));
            label.AddToClassList("info-label");
            return label;
        }

        #endregion
    }
}
