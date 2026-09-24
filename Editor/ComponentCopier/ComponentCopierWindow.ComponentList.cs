using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kanameliser.Editor.MAMaterialHelper.Common;
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
        // The limit of one match, not of the whole search. A pattern that backtracks badly can take minutes on a
        // single object path, which would freeze the window.
        internal static readonly TimeSpan SearchMatchTimeout = TimeSpan.FromMilliseconds(200);

        private string searchText = "";
        private bool searchUsesRegex;
        // The current pattern took too long on some entry. It is ignored until the search text or the regex mode
        // changes, so that a checkbox click or a rescan does not wait for it again.
        private bool searchTimedOut;
        private bool showExcluded;
        private GroupMode groupMode;

        private Button groupByTypeButton;
        private Button groupByObjectButton;
        private ToolbarSearchField searchField;
        private ToolbarToggle regexToggle;
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
                searchTimedOut = false;
                RenderComponentList();
            });
            filterRow.Add(searchField);

            regexToggle = new ToolbarToggle { text = ".*" };
            regexToggle.AddToClassList("regex-toggle");
            regexToggle.RegisterValueChangedCallback(evt =>
            {
                searchUsesRegex = evt.newValue;
                searchTimedOut = false;
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

        private void TogglePreset(Func<ComponentEntry, bool> filter)
        {
            session.TogglePreset(filter);
            RenderAll();
        }

        /// <summary>
        /// Tools other than MA are too many to list up front, so their chips are created from what the source
        /// has. The fixed chips are hidden in the same situation, see <see cref="UpdatePresetButton"/>.
        /// </summary>
        private void SyncToolPresetButtons()
        {
            var tools = session.Entries
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
            var matching = session.Entries.Where(filter).ToList();
            // A chip that can never be pressed for this source is only noise
            button.style.display = hideWhenEmpty && matching.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            button.SetEnabled(matching.Count > 0);
            button.EnableInClassList("preset-chip--active", matching.Count > 0 && matching.All(session.IsChecked));
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

        private void UpdateListControls()
        {
            groupByTypeButton.text = Localization.S("componentCopier.groupBy.type");
            groupByTypeButton.tooltip = Localization.S("componentCopier.groupBy.type:tooltip");
            groupByObjectButton.text = Localization.S("componentCopier.groupBy.object");
            groupByObjectButton.tooltip = Localization.S("componentCopier.groupBy.object:tooltip");
            // The toggle only shows ".*", so its text is no key and NDMF cannot localize the tooltip for us
            regexToggle.tooltip = Localization.S("componentCopier.search.regex:tooltip");
            groupByTypeButton.EnableInClassList("mode-toggle-button--active", groupMode == GroupMode.ByType);
            groupByObjectButton.EnableInClassList("mode-toggle-button--active", groupMode == GroupMode.ByObject);
        }

        private Label NoSideComponentsLabel()
        {
            var side = Localization.S(session.MirrorSide == Side.Left
                ? "componentCopier.side.left"
                : "componentCopier.side.right");
            var label = new Label(Localization.S("componentCopier.info.noSideComponents", side));
            label.AddToClassList("info-label");
            return label;
        }

        private void RenderComponentList()
        {
            listContainer.Clear();
            UpdatePresetButtons();
            UpdateListControls();

            if (session.SourceRoot == null)
            {
                listContainer.Add(InfoLabel("componentCopier.info.selectSource"));
                return;
            }

            var visible = FilterEntries();

            var normal = visible.Where(e => e.Category != ComponentCategory.ExcludedByDefault).ToList();
            var excluded = visible.Where(e => e.Category == ComponentCategory.ExcludedByDefault).ToList();

            if (normal.Count == 0 && (!showExcluded || excluded.Count == 0))
            {
                // Nothing on the chosen side at all (renderers aside) is said as such, not hidden by the search
                bool sideIsEmpty = session.MirrorMode &&
                                   session.Entries.All(e => e.Category == ComponentCategory.ExcludedByDefault);
                listContainer.Add(sideIsEmpty
                    ? NoSideComponentsLabel()
                    : InfoLabel("componentCopier.info.noComponents"));
                AddMissingObjectGroup();
                return;
            }

            if (groupMode == GroupMode.ByObject)
            {
                // An object keeps all of its components together; the ones that are not copied by default
                // are dimmed per row instead of being moved to a section of their own.
                AddGroups(visible, false);
                AddMissingObjectGroup();
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

            AddMissingObjectGroup();
        }

        private const string MissingObjectGroupId = "__missingObjects";

        /// <summary>
        /// Nested prefabs and empty objects that the target lacks. They are added automatically when a selected
        /// component needs them; here the user can also add the ones nothing asks for, such as a hat that only
        /// has meshes or an anchor object.
        /// </summary>
        private void AddMissingObjectGroup()
        {
            var missingRoots = session.MissingRoots.ToList();
            if (session.Plan == null || missingRoots.Count == 0) return;

            var blockedReasons = session.Plan.BlockedObjects.ToDictionary(b => b.Source, b => b.Reason);
            int plannedCount = missingRoots.Count(session.IsObjectPlanned);

            // Same rule as the rows: an object that comes along with a selected component counts as checked,
            // otherwise the header stays empty above a row that is ticked
            var (group, header, toggle) = CreateGroupFrame(
                MissingObjectGroupId, missingRoots.Count(session.IsObjectChecked), missingRoots.Count,
                value => session.SetObjectsChecked(missingRoots, value),
                () => missingRoots.Select(missing => CreateMissingObjectRow(missing, blockedReasons)));
            group.AddToClassList("prefab-group");
            // Nothing to decide when every object is already on its way
            toggle.SetEnabled(!missingRoots.All(session.IsObjectAddedByComponents));
            listContainer.Add(group);

            var icon = new Image { image = EditorGUIUtility.IconContent("GameObject Icon").image };
            icon.AddToClassList("group-icon");
            header.Add(icon);

            var nameLabel = new Label(Localization.S("componentCopier.prefabs.title"))
            {
                tooltip = Localization.S("componentCopier.prefabs.title:tooltip"),
            };
            nameLabel.AddToClassList("group-name");
            header.Add(nameLabel);

            AddGroupBadges(header, missingRoots.Count,
                plannedCount > 0 ? Localization.S("componentCopier.prefabs.summary", plannedCount) : "",
                blockedReasons.Count);
        }

        private VisualElement CreateMissingObjectRow(
            Transform missing, Dictionary<Transform, BlockReason> blockedReasons)
        {
            // Already on its way because a selected component needs it; unchecking would have no effect
            bool addedByComponents = session.IsObjectAddedByComponents(missing);

            var row = new VisualElement();
            row.AddToClassList("component-row");

            var toggle = new Toggle { value = session.IsObjectChecked(missing) };
            toggle.SetEnabled(!addedByComponents);
            toggle.RegisterValueChangedCallback(evt =>
            {
                session.SetObjectsChecked(new[] { missing }, evt.newValue);
                RenderAll();
            });
            row.Add(toggle);

            // Prefabs and plain objects share the group, so the icon tells them apart
            var icon = new Image
            {
                image = EditorGUIUtility.ObjectContent(missing.gameObject, typeof(GameObject)).image,
            };
            icon.AddToClassList("row-icon");
            row.Add(icon);

            // A mirror copy can lack the counterpart of the source itself, whose path is empty
            var pathLabel = WithOverflowTooltip(new Label(GetObjectPath(missing)));
            pathLabel.AddToClassList("row-path");
            row.Add(pathLabel);
            RevealOnRowClick(row, toggle, missing);

            // From the root of the map: in a mirror copy, the source itself can be the missing prefab
            bool isPrefab = NestedPrefabs.GetPrefabAsset(missing, session.Map.SourceRoot) != null;
            int emptyChildren = isPrefab ? 0 : MissingObjects.CountBelow(missing, session.Map);
            if (emptyChildren > 0)
            {
                var childrenLabel = new Label("+" + emptyChildren)
                {
                    tooltip = Localization.S("componentCopier.prefabs.emptyChildren:tooltip", emptyChildren),
                };
                childrenLabel.AddToClassList("count-badge");
                row.Add(childrenLabel);
            }

            if (blockedReasons.ContainsKey(missing))
            {
                var chip = new Label(ActionName(ComponentAction.Blocked))
                {
                    tooltip = Localization.S("componentCopier.prefabs.blocked"),
                };
                chip.AddToClassList("status-chip");
                chip.AddToClassList(ComponentCopierStrings.StatusChipClass(ComponentAction.Blocked));
                row.Add(chip);
            }
            else if (session.IsObjectPlanned(missing))
            {
                var chip = new Label(Localization.S(addedByComponents
                    ? "componentCopier.prefabs.status.withComponents"
                    : "componentCopier.prefabs.status.add"));
                chip.AddToClassList("status-chip");
                chip.AddToClassList(ComponentCopierStrings.StatusChipClass(ComponentAction.Add));
                row.Add(chip);
            }

            return row;
        }

        /// <summary>What a group of the list stands for: a component type, or an object of the source.</summary>
        private sealed class GroupInfo
        {
            public string Id;
            /// <summary>Dimmed lead-in of the title: the folded parents of an object in the object tree.</summary>
            public string TitlePrefix;
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
            return groupMode == GroupMode.ByType ? entry.Type.FullName : ObjectGroupPrefix + entry.Key.ObjectPath;
        }

        private void AddGroups(List<ComponentEntry> groupEntries, bool excluded)
        {
            if (groupMode == GroupMode.ByObject)
            {
                AddObjectTree(groupEntries);
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

        /// <summary>An object of the source that has listed components, or an ancestor of one.</summary>
        private sealed class ObjectNode
        {
            public Transform Transform;
            public readonly List<ComponentEntry> Entries = new();
            public readonly List<ObjectNode> Children = new();
        }

        /// <summary>
        /// Lays the objects out like the Hierarchy window, so siblings such as Breast_L / Breast_R sit together
        /// under their parent instead of repeating the full path on every line.
        /// </summary>
        private void AddObjectTree(List<ComponentEntry> groupEntries)
        {
            var root = session.SourceRoot.transform;
            var nodes = new Dictionary<Transform, ObjectNode>();

            ObjectNode NodeFor(Transform transform)
            {
                if (nodes.TryGetValue(transform, out var node)) return node;

                node = new ObjectNode { Transform = transform };
                nodes[transform] = node;
                if (transform != root && transform.parent != null)
                    NodeFor(transform.parent).Children.Add(node);
                return node;
            }

            // Scan order is hierarchy order, so the children end up in sibling order
            foreach (var entry in groupEntries)
            {
                if (entry.Host != null) NodeFor(entry.Host).Entries.Add(entry);
            }

            if (!nodes.TryGetValue(root, out var rootNode)) return;

            // The root's children are not indented; everything is below the root anyway
            if (rootNode.Entries.Count > 0)
                listContainer.Add(CreateObjectGroup(rootNode, "", session.SourceRoot.name));
            foreach (var child in rootNode.Children)
                AddObjectNode(listContainer, child, "");
        }

        private void AddObjectNode(VisualElement container, ObjectNode node, string prefix)
        {
            string name = node.Transform.name;
            var prefabBadge = CreatePrefabBadge(node.Transform);

            // Bones that only lead to the next object are folded into its title ("Spine/Chest/Breast_L").
            // A nested prefab keeps a line of its own, where it can say that it is one.
            if (node.Entries.Count == 0 && node.Children.Count == 1 && prefabBadge == null)
            {
                AddObjectNode(container, node.Children[0], prefix + name + "/");
                return;
            }

            if (prefabBadge != null)
            {
                // The prefab and everything below it, set apart from its neighbors: it arrives as one unit
                var prefabBlock = new VisualElement();
                prefabBlock.AddToClassList("object-tree-prefab");
                container.Add(prefabBlock);
                container = prefabBlock;
            }

            if (node.Entries.Count > 0)
            {
                container.Add(CreateObjectGroup(node, prefix, name));
            }
            else
            {
                // A branching point without components of its own
                var pingTarget = node.Transform.gameObject;
                var heading = new Label(prefix + name) { tooltip = GetObjectPath(node.Transform) };
                heading.AddToClassList("object-tree-heading");
                heading.RegisterCallback<ClickEvent>(_ =>
                {
                    Reveal(pingTarget);
                });

                if (prefabBadge == null)
                {
                    container.Add(heading);
                }
                else
                {
                    var headingRow = new VisualElement();
                    headingRow.AddToClassList("object-tree-heading-row");
                    headingRow.Add(heading);
                    headingRow.Add(prefabBadge);
                    container.Add(headingRow);
                }
            }

            if (node.Children.Count == 0) return;

            var children = new VisualElement();
            children.AddToClassList("object-tree-children");
            container.Add(children);
            foreach (var child in node.Children)
                AddObjectNode(children, child, "");
        }

        private VisualElement CreateObjectGroup(ObjectNode node, string prefix, string name)
        {
            var first = node.Entries[0];
            return CreateGroup(new GroupInfo
            {
                Id = GroupId(first),
                TitlePrefix = prefix,
                Title = name,
                Tooltip = DisplayPath(first),
                Icon = EditorGUIUtility.ObjectContent(node.Transform.gameObject, typeof(GameObject)).image,
                Entries = node.Entries,
                // Dimmed only when nothing in the group is copied by default
                Excluded = node.Entries.All(e => e.Category == ComponentCategory.ExcludedByDefault),
                PingTarget = node.Transform.gameObject,
            });
        }

        /// <summary>
        /// Marks the root of a nested prefab in the object tree. Everything below it is added as one prefab
        /// instance when the target lacks it, which the rows alone do not explain. Null for other objects.
        /// </summary>
        private Label CreatePrefabBadge(Transform transform)
        {
            var asset = NestedPrefabs.GetPrefabAsset(transform, session.SourceRoot.transform);
            if (asset == null) return null;

            // A product term, like the preset chips
            var badge = new Label("Prefab") { tooltip = AssetDatabase.GetAssetPath(asset) };
            badge.AddToClassList("prefab-badge");
            return badge;
        }

        private string GetObjectPath(Transform transform)
        {
            string path = ObjectMatcher.GetRelativePathFromRoot(transform, session.SourceRoot.transform);
            return string.IsNullOrEmpty(path) ? session.SourceRoot.name : path;
        }

        private VisualElement CreateGroup(GroupInfo info)
        {
            var groupEntries = info.Entries;

            var (group, header, _) = CreateGroupFrame(
                info.Id, groupEntries.Count(session.IsChecked), groupEntries.Count,
                value => session.SetChecked(groupEntries, value),
                () => groupEntries.Select(CreateRow));
            group.EnableInClassList("component-group--excluded", info.Excluded);

            var icon = new Image { image = info.Icon };
            icon.AddToClassList("group-icon");
            header.Add(icon);

            var nameLabel = new Label(info.Title) { tooltip = info.Tooltip };
            nameLabel.AddToClassList("group-name");
            if (!string.IsNullOrEmpty(info.TitlePrefix))
            {
                // A label of its own, so a narrow window cuts the parents off before the object name
                var prefixLabel = new Label(info.TitlePrefix) { tooltip = info.Tooltip };
                prefixLabel.AddToClassList("group-name-prefix");
                header.Add(prefixLabel);
                nameLabel.AddToClassList("group-name--prefixed");
            }

            if (info.PingTarget != null)
            {
                var pingTarget = info.PingTarget;
                nameLabel.AddToClassList("group-name--clickable");
                nameLabel.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    Reveal(pingTarget);
                });
            }

            header.Add(nameLabel);

            var prefabBadge = info.PingTarget != null ? CreatePrefabBadge(info.PingTarget.transform) : null;
            if (prefabBadge != null) header.Add(prefabBadge);

            AddGroupBadges(header, groupEntries.Count, BuildGroupSummary(groupEntries), groupEntries.Count(HasWarning));
            return group;
        }

        /// <summary>
        /// The frame of a collapsible group in the component list: the fold arrow and the checkbox for the whole
        /// group in the header, and rows that are built when the group is first opened. The caller adds the rest
        /// of the header. Alt+click on a header opens or closes every group at once.
        /// </summary>
        /// <param name="setChecked">Applies the group checkbox to the rows, see <see cref="CopySession"/>.</param>
        /// <param name="createRows">Builds the rows. Called once, when the group is first shown open.</param>
        private (VisualElement group, VisualElement header, Toggle toggle) CreateGroupFrame(
            string id, int checkedCount, int count, Action<bool> setChecked, Func<IEnumerable<VisualElement>> createRows)
        {
            var group = new VisualElement();
            group.AddToClassList("component-group");

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
                    foreach (var row in createRows())
                        content.Add(row);
                }
            }

            var toggle = new Toggle
            {
                value = checkedCount == count,
                showMixedValue = checkedCount > 0 && checkedCount < count,
            };
            toggle.AddToClassList("group-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                setChecked(evt.newValue);
                RenderAll();
            });
            header.Add(toggle);

            header.RegisterCallback<ClickEvent>(evt =>
            {
                if (IsClickOn(evt, toggle)) return;

                bool expanded = !expandedTypes.Contains(id);

                if (evt.altKey)
                {
                    if (expanded)
                    {
                        expandedTypes.UnionWith(session.Entries.Select(GroupId));
                        expandedTypes.Add(MissingObjectGroupId);
                    }
                    else
                    {
                        expandedTypes.Clear();
                    }

                    RenderComponentList();
                    return;
                }

                if (expanded) expandedTypes.Add(id);
                else expandedTypes.Remove(id);
                ApplyExpanded(expanded);
            });

            ApplyExpanded(expandedTypes.Contains(id));
            return (group, header, toggle);
        }

        /// <summary>The end of a group header: the number of rows, what happens to them, and the warnings.</summary>
        private static void AddGroupBadges(VisualElement header, int count, string summary, int warnings)
        {
            var countLabel = new Label(count.ToString());
            countLabel.AddToClassList("count-badge");
            header.Add(countLabel);

            var summaryLabel = new Label(summary);
            summaryLabel.AddToClassList("group-summary");
            header.Add(summaryLabel);

            if (warnings > 0)
            {
                var warningLabel = new Label("⚠ " + warnings);
                warningLabel.AddToClassList("warning-badge");
                header.Add(warningLabel);
            }
        }

        private VisualElement CreateRow(ComponentEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("component-row");

            var toggle = new Toggle { value = session.IsChecked(entry) };
            toggle.RegisterValueChangedCallback(evt =>
            {
                session.SetChecked(entry, evt.newValue);
                RenderAll();
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
                rowLabel = WithOverflowTooltip(new Label(path));
            }

            rowLabel.AddToClassList("row-path");
            row.Add(rowLabel);
            RevealOnRowClick(row, toggle, entry.Host);

            if (session.TryGetPlanned(entry.Key, out var planned))
            {
                int unresolved = planned.References.Count(r => r.Kind == ReferenceKind.InternalUnresolved) +
                                 planned.UnresolvedReferences.Count;
                if (unresolved > 0)
                {
                    var unresolvedLabel = new Label("⚠ " + unresolved)
                    {
                        tooltip = Localization.S(planned.IsHeldBack
                            ? "componentCopier.row.heldBack:tooltip"
                            : "componentCopier.row.unresolved:tooltip", unresolved),
                    };
                    unresolvedLabel.AddToClassList("warning-badge");
                    row.Add(unresolvedLabel);
                }

                var chip = new Label(ActionLabel(planned)) { tooltip = ActionTooltip(planned) };
                chip.AddToClassList("status-chip");
                chip.AddToClassList(ComponentCopierStrings.StatusChipClass(planned.Action));
                row.Add(chip);
            }

            return row;
        }

        private string DisplayPath(ComponentEntry entry)
        {
            return string.IsNullOrEmpty(entry.Key.RelativePath) ? session.SourceRoot.name : entry.Key.RelativePath;
        }

        #endregion

        #region Helpers

        private bool HasWarning(ComponentEntry entry)
        {
            if (!session.TryGetPlanned(entry.Key, out var planned)) return false;
            return planned.Action == ComponentAction.Blocked ||
                   planned.References.Any(r => r.Kind == ReferenceKind.InternalUnresolved);
        }

        private string BuildGroupSummary(List<ComponentEntry> groupEntries)
        {
            // Counted by label rather than by action, so components that arrive with a prefab show up apart
            var counts = new List<(string label, int count)>();
            foreach (var entry in groupEntries)
            {
                if (!session.TryGetPlanned(entry.Key, out var planned)) continue;

                string label = ActionLabel(planned);
                int index = counts.FindIndex(c => c.label == label);
                if (index < 0) counts.Add((label, 1));
                else counts[index] = (label, counts[index].count + 1);
            }

            return string.Join(" · ", counts.Select(c => $"{c.label} {c.count}"));
        }

        private static string ActionName(ComponentAction action)
        {
            return Localization.S(ComponentCopierStrings.ActionKey(action));
        }

        /// <summary>
        /// Everything inside a nested prefab that gets added shares one label, selected or not: the prefab
        /// arrives as a whole either way. "New" on some of its rows would suggest that only those are added.
        /// </summary>
        private static string ActionLabel(PlannedComponent planned)
        {
            // Held back by the unresolved-reference setting: "Blocked" alone would hide that it is a skip
            if (planned.IsHeldBack)
                return Localization.S("componentCopier.action.skipUnresolved");

            return planned.WillWrite && planned.ArrivesWithPrefab
                ? Localization.S("componentCopier.action.withPrefab")
                : ActionName(planned.Action);
        }

        private static string ActionTooltip(PlannedComponent planned)
        {
            switch (planned.Action)
            {
                case ComponentAction.Blocked:
                    return Localization.S(ComponentCopierStrings.BlockReasonKey(planned.BlockReason));
                // Both leave the target alone, but for different reasons; with the Overwrite policy an
                // unexplained "Identical" reads as if the policy was ignored
                case ComponentAction.Skip:
                case ComponentAction.SkipIdentical:
                case ComponentAction.LeftOut:
                    return Localization.S(ComponentCopierStrings.ActionTooltipKey(planned.Action));
                // Likewise an unexplained "Overwrite" with the Replace policy
                case ComponentAction.Overwrite when planned.KeptBy != null:
                    return Localization.S("componentCopier.action.overwrite.kept:tooltip", planned.KeptBy);
                default:
                    return "";
            }
        }

        /// <summary>
        /// The entries the list shows: the ones of the categories on display that match the search field. A search
        /// text that cannot be used filters nothing and marks the field, so the list stays usable.
        /// </summary>
        private List<ComponentEntry> FilterEntries()
        {
            var inCategory = session.Entries
                .Where(e => showExcluded || e.Category != ComponentCategory.ExcludedByDefault)
                .ToList();

            var visible = inCategory;
            bool invalidPattern = false;
            // A pattern that timed out is not tried again, or every render would wait for it once more
            if (!searchTimedOut)
            {
                visible = FilterBySearch(inCategory, searchText, searchUsesRegex, SearchMatchTimeout,
                    out invalidPattern, out bool timedOut);
                searchTimedOut = timedOut;
            }

            searchField.EnableInClassList("search-field--invalid", invalidPattern || searchTimedOut);
            // Unlike an invalid pattern, a slow one looks fine, so the field says why it is ignored
            searchField.tooltip = searchTimedOut ? Localization.S("componentCopier.search.timeout:tooltip") : "";
            return visible;
        }

        /// <summary>
        /// The entries that match the search text in their object path and type name, as plain text or as a regular
        /// expression (case-insensitive either way). A text that is not a valid pattern, or a pattern that takes
        /// longer than <paramref name="matchTimeout"/> on one entry, filters nothing: every entry is returned, as
        /// with an empty search.
        /// </summary>
        /// <param name="matchTimeout">The limit of one match, not of the whole search.</param>
        internal static List<ComponentEntry> FilterBySearch(
            List<ComponentEntry> entries, string searchText, bool useRegex, TimeSpan matchTimeout,
            out bool invalidPattern, out bool timedOut)
        {
            invalidPattern = false;
            timedOut = false;
            if (string.IsNullOrWhiteSpace(searchText)) return entries;

            if (!useRegex)
            {
                return entries
                    .Where(e => SearchTarget(e).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            Regex regex;
            try
            {
                regex = new Regex(searchText, RegexOptions.IgnoreCase, matchTimeout);
            }
            catch (ArgumentException)
            {
                // Keep the list usable while the pattern is still being typed
                invalidPattern = true;
                return entries;
            }

            try
            {
                return entries.Where(e => regex.IsMatch(SearchTarget(e))).ToList();
            }
            catch (RegexMatchTimeoutException)
            {
                // Every entry, not only the ones after the timeout: the rows rejected before it come back, and the
                // list does not depend on the order in which they were tried
                timedOut = true;
                return entries;
            }
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
