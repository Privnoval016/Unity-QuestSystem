namespace QuestSystem
{
    /** <summary>A quest's state.</summary> */
    public enum QuestState
    {
        /** <summary>Not given yet.</summary> */
        NotGiven = 0,
        /** <summary>Given and under way.</summary> */
        Active = 1,
        /** <summary>Completed. A repeatable quest can be given again.</summary> */
        Done = 2,
        /** <summary>Failed.</summary> */
        Failed = 3,
    }

    /** <summary>A step's state.</summary> */
    public enum StepState
    {
        /** <summary>Not started.</summary> */
        Locked = 0,
        /** <summary>Started; its objectives count.</summary> */
        Active = 1,
        /** <summary>Completed.</summary> */
        Done = 2,
        /** <summary>Failed, which fails its quest.</summary> */
        Failed = 3,
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

        /** <summary>An objective's progress, by its id in the quest; 0 when unknown.</summary> */
        int ProgressOf(QuestDefinition quest, string objectiveId);
    }

    /** <summary>Story flags, as conditions read them.</summary> */
    public interface IFlagFacts
    {
        /** <summary>A flag's value.</summary> */
        int ValueOf(StoryFlag flag);
    }
}
