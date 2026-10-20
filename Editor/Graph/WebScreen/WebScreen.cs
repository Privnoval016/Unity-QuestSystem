using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
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

        private const int KindColours = 6;
        /** <summary>How far above the first band the story-flag row sits.</summary> */
        private const float FlagRowAbove = 70f;

        /** <summary>How far left of its first reader a flag starts.</summary> */
        private const float FlagLead = 230f;

        private const float FlagWidth = 150f;
        private const float BandWidthPastLastColumn = 400f;

        private readonly QuestIndex _index;
        private readonly GraphCanvas _canvas = new GraphCanvas();
        private readonly VisualElement _bar = new VisualElement();
        private readonly ToolbarSearchField _search = new ToolbarSearchField();
        private readonly VisualElement _chips = new VisualElement();
        private readonly VisualElement _depth = new VisualElement();
        private readonly VisualElement _bulk = new VisualElement();
        private readonly VisualElement _empty = new VisualElement();
        private readonly Notice _notice = new Notice();
        private readonly Dictionary<QuestDefinition, QuestNodeView> _nodes = new Dictionary<QuestDefinition, QuestNodeView>();
        private readonly List<(Wire wire, WebLink link, Label label)> _wires = new List<(Wire, WebLink, Label)>();

        /** <summary>The story-flag view's wires, each from its flag to a quest that reads it.</summary> */
        private readonly List<(Wire wire, VisualElement flag, QuestDefinition reader)> _flagWires = new List<(Wire, VisualElement, QuestDefinition)>();
        private readonly HashSet<QuestDefinition> _selected = new HashSet<QuestDefinition>();
        private readonly HashSet<string> _filters = new HashSet<string>();

        /** <summary>Keyed by segment button, resolving to whether it shows as on.</summary> */
        private readonly Dictionary<Button, Func<bool>> _segments = new Dictionary<Button, Func<bool>>();
        private List<WebBand> _bands = new List<WebBand>();
        private List<WebLink> _links = new List<WebLink>();
        private bool _flagsView;
        private bool _chain = true;
        private QuestDefinition _focus;
        private Dictionary<QuestDefinition, Vector2> _dragFrom;
        private Vector2 _dragStart;
        private bool _dragged;
        private Wire _preview;
        private QuestNodeView _previewTarget;
        private Label _previewText;
        private bool _frameOnLayout = true;

        #endregion

        #region Properties

        /** <summary>The canvas, for the window's view state.</summary> */
        public GraphCanvas Canvas => _canvas;

        /** <summary>Counts for the status bar: quests shown, of all, links, errors, warnings, unreadable conditions.</summary> */
        public (int shown, int total, int links, int errors, int warnings, int unreadable) Counts { get; private set; }

        /** <summary>Raised when a quest should open on its own screen.</summary> */
        public event Action<QuestDefinition> OnOpenQuest = delegate { };

        /** <summary>Raised after the web redraws.</summary> */
        public event Action OnRebuilt = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes the web over an index.</summary> */
        public WebScreen(QuestIndex index)
        {
            _index = index;
            AddToClassList("quest-graph__screen");
            Add(_canvas);
            BuildBar();
            Add(_bar);
            _bulk.AddToClassList("quest-graph__bulk");
            _bulk.style.display = DisplayStyle.None;
            Add(_bulk);
            _empty.AddToClassList("quest-graph__empty");
            _empty.pickingMode = PickingMode.Ignore;
            Add(_empty);
            Add(_notice);

            _canvas.OnBackgroundClicked += (_, additive) =>
            {
                if (!additive) Select(Array.Empty<QuestDefinition>(), null);
            };
            _canvas.OnMarquee += (box, additive) =>
            {
                var inside = _nodes.Where(pair => pair.Value.visible && NodeRect(pair.Value).Overlaps(box)).Select(pair => pair.Key);
                Select(additive ? _selected.Union(inside).ToList() : inside.ToList(), null);
            };
            _canvas.RegisterCallback<KeyDownEvent>(HandleKeyDown);
            _canvas.AddManipulator(new ContextualMenuManipulator(BuildMenu));
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => FrameIfWaiting());
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

        /** <summary>Shows the web, keeping a view when given one.</summary> */
        public void Show(CanvasView? view)
        {
            Rebuild();
            if (view.HasValue && view.Value.Zoom > 0f) _canvas.SetView(view.Value);
            else
            {
                _frameOnLayout = true;
                FrameIfWaiting();
            }
        }

        /** <summary>Redraws everything from the index, keeping the view, the selection and the filters.</summary> */
        public void Rebuild()
        {
            _canvas.Background.Clear();
            _canvas.Nodes.Clear();
            _canvas.Labels.Clear();
            _nodes.Clear();
            _wires.Clear();
            _flagWires.Clear();
            var quests = _index.Quests.Where(quest => quest != null).ToList();
            _selected.RemoveWhere(quest => quest == null || !quests.Contains(quest));
            if (_focus != null && !quests.Contains(_focus)) _focus = null;
            _links = WebLinks.Of(_index, quests);
            var (positions, bands) = WebLayout.Arrange(quests, _links);
            _bands = bands;
            BuildChips(quests);

            var kindOrder = quests.Select(quest => quest.Kind).Where(kind => kind != null).Distinct().OrderBy(kind => kind.Order).ThenBy(kind => kind.name).ToList();
            var right = positions.Count == 0 ? 0f : positions.Values.Max(p => p.x) + WebLayout.NodeWidth + BandWidthPastLastColumn;
            foreach (var band in bands)
            {
                var element = new VisualElement();
                element.AddToClassList("quest-graph__band");
                element.style.top = band.Top;
                element.style.height = band.Height;
                element.style.width = Mathf.Max(right, 1200f);
                var name = new Label(band.Kind == null ? "No kind" : band.Kind.name);
                name.AddToClassList("quest-graph__band-name");
                name.AddToClassList("quest-graph__kind-" + KindColour(kindOrder, band.Kind));
                element.Add(name);
                _canvas.Background.Add(element);
            }

            foreach (var quest in quests)
            {
                var node = new QuestNodeView(quest, _index.SummaryOf(quest), KindColour(kindOrder, quest.Kind), WebLinks.ReadsItself(_index, quest)) { Position = positions[quest] };
                node.SetSelected(_selected.Contains(quest));
                HookNode(node);
                _nodes[quest] = node;
                _canvas.Nodes.Add(node);
            }

            foreach (var link in _links)
            {
                var wire = new Wire { Style = link.Fails ? WireStyle.Fails : WireStyle.Normal, Dashed = link.Fails, Arrow = true };
                var label = new Label(link.Label);
                label.AddToClassList("quest-graph__label");
                label.EnableInClassList("quest-graph__label--fails", link.Fails);
                _canvas.Labels.Add(label);
                _wires.Add((wire, link, label));
            }
            if (_flagsView) BuildFlags(quests);
            PlaceWires(true);
            ApplyFilters();
            Counts = (_nodes.Values.Count(node => node.visible && node.style.display != DisplayStyle.None), quests.Count, _links.Count,
                quests.Sum(quest => _index.SummaryOf(quest)?.Errors ?? 0), quests.Sum(quest => _index.SummaryOf(quest)?.Warnings ?? 0),
                quests.Sum(quest => _index.SummaryOf(quest)?.Unreadable ?? 0));
            ShowEmpty(quests.Count);
            _canvas.schedule.Execute(_canvas.Cull);
            OnRebuilt();
        }

        /** <summary>Frames every quest shown, or the selection.</summary> */
        public void Frame(bool selectionOnly = true)
        {
            var nodes = (selectionOnly && _selected.Count > 0 ? _selected.Select(quest => _nodes[quest]) : _nodes.Values.Where(Shown)).ToList();
            if (nodes.Count == 0 || !_canvas.HasSize || !CanvasView.HasSize(_bar.layout.size)) return;
            var rects = nodes.Select(NodeRect).ToList();
            if (!selectionOnly || _selected.Count == 0)
                rects.AddRange(_canvas.Nodes.Children().Where(element => element.ClassListContains("quest-graph__flag"))
                    .Select(flag => new Rect(flag.resolvedStyle.left, flag.resolvedStyle.top, FlagWidth, 26f)));
            var bounds = rects.Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
            if (!selectionOnly || _selected.Count == 0)
            {
                bounds.xMin = 0f;
                bounds.yMin = Mathf.Min(bounds.yMin, _bands.Count > 0 ? _bands[0].Top : bounds.yMin);
                bounds.xMin = Mathf.Min(bounds.xMin, rects.Min(rect => rect.xMin));
            }
            const float margin = 20f;
            var top = _bar.layout.yMax + margin;
            var area = new Vector2(_canvas.layout.width - margin * 2f, _canvas.layout.height - top - margin);
            var zoom = Mathf.Clamp(Mathf.Min(area.x / bounds.width, area.y / bounds.height), CanvasView.MinZoom, 1f);
            var width = bounds.width * zoom;
            var x = width <= area.x ? margin + (area.x - width) / 2f : margin;
            _canvas.SetView(new CanvasView(new Vector2(x - bounds.xMin * zoom, top - bounds.yMin * zoom), zoom));
        }

        /** <summary>Lays out every quest that isn't pinned again; pinned quests stay.</summary> */
        public void Tidy() => Rebuild();

        /** <summary>Lets every selected quest (or all, with none selected) be laid out again.</summary> */
        public void UnpinSelection()
        {
            GraphEdits.Unpin(_selected.Count > 0 ? _selected : _nodes.Keys);
            Rebuild();
        }

        #endregion

        #region Event Handlers

        private void HandleIndexChanged(QuestDefinition quest) => schedule.Execute(Rebuild);

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

        #region Helpers: Building

        private void BuildBar()
        {
            _bar.AddToClassList("quest-graph__web-bar");
            var views = new VisualElement();
            views.AddToClassList("quest-graph__segment");
            views.Add(Segment("Dependencies", () => !_flagsView, () => SetView(false)));
            views.Add(Segment("Story flags", () => _flagsView, () => SetView(true)));
            _bar.Add(views);
            _search.AddToClassList("quest-graph__web-search");
            _search.RegisterValueChangedCallback(_ => ApplyFilters());
            _bar.Add(_search);
            _chips.AddToClassList("quest-graph__chips");
            _bar.Add(_chips);
            _depth.AddToClassList("quest-graph__segment");
            _depth.Add(Segment("Direct", () => !_chain, () => { _chain = false; ApplyFilters(); }));
            _depth.Add(Segment("Chain", () => _chain, () => { _chain = true; ApplyFilters(); }));
            _bar.Add(_depth);
        }

        private void SetView(bool flags)
        {
            _flagsView = flags;
            Rebuild();
            _canvas.schedule.Execute(() => Frame(false));
        }

        private Button Segment(string text, Func<bool> isOn, Action onClick)
        {
            var button = new Button(() =>
            {
                onClick();
                RefreshSegments();
            }) { text = text };
            button.AddToClassList("quest-graph__segment-button");
            _segments[button] = isOn;
            return button;
        }

        private void RefreshSegments()
        {
            foreach (var pair in _segments)
                pair.Key.EnableInClassList("quest-graph__segment-button--on", pair.Value());
            _depth.style.display = _focus != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildChips(List<QuestDefinition> quests)
        {
            _chips.Clear();
            foreach (var kind in quests.Select(quest => quest.Kind).Where(kind => kind != null).Distinct().OrderBy(kind => kind.Order).ThenBy(kind => kind.name))
                _chips.Add(Chip("kind:" + kind.name, kind.name));
            _chips.Add(Chip("repeatable", "Repeatable"));
            _chips.Add(Chip("shown", "Shown early"));
            _chips.Add(Chip("problems", "Problems"));
            RefreshSegments();
        }

        private Button Chip(string key, string text)
        {
            Button chip = null;
            chip = new Button(() =>
            {
                if (!_filters.Remove(key)) _filters.Add(key);
                chip.EnableInClassList("quest-graph__chip--on", _filters.Contains(key));
                ApplyFilters();
            }) { text = text };
            chip.AddToClassList("quest-graph__chip");
            chip.EnableInClassList("quest-graph__chip--on", _filters.Contains(key));
            return chip;
        }

        private void BuildFlags(List<QuestDefinition> quests)
        {
            var readers = new Dictionary<StoryFlag, List<QuestDefinition>>();
            foreach (var quest in quests)
                foreach (var flag in _index.SummaryOf(quest)?.Flags ?? Array.Empty<StoryFlag>())
                {
                    if (!readers.TryGetValue(flag, out var list)) readers[flag] = list = new List<QuestDefinition>();
                    list.Add(quest);
                }
            // Flags get rows of their own above the bands, each up and to the left of its first reader, never on a quest; a
            // flag that would touch another goes up a row rather than sliding past its reader.
            var bottomRow = (_bands.Count > 0 ? _bands[0].Top : 0f) - FlagRowAbove;
            var taken = new List<Rect>();
            foreach (var pair in readers.OrderBy(pair => pair.Value.Min(quest => _nodes[quest].Position.x)).ThenBy(pair => pair.Key.name))
            {
                var at = new Vector2(pair.Value.Min(quest => _nodes[quest].Position.x) - FlagLead, bottomRow);
                while (taken.Any(rect => rect.Overlaps(new Rect(at.x - 6f, at.y, FlagWidth + 12f, 26f)))) at.y -= 34f;
                taken.Add(new Rect(at, new Vector2(FlagWidth, 26f)));
                var node = new Label(pair.Key.name) { tooltip = $"Read by {string.Join(", ", pair.Value.Select(QuestStates.TitleOf))}" };
                node.AddToClassList("quest-graph__flag");
                node.style.maxWidth = FlagWidth;
                node.style.left = at.x;
                node.style.top = at.y;
                _canvas.Nodes.Add(node);
                foreach (var quest in pair.Value)
                    _flagWires.Add((new Wire { Style = WireStyle.Flag, Arrow = true }, node, quest));
                node.RegisterCallback<GeometryChangedEvent>(_ => PlaceWires(false));
            }
        }

        /** <summary>Puts every wire on its ports, spread along each node's edge, and (when asked) places the labels.</summary> */
        private void PlaceWires(bool placeLabels)
        {
            var outgoing = new Dictionary<QuestDefinition, List<Wire>>();
            var incoming = new Dictionary<QuestDefinition, List<Wire>>();
            foreach (var (wire, link, _) in _wires)
            {
                Add(outgoing, link.From, wire);
                Add(incoming, link.To, wire);
            }
            foreach (var (wire, flag, reader) in _flagWires)
            {
                wire.From = new Vector2(flag.resolvedStyle.left + (CanvasView.HasSize(flag.layout.size) ? flag.layout.width : FlagWidth), flag.resolvedStyle.top + 13f);
                Add(incoming, reader, wire);
            }
            var targetY = _wires.ToDictionary(w => w.wire, w => _nodes[w.link.To].Position.y);
            foreach (var pair in outgoing)
                Spread(pair.Value.OrderBy(wire => targetY[wire]).ToList(), _nodes[pair.Key], true);
            foreach (var pair in incoming)
                Spread(pair.Value.OrderBy(w => w.From.y).ToList(), _nodes[pair.Key], false);
            _canvas.Wires.SetWires(_wires.Select(w => w.wire).Concat(_flagWires.Select(w => w.wire)).Concat(_preview == null ? Enumerable.Empty<Wire>() : new[] { _preview }));
            if (placeLabels) PlaceLabels();
        }

        private static void Add(Dictionary<QuestDefinition, List<Wire>> map, QuestDefinition quest, Wire wire)
        {
            if (!map.TryGetValue(quest, out var list)) map[quest] = list = new List<Wire>();
            list.Add(wire);
        }

        private static void Spread(List<Wire> wires, QuestNodeView node, bool outgoing)
        {
            var height = CanvasView.HasSize(node.layout.size) ? node.layout.height : 52f;
            for (var i = 0; i < wires.Count; i++)
            {
                var y = node.Position.y + 6f + (height - 12f) * (i + 1) / (wires.Count + 1);
                if (outgoing) wires[i].From = new Vector2(node.Position.x + WebLayout.NodeWidth, y);
                else wires[i].To = new Vector2(node.Position.x - 2f, y);
            }
        }

        /** <summary>Places labels once over every wire and node, so focus and search never move them.</summary> */
        private void PlaceLabels()
        {
            var labelled = _wires;
            var requests = labelled.Select(w => new LabelRequest(w.wire.From, w.wire.To, LabelSize(w.label))).ToList();
            var obstacles = _nodes.Values.Select(NodeRect).ToList();
            var centres = LabelPlacer.Place(requests, obstacles);
            for (var i = 0; i < labelled.Count; i++)
            {
                var size = requests[i].Size;
                labelled[i].label.style.left = centres[i].x - size.x / 2f;
                labelled[i].label.style.top = centres[i].y - size.y / 2f;
                labelled[i].label.style.width = size.x;
            }
        }

        private static Vector2 LabelSize(Label label)
        {
            var text = label.MeasureTextSize(label.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
            var width = float.IsFinite(text.x) && text.x > 0f ? text.x : label.text.Length * 6.2f;
            return new Vector2(Mathf.Ceil(width) + 16f, 16f);
        }

        #endregion

        #region Helpers: Filters and focus

        private bool Shown(QuestNodeView node) => node.style.display != DisplayStyle.None;

        private void ApplyFilters()
        {
            var term = _search.value?.Trim() ?? "";
            var kinds = _filters.Where(f => f.StartsWith("kind:", StringComparison.Ordinal)).Select(f => f.Substring(5)).ToList();
            var related = _focus == null ? null : Related(_focus);
            foreach (var pair in _nodes)
            {
                var quest = pair.Key;
                var summary = _index.SummaryOf(quest);
                var shown = (kinds.Count == 0 || (quest.Kind != null && kinds.Contains(quest.Kind.name)))
                    && (!_filters.Contains("repeatable") || quest.Repeatable)
                    && (!_filters.Contains("shown") || !quest.HiddenUntilGiven)
                    && (!_filters.Contains("problems") || (summary != null && summary.Errors + summary.Warnings > 0));
                pair.Value.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                var match = term.Length > 0 && Matches(quest, term);
                pair.Value.SetMatch(match);
                pair.Value.SetFaded((related != null && !related.Contains(quest)) || (term.Length > 0 && !match) || (_flagsView && (summary?.Flags.Count ?? 0) == 0));
            }
            foreach (var (wire, link, label) in _wires)
            {
                var visible = Shown(_nodes[link.From]) && Shown(_nodes[link.To]);
                var live = related == null || (related.Contains(link.From) && related.Contains(link.To));
                wire.Faded = !visible || !live || _flagsView;
                label.style.display = visible && live && !_flagsView ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (var (wire, _, reader) in _flagWires)
                wire.Faded = !Shown(_nodes[reader]) || (related != null && !related.Contains(reader));
            _canvas.Wires.Redraw();
            RefreshSegments();
            var shownCount = _nodes.Values.Count(Shown);
            Counts = (shownCount, Counts.total, Counts.links, Counts.errors, Counts.warnings, Counts.unreadable);
            ShowEmpty(_nodes.Count);
            OnRebuilt();
        }

        private static bool Matches(QuestDefinition quest, string term) =>
            QuestStates.TitleOf(quest).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
            || quest.Steps.Any(step => step != null && ((step.Text ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || step.Id == term));

        private HashSet<QuestDefinition> Related(QuestDefinition quest)
        {
            var related = new HashSet<QuestDefinition> { quest };
            void Walk(QuestDefinition from, bool forward)
            {
                foreach (var link in _links)
                {
                    var next = forward ? (link.From == from ? link.To : null) : (link.To == from ? link.From : null);
                    if (next == null || !related.Add(next)) continue;
                    if (_chain) Walk(next, forward);
                }
            }
            Walk(quest, true);
            Walk(quest, false);
            return related;
        }

        private void ShowEmpty(int quests)
        {
            _empty.Clear();
            var nothingShown = quests == 0 || _nodes.Values.All(node => !Shown(node));
            _empty.style.display = nothingShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!nothingShown) return;
            _empty.Add(new Label(quests == 0 ? "No quests" : "No quests match"));
            if (quests > 0)
                _empty.Add(new Button(() =>
                {
                    _filters.Clear();
                    _search.value = "";
                    Rebuild();
                }) { text = "Clear filters" });
        }

        #endregion

        #region Helpers: Editing

        private void HookNode(QuestNodeView node)
        {
            node.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target == node.Handle) return;
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
                _dragFrom = _selected.ToDictionary(quest => quest, quest => _nodes[quest].Position);
                _dragStart = evt.position;
                _dragged = false;
                node.CapturePointer(evt.pointerId);
                _canvas.Focus();
                evt.StopPropagation();
            });
            node.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!node.HasPointerCapture(evt.pointerId) || _dragFrom == null) return;
                var delta = ((Vector2)evt.position - _dragStart) / _canvas.View.Zoom;
                if (!_dragged && delta.sqrMagnitude < 9f) return;
                _dragged = true;
                foreach (var pair in _dragFrom)
                    _nodes[pair.Key].Position = new Vector2(Mathf.Round((pair.Value.x + delta.x) / 10f) * 10f, Mathf.Round((pair.Value.y + delta.y) / 10f) * 10f);
                PlaceWires(false);
            });
            node.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!node.HasPointerCapture(evt.pointerId)) return;
                node.ReleasePointer(evt.pointerId);
                if (_dragged && _dragFrom != null)
                {
                    GraphEdits.MoveQuests(_dragFrom.Keys.ToDictionary(quest => quest, quest => _nodes[quest].Position));
                    Rebuild();
                }
                _dragFrom = null;
                _dragged = false;
            });

            node.Handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _preview = new Wire { Style = WireStyle.Preview, Dashed = true, Arrow = true, From = node.Position + new Vector2(WebLayout.NodeWidth, 26f) };
                _preview.To = _preview.From;
                node.Handle.CapturePointer(evt.pointerId);
                PlaceWires(false);
                evt.StopPropagation();
            });
            node.Handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_preview == null || !node.Handle.HasPointerCapture(evt.pointerId)) return;
                var at = _canvas.ToContent(_canvas.WorldToLocal(evt.position));
                _preview.To = at;
                var target = _nodes.Values.FirstOrDefault(other => other != node && Shown(other) && NodeRect(other).Contains(at));
                if (target != _previewTarget) ShowPreview(node.Quest, target);
                _canvas.Wires.Redraw();
            });
            node.Handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_preview == null || !node.Handle.HasPointerCapture(evt.pointerId)) return;
                node.Handle.ReleasePointer(evt.pointerId);
                var target = _previewTarget;
                ShowPreview(null, null);
                _preview = null;
                if (target == null) PlaceWires(false);
                else if (GraphEdits.WouldLoop(_index, target.Quest, node.Quest))
                {
                    _notice.Show("Those quests would need each other, so they weren't linked.", false);
                    PlaceWires(false);
                }
                else
                {
                    GraphEdits.AddAvailability(target.Quest, node.Quest);
                    _index.Refresh(target.Quest);
                    _notice.Show($"{QuestStates.TitleOf(target.Quest)} now needs {QuestStates.TitleOf(node.Quest)} done.", true);
                }
            });
        }

        /** <summary>Shows what a link would write on the quest under the pointer.</summary> */
        private void ShowPreview(QuestDefinition needs, QuestNodeView target)
        {
            _previewText?.RemoveFromHierarchy();
            _previewText = null;
            _previewTarget = target;
            if (target == null || needs == null) return;
            _previewText = new Label("Available when " + GraphEdits.PreviewAvailability(target.Quest, needs));
            _previewText.AddToClassList("quest-graph__preview");
            _previewText.style.left = target.Position.x;
            _previewText.style.top = target.Position.y + (CanvasView.HasSize(target.layout.size) ? target.layout.height : 52f) + 6f;
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
            BuildBulk();
            ApplyFilters();
        }

        private void BuildBulk()
        {
            _bulk.Clear();
            _bulk.style.display = _selected.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_selected.Count < 2) return;
            _bulk.Add(new Label($"{_selected.Count} quests"));
            var kinds = _index.Quests.Where(quest => quest != null && quest.Kind != null).Select(quest => quest.Kind).Distinct().OrderBy(kind => kind.Order).ToList();
            var choices = new List<string> { "—" };
            choices.AddRange(kinds.Select(kind => kind.name));
            var current = _selected.Select(quest => quest.Kind).Distinct().Count() == 1 && _selected.First().Kind != null ? _selected.First().Kind.name : "—";
            var kindField = new PopupField<string>("Kind", choices, choices.Contains(current) ? current : "—");
            kindField.RegisterValueChangedCallback(evt =>
            {
                var kind = kinds.FirstOrDefault(k => k.name == evt.newValue);
                if (kind == null) return;
                GraphEdits.SetKind(_selected.ToList(), kind);
                Rebuild();
            });
            _bulk.Add(kindField);
            _bulk.Add(ObjectPicker("Giver", quest => quest.GiverAsset, GraphEdits.SetGiver));
            _bulk.Add(ObjectPicker("Place", quest => quest.PlaceAsset, GraphEdits.SetPlace));
        }

        private ObjectField ObjectPicker(string label, Func<QuestDefinition, Object> read, Action<IEnumerable<QuestDefinition>, Object> write)
        {
            var values = _selected.Select(read).Distinct().ToList();
            var field = new ObjectField(label) { objectType = typeof(Object), allowSceneObjects = false, value = values.Count == 1 ? values[0] : null };
            field.showMixedValue = values.Count > 1;
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != null && !DisplayNames.IsNamedOrEmpty(evt.newValue))
                {
                    _notice.Show("That asset has no display name to show.", false);
                    field.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
                write(_selected.ToList(), evt.newValue);
                Rebuild();
            });
            return field;
        }

        private void BuildMenu(ContextualMenuPopulateEvent evt)
        {
            var at = _canvas.ToContent(_canvas.WorldToLocal(evt.mousePosition));
            var band = _bands.FirstOrDefault(b => at.y >= b.Top && at.y < b.Top + b.Height);
            var kindName = band.Kind == null ? "Quest" : band.Kind.name + " quest";
            evt.menu.AppendAction($"New {kindName}", _ => CreateQuest(band.Kind, at));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Open", _ => OnOpenQuest(_selected.First()), _selected.Count == 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Unpin", _ => UnpinSelection(), _selected.Any(quest => quest.WebPinned) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Unpin All", _ =>
            {
                GraphEdits.Unpin(_nodes.Keys);
                Rebuild();
            });
            evt.menu.AppendAction("Frame All", _ => Frame(false));
            evt.StopPropagation();
        }

        /** <summary>Makes a quest of a kind, beside other quests of that kind (or any quest), where the menu opened.</summary> */
        private void CreateQuest(QuestKind kind, Vector2 at)
        {
            var sibling = _index.Quests.FirstOrDefault(quest => quest != null && quest.Kind == kind) ?? _index.Quests.FirstOrDefault(quest => quest != null);
            var folder = sibling == null ? "Assets" : Path.GetDirectoryName(AssetDatabase.GetAssetPath(sibling))?.Replace('\\', '/') ?? "Assets";
            var quest = GraphEdits.CreateQuest(folder, kind == null ? "New Quest" : "New " + kind.name, kind, at);
            _index.Refresh(quest);
            Select(new[] { quest }, quest);
            EditorGUIUtility.PingObject(quest);
        }

        private void FrameIfWaiting()
        {
            if (!_frameOnLayout || !_canvas.HasSize || !CanvasView.HasSize(_bar.layout.size)) return;
            _frameOnLayout = false;
            _canvas.schedule.Execute(() => Frame(false));
        }

        private static int KindColour(List<QuestKind> kinds, QuestKind kind) => kind == null ? KindColours : kinds.IndexOf(kind) % KindColours;

        private static Rect NodeRect(QuestNodeView node) => new Rect(node.Position, new Vector2(WebLayout.NodeWidth, CanvasView.HasSize(node.layout.size) ? node.layout.height : 52f));

        #endregion
    }
}
