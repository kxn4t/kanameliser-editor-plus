using System.Collections.Generic;
using System.Linq;
using Kanameliser.Editor.MAMaterialHelper.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Copies components between hierarchies and redirects the references inside them.
    /// The window only gathers input and renders the model; all decisions are made in the Core classes.
    /// </summary>
    public partial class ComponentCopierWindow : EditorWindow
    {
        internal const string UssPath =
            "Packages/net.kanameliser.editor-plus/Editor/ComponentCopier/ComponentCopierWindow.uss";
        private const long RefreshDebounceMs = 400;

        [SerializeField] private GameObject sourceRoot;
        [SerializeField] private GameObject targetRoot;
        // Mirror copy: the components of one side of the source go to the other side of the same hierarchy
        [SerializeField] private bool mirrorMode;
        [SerializeField] private Side mirrorSide = Side.Left;

        private CopySettings settings;
        private List<ComponentEntry> entries = new();
        private readonly HashSet<ComponentKey> selectedKeys = new();
        // Components the user unchecked while they were about to arrive with a nested prefab, see SetChecked
        private readonly HashSet<ComponentKey> leftOutKeys = new();
        private readonly HashSet<string> expandedTypes = new();
        // Nested prefabs and empty objects the user chose to add although no selected component needs them
        private readonly HashSet<string> selectedObjectPaths = new();
        private readonly List<Transform> missingObjects = new();
        private readonly Dictionary<Transform, Transform> manualMappings = new();

        private TransformMap map;
        private CopyPlan plan;

        // Cache of the map for references that point outside of the source, see GetExternalMap
        private TransformMap externalMap;
        private HashSet<Transform> externalMapScope;
        private int externalMapSignature;
        private TransformMap mirrorMap;
        private int mirrorMapSignature;
        private int rescanCount;
        private readonly Dictionary<ComponentKey, PlannedComponent> plannedByKey = new();

        private VisualElement listContainer;
        private VisualElement mappingContainer;
        private VisualElement reportContainer;
        private Button applyButton;
        private Button diffButton;
        private bool refreshScheduled;
        // A component the next rescan narrows the selection down to, see Open
        private Component pendingOnlyComponent;

        [MenuItem("Tools/Kanameliser Editor Plus/Component Copier")]
        public static void ShowWindow() => Open();

        /// <summary>
        /// Opens the window and fills in what the caller already knows. With <paramref name="only"/>, just
        /// that component is checked after the scan, so a single component can be brought over from its
        /// context menu without searching the list for it.
        /// </summary>
        internal static ComponentCopierWindow Open(
            GameObject source = null, GameObject target = null, Component only = null)
        {
            var window = GetWindow<ComponentCopierWindow>();
            window.titleContent = new GUIContent("Component Copier");
            window.minSize = new Vector2(480, 520);

            if (source != null) window.SetSource(source, only);
            if (target != null) window.SetTarget(target);
            return window;
        }

        /// <summary>
        /// The fields may not exist yet when the window was just created: CreateGUI then picks the roots up
        /// through its own rescan, so only the callbacks of an existing field are run here.
        /// </summary>
        private void SetSource(GameObject source, Component only)
        {
            pendingOnlyComponent = only;
            // A component on the middle line has no other side to go to, so it asks for an ordinary copy
            if (mirrorMode && only != null && new MirrorSides(MirrorRootOf(source.transform)).Of(only.transform) == Side.None)
                LeaveMirrorMode();

            sourceField?.SetValueWithoutNotify(source);
            if (sourceField != null) OnSourceChanged(source);
            else sourceRoot = source;
        }

        private void SetTarget(GameObject target)
        {
            targetField?.SetValueWithoutNotify(target);
            if (targetField != null)
            {
                OnTargetChanged(target);
                return;
            }

            // Same request as a target picked in the field, see OnTargetChanged
            targetRoot = target;
            if (target != null) LeaveMirrorMode();
        }

        private void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            EditorApplication.hierarchyChanged += ScheduleRefresh;
            Undo.undoRedoPerformed += ScheduleRefresh;
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            EditorApplication.hierarchyChanged -= ScheduleRefresh;
            Undo.undoRedoPerformed -= ScheduleRefresh;
        }

        public void CreateGUI()
        {
            settings = CopySettings.Load();

            var root = rootVisualElement;
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList("component-copier");

            var langSwitcher = new IMGUIContainer(Localization.ShowLanguageUI);
            langSwitcher.AddToClassList("language-switcher");
            root.Add(langSwitcher);

            CreateSourceSection(root);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("main-scroll");
            root.Add(scrollView);

            CreateComponentListSection(scrollView);
            CreateMappingSection(scrollView);
            CreateReportSection(scrollView);

            CreateFooter(root);

            Localization.LocalizeUIElements(root);
            // Texts built with S() are not tracked by NDMF, so they are re-rendered on language change
            Localization.RegisterLanguageChangeCallback(this, w => w.OnLanguageChanged());

            Rescan(resetSelection: true);
        }

        private void OnLanguageChanged()
        {
            UpdateSourceWarnings();
            UpdateFooterTexts();
            RenderAll();
        }

        #region Model

        private void OnObjectChangesPublished(ref ObjectChangeEventStream stream) => ScheduleRefresh();

        /// <summary>
        /// Scene edits arrive in bursts (and our own Apply raises them too), so refreshes are debounced.
        /// </summary>
        private void ScheduleRefresh()
        {
            // Right away, not with the refresh: a checkbox ticked in the meantime must not plan with a map
            // that still holds deleted objects
            mirrorMap = null;
            externalMapScope = null;
            if (refreshScheduled || rootVisualElement == null || listContainer == null) return;
            if (sourceRoot == null && targetRoot == null) return;

            refreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                refreshScheduled = false;
                Rescan(resetSelection: false);
            }).StartingIn(RefreshDebounceMs);
        }

        /// <summary>
        /// Rescans the source. Selection is carried over by <see cref="ComponentKey"/>; components that appear
        /// for the first time follow the default selection rule.
        /// </summary>
        private void Rescan(bool resetSelection)
        {
            rescanCount++;
            var previousKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            entries = sourceRoot != null ? ComponentScanner.Scan(sourceRoot.transform) : new List<ComponentEntry>();
            if (mirrorMode && sourceRoot != null)
            {
                // Built here rather than on the Recompute below, which reuses it
                var sides = GetMirrorMap().Sides;

                // A component picked from its context menu decides the direction
                if (pendingOnlyComponent != null) AdoptSideOf(pendingOnlyComponent.transform, sides);

                // Only the chosen side is on offer; objects on the middle line have no other side to go to
                entries = entries.Where(e => sides.Of(e.Host) == mirrorSide).ToList();
            }

            if (resetSelection)
            {
                selectedKeys.Clear();
                leftOutKeys.Clear();
                selectedObjectPaths.Clear();
                expandedIssues.Clear();
                previousKeys.Clear();
            }

            var currentKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            selectedKeys.IntersectWith(currentKeys);
            leftOutKeys.IntersectWith(currentKeys);
            foreach (var entry in entries)
            {
                if (!previousKeys.Contains(entry.Key) && entry.Category != ComponentCategory.ExcludedByDefault)
                    selectedKeys.Add(entry.Key);
            }

            if (pendingOnlyComponent != null)
            {
                var only = entries.FirstOrDefault(e => e.Component == pendingOnlyComponent);
                pendingOnlyComponent = null;
                // Nothing else is checked even when it is not listed: the user asked for that one component
                selectedKeys.Clear();
                if (only != null)
                {
                    selectedKeys.Add(only.Key);
                    expandedTypes.Add(GroupId(only));
                }
            }

            // Drop manual mappings whose objects were deleted. A genuinely null value is kept: it means
            // "no counterpart", whereas a destroyed target only compares equal to null.
            var dead = manualMappings
                .Where(p => p.Key == null || (!ReferenceEquals(p.Value, null) && p.Value == null))
                .Select(p => p.Key)
                .ToList();
            foreach (var key in dead)
                manualMappings.Remove(key);

            Recompute();
        }

        private void Recompute()
        {
            map = null;
            plan = null;
            plannedByKey.Clear();
            missingObjects.Clear();

            if (sourceRoot != null && (mirrorMode || (targetRoot != null && IsTargetUsable())))
            {
                map = mirrorMode
                    ? GetMirrorMap()
                    : TransformMapper.Build(sourceRoot.transform, targetRoot.transform, manualMappings);
                // The mirror map spans the avatar; only the chosen side of the source is on offer
                var scope = mirrorMode ? sourceRoot.transform : null;
                missingObjects.AddRange(NestedPrefabs.FindMissingRoots(map, scope));
                missingObjects.AddRange(MissingObjects.FindRoots(map, scope));
                if (mirrorMode) missingObjects.RemoveAll(t => map.Sides.Of(t) != mirrorSide);
                plan = BuildPlan(settings);

                // A mirror copy can bring a prefab from elsewhere in the avatar. Its components are keyed from
                // the avatar, and such a key could name a component of the source.
                foreach (var planned in plan.Components)
                {
                    if (Hierarchy.IsInside(planned.Entry.Host, sourceRoot.transform))
                        plannedByKey[planned.Entry.Key] = planned;
                }
            }

            RenderAll();
        }

        private CopyPlan BuildPlan(CopySettings planSettings)
        {
            // A mirror copy stays inside one avatar, so there are no surroundings to redirect to
            return CopyPlanBuilder.Build(
                entries.Where(e => selectedKeys.Contains(e.Key)), map, planSettings,
                missingObjects.Where(p => selectedObjectPaths.Contains(ObjectPath(p))),
                mirrorMode ? null : GetExternalMap, leftOutKeys,
                mirrorMode ? map.SourceRoot : null, sourceRoot.transform);
        }

        /// <summary>
        /// The hierarchy a mirror copy works in: the avatar the source sits on, so that references to the
        /// bones and colliders of the avatar are mirrored too. Outside of an avatar, the outermost model (an
        /// object with an Animator), else the topmost parent: a part such as "UpperLeg_L" has no other side of
        /// its own. The model comes first because an organizing object above it ("Avatars") need not sit on
        /// its middle line, and the outermost one because an outfit FBX on the model has an Animator too.
        /// </summary>
        private Transform MirrorRoot() => sourceRoot != null ? MirrorRootOf(sourceRoot.transform) : null;

        private static Transform MirrorRootOf(Transform source)
        {
            var avatarRoot = AvatarObjectReferences.FindAvatarRoot(source);
            if (avatarRoot != null) return avatarRoot;

            Transform model = null;
            for (var current = source; current != null; current = current.parent)
            {
                if (current.GetComponent<Animator>() != null) model = current;
            }

            return model != null ? model : source.root;
        }

        /// <summary>
        /// The mirror map spans the whole avatar, so like the map of the surroundings it is kept while nothing
        /// it depends on changes. Ticking a checkbox only rebuilds the plan.
        /// </summary>
        private TransformMap GetMirrorMap()
        {
            var mirrorRoot = MirrorRoot();
            int signature = System.HashCode.Combine(mirrorRoot.GetInstanceID(), rescanCount, ManualMappingSignature());
            if (mirrorMap == null || signature != mirrorMapSignature)
            {
                mirrorMap = MirrorMapper.Build(mirrorRoot, manualMappings);
                mirrorMapSignature = signature;
            }

            return mirrorMap;
        }

        /// <summary>The object that receives the copy: the source itself in a mirror copy.</summary>
        private GameObject EffectiveTarget => mirrorMode ? sourceRoot : targetRoot;

        /// <summary>
        /// The map of the surroundings spans whole avatars, so it is kept while nothing it depends on changes.
        /// Ticking a checkbox rebuilds the plan, but usually asks for objects that are resolved already.
        /// </summary>
        private TransformMap GetExternalMap(IReadOnlyCollection<Transform> referenced)
        {
            int signature = System.HashCode.Combine(
                sourceRoot.GetInstanceID(), targetRoot.GetInstanceID(), rescanCount, ManualMappingSignature());

            if (externalMapScope == null || signature != externalMapSignature ||
                !externalMapScope.IsSupersetOf(referenced))
            {
                externalMapScope = new HashSet<Transform>(referenced);
                externalMapSignature = signature;
                externalMap = ExternalContext.BuildMap(
                    sourceRoot.transform, targetRoot.transform, referenced, manualMappings);
            }

            return externalMap;
        }

        private int ManualMappingSignature()
        {
            int signature = manualMappings.Count;
            foreach (var pair in manualMappings)
            {
                // Destroyed objects compare equal to null but still have an instance id
                int key = ReferenceEquals(pair.Key, null) ? 0 : pair.Key.GetInstanceID();
                int value = ReferenceEquals(pair.Value, null) ? 0 : pair.Value.GetInstanceID();
                signature ^= System.HashCode.Combine(key, value);
            }

            return signature;
        }

        /// <summary>The objects chosen for adding are kept by path so that they survive a rescan.</summary>
        private string ObjectPath(Transform source) =>
            ObjectMatcher.GetRelativePathFromRoot(source, sourceRoot.transform);

        private void RenderAll()
        {
            if (listContainer == null) return;

            RenderComponentList();
            RenderMapping();
            RenderReport();
            UpdateFooterState();
        }

        #endregion

        #region Footer

        private PopupField<ExistingComponentPolicy> policyField;
        private PopupField<UnresolvedReferencePolicy> unresolvedPolicyField;
        private Toggle createObjectsToggle;

        private void CreateFooter(VisualElement root)
        {
            var footer = new VisualElement();
            footer.AddToClassList("footer");
            root.Add(footer);

            // The two popups stack in a column of their own so that they share one width and one label
            // column, with the toggle centered beside them
            var settingsRow = new VisualElement();
            settingsRow.AddToClassList("settings-row");
            footer.Add(settingsRow);

            var settingsFields = new VisualElement();
            settingsFields.AddToClassList("settings-fields");
            settingsRow.Add(settingsFields);

            var policies = new List<ExistingComponentPolicy>
            {
                ExistingComponentPolicy.Overwrite,
                ExistingComponentPolicy.Add,
                ExistingComponentPolicy.Skip,
                ExistingComponentPolicy.Replace,
            };
            policyField = new PopupField<ExistingComponentPolicy>(
                "componentCopier.settings.existing", policies, settings.ExistingPolicy, PolicyLabel, PolicyLabel);
            policyField.AddToClassList("settings-policy");
            policyField.AddToClassList("ndmf-tr");
            policyField.RegisterValueChangedCallback(evt =>
            {
                settings.ExistingPolicy = evt.newValue;
                settings.Save();
                Recompute();
            });
            settingsFields.Add(policyField);

            createObjectsToggle = new Toggle("componentCopier.settings.createObjects")
            {
                value = settings.CreateMissingObjects,
            };
            createObjectsToggle.AddToClassList("settings-create");
            createObjectsToggle.AddToClassList("ndmf-tr");
            createObjectsToggle.RegisterValueChangedCallback(evt =>
            {
                settings.CreateMissingObjects = evt.newValue;
                settings.Save();
                Recompute();
            });
            var unresolvedPolicies = new List<UnresolvedReferencePolicy>
            {
                UnresolvedReferencePolicy.Clear,
                UnresolvedReferencePolicy.SkipComponent,
            };
            unresolvedPolicyField = new PopupField<UnresolvedReferencePolicy>(
                "componentCopier.settings.unresolved", unresolvedPolicies, settings.UnresolvedPolicy,
                UnresolvedPolicyLabel, UnresolvedPolicyLabel);
            unresolvedPolicyField.AddToClassList("settings-policy");
            unresolvedPolicyField.AddToClassList("ndmf-tr");
            unresolvedPolicyField.RegisterValueChangedCallback(evt =>
            {
                settings.UnresolvedPolicy = evt.newValue;
                settings.Save();
                Recompute();
            });
            settingsFields.Add(unresolvedPolicyField);

            settingsRow.Add(createObjectsToggle);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("button-row");
            footer.Add(buttonRow);

            diffButton = new Button(RunDiffCheck);
            diffButton.AddToClassList("diff-button");
            buttonRow.Add(diffButton);

            applyButton = new Button(Apply);
            applyButton.AddToClassList("apply-button");
            buttonRow.Add(applyButton);

            UpdateFooterTexts();
        }

        private static string PolicyLabel(ExistingComponentPolicy policy)
        {
            return Localization.S(ComponentCopierStrings.PolicyKey(policy));
        }

        private static string UnresolvedPolicyLabel(UnresolvedReferencePolicy policy)
        {
            return Localization.S(ComponentCopierStrings.UnresolvedPolicyKey(policy));
        }

        /// <summary>
        /// Shows an object that was clicked in the window: pings it and selects it, so the Inspector follows
        /// and the components can be checked right away.
        /// </summary>
        private static void Reveal(Object target)
        {
            if (target == null) return;

            Selection.activeObject = target is Component component ? component.gameObject : target;
            EditorGUIUtility.PingObject(target);
        }

        /// <summary>
        /// Makes the whole row reveal its object, except for the checkbox. The label alone is only as wide as
        /// its text (see <see cref="WithOverflowTooltip"/>), which would leave most of the row dead.
        /// </summary>
        private static void RevealOnRowClick(VisualElement row, VisualElement checkbox, Object target)
        {
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement element && (element == checkbox || checkbox.Contains(element))) return;
                Reveal(target);
            });
        }

        /// <summary>
        /// Shows the label's own text as a tooltip, but only while the label cuts it off. UI Toolkit centers a
        /// tooltip on its element, so a wide label with a short text shows it far away from the text and the
        /// cursor, where it only repeats what is readable anyway.
        /// </summary>
        private static Label WithOverflowTooltip(Label label)
        {
            label.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float textWidth = label.MeasureTextSize(
                    label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                label.tooltip = textWidth > label.contentRect.width + 0.5f ? label.text : "";
            });
            return label;
        }

        private void UpdateFooterTexts()
        {
            if (applyButton == null) return;

            diffButton.text = Localization.S("componentCopier.diffOnly");
            diffButton.tooltip = Localization.S("componentCopier.diffOnly:tooltip");
            applyButton.text = Localization.S("componentCopier.apply");
            policyField.tooltip = Localization.S("componentCopier.settings.existing:tooltip");
            createObjectsToggle.tooltip = Localization.S("componentCopier.settings.createObjects:tooltip");
            unresolvedPolicyField.tooltip = Localization.S("componentCopier.settings.unresolved:tooltip");
            // PopupField caches its formatted text
            policyField.SetValueWithoutNotify(policyField.value);
            unresolvedPolicyField.SetValueWithoutNotify(unresolvedPolicyField.value);
        }

        private void UpdateFooterState()
        {
            if (applyButton == null) return;

            // Adding a prefab is work too, even when no component is selected
            bool hasWork = plan != null &&
                           (plan.Components.Any(c => c.WillWrite) || plan.ObjectsToCreate.Count > 0);
            applyButton.SetEnabled(hasWork && !IsTargetAsset());
            diffButton.SetEnabled(plan != null && (plan.Components.Count > 0 || plan.ObjectsToCreate.Count > 0));
        }

        #endregion
    }
}
