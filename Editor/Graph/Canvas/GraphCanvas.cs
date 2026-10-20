using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>A pannable, zoomable canvas with a node layer, a wire layer and a label layer. Middle mouse or Alt-drag
     * pans, the wheel zooms about the cursor, and dragging on empty canvas draws a selection box. Elements off screen
     * are hidden, so a large graph costs only what is visible.</summary>
     */
    public sealed class GraphCanvas : VisualElement
    {
        #region Private Fields

        private const float WheelStep = 0.08f;
        private const float CullMargin = 64f;

        /** <summary>Below this zoom wire labels are hidden: too small to read, and too many to lay out.</summary> */
        private const float LabelZoom = 0.5f;

        private readonly CanvasGrid _grid = new CanvasGrid();
        private readonly VisualElement _content = new VisualElement();
        private readonly VisualElement _marquee = new VisualElement();
        private CanvasView _view = CanvasView.Default;
        private Vector2 _dragStart;
        private CanvasView _panStart;
        private bool _panning;
        private bool _selecting;
        private bool _additive;

        #endregion

        #region Properties

        /** <summary>Behind everything else, positioned in content space: bands and other backdrops.</summary> */
        public VisualElement Background { get; } = new VisualElement();

        /** <summary>Where nodes go, positioned in content space.</summary> */
        public VisualElement Nodes { get; } = new VisualElement();

        /** <summary>The wires.</summary> */
        public WireLayer Wires { get; } = new WireLayer();

        /** <summary>Where wire labels go, above wires and nodes, positioned in content space.</summary> */
        public VisualElement Labels { get; } = new VisualElement();

        /** <summary>The current view.</summary> */
        public CanvasView View => _view;

        /** <summary>Raised when the view pans or zooms.</summary> */
        public event Action<CanvasView> OnViewChanged = delegate { };

        /** <summary>Raised when a selection box is drawn: the box in content space, and whether Shift or Ctrl added to the selection.</summary> */
        public event Action<Rect, bool> OnMarquee = delegate { };

        /** <summary>Raised when empty canvas is clicked without dragging, at a content point.</summary> */
        public event Action<Vector2, bool> OnBackgroundClicked = delegate { };

        #endregion

        #region Public API

        /** <summary>Makes a canvas.</summary> */
        public GraphCanvas()
        {
            AddToClassList("quest-graph__canvas");
            focusable = true;
            _content.AddToClassList("quest-graph__content");
            _content.pickingMode = PickingMode.Ignore;
            foreach (var layer in new[] { Background, Wires, Nodes, Labels })
            {
                layer.AddToClassList("quest-graph__layer");
                _content.Add(layer);
            }
            Background.pickingMode = PickingMode.Ignore;
            Nodes.pickingMode = PickingMode.Ignore;
            Labels.pickingMode = PickingMode.Ignore;
            _marquee.AddToClassList("quest-graph__marquee");
            _marquee.pickingMode = PickingMode.Ignore;
            _marquee.style.display = DisplayStyle.None;
            Add(_grid);
            Add(_content);
            Add(_marquee);

            RegisterCallback<PointerDownEvent>(HandlePointerDown);
            RegisterCallback<PointerMoveEvent>(HandlePointerMove);
            RegisterCallback<PointerUpEvent>(HandlePointerUp);
            RegisterCallback<WheelEvent>(HandleWheel);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndGesture());
            RegisterCallback<GeometryChangedEvent>(_ => ApplyView());
        }

        /** <summary>Sets the view.</summary> */
        public void SetView(CanvasView view)
        {
            _view = view;
            ApplyView();
            OnViewChanged(_view);
        }


        /** <summary>True once the canvas has been laid out and has a size.</summary> */
        public bool HasSize => CanvasView.HasSize(layout.size);

        /** <summary>A point in the canvas's own space, in content space.</summary> */
        public Vector2 ToContent(Vector2 local) => _view.ToContent(local);

        /** <summary>Hides nodes and labels off screen, and shows those that came back. Call after adding or moving many.</summary> */
        public void Cull()
        {
            Labels.style.display = _view.Zoom >= LabelZoom ? DisplayStyle.Flex : DisplayStyle.None;
            if (!HasSize)
            {
                foreach (var child in Nodes.Children()) child.visible = true;
                foreach (var child in Labels.Children()) child.visible = true;
                return;
            }
            var visible = _view.Visible(layout.size);
            var margin = new Rect(visible.x - CullMargin, visible.y - CullMargin, visible.width + CullMargin * 2f, visible.height + CullMargin * 2f);
            CullChildren(Nodes, margin);
            CullChildren(Labels, margin);
            Wires.SetVisible(visible);
        }

        #endregion

        #region Event Handlers

        private void HandlePointerDown(PointerDownEvent evt)
        {
            if (!IsBackground(evt.target)) return;
            var local = this.WorldToLocal(evt.position);
            if (evt.button == (int)MouseButton.MiddleMouse || (evt.button == (int)MouseButton.LeftMouse && evt.altKey))
            {
                _panning = true;
                _panStart = _view;
                _dragStart = local;
            }
            else if (evt.button == (int)MouseButton.LeftMouse)
            {
                _selecting = true;
                _additive = evt.shiftKey || evt.actionKey;
                _dragStart = local;
            }
            else return;
            Focus();
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void HandlePointerMove(PointerMoveEvent evt)
        {
            if (!this.HasPointerCapture(evt.pointerId)) return;
            var local = this.WorldToLocal(evt.position);
            if (_panning) SetView(_panStart.Panned(local - _dragStart));
            else if (_selecting)
            {
                var box = Box(_dragStart, local);
                _marquee.style.display = DisplayStyle.Flex;
                _marquee.style.left = box.x;
                _marquee.style.top = box.y;
                _marquee.style.width = box.width;
                _marquee.style.height = box.height;
            }
        }

        private void HandlePointerUp(PointerUpEvent evt)
        {
            if (!this.HasPointerCapture(evt.pointerId)) return;
            this.ReleasePointer(evt.pointerId);
            var local = this.WorldToLocal(evt.position);
            if (_selecting)
            {
                _marquee.style.display = DisplayStyle.None;
                var box = Box(_dragStart, local);
                if (box.width < GraphMetrics.ClickThreshold && box.height < GraphMetrics.ClickThreshold) OnBackgroundClicked(_view.ToContent(local), _additive);
                else OnMarquee(Rect.MinMaxRect(_view.ToContent(box.min).x, _view.ToContent(box.min).y, _view.ToContent(box.max).x, _view.ToContent(box.max).y), _additive);
            }
            EndGesture();
            evt.StopPropagation();
        }

        private void HandleWheel(WheelEvent evt)
        {
            var factor = 1f - Mathf.Clamp(evt.delta.y, -3f, 3f) * WheelStep;
            SetView(_view.ZoomedAbout(this.WorldToLocal(evt.mousePosition), factor));
            evt.StopPropagation();
        }

        #endregion

        #region Helpers

        /** <summary>Ends a pan or a selection box, as when the pointer is released or capture is lost.</summary> */
        private void EndGesture()
        {
            _panning = false;
            _selecting = false;
            _marquee.style.display = DisplayStyle.None;
        }

        private bool IsBackground(IEventHandler target) =>
            target == this || target == _content || target == _grid || target == Nodes || target == Labels || target == Wires || target == Background
            || (target is VisualElement element && Background.Contains(element));

        private void ApplyView()
        {
            _content.style.translate = new Translate(_view.Pan.x, _view.Pan.y);
            _content.style.scale = new Scale(new Vector2(_view.Zoom, _view.Zoom));
            _grid.SetView(_view);
            Cull();
        }

        /** <summary>Hides children outside the visible rect; one added since the last layout has no size yet, so it stays shown until measured.</summary> */
        private static void CullChildren(VisualElement layer, Rect visible)
        {
            foreach (var child in layer.Children())
            {
                if (!CanvasView.HasSize(child.layout.size))
                {
                    child.visible = true;
                    continue;
                }
                var rect = new Rect(child.resolvedStyle.left, child.resolvedStyle.top, child.layout.width, child.layout.height);
                child.visible = rect.Overlaps(visible);
            }
        }

        private static Rect Box(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        #endregion
    }
}
