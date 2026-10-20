using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The single-quest edit screen: a summary bar, the quest's steps as nodes wired by what they follow, and
     * stubs for other quests that read its steps. Every change goes through <see cref="GraphEdits" />, one Undo step
     * each; step details are edited in the Inspector.</summary>
     */
    public sealed class QuestScreen : VisualElement
    {
        #region Private Fields

        private const float WireHitDistance = 6f;
        private const int MaxStubsPerStep = 3;
        private const float StubGap = 40f;
        private const float StubHeight = 24f;
        private static readonly string[] Commands = { "Copy", "Paste", "Duplicate", "Delete", "SoftDelete", "FrameSelected", "FrameSelectedWithLock", "SelectAll" };

        private readonly QuestIndex _index;
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly VisualElement _summary = new VisualElement();
        private readonly Notice _notice = new Notice();
        private readonly Dictionary<string, StepNodeView> _nodes = new Dictionary<string, StepNodeView>();
        private readonly Dictionary<Wire, (string from, string to)> _links = new Dictionary<Wire, (string from, string to)>();
        private readonly List<(Wire wire, VisualElement stub, StepNodeView node, bool into)> _stubWires = new List<(Wire, VisualElement, StepNodeView, bool)>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private QuestDefinition _quest;
        private Wire _selectedWire;
        private Vector2 _pointer;
        private Dictionary<string, Vector2> _dragFrom;
        private Vector2 _dragStart;
        private bool _dragged;
        private StepNodeView _linkFrom;
        private Wire _preview;
        private bool _frameOnLayout;

        #endregion

        #region Properties

        /** <summary>The quest shown.</summary> */
        public QuestDefinition Quest => _quest;

        /** <summary>The canvas, for the window's view state.</summary> */
        public GraphCanvas Canvas => _canvas;

        /** <summary>How many steps and objectives are shown, and how many problems, for the status bar.</summary> */
        public (int steps, int objectives, int errors, int warnings) Counts { get; private set; }

        /** <summary>Raised when another quest should open.</summary> */
        public event Action<QuestDefinition> OnOpenQuest = delegate { };

        /** <summary>Raised after the screen redraws, so the window can update its status bar.</summary> */
        public event Action OnRebuilt = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes the screen over an index.</summary> */
        public QuestScreen(QuestIndex index)
        {
            _index = index;
            AddToClassList("quest-graph__screen");
            _summary.AddToClassList("quest-graph__summary");
            _summary.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button || _quest == null) return;
                Select(Array.Empty<string>());
            });
            Add(_canvas);
            Add(_summary);
            Add(_notice);

            _canvas.OnBackgroundClicked += HandleBackgroundClicked;
            _canvas.OnMarquee += HandleMarquee;
            _canvas.RegisterCallback<PointerMoveEvent>(evt => _pointer = _canvas.ToContent(_canvas.WorldToLocal(evt.position)), TrickleDown.TrickleDown);
            _canvas.RegisterCallback<ValidateCommandEvent>(HandleValidateCommand);
            _canvas.RegisterCallback<ExecuteCommandEvent>(HandleExecuteCommand);
            _canvas.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
            _canvas.AddManipulator(new ContextualMenuManipulator(BuildMenu));
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += Rebuild;
                _index.OnChanged += HandleIndexChanged;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= Rebuild;
                _index.OnChanged -= HandleIndexChanged;
            });
        }

        /** <summary>Shows a quest, framed to fit.</summary> */
        public void Show(QuestDefinition quest, CanvasView? view = null)
        {
            _quest = quest;
            _selected.Clear();
            _selectedWire = null;
            _notice.Hide();
            Rebuild();
            if (view.HasValue) _canvas.SetView(view.Value);
            else
            {
                _frameOnLayout = true;
                FrameIfWaiting();
            }
        }

        /** <summary>Redraws everything from the quest, keeping the view and the selection.</summary> */
        public void Rebuild()
        {
            _canvas.Nodes.Clear();
            _canvas.Labels.Clear();
            _nodes.Clear();
            _links.Clear();
            _stubWires.Clear();
            _summary.Clear();
            if (_quest == null)
            {
                _canvas.Wires.SetWires(Array.Empty<Wire>());
                Counts = default;
                OnRebuilt();
                return;
            }

            var summary = _index.SummaryOf(_quest) ?? QuestSummary.Of(_quest);
            var issues = QuestValidator.Validate(_quest);
            BuildSummary(summary, issues);
            var cards = StepCard.Of(_quest, issues);
            foreach (var card in cards)
            {
                var node = new StepNodeView(card);
                _nodes[card.Id] = node;
                node.SetSelected(_selected.Contains(card.Id));
                HookNode(node);
                _canvas.Nodes.Add(node);
            }
            _selected.IntersectWith(_nodes.Keys);
            PlaceNodes();
            BuildStubs(summary);
            RebuildWires();
            Counts = (cards.Count, cards.Sum(card => card.Objectives.Count), issues.Count(i => i.Level == QuestIssueLevel.Error), issues.Count(i => i.Level == QuestIssueLevel.Warning));
            _canvas.schedule.Execute(_canvas.Cull);
            OnRebuilt();
        }

        /** <summary>Frames the selected steps, or all of them when none is selected.</summary> */
        public void Frame(bool selectionOnly = true)
        {
            var nodes = _nodes.Values.Where(node => !selectionOnly || _selected.Count == 0 || _selected.Contains(node.StepId)).ToList();
            if (nodes.Count == 0) return;
            var bounds = nodes.Select(NodeRect).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
            bounds.yMin -= StepLayout.Origin.y - 20f;
            _canvas.Frame(bounds);
        }

        /** <summary>Lays the steps out again and saves where they went, as one Undo step.</summary> */
        public void Tidy()
        {
            if (_quest == null) return;
            GraphEdits.MoveSteps(_quest, StepLayout.Arrange(_quest, HeightOf));
            Rebuild();
        }

        /** <summary>Adds a step where the pointer last was, or after the selected step.</summary> */
        public void AddStep()
        {
            if (_quest == null) return;
            var after = _selected.Count == 1 ? _selected.First() : null;
            var at = after != null ? _nodes[after].Position + new Vector2(StepLayout.ColumnWidth, 0f) : _pointer;
            var step = GraphEdits.AddStep(_quest, at, after);
            Select(new[] { step.Id });
            Rebuild();
        }

        #endregion

        #region Event Handlers

        private void HandleIndexChanged(QuestDefinition quest)
        {
            if (_quest == null || (quest != _quest && _index.ReadersOf(_quest).All(link => link.Reader != quest))) return;
            schedule.Execute(Rebuild);
        }

        private void HandleBackgroundClicked(Vector2 point, bool additive)
        {
            _selectedWire = _canvas.Wires.WireAt(point, WireHitDistance / _canvas.View.Zoom);
            if (_selectedWire != null && !_links.ContainsKey(_selectedWire)) _selectedWire = null;
            foreach (var wire in _canvas.Wires.Wires)
                wire.Selected = wire == _selectedWire;
            _canvas.Wires.Redraw();
            if (!additive) Select(Array.Empty<string>());
        }

        private void HandleMarquee(Rect box, bool additive)
        {
            var inside = _nodes.Values.Where(node => NodeRect(node).Overlaps(box)).Select(node => node.StepId);
            Select(additive ? _selected.Union(inside).ToList() : inside.ToList());
        }

        private void HandleValidateCommand(ValidateCommandEvent evt)
        {
            if (_quest == null || !Commands.Contains(evt.commandName)) return;
            evt.StopPropagation();
            evt.imguiEvent?.Use();
        }

        private void HandleExecuteCommand(ExecuteCommandEvent evt)
        {
            if (_quest == null || !Commands.Contains(evt.commandName)) return;
            switch (evt.commandName)
            {
                case "Copy": Copy(); break;
                case "Paste": Paste(_pointer); break;
                case "Duplicate": Duplicate(); break;
                case "Delete":
                case "SoftDelete": DeleteSelection(); break;
                case "SelectAll": Select(_nodes.Keys.ToList()); break;
                default: Frame(); break;
            }
            evt.StopPropagation();
            evt.imguiEvent?.Use();
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (_quest == null) return;
            switch (evt.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelection();
                    break;
                case KeyCode.F:
                    Frame();
                    break;
                case KeyCode.A when !evt.actionKey:
                    Frame(false);
                    break;
                case KeyCode.Escape:
                    Select(Array.Empty<string>());
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        #endregion

        #region Helpers: Building

        private void BuildSummary(QuestSummary summary, IReadOnlyList<QuestIssue> issues)
        {
            var title = new Label(QuestStates.TitleOf(_quest));
            title.AddToClassList("quest-graph__summary-title");
            _summary.Add(title);
            AddField("Kind", _quest.Kind == null ? "none" : _quest.Kind.name);
            if (_quest.Giver != null) AddField("Giver", _quest.Giver.DisplayName);
            if (_quest.Place != null) AddField("Place", _quest.Place.DisplayName);
            AddRefs("Needs", summary.Reads.Where(link => link.Slot == LinkSlot.Available && link.Read != _quest).Select(link => (link.Read, link.ReadStepId == null ? link.StateName : "step " + link.StateName)));
            AddRefs("Fails if", summary.Reads.Where(link => link.Slot == LinkSlot.Fails && link.Read != _quest).Select(link => (link.Read, link.StateName)));
            AddRefs("Read by", _index.ReadersOf(_quest).Where(link => link.Reader != _quest && link.ReadStepId == null).Select(link => (link.Reader, link.Label)));
            if (_quest.Rewards.Count > 0) AddField("Rewards", string.Join(" · ", _quest.Rewards.Where(reward => reward != null).Select(reward => reward.Describe().Text)));
            if (_quest.GivenAutomatically) AddPill("Auto-given");
            if (_quest.Repeatable) AddPill("Repeatable");
            if (summary.Unreadable > 0)
            {
                var hidden = new Label($"{summary.Unreadable} not shown") { tooltip = "Conditions that can't say what they read; links through them aren't drawn" };
                hidden.AddToClassList("quest-graph__summary-note");
                _summary.Add(hidden);
            }
            var spacer = new VisualElement();
            spacer.AddToClassList("quest-graph__spacer");
            _summary.Add(spacer);
            var errors = issues.Count(issue => issue.Level == QuestIssueLevel.Error);
            var warnings = issues.Count - errors;
            var questIssues = string.Join("\n", issues.Where(issue => issue.StepId == null).Select(issue => issue.Problem));
            if (errors > 0) _summary.Add(WithTip(QuestList.Badge(errors, "error"), questIssues));
            if (warnings > 0) _summary.Add(WithTip(QuestList.Badge(warnings, "warning"), questIssues));
        }

        private static Label WithTip(Label label, string tip)
        {
            label.tooltip = tip;
            return label;
        }

        private void AddField(string name, string value)
        {
            var field = new VisualElement();
            field.AddToClassList("quest-graph__field");
            var label = new Label(name);
            label.AddToClassList("quest-graph__field-name");
            field.Add(label);
            field.Add(new Label(value) { tooltip = value });
            _summary.Add(field);
        }

        private void AddPill(string text)
        {
            var pill = new Label(text);
            pill.AddToClassList("quest-graph__pill");
            pill.AddToClassList("quest-graph__pill--plain");
            _summary.Add(pill);
        }

        private void AddRefs(string name, IEnumerable<(QuestDefinition quest, string note)> refs)
        {
            var distinct = refs.Where(r => r.quest != null).GroupBy(r => r.quest).Select(group => group.First()).ToList();
            if (distinct.Count == 0) return;
            var field = new VisualElement();
            field.AddToClassList("quest-graph__field");
            var label = new Label(name);
            label.AddToClassList("quest-graph__field-name");
            field.Add(label);
            foreach (var (quest, note) in distinct)
                field.Add(RefButton(quest, note));
            _summary.Add(field);
        }

        private Button RefButton(QuestDefinition quest, string note)
        {
            var button = new Button(() => OnOpenQuest(quest)) { tooltip = $"Open {QuestStates.TitleOf(quest)}" };
            button.AddToClassList("quest-graph__ref");
            var swatch = new VisualElement();
            swatch.AddToClassList("quest-graph__swatch");
            button.Add(swatch);
            button.Add(new Label(QuestStates.TitleOf(quest)));
            if (!string.IsNullOrEmpty(note))
            {
                var detail = new Label(note);
                detail.AddToClassList("quest-graph__ref-note");
                button.Add(detail);
            }
            return button;
        }

        private void PlaceNodes()
        {
            var arranged = StepLayout.NeedsLayout(_quest) ? StepLayout.Arrange(_quest, EstimatedHeight) : null;
            foreach (var step in _quest.Steps)
            {
                if (step == null || !_nodes.TryGetValue(step.Id, out var node)) continue;
                node.Position = arranged != null && arranged.TryGetValue(step.Id, out var at) ? at : step.GraphPosition;
            }
        }

        private float EstimatedHeight(string stepId)
        {
            var step = _quest.FindStep(stepId);
            var rules = (step.UnlockWhen != null ? 1 : 0) + (step.FailWhen != null ? 1 : 0);
            return 36f + 22f + step.Objectives.Count * 34f + rules * 18f;
        }

        private float HeightOf(string stepId) => _nodes.TryGetValue(stepId, out var node) && node.layout.height > 0f ? node.layout.height : EstimatedHeight(stepId);

        private void BuildStubs(QuestSummary summary)
        {
            var readers = _index.ReadersOf(_quest).Where(link => link.Reader != _quest && link.ReadStepId != null && _nodes.ContainsKey(link.ReadStepId))
                .GroupBy(link => link.ReadStepId);
            foreach (var group in readers)
                AddStubs(_nodes[group.Key], group.GroupBy(link => link.Reader).Select(g => g.First()).Select(link => (link.Reader, $"reads this step: {link.Label}")).ToList(), false);
            var reads = summary.Reads.Where(link => link.ReaderStepId != null && link.Read != _quest && _nodes.ContainsKey(link.ReaderStepId)).GroupBy(link => link.ReaderStepId);
            foreach (var group in reads)
                AddStubs(_nodes[group.Key], group.GroupBy(link => link.Read).Select(g => g.First()).Select(link => (link.Read, link.Label)).ToList(), true);
        }

        private void AddStubs(StepNodeView node, List<(QuestDefinition quest, string note)> quests, bool into)
        {
            for (var i = 0; i < quests.Count && i <= MaxStubsPerStep; i++)
            {
                var more = i == MaxStubsPerStep;
                var (quest, note) = quests[i];
                var stub = new VisualElement { tooltip = more ? string.Join("\n", quests.Skip(MaxStubsPerStep).Select(q => QuestStates.TitleOf(q.quest))) : $"Open {QuestStates.TitleOf(quest)}" };
                stub.AddToClassList("quest-graph__stub");
                if (more) stub.Add(new Label($"+{quests.Count - MaxStubsPerStep} more"));
                else
                {
                    stub.Add(new Label(QuestStates.TitleOf(quest)));
                    var detail = new Label(note);
                    detail.AddToClassList("quest-graph__ref-note");
                    stub.Add(detail);
                    stub.RegisterCallback<ClickEvent>(_ => OnOpenQuest(quest));
                }
                var y = node.Position.y + i * (StubHeight + 4f);
                stub.style.top = y;
                if (into) stub.style.left = node.Position.x - StubGap - 220f;
                else stub.style.left = node.Position.x + StepNodeView.Width + StubGap;
                _canvas.Nodes.Add(stub);
                var wire = new Wire { Style = WireStyle.Normal, Dashed = true, Arrow = true };
                _stubWires.Add((wire, stub, node, into));
                stub.RegisterCallback<GeometryChangedEvent>(_ => PlaceStubWires());
            }
        }

        private void RebuildWires()
        {
            var wires = new List<Wire>();
            var record = new QuestRecord(_quest);
            foreach (var step in record.Steps)
            {
                if (!_nodes.TryGetValue(step.Definition.Id, out var to)) continue;
                var definition = step.Definition;
                if (definition.Start == StepStart.AfterStepAbove && definition.UnlockWhen != null) continue;
                foreach (var before in step.Follows)
                {
                    if (!_nodes.TryGetValue(before.Definition.Id, out var from)) continue;
                    var ghost = definition.Start == StepStart.AfterStepAbove;
                    var wire = new Wire
                    {
                        Key = before.Definition.Id + ">" + definition.Id,
                        From = from.OutPoint,
                        To = to.InPoint,
                        Style = ghost ? WireStyle.Ghost : definition.AfterRule == StepJoin.Any ? WireStyle.Any : WireStyle.Normal,
                        Dashed = ghost,
                    };
                    wire.Selected = _selectedWire != null && _selectedWire.Key == wire.Key;
                    if (wire.Selected) _selectedWire = wire;
                    _links[wire] = (before.Definition.Id, definition.Id);
                    wires.Add(wire);
                }
            }
            wires.AddRange(_stubWires.Select(stub => stub.wire));
            if (_preview != null) wires.Add(_preview);
            _canvas.Wires.SetWires(wires);
            PlaceStubWires();
        }

        private void MoveWires()
        {
            foreach (var pair in _links)
            {
                pair.Key.From = _nodes[pair.Value.from].OutPoint;
                pair.Key.To = _nodes[pair.Value.to].InPoint;
            }
            PlaceStubWires();
        }

        private void PlaceStubWires()
        {
            foreach (var (wire, stub, node, into) in _stubWires)
            {
                var stubAt = new Vector2(stub.resolvedStyle.left, stub.resolvedStyle.top);
                var mid = stubAt.y + Mathf.Max(stub.layout.height, StubHeight) / 2f;
                if (into)
                {
                    wire.From = new Vector2(stubAt.x + stub.layout.width, mid);
                    wire.To = node.InPoint;
                }
                else
                {
                    wire.From = node.OutPoint;
                    wire.To = new Vector2(stubAt.x, mid);
                }
            }
            _canvas.Wires.Redraw();
        }

        #endregion

        #region Helpers: Editing

        private void HookNode(StepNodeView node)
        {
            node.Header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.shiftKey || evt.actionKey)
                {
                    var next = new HashSet<string>(_selected);
                    if (!next.Remove(node.StepId)) next.Add(node.StepId);
                    Select(next.ToList());
                }
                else if (!_selected.Contains(node.StepId)) Select(new[] { node.StepId });
                _dragFrom = _selected.ToDictionary(id => id, id => _nodes[id].Position);
                _dragStart = evt.position;
                _dragged = false;
                node.Header.CapturePointer(evt.pointerId);
                _canvas.Focus();
                evt.StopPropagation();
            });
            node.Header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!node.Header.HasPointerCapture(evt.pointerId) || _dragFrom == null) return;
                var delta = ((Vector2)evt.position - _dragStart) / _canvas.View.Zoom;
                if (!_dragged && delta.sqrMagnitude < 9f) return;
                _dragged = true;
                foreach (var pair in _dragFrom)
                    _nodes[pair.Key].Position = Snap(pair.Value + delta);
                MoveWires();
            });
            node.Header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!node.Header.HasPointerCapture(evt.pointerId)) return;
                node.Header.ReleasePointer(evt.pointerId);
                if (_dragged && _dragFrom != null) GraphEdits.MoveSteps(_quest, _dragFrom.Keys.ToDictionary(id => id, id => _nodes[id].Position));
                _dragFrom = null;
                _dragged = false;
            });
            node.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || node.Header.Contains(evt.target as VisualElement) || node.OutPort == evt.target) return;
                if (!_selected.Contains(node.StepId) || _selected.Count > 1) Select(new[] { node.StepId });
                _canvas.Focus();
                evt.StopPropagation();
            });

            node.OutPort.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _linkFrom = node;
                _preview = new Wire { Style = WireStyle.Preview, From = node.OutPoint, To = node.OutPoint, Dashed = true };
                RebuildWires();
                node.OutPort.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            node.OutPort.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_preview == null || !node.OutPort.HasPointerCapture(evt.pointerId)) return;
                _preview.To = _canvas.ToContent(_canvas.WorldToLocal(evt.position));
                _canvas.Wires.Redraw();
            });
            node.OutPort.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_preview == null || !node.OutPort.HasPointerCapture(evt.pointerId)) return;
                node.OutPort.ReleasePointer(evt.pointerId);
                var drop = _canvas.ToContent(_canvas.WorldToLocal(evt.position));
                var target = _nodes.Values.FirstOrDefault(other => NodeRect(other).Contains(drop));
                var from = _linkFrom;
                _preview = null;
                _linkFrom = null;
                if (target == from) RebuildWires();
                else if (target != null)
                {
                    if (!GraphEdits.Link(_quest, from.StepId, target.StepId)) _notice.Show("Those steps would wait on each other, so they weren't linked.", false);
                    Rebuild();
                }
                else
                {
                    var step = GraphEdits.AddStep(_quest, drop - new Vector2(0f, StepNodeView.PortY), from.StepId);
                    Select(new[] { step.Id });
                    Rebuild();
                }
            });
        }

        private void Select(IReadOnlyCollection<string> ids)
        {
            _selected.Clear();
            _selected.UnionWith(ids);
            foreach (var node in _nodes.Values)
                node.SetSelected(_selected.Contains(node.StepId));
            if (_quest != null) QuestGraphFocus.Focus(_quest, _selected.Count == 1 ? _selected.First() : null);
        }

        private void DeleteSelection()
        {
            if (_selectedWire != null && _links.TryGetValue(_selectedWire, out var link))
            {
                GraphEdits.Unlink(_quest, link.from, link.to);
                _selectedWire = null;
                Rebuild();
                return;
            }
            if (_selected.Count == 0) return;
            var result = GraphEdits.DeleteSteps(_quest, _selected.ToList(), _index);
            var count = _selected.Count;
            Select(Array.Empty<string>());
            Rebuild();
            var message = count == 1 ? "Deleted a step." : $"Deleted {count} steps.";
            if (result.Rewired.Count > 0) message += $" {result.Rewired.Count} rewired to keep their order.";
            var readers = result.Readers.GroupBy(r => r.Reader).Select(g => g.Key).ToList();
            if (readers.Count > 0) message += " Still read by:";
            _notice.Show(message, true, readers.Select(reader => (QuestStates.TitleOf(reader), (Action)(() => OnOpenQuest(reader)))).ToArray());
        }

        private void Copy()
        {
            if (_selected.Count == 0) return;
            EditorGUIUtility.systemCopyBuffer = GraphEdits.Copy(_quest, _selected.ToList());
        }

        private void Paste(Vector2 at)
        {
            var result = GraphEdits.Paste(_quest, EditorGUIUtility.systemCopyBuffer, at);
            if (result.StepIds.Count == 0) return;
            Rebuild();
            Select(result.StepIds.ToList());
            if (result.DroppedLinks > 0) _notice.Show($"Pasted. {result.DroppedLinks} link{(result.DroppedLinks == 1 ? "" : "s")} to steps that weren't copied dropped.", true);
        }

        private void Duplicate()
        {
            if (_selected.Count == 0) return;
            var origin = _selected.Select(id => _nodes[id].Position).Aggregate(Vector2.Min);
            Copy();
            Paste(origin + new Vector2(30f, 30f));
        }

        private void BuildMenu(ContextualMenuPopulateEvent evt)
        {
            if (_quest == null) return;
            var at = _canvas.ToContent(_canvas.WorldToLocal(evt.mousePosition));
            var hasSelection = _selected.Count > 0;
            evt.menu.AppendAction("Add Step", _ =>
            {
                var step = GraphEdits.AddStep(_quest, at);
                Select(new[] { step.Id });
                Rebuild();
            });
            evt.menu.AppendAction("Paste", _ => Paste(at), GraphEdits.CanPaste(EditorGUIUtility.systemCopyBuffer) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy", _ => Copy(), hasSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Duplicate", _ => Duplicate(), hasSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Delete", _ => DeleteSelection(), hasSelection || _selectedWire != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Tidy", _ => Tidy());
            evt.menu.AppendAction("Frame All", _ => Frame(false));
            evt.StopPropagation();
        }

        private void FrameIfWaiting()
        {
            if (!_frameOnLayout || _canvas.layout.width <= 0f || _nodes.Count == 0) return;
            _frameOnLayout = false;
            _canvas.schedule.Execute(() => Frame(false));
        }

        private static Rect NodeRect(StepNodeView node) => new Rect(node.Position, new Vector2(StepNodeView.Width, Mathf.Max(node.layout.height, 40f)));

        private static Vector2 Snap(Vector2 position) => new Vector2(Mathf.Round(position.x / 10f) * 10f, Mathf.Round(position.y / 10f) * 10f);

        #endregion
    }
}
