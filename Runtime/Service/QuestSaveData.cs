using System;
using System.Collections.Generic;

namespace QuestSystem
{
    /** <summary>Everything the quest service saves, as plain data. Assets are named by their asset ids.</summary> */
    [Serializable]
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
    [Serializable]
    public sealed class QuestEntry
    {
        /** <summary>The quest's asset id.</summary> */
        public string Quest;

        /** <summary>Its state.</summary> */
        public QuestState State;

        /** <summary>How many times it has been completed.</summary> */
        public int Completions;

        /** <summary>Its steps' states, by step id.</summary> */
        public List<StepEntry> Steps = new List<StepEntry>();
    }

    /** <summary>One step's saved state.</summary> */
    [Serializable]
    public sealed class StepEntry
    {
        /** <summary>The step's id.</summary> */
        public string Step;

        /** <summary>Its state.</summary> */
        public StepState State;

        /** <summary>Its objectives' progress, by objective id.</summary> */
        public List<ObjectiveEntry> Objectives = new List<ObjectiveEntry>();
    }

    /** <summary>One objective's saved progress.</summary> */
    [Serializable]
    public sealed class ObjectiveEntry
    {
        /** <summary>The objective's id.</summary> */
        public string Objective;

        /** <summary>Its progress.</summary> */
        public int Progress;
    }

    /** <summary>One flag's saved value.</summary> */
    [Serializable]
    public sealed class FlagEntry
    {
        /** <summary>The flag's asset id.</summary> */
        public string Flag;

        /** <summary>Its value.</summary> */
        public int Value;
    }
}
