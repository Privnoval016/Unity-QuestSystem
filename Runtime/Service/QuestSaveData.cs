using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace QuestSystem
{
    /** <summary>Everything the quest service saves, as plain data. Assets are named by their asset ids.</summary> */
    [Serializable, Preserve]
    public sealed class QuestSaveData
    {
        /** <summary>Every quest that has been given at least once.</summary> */
        public List<QuestEntry> Quests = new List<QuestEntry>();

        /** <summary>Every flag whose value differs from its initial value, or was set.</summary> */
        public List<FlagEntry> Flags = new List<FlagEntry>();

        /** <summary>The quest the player follows, by id; null when none.</summary> */
        public string ActiveQuest;
    }

    /** <summary>One quest's saved state.</summary> */
    [Serializable, Preserve]
    public sealed class QuestEntry
    {
        /** <summary>The quest's asset id.</summary> */
        public string Quest;

        /** <summary>Its state.</summary> */
        public QuestState State;

        /** <summary>How many times it has been completed.</summary> */
        public int Completions;

        /** <summary>When it was last given, as a count of quest events that game; 0 when never.</summary> */
        public long GivenOrder;

        /** <summary>When it last ended, as a count of quest events that game; 0 when never.</summary> */
        public long EndedOrder;

        /** <summary>Its steps' states, by step id.</summary> */
        public List<StepEntry> Steps = new List<StepEntry>();

        /** <summary>Its objectives' progress, by objective id (unique in the quest), whichever step they are in.</summary> */
        public List<ObjectiveEntry> Objectives = new List<ObjectiveEntry>();
    }

    /** <summary>One step's saved state.</summary> */
    [Serializable, Preserve]
    public sealed class StepEntry
    {
        /** <summary>The step's id.</summary> */
        public string Step;

        /** <summary>Its state.</summary> */
        public StepState State;
    }

    /** <summary>One objective's saved progress.</summary> */
    [Serializable, Preserve]
    public sealed class ObjectiveEntry
    {
        /** <summary>The objective's id.</summary> */
        public string Objective;

        /** <summary>Its progress.</summary> */
        public int Progress;
    }

    /** <summary>One flag's saved value.</summary> */
    [Serializable, Preserve]
    public sealed class FlagEntry
    {
        /** <summary>The flag's asset id.</summary> */
        public string Flag;

        /** <summary>Its value.</summary> */
        public int Value;
    }
}
