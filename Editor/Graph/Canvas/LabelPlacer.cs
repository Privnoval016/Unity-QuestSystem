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
     * touches no node, no other label and no other wire, and keeps clear of both ends so arrowheads show. When no
     * spot is clear it takes the one that covers least.</summary>
     */
    public static class LabelPlacer
    {
        #region Private Fields

        /** <summary>Where along a wire a label may sit, best first.</summary> */
        private static readonly float[] Spots = { 0.5f, 0.42f, 0.58f, 0.34f, 0.66f, 0.28f, 0.72f, 0.22f, 0.78f };

        /** <summary>Space kept around a label.</summary> */
        private const float Padding = 2f;

        private const int WireSamples = 32;

        #endregion

        #region Public API

        /** <summary>How far a label keeps from the wire's end, past its own half-width, so the arrowhead shows.</summary> */
        public const float EndClearance = 22f;

        /** <summary>How far a label keeps from the wire's start, past its own half-width.</summary> */
        public const float StartClearance = 12f;

        /** <summary>The centre of each label, in order. Obstacles are the nodes' rects.</summary> */
        public static Vector2[] Place(IReadOnlyList<LabelRequest> labels, IReadOnlyList<Rect> obstacles)
        {
            var samples = new List<Vector2>[labels.Count];
            var bounds = new Rect[labels.Count];
            for (var i = 0; i < labels.Count; i++)
            {
                samples[i] = new List<Vector2>(WireSamples + 1);
                WireCurve.Sample(labels[i].From, labels[i].To, WireSamples, samples[i]);
                bounds[i] = WireCurve.Bounds(labels[i].From, labels[i].To);
            }

            var placed = new List<Rect>(labels.Count);
            var centres = new Vector2[labels.Count];
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
                    foreach (var obstacle in obstacles)
                        if (box.Overlaps(obstacle)) score += 1000f;
                    foreach (var other in placed)
                        if (box.Overlaps(other)) score += 1000f;
                    for (var j = 0; j < samples.Length; j++)
                        if (j != i && bounds[j].Overlaps(box) && Crosses(samples[j], box)) score += 10f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = centre;
                    }
                    if (score < 1f) break;
                }
                centres[i] = best;
                placed.Add(Box(best, label.Size));
            }
            return centres;
        }

        /** <summary>The rect a label of a size takes at a centre, padding included.</summary> */
        public static Rect Box(Vector2 centre, Vector2 size) => new Rect(centre - size / 2f - Vector2.one * Padding, size + Vector2.one * Padding * 2f);

        #endregion

        #region Helpers

        private static bool Crosses(List<Vector2> wire, Rect box)
        {
            foreach (var point in wire)
                if (box.Contains(point)) return true;
            for (var i = 1; i < wire.Count; i++)
                if (SegmentCrosses(wire[i - 1], wire[i], box)) return true;
            return false;
        }

        /** <summary>True when a segment passes through a rect: either end inside, or it crosses an edge.</summary> */
        private static bool SegmentCrosses(Vector2 a, Vector2 b, Rect box)
        {
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

        #endregion
    }
}
