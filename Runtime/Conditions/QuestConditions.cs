using System;
using System.Collections.Generic;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>True when a story flag's value compares to a number as asked: by default, when the flag is set.</summary> */
    [Serializable]
    public sealed class FlagCondition : ICondition<IFacts>, ISubjectReader
    {
        #region Inspector Fields

        [Tooltip("The flag to read.")]
        [SerializeField] private StoryFlag flag;

        [Tooltip("How its value compares to the number.")]
        [SerializeField] private NumberComparison comparison;

        [Tooltip("The number to compare with. At least 1 means 'is set'.")]
        [SerializeField] private int value = 1;

        #endregion

        #region Public API

        /** <summary>An empty condition, for the Inspector.</summary> */
        public FlagCondition() { }

        /** <summary>A condition on a flag, as code builds one.</summary> */
        public FlagCondition(StoryFlag flag, int value = 1, NumberComparison comparison = NumberComparison.AtLeast)
        {
            this.flag = flag;
            this.value = value;
            this.comparison = comparison;
        }

        /** <inheritdoc /> */
        public bool Evaluate(IFacts context) =>
            flag != null && context != null && context.TryGet<IFlagFacts>(out var flags) && NumberComparisons.Holds(comparison, flags.ValueOf(flag), value);

        /** <inheritdoc /> */
        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (flag != null) subjects.Add(flag);
            return true;
        }

        /** <summary>The flag and the comparison: "Sided With Guild", "Elders ≥ 3", "Sided With Guild = 0".</summary> */
        public override string ToString()
        {
            var name = flag == null ? "no flag" : flag.name;
            if (comparison == NumberComparison.AtLeast && value == 1) return name;
            return comparison switch
            {
                NumberComparison.Equal => $"{name} = {value}",
                NumberComparison.Below => $"{name} < {value}",
                _ => $"{name} ≥ {value}",
            };
        }

        #endregion
    }

    /** <summary>True when a quest is in a state, or has been completed at least some number of times.</summary> */
    [Serializable]
    public sealed class QuestStateCondition : ICondition<IFacts>, ISubjectReader, IQuestReference
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
        public QuestDefinition Quest => quest;

        /** <inheritdoc /> */
        public string StepId => null;

        /** <inheritdoc /> */
        public string StateName => state == QuestState.Done && completions > 1 ? $"done {completions} times" : QuestStates.NameOf(state);

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

        /** <summary>The quest and the state: "The Smith's Request done".</summary> */
        public override string ToString() => $"{QuestStates.TitleOf(quest)} {StateName}";

        #endregion
    }

    /** <summary>True when one of a quest's steps is in a state.</summary> */
    [Serializable]
    public sealed class StepStateCondition : ICondition<IFacts>, ISubjectReader, IQuestReference, IStepRetargetable
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

        /** <inheritdoc /> */
        public QuestDefinition Quest => quest;

        /** <inheritdoc /> */
        public string StepId => stepId;

        /** <inheritdoc /> */
        public string StateName => QuestStates.NameOf(state);

        /** <inheritdoc /> */
        public void Retarget(QuestDefinition targetQuest, string targetStepId)
        {
            quest = targetQuest;
            stepId = targetStepId;
        }

        /** <inheritdoc /> */
        public bool Evaluate(IFacts context) =>
            quest != null && context != null && context.TryGet<IQuestFacts>(out var quests) && quests.StateOf(quest, stepId) == state;

        /** <inheritdoc /> */
        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (quest != null) subjects.Add(quest);
            return true;
        }

        /** <summary>The quest, the step and the state: "Guild Vote: Side with the rebels done".</summary> */
        public override string ToString()
        {
            var step = quest == null ? null : quest.FindStep(stepId);
            var stepName = step == null ? $"missing step {stepId}" : string.IsNullOrWhiteSpace(step.Text) ? step.Id : step.Text;
            return $"{QuestStates.TitleOf(quest)}: {stepName} {StateName}";
        }

        #endregion
    }
}
