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
     * <summary>The quest graph window: one quest's steps to edit, with a quest list, back and forward, and a status bar.
     * Opens from <i>Window &gt; Quests &gt; Quest Graph</i> and by double-clicking a quest. Its place survives domain reloads.</summary>
     */
    public sealed class QuestGraphWindow : EditorWindow
    {
        #region Inspector Fields

        [SerializeField] private string questGuid;
        [SerializeField] private List<string> back = new List<string>();
        [SerializeField] private List<string> forward = new List<string>();
        [SerializeField] private Vector2 pan;
        [SerializeField] private float zoom = 1f;

        #endregion

        #region Private Fields

        private QuestIndex _index;
        private QuestScreen _questScreen;
        private QuestList _list;
        private ToolbarButton _back;
        private ToolbarButton _forward;
        private ToolbarBreadcrumbs _crumbs;
        private Label _status;

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
            toolbar.Add(new ToolbarButton(() => _questScreen.AddStep()) { text = "+ Step", tooltip = "Add a step" });
            toolbar.Add(new ToolbarSpacer { flex = true });
            toolbar.Add(new ToolbarButton(() => _questScreen.Tidy()) { text = "Tidy", tooltip = "Lay out the steps again" });
            toolbar.Add(new ToolbarButton(() => _questScreen.Frame(false)) { text = "Frame", tooltip = "Frame every step (A); F frames the selection" });
            toolbar.Add(new ToolbarButton(Validate) { text = "Validate", tooltip = "Check every quest" });
            root.Add(toolbar);

            var body = new VisualElement();
            body.AddToClassList("quest-graph__body");
            _list = new QuestList(_index);
            _list.OnPicked += quest => Navigate(GuidOf(quest));
            body.Add(_list);
            _questScreen = new QuestScreen(_index);
            _questScreen.OnOpenQuest += quest => Navigate(GuidOf(quest));
            _questScreen.OnRebuilt += UpdateStatus;
            _questScreen.Canvas.OnViewChanged += view =>
            {
                pan = view.Pan;
                zoom = view.Zoom;
            };
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

        #endregion

        #region Public API

        /** <summary>Opens the window.</summary> */
        [MenuItem("Window/Quests/Quest Graph")]
        public static void OpenWindow() => GetWindow<QuestGraphWindow>("Quest Graph");

        /** <summary>Opens the window on a quest.</summary> */
        public static void Open(QuestDefinition quest)
        {
            var window = GetWindow<QuestGraphWindow>("Quest Graph");
            window.Navigate(GuidOf(quest));
        }

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

        private void HandleIndexChanged(QuestDefinition quest)
        {
            _list?.Rebuild();
            if (_questScreen != null && _questScreen.Quest == null && QuestOf(questGuid) != null) ShowCurrent(null);
        }

        #endregion

        #region Helpers

        private void Navigate(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid == questGuid) return;
            if (!string.IsNullOrEmpty(questGuid)) back.Add(questGuid);
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
                if (QuestOf(guid) == null) continue;
                if (!string.IsNullOrEmpty(questGuid)) to.Add(questGuid);
                questGuid = guid;
                ShowCurrent(null);
                return;
            }
            UpdateToolbar();
        }

        private void ShowCurrent(CanvasView? view)
        {
            if (_questScreen == null) return;
            var quest = QuestOf(questGuid);
            _questScreen.Show(quest, quest == null ? null : view);
            _list.SetCurrent(quest);
            titleContent = new GUIContent(quest == null ? "Quest Graph" : QuestStates.TitleOf(quest));
            UpdateToolbar();
        }

        private void UpdateToolbar()
        {
            _back.SetEnabled(back.Count > 0);
            _forward.SetEnabled(forward.Count > 0);
            while (_crumbs.childCount > 0) _crumbs.PopItem();
            var quest = QuestOf(questGuid);
            _crumbs.PushItem("Quests");
            if (quest == null) return;
            _crumbs.PushItem(quest.Kind == null ? "No kind" : quest.Kind.name);
            _crumbs.PushItem(QuestStates.TitleOf(quest));
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            var (steps, objectives, errors, warnings) = _questScreen.Counts;
            _status.text = _questScreen.Quest == null
                ? $"{_index.Quests.Count} quests. Pick one on the left."
                : $"{steps} steps   {objectives} objectives" + (errors > 0 ? $"   {errors} error{(errors == 1 ? "" : "s")}" : "") + (warnings > 0 ? $"   {warnings} warning{(warnings == 1 ? "" : "s")}" : "");
        }

        /** <summary>Checks every quest again and shows the totals.</summary> */
        private void Validate()
        {
            foreach (var quest in _index.Quests.ToList())
                _index.Refresh(quest);
            _list.Rebuild();
            _questScreen.Rebuild();
            var errors = _index.Quests.Sum(quest => _index.SummaryOf(quest)?.Errors ?? 0);
            var warnings = _index.Quests.Sum(quest => _index.SummaryOf(quest)?.Warnings ?? 0);
            _status.text = $"{_index.Quests.Count} quests checked   {errors} error{(errors == 1 ? "" : "s")}   {warnings} warning{(warnings == 1 ? "" : "s")}";
        }

        private static QuestDefinition QuestOf(string guid) =>
            string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));

        private static string GuidOf(QuestDefinition quest) => quest == null ? null : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(quest));

        #endregion
    }
}
