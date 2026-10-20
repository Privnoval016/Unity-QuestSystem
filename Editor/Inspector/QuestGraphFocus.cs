using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace QuestSystem.EditorTools
{
    /** <summary>Which step of a quest the graph has selected, so the Inspector shows that step alone.</summary> */
    public static class QuestGraphFocus
    {
        #region Private Fields

        /** <summary>Keyed by quest, resolving to the step the graph has selected.</summary> */
        private static readonly Dictionary<QuestDefinition, string> Steps = new Dictionary<QuestDefinition, string>();

        #endregion

        #region Public API

        /** <summary>The step the graph has selected in a quest; null for the whole quest.</summary> */
        public static string StepOf(QuestDefinition quest) => quest != null && Steps.TryGetValue(quest, out var id) ? id : null;

        /** <summary>Selects a quest in the Inspector, showing one step or (null) the whole quest.</summary> */
        public static void Focus(QuestDefinition quest, string stepId)
        {
            if (quest == null) return;
            Forget(null);
            var changed = StepOf(quest) != stepId || Selection.activeObject != quest;
            if (stepId == null) Steps.Remove(quest);
            else Steps[quest] = stepId;
            if (Selection.activeObject != quest) Selection.activeObject = quest;
            if (changed) ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        /** <summary>Lets the Inspector show a quest whole again, without selecting it; with null, forgets quests that no longer exist.</summary> */
        public static void Forget(QuestDefinition quest)
        {
            if (quest != null)
            {
                if (Steps.Remove(quest)) ActiveEditorTracker.sharedTracker.ForceRebuild();
                return;
            }
            foreach (var gone in Steps.Keys.Where(key => key == null).ToList())
                Steps.Remove(gone);
        }

        #endregion
    }
}
