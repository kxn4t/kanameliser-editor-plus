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
        private const string UssPath =
            "Packages/net.kanameliser.editor-plus/Editor/ComponentCopier/ComponentCopierWindow.uss";
        private const long RefreshDebounceMs = 400;

        [SerializeField] private GameObject sourceRoot;
        [SerializeField] private GameObject targetRoot;

        private CopySettings settings;
        private List<ComponentEntry> entries = new();
        private readonly HashSet<ComponentKey> selectedKeys = new();
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
        private int rescanCount;
        private readonly Dictionary<ComponentKey, PlannedComponent> plannedByKey = new();

        private VisualElement listContainer;
        private VisualElement mappingContainer;
        private VisualElement reportContainer;
        private Button applyButton;
        private Button diffButton;
        private bool refreshScheduled;

        [MenuItem("Tools/Kanameliser Editor Plus/Component Copier")]
        public static void ShowWindow()
        {
            var window = GetWindow<ComponentCopierWindow>();
            window.titleContent = new GUIContent("Component Copier");
            window.minSize = new Vector2(480, 520);
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

            if (resetSelection)
            {
                selectedKeys.Clear();
                selectedObjectPaths.Clear();
                previousKeys.Clear();
            }

            var currentKeys = new HashSet<ComponentKey>(entries.Select(e => e.Key));
            selectedKeys.IntersectWith(currentKeys);
            foreach (var entry in entries)
            {
                if (!previousKeys.Contains(entry.Key) && entry.Category != ComponentCategory.ExcludedByDefault)
                    selectedKeys.Add(entry.Key);
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

            if (sourceRoot != null && targetRoot != null && IsTargetUsable())
            {
                map = TransformMapper.Build(sourceRoot.transform, targetRoot.transform, manualMappings);
                missingObjects.AddRange(NestedPrefabs.FindMissingRoots(map));
                missingObjects.AddRange(MissingObjects.FindRoots(map));
                plan = BuildPlan(settings);

                foreach (var planned in plan.Components)
                    plannedByKey[planned.Entry.Key] = planned;
            }

            RenderAll();
        }

        private CopyPlan BuildPlan(CopySettings planSettings)
        {
            return CopyPlanBuilder.Build(
                entries.Where(e => selectedKeys.Contains(e.Key)), map, planSettings,
                missingObjects.Where(p => selectedObjectPaths.Contains(ObjectPath(p))),
                GetExternalMap);
        }

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
        private Toggle createObjectsToggle;

        private void CreateFooter(VisualElement root)
        {
            var footer = new VisualElement();
            footer.AddToClassList("footer");
            root.Add(footer);

            var settingsRow = new VisualElement();
            settingsRow.AddToClassList("settings-row");
            footer.Add(settingsRow);

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
            settingsRow.Add(policyField);

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
            return Localization.S("componentCopier.policy." + Camel(policy));
        }

        /// <summary>Enum value as a localization key segment ("SkipIdentical" becomes "skipIdentical").</summary>
        private static string Camel(System.Enum value)
        {
            string name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
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

        private void UpdateFooterTexts()
        {
            if (applyButton == null) return;

            diffButton.text = Localization.S("componentCopier.diffOnly");
            diffButton.tooltip = Localization.S("componentCopier.diffOnly:tooltip");
            applyButton.text = Localization.S("componentCopier.apply");
            policyField.tooltip = Localization.S("componentCopier.settings.existing:tooltip");
            createObjectsToggle.tooltip = Localization.S("componentCopier.settings.createObjects:tooltip");
            // PopupField caches its formatted text
            policyField.SetValueWithoutNotify(policyField.value);
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
