using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Draws every wire as one mesh, rebuilt only when the wires change or the view leaves the area drawn last.
     * Colours come from the stylesheet, so they follow the editor skin.</summary>
     */
    public sealed class WireLayer : VisualElement
    {
        #region Private Fields

        private static readonly CustomStyleProperty<Color> NormalColor = new CustomStyleProperty<Color>("--wire-normal");
        private static readonly CustomStyleProperty<Color> AnyColor = new CustomStyleProperty<Color>("--wire-any");
        private static readonly CustomStyleProperty<Color> FailsColor = new CustomStyleProperty<Color>("--wire-fails");
        private static readonly CustomStyleProperty<Color> FlagColor = new CustomStyleProperty<Color>("--wire-flag");
        private static readonly CustomStyleProperty<Color> GhostColor = new CustomStyleProperty<Color>("--wire-ghost");
        private static readonly CustomStyleProperty<Color> SelectColor = new CustomStyleProperty<Color>("--wire-select");

        private const float Width = 1.8f;
        private const float SelectedWidth = 3f;
        private const float FadedAlpha = 0.2f;
        private const float DashLength = 6f;
        private const float ArrowSize = 8f;

        private readonly List<Wire> _wires = new List<Wire>();
        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly Color[] _colors = { Color.grey, Color.cyan, Color.red, Color.yellow, Color.grey, Color.blue };
        private Color _select = Color.blue;
        private Rect _drawn;
        private Rect _visible;

        #endregion

        #region Properties

        /** <summary>The wires drawn.</summary> */
        public IReadOnlyList<Wire> Wires => _wires;

        #endregion

        #region Public API

        /** <summary>Makes an empty layer.</summary> */
        public WireLayer()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("quest-graph__wires");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(HandleStyleResolved);
        }

        /** <summary>Replaces the wires and redraws.</summary> */
        public void SetWires(IEnumerable<Wire> wires)
        {
            _wires.Clear();
            _wires.AddRange(wires);
            Redraw();
        }

        /** <summary>Redraws after wires were changed in place.</summary> */
        public void Redraw()
        {
            _drawn = Inflate(_visible);
            MarkDirtyRepaint();
        }

        /** <summary>Tells the layer what part of the content is on screen; it redraws only when that leaves the area drawn last.</summary> */
        public void SetVisible(Rect visible)
        {
            _visible = visible;
            if (Contains(_drawn, visible)) return;
            Redraw();
        }

        /** <summary>The wire nearest a content point within a distance; null when none is.</summary> */
        public Wire WireAt(Vector2 point, float within)
        {
            Wire best = null;
            var bestDistance = within;
            foreach (var wire in _wires)
            {
                if (!Inflate(WireCurve.Bounds(wire.From, wire.To), within).Contains(point)) continue;
                var distance = WireCurve.Distance(wire.From, wire.To, point);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = wire;
            }
            return best;
        }

        #endregion

        #region Event Handlers

        private void HandleStyleResolved(CustomStyleResolvedEvent evt)
        {
            var style = evt.customStyle;
            style.TryGetValue(NormalColor, out _colors[(int)WireStyle.Normal]);
            style.TryGetValue(AnyColor, out _colors[(int)WireStyle.Any]);
            style.TryGetValue(FailsColor, out _colors[(int)WireStyle.Fails]);
            style.TryGetValue(FlagColor, out _colors[(int)WireStyle.Flag]);
            style.TryGetValue(GhostColor, out _colors[(int)WireStyle.Ghost]);
            style.TryGetValue(SelectColor, out _select);
            _colors[(int)WireStyle.Preview] = _select;
            MarkDirtyRepaint();
        }

        #endregion

        #region Helpers

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            foreach (var wire in _wires)
            {
                if (!WireCurve.Bounds(wire.From, wire.To).Overlaps(_drawn)) continue;
                var color = wire.Selected ? _select : _colors[(int)wire.Style];
                if (wire.Faded) color.a *= FadedAlpha;
                painter.strokeColor = color;
                painter.lineWidth = wire.Selected ? SelectedWidth : Width;
                if (wire.Dashed) DrawDashed(painter, wire);
                else
                {
                    var (c1, c2) = WireCurve.Handles(wire.From, wire.To);
                    painter.BeginPath();
                    painter.MoveTo(wire.From);
                    painter.BezierCurveTo(c1, c2, wire.To);
                    painter.Stroke();
                }
                if (wire.Arrow) DrawArrow(painter, wire);
            }
        }

        private void DrawDashed(Painter2D painter, Wire wire)
        {
            _points.Clear();
            var length = Vector2.Distance(wire.From, wire.To) + WireCurve.Bend(wire.From, wire.To);
            WireCurve.Sample(wire.From, wire.To, Mathf.Clamp(Mathf.CeilToInt(length / DashLength), 4, 400), _points);
            painter.BeginPath();
            for (var i = 0; i + 1 < _points.Count; i += 2)
            {
                painter.MoveTo(_points[i]);
                painter.LineTo(_points[i + 1]);
            }
            painter.Stroke();
        }

        private static void DrawArrow(Painter2D painter, Wire wire)
        {
            var direction = (wire.To - WireCurve.Point(wire.From, wire.To, 0.96f)).normalized;
            if (direction == Vector2.zero) direction = Vector2.right;
            var side = new Vector2(-direction.y, direction.x) * (ArrowSize * 0.55f);
            var back = wire.To - direction * ArrowSize;
            painter.BeginPath();
            painter.MoveTo(back + side);
            painter.LineTo(wire.To);
            painter.LineTo(back - side);
            painter.Stroke();
        }

        private static Rect Inflate(Rect rect) => Inflate(rect, Mathf.Max(rect.width, rect.height) * 0.5f);

        private static Rect Inflate(Rect rect, float by) => new Rect(rect.x - by, rect.y - by, rect.width + by * 2f, rect.height + by * 2f);

        private static bool Contains(Rect outer, Rect inner) => outer.xMin <= inner.xMin && outer.yMin <= inner.yMin && outer.xMax >= inner.xMax && outer.yMax >= inner.yMax;

        #endregion
    }
}
