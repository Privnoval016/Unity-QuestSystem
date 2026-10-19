using UnityEngine;

namespace QuestSystem
{
    /** <summary>Shows a string field as a menu of a quest's steps; the quest is the named sibling field.</summary> */
    public sealed class StepIdPickerAttribute : PropertyAttribute
    {
        /** <summary>The sibling field holding the quest.</summary> */
        public string QuestField { get; }

        /** <summary>Picks from the steps of the quest in the named sibling field.</summary> */
        public StepIdPickerAttribute(string questField) => QuestField = questField;
    }
}
