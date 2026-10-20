using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The quest graph window: the web of every quest, or one quest's steps to edit, with back and forward
     * between them. Opens from <i>Window &gt; Quests &gt; Quest Graph</i> and by double-clicking a quest. Where it was
     * survives domain reloads.</summary>
     */
    public sealed class QuestGraphWindow : EditorWindow
    {
        #region Inspector Fields

        [Tooltip("The quest open, by asset GUID; empty for the web.")]
        [SerializeField] private string questGuid = "";

        [Tooltip("Where Back goes, most recent last, by asset GUID; empty entries are the web.")]
        [SerializeField] private List<string> back = new List<string>();

        [Tooltip("Where Forward goes, most recent last, by asset GUID; empty entries are the web.")]
        [SerializeField] private List<string> forward = new List<string>();

        [Tooltip("The canvas's pan when the window was last reloaded.")]
        [SerializeField] private Vector2 pan;

        [Tooltip("The canvas's zoom when the window was last reloaded.")]
        [SerializeField] private float zoom = 1f;

        #endregion

        #region Private Fields

        private QuestIndex _index;
        private WebScreen _webScreen;
        private QuestScreen _questScreen;
        private QuestList _list;
        private ToolbarButton _back;
        private ToolbarButton _forward;
        private ToolbarBreadcrumbs _crumbs;
        private ToolbarButton _questTools;
        private Label _status;

        #endregion

        #region Properties

        private bool OnWeb => string.IsNullOrEmpty(questGuid) || QuestOf(questGuid) == null;

        #endregion

        #region MonoBehaviour Callbacks

        private void CreateGUI()
        {
            _index = SharedQuestIndex.Get();
            var root = rootVisualElement;
            QuestGraphStyles.Apply(root);
            root.AddToClassList("quest-graph");

            var toolbar = new Toolbar();
            _back = new ToolbarButton(Back) { text = "◀", tooltip = "Back" };
            _forward = new ToolbarButton(Forward) { text = "▶", tooltip = "Forward" };
            _crumbs = new ToolbarBreadcrumbs();
            toolbar.Add(_back);
            toolbar.Add(_forward);
            toolbar.Add(_crumbs);
            toolbar.Add(new ToolbarSpacer { flex = true });
            _questTools = new ToolbarButton(() => _questScreen.AddStep()) { text = "+ Step", tooltip = "Add a step after the selected step, or where the pointer last was" };
            toolbar.Add(_questTools);
            toolbar.Add(new ToolbarButton(Tidy) { text = "Tidy", tooltip = "Lay out again; pinned quests stay" });
            toolbar.Add(new ToolbarButton(Frame) { text = "Frame", tooltip = "Frame everything (A); F frames the selection" });
            toolbar.Add(new ToolbarButton(Validate) { text = "Validate", tooltip = "Check every quest" });
            root.Add(toolbar);

            var body = new VisualElement();
            body.AddToClassList("quest-graph__body");
            _list = new QuestList(_index);
            _list.OnPicked += quest => Navigate(GuidOf(quest));
            body.Add(_list);
            _webScreen = new WebScreen(_index);
            _webScreen.OnOpenQuest += quest => Navigate(GuidOf(quest));
            _webScreen.OnRebuilt += UpdateStatus;
            _webScreen.Canvas.OnViewChanged += RememberView;
            body.Add(_webScreen);
            _questScreen = new QuestScreen(_index);
            _questScreen.OnOpenQuest += quest => Navigate(GuidOf(quest));
            _questScreen.OnRebuilt += UpdateStatus;
            _questScreen.Canvas.OnViewChanged += RememberView;
            body.Add(_questScreen);
            root.Add(body);

            _status = new Label();
            _status.AddToClassList("quest-graph__status");
            root.Add(_status);

            _index.OnChanged += HandleIndexChanged;
            ShowCurrent(new CanvasView(pan, zoom));
        }

        private void OnDisable()
        {
            if (_index != null) _index.OnChanged -= HandleIndexChanged;
        }

        private void OnFocus()
        {
            if (rootVisualElement != null) QuestGraphStyles.Apply(rootVisualElement);
        }

        private void OnLostFocus()
        {
            _questScreen?.HideNotice();
            _webScreen?.HideNotice();
        }

        #endregion

        #region Public API

        /** <summary>Opens the window on the web.</summary> */
        [MenuItem("Window/Quests/Quest Graph")]
        public static void OpenWindow() => GetWindow<QuestGraphWindow>("Quest Graph");

        /** <summary>Opens the window on a quest.</summary> */
        public static void Open(QuestDefinition quest) => GetWindow<QuestGraphWindow>("Quest Graph").ShowQuest(quest);

        /** <summary>Goes to a quest's own screen, or to the web with none, keeping where it was for Back.</summary> */
        public void ShowQuest(QuestDefinition quest) => Navigate(GuidOf(quest));

        #endregion

        #region Event Handlers

        [OnOpenAsset]
        private static bool HandleOpenAsset(EntityId entityId, int line)
        {
            var quest = EditorUtility.EntityIdToObject(entityId) as QuestDefinition;
            if (quest == null) return false;
            Open(quest);
            return true;
        }

        /** <summary>Keeps the list current, and leaves for the web when the quest open is deleted.</summary> */
        private void HandleIndexChanged(QuestDefinition quest)
        {
            _list?.RequestRebuild();
            if (_questScreen == null || string.IsNullOrEmpty(questGuid) || QuestOf(questGuid) != null) return;
            questGuid = "";
            ShowCurrent(null);
        }

        private void RememberView(CanvasView view)
        {
            pan = view.Pan;
            zoom = view.Zoom;
        }

        #endregion

        #region Helpers

        /** <summary>Goes to a quest by GUID, or to the web with an empty one, remembering where it was.</summary> */
        private void Navigate(string guid)
        {
            guid ??= "";
            if (guid == questGuid && _questScreen != null) return;
            back.Add(questGuid);
            forward.Clear();
            questGuid = guid;
            ShowCurrent(null);
        }

        private void Back() => Step(back, forward);

        private void Forward() => Step(forward, back);

        /** <summary>Moves through history, skipping quests that no longer exist.</summary> */
        private void Step(List<string> from, List<string> to)
        {
            while (from.Count > 0)
            {
                var guid = from[from.Count - 1];
                from.RemoveAt(from.Count - 1);
                if (!string.IsNullOrEmpty(guid) && QuestOf(guid) == null) continue;
                to.Add(questGuid);
                questGuid = guid;
                ShowCurrent(null);
                return;
            }
            UpdateToolbar();
        }

        private void ShowCurrent(CanvasView? view)
        {
            if (_questScreen == null) return;
            var web = OnWeb;
            _webScreen.style.display = web ? DisplayStyle.Flex : DisplayStyle.None;
            _questScreen.style.display = web ? DisplayStyle.None : DisplayStyle.Flex;
            _list.style.display = web ? DisplayStyle.None : DisplayStyle.Flex;
            _questTools.style.display = web ? DisplayStyle.None : DisplayStyle.Flex;
            if (web)
            {
                _questScreen.Show(null);
                _webScreen.Show(view);
                titleContent = new GUIContent("Quest Graph");
            }
            else
            {
                var quest = QuestOf(questGuid);
                _questScreen.Show(quest, view);
                _list.SetCurrent(quest);
                titleContent = new GUIContent(QuestStates.TitleOf(quest));
            }
            UpdateToolbar();
            UpdateStatus();
        }

        private void UpdateToolbar()
        {
            _back.SetEnabled(back.Count > 0);
            _forward.SetEnabled(forward.Count > 0);
            while (_crumbs.childCount > 0) _crumbs.PopItem();
            _crumbs.PushItem("Quests", () => Navigate(""));
            if (OnWeb) return;
            var quest = QuestOf(questGuid);
            _crumbs.PushItem(QuestStates.NameOf(quest.Kind));
            _crumbs.PushItem(QuestStates.TitleOf(quest));
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            if (OnWeb)
            {
                var web = _webScreen.Counts;
                _status.text = Join(web.Shown == web.Total ? Count(web.Total, "quest") : $"{web.Shown} of {Count(web.Total, "quest")}", Count(web.Links, "link"),
                    Count(web.Errors, "error"), Count(web.Warnings, "warning"), web.Unreadable > 0 ? $"{Count(web.Unreadable, "condition")} not shown" : null);
                return;
            }
            var counts = _questScreen.Counts;
            _status.text = Join(Count(counts.Steps, "step"), Count(counts.Objectives, "objective"), Count(counts.Errors, "error"), Count(counts.Warnings, "warning"));
        }

        /** <summary>A count with its noun, plural when not one; null for none, so it is left out.</summary> */
        private static string Count(int count, string noun) => count > 0 ? $"{count} {noun}{(count == 1 ? "" : "s")}" : null;

        private static string Join(params string[] parts) => string.Join("  ·  ", parts.Where(part => part != null));

        private void Tidy()
        {
            if (OnWeb) _webScreen.Tidy();
            else _questScreen.Tidy();
        }

        private void Frame()
        {
            if (OnWeb) _webScreen.Frame(false);
            else _questScreen.Frame(false);
        }

        /** <summary>Checks every quest again and shows the totals.</summary> */
        private void Validate()
        {
            using (_index.Batch())
                foreach (var quest in _index.Quests.ToList())
                    _index.Refresh(quest);
            _list.Rebuild();
            if (OnWeb) _webScreen.Rebuild();
            else _questScreen.Rebuild();
            var errors = _index.Quests.Sum(quest => _index.SummaryOf(quest)?.Errors ?? 0);
            var warnings = _index.Quests.Sum(quest => _index.SummaryOf(quest)?.Warnings ?? 0);
            _status.text = Join($"{_index.Quests.Count} quests checked", Count(errors, "error") ?? "no errors", Count(warnings, "warning") ?? "no warnings");
        }

        private static QuestDefinition QuestOf(string guid) =>
            string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));

        private static string GuidOf(QuestDefinition quest) => quest == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(quest));

        #endregion
    }
}
