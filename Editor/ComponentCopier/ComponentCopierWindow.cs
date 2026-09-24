using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// Copies components between hierarchies and redirects the references inside them.
    /// The window only gathers input and renders the <see cref="CopySession"/>; all decisions are made in the
    /// Core classes.
    /// </summary>
    public partial class ComponentCopierWindow : EditorWindow
    {
        internal const string UssPath =
            "Packages/net.kanameliser.editor-plus/Editor/ComponentCopier/ComponentCopierWindow.uss";
        private const long RefreshDebounceMs = 400;

        [SerializeField] private CopySession.Inputs inputs = new();
        private CopySession session;

        private readonly HashSet<string> expandedTypes = new();

        private VisualElement listContainer;
        private VisualElement mappingContainer;
        private VisualElement reportContainer;
        private Button applyButton;
        private Button diffButton;
        private bool refreshScheduled;
        private IVisualElementScheduledItem scheduledRefresh;

        [MenuItem("Tools/Kanameliser Editor Plus/Component Copier")]
        public static void ShowWindow() => Open();

        /// <summary>
        /// Opens the window and fills in what the caller already knows. With <paramref name="only"/>, just
        /// that component is checked after the scan, see <see cref="CopySession.SetSource"/>.
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

        private void OnEnable()
        {
            // Created here rather than in CreateGUI: Open fills in the roots before the window has its GUI.
            // Kept if the window is enabled again without a domain reload, which leaves its GUI in place.
            session ??= new CopySession(inputs, CopySettings.Load());
            groupMode = (GroupMode)EditorPrefs.GetInt(GroupModePrefsKey, (int)GroupMode.ByType);

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

            // A source that Open filled in before the window had its GUI was scanned then, with the choices it
            // asked for. They are kept, but the scene may have changed since.
            session.Rescan(resetSelection: !session.HasScanned);
            RenderAll();
        }

        private void OnLanguageChanged()
        {
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
            session.InvalidateMaps();
            if (refreshScheduled || rootVisualElement == null || listContainer == null) return;
            if (session.SourceRoot == null && session.TargetRoot == null) return;

            refreshScheduled = true;
            scheduledRefresh = rootVisualElement.schedule.Execute(() =>
            {
                refreshScheduled = false;
                Rescan();
            }).StartingIn(RefreshDebounceMs);
        }

        /// <summary>Drops the refresh that waits for the debounce, for a caller that rescans right away.</summary>
        private void CancelScheduledRefresh()
        {
            scheduledRefresh?.Pause();
            refreshScheduled = false;
        }

        /// <summary>Scans the source again and keeps the choices, see <see cref="CopySession.Rescan"/>.</summary>
        private void Rescan()
        {
            session.Rescan(resetSelection: false);
            RenderAll();
        }

        /// <summary>Applies a change to the settings, keeps it for the next session and plans with it.</summary>
        private void ChangeSettings(Action<CopySettings> change)
        {
            session.ChangeSettings(change);
            session.Settings.Save();
            RenderAll();
        }

        private void RenderAll()
        {
            if (listContainer == null) return;

            UpdateSourceWarnings();
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
                "componentCopier.settings.existing", policies, session.Settings.ExistingPolicy,
                PolicyLabel, PolicyLabel);
            policyField.AddToClassList("settings-policy");
            policyField.AddToClassList("ndmf-tr");
            policyField.RegisterValueChangedCallback(evt => ChangeSettings(s => s.ExistingPolicy = evt.newValue));
            settingsFields.Add(policyField);

            createObjectsToggle = new Toggle("componentCopier.settings.createObjects")
            {
                value = session.Settings.CreateMissingObjects,
            };
            createObjectsToggle.AddToClassList("settings-create");
            createObjectsToggle.AddToClassList("ndmf-tr");
            createObjectsToggle.RegisterValueChangedCallback(evt =>
                ChangeSettings(s => s.CreateMissingObjects = evt.newValue));
            var unresolvedPolicies = new List<UnresolvedReferencePolicy>
            {
                UnresolvedReferencePolicy.Clear,
                UnresolvedReferencePolicy.SkipComponent,
            };
            unresolvedPolicyField = new PopupField<UnresolvedReferencePolicy>(
                "componentCopier.settings.unresolved", unresolvedPolicies, session.Settings.UnresolvedPolicy,
                UnresolvedPolicyLabel, UnresolvedPolicyLabel);
            unresolvedPolicyField.AddToClassList("settings-policy");
            unresolvedPolicyField.AddToClassList("ndmf-tr");
            unresolvedPolicyField.RegisterValueChangedCallback(evt =>
                ChangeSettings(s => s.UnresolvedPolicy = evt.newValue));
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
                if (IsClickOn(evt, checkbox)) return;
                Reveal(target);
            });
        }

        /// <summary>True when a click that bubbled up to a row or header landed on <paramref name="element"/>.</summary>
        private static bool IsClickOn(ClickEvent evt, VisualElement element) =>
            evt.target is VisualElement target && (target == element || element.Contains(target));

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

            var plan = session.Plan;
            // Adding a prefab is work too, even when no component is selected
            bool hasWork = plan != null &&
                           (plan.Components.Any(c => c.WillWrite) || plan.ObjectsToCreate.Count > 0);
            applyButton.SetEnabled(hasWork && !session.IsTargetAsset);
            diffButton.SetEnabled(plan != null && (plan.Components.Count > 0 || plan.ObjectsToCreate.Count > 0));
        }

        #endregion
    }
}
