namespace QuestSystem
{
    /** <summary>Words for quest and step states, for summaries.</summary> */
    public static class QuestStates
    {
        /** <summary>A quest state in words: "not given", "active", "done", "failed".</summary> */
        public static string NameOf(QuestState state) => state switch
        {
            QuestState.NotGiven => "not given",
            QuestState.Active => "active",
            QuestState.Done => "done",
            _ => "failed",
        };

        /** <summary>A step state in words: "locked", "active", "done", "failed".</summary> */
        public static string NameOf(StepState state) => state switch
        {
            StepState.Locked => "locked",
            StepState.Active => "active",
            StepState.Done => "done",
            _ => "failed",
        };

        /** <summary>A kind's display name, or its asset name without one; "No kind" when unset.</summary> */
        public static string NameOf(QuestKind kind) =>
            kind == null ? "No kind" : string.IsNullOrWhiteSpace(kind.DisplayName) ? kind.name : kind.DisplayName;

        /** <summary>A quest's title, or its asset name without one; "no quest" when unset.</summary> */
        public static string TitleOf(QuestDefinition quest) =>
            quest == null ? "no quest" : string.IsNullOrWhiteSpace(quest.Title) ? quest.name : quest.Title;
    }
}
