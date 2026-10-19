using System;
using Extensions.Inspector;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>One thing a step asks for, counted by its tracker up to a target.</summary> */
    [Serializable]
    public sealed class QuestObjective
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor, unique in its quest. Saves point at it.")]
        [SerializeField] private string id;

        [Tooltip("What the objective asks, as players see it.")]
        [TextArea]
        [SerializeField] private string text;

        [Tooltip("An optional (bonus) objective never holds its step back.")]
        [SerializeField] private bool optional;

        [Tooltip("How much progress completes it: 1 for a single event, more for a count.")]
        [Min(1)]
        [SerializeField] private int targetCount = 1;

        [Tooltip("What counts towards it.")]
        [SerializeReference, SubclassPicker] private ObjectiveTracker tracker;

        #endregion

        #region Properties

        /** <summary>The objective's id, unique in its quest.</summary> */
        public string Id => id;

        /** <summary>What the objective asks.</summary> */
        public string Text => text;

        /** <summary>True for a bonus objective.</summary> */
        public bool Optional => optional;

        /** <summary>How much progress completes it; at least 1.</summary> */
        public int TargetCount => Math.Max(1, targetCount);

        /** <summary>What counts towards it; null counts nothing.</summary> */
        public ObjectiveTracker Tracker => tracker;

        #endregion

        #region Public API

        /** <summary>Makes an objective in code, as tests and tools do.</summary> */
        public QuestObjective(string id, ObjectiveTracker tracker, int targetCount = 1, bool optional = false, string text = null)
        {
            this.id = id;
            this.tracker = tracker;
            this.targetCount = targetCount;
            this.optional = optional;
            this.text = text;
        }

        /** <summary>Gives the objective a new id, for the editor when it is missing or repeated.</summary> */
        public void AssignId(string newId) => id = newId;

        #endregion
    }
}
