using System;
using Extensions.Logic;

namespace QuestSystem
{
    [Serializable]

    public class ObjectiveCondition : ICondition<QuestOutcome>
    {
        public QuestObjective objectiveKey;

        public bool Evaluate(QuestOutcome context)
        {
            if (objectiveKey == null)
            {
                return false;
            }

            return QuestManager.Instance.IsObjectiveComplete(objectiveKey);
        }
    }

    [Serializable]
    public class OutcomeCondition : ICondition<QuestOutcome>
    {
        public QuestOutcome outcomeKey;
        public int requiredCompletions = 1;

        public bool Evaluate(QuestOutcome context)
        {
            if (outcomeKey == null)
            {
                return false;
            }

            return QuestManager.Instance.IsObjectiveComplete(outcomeKey, requiredCompletions);
        }
    }
}

