using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>A ring drawn around a selected node, outside its border, so selecting never moves anything inside it.</summary> */
    public static class SelectionRing
    {
        #region Public API

        /** <summary>The class a node takes while selected, which shows its ring.</summary> */
        public const string SelectedClass = "quest-graph--selected";

        /** <summary>Makes a ring to add as a node's last child.</summary> */
        public static VisualElement Make()
        {
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("quest-graph__ring");
            return ring;
        }

        #endregion
    }
}
