using UnityEngine;

namespace QuestSystem
{
    /** <summary>Shows a string field as a menu of a quest's steps: those of the quest in a named sibling field, or of the quest being edited.</summary> */
    public sealed class StepIdPickerAttribute : PropertyAttribute
    {
        /** <summary>The sibling field holding the quest; null for the quest being edited.</summary> */
        public string QuestField { get; }

        /** <summary>Picks from the steps of the quest in the named sibling field, or of the quest being edited when none is named.</summary> */
        public StepIdPickerAttribute(string questField = null) => QuestField = questField;
    }
}
