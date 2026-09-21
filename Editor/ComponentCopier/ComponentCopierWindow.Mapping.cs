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
            var external = CollectExternalReferences();
            if (mappings.Count == 0 && external.Count == 0)
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

            // An object that is going to be created is taken care of; only the rest needs the user.
            // References to the outside only count when a suggestion waits for an answer: without a
            // counterpart they are simply kept, which is no problem to solve.
            int unresolved = unmapped.Count(m => !created.ContainsKey(m.Source));
            int toReview = needsReview.Count +
                           external.Count(e => plan.ExternalMap?.Get(e.Target)?.State == MappingState.NeedsReview);
            mappingSummaryLabel.text = Localization.S("componentCopier.mapping.summary",
                confirmed.Count + manual.Count, mappings.Count, toReview, unresolved);
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

            AddExternalGroup(external);
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
                    external.Holders.Add((planned, reference.PropertyPath));
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
            bool canRedirect = ExternalContext.CanRedirect(sourceRoot.transform, targetRoot.transform);

            var titleRow = new VisualElement();
            titleRow.AddToClassList("mapping-group-title-row");
            mappingContainer.Add(titleRow);

            var title = new Label(owner != null
                ? Localization.S("componentCopier.mapping.group.external", owner.SourceRoot.name, owner.TargetRoot.name)
                : Localization.S("componentCopier.mapping.group.externalKept"))
            {
                tooltip = Localization.S(canRedirect
                    ? "componentCopier.mapping.group.external:tooltip"
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
                mappingContainer.Add(InfoLabel("componentCopier.mapping.external.noSurroundings"));
            }

            foreach (var reference in external)
            {
                var mapping = owner != null && ReferenceWalker.IsInside(reference.Target, owner.SourceRoot)
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

            var label = new Label(Localization.S("componentCopier.mapping.external.holders", text))
            {
                // The full list, with the properties, for the cases the line has no room for
                tooltip = string.Join("\n", holders.Select(h =>
                    Describe(h.component) + "\n    " + string.Join("\n    ", h.properties))),
            };
            label.AddToClassList("mapping-holders");

            var first = holders[0].component.Entry.Host;
            label.RegisterCallback<ClickEvent>(_ => Reveal(first));
            return label;
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
            var sourceLabel = new Label(path) { tooltip = path };
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
                    (EditorUtility.IsPersistent(picked) || ReferenceWalker.IsInside(picked, sourceRoot.transform)))
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

        private VisualElement CreateMappingRow(
            TransformMapping mapping, PlannedObject plannedObject, TransformMap owner = null)
        {
            var row = new VisualElement();
            row.AddToClassList("mapping-row");
            row.AddToClassList("mapping-row--" + mapping.State.ToString().ToLowerInvariant());

            // Rows about the outside belong to the map of the surroundings; their paths start at its root
            bool external = owner != null;
            owner ??= map;

            string sourcePath = ObjectMatcher.GetRelativePathFromRoot(mapping.Source, owner.SourceRoot);
            if (external)
                sourcePath = string.IsNullOrEmpty(sourcePath) ? owner.SourceRoot.name : owner.SourceRoot.name + "/" + sourcePath;
            var sourceLabel = new Label(sourcePath) { tooltip = sourcePath };
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

                if (picked != null && !ReferenceWalker.IsInside(picked, owner.TargetRoot))
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
                    // Not a problem to solve: the object stays where it is, so the reference stays valid
                    return Localization.S("componentCopier.mapping.note.externalKept");
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
