using System;
using System.Collections.Generic;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>How a value compares to a number.</summary> */
    public enum Comparison
    {
        /** <summary>At least the number.</summary> */
        AtLeast,
        /** <summary>Exactly the number.</summary> */
        Equal,
        /** <summary>Below the number.</summary> */
        Below,
    }

    /** <summary>Compares values by a <see cref="Comparison" />.</summary> */
    public static class Comparisons
    {
        /** <summary>True when the value compares to the number as asked.</summary> */
        public static bool Holds(Comparison comparison, int value, int number) => comparison switch
        {
            Comparison.Equal => value == number,
            Comparison.Below => value < number,
            _ => value >= number,
        };
    }

    /** <summary>True when a story flag's value compares to a number as asked: by default, when the flag is set.</summary> */
    [Serializable]
    public sealed class FlagCondition : ICondition<IFacts>, ISubjectReader
    {
        #region Inspector Fields

        [Tooltip("The flag to read.")]
        [SerializeField] private StoryFlag flag;

        [Tooltip("How its value compares to the number.")]
        [SerializeField] private Comparison comparison;

        [Tooltip("The number to compare with. At least 1 means 'is set'.")]
        [SerializeField] private int value = 1;

        #endregion

        #region Public API

        /** <summary>An empty condition, for the Inspector.</summary> */
        public FlagCondition() { }

        /** <summary>A condition on a flag, as code builds one.</summary> */
        public FlagCondition(StoryFlag flag, int value = 1, Comparison comparison = Comparison.AtLeast)
        {
            this.flag = flag;
            this.value = value;
            this.comparison = comparison;
        }

        /** <inheritdoc /> */
        public bool Evaluate(IFacts context) =>
            flag != null && context != null && context.TryGet<IFlagFacts>(out var flags) && Comparisons.Holds(comparison, flags.ValueOf(flag), value);

        /** <inheritdoc /> */
        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (flag != null) subjects.Add(flag);
            return true;
        }

        #endregion
    }

    /** <summary>True when a quest is in a state, or has been completed at least some number of times.</summary> */
    [Serializable]
    public sealed class QuestStateCondition : ICondition<IFacts>, ISubjectReader
    {
        #region Inspector Fields

        [Tooltip("The quest to read.")]
        [SerializeField] private QuestDefinition quest;

        [Tooltip("The state it must be in.")]
        [SerializeField] private QuestState state = QuestState.Done;

        [Tooltip("For Done: how many completions are needed. A repeatable quest can need more than one.")]
        [Min(1)]
        [SerializeField] private int completions = 1;

        #endregion

        #region Public API

        /** <summary>An empty condition, for the Inspector.</summary> */
        public QuestStateCondition() { }

        /** <summary>A condition on a quest, as code builds one.</summary> */
        public QuestStateCondition(QuestDefinition quest, QuestState state = QuestState.Done, int completions = 1)
        {
            this.quest = quest;
            this.state = state;
            this.completions = completions;
        }

        /** <inheritdoc /> */
        public bool Evaluate(IFacts context)
        {
            if (quest == null || context == null || !context.TryGet<IQuestFacts>(out var quests)) return false;
            return state == QuestState.Done
                ? quests.CompletionsOf(quest) >= Math.Max(1, completions)
                : quests.StateOf(quest) == state;
        }

        /** <inheritdoc /> */
        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (quest != null) subjects.Add(quest);
            return true;
        }

        #endregion
    }

    /** <summary>Shows a string field as a menu of a quest's steps; the quest is the named sibling field.</summary> */
    public sealed class StepIdPickerAttribute : PropertyAttribute
    {
        /** <summary>The sibling field holding the quest.</summary> */
        public string QuestField { get; }

        /** <summary>Picks from the steps of the quest in the named sibling field.</summary> */
        public StepIdPickerAttribute(string questField) => QuestField = questField;
    }

    /** <summary>True when one of a quest's steps is in a state.</summary> */
    [Serializable]
    public sealed class StepStateCondition : ICondition<IFacts>, ISubjectReader
    {
        #region Inspector Fields

        [Tooltip("The quest the step belongs to.")]
        [SerializeField] private QuestDefinition quest;

        [Tooltip("The step, chosen from the quest's steps.")]
        [StepIdPicker(nameof(quest))]
        [SerializeField] private string stepId;

        [Tooltip("The state it must be in.")]
        [SerializeField] private StepState state = StepState.Done;

        #endregion

        #region Public API

        /** <summary>An empty condition, for the Inspector.</summary> */
        public StepStateCondition() { }

        /** <summary>A condition on a step, as code builds one.</summary> */
        public StepStateCondition(QuestDefinition quest, string stepId, StepState state = StepState.Done)
        {
            this.quest = quest;
            this.stepId = stepId;
            this.state = state;
        }

        /** <summary>The quest the step belongs to.</summary> */
        public QuestDefinition Quest => quest;

        /** <summary>The step's id.</summary> */
        public string StepId => stepId;

        /** <inheritdoc /> */
        public bool Evaluate(IFacts context) =>
            quest != null && context != null && context.TryGet<IQuestFacts>(out var quests) && quests.StateOf(quest, stepId) == state;

        /** <inheritdoc /> */
        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (quest != null) subjects.Add(quest);
            return true;
        }

        #endregion
    }
}
