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
     * stubs for other quests linked to its steps. Every change goes through <see cref="StepEdits" />, one Undo step
     * each; step details are edited in the Inspector.</summary>
     */
    public sealed class QuestScreen : VisualElement
    {
        #region Private Fields

        private const float WireHitDistance = 6f;

        /** <summary>The smallest zoom a quest opens at; framing everything with A can go smaller.</summary> */
        private const float OpeningZoom = 0.8f;

        /** <summary>Space kept between framed content and the canvas's edges and summary bar.</summary> */
        private const float FrameMargin = 24f;

        /** <summary>How far a pasted copy of the selection lands from the original.</summary> */
        private static readonly Vector2 DuplicateOffset = new Vector2(30f, 30f);

        private static readonly string[] Commands = { "Copy", "Paste", "Duplicate", "Delete", "SoftDelete", "FrameSelected", "FrameSelectedWithLock", "SelectAll" };

        private readonly QuestIndex _index;
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly QuestSummaryBar _summary = new QuestSummaryBar();
        private readonly StepStubs _stubs = new StepStubs();
        private readonly Notice _notice = new Notice();
        private readonly DeferredRebuild _rebuild;
        private readonly NodeDrag<string> _drag = new NodeDrag<string>();

        /** <summary>Keyed by step id, resolving to its node.</summary> */
        private readonly Dictionary<string, StepNodeView> _nodes = new Dictionary<string, StepNodeView>();

        /** <summary>Keyed by wire, resolving to the steps it links.</summary> */
        private readonly Dictionary<Wire, (string from, string to)> _links = new Dictionary<Wire, (string from, string to)>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private QuestDefinition _quest;
        private string _selectedWire;
        private Vector2 _pointer;
        private StepNodeView _linkFrom;
        private Vector2 _linkPress;
        private Wire _preview;
        private bool _frameOnLayout;

        #endregion

        #region Properties

        /** <summary>The quest shown.</summary> */
        public QuestDefinition Quest => _quest;

        /** <summary>The canvas, for the window's view state.</summary> */
        public GraphCanvas Canvas => _canvas;

        /** <summary>How many steps and objectives are shown, and how many problems, for the status bar.</summary> */
        public QuestCounts Counts { get; private set; }

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
            _rebuild = new DeferredRebuild(this, Rebuild);
            AddToClassList("quest-graph__screen");
            Add(_canvas);
            Add(_summary);
            Add(_notice);
            _summary.OnOpenQuest += quest => OnOpenQuest(quest);
            _summary.OnClicked += () => Select(Array.Empty<string>());
            _stubs.OnOpenQuest += quest => OnOpenQuest(quest);

            _canvas.OnBackgroundClicked += HandleBackgroundClicked;
            _canvas.OnMarquee += HandleMarquee;
            _canvas.RegisterCallback<PointerDownEvent>(_ => _notice.Hide(), TrickleDown.TrickleDown);
            _canvas.RegisterCallback<PointerMoveEvent>(evt => _pointer = _canvas.ToContent(_canvas.WorldToLocal(evt.position)), TrickleDown.TrickleDown);
            _canvas.RegisterCallback<ValidateCommandEvent>(HandleValidateCommand);
            _canvas.RegisterCallback<ExecuteCommandEvent>(HandleExecuteCommand);
            _canvas.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
            _summary.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
            _canvas.AddManipulator(new ContextualMenuManipulator(BuildMenu));
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += _rebuild.Request;
                _index.OnChanged += HandleIndexChanged;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= _rebuild.Request;
                _index.OnChanged -= HandleIndexChanged;
            });
        }

        /** <summary>Shows a quest at a view, or framed to fit without one. Anything half done is dropped.</summary> */
        public void Show(QuestDefinition quest, CanvasView? view = null)
        {
            _quest = quest;
            _selected.Clear();
            _selectedWire = null;
            _drag.End();
            _linkFrom = null;
            _preview = null;
            _notice.Hide();
            _frameOnLayout = !view.HasValue;
            Rebuild();
            if (view.HasValue) _canvas.SetView(view.Value);
            else FrameIfWaiting();
        }

        /** <summary>Redraws on the next update, once however often it is asked.</summary> */
        public void RequestRebuild() => _rebuild.Request();

        /** <summary>Redraws everything from the quest now, keeping the view and the selection.</summary> */
        public void Rebuild()
        {
            _rebuild.Cancel();
            _canvas.Nodes.Clear();
            _canvas.Labels.Clear();
            _nodes.Clear();
            _links.Clear();
            if (_quest == null)
            {
                _summary.Show(null, null, null);
                _canvas.Wires.SetWires(Array.Empty<Wire>());
                Counts = default;
                OnRebuilt();
                return;
            }

            var summary = _index.SummaryOf(_quest) ?? QuestSummary.Of(_quest);
            var readers = _index.ReadersOf(_quest);
            _summary.Show(_quest, summary, readers);
            var cards = StepCard.Of(_quest, summary.Issues);
            foreach (var card in cards)
            {
                var node = new StepNodeView(card);
                _nodes[card.Id] = node;
                node.SetSelected(_selected.Contains(card.Id));
                node.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
                HookNode(node);
                _canvas.Nodes.Add(node);
            }
            _selected.IntersectWith(_nodes.Keys);
            PlaceNodes();
            _stubs.Build(_canvas.Nodes, _quest, summary, readers, _nodes);
            RebuildWires();
            Counts = new QuestCounts(cards.Count, cards.Sum(card => card.Objectives.Count), summary.Errors, summary.Warnings);
            _canvas.schedule.Execute(_canvas.Cull);
            OnRebuilt();
        }

        /**
         * <summary>Frames the selected steps, or everything (stubs included) when none is selected, below the summary
         * bar. A quest opening never goes below 80%.</summary>
         */
        public void Frame(bool selectionOnly = true, bool opening = false)
        {
            if (!_canvas.HasSize) return;
            var rects = selectionOnly && _selected.Count > 0
                ? _selected.Select(id => NodeRect(_nodes[id])).ToList()
                : _nodes.Values.Select(NodeRect).Concat(_stubs.Rects).ToList();
            if (rects.Count == 0) return;
            _canvas.SetView(CanvasView.FramingBelow(CanvasView.Union(rects), _canvas.layout.size, _summary.layout.yMax + FrameMargin, FrameMargin, opening ? OpeningZoom : CanvasView.MinZoom));
        }

        /** <summary>Lays the steps out again and saves where they went, as one Undo step.</summary> */
        public void Tidy()
        {
            if (_quest == null) return;
            StepEdits.MoveSteps(_quest, StepLayout.Arrange(_quest, HeightOf));
            AfterEdit();
        }

        /** <summary>Adds a step after the selected step, or where the pointer last was.</summary> */
        public void AddStep()
        {
            if (_quest == null) return;
            var after = _selected.Count == 1 ? _selected.First() : null;
            AddStep(after != null ? _nodes[after].Position + new Vector2(StepLayout.ColumnWidth, 0f) : _pointer, after);
        }

        /** <summary>Hides the notice, as when the user turns to something else.</summary> */
        public void HideNotice() => _notice.Hide();

        #endregion

        #region Event Handlers

        private void HandleIndexChanged(QuestDefinition quest)
        {
            if (_quest != null) _rebuild.Request();
        }

        private void HandleBackgroundClicked(Vector2 point, bool additive)
        {
            var wire = _canvas.Wires.WireAt(point, WireHitDistance / _canvas.View.Zoom);
            if (!additive) Select(Array.Empty<string>());
            SelectWire(wire != null && _links.ContainsKey(wire) ? wire.Key : null);
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

        #region Helpers

        /** <summary>Reads the quest again after a change made here, then redraws.</summary> */
        private void AfterEdit()
        {
            _index.Refresh(_quest);
            Rebuild();
        }

        /** <summary>Puts each step where it was saved, or where the automatic layout puts it in a quest never arranged.</summary> */
        private void PlaceNodes()
        {
            var arranged = StepLayout.NeedsLayout(_quest) ? StepLayout.Arrange(_quest, EstimatedHeight) : null;
            foreach (var step in _quest.Steps)
            {
                if (step == null || !_nodes.TryGetValue(step.Id, out var node)) continue;
                node.Position = arranged != null && arranged.TryGetValue(step.Id, out var at) ? at : step.GraphPosition;
            }
        }

        /** <summary>Where every step is drawn now, for edits to save in a quest never arranged; null once it has been.</summary> */
        private Dictionary<string, Vector2> Arrangement() => _quest.GraphArranged ? null : Positions();

        private Dictionary<string, Vector2> Positions() => _nodes.ToDictionary(pair => pair.Key, pair => pair.Value.Position);

        /** <summary>A step's height before it has been measured, from what it shows.</summary> */
        private float EstimatedHeight(string stepId)
        {
            var step = _quest.FindStep(stepId);
            var rules = (step.UnlockWhen != null ? 1 : 0) + (step.FailWhen != null ? 1 : 0);
            return StepNodeView.EstimatedHeight(step.Objectives.Count, rules);
        }

        private float HeightOf(string stepId) => _nodes.TryGetValue(stepId, out var node) && CanvasView.HasSize(node.layout.size) ? node.layout.height : EstimatedHeight(stepId);

        private void RebuildWires()
        {
            var wires = new List<Wire>();
            var record = new QuestRecord(_quest);
            foreach (var step in record.Steps)
            {
                var definition = step.Definition;
                if (!_nodes.TryGetValue(definition.Id, out var to) || (definition.Start == StepStart.AfterStepAbove && definition.UnlockWhen != null)) continue;
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
                    wire.Selected = wire.Key == _selectedWire;
                    _links[wire] = (before.Definition.Id, definition.Id);
                    wires.Add(wire);
                }
            }
            if (_selectedWire != null && !wires.Any(wire => wire.Selected)) _selectedWire = null;
            wires.AddRange(_stubs.Wires);
            if (_preview != null) wires.Add(_preview);
            _canvas.Wires.SetWires(wires);
        }

        /** <summary>Moves wires and stubs after nodes moved.</summary> */
        private void MoveWires()
        {
            foreach (var pair in _links)
            {
                pair.Key.From = _nodes[pair.Value.from].OutPoint;
                pair.Key.To = _nodes[pair.Value.to].InPoint;
            }
            _stubs.Place();
            _canvas.Wires.Redraw();
        }

        private void HookNode(StepNodeView node)
        {
            node.Header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse) return;
                if (evt.shiftKey || evt.actionKey)
                {
                    var next = new HashSet<string>(_selected);
                    if (!next.Remove(node.StepId)) next.Add(node.StepId);
                    Select(next.ToList());
                }
                else if (!_selected.Contains(node.StepId)) Select(new[] { node.StepId });
                else QuestGraphFocus.Focus(_quest, _selected.Count == 1 ? node.StepId : null);
                _drag.Begin(_selected.Select(id => new KeyValuePair<string, Vector2>(id, _nodes[id].Position)), evt.position);
                node.Header.CapturePointer(evt.pointerId);
                _canvas.Focus();
                evt.StopPropagation();
            });
            node.Header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!node.Header.HasPointerCapture(evt.pointerId)) return;
                var moved = _drag.Move(evt.position, _canvas.View.Zoom);
                if (moved == null) return;
                foreach (var pair in moved)
                    _nodes[pair.Key].Position = pair.Value;
                MoveWires();
            });
            node.Header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!node.Header.HasPointerCapture(evt.pointerId)) return;
                var moved = _drag.End();
                node.Header.ReleasePointer(evt.pointerId);
                if (!moved) return;
                StepEdits.MoveSteps(_quest, Positions());
                AfterEdit();
            });
            node.Header.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_drag.End()) Rebuild();
            });
            node.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse || node.Header.Contains(evt.target as VisualElement) || node.OutPort == evt.target) return;
                if (!_selected.Contains(node.StepId) || _selected.Count > 1) Select(new[] { node.StepId });
                else QuestGraphFocus.Focus(_quest, node.StepId);
                _canvas.Focus();
                evt.StopPropagation();
            });

            node.OutPort.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse) return;
                _linkFrom = node;
                _linkPress = evt.position;
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
                var from = _linkFrom;
                var clicked = ((Vector2)evt.position - _linkPress).magnitude < GraphMetrics.ClickThreshold;
                EndLinkDrag();
                node.OutPort.ReleasePointer(evt.pointerId);
                if (clicked) return;
                var drop = _canvas.ToContent(_canvas.WorldToLocal(evt.position));
                var target = _nodes.Values.FirstOrDefault(other => DropRect(other).Contains(drop));
                if (target == from) return;
                if (target == null) AddStep(drop - new Vector2(0f, StepNodeView.PortY), from.StepId);
                else if (StepEdits.Link(_quest, from.StepId, target.StepId)) AfterEdit();
                else _notice.Show("Those steps would wait on each other, so they weren't linked.", false);
            });
            node.OutPort.RegisterCallback<PointerCaptureOutEvent>(_ => EndLinkDrag());
        }

        /** <summary>Drops a link being drawn, taking its preview wire away.</summary> */
        private void EndLinkDrag()
        {
            if (_preview == null) return;
            _preview = null;
            _linkFrom = null;
            RebuildWires();
        }

        private void AddStep(Vector2 at, string after)
        {
            var step = StepEdits.AddStep(_quest, at, after, Arrangement());
            AfterEdit();
            Select(new[] { step.Id });
        }

        private void Select(IReadOnlyCollection<string> ids)
        {
            _selected.Clear();
            _selected.UnionWith(ids);
            foreach (var node in _nodes.Values)
                node.SetSelected(_selected.Contains(node.StepId));
            SelectWire(null);
            if (_quest != null) QuestGraphFocus.Focus(_quest, _selected.Count == 1 ? _selected.First() : null);
        }

        private void SelectWire(string key)
        {
            if (_selectedWire == key) return;
            _selectedWire = key;
            foreach (var wire in _links.Keys)
                wire.Selected = wire.Key == key;
            _canvas.Wires.Redraw();
        }

        private void DeleteSelection()
        {
            if (_selectedWire != null)
            {
                var link = _links.First(pair => pair.Key.Key == _selectedWire).Value;
                StepEdits.Unlink(_quest, link.from, link.to);
                _selectedWire = null;
                AfterEdit();
                return;
            }
            if (_selected.Count == 0) return;
            var count = _selected.Count;
            var result = StepEdits.DeleteSteps(_quest, _selected.ToList(), _index);
            Select(Array.Empty<string>());
            AfterEdit();
            var message = count == 1 ? "Deleted a step." : $"Deleted {count} steps.";
            if (result.Rewired.Count > 0) message += $" {result.Rewired.Count} rewired to keep their order.";
            var readers = result.Readers.Select(link => link.Reader).Distinct().ToList();
            if (readers.Count > 0) message += " Still read by:";
            _notice.Show(message, true, readers.Select(reader => (QuestStates.TitleOf(reader), (Action)(() => OnOpenQuest(reader)))).ToArray());
        }

        private void Copy()
        {
            if (_selected.Count == 0) return;
            EditorGUIUtility.systemCopyBuffer = StepEdits.Copy(_quest, _selected.ToList());
        }

        private void Paste(Vector2 at)
        {
            var result = StepEdits.Paste(_quest, EditorGUIUtility.systemCopyBuffer, at, Arrangement());
            if (result.StepIds.Count == 0) return;
            AfterEdit();
            Select(result.StepIds.ToList());
            if (result.DroppedLinks > 0) _notice.Show($"Pasted. {result.DroppedLinks} link{(result.DroppedLinks == 1 ? "" : "s")} to steps that weren't copied dropped.", true);
        }

        private void Duplicate()
        {
            if (_selected.Count == 0) return;
            var origin = _selected.Select(id => _nodes[id].Position).Aggregate(Vector2.Min);
            Copy();
            Paste(origin + DuplicateOffset);
        }

        private void BuildMenu(ContextualMenuPopulateEvent evt)
        {
            if (_quest == null) return;
            var at = _canvas.ToContent(_canvas.WorldToLocal(evt.mousePosition));
            var hasSelection = _selected.Count > 0;
            evt.menu.AppendAction("Add Step", _ => AddStep(at, null));
            evt.menu.AppendAction("Paste", _ => Paste(at), StepEdits.CanPaste(EditorGUIUtility.systemCopyBuffer) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy", _ => Copy(), hasSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Duplicate", _ => Duplicate(), hasSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Delete", _ => DeleteSelection(), hasSelection || _selectedWire != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Tidy", _ => Tidy());
            evt.menu.AppendAction("Frame All", _ => Frame(false));
            evt.StopPropagation();
        }

        /** <summary>Frames the quest once everything in it has been measured, if a frame is waiting.</summary> */
        private void FrameIfWaiting()
        {
            if (!_frameOnLayout || !_canvas.HasSize || _nodes.Count == 0 || !CanvasView.HasSize(_summary.layout.size)) return;
            if (_nodes.Values.Any(node => !CanvasView.HasSize(node.layout.size))) return;
            _frameOnLayout = false;
            _canvas.schedule.Execute(() => Frame(false, true));
        }

        private static Rect NodeRect(StepNodeView node) =>
            new Rect(node.Position, new Vector2(StepNodeView.Width, CanvasView.HasSize(node.layout.size) ? node.layout.height : StepNodeView.MinHeight));

        /** <summary>Where a link dropped counts as on a node: the node with a little room around it.</summary> */
        private static Rect DropRect(StepNodeView node)
        {
            var rect = NodeRect(node);
            return new Rect(rect.x - GraphMetrics.DropReach, rect.y - GraphMetrics.DropReach, rect.width + GraphMetrics.DropReach * 2f, rect.height + GraphMetrics.DropReach * 2f);
        }

        #endregion
    }
}
