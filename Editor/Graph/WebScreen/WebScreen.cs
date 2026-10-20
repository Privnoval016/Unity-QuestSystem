using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The whole-quest web: every quest in bands by kind, wired by the conditions that read other quests. Two
     * views (dependencies, story flags), a search, filters, focus on a quest's chain, and editing: drag to pin, draw a
     * link to make a quest need another, bulk edits, and new quests.</summary>
     */
    public sealed class WebScreen : VisualElement
    {
        #region Private Fields

        /** <summary>How far bands reach past the last column.</summary> */
        private const float BandWidthPastLastColumn = 400f;

        /** <summary>The narrowest a band is drawn.</summary> */
        private const float MinBandWidth = 1200f;

        /** <summary>Space kept between framed content and the canvas's edges.</summary> */
        private const float FrameMargin = 20f;

        /** <summary>Space below a quest before the preview of a link drawn to it.</summary> */
        private const float PreviewGap = 6f;

        /** <summary>Padding either side of a wire label's text, and its height.</summary> */
        private const float LabelPadding = 16f, LabelHeight = 16f;

        /** <summary>A label's width per character when its text can't be measured yet.</summary> */
        private const float LabelCharWidth = 6.2f;

        private readonly QuestIndex _index;
        private readonly WebFilterBar _bar = new WebFilterBar();
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly BulkEditPanel _bulk = new BulkEditPanel();
        private readonly VisualElement _empty = new VisualElement();
        private readonly Notice _notice = new Notice();
        private readonly DeferredRebuild _rebuild;
        private readonly NodeDrag<QuestDefinition> _drag = new NodeDrag<QuestDefinition>();

        /** <summary>Keyed by quest, resolving to its node.</summary> */
        private readonly Dictionary<QuestDefinition, QuestNodeView> _nodes = new Dictionary<QuestDefinition, QuestNodeView>();
        private readonly List<LinkWire> _wires = new List<LinkWire>();

        /** <summary>The story-flag view's wires, each from its flag to a quest that reads it.</summary> */
        private readonly List<FlagWire> _flagWires = new List<FlagWire>();

        /** <summary>Keyed by quest, resolving to the quests it leads to.</summary> */
        private readonly Dictionary<QuestDefinition, List<QuestDefinition>> _leadsTo = new Dictionary<QuestDefinition, List<QuestDefinition>>();

        /** <summary>Keyed by quest, resolving to the quests it comes from.</summary> */
        private readonly Dictionary<QuestDefinition, List<QuestDefinition>> _comesFrom = new Dictionary<QuestDefinition, List<QuestDefinition>>();
        private readonly HashSet<QuestDefinition> _selected = new HashSet<QuestDefinition>();
        private readonly List<Rect> _flagRects = new List<Rect>();
        private List<QuestKind> _kinds = new List<QuestKind>();
        private List<WebBand> _bands = new List<WebBand>();
        private QuestDefinition _focus;
        private Wire _preview;
        private Vector2 _linkPress;
        private QuestNodeView _previewTarget;
        private Label _previewText;
        private bool _frameOnLayout = true;

        #endregion

        #region Properties

        /** <summary>The canvas, for the window's view state.</summary> */
        public GraphCanvas Canvas => _canvas;

        /** <summary>Counts for the status bar.</summary> */
        public WebCounts Counts { get; private set; }

        /** <summary>Raised when a quest should open on its own screen.</summary> */
        public event Action<QuestDefinition> OnOpenQuest = delegate { };

        /** <summary>Raised after the web redraws or refilters.</summary> */
        public event Action OnRebuilt = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes the web over an index.</summary> */
        public WebScreen(QuestIndex index)
        {
            _index = index;
            _rebuild = new DeferredRebuild(this, Rebuild);
            AddToClassList("quest-graph__screen");
            Add(_bar);
            Add(_canvas);
            Add(_bulk);
            _empty.AddToClassList("quest-graph__empty");
            _empty.pickingMode = PickingMode.Ignore;
            Add(_empty);
            Add(_notice);

            _bar.OnFiltersChanged += ApplyFilters;
            _bar.OnViewChanged += Rebuild;
            _bulk.OnEdited += AfterEdit;
            _bulk.OnRefused += () => _notice.Show("That asset has no display name to show.", false);
            _canvas.OnBackgroundClicked += (_, additive) =>
            {
                if (!additive) Select(Array.Empty<QuestDefinition>(), null);
            };
            _canvas.OnMarquee += (box, additive) =>
            {
                var inside = _nodes.Values.Where(node => Shown(node) && node.Rect.Overlaps(box)).Select(node => node.Quest);
                Select(additive ? _selected.Union(inside).ToList() : inside.ToList(), null);
            };
            _canvas.RegisterCallback<PointerDownEvent>(_ => _notice.Hide(), TrickleDown.TrickleDown);
            _canvas.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _canvas.AddManipulator(new ContextualMenuManipulator(BuildMenu));
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
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

        /** <summary>Shows the web at a view, or framed to fit without one. Anything half done is dropped.</summary> */
        public void Show(CanvasView? view)
        {
            _drag.End();
            EndLinkDrag();
            _notice.Hide();
            _frameOnLayout = !view.HasValue;
            Rebuild();
            if (view.HasValue) _canvas.SetView(view.Value);
            else FrameIfWaiting();
        }

        /** <summary>Redraws on the next update, once however often it is asked.</summary> */
        public void RequestRebuild() => _rebuild.Request();

        /** <summary>Redraws everything from the index now, keeping the view, the selection and the filters.</summary> */
        public void Rebuild()
        {
            _rebuild.Cancel();
            _canvas.Background.Clear();
            _canvas.Nodes.Clear();
            _canvas.Labels.Clear();
            _nodes.Clear();
            _wires.Clear();
            _flagWires.Clear();
            _flagRects.Clear();
            _previewText = null;
            _previewTarget = null;
            var quests = _index.Quests.Where(quest => quest != null).ToList();
            var known = new HashSet<QuestDefinition>(quests);
            _selected.RemoveWhere(quest => !known.Contains(quest));
            if (_focus != null && !known.Contains(_focus)) _focus = null;
            var links = WebLinks.Of(_index, quests);
            MapLinks(links);
            var (positions, bands) = WebLayout.Arrange(quests, links);
            _bands = bands;
            _kinds = quests.Select(quest => quest.Kind).Where(kind => kind != null).Distinct().OrderBy(kind => kind.Order).ThenBy(QuestStates.NameOf).ToList();
            _bar.SetKinds(_kinds);

            var right = Mathf.Max(positions.Count == 0 ? 0f : positions.Values.Max(position => position.x) + WebLayout.NodeWidth + BandWidthPastLastColumn, MinBandWidth);
            foreach (var band in bands)
                _canvas.Background.Add(BandElement(band, right));
            foreach (var quest in quests)
            {
                var node = new QuestNodeView(quest, _index.SummaryOf(quest), WebLinks.ReadsItself(_index, quest)) { Position = positions[quest] };
                node.SetSelected(_selected.Contains(quest));
                HookNode(node);
                _nodes[quest] = node;
                _canvas.Nodes.Add(node);
            }
            foreach (var link in links)
            {
                var label = new Label(link.Label);
                label.AddToClassList("quest-graph__label");
                label.EnableInClassList("quest-graph__label--fails", link.Fails);
                _canvas.Labels.Add(label);
                _wires.Add(new LinkWire(new Wire { Style = link.Fails ? WireStyle.Fails : WireStyle.Normal, Dashed = link.Fails, Arrow = true }, link, label));
            }
            if (_bar.View == WebView.StoryFlags) BuildFlags(quests);
            PlaceWires(true);
            Counts = new WebCounts(quests.Count, quests.Count, links.Count, quests.Sum(quest => _index.SummaryOf(quest)?.Errors ?? 0),
                quests.Sum(quest => _index.SummaryOf(quest)?.Warnings ?? 0), quests.Sum(quest => _index.SummaryOf(quest)?.Unreadable ?? 0));
            _bulk.Show(_selected, _kinds);
            ApplyFilters();
            _canvas.schedule.Execute(_canvas.Cull);
        }

        /** <summary>Frames the selection, or everything shown (with the bands' names and story flags) when none is selected.</summary> */
        public void Frame(bool selectionOnly = true)
        {
            if (!_canvas.HasSize) return;
            var all = !selectionOnly || _selected.Count == 0;
            var rects = (all ? _nodes.Values.Where(Shown) : _selected.Select(quest => _nodes[quest])).Select(node => node.Rect).ToList();
            if (rects.Count == 0) return;
            if (all)
            {
                rects.AddRange(_flagRects);
                if (_bands.Count > 0) rects.Add(new Rect(0f, _bands[0].Top, 1f, 1f));
            }
            _canvas.SetView(CanvasView.FramingBelow(CanvasView.Union(rects), _canvas.layout.size, FrameMargin, FrameMargin, CanvasView.MinZoom));
        }

        /** <summary>Lays out every quest that isn't pinned again; pinned quests stay.</summary> */
        public void Tidy() => Rebuild();

        /** <summary>Lets every selected quest (or all, with none selected) be laid out again.</summary> */
        public void UnpinSelection()
        {
            var quests = _selected.Count > 0 ? _selected.ToList() : _nodes.Keys.ToList();
            QuestEdits.Unpin(quests);
            AfterEdit(quests);
        }

        /** <summary>Hides the notice, as when the user turns to something else.</summary> */
        public void HideNotice() => _notice.Hide();

        #endregion

        #region Event Handlers

        private void HandleIndexChanged(QuestDefinition quest) => _rebuild.Request();

        private void HandleKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.F:
                    Frame();
                    break;
                case KeyCode.A when !evt.actionKey:
                    Frame(false);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_selected.Count == 1) OnOpenQuest(_selected.First());
                    break;
                case KeyCode.Escape:
                    Select(Array.Empty<QuestDefinition>(), null);
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        #endregion

        #region Helpers

        /** <summary>Reads quests changed here again, then redraws.</summary> */
        private void AfterEdit(IReadOnlyList<QuestDefinition> quests)
        {
            using (_index.Batch())
                foreach (var quest in quests)
                    _index.Refresh(quest);
            Rebuild();
        }

        /** <summary>Who leads to whom, for focusing on a quest's links or chain.</summary> */
        private void MapLinks(List<WebLink> links)
        {
            _leadsTo.Clear();
            _comesFrom.Clear();
            foreach (var link in links)
            {
                AddTo(_leadsTo, link.From, link.To);
                AddTo(_comesFrom, link.To, link.From);
            }
        }

        private static void AddTo<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<TValue>();
            list.Add(value);
        }

        private static VisualElement BandElement(WebBand band, float width)
        {
            var element = new VisualElement();
            element.AddToClassList("quest-graph__band");
            element.style.top = band.Top;
            element.style.height = band.Height;
            element.style.width = width;
            var name = new Label(QuestStates.NameOf(band.Kind));
            name.AddToClassList("quest-graph__band-name");
            name.AddToClassList(KindLooks.ClassOf(band.Kind));
            element.Add(name);
            return element;
        }

        /** <summary>Adds a node per story flag, in rows above the bands, wired to each quest that reads it.</summary> */
        private void BuildFlags(List<QuestDefinition> quests)
        {
            var readers = new Dictionary<StoryFlag, List<QuestDefinition>>();
            foreach (var quest in quests)
                foreach (var flag in _index.SummaryOf(quest)?.Flags ?? Array.Empty<StoryFlag>())
                    AddTo(readers, flag, quest);
            var ordered = readers.Select(pair => (flag: pair.Key, readers: pair.Value, firstX: pair.Value.Min(quest => _nodes[quest].Position.x)))
                .OrderBy(entry => entry.firstX).ThenBy(entry => entry.flag.name).ToList();
            var places = FlagRows.Place(ordered.Select(entry => entry.firstX).ToList(), _bands.Count > 0 ? _bands[0].Top : 0f);
            for (var i = 0; i < ordered.Count; i++)
            {
                var (flag, flagReaders, _) = ordered[i];
                var rect = new Rect(places[i], new Vector2(FlagRows.FlagWidth, FlagRows.FlagHeight));
                var node = new Label(flag.name) { tooltip = $"{flag.name}\nRead by {string.Join(", ", flagReaders.Select(QuestStates.TitleOf))}" };
                node.AddToClassList("quest-graph__flag");
                node.style.left = rect.x;
                node.style.top = rect.y;
                node.style.width = rect.width;
                node.style.height = rect.height;
                _canvas.Nodes.Add(node);
                _flagRects.Add(rect);
                foreach (var quest in flagReaders)
                    _flagWires.Add(new FlagWire(new Wire { Style = WireStyle.Flag, Arrow = true, From = new Vector2(rect.xMax, rect.center.y) }, quest));
            }
        }

        /** <summary>Puts every wire on its ports, spread along each node's edge, and (when asked) places the labels.</summary> */
        private void PlaceWires(bool placeLabels)
        {
            var outgoing = new Dictionary<QuestDefinition, List<(Wire wire, float otherY)>>();
            var incoming = new Dictionary<QuestDefinition, List<(Wire wire, float otherY)>>();
            foreach (var entry in _wires)
            {
                AddPort(outgoing, entry.Link.From, entry.Wire, _nodes[entry.Link.To].Position.y);
                AddPort(incoming, entry.Link.To, entry.Wire, _nodes[entry.Link.From].Position.y);
            }
            foreach (var entry in _flagWires)
                AddPort(incoming, entry.Reader, entry.Wire, entry.Wire.From.y);
            foreach (var pair in outgoing)
                Spread(pair.Value, _nodes[pair.Key], true);
            foreach (var pair in incoming)
                Spread(pair.Value, _nodes[pair.Key], false);
            var wires = _wires.Select(entry => entry.Wire).Concat(_flagWires.Select(entry => entry.Wire));
            _canvas.Wires.SetWires(_preview == null ? wires : wires.Append(_preview));
            if (placeLabels) PlaceLabels();
        }

        private static void AddPort(Dictionary<QuestDefinition, List<(Wire wire, float otherY)>> map, QuestDefinition quest, Wire wire, float otherY)
        {
            if (!map.TryGetValue(quest, out var list)) map[quest] = list = new List<(Wire, float)>();
            list.Add((wire, otherY));
        }

        /** <summary>Spreads a node's wires down its edge in the order of where their other ends sit, so they don't cross at the node.</summary> */
        private static void Spread(List<(Wire wire, float otherY)> ports, QuestNodeView node, bool outgoing)
        {
            var sorted = ports.OrderBy(port => port.otherY).ToList();
            var ys = PortLayout.Spread(sorted.Count, node.Position.y, WebLayout.NodeHeight);
            for (var i = 0; i < sorted.Count; i++)
            {
                if (outgoing) sorted[i].wire.From = new Vector2(node.Position.x + WebLayout.NodeWidth, ys[i]);
                else sorted[i].wire.To = new Vector2(node.Position.x, ys[i]);
            }
        }

        /** <summary>Places labels once over every wire and node, so focus, search and filters never move them.</summary> */
        private void PlaceLabels()
        {
            var requests = _wires.Select(entry => new LabelRequest(entry.Wire.From, entry.Wire.To, LabelSize(entry.Label))).ToList();
            var centres = LabelPlacer.Place(requests, _nodes.Values.Select(node => node.Rect).Concat(_flagRects).ToList());
            for (var i = 0; i < _wires.Count; i++)
            {
                var size = requests[i].Size;
                var label = _wires[i].Label;
                label.style.left = centres[i].x - size.x / 2f;
                label.style.top = centres[i].y - size.y / 2f;
                label.style.width = size.x;
            }
        }

        private static Vector2 LabelSize(Label label)
        {
            var text = label.MeasureTextSize(label.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
            var width = float.IsFinite(text.x) && text.x > 0f ? text.x : label.text.Length * LabelCharWidth;
            return new Vector2(Mathf.Ceil(width) + LabelPadding, LabelHeight);
        }

        private static bool Shown(QuestNodeView node) => node.style.display != DisplayStyle.None;

        /** <summary>Shows, hides and fades quests, wires and labels for the filters, the search, the view and the focus.</summary> */
        private void ApplyFilters()
        {
            var filters = _bar.Filters;
            var related = _focus == null ? null : Related(_focus, _bar.Chain);
            var flagsView = _bar.View == WebView.StoryFlags;
            foreach (var pair in _nodes)
            {
                var summary = _index.SummaryOf(pair.Key);
                var match = filters.Matches(pair.Key);
                pair.Value.style.display = filters.Shows(pair.Key, summary) ? DisplayStyle.Flex : DisplayStyle.None;
                pair.Value.SetMatch(match);
                pair.Value.SetFaded((related != null && !related.Contains(pair.Key)) || (filters.Searching && !match) || (flagsView && (summary?.Flags.Count ?? 0) == 0));
            }
            foreach (var entry in _wires)
            {
                var visible = Shown(_nodes[entry.Link.From]) && Shown(_nodes[entry.Link.To]);
                var live = related == null || (related.Contains(entry.Link.From) && related.Contains(entry.Link.To));
                entry.Wire.Hidden = !visible;
                entry.Wire.Faded = !live || flagsView;
                entry.Label.style.display = visible && live && !flagsView ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (var entry in _flagWires)
            {
                entry.Wire.Hidden = !Shown(_nodes[entry.Reader]);
                entry.Wire.Faded = related != null && !related.Contains(entry.Reader);
            }
            _canvas.Wires.Redraw();
            _bar.ShowFocusDepth(_focus != null);
            Counts = Counts.WithShown(_nodes.Values.Count(Shown));
            ShowEmpty();
            OnRebuilt();
        }

        /** <summary>A quest and the quests it is linked to: directly, or (with the chain) everything before and after it.</summary> */
        private HashSet<QuestDefinition> Related(QuestDefinition quest, bool chain)
        {
            var related = new HashSet<QuestDefinition> { quest };
            foreach (var map in new[] { _leadsTo, _comesFrom })
            {
                var open = new Stack<QuestDefinition>();
                open.Push(quest);
                while (open.Count > 0)
                {
                    var current = open.Pop();
                    if (!map.TryGetValue(current, out var next)) continue;
                    foreach (var other in next)
                        if (related.Add(other) && chain) open.Push(other);
                }
            }
            return related;
        }

        private void ShowEmpty()
        {
            _empty.Clear();
            var nothingShown = _nodes.Values.All(node => !Shown(node));
            _empty.style.display = nothingShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!nothingShown) return;
            _empty.Add(new Label(_nodes.Count == 0 ? "No quests" : "No quests match"));
            if (_nodes.Count > 0) _empty.Add(new Button(_bar.ClearFilters) { text = "Clear filters" });
        }

        private void HookNode(QuestNodeView node)
        {
            node.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse || evt.target == node.Handle) return;
                if (evt.clickCount == 2)
                {
                    OnOpenQuest(node.Quest);
                    evt.StopPropagation();
                    return;
                }
                if (evt.shiftKey || evt.actionKey)
                {
                    var next = new HashSet<QuestDefinition>(_selected);
                    if (!next.Remove(node.Quest)) next.Add(node.Quest);
                    Select(next.ToList(), null);
                }
                else if (!_selected.Contains(node.Quest) || _selected.Count == 1) Select(new[] { node.Quest }, node.Quest);
                _drag.Begin(_selected.Select(quest => new KeyValuePair<QuestDefinition, Vector2>(quest, _nodes[quest].Position)), evt.position);
                node.CapturePointer(evt.pointerId);
                _canvas.Focus();
                evt.StopPropagation();
            });
            node.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!node.HasPointerCapture(evt.pointerId)) return;
                var moved = _drag.Move(evt.position, _canvas.View.Zoom);
                if (moved == null) return;
                foreach (var pair in moved)
                    _nodes[pair.Key].Position = pair.Value;
                PlaceWires(false);
            });
            node.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!node.HasPointerCapture(evt.pointerId)) return;
                var keys = _drag.Keys.ToList();
                var moved = _drag.End();
                node.ReleasePointer(evt.pointerId);
                if (!moved) return;
                QuestEdits.MoveQuests(keys.ToDictionary(quest => quest, quest => _nodes[quest].Position));
                AfterEdit(keys);
            });
            node.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_drag.End()) Rebuild();
            });

            node.Handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.LeftMouse) return;
                var from = new Vector2(node.Position.x + WebLayout.NodeWidth, node.Position.y + WebLayout.NodeHeight / 2f);
                _preview = new Wire { Style = WireStyle.Preview, Dashed = true, Arrow = true, From = from, To = from };
                _linkPress = evt.position;
                node.Handle.CapturePointer(evt.pointerId);
                PlaceWires(false);
                evt.StopPropagation();
            });
            node.Handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_preview == null || !node.Handle.HasPointerCapture(evt.pointerId)) return;
                var at = _canvas.ToContent(_canvas.WorldToLocal(evt.position));
                _preview.To = at;
                var target = _nodes.Values.FirstOrDefault(other => other != node && Shown(other) && DropRect(other).Contains(at));
                if (target != _previewTarget) ShowPreview(node.Quest, target);
                _canvas.Wires.Redraw();
            });
            node.Handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_preview == null || !node.Handle.HasPointerCapture(evt.pointerId)) return;
                var target = ((Vector2)evt.position - _linkPress).magnitude < GraphMetrics.ClickThreshold ? null : _previewTarget;
                EndLinkDrag();
                node.Handle.ReleasePointer(evt.pointerId);
                if (target != null) LinkQuests(target.Quest, node.Quest);
            });
            node.Handle.RegisterCallback<PointerCaptureOutEvent>(_ => EndLinkDrag());
        }

        /** <summary>Makes a quest available only once another is done, unless that would make them need each other.</summary> */
        private void LinkQuests(QuestDefinition target, QuestDefinition needs)
        {
            if (QuestEdits.WouldLoop(_index, target, needs))
            {
                _notice.Show("Those quests would need each other, so they weren't linked.", false);
                return;
            }
            if (!QuestEdits.AddAvailability(target, needs))
            {
                _notice.Show($"{QuestStates.TitleOf(target)} already needs {QuestStates.TitleOf(needs)} done.", false);
                return;
            }
            AfterEdit(new[] { target });
            _notice.Show($"{QuestStates.TitleOf(target)} now needs {QuestStates.TitleOf(needs)} done.", true);
        }

        /** <summary>Drops a link being drawn, taking its preview away.</summary> */
        private void EndLinkDrag()
        {
            if (_preview == null) return;
            ShowPreview(null, null);
            _preview = null;
            PlaceWires(false);
        }

        /** <summary>Shows what a link would write on the quest under the pointer.</summary> */
        private void ShowPreview(QuestDefinition needs, QuestNodeView target)
        {
            _previewText?.RemoveFromHierarchy();
            _previewText = null;
            _previewTarget = target;
            if (target == null || needs == null) return;
            _previewText = new Label("Available when " + QuestEdits.PreviewAvailability(target.Quest, needs));
            _previewText.AddToClassList("quest-graph__preview");
            _previewText.style.left = target.Position.x;
            _previewText.style.top = target.Rect.yMax + PreviewGap;
            _canvas.Labels.Add(_previewText);
        }

        private void Select(IReadOnlyCollection<QuestDefinition> quests, QuestDefinition focus)
        {
            _selected.Clear();
            _selected.UnionWith(quests);
            _focus = focus;
            foreach (var pair in _nodes)
                pair.Value.SetSelected(_selected.Contains(pair.Key));
            if (_selected.Count == 1) QuestGraphFocus.Focus(_selected.First(), null);
            else if (_selected.Count > 1) Selection.objects = _selected.Cast<Object>().ToArray();
            _bulk.Show(_selected, _kinds);
            ApplyFilters();
        }

        private void BuildMenu(ContextualMenuPopulateEvent evt)
        {
            var at = _canvas.ToContent(_canvas.WorldToLocal(evt.mousePosition));
            var band = _bands.FirstOrDefault(candidate => at.y >= candidate.Top && at.y < candidate.Top + candidate.Height);
            var kind = band.Kind;
            evt.menu.AppendAction(kind == null ? "New Quest" : $"New {QuestStates.NameOf(kind)} Quest", _ => CreateQuest(kind, at));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Open", _ => OnOpenQuest(_selected.First()), _selected.Count == 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Unpin", _ => UnpinSelection(), _selected.Any(quest => quest.WebPinned) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Unpin All", _ =>
            {
                var quests = _nodes.Keys.ToList();
                QuestEdits.Unpin(quests);
                AfterEdit(quests);
            }, _nodes.Keys.Any(quest => quest.WebPinned) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Frame All", _ => Frame(false));
            evt.StopPropagation();
        }

        /** <summary>Makes a quest of a kind, in the folder of another quest of that kind (or any quest), where the menu opened.</summary> */
        private void CreateQuest(QuestKind kind, Vector2 at)
        {
            var sibling = _index.Quests.FirstOrDefault(quest => quest != null && quest.Kind == kind) ?? _index.Quests.FirstOrDefault(quest => quest != null);
            var folder = sibling == null ? "Assets" : Path.GetDirectoryName(AssetDatabase.GetAssetPath(sibling))?.Replace('\\', '/') ?? "Assets";
            var quest = QuestEdits.CreateQuest(folder, kind == null ? "New Quest" : $"New {QuestStates.NameOf(kind)}", kind, at);
            _index.Refresh(quest);
            Rebuild();
            Select(new[] { quest }, quest);
            EditorGUIUtility.PingObject(quest);
        }

        /** <summary>Frames the web once the canvas has a size, if a frame is waiting.</summary> */
        private void FrameIfWaiting()
        {
            if (!_frameOnLayout || !_canvas.HasSize) return;
            _frameOnLayout = false;
            _canvas.schedule.Execute(() => Frame(false));
        }

        /** <summary>Where a link dropped counts as on a quest: the quest with a little room around it.</summary> */
        private static Rect DropRect(QuestNodeView node)
        {
            var rect = node.Rect;
            return new Rect(rect.x - GraphMetrics.DropReach, rect.y - GraphMetrics.DropReach, rect.width + GraphMetrics.DropReach * 2f, rect.height + GraphMetrics.DropReach * 2f);
        }

        /** <summary>A quest link's wire and label.</summary> */
        private sealed class LinkWire
        {
            public Wire Wire { get; }
            public WebLink Link { get; }
            public Label Label { get; }

            public LinkWire(Wire wire, WebLink link, Label label)
            {
                Wire = wire;
                Link = link;
                Label = label;
            }
        }

        /** <summary>A story flag's wire to a quest that reads it.</summary> */
        private sealed class FlagWire
        {
            public Wire Wire { get; }
            public QuestDefinition Reader { get; }

            public FlagWire(Wire wire, QuestDefinition reader)
            {
                Wire = wire;
                Reader = reader;
            }
        }

        #endregion
    }
}
