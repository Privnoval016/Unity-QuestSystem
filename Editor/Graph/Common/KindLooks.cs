using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>How the quest graph shows a quest's kind: a colour class from the kind's chosen colour, grey for none.</summary> */
    public static class KindLooks
    {
        #region Public API

        /** <summary>The USS class colouring an element by a kind.</summary> */
        public static string ClassOf(QuestKind kind) => "quest-graph__kind--" + (kind == null ? "none" : kind.GraphColour.ToString().ToLowerInvariant());

        /** <summary>A small square in a kind's colour.</summary> */
        public static VisualElement Swatch(QuestKind kind)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("quest-graph__swatch");
            swatch.AddToClassList(ClassOf(kind));
            return swatch;
        }

        #endregion
    }
}
