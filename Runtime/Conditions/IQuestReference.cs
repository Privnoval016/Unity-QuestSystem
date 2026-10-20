using System.Collections.Generic;
using Extensions.Logic;

namespace QuestSystem
{
    /**
     * <summary>A condition that reads a quest or one of its steps, so tools can show what links to what. A game's own
     * condition on quests implements it to appear in the quest graph.</summary>
     */
    public interface IQuestReference
    {
        /** <summary>The quest it reads; null when unset.</summary> */
        QuestDefinition Quest { get; }

        /** <summary>The step it reads; null when it reads the whole quest.</summary> */
        string StepId { get; }

        /** <summary>The state it asks for, in a word or two: "done", "failed", "active".</summary> */
        string StateName { get; }
    }

    /** <summary>A condition reading a quest's step that tools can point at another quest's step, as copying steps between quests does.</summary> */
    public interface IStepRetargetable
    {
        /** <summary>Reads this quest's step instead.</summary> */
        void Retarget(QuestDefinition quest, string stepId);
    }

    /** <summary>A quest reference found in a condition tree, with how the tree uses it.</summary> */
    public readonly struct FoundReference
    {
        #region Properties

        /** <summary>The reference.</summary> */
        public IQuestReference Reference { get; }

        /** <summary>How the tree uses it: negated or not, required or one of several ways.</summary> */
        public LeafUse Use { get; }

        #endregion

        #region Public API

        /** <summary>Makes a found reference.</summary> */
        public FoundReference(IQuestReference reference, LeafUse use)
        {
            Reference = reference;
            Use = use;
        }

        #endregion
    }

    /** <summary>The one place that asks conditions and subjects what quests and flags they are.</summary> */
    public static class QuestReferences
    {
        #region Public API

        /** <summary>The condition as a quest reference; null when it isn't one.</summary> */
        public static IQuestReference Of(ICondition<IFacts> condition) => condition as IQuestReference;

        /** <summary>The condition as one tools can point at another step; null when it isn't one.</summary> */
        public static IStepRetargetable RetargetableOf(ICondition<IFacts> condition) => condition as IStepRetargetable;

        /** <summary>The subject as a quest; null when it isn't one.</summary> */
        public static QuestDefinition QuestOf(IConditionSubject subject) => subject as QuestDefinition;

        /** <summary>The subject as a story flag; null when it isn't one.</summary> */
        public static StoryFlag FlagOf(IConditionSubject subject) => subject as StoryFlag;

        /**
         * <summary>Adds every quest reference in a condition tree to <paramref name="found" />.</summary>
         * <returns>How many parts can't say what they read, so tools can say some links may be missing.</returns>
         */
        public static int Collect(ICondition<IFacts> condition, ICollection<FoundReference> found)
        {
            var unreadable = 0;
            var subjects = new List<IConditionSubject>();
            ConditionTree.VisitLeaves(condition, (leaf, use) =>
            {
                var reference = Of(leaf);
                if (reference != null)
                {
                    if (reference.Quest != null) found.Add(new FoundReference(reference, use));
                    return;
                }
                subjects.Clear();
                if (!ConditionSubjects.TryCollect(leaf, subjects)) unreadable++;
            });
            return unreadable;
        }

        #endregion
    }
}
