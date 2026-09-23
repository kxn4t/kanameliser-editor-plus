using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    public partial class ComponentCopierWindow
    {
        private bool confirmedMappingsExpanded;
        private bool createdMappingsExpanded;
        // Objects that would be created, for which the user wants to pick an existing object instead
        private readonly HashSet<Transform> pickExistingFor = new();
        private Label mappingSummaryLabel;

        private void CreateMappingSection(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            parent.Add(section);

            var titleRow = new VisualElement();
            titleRow.AddToClassList("section-title-row");
            section.Add(titleRow);

            var title = new Label("componentCopier.mapping");
            title.AddToClassList("section-title");
            title.AddToClassList("ndmf-tr");
            titleRow.Add(title);

            mappingSummaryLabel = new Label();
            mappingSummaryLabel.AddToClassList("section-summary");
            titleRow.Add(mappingSummaryLabel);

            mappingContainer = new VisualElement();
            mappingContainer.AddToClassList("mapping-list");
            section.Add(mappingContainer);
        }

        /// <summary>
        /// Only transforms that matter for the current selection are listed: objects that host a selected
        /// component and objects referenced by one. A few dozen rows instead of every bone of the avatar.
        /// </summary>
        private List<TransformMapping> CollectRelevantMappings(Dictionary<Transform, int> hierarchyOrder = null)
        {
            hierarchyOrder ??= SourceHierarchyOrder();

            var relevant = new List<Transform>();
            var seen = new HashSet<Transform>();

            void Add(Transform transform)
            {
                if (transform == null || transform == map.SourceRoot) return;
                if (!Hierarchy.IsInside(transform, map.SourceRoot)) return;
                if (seen.Add(transform)) relevant.Add(transform);
            }

            foreach (var planned in plan.Components)
            {
                Add(planned.Entry.Host);
                foreach (var reference in planned.References)
                    Add(ReferenceWalker.GetTransform(reference.SourceValue));
                // A held-back component writes nothing, but the references that hold it back are exactly
                // what the user has to map to get it copied
                foreach (var reference in planned.UnresolvedReferences)
                    Add(ReferenceWalker.GetTransform(reference.SourceValue));
            }

            // Listed in Hierarchy order. The order of discovery jumps between a component's object and the
            // objects it references, which makes a row hard to find.
            // Blocked components have no references collected yet, but their host is what needs attention
            return relevant
                .OrderBy(t => hierarchyOrder.TryGetValue(t, out int index) ? index : int.MaxValue)
                .Select(t => map.Get(t))
                .Where(m => m != null)
                .ToList();
        }

        private Dictionary<Transform, int> SourceHierarchyOrder()
        {
            var order = new Dictionary<Transform, int>();
            foreach (var transform in map.SourceRoot.GetComponentsInChildren<Transform>(true))
                order[transform] = order.Count;
            return order;
        }

        private void RenderMapping()
        {
            mappingContainer.Clear();
            mappingSummaryLabel.text = "";

            if (map == null || plan == null)
            {
                mappingContainer.Add(InfoLabel(
                    mirrorMode ? "componentCopier.info.selectSource"
                    : targetRoot == null ? "componentCopier.info.selectTarget"
                    : sourceRoot == null ? "componentCopier.info.needsSource"
                    : "componentCopier.info.fixTarget"));
                return;
            }

            var created = plan.ObjectsToCreate.ToDictionary(o => o.Source);

            // An object that is going to be created is taken care of, so it is listed apart from what needs a
            // counterpart. Every such object is listed, also the parents created on the way (PB, PB/Tops, ...):
            // the count then agrees with "objects to create" of the pre-check. Objects inside a nested prefab
            // that gets instantiated are not decisions of their own; only the prefab root is listed.
            var hierarchyOrder = SourceHierarchyOrder();
            var planned = plan.ObjectsToCreate
                .Where(o => o.PrefabRoot == null)
                .OrderBy(o => hierarchyOrder.TryGetValue(o.Source, out int index) ? index : int.MaxValue)
                .Select(o => map.Get(o.Source) ?? new TransformMapping { Source = o.Source })
                .ToList();
            // ... unless the user asked to pick an existing object instead; those rows need a counterpart again
            var toCreate = planned.Where(m => !pickExistingFor.Contains(m.Source)).ToList();
            var pickingExisting = planned.Where(m => pickExistingFor.Contains(m.Source)).ToList();

            var mappings = CollectRelevantMappings(hierarchyOrder)
                .Where(m => !created.ContainsKey(m.Source))
                .ToList();
            var external = CollectExternalReferences();
            if (mappings.Count == 0 && planned.Count == 0 && external.Count == 0)
            {
                mappingContainer.Add(InfoLabel("componentCopier.info.noMappings"));
                return;
            }

            // "No counterpart" picked by hand is a decision, not something left to do. The consequences are the
            // business of the pre-check; here it must not keep counting as unmapped once the user has dealt with it.
            static bool IsManualNone(TransformMapping m) => m.State == MappingState.Manual && m.Target == null;

            var needsReview = mappings.Where(m => m.State == MappingState.NeedsReview).ToList();
            var unmapped = mappings
                .Where(m => !m.IsUsable && m.State != MappingState.NeedsReview && !IsManualNone(m))
                .Concat(pickingExisting)
                .ToList();
            // Both kinds stay in view so that they can be changed again
            var manual = mappings.Where(m => m.State == MappingState.Manual && (m.IsUsable || IsManualNone(m))).ToList();
            int manualNone = manual.Count(IsManualNone);
            var confirmed = mappings.Where(m => m.State == MappingState.Confirmed).ToList();

            // "x/y mapped" is about the objects that need a counterpart; the ones to create are counted apart,
            // and the ones the user gave none on purpose are not counted at all.
            // References to the outside count as "to review", never as unmapped: without a counterpart they
            // are kept, so nothing breaks, but the user should decide whether that is what they want.
            int unresolved = unmapped.Count - pickingExisting.Count;
            int toReview = needsReview.Count + external.Count(e =>
            {
                var externalMapping = plan.ExternalMap?.Get(e.Target);
                return externalMapping?.State == MappingState.NeedsReview || NeedsDecision(externalMapping, true);
            });
            mappingSummaryLabel.text = Localization.S("componentCopier.mapping.summary",
                confirmed.Count + manual.Count - manualNone, mappings.Count - manualNone, toReview, unresolved,
                planned.Count);
            mappingSummaryLabel.EnableInClassList("section-summary--warning", toReview + unresolved > 0);

            int affixCount = needsReview.Count(m => m.Reason == MappingReason.AffixStripped);
            if (affixCount > 1)
            {
                var bulkButton = new Button(() => ConfirmAll(MappingReason.AffixStripped))
                {
                    text = Localization.S("componentCopier.mapping.confirmAffix", affixCount),
                    tooltip = Localization.S("componentCopier.mapping.confirmAffix:tooltip"),
                };
                bulkButton.AddToClassList("bulk-confirm-button");
                mappingContainer.Add(bulkButton);
            }

            // Bones and other objects are matched by different rules and differ in what can be done about a
            // missing one (a bone is never created), so they are listed apart.
            var attention = needsReview.Concat(unmapped).Concat(manual).ToList();
            var bones = attention.Where(m => map.SourceSkeleton.IsBone(m.Source)).ToList();
            var others = attention.Where(m => !map.SourceSkeleton.IsBone(m.Source)).ToList();
            bool showGroups = bones.Count > 0 && others.Count > 0;

            AddMappingGroup(bones, showGroups ? "componentCopier.mapping.group.bones" : null, created);
            AddMappingGroup(others, showGroups ? "componentCopier.mapping.group.others" : null, created);

            AddFoldedGroup(
                Localization.S("componentCopier.mapping.willCreate", toCreate.Count), toCreate,
                createdMappingsExpanded, expanded => createdMappingsExpanded = expanded,
                mapping => CreateWillCreateRow(mapping, created[mapping.Source]));
            AddFoldedGroup(
                Localization.S("componentCopier.mapping.confirmed", confirmed.Count), confirmed,
                confirmedMappingsExpanded, expanded => confirmedMappingsExpanded = expanded,
                mapping => CreateMappingRow(mapping, null));

            AddExternalGroup(external);
        }

        /// <summary>Rows that need no decision are kept out of the way, folded by default.</summary>
        private void AddFoldedGroup(
            string title, List<TransformMapping> rows, bool expanded, System.Action<bool> rememberExpanded,
            System.Func<TransformMapping, VisualElement> createRow)
        {
            if (rows.Count == 0) return;

            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList("confirmed-foldout");

            // Rows are only built when first shown
            void Fill()
            {
                if (foldout.childCount > 0) return;
                foreach (var mapping in rows)
                    foldout.Add(createRow(mapping));
            }

            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout) return;
                rememberExpanded(evt.newValue);
                if (evt.newValue) Fill();
            });
            if (expanded) Fill();

            mappingContainer.Add(foldout);
        }

        /// <summary>
        /// An object that does not exist in the target and gets created. Not a problem, so it is not dressed
        /// like one: the right-hand side says where the object will be, as everywhere in this list it shows the
        /// state after applying. An empty object field would read as "nothing", and that is not what happens.
        /// </summary>
        private VisualElement CreateWillCreateRow(TransformMapping mapping, PlannedObject plannedObject)
        {
            var row = new VisualElement();
            row.AddToClassList("mapping-row");
            row.AddToClassList("mapping-row--willcreate");

            string sourcePath = ObjectMatcher.GetRelativePathFromRoot(mapping.Source, map.SourceRoot);
            var sourceLabel = WithOverflowTooltip(new Label(sourcePath));
            sourceLabel.AddToClassList("mapping-source");
            sourceLabel.RegisterCallback<ClickEvent>(_ => Reveal(mapping.Source));
            row.Add(sourceLabel);

            var arrow = new Label("→");
            arrow.AddToClassList("mapping-arrow");
            row.Add(arrow);

            string createdPath = PlannedTargetPath(plannedObject);
            var createdLabel = WithOverflowTooltip(new Label("+ " + createdPath));
            createdLabel.AddToClassList("mapping-target");
            createdLabel.AddToClassList("mapping-created-path");
            row.Add(createdLabel);

            var note = new Label(MappingNote(mapping, plannedObject, false));
            note.AddToClassList("mapping-note");
            row.Add(note);

            var menuButton = new Button { text = "▾" };
            menuButton.clicked += () => ShowWillCreateMenu(mapping);
            menuButton.AddToClassList("mapping-menu-button");
            menuButton.tooltip = Localization.S("componentCopier.mapping.menu:tooltip");
            row.Add(menuButton);

            return row;
        }

        /// <summary>Where a planned object ends up, as a path below the target root.</summary>
        private string PlannedTargetPath(PlannedObject plannedObject)
        {
            var names = new List<string>();
            Transform existingParent = null;
            for (var current = plannedObject; current != null; current = current.ParentToCreate)
            {
                names.Insert(0, current.Name);
                existingParent = current.ExistingParent;
            }

            string parentPath = existingParent != null
                ? ObjectMatcher.GetRelativePathFromRoot(existingParent, map.TargetRoot)
                : "";
            if (!string.IsNullOrEmpty(parentPath)) names.Insert(0, parentPath);
            return string.Join("/", names);
        }

        private void ShowWillCreateMenu(TransformMapping mapping)
        {
            var menu = new GenericMenu();

            foreach (var candidate in mapping.Candidates)
            {
                var target = candidate.Target;
                if (target == null) continue;

                // GenericMenu turns '/' into submenus
                string path = ObjectMatcher.GetRelativePathFromRoot(target, map.TargetRoot).Replace("/", " ∕ ");
                menu.AddItem(new GUIContent(path), false, () => SetManualMapping(mapping.Source, target));
            }

            if (mapping.Candidates.Count > 0) menu.AddSeparator("");

            // Creating is the default, but the counterpart may exist under a name that could not be matched
            menu.AddItem(new GUIContent(Localization.S("componentCopier.mapping.menu.pickExisting")), false, () =>
            {
                pickExistingFor.Add(mapping.Source);
                RenderMapping();
            });

            menu.ShowAsContext();
        }

        /// <summary>
        /// Scene objects outside of the source hierarchy that the copied components refer to, typically parts
        /// of the avatar the source outfit sits on.
        /// </summary>
        private List<ExternalReference> CollectExternalReferences()
        {
            var byTarget = new Dictionary<Transform, ExternalReference>();

            foreach (var planned in plan.Components)
            {
                foreach (var reference in planned.References)
                {
                    if (reference.Kind != ReferenceKind.ExternalScene &&
                        reference.Kind != ReferenceKind.ExternalMapped) continue;

                    var target = ReferenceWalker.GetTransform(reference.SourceValue);
                    if (target == null) continue;

                    if (!byTarget.TryGetValue(target, out var external))
                        byTarget[target] = external = new ExternalReference { Target = target };
                    external.Holders.Add((planned, reference.DisplayPath));
                }
            }

            return byTarget.Values.OrderBy(e => ScenePath(e.Target), System.StringComparer.Ordinal).ToList();
        }

        /// <summary>An object outside of the source, and the copied components that refer to it.</summary>
        private sealed class ExternalReference
        {
            public Transform Target;
            public readonly List<(PlannedComponent component, string propertyPath)> Holders = new();
        }

        /// <summary>
        /// Lists every reference to the outside, redirected or not, so that the user sees what the copied
        /// components will depend on. With surroundings on both sides the rows work like the ones above.
        /// </summary>
        private void AddExternalGroup(List<ExternalReference> external)
        {
            if (external.Count == 0) return;

            var owner = plan.ExternalMap;
            // A mirror copy spans the avatar, so the outside is outside of the avatar and stays as it is
            bool canRedirect = !mirrorMode && targetRoot != null &&
                               ExternalContext.CanRedirect(sourceRoot.transform, targetRoot.transform);

            var titleRow = new VisualElement();
            titleRow.AddToClassList("mapping-group-title-row");
            mappingContainer.Add(titleRow);

            var title = new Label(owner != null
                ? Localization.S("componentCopier.mapping.group.external", owner.SourceRoot.name, owner.TargetRoot.name)
                : Localization.S(mirrorMode
                    ? "componentCopier.mapping.group.externalKept.mirror"
                    : "componentCopier.mapping.group.externalKept"))
            {
                tooltip = Localization.S(canRedirect ? "componentCopier.mapping.group.external:tooltip"
                    : mirrorMode ? "componentCopier.mapping.external.mirrorKept"
                    : "componentCopier.mapping.group.externalKept:tooltip"),
            };
            title.AddToClassList("mapping-group-title");
            titleRow.Add(title);

            // Whether to redirect at all is a choice of its own: a gimmick may be meant to keep following the
            // other avatar. Single rows can still be kept through their menu.
            if (canRedirect)
            {
                var redirectToggle = new Toggle(Localization.S("componentCopier.mapping.external.redirect"))
                {
                    value = settings.RedirectExternalReferences,
                    tooltip = Localization.S("componentCopier.mapping.external.redirect:tooltip"),
                };
                redirectToggle.AddToClassList("mapping-group-toggle");
                redirectToggle.RegisterValueChangedCallback(evt =>
                {
                    settings.RedirectExternalReferences = evt.newValue;
                    settings.Save();
                    Recompute();
                });
                titleRow.Add(redirectToggle);
            }
            else
            {
                // Said in the open rather than in a tooltip: rows that only say "kept" look like a failure
                mappingContainer.Add(InfoLabel(mirrorMode
                    ? "componentCopier.mapping.external.mirrorKept"
                    : "componentCopier.mapping.external.noSurroundings"));
            }

            foreach (var reference in external)
            {
                var mapping = owner != null && Hierarchy.IsInside(reference.Target, owner.SourceRoot)
                    ? owner.Get(reference.Target)
                    : null;
                mappingContainer.Add(mapping != null
                    ? CreateMappingRow(mapping, null, owner)
                    : CreateKeptReferenceRow(reference.Target));
                mappingContainer.Add(CreateHoldersLabel(reference));
            }
        }

        /// <summary>Says which components hold the reference: the row alone only names what is pointed at.</summary>
        private VisualElement CreateHoldersLabel(ExternalReference reference)
        {
            const int maxListed = 3;

            var holders = reference.Holders
                .GroupBy(h => h.component)
                .Select(g => (component: g.Key, properties: g.Select(h => h.propertyPath).ToList()))
                .ToList();

            string Describe(PlannedComponent planned) =>
                $"{DisplayPath(planned.Entry)} ({planned.Entry.Type.Name})";

            string text = string.Join(", ", holders.Take(maxListed).Select(h => Describe(h.component)));
            if (holders.Count > maxListed)
                text += " " + Localization.S("componentCopier.mapping.external.more", holders.Count - maxListed);

            // The full list, with the properties, for the cases the line has no room for
            var tooltipLines = new List<string>();
            foreach (var holder in holders)
            {
                tooltipLines.Add(Describe(holder.component));
                tooltipLines.AddRange(SummarizePropertyPaths(holder.properties).Select(p => "    " + p));
            }

            // Unity moves a tooltip that does not fit next to the cursor to the middle of the window
            const int maxTooltipLines = 12;
            if (tooltipLines.Count > maxTooltipLines)
            {
                tooltipLines = tooltipLines.Take(maxTooltipLines - 1).ToList();
                tooltipLines.Add("…");
            }

            var label = new Label(Localization.S("componentCopier.mapping.external.holders", text))
            {
                tooltip = string.Join("\n", tooltipLines),
            };
            label.AddToClassList("mapping-holders");

            var first = holders[0].component.Entry.Host;
            label.RegisterCallback<ClickEvent>(_ => Reveal(first));
            return label;
        }

        private static readonly System.Text.RegularExpressions.Regex ArrayElement =
            new System.Text.RegularExpressions.Regex(@"\.Array\.data\[(\d+)\]");

        /// <summary>
        /// Shortens serialized property paths for display: "m_shapes.Array.data[3].Object" becomes
        /// "m_shapes[3].Object", and the elements of one array are folded into "m_shapes[0-18].Object".
        /// A Shape Changer refers to the body mesh once per shape, which would fill the tooltip otherwise.
        /// </summary>
        private static List<string> SummarizePropertyPaths(IEnumerable<string> paths)
        {
            // Keyed by the path with its first array index taken out; insertion order is display order
            var order = new List<string>();
            var indices = new Dictionary<string, List<int>>();

            foreach (var path in paths)
            {
                var match = ArrayElement.Match(path);
                string key = match.Success
                    ? path.Substring(0, match.Index) + "[#]" + path.Substring(match.Index + match.Length)
                    : path;
                key = ArrayElement.Replace(key, "[$1]");

                if (!indices.TryGetValue(key, out var list))
                {
                    indices[key] = list = new List<int>();
                    order.Add(key);
                }

                if (match.Success) list.Add(int.Parse(match.Groups[1].Value));
            }

            return order.Select(key => key.Replace("[#]", "[" + FormatRanges(indices[key]) + "]")).ToList();
        }

        /// <summary>"0, 1, 2, 5" becomes "0-2, 5".</summary>
        private static string FormatRanges(List<int> values)
        {
            var sorted = values.Distinct().OrderBy(v => v).ToList();
            var parts = new List<string>();
            for (int i = 0; i < sorted.Count; i++)
            {
                int start = sorted[i];
                while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1) i++;
                parts.Add(start == sorted[i] ? start.ToString() : $"{start}-{sorted[i]}");
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// A reference to the outside without a map to look it up in: there are no surroundings to compare, or
        /// redirecting is turned off. It is kept unless the user names a replacement.
        /// </summary>
        private VisualElement CreateKeptReferenceRow(Transform transform)
        {
            var row = new VisualElement();
            row.AddToClassList("mapping-row");

            string path = ScenePath(transform);
            var sourceLabel = WithOverflowTooltip(new Label(path));
            sourceLabel.AddToClassList("mapping-source");
            sourceLabel.RegisterCallback<ClickEvent>(_ => Reveal(transform));
            row.Add(sourceLabel);

            var arrow = new Label("→");
            arrow.AddToClassList("mapping-arrow");
            row.Add(arrow);

            // Nothing can be suggested here, but the user may know better: e.g. the avatar the target is
            // going to be placed on is in the scene already
            manualMappings.TryGetValue(transform, out var chosen);
            var targetPicker = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
                // The right-hand side always shows what the reference points at afterwards. An empty field
                // next to "kept" would read as if the reference was going to be cleared.
                value = chosen != null ? chosen : transform,
            };
            targetPicker.AddToClassList("mapping-target");
            targetPicker.EnableInClassList("mapping-target--kept", chosen == null);
            targetPicker.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as Transform;
                if (picked != null && picked != transform &&
                    (EditorUtility.IsPersistent(picked) || Hierarchy.IsInside(picked, sourceRoot.transform)))
                {
                    targetPicker.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                // Emptying the field, or picking the object itself, goes back to keeping the reference
                if (picked == null || picked == transform) manualMappings.Remove(transform);
                else manualMappings[transform] = picked;
                Recompute();
            });
            row.Add(targetPicker);

            var note = new Label(Localization.S(chosen != null
                ? "componentCopier.mapping.note.manual"
                : "componentCopier.mapping.note.externalKept"));
            note.AddToClassList("mapping-note");
            row.Add(note);
            row.EnableInClassList("mapping-row--manual", chosen != null);

            return row;
        }

        private static string ScenePath(Transform transform)
        {
            string path = transform.name;
            for (var current = transform.parent; current != null; current = current.parent)
                path = current.name + "/" + path;
            return path;
        }

        private void AddMappingGroup(
            List<TransformMapping> mappings, string titleKey, Dictionary<Transform, PlannedObject> created)
        {
            if (mappings.Count == 0) return;

            if (titleKey != null)
            {
                var title = new Label(Localization.S(titleKey));
                title.AddToClassList("mapping-group-title");
                mappingContainer.Add(title);
            }

            foreach (var mapping in mappings)
            {
                created.TryGetValue(mapping.Source, out var plannedObject);
                mappingContainer.Add(CreateMappingRow(mapping, plannedObject));
            }
        }

        /// <summary>
        /// True for a reference to the outside that found no counterpart although there are surroundings to look
        /// in. Settled once the user picks a replacement or says "keep as is" (both are manual mappings).
        /// </summary>
        private static bool NeedsDecision(TransformMapping mapping, bool external)
        {
            return external && mapping != null && mapping.State == MappingState.Unmapped;
        }

        private VisualElement CreateMappingRow(
            TransformMapping mapping, PlannedObject plannedObject, TransformMap owner = null)
        {
            // Rows about the outside belong to the map of the surroundings; their paths start at its root
            bool external = owner != null;
            owner ??= map;

            var row = new VisualElement();
            row.AddToClassList("mapping-row");
            if (plannedObject != null)
            {
                // Only here while the user picks an existing object for it; still nothing to be alarmed about
                row.AddToClassList("mapping-row--willcreate");
            }
            else if (NeedsDecision(mapping, external))
            {
                // A reference to the outside without a counterpart is kept, so nothing is lost and it does
                // not get the red of an unmapped object. But it keeps pointing at the other avatar, which is
                // rarely what a copy to a new avatar wants: the user should have a look, as with a suggestion.
                row.AddToClassList("mapping-row--needsreview");
            }
            else
            {
                row.AddToClassList("mapping-row--" + mapping.State.ToString().ToLowerInvariant());
            }

            string sourcePath = ObjectMatcher.GetRelativePathFromRoot(mapping.Source, owner.SourceRoot);
            if (external)
                sourcePath = string.IsNullOrEmpty(sourcePath) ? owner.SourceRoot.name : owner.SourceRoot.name + "/" + sourcePath;
            var sourceLabel = WithOverflowTooltip(new Label(sourcePath));
            sourceLabel.AddToClassList("mapping-source");
            sourceLabel.RegisterCallback<ClickEvent>(_ => Reveal(mapping.Source));
            row.Add(sourceLabel);

            var arrow = new Label("→");
            arrow.AddToClassList("mapping-arrow");
            row.Add(arrow);

            // A reference to the outside without a counterpart is kept, so the field shows the object it keeps
            // pointing at. Inside the source an empty field is the truth: that reference gets cleared.
            bool keptAsIs = external && mapping.Target == null;
            var targetPicker = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
                value = keptAsIs ? mapping.Source : mapping.Target,
            };
            targetPicker.AddToClassList("mapping-target");
            targetPicker.EnableInClassList("mapping-target--kept", keptAsIs);
            targetPicker.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as Transform;

                // Picking the object itself is another way of saying "keep it"
                if (external && picked == mapping.Source) picked = null;

                if (picked != null && !Hierarchy.IsInside(picked, owner.TargetRoot))
                {
                    targetPicker.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                SetManualMapping(mapping.Source, picked);
            });
            row.Add(targetPicker);

            var note = new Label(MappingNote(mapping, plannedObject, external));
            note.AddToClassList("mapping-note");
            row.Add(note);

            if (mapping.State == MappingState.NeedsReview)
            {
                var confirmButton = new Button(() => SetManualMapping(mapping.Source, mapping.Target))
                {
                    text = Localization.S("componentCopier.mapping.confirm"),
                };
                confirmButton.AddToClassList("mapping-button");
                row.Add(confirmButton);
            }

            // A confirmed match inside the source needs no second thought. A reference to the outside does:
            // keeping it is a legitimate choice even when a counterpart was found.
            if (external || mapping.State != MappingState.Confirmed)
            {
                var menuButton = new Button { text = "▾" };
                menuButton.clicked += () => ShowMappingMenu(mapping, owner);
                menuButton.AddToClassList("mapping-menu-button");
                menuButton.tooltip = Localization.S("componentCopier.mapping.menu:tooltip");
                row.Add(menuButton);
            }

            return row;
        }

        private void ShowMappingMenu(TransformMapping mapping, TransformMap owner)
        {
            var menu = new GenericMenu();

            foreach (var candidate in mapping.Candidates)
            {
                var target = candidate.Target;
                if (target == null) continue;

                // GenericMenu turns '/' into submenus
                string path = ObjectMatcher.GetRelativePathFromRoot(target, owner.TargetRoot).Replace("/", " ∕ ");
                menu.AddItem(new GUIContent(path), mapping.Target == target,
                    () => SetManualMapping(mapping.Source, target));
            }

            if (mapping.Candidates.Count > 0) menu.AddSeparator("");

            // "No counterpart" clears a reference into the source, but keeps one to the outside as it is
            bool keepsReference = owner != map;
            bool isNone = mapping.State == MappingState.Manual && mapping.Target == null;
            menu.AddItem(
                new GUIContent(Localization.S(keepsReference
                    ? "componentCopier.mapping.menu.keep"
                    : "componentCopier.mapping.menu.none")),
                isNone, () => SetManualMapping(mapping.Source, null));

            if (pickExistingFor.Contains(mapping.Source))
            {
                menu.AddItem(new GUIContent(Localization.S("componentCopier.mapping.menu.createInstead")), false, () =>
                {
                    pickExistingFor.Remove(mapping.Source);
                    RenderMapping();
                });
            }

            if (manualMappings.ContainsKey(mapping.Source))
            {
                menu.AddItem(new GUIContent(Localization.S("componentCopier.mapping.menu.reset")), false, () =>
                {
                    manualMappings.Remove(mapping.Source);
                    // Back to automatic also means back to being created, if that is what automatic says
                    pickExistingFor.Remove(mapping.Source);
                    Recompute();
                });
            }

            menu.ShowAsContext();
        }

        private void SetManualMapping(Transform source, Transform target)
        {
            manualMappings[source] = target;
            Recompute();
        }

        private void ConfirmAll(MappingReason reason)
        {
            foreach (var mapping in CollectRelevantMappings())
            {
                if (mapping.State == MappingState.NeedsReview && mapping.Reason == reason)
                    manualMappings[mapping.Source] = mapping.Target;
            }

            Recompute();
        }

        private string MappingNote(TransformMapping mapping, PlannedObject plannedObject, bool external)
        {
            if (plannedObject != null)
            {
                return Localization.S(plannedObject.IsPrefabRoot
                    ? "componentCopier.mapping.note.prefab"
                    : "componentCopier.mapping.note.willCreate");
            }

            switch (mapping.State)
            {
                case MappingState.Manual when mapping.Target == null && external:
                    return Localization.S("componentCopier.mapping.note.externalKeptManual");
                case MappingState.Manual:
                    return Localization.S(mapping.Target != null
                        ? "componentCopier.mapping.note.manual"
                        : "componentCopier.mapping.note.manualNone");
                case MappingState.Unmapped when external:
                    // Kept, but worth a look: see NeedsDecision
                    return Localization.S("componentCopier.mapping.note.externalNoCounterpart");
                case MappingState.Unmapped:
                    return Localization.S(map.SourceSkeleton.IsBone(mapping.Source)
                        ? "componentCopier.mapping.note.boneMissing"
                        : "componentCopier.mapping.note.unmapped");
                default:
                    return Localization.S("componentCopier.mapping.reason." + Camel(mapping.Reason));
            }
        }
    }
}
