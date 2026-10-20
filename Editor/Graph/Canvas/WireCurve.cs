using System.Collections.Generic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>The shape every wire takes: a horizontal S-curve from an output on the right to an input on the left.</summary> */
    public static class WireCurve
    {
        #region Public API

        /** <summary>How far the curve's handles reach out from each end.</summary> */
        public static float Bend(Vector2 from, Vector2 to) => Mathf.Max(40f, Mathf.Abs(to.x - from.x) * 0.45f);

        /** <summary>The point a fraction <paramref name="t" /> of the way along the curve.</summary> */
        public static Vector2 Point(Vector2 from, Vector2 to, float t)
        {
            var bend = Bend(from, to);
            var u = 1f - t;
            var c1 = from + new Vector2(bend, 0f);
            var c2 = to - new Vector2(bend, 0f);
            return u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to;
        }

        /** <summary>The two control points for drawing the curve.</summary> */
        public static (Vector2 c1, Vector2 c2) Handles(Vector2 from, Vector2 to)
        {
            var bend = Bend(from, to);
            return (from + new Vector2(bend, 0f), to - new Vector2(bend, 0f));
        }

        /** <summary>Points along the curve, both ends included.</summary> */
        public static void Sample(Vector2 from, Vector2 to, int segments, List<Vector2> into)
        {
            for (var i = 0; i <= segments; i++)
                into.Add(Point(from, to, i / (float)segments));
        }

        /** <summary>A box around the whole curve, handles and line width included, so anything outside it can't touch the curve.</summary> */
        public static Rect Bounds(Vector2 from, Vector2 to)
        {
            var (c1, c2) = Handles(from, to);
            var min = Vector2.Min(Vector2.Min(from, to), Vector2.Min(c1, c2)) - Vector2.one;
            var max = Vector2.Max(Vector2.Max(from, to), Vector2.Max(c1, c2)) + Vector2.one;
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /** <summary>The distance from a point to the curve, measured along sampled segments.</summary> */
        public static float Distance(Vector2 from, Vector2 to, Vector2 point, int segments = 24)
        {
            var best = float.MaxValue;
            var previous = from;
            for (var i = 1; i <= segments; i++)
            {
                var next = Point(from, to, i / (float)segments);
                best = Mathf.Min(best, DistanceToSegment(point, previous, next));
                previous = next;
            }
            return best;
        }

        #endregion

        #region Helpers

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(point, a + ab * t);
        }

        #endregion
    }
}
