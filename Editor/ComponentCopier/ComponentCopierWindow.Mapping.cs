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
        private List<TransformMapping> CollectRelevantMappings()
        {
            var relevant = new List<Transform>();
            var seen = new HashSet<Transform>();

            void Add(Transform transform)
            {
                if (transform == null || transform == map.SourceRoot) return;
                if (!ReferenceWalker.IsInside(transform, map.SourceRoot)) return;
                if (seen.Add(transform)) relevant.Add(transform);
            }

            foreach (var planned in plan.Components)
            {
                Add(planned.Entry.Host);
                foreach (var reference in planned.References)
                    Add(ReferenceWalker.GetTransform(reference.SourceValue));
            }

            // Listed in Hierarchy order. The order of discovery jumps between a component's object and the
            // objects it references, which makes a row hard to find.
            var hierarchyOrder = new Dictionary<Transform, int>();
            foreach (var transform in map.SourceRoot.GetComponentsInChildren<Transform>(true))
                hierarchyOrder[transform] = hierarchyOrder.Count;

            // Blocked components have no references collected yet, but their host is what needs attention
            return relevant
                .OrderBy(t => hierarchyOrder.TryGetValue(t, out int index) ? index : int.MaxValue)
                .Select(t => map.Get(t))
                .Where(m => m != null)
                .ToList();
        }

        private void RenderMapping()
        {
            mappingContainer.Clear();
            mappingSummaryLabel.text = "";

            if (map == null || plan == null)
            {
                mappingContainer.Add(InfoLabel(targetRoot == null
                    ? "componentCopier.info.selectTarget"
                    : "componentCopier.info.fixTarget"));
                return;
            }

            var mappings = CollectRelevantMappings();
            if (mappings.Count == 0)
            {
                mappingContainer.Add(InfoLabel("componentCopier.info.noMappings"));
                return;
            }

            var created = plan.ObjectsToCreate.ToDictionary(o => o.Source);

            // Objects inside a nested prefab that gets instantiated are not decisions of their own;
            // only the prefab root is listed.
            mappings = mappings
                .Where(m => !created.TryGetValue(m.Source, out var inPrefab) || inPrefab.PrefabRoot == null)
                .ToList();
            var needsReview = mappings.Where(m => m.State == MappingState.NeedsReview).ToList();
            var unmapped = mappings.Where(m => !m.IsUsable && m.State != MappingState.NeedsReview).ToList();
            var manual = mappings.Where(m => m.State == MappingState.Manual && m.IsUsable).ToList();
            var confirmed = mappings.Where(m => m.State == MappingState.Confirmed).ToList();

            // An object that is going to be created is taken care of; only the rest needs the user
            int unresolved = unmapped.Count(m => !created.ContainsKey(m.Source));
            mappingSummaryLabel.text = Localization.S("componentCopier.mapping.summary",
                confirmed.Count + manual.Count, mappings.Count, needsReview.Count, unresolved);
            mappingSummaryLabel.EnableInClassList("section-summary--warning",
                needsReview.Count + unresolved > 0);

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

            if (confirmed.Count > 0)
            {
                var foldout = new Foldout
                {
                    text = Localization.S("componentCopier.mapping.confirmed", confirmed.Count),
                    value = confirmedMappingsExpanded,
                };
                foldout.AddToClassList("confirmed-foldout");
                foldout.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != foldout) return;
                    confirmedMappingsExpanded = evt.newValue;
                    if (evt.newValue && foldout.childCount == 0)
                    {
                        foreach (var mapping in confirmed)
                            foldout.Add(CreateMappingRow(mapping, null));
                    }
                });
                if (confirmedMappingsExpanded)
                {
                    foreach (var mapping in confirmed)
                        foldout.Add(CreateMappingRow(mapping, null));
                }

                mappingContainer.Add(foldout);
            }
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

        private VisualElement CreateMappingRow(TransformMapping mapping, PlannedObject plannedObject)
        {
            var row = new VisualElement();
            row.AddToClassList("mapping-row");
            row.AddToClassList("mapping-row--" + mapping.State.ToString().ToLowerInvariant());

            string sourcePath = ObjectMatcher.GetRelativePathFromRoot(mapping.Source, map.SourceRoot);
            var sourceLabel = new Label(sourcePath) { tooltip = sourcePath };
            sourceLabel.AddToClassList("mapping-source");
            sourceLabel.RegisterCallback<ClickEvent>(_ => Reveal(mapping.Source));
            row.Add(sourceLabel);

            var arrow = new Label("→");
            arrow.AddToClassList("mapping-arrow");
            row.Add(arrow);

            var targetPicker = new ObjectField
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
                value = mapping.Target,
            };
            targetPicker.AddToClassList("mapping-target");
            targetPicker.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as Transform;
                if (picked != null && !ReferenceWalker.IsInside(picked, map.TargetRoot))
                {
                    targetPicker.SetValueWithoutNotify(evt.previousValue);
                    return;
                }

                SetManualMapping(mapping.Source, picked);
            });
            row.Add(targetPicker);

            var note = new Label(MappingNote(mapping, plannedObject));
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

            if (mapping.State != MappingState.Confirmed)
            {
                var menuButton = new Button { text = "▾" };
                menuButton.clicked += () => ShowMappingMenu(mapping);
                menuButton.AddToClassList("mapping-menu-button");
                menuButton.tooltip = Localization.S("componentCopier.mapping.menu:tooltip");
                row.Add(menuButton);
            }

            return row;
        }

        private void ShowMappingMenu(TransformMapping mapping)
        {
            var menu = new GenericMenu();

            foreach (var candidate in mapping.Candidates)
            {
                var target = candidate.Target;
                if (target == null) continue;

                // GenericMenu turns '/' into submenus
                string path = ObjectMatcher.GetRelativePathFromRoot(target, map.TargetRoot).Replace("/", " ∕ ");
                menu.AddItem(new GUIContent(path), mapping.Target == target,
                    () => SetManualMapping(mapping.Source, target));
            }

            if (mapping.Candidates.Count > 0) menu.AddSeparator("");

            menu.AddItem(new GUIContent(Localization.S("componentCopier.mapping.menu.none")), false,
                () => SetManualMapping(mapping.Source, null));

            if (manualMappings.ContainsKey(mapping.Source))
            {
                menu.AddItem(new GUIContent(Localization.S("componentCopier.mapping.menu.reset")), false, () =>
                {
                    manualMappings.Remove(mapping.Source);
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

        private string MappingNote(TransformMapping mapping, PlannedObject plannedObject)
        {
            if (plannedObject != null)
            {
                return Localization.S(plannedObject.IsPrefabRoot
                    ? "componentCopier.mapping.note.prefab"
                    : "componentCopier.mapping.note.willCreate");
            }

            switch (mapping.State)
            {
                case MappingState.Manual:
                    return Localization.S(mapping.Target != null
                        ? "componentCopier.mapping.note.manual"
                        : "componentCopier.mapping.note.manualNone");
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
