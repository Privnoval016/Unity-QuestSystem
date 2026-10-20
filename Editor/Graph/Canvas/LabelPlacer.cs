using System.Collections.Generic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>A label to place on a wire.</summary> */
    public readonly struct LabelRequest
    {
        /** <summary>Where the wire starts.</summary> */
        public Vector2 From { get; }

        /** <summary>Where the wire ends.</summary> */
        public Vector2 To { get; }

        /** <summary>The label's size.</summary> */
        public Vector2 Size { get; }

        /** <summary>Makes a request.</summary> */
        public LabelRequest(Vector2 from, Vector2 to, Vector2 size)
        {
            From = from;
            To = to;
            Size = size;
        }
    }

    /**
     * <summary>Places wire labels so they are always readable: each takes the first spot along its own wire that
     * touches no node, no other label and no other wire, keeps clear of both ends so arrowheads show, and keeps a
     * little room from other nodes so it never reads as theirs. When no spot is clear it takes the one that covers
     * least.</summary>
     */
    public static class LabelPlacer
    {
        #region Private Fields

        /** <summary>Where along a wire a label may sit, best first.</summary> */
        private static readonly float[] Spots = { 0.5f, 0.42f, 0.58f, 0.34f, 0.66f, 0.28f, 0.72f, 0.22f, 0.78f };

        /** <summary>Space kept around a label.</summary> */
        private const float Padding = 2f;

        /** <summary>How many segments a wire is checked as.</summary> */
        private const int WireSamples = 32;

        /** <summary>The cost of a spot that covers a node or another label: never chosen while any spot doesn't.</summary> */
        private const float CoverNodeOrLabel = 1000f;

        /** <summary>The cost of a spot for each other wire it covers.</summary> */
        private const float CrossWire = 10f;

        /** <summary>How close to a node a label may sit before it could be read as that node's own.</summary> */
        private const float NodeMargin = 24f;

        /** <summary>The cost of a spot that crowds a node without covering it.</summary> */
        private const float CrowdNode = 5f;

        #endregion

        #region Public API

        /** <summary>How far a label keeps from the wire's end, past its own half-width, so the arrowhead shows.</summary> */
        public const float EndClearance = 22f;

        /** <summary>How far a label keeps from the wire's start, past its own half-width.</summary> */
        public const float StartClearance = 12f;

        /**
         * <summary>The centre of each label, in order. Obstacles are the nodes' rects. Nodes, placed labels and wire
         * segments sit in a grid, so each check looks only at the cells a label covers.</summary>
         */
        public static Vector2[] Place(IReadOnlyList<LabelRequest> labels, IReadOnlyList<Rect> obstacles)
        {
            var nodes = new CellGrid<Rect>();
            foreach (var obstacle in obstacles)
                nodes.Add(Inflate(obstacle, NodeMargin), obstacle);
            var segments = new CellGrid<(int wire, Vector2 a, Vector2 b)>();
            var points = new List<Vector2>(WireSamples + 1);
            for (var i = 0; i < labels.Count; i++)
            {
                points.Clear();
                WireCurve.Sample(labels[i].From, labels[i].To, WireSamples, points);
                for (var k = 1; k < points.Count; k++)
                    segments.Add(Rect.MinMaxRect(Mathf.Min(points[k - 1].x, points[k].x), Mathf.Min(points[k - 1].y, points[k].y), Mathf.Max(points[k - 1].x, points[k].x), Mathf.Max(points[k - 1].y, points[k].y)), (i, points[k - 1], points[k]));
            }

            var placed = new CellGrid<Rect>();
            var centres = new Vector2[labels.Count];
            var crossed = new HashSet<int>();
            for (var i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                var best = WireCurve.Point(label.From, label.To, 0.5f);
                var bestScore = float.MaxValue;
                foreach (var t in Spots)
                {
                    var centre = WireCurve.Point(label.From, label.To, t);
                    var half = label.Size.x / 2f;
                    if (Vector2.Distance(centre, label.To) < half + EndClearance || Vector2.Distance(centre, label.From) < half + StartClearance) continue;
                    var box = Box(centre, label.Size);
                    var score = Mathf.Abs(t - 0.5f);
                    foreach (var node in nodes.Near(box))
                    {
                        if (box.Overlaps(node)) score += CoverNodeOrLabel;
                        else if (box.Overlaps(Inflate(node, NodeMargin)) && !IsEndOf(node, label)) score += CrowdNode;
                    }
                    foreach (var other in placed.Near(box))
                        if (box.Overlaps(other)) score += CoverNodeOrLabel;
                    crossed.Clear();
                    foreach (var (wire, a, b) in segments.Near(box))
                        if (wire != i && !crossed.Contains(wire) && SegmentCrosses(a, b, box)) crossed.Add(wire);
                    score += crossed.Count * CrossWire;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = centre;
                    }
                    if (score < 1f) break;
                }
                centres[i] = best;
                var chosen = Box(best, label.Size);
                placed.Add(chosen, chosen);
            }
            return centres;
        }

        /** <summary>The rect a label of a size takes at a centre, padding included.</summary> */
        public static Rect Box(Vector2 centre, Vector2 size) => new Rect(centre - size / 2f - Vector2.one * Padding, size + Vector2.one * Padding * 2f);

        #endregion

        #region Helpers

        /** <summary>True when a node is one the label's own wire starts or ends on, whose edge its ports sit on.</summary> */
        private static bool IsEndOf(Rect node, LabelRequest label)
        {
            var edge = Inflate(node, 1f);
            return edge.Contains(label.From) || edge.Contains(label.To);
        }

        private static Rect Inflate(Rect rect, float by) => new Rect(rect.x - by, rect.y - by, rect.width + by * 2f, rect.height + by * 2f);

        /** <summary>True when a segment passes through a rect: either end inside, or it crosses an edge.</summary> */
        private static bool SegmentCrosses(Vector2 a, Vector2 b, Rect box)
        {
            if (box.Contains(a) || box.Contains(b)) return true;
            if (Mathf.Max(a.x, b.x) < box.xMin || Mathf.Min(a.x, b.x) > box.xMax || Mathf.Max(a.y, b.y) < box.yMin || Mathf.Min(a.y, b.y) > box.yMax) return false;
            Vector2 topLeft = new Vector2(box.xMin, box.yMin), topRight = new Vector2(box.xMax, box.yMin);
            Vector2 bottomRight = new Vector2(box.xMax, box.yMax), bottomLeft = new Vector2(box.xMin, box.yMax);
            return Intersect(a, b, topLeft, topRight) || Intersect(a, b, topRight, bottomRight) || Intersect(a, b, bottomRight, bottomLeft) || Intersect(a, b, bottomLeft, topLeft);
        }

        private static bool Intersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var d1 = Cross(q1, q2, p1);
            var d2 = Cross(q1, q2, p2);
            var d3 = Cross(p1, p2, q1);
            var d4 = Cross(p1, p2, q2);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /** <summary>Items bucketed by the grid cells their bounds touch, for finding what is near a rect quickly.</summary> */
        private sealed class CellGrid<T>
        {
            private const float CellSize = 160f;
            private readonly List<T> _items = new List<T>();
            private readonly Dictionary<(int, int), List<int>> _cells = new Dictionary<(int, int), List<int>>();
            private readonly List<T> _found = new List<T>();
            private readonly HashSet<int> _seen = new HashSet<int>();

            public void Add(Rect bounds, T item)
            {
                var index = _items.Count;
                _items.Add(item);
                foreach (var cell in Cells(bounds))
                {
                    if (!_cells.TryGetValue(cell, out var list)) _cells[cell] = list = new List<int>();
                    list.Add(index);
                }
            }

            /** <summary>Every item whose bounds share a cell with the rect, each once.</summary> */
            public List<T> Near(Rect rect)
            {
                _found.Clear();
                _seen.Clear();
                foreach (var cell in Cells(rect))
                    if (_cells.TryGetValue(cell, out var list))
                        foreach (var index in list)
                            if (_seen.Add(index)) _found.Add(_items[index]);
                return _found;
            }

            private static IEnumerable<(int, int)> Cells(Rect rect)
            {
                int x0 = Mathf.FloorToInt(rect.xMin / CellSize), x1 = Mathf.FloorToInt(rect.xMax / CellSize);
                int y0 = Mathf.FloorToInt(rect.yMin / CellSize), y1 = Mathf.FloorToInt(rect.yMax / CellSize);
                for (var x = x0; x <= x1; x++)
                    for (var y = y0; y <= y1; y++)
                        yield return (x, y);
            }
        }

        #endregion
    }
}
