using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The bar across the top of the quest screen: the quest's title, kind, giver and place, the quests it needs,
     * fails on and is read by (each a button that opens it), its rewards and flags, and its problem counts.</summary>
     */
    public sealed class QuestSummaryBar : VisualElement
    {
        #region Properties

        /** <summary>Raised when a quest named in the bar should open.</summary> */
        public event Action<QuestDefinition> OnOpenQuest = delegate { };

        /** <summary>Raised when the bar is clicked anywhere but a button, to show the whole quest in the Inspector.</summary> */
        public event Action OnClicked = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes an empty bar.</summary> */
        public QuestSummaryBar()
        {
            AddToClassList("quest-graph__summary");
            RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is VisualElement target && target.GetFirstOfType<Button>() != null) return;
                OnClicked();
            });
        }

        /** <summary>Shows a quest: its summary, and the links of every quest that reads it.</summary> */
        public void Show(QuestDefinition quest, QuestSummary summary, IReadOnlyList<QuestLink> readers)
        {
            Clear();
            if (quest == null) return;
            var title = new Label(QuestStates.TitleOf(quest));
            title.AddToClassList("quest-graph__summary-title");
            Add(title);
            AddKind(quest.Kind);
            if (quest.Giver != null) AddField("Giver", quest.Giver.DisplayName);
            if (quest.Place != null) AddField("Place", quest.Place.DisplayName);
            AddRefs("Needs", summary.Reads.Where(link => LinkSlots.GatesAvailability(link.Slot) && link.Read != quest)
                .Select(link => (link.Read, link.ReadStepId == null ? link.StateName : "step " + link.StateName)));
            AddRefs("Fails if", summary.Reads.Where(link => link.Slot == LinkSlot.Fails && link.Read != quest).Select(link => (link.Read, link.StateName)));
            AddRefs("Read by", readers.Where(link => link.Reader != quest && link.ReadStepId == null).Select(link => (link.Reader, link.Label)));
            if (quest.Rewards.Count > 0) AddField("Rewards", string.Join(" · ", quest.Rewards.Where(reward => reward != null).Select(reward => reward.Describe().Text)));
            if (quest.GivenAutomatically) AddPill("Auto-given");
            if (quest.Repeatable) AddPill("Repeatable");
            if (summary.Unreadable > 0)
            {
                var hidden = new Label($"{summary.Unreadable} not shown") { tooltip = "Conditions that can't say what they read; links through them aren't drawn" };
                hidden.AddToClassList("quest-graph__summary-note");
                Add(hidden);
            }
            var spacer = new VisualElement();
            spacer.AddToClassList("quest-graph__spacer");
            Add(spacer);
            var questIssues = string.Join("\n", summary.Issues.Where(issue => issue.StepId == null).Select(issue => issue.Problem));
            if (summary.Errors > 0) Add(WithTip(QuestList.Badge(summary.Errors, QuestIssueLevel.Error), questIssues));
            if (summary.Warnings > 0) Add(WithTip(QuestList.Badge(summary.Warnings, QuestIssueLevel.Warning), questIssues));
        }

        #endregion

        #region Helpers

        private void AddKind(QuestKind kind)
        {
            var field = Field("Kind");
            field.Add(KindLooks.Swatch(kind));
            field.Add(new Label(QuestStates.NameOf(kind)));
            Add(field);
        }

        private void AddField(string name, string value)
        {
            var field = Field(name);
            field.Add(new Label(value) { tooltip = value });
            Add(field);
        }

        private static VisualElement Field(string name)
        {
            var field = new VisualElement();
            field.AddToClassList("quest-graph__field");
            var label = new Label(name);
            label.AddToClassList("quest-graph__field-name");
            field.Add(label);
            return field;
        }

        private void AddPill(string text)
        {
            var pill = new Label(text);
            pill.AddToClassList("quest-graph__pill");
            pill.AddToClassList("quest-graph__pill--plain");
            Add(pill);
        }

        /** <summary>A field of buttons, one per quest, each with a note on how it is linked.</summary> */
        private void AddRefs(string name, IEnumerable<(QuestDefinition quest, string note)> refs)
        {
            var distinct = refs.Where(entry => entry.quest != null).GroupBy(entry => entry.quest).Select(group => group.First()).ToList();
            if (distinct.Count == 0) return;
            var field = Field(name);
            foreach (var (quest, note) in distinct)
                field.Add(RefButton(quest, note));
            Add(field);
        }

        private Button RefButton(QuestDefinition quest, string note)
        {
            var button = new Button(() => OnOpenQuest(quest)) { tooltip = $"Open {QuestStates.TitleOf(quest)}" };
            button.AddToClassList("quest-graph__ref");
            button.Add(KindLooks.Swatch(quest.Kind));
            button.Add(new Label(QuestStates.TitleOf(quest)));
            if (!string.IsNullOrEmpty(note))
            {
                var detail = new Label(note);
                detail.AddToClassList("quest-graph__ref-note");
                button.Add(detail);
            }
            return button;
        }

        private static Label WithTip(Label label, string tip)
        {
            label.tooltip = tip;
            return label;
        }

        #endregion
    }
}
