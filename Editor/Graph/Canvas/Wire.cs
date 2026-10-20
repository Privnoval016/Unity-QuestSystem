using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>What a wire means, which sets its colour.</summary> */
    public enum WireStyle
    {
        /** <summary>A plain link.</summary> */
        Normal,
        /** <summary>One of several links, any of which starts the step.</summary> */
        Any,
        /** <summary>A link that fails the quest.</summary> */
        Fails,
        /** <summary>A story flag read.</summary> */
        Flag,
        /** <summary>An implicit link, such as "after the step above".</summary> */
        Ghost,
        /** <summary>A wire being drawn.</summary> */
        Preview,
    }

    /** <summary>One wire on the canvas, in content space.</summary> */
    public sealed class Wire
    {
        /** <summary>Names the wire for its owner, such as "a>b".</summary> */
        public string Key { get; set; }

        /** <summary>Where it starts.</summary> */
        public Vector2 From { get; set; }

        /** <summary>Where it ends.</summary> */
        public Vector2 To { get; set; }

        /** <summary>What it means.</summary> */
        public WireStyle Style { get; set; }

        /** <summary>True for a dashed line.</summary> */
        public bool Dashed { get; set; }

        /** <summary>True to draw an arrowhead at its end.</summary> */
        public bool Arrow { get; set; }

        /** <summary>True when it is drawn faint, outside a focus.</summary> */
        public bool Faded { get; set; }

        /** <summary>True when it is selected.</summary> */
        public bool Selected { get; set; }
    }
}
