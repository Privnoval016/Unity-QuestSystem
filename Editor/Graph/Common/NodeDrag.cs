using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Moving nodes by dragging: where each started, how far the pointer has gone, the grid they snap to, and
     * the threshold below which a press is still a click. Both quest graph screens use it.</summary>
     */
    public sealed class NodeDrag<TKey>
    {
        #region Private Fields

        private Dictionary<TKey, Vector2> _from;
        private Vector2 _pointer;

        #endregion

        #region Properties

        /** <summary>True while a press may become a drag.</summary> */
        public bool Active => _from != null;

        /** <summary>True once the pointer has gone past the threshold.</summary> */
        public bool Moved { get; private set; }

        /** <summary>The nodes being dragged.</summary> */
        public IEnumerable<TKey> Keys => _from?.Keys ?? Enumerable.Empty<TKey>();

        #endregion

        #region Public API

        /** <summary>Starts a press on nodes at their positions, with the pointer's screen position.</summary> */
        public void Begin(IEnumerable<KeyValuePair<TKey, Vector2>> positions, Vector2 pointer)
        {
            _from = positions.ToDictionary(pair => pair.Key, pair => pair.Value);
            _pointer = pointer;
            Moved = false;
        }

        /**
         * <summary>Where each node goes for a pointer at a screen position, snapped to the grid; null while the press is
         * still within the threshold.</summary>
         */
        public Dictionary<TKey, Vector2> Move(Vector2 pointer, float zoom)
        {
            if (_from == null) return null;
            var screen = pointer - _pointer;
            if (!Moved && screen.magnitude < GraphMetrics.DragThreshold) return null;
            Moved = true;
            var delta = screen / zoom;
            return _from.ToDictionary(pair => pair.Key, pair => GraphMetrics.Snap(pair.Value + delta));
        }

        /** <summary>Ends the press, returning whether it became a drag.</summary> */
        public bool End()
        {
            var moved = Moved;
            _from = null;
            Moved = false;
            return moved;
        }

        #endregion
    }
}
