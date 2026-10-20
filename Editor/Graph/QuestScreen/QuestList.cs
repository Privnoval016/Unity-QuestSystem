using System;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>The quest screen's sidebar: every quest, grouped by kind, with problem badges and a search.</summary> */
    public sealed class QuestList : VisualElement
    {
        #region Private Fields

        private readonly QuestIndex _index;
        private readonly ToolbarSearchField _search = new ToolbarSearchField();
        private readonly ScrollView _list = new ScrollView();
        private readonly DeferredRebuild _rebuild;
        private QuestDefinition _current;

        #endregion

        #region Properties

        /** <summary>Raised when a quest is picked.</summary> */
        public event Action<QuestDefinition> OnPicked = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes a list over an index.</summary> */
        public QuestList(QuestIndex index)
        {
            _index = index;
            _rebuild = new DeferredRebuild(this, Rebuild);
            AddToClassList("quest-graph__sidebar");
            _search.AddToClassList("quest-graph__sidebar-search");
            _search.RegisterValueChangedCallback(_ => Rebuild());
            Add(_search);
            Add(_list);
        }

        /** <summary>Marks the quest being edited and redraws.</summary> */
        public void SetCurrent(QuestDefinition quest)
        {
            _current = quest;
            Rebuild();
        }

        /** <summary>Redraws the list on the next update, once however often it is asked; not while hidden.</summary> */
        public void RequestRebuild() => _rebuild.Request();

        /** <summary>Redraws the list from the index now.</summary> */
        public void Rebuild()
        {
            _rebuild.Cancel();
            _list.Clear();
            var filter = _search.value?.Trim() ?? "";
            var groups = _index.Quests.Where(quest => quest != null && (filter.Length == 0 || QuestStates.TitleOf(quest).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(quest => quest.Kind)
                .OrderBy(group => group.Key == null ? int.MaxValue : group.Key.Order).ThenBy(group => QuestStates.NameOf(group.Key));
            var any = false;
            foreach (var group in groups)
            {
                any = true;
                var heading = new VisualElement();
                heading.AddToClassList("quest-graph__group");
                heading.Add(KindLooks.Swatch(group.Key));
                heading.Add(new Label(QuestStates.NameOf(group.Key)));
                _list.Add(heading);
                foreach (var quest in group.OrderBy(quest => quest.Order).ThenBy(QuestStates.TitleOf))
                {
                    var item = new Button(() => OnPicked(quest));
                    item.AddToClassList("quest-graph__list-item");
                    item.EnableInClassList("quest-graph__list-item--current", quest == _current);
                    var name = new Label(QuestStates.TitleOf(quest)) { tooltip = QuestStates.TitleOf(quest) };
                    name.AddToClassList("quest-graph__list-name");
                    item.Add(name);
                    var summary = _index.SummaryOf(quest);
                    if (summary?.Errors > 0) item.Add(Badge(summary.Errors, QuestIssueLevel.Error));
                    if (summary?.Warnings > 0) item.Add(Badge(summary.Warnings, QuestIssueLevel.Warning));
                    _list.Add(item);
                }
            }
            if (!any) _list.Add(new Label("No matches") { name = "empty" });
        }

        /** <summary>A count of problems in a small badge coloured by their level.</summary> */
        internal static Label Badge(int count, QuestIssueLevel level)
        {
            var badge = new Label(count.ToString());
            badge.AddToClassList("quest-graph__badge");
            badge.AddToClassList(level == QuestIssueLevel.Error ? "quest-graph__badge--error" : "quest-graph__badge--warning");
            return badge;
        }

        #endregion
    }
}
