using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Other quests linked to a quest's steps, drawn as small stubs beside the step: quests a step reads on its
     * left, quests that read a step on its right. A few per step, then "+N more"; clicking one opens that quest. Stubs
     * follow their step when it moves.</summary>
     */
    public sealed class StepStubs
    {
        #region Private Fields

        /** <summary>How many stubs a step shows on a side before "+N more".</summary> */
        private const int MaxPerStep = 3;

        /** <summary>Space between a step and its stubs.</summary> */
        private const float Gap = 40f;

        /** <summary>Space between stubs stacked beside one step.</summary> */
        private const float Spacing = 4f;

        private readonly List<Stub> _stubs = new List<Stub>();

        #endregion

        #region Properties

        /** <summary>How wide a stub is.</summary> */
        public const float Width = 240f;

        /** <summary>How tall a stub is.</summary> */
        public const float Height = 24f;

        /** <summary>The stubs' wires.</summary> */
        public IEnumerable<Wire> Wires => _stubs.Select(stub => stub.Wire);

        /** <summary>Where every stub is, for framing.</summary> */
        public IEnumerable<Rect> Rects => _stubs.Select(stub => new Rect(stub.Position, new Vector2(Width, Height)));

        /** <summary>Raised when a stub's quest should open.</summary> */
        public event Action<QuestDefinition> OnOpenQuest = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes stubs for a quest's steps, adding them to a layer.</summary> */
        public void Build(VisualElement layer, QuestDefinition quest, QuestSummary summary, IReadOnlyList<QuestLink> readers, IReadOnlyDictionary<string, StepNodeView> nodes)
        {
            _stubs.Clear();
            foreach (var group in readers.Where(link => link.Reader != quest && link.ReadStepId != null && nodes.ContainsKey(link.ReadStepId)).GroupBy(link => link.ReadStepId))
                Add(layer, nodes[group.Key], Distinct(group.Select(link => (link.Reader, link.Label))), false);
            foreach (var group in summary.Reads.Where(link => link.ReaderStepId != null && link.Read != quest && nodes.ContainsKey(link.ReaderStepId)).GroupBy(link => link.ReaderStepId))
                Add(layer, nodes[group.Key], Distinct(group.Select(link => (link.Read, link.Label))), true);
            Place();
        }

        /** <summary>Moves every stub beside its step and its wire between them.</summary> */
        public void Place()
        {
            foreach (var stub in _stubs)
            {
                var node = stub.Node.Position;
                stub.Position = new Vector2(stub.Into ? node.x - Gap - Width : node.x + StepNodeView.Width + Gap, node.y + stub.Slot * (Height + Spacing));
                stub.Element.style.left = stub.Position.x;
                stub.Element.style.top = stub.Position.y;
                var middle = stub.Position.y + Height / 2f;
                stub.Wire.From = stub.Into ? new Vector2(stub.Position.x + Width, middle) : stub.Node.OutPoint;
                stub.Wire.To = stub.Into ? stub.Node.InPoint : new Vector2(stub.Position.x, middle);
            }
        }

        #endregion

        #region Helpers

        private static List<(QuestDefinition quest, string note)> Distinct(IEnumerable<(QuestDefinition quest, string note)> links) =>
            links.Where(link => link.quest != null).GroupBy(link => link.quest).Select(group => group.First()).ToList();

        private void Add(VisualElement layer, StepNodeView node, List<(QuestDefinition quest, string note)> quests, bool into)
        {
            for (var i = 0; i < quests.Count && i <= MaxPerStep; i++)
            {
                var more = i == MaxPerStep && quests.Count > MaxPerStep + 1;
                var (quest, note) = quests[i];
                var element = new VisualElement();
                element.AddToClassList("quest-graph__stub");
                element.style.width = Width;
                element.style.height = Height;
                if (more)
                {
                    element.tooltip = string.Join("\n", quests.Skip(MaxPerStep).Select(entry => QuestStates.TitleOf(entry.quest)));
                    element.Add(new Label($"+{quests.Count - MaxPerStep} more"));
                }
                else
                {
                    element.tooltip = $"Open {QuestStates.TitleOf(quest)}";
                    element.Add(KindLooks.Swatch(quest.Kind));
                    var title = new Label(QuestStates.TitleOf(quest));
                    title.AddToClassList("quest-graph__stub-title");
                    element.Add(title);
                    var detail = new Label(note);
                    detail.AddToClassList("quest-graph__ref-note");
                    element.Add(detail);
                    element.RegisterCallback<ClickEvent>(_ => OnOpenQuest(quest));
                }
                layer.Add(element);
                _stubs.Add(new Stub(element, node, i, into, new Wire { Style = WireStyle.Normal, Dashed = true, Arrow = true }));
            }
        }

        /** <summary>One stub: its element, its step, its place in the step's stack, its side, and its wire.</summary> */
        private sealed class Stub
        {
            public VisualElement Element { get; }
            public StepNodeView Node { get; }
            public int Slot { get; }
            public bool Into { get; }
            public Wire Wire { get; }
            public Vector2 Position { get; set; }

            public Stub(VisualElement element, StepNodeView node, int slot, bool into, Wire wire)
            {
                Element = element;
                Node = node;
                Slot = slot;
                Into = into;
                Wire = wire;
            }
        }

        #endregion
    }
}
