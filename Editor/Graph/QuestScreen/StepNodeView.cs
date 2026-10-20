using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>A step on the quest canvas: its title, pills, objectives and rules, with an input port on the left and an output on the right.</summary> */
    public sealed class StepNodeView : VisualElement
    {
        #region Private Fields

        /** <summary>Rough heights of a node's parts, for estimating a node before it is measured.</summary> */
        private const float HeaderHeight = 36f, TagsHeight = 22f, ObjectiveHeight = 34f, RuleHeight = 18f;

        #endregion

        #region Properties

        /** <summary>How wide every step node is.</summary> */
        public const float Width = 224f;

        /** <summary>How far down the node its ports sit.</summary> */
        public const float PortY = 15f;

        /** <summary>The height of a node with nothing in it, used before a node is measured.</summary> */
        public const float MinHeight = 40f;

        /** <summary>The step's id.</summary> */
        public string StepId { get; }

        /** <summary>The title bar, which drags the node.</summary> */
        public VisualElement Header { get; }

        /** <summary>The output port, which drags out a link.</summary> */
        public VisualElement OutPort { get; }

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

        /** <summary>Where wires into the node end.</summary> */
        public Vector2 InPoint => Position + new Vector2(-1f, PortY);

        /** <summary>Where wires out of the node start.</summary> */
        public Vector2 OutPoint => Position + new Vector2(Width + 1f, PortY);

        #endregion

        #region Public API

        /** <summary>Makes a node from a step's card.</summary> */
        public StepNodeView(StepCard card)
        {
            StepId = card.Id;
            AddToClassList("quest-graph__node");
            EnableInClassList("quest-graph__node--error", card.HasError);
            focusable = true;
            style.width = Width;

            Header = new VisualElement();
            Header.AddToClassList("quest-graph__node-head");
            var title = new Label(card.Title) { tooltip = card.Title };
            title.AddToClassList("quest-graph__node-title");
            Header.Add(title);
            if (card.Problems.Count > 0)
            {
                var marker = new Label("!") { tooltip = string.Join("\n", card.Problems.Select(issue => issue.Problem)) };
                marker.AddToClassList("quest-graph__marker");
                marker.EnableInClassList("quest-graph__marker--warning", !card.HasError);
                Header.Add(marker);
            }
            Add(Header);

            if (card.Tags.Count > 0)
            {
                var tags = new VisualElement();
                tags.AddToClassList("quest-graph__tags");
                foreach (var tag in card.Tags)
                    tags.Add(Pill(StepTagLook.Of(tag, card.Objectives.Count)));
                Add(tags);
            }

            var body = new VisualElement();
            body.AddToClassList("quest-graph__node-body");
            foreach (var objective in card.Objectives)
            {
                var row = new VisualElement();
                row.AddToClassList("quest-graph__objective");
                row.EnableInClassList("quest-graph__objective--optional", objective.Optional);
                row.EnableInClassList("quest-graph__objective--broken", objective.Broken);
                var dot = new VisualElement();
                dot.AddToClassList("quest-graph__objective-dot");
                row.Add(dot);
                var text = new VisualElement();
                text.AddToClassList("quest-graph__objective-text");
                var line = new VisualElement();
                line.AddToClassList("quest-graph__objective-line");
                line.Add(new Label(objective.Text));
                if (objective.Optional)
                {
                    var optional = new Label("optional");
                    optional.AddToClassList("quest-graph__objective-optional");
                    line.Add(optional);
                }
                text.Add(line);
                var tracker = new Label(objective.Tracker);
                tracker.AddToClassList("quest-graph__tracker");
                text.Add(tracker);
                row.Add(text);
                body.Add(row);
            }
            foreach (var rule in card.Rules)
            {
                var row = new VisualElement();
                row.AddToClassList("quest-graph__rule");
                var label = new Label(rule.Label);
                label.AddToClassList("quest-graph__rule-label");
                label.EnableInClassList("quest-graph__rule-label--fails", rule.Label == StepCard.FailsLabel);
                row.Add(label);
                var text = new Label(rule.Text) { tooltip = rule.Text };
                text.AddToClassList("quest-graph__rule-text");
                row.Add(text);
                body.Add(row);
            }
            Add(body);

            var inPort = new VisualElement { tooltip = card.Tags.Contains(StepTag.AfterAny) ? "After any linked step" : "After all linked steps" };
            inPort.AddToClassList("quest-graph__port");
            inPort.AddToClassList("quest-graph__port--in");
            inPort.EnableInClassList("quest-graph__port--any", card.Tags.Contains(StepTag.AfterAny));
            Add(inPort);
            OutPort = new VisualElement { tooltip = "Drag to link, or to empty canvas to add a step after this" };
            OutPort.AddToClassList("quest-graph__port");
            OutPort.AddToClassList("quest-graph__port--out");
            Add(OutPort);
            Add(SelectionRing.Make());
        }

        /** <summary>How tall a node with these objectives and rules is likely to be, before it is measured.</summary> */
        public static float EstimatedHeight(int objectives, int rules) => HeaderHeight + TagsHeight + objectives * ObjectiveHeight + rules * RuleHeight;

        /** <summary>Shows the node as selected or not.</summary> */
        public void SetSelected(bool selected) => EnableInClassList(SelectionRing.SelectedClass, selected);

        #endregion

        #region Helpers

        private static Label Pill(StepTagLook look)
        {
            var pill = new Label(look.Text) { tooltip = look.Meaning };
            pill.AddToClassList("quest-graph__pill");
            pill.AddToClassList(look.ClassName);
            return pill;
        }

        #endregion
    }
}
