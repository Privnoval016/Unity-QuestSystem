namespace QuestSystem.EditorTools
{
    /** <summary>What the quest screen shows, for the status bar.</summary> */
    public readonly struct QuestCounts
    {
        /** <summary>How many steps.</summary> */
        public int Steps { get; }

        /** <summary>How many objectives across them.</summary> */
        public int Objectives { get; }

        /** <summary>How many errors the quest has.</summary> */
        public int Errors { get; }

        /** <summary>How many warnings the quest has.</summary> */
        public int Warnings { get; }

        /** <summary>Makes counts.</summary> */
        public QuestCounts(int steps, int objectives, int errors, int warnings)
        {
            Steps = steps;
            Objectives = objectives;
            Errors = errors;
            Warnings = warnings;
        }
    }

    /** <summary>What the web shows, for the status bar.</summary> */
    public readonly struct WebCounts
    {
        /** <summary>How many quests the filters show.</summary> */
        public int Shown { get; }

        /** <summary>How many quests there are.</summary> */
        public int Total { get; }

        /** <summary>How many links between them.</summary> */
        public int Links { get; }

        /** <summary>How many errors across every quest.</summary> */
        public int Errors { get; }

        /** <summary>How many warnings across every quest.</summary> */
        public int Warnings { get; }

        /** <summary>How many conditions can't say what they read.</summary> */
        public int Unreadable { get; }

        /** <summary>Makes counts.</summary> */
        public WebCounts(int shown, int total, int links, int errors, int warnings, int unreadable)
        {
            Shown = shown;
            Total = total;
            Links = links;
            Errors = errors;
            Warnings = warnings;
            Unreadable = unreadable;
        }

        /** <summary>The same counts with a different number shown.</summary> */
        public WebCounts WithShown(int shown) => new WebCounts(shown, Total, Links, Errors, Warnings, Unreadable);
    }
}
