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

        /** <summary>Redraws the list from the index.</summary> */
        public void Rebuild()
        {
            _list.Clear();
            var filter = _search.value?.Trim() ?? "";
            var groups = _index.Quests.Where(quest => quest != null && (filter.Length == 0 || QuestStates.TitleOf(quest).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(quest => quest.Kind)
                .OrderBy(group => group.Key == null ? int.MaxValue : group.Key.Order).ThenBy(group => group.Key == null ? "" : group.Key.name);
            var any = false;
            foreach (var group in groups)
            {
                any = true;
                var heading = new Label(group.Key == null ? "No kind" : group.Key.name);
                heading.AddToClassList("quest-graph__group");
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
                    if (summary?.Errors > 0) item.Add(Badge(summary.Errors, "error"));
                    if (summary?.Warnings > 0) item.Add(Badge(summary.Warnings, "warning"));
                    _list.Add(item);
                }
            }
            if (!any) _list.Add(new Label("No matches") { name = "empty" });
        }

        #endregion

        #region Helpers

        /** <summary>A count in a small coloured badge.</summary> */
        internal static Label Badge(int count, string kind)
        {
            var badge = new Label(count.ToString());
            badge.AddToClassList("quest-graph__badge");
            badge.AddToClassList("quest-graph__badge--" + kind);
            return badge;
        }

        #endregion
    }
}
