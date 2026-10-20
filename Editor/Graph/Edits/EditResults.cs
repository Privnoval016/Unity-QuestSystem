using System.Collections.Generic;

namespace QuestSystem.EditorTools
{
    /** <summary>What deleting steps changed: the steps rewired to keep their order, and the conditions elsewhere that read a deleted step.</summary> */
    public readonly struct DeleteResult
    {
        /** <summary>The steps whose links were rewired.</summary> */
        public IReadOnlyList<QuestStep> Rewired { get; }

        /** <summary>The conditions that read a deleted step, in this quest or others.</summary> */
        public IReadOnlyList<QuestLink> Readers { get; }

        /** <summary>Makes a result.</summary> */
        public DeleteResult(IReadOnlyList<QuestStep> rewired, IReadOnlyList<QuestLink> readers)
        {
            Rewired = rewired;
            Readers = readers;
        }
    }

    /** <summary>What a paste added: the new steps' ids, and how many links to steps that weren't copied were dropped.</summary> */
    public readonly struct PasteResult
    {
        /** <summary>The ids of the pasted steps.</summary> */
        public IReadOnlyList<string> StepIds { get; }

        /** <summary>How many links pointed outside the copied steps and were dropped.</summary> */
        public int DroppedLinks { get; }

        /** <summary>Makes a result.</summary> */
        public PasteResult(IReadOnlyList<string> stepIds, int droppedLinks)
        {
            StepIds = stepIds;
            DroppedLinks = droppedLinks;
        }
    }
}
