using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>A quest in the web: its title, problem badges, a few facts, its kind as a colour, and a handle to draw links from.</summary> */
    public sealed class QuestNodeView : VisualElement
    {
        #region Properties

        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>The handle on the right edge that draws a link.</summary> */
        public VisualElement Handle { get; }

        /** <summary>Where the node sits, in content space.</summary> */
        public Vector2 Position
        {
            get => new Vector2(style.left.value.value, style.top.value.value);
            set
            {
                style.left = value.x;
                style.top = value.y;
            }
        }

        #endregion

        #region Public API

        /** <summary>Makes a node.</summary> */
        public QuestNodeView(QuestDefinition quest, QuestSummary summary, int kindColour, bool readsItself)
        {
            Quest = quest;
            AddToClassList("quest-graph__quest");
            AddToClassList("quest-graph__kind-" + kindColour);
            style.width = WebLayout.NodeWidth;
            focusable = true;

            var head = new VisualElement();
            head.AddToClassList("quest-graph__quest-head");
            var title = new Label(QuestStates.TitleOf(quest)) { tooltip = QuestStates.TitleOf(quest) };
            title.AddToClassList("quest-graph__quest-title");
            head.Add(title);
            if (quest.WebPinned)
            {
                var pin = new Label("•") { tooltip = "Pinned where it was dragged; Tidy leaves it" };
                pin.AddToClassList("quest-graph__pin");
                head.Add(pin);
            }
            if (readsItself)
            {
                var loop = new Label("↻") { tooltip = "Its own conditions read this quest" };
                loop.AddToClassList("quest-graph__pin");
                head.Add(loop);
            }
            if (summary?.Errors > 0) head.Add(Badge(summary.Errors, "error", summary));
            if (summary?.Warnings > 0) head.Add(Badge(summary.Warnings, "warning", summary));
            Add(head);

            var facts = new Label(string.Join("  ·  ", Facts(quest)));
            facts.AddToClassList("quest-graph__quest-facts");
            Add(facts);

            Handle = new VisualElement { tooltip = "Drag to another quest: it becomes available only once this is done" };
            Handle.AddToClassList("quest-graph__handle");
            Add(Handle);
        }

        /** <summary>Shows the node as selected or not.</summary> */
        public void SetSelected(bool selected) => EnableInClassList("quest-graph__quest--selected", selected);

        /** <summary>Fades the node out of a focus or a search, or brings it back.</summary> */
        public void SetFaded(bool faded) => EnableInClassList("quest-graph__quest--faded", faded);

        /** <summary>Marks the node as matching a search.</summary> */
        public void SetMatch(bool match) => EnableInClassList("quest-graph__quest--match", match);

        #endregion

        #region Helpers

        private static IEnumerable<string> Facts(QuestDefinition quest)
        {
            yield return quest.Steps.Count == 1 ? "1 step" : $"{quest.Steps.Count} steps";
            if (quest.GivenAutomatically) yield return "auto-given";
            if (quest.Repeatable) yield return "repeatable";
            if (!quest.HiddenUntilGiven) yield return "shown early";
        }

        private static Label Badge(int count, string kind, QuestSummary summary)
        {
            var badge = QuestList.Badge(count, kind);
            badge.tooltip = string.Join("\n", summary.Issues.Select(issue => issue.Problem));
            return badge;
        }

        #endregion
    }
}
