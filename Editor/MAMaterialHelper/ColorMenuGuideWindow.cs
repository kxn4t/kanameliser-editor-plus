using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Kanameliser.Editor.MAMaterialHelper.Common;
using Kanameliser.EditorPlus;

namespace Kanameliser.Editor.MAMaterialHelper
{
    /// <summary>
    /// Guide window explaining the two-step Color Menu workflow,
    /// with a live view of the currently copied color data
    /// </summary>
    public class ColorMenuGuideWindow : EditorWindow
    {
        private const string StyleSheetPath =
            "Packages/net.kanameliser.editor-plus/Editor/MAMaterialHelper/ColorMenuGuideWindow.uss";
        private const long StatusPollIntervalMs = 500;

        private VisualElement statusCard;
        private Label statusLabel;
        private ScrollView copiedListView;
        private CopiedMaterialData lastSeenData;
        private int copiedObjectCount;
        private int copiedMaterialCount;

        public static void ShowWindow()
        {
            var window = GetWindow<ColorMenuGuideWindow>();
            window.titleContent = new GUIContent("How to Create Color Menu");
            window.minSize = new Vector2(380, 490);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            var container = new VisualElement();
            container.AddToClassList("guide-container");
            root.Add(container);

            // Language switcher at top
            var langSwitcher = new IMGUIContainer(Localization.ShowLanguageUI);
            langSwitcher.AddToClassList("language-switcher");
            container.Add(langSwitcher);

            var title = new Label("maMaterialHelper.guide.title");
            title.AddToClassList("guide-title");
            title.AddToClassList("ndmf-tr");
            container.Add(title);

            container.Add(CreateStepCard("1", "maMaterialHelper.guide.step1"));
            container.Add(CreateStepCard("2", "maMaterialHelper.guide.step2"));

            statusCard = new VisualElement();
            statusCard.AddToClassList("status-card");

            statusLabel = new Label();
            statusLabel.AddToClassList("status-label");
            statusCard.Add(statusLabel);

            copiedListView = new ScrollView(ScrollViewMode.Vertical);
            copiedListView.AddToClassList("copied-list");
            statusCard.Add(copiedListView);

            container.Add(statusCard);

            container.Add(CreatePerformanceNote());

            // Polled so the view follows copy operations and language changes
            UpdateStatus();
            statusCard.schedule.Execute(UpdateStatus).Every(StatusPollIntervalMs);

            Localization.LocalizeUIElements(root);
        }

        private static VisualElement CreatePerformanceNote()
        {
            var card = new VisualElement();
            card.AddToClassList("perf-card");

            var title = new Label("maMaterialHelper.guide.perfTitle");
            title.AddToClassList("perf-title");
            title.AddToClassList("ndmf-tr");
            card.Add(title);

            // The merge command only exists when AAO is installed, so guide to it accordingly
#if AVATAR_OPTIMIZER_INSTALLED
            var text = new Label("maMaterialHelper.guide.perfNote");
#else
            var text = new Label("maMaterialHelper.guide.perfNoteNoAao");
#endif
            text.AddToClassList("perf-text");
            text.AddToClassList("ndmf-tr");
            card.Add(text);

            return card;
        }

        private static VisualElement CreateStepCard(string number, string textKey)
        {
            var card = new VisualElement();
            card.AddToClassList("step-card");

            var badge = new Label(number);
            badge.AddToClassList("step-badge");
            card.Add(badge);

            var text = new Label(textKey);
            text.AddToClassList("step-text");
            text.AddToClassList("ndmf-tr");
            card.Add(text);

            return card;
        }

        private void UpdateStatus()
        {
            var data = MAMaterialHelperSession.HasCopiedData ? MAMaterialHelperSession.CopiedData : null;

            // The list and counts only need rebuilding when the copied data itself changes
            if (!ReferenceEquals(data, lastSeenData))
            {
                lastSeenData = data;
                RebuildCopiedList(data);
            }

            // The text is refreshed every tick so it follows language changes
            if (data != null)
            {
                statusLabel.text = Localization.S("maMaterialHelper.guide.statusCopied",
                    data.sourceRootName, copiedObjectCount, copiedMaterialCount);
                statusCard.AddToClassList("status-card--copied");
            }
            else
            {
                statusLabel.text = Localization.S("maMaterialHelper.guide.statusNotCopied");
                statusCard.RemoveFromClassList("status-card--copied");
            }
        }

        private void RebuildCopiedList(CopiedMaterialData data)
        {
            copiedListView.Clear();
            copiedObjectCount = 0;
            copiedMaterialCount = 0;

            if (data == null)
            {
                copiedListView.style.display = DisplayStyle.None;
                return;
            }

            copiedListView.style.display = DisplayStyle.Flex;

            // Groups exclude the internal __GROUP_START_ marker entries
            foreach (var group in MAMaterialHelperSession.GetCopiedDataGroups())
            {
                if (group.Count == 0) continue;

                var groupName = string.IsNullOrEmpty(group[0].rootObjectName)
                    ? data.sourceRootName
                    : group[0].rootObjectName;

                var foldout = new Foldout { text = $"{groupName} ({group.Count})", value = false };
                foldout.AddToClassList("copied-group");

                foreach (var setup in group)
                {
                    copiedObjectCount++;
                    copiedMaterialCount += setup.materials.Length;

                    var row = new Label($"{setup.objectName} ({setup.materials.Length})");
                    row.AddToClassList("copied-list-item");
                    row.tooltip = string.IsNullOrEmpty(setup.relativePath) ? setup.objectName : setup.relativePath;
                    foldout.Add(row);
                }

                copiedListView.Add(foldout);
            }
        }
    }
}
