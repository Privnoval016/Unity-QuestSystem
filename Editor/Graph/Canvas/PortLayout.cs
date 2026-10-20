using System.Collections.Generic;

namespace QuestSystem.EditorTools
{
    /** <summary>Spreads a node's ports along its edge, ordered by where each wire's other end sits, so fan-in and fan-out wires don't stack.</summary> */
    public static class PortLayout
    {
        #region Public API

        /** <summary>How far ports keep from a node's top and bottom.</summary> */
        public const float Inset = 6f;

        /** <summary>The y of each of <paramref name="count" /> ports along an edge from <paramref name="top" />, evenly spaced inside the inset.</summary> */
        public static List<float> Spread(int count, float top, float height)
        {
            var ys = new List<float>(count);
            for (var i = 0; i < count; i++)
                ys.Add(top + Inset + (height - Inset * 2f) * (i + 1) / (count + 1));
            return ys;
        }

        #endregion
    }
}
