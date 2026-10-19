namespace QuestSystem
{
    /** <summary>A quest's state.</summary> */
    public enum QuestState
    {
        /** <summary>Not given yet.</summary> */
        NotGiven,
        /** <summary>Given and under way.</summary> */
        Active,
        /** <summary>Completed. A repeatable quest can be given again.</summary> */
        Done,
        /** <summary>Failed.</summary> */
        Failed,
    }

    /** <summary>A step's state.</summary> */
    public enum StepState
    {
        /** <summary>Not started.</summary> */
        Locked,
        /** <summary>Started; its objectives count.</summary> */
        Active,
        /** <summary>Completed.</summary> */
        Done,
        /** <summary>Failed, which fails its quest.</summary> */
        Failed,
    }

    /** <summary>Quest state, as conditions read it.</summary> */
    public interface IQuestFacts
    {
        /** <summary>A quest's state; <see cref="QuestState.NotGiven" /> for a quest the service doesn't know.</summary> */
        QuestState StateOf(QuestDefinition quest);

        /** <summary>A step's state; <see cref="StepState.Locked" /> when the quest or step is unknown.</summary> */
        StepState StateOf(QuestDefinition quest, string stepId);

        /** <summary>How many times a quest has been completed.</summary> */
        int CompletionsOf(QuestDefinition quest);
    }

    /** <summary>Story flags, as conditions read them.</summary> */
    public interface IFlagFacts
    {
        /** <summary>A flag's value.</summary> */
        int ValueOf(StoryFlag flag);
    }
}
