using Extensions.Logic;

namespace QuestSystem.EditorTools
{
    /** <summary>Where in a quest a condition sits.</summary> */
    public enum LinkSlot
    {
        /** <summary>The quest's "available when".</summary> */
        Available,
        /** <summary>The quest's "fail when".</summary> */
        Fails,
        /** <summary>A step's "unlock when".</summary> */
        StepUnlock,
        /** <summary>A step's "fail when".</summary> */
        StepFails,
        /** <summary>An objective's tracker.</summary> */
        Objective,
    }

    /** <summary>What each slot means for a link: its wording, whether it fails the quest, whether it gates giving it.</summary> */
    public static class LinkSlots
    {
        /** <summary>The verb a link in the slot starts with: "needs", "fails if", "tracks".</summary> */
        public static string Verb(LinkSlot slot) => slot switch
        {
            LinkSlot.Fails or LinkSlot.StepFails => "fails if",
            LinkSlot.Objective => "tracks",
            _ => "needs",
        };

        /** <summary>True when a condition in the slot fails the quest.</summary> */
        public static bool Fails(LinkSlot slot) => slot == LinkSlot.Fails || slot == LinkSlot.StepFails;

        /** <summary>True when a condition in the slot decides whether the quest can be given.</summary> */
        public static bool GatesAvailability(LinkSlot slot) => slot == LinkSlot.Available;
    }

    /** <summary>One quest reading another (or itself): which condition, which step, and how.</summary> */
    public sealed class QuestLink
    {
        #region Properties

        /** <summary>The quest whose condition reads.</summary> */
        public QuestDefinition Reader { get; }

        /** <summary>The reader's step the condition belongs to; null for the quest's own conditions.</summary> */
        public string ReaderStepId { get; }

        /** <summary>Where the condition sits.</summary> */
        public LinkSlot Slot { get; }

        /** <summary>The quest read.</summary> */
        public QuestDefinition Read { get; }

        /** <summary>The step read; null when the whole quest is.</summary> */
        public string ReadStepId { get; }

        /** <summary>The state asked for, in words; <see cref="UnknownState" /> when the condition can't say.</summary> */
        public string StateName { get; }

        /** <summary>How the condition tree uses it.</summary> */
        public LeafUse Use { get; }

        /** <summary>The link in a few words, for wires: "needs done", "fails if step done", "needs not active".</summary> */
        public string Label => $"{LinkSlots.Verb(Slot)} {(Use.AsItself ? "" : "not ")}{(ReadStepId == null ? "" : "step ")}{StateName}";

        /** <summary>The state a link shows when its condition reads a quest but can't say which state.</summary> */
        public const string UnknownState = "read";

        #endregion

        #region Public API

        /** <summary>Makes a link.</summary> */
        public QuestLink(QuestDefinition reader, string readerStepId, LinkSlot slot, QuestDefinition read, string readStepId, string stateName, LeafUse use)
        {
            Reader = reader;
            ReaderStepId = readerStepId;
            Slot = slot;
            Read = read;
            ReadStepId = readStepId;
            StateName = stateName;
            Use = use;
        }

        #endregion
    }
}
