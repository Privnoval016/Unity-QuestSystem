using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>Measures the quest graph shares across its screens and edits.</summary> */
    public static class GraphMetrics
    {
        #region Public API

        /** <summary>Positions snap to multiples of this, so moves make small, stable diffs.</summary> */
        public const float Grid = 10f;

        /** <summary>How far, in screen points, the pointer moves before a press becomes a drag.</summary> */
        public const float DragThreshold = 3f;

        /** <summary>How far, in screen points, a selection box must reach before it selects instead of clicking.</summary> */
        public const float ClickThreshold = 4f;

        /** <summary>How far, in content units, a drop may miss a node and still land on it, so ports at its edge count.</summary> */
        public const float DropReach = 12f;

        /** <summary>A position on the grid; halves round away from zero, so a position never snaps differently on a second move.</summary> */
        public static Vector2 Snap(Vector2 position) => new Vector2(SnapAxis(position.x), SnapAxis(position.y));

        #endregion

        #region Helpers

        private static float SnapAxis(float value) => Mathf.Sign(value) * Mathf.Floor(Mathf.Abs(value) / Grid + 0.5f) * Grid;

        #endregion
    }
}
