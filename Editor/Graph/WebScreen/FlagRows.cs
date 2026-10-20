using System.Collections.Generic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Places story flags in rows above the web: each up and to the left of its first reader, never on a quest
     * or another flag. A flag that would touch the one before it goes up a row rather than sliding past its reader.</summary>
     */
    public static class FlagRows
    {
        #region Public API

        /** <summary>How wide a flag may be.</summary> */
        public const float FlagWidth = 150f;

        /** <summary>How tall a flag is.</summary> */
        public const float FlagHeight = 26f;

        /** <summary>How far left of its first reader a flag starts.</summary> */
        public const float Lead = 230f;

        /** <summary>How far above the bands the lowest row sits.</summary> */
        public const float RowAbove = 70f;

        /** <summary>Space between rows.</summary> */
        public const float RowStep = 34f;

        /** <summary>Space between flags in a row.</summary> */
        public const float Gap = 12f;

        /** <summary>Where each flag goes, in the order given; flags are best given sorted by their first reader's x.</summary> */
        public static List<Vector2> Place(IReadOnlyList<float> firstReaderX, float bandsTop)
        {
            var nextFree = new List<float>();
            var positions = new List<Vector2>(firstReaderX.Count);
            foreach (var readerX in firstReaderX)
            {
                var x = readerX - Lead;
                var row = 0;
                while (row < nextFree.Count && x < nextFree[row]) row++;
                if (row == nextFree.Count) nextFree.Add(float.MinValue);
                nextFree[row] = x + FlagWidth + Gap;
                positions.Add(new Vector2(x, bandsTop - RowAbove - row * RowStep));
            }
            return positions;
        }

        #endregion
    }
}
