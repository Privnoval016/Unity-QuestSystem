using Extensions.EventBus;

namespace QuestSystem
{
    /** <summary>A quest was given.</summary> */
    public readonly struct QuestGivenEvent : IEvent
    {
        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>Makes the event.</summary> */
        public QuestGivenEvent(QuestDefinition quest) => Quest = quest;
    }

    /** <summary>A step started, or completed, or failed.</summary> */
    public readonly struct StepChangedEvent : IEvent
    {
        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>The step.</summary> */
        public QuestStep Step { get; }

        /** <summary>Its new state.</summary> */
        public StepState State { get; }

        /** <summary>Makes the event.</summary> */
        public StepChangedEvent(QuestDefinition quest, QuestStep step, StepState state)
        {
            Quest = quest;
            Step = step;
            State = state;
        }
    }

    /** <summary>An objective's progress changed.</summary> */
    public readonly struct ObjectiveProgressedEvent : IEvent
    {
        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>The objective.</summary> */
        public QuestObjective Objective { get; }

        /** <summary>Its progress now, out of its target.</summary> */
        public int Progress { get; }

        /** <summary>Makes the event.</summary> */
        public ObjectiveProgressedEvent(QuestDefinition quest, QuestObjective objective, int progress)
        {
            Quest = quest;
            Objective = objective;
            Progress = progress;
        }
    }

    /** <summary>A quest was completed or failed.</summary> */
    public readonly struct QuestEndedEvent : IEvent
    {
        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary><see cref="QuestState.Done" /> or <see cref="QuestState.Failed" />.</summary> */
        public QuestState State { get; }

        /** <summary>How many times it has been completed, this time included.</summary> */
        public int Completions { get; }

        /** <summary>Makes the event.</summary> */
        public QuestEndedEvent(QuestDefinition quest, QuestState state, int completions)
        {
            Quest = quest;
            State = state;
            Completions = completions;
        }
    }

    /** <summary>A story flag's value changed.</summary> */
    public readonly struct FlagChangedEvent : IEvent
    {
        /** <summary>The flag.</summary> */
        public StoryFlag Flag { get; }

        /** <summary>Its new value.</summary> */
        public int Value { get; }

        /** <summary>Makes the event.</summary> */
        public FlagChangedEvent(StoryFlag flag, int value)
        {
            Flag = flag;
            Value = value;
        }
    }

    /** <summary>The quest the player follows changed; null when none.</summary> */
    public readonly struct ActiveQuestChangedEvent : IEvent
    {
        /** <summary>The quest; null when none.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>Makes the event.</summary> */
        public ActiveQuestChangedEvent(QuestDefinition quest) => Quest = quest;
    }
}
