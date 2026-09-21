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
        private enum GroupMode
        {
            /// <summary>One group per component type; rows are the objects. Good for "copy all PhysBones".</summary>
            ByType,
            /// <summary>One group per object; rows are its components. Good for "what sits on this bone?".</summary>
            ByObject,
        }

        private const string GroupModePrefsKey = "Kanameliser.EditorPlus.ComponentCopier.GroupMode";
        private const string ObjectGroupPrefix = "object:";

        private string searchText = "";
        private bool searchUsesRegex;
        private bool showExcluded;
        private GroupMode groupMode;

        private Button groupByTypeButton;
        private Button groupByObjectButton;
        private ToolbarSearchField searchField;
        private readonly Dictionary<ComponentCategory, Button> presetButtons = new();
        // Chips of the tools found in the source, keyed by ToolInfo.Id and kept in display order
        private readonly List<(string id, Button button)> toolPresetButtons = new();
        private VisualElement presetRow;
        private Button presetAllButton;

        private void CreateComponentListSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            parent.Add(section);

            // Header row: title on the left, grouping mode as a pill toggle on the right
            // (same layout as the mode toggle of Color Variant Generator's Creator window)
            var headerRow = new VisualElement();
            headerRow.AddToClassList("section-title-row");
            headerRow.AddToClassList("section-title-row--spread");
            section.Add(headerRow);

            var title = new Label("componentCopier.components");
            title.AddToClassList("section-title");
            title.AddToClassList("ndmf-tr");
            headerRow.Add(title);

            groupMode = (GroupMode)EditorPrefs.GetInt(GroupModePrefsKey, (int)GroupMode.ByType);

            var groupModeToggle = new VisualElement();
            groupModeToggle.AddToClassList("mode-toggle-group");
            headerRow.Add(groupModeToggle);

            groupByTypeButton = new Button(() => SetGroupMode(GroupMode.ByType));
            groupByTypeButton.AddToClassList("mode-toggle-button");
            groupModeToggle.Add(groupByTypeButton);

            groupByObjectButton = new Button(() => SetGroupMode(GroupMode.ByObject));
            groupByObjectButton.AddToClassList("mode-toggle-button");
            groupModeToggle.Add(groupByObjectButton);

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

            presetRow = new VisualElement();
            presetRow.AddToClassList("preset-row");
            section.Add(presetRow);

            // Category names are product terms and stay in English
            AddPresetButton(presetRow, ComponentCategory.PhysBone, "PhysBone");
            AddPresetButton(presetRow, ComponentCategory.Contact, "Contact");
            AddPresetButton(presetRow, ComponentCategory.Constraint, "Constraint");
            AddPresetButton(presetRow, ComponentCategory.ModularAvatar, "MA");
            // Chips of other tools are inserted here by SyncToolPresetButtons

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

        /// <summary>
        /// Tools other than MA are too many to list up front, so their chips are created from what the source
        /// has. The fixed chips are hidden in the same situation, see <see cref="UpdatePresetButton"/>.
        /// </summary>
        private void SyncToolPresetButtons()
        {
            var tools = entries
                .Where(e => e.Tool != null)
                .GroupBy(e => e.Tool.Id)
                .Select(g => g.First().Tool)
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Rebuilding on every render would replace a chip in the middle of its own click
            if (tools.Select(t => t.Id).SequenceEqual(toolPresetButtons.Select(p => p.id))) return;

            foreach (var (_, button) in toolPresetButtons)
                button.RemoveFromHierarchy();
            toolPresetButtons.Clear();

            int index = presetRow.IndexOf(presetAllButton);
            foreach (var tool in tools)
            {
                string id = tool.Id;
                var button = new Button(() => TogglePreset(e => e.Tool != null && e.Tool.Id == id))
                {
                    text = tool.ShortName,
                    tooltip = tool.DisplayName,
                };
                button.AddToClassList("preset-chip");
                presetRow.Insert(index++, button);
                toolPresetButtons.Add((id, button));
            }
        }

        private void UpdatePresetButtons()
        {
            SyncToolPresetButtons();

            foreach (var pair in presetButtons)
                UpdatePresetButton(pair.Value, e => e.Category == pair.Key, hideWhenEmpty: true);

            foreach (var (id, button) in toolPresetButtons)
                UpdatePresetButton(button, e => e.Tool != null && e.Tool.Id == id, hideWhenEmpty: true);

            // "All" stays as the anchor of the row and is only disabled
            UpdatePresetButton(presetAllButton, e => e.Category != ComponentCategory.ExcludedByDefault,
                hideWhenEmpty: false);
        }

        private void UpdatePresetButton(Button button, Func<ComponentEntry, bool> filter, bool hideWhenEmpty)
        {
            var matching = entries.Where(filter).ToList();
            // A chip that can never be pressed for this source is only noise
            button.style.display = hideWhenEmpty && matching.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            button.SetEnabled(matching.Count > 0);
            button.EnableInClassList("preset-chip--active",
                matching.Count > 0 && matching.All(e => selectedKeys.Contains(e.Key)));
        }

        #endregion

        #region Rendering

        private void SetGroupMode(GroupMode mode)
        {
            if (groupMode == mode) return;

            groupMode = mode;
            EditorPrefs.SetInt(GroupModePrefsKey, (int)mode);
            // Only the presentation changes, so the plan is left alone
            RenderComponentList();
        }

        private void UpdateGroupModeButtons()
        {
            groupByTypeButton.text = Localization.S("componentCopier.groupBy.type");
            groupByTypeButton.tooltip = Localization.S("componentCopier.groupBy.type:tooltip");
            groupByObjectButton.text = Localization.S("componentCopier.groupBy.object");
            groupByObjectButton.tooltip = Localization.S("componentCopier.groupBy.object:tooltip");

            groupByTypeButton.EnableInClassList("mode-toggle-button--active", groupMode == GroupMode.ByType);
            groupByObjectButton.EnableInClassList("mode-toggle-button--active", groupMode == GroupMode.ByObject);
        }

        private void RenderComponentList()
        {
            listContainer.Clear();
            UpdatePresetButtons();
            UpdateGroupModeButtons();

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
                AddMissingPrefabGroup();
                return;
            }

            if (groupMode == GroupMode.ByObject)
            {
                // An object keeps all of its components together; the ones that are not copied by default
                // are dimmed per row instead of being moved to a section of their own.
                AddGroups(visible, false);
                AddMissingPrefabGroup();
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

            AddMissingPrefabGroup();
        }

        private const string PrefabGroupId = "__missingPrefabs";

        /// <summary>
        /// Nested prefabs that the target lacks. A prefab is added automatically when a selected component lives
        /// inside it; here the user can also add prefabs without one, such as a hat that only has meshes.
        /// </summary>
        private void AddMissingPrefabGroup()
        {
            if (plan == null || missingPrefabs.Count == 0) return;

            var plannedRoots = new HashSet<Transform>(
                plan.ObjectsToCreate.Where(o => o.IsPrefabRoot && o.PrefabRoot == null).Select(o => o.Source));
            var blockedReasons = plan.BlockedPrefabs.ToDictionary(b => b.Source, b => b.Reason);

            var group = new VisualElement();
            group.AddToClassList("component-group");
            group.AddToClassList("prefab-group");
            listContainer.Add(group);

            var header = new VisualElement();
            header.AddToClassList("group-header");
            group.Add(header);

            var content = new VisualElement();
            content.AddToClassList("group-content");
            group.Add(content);

            var arrow = new Label("▶");
            arrow.AddToClassList("collapsible-arrow");
            header.Add(arrow);

            int selectedCount = missingPrefabs.Count(p => selectedPrefabPaths.Contains(PrefabPath(p)));
            var toggle = new Toggle
            {
                value = selectedCount == missingPrefabs.Count,
                showMixedValue = selectedCount > 0 && selectedCount < missingPrefabs.Count,
            };
            toggle.AddToClassList("group-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                foreach (var prefab in missingPrefabs)
                {
                    if (evt.newValue) selectedPrefabPaths.Add(PrefabPath(prefab));
                    else selectedPrefabPaths.Remove(PrefabPath(prefab));
                }

                Recompute();
            });
            header.Add(toggle);

            var icon = new Image { image = EditorGUIUtility.IconContent("Prefab Icon").image };
            icon.AddToClassList("group-icon");
            header.Add(icon);

            var nameLabel = new Label(Localization.S("componentCopier.prefabs.title"))
            {
                tooltip = Localization.S("componentCopier.prefabs.title:tooltip"),
            };
            nameLabel.AddToClassList("group-name");
            header.Add(nameLabel);

            var countLabel = new Label(missingPrefabs.Count.ToString());
            countLabel.AddToClassList("count-badge");
            header.Add(countLabel);

            var summaryLabel = new Label(plannedRoots.Count > 0
                ? Localization.S("componentCopier.prefabs.summary", plannedRoots.Count)
                : "");
            summaryLabel.AddToClassList("group-summary");
            header.Add(summaryLabel);

            if (blockedReasons.Count > 0)
            {
                var warningLabel = new Label("⚠ " + blockedReasons.Count);
                warningLabel.AddToClassList("warning-badge");
                header.Add(warningLabel);
            }

            void ApplyExpanded(bool expanded)
            {
                arrow.EnableInClassList("collapsible-arrow--open", expanded);
                content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            header.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement element && (element == toggle || toggle.Contains(element))) return;

                bool expanded = !expandedTypes.Contains(PrefabGroupId);
                if (expanded) expandedTypes.Add(PrefabGroupId);
                else expandedTypes.Remove(PrefabGroupId);
                ApplyExpanded(expanded);
            });

            foreach (var prefab in missingPrefabs)
                content.Add(CreatePrefabRow(prefab, plannedRoots.Contains(prefab), blockedReasons));

            ApplyExpanded(expandedTypes.Contains(PrefabGroupId));
        }

        private VisualElement CreatePrefabRow(
            Transform prefab, bool planned, Dictionary<Transform, BlockReason> blockedReasons)
        {
            string path = PrefabPath(prefab);
            bool selected = selectedPrefabPaths.Contains(path);
            // Already on its way because a selected component lives inside; unchecking would have no effect
            bool addedByComponents = planned && !selected;

            var row = new VisualElement();
            row.AddToClassList("component-row");

            var toggle = new Toggle { value = selected || addedByComponents };
            toggle.SetEnabled(!addedByComponents);
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) selectedPrefabPaths.Add(path);
                else selectedPrefabPaths.Remove(path);
                Recompute();
            });
            row.Add(toggle);

            var pathLabel = new Label(path) { tooltip = path };
            pathLabel.AddToClassList("row-path");
            pathLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (prefab != null) EditorGUIUtility.PingObject(prefab.gameObject);
            });
            row.Add(pathLabel);

            if (blockedReasons.ContainsKey(prefab))
            {
                var chip = new Label(ActionName(ComponentAction.Blocked))
                {
                    tooltip = Localization.S("componentCopier.prefabs.blocked"),
                };
                chip.AddToClassList("status-chip");
                chip.AddToClassList("status-chip--blocked");
                row.Add(chip);
            }
            else if (planned)
            {
                var chip = new Label(Localization.S(addedByComponents
                    ? "componentCopier.prefabs.status.withComponents"
                    : "componentCopier.prefabs.status.add"));
                chip.AddToClassList("status-chip");
                chip.AddToClassList("status-chip--add");
                row.Add(chip);
            }

            return row;
        }

        /// <summary>What a group of the list stands for: a component type, or an object of the source.</summary>
        private sealed class GroupInfo
        {
            public string Id;
            public string Title;
            public string Tooltip;
            public Texture Icon;
            public List<ComponentEntry> Entries;
            public bool Excluded;
            /// <summary>Object to ping when the title is clicked (object grouping only).</summary>
            public GameObject PingTarget;
        }

        private string GroupId(ComponentEntry entry)
        {
            return groupMode == GroupMode.ByType ? entry.Type.FullName : ObjectGroupPrefix + entry.Key.RelativePath;
        }

        private void AddGroups(List<ComponentEntry> groupEntries, bool excluded)
        {
            if (groupMode == GroupMode.ByObject)
            {
                // Scan order is hierarchy order, so the groups read like the Hierarchy window
                foreach (var group in groupEntries.GroupBy(e => e.Host))
                {
                    var first = group.First();
                    string path = DisplayPath(first);
                    listContainer.Add(CreateGroup(new GroupInfo
                    {
                        Id = GroupId(first),
                        Title = path,
                        Tooltip = path,
                        Icon = EditorGUIUtility.ObjectContent(first.Host.gameObject, typeof(GameObject)).image,
                        Entries = group.ToList(),
                        // Dimmed only when nothing in the group is copied by default
                        Excluded = group.All(e => e.Category == ComponentCategory.ExcludedByDefault),
                        PingTarget = first.Host.gameObject,
                    }));
                }

                return;
            }

            // Types are listed under a heading per category, in the same order as the preset chips
            var sections = groupEntries
                .GroupBy(CategorySectionId)
                .OrderBy(s => s.First().Category)
                .ThenBy(s => s.First().Tool?.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var section in sections)
            {
                // The excluded types already sit under a divider of their own
                if (!excluded)
                {
                    var heading = new Label(CategoryTitle(section.First()));
                    heading.AddToClassList("category-heading");
                    listContainer.Add(heading);
                }

                foreach (var group in section.GroupBy(e => e.Type)
                             .OrderBy(g => g.Key.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var first = group.First();
                    listContainer.Add(CreateGroup(new GroupInfo
                    {
                        Id = GroupId(first),
                        Title = group.Key.Name,
                        Tooltip = group.Key.FullName,
                        Icon = EditorGUIUtility.ObjectContent(first.Component, group.Key).image,
                        Entries = group.ToList(),
                        Excluded = excluded,
                    }));
                }
            }
        }

        private static string CategorySectionId(ComponentEntry entry)
        {
            return entry.Tool != null ? "tool:" + entry.Tool.Id : entry.Category.ToString();
        }

        private static string CategoryTitle(ComponentEntry entry)
        {
            switch (entry.Category)
            {
                // Product terms stay in English, like the preset chips
                case ComponentCategory.PhysBone: return "PhysBone";
                case ComponentCategory.Contact: return "Contact";
                case ComponentCategory.Constraint: return "Constraint";
                case ComponentCategory.ModularAvatar: return "Modular Avatar";
                case ComponentCategory.Tool: return entry.Tool.DisplayName;
                default: return Localization.S("componentCopier.category.other");
            }
        }

        private VisualElement CreateGroup(GroupInfo info)
        {
            var groupEntries = info.Entries;

            var group = new VisualElement();
            group.AddToClassList("component-group");
            group.EnableInClassList("component-group--excluded", info.Excluded);

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

            var icon = new Image { image = info.Icon };
            icon.AddToClassList("group-icon");
            header.Add(icon);

            var nameLabel = new Label(info.Title) { tooltip = info.Tooltip };
            nameLabel.AddToClassList("group-name");
            if (info.PingTarget != null)
            {
                var pingTarget = info.PingTarget;
                nameLabel.AddToClassList("group-name--clickable");
                nameLabel.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    if (pingTarget != null) EditorGUIUtility.PingObject(pingTarget);
                });
            }

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

                bool expanded = !expandedTypes.Contains(info.Id);

                // Alt+click expands or collapses every group at once
                if (evt.altKey)
                {
                    if (expanded) expandedTypes.UnionWith(entries.Select(GroupId));
                    else expandedTypes.Clear();
                    RenderComponentList();
                    return;
                }

                if (expanded) expandedTypes.Add(info.Id);
                else expandedTypes.Remove(info.Id);
                ApplyExpanded(expanded);
            });

            ApplyExpanded(expandedTypes.Contains(info.Id));
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

            // The group already says what the rows have in common, so a row shows the other half:
            // the object when grouped by type, the component when grouped by object.
            Label rowLabel;
            if (groupMode == GroupMode.ByObject)
            {
                var icon = new Image { image = EditorGUIUtility.ObjectContent(entry.Component, entry.Type).image };
                icon.AddToClassList("row-icon");
                row.Add(icon);

                string typeName = entry.Key.Index > 0 ? $"{entry.Type.Name} ({entry.Key.Index + 1})" : entry.Type.Name;
                rowLabel = new Label(typeName) { tooltip = entry.Type.FullName };
                row.EnableInClassList("component-row--excluded",
                    entry.Category == ComponentCategory.ExcludedByDefault);
            }
            else
            {
                string path = DisplayPath(entry);
                rowLabel = new Label(path) { tooltip = path };
            }

            rowLabel.AddToClassList("row-path");
            rowLabel.RegisterCallback<ClickEvent>(_ =>
            {
                if (entry.Host != null) EditorGUIUtility.PingObject(entry.Host.gameObject);
            });
            row.Add(rowLabel);

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

        private string DisplayPath(ComponentEntry entry)
        {
            return string.IsNullOrEmpty(entry.Key.RelativePath) ? sourceRoot.name : entry.Key.RelativePath;
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
            // Counted by label rather than by action, so components that arrive with a prefab show up apart
            var counts = new List<(string label, int count)>();
            foreach (var entry in groupEntries)
            {
                if (!plannedByKey.TryGetValue(entry.Key, out var planned)) continue;

                string label = ActionLabel(planned);
                int index = counts.FindIndex(c => c.label == label);
                if (index < 0) counts.Add((label, 1));
                else counts[index] = (label, counts[index].count + 1);
            }

            return string.Join(" · ", counts.Select(c => $"{c.label} {c.count}"));
        }

        private static string ActionName(ComponentAction action)
        {
            return Localization.S("componentCopier.action." + Camel(action));
        }

        /// <summary>
        /// Components that were not selected but come along with an instantiated prefab get their own label;
        /// "New" next to an unchecked row would look like a bug.
        /// </summary>
        private static string ActionLabel(PlannedComponent planned)
        {
            return planned.Implicit && planned.WillWrite
                ? Localization.S("componentCopier.action.withPrefab")
                : ActionName(planned.Action);
        }

        private static string ActionTooltip(PlannedComponent planned)
        {
            if (planned.Action != ComponentAction.Blocked) return "";
            return Localization.S("componentCopier.blocked." + Camel(planned.BlockReason));
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
