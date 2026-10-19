using System;
using System.Collections.Generic;
using Extensions.Inspector;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>How a step's objectives complete it.</summary> */
    public enum StepCompletion
    {
        /** <summary>Every objective that isn't optional is done. A step with none is done as soon as it starts.</summary> */
        AllRequired,
        /** <summary>Any one objective is done.</summary> */
        AnyOne,
    }

    /** <summary>One stage of a quest: a set of objectives that unlocks by a condition, in order by default.</summary> */
    [Serializable]
    public sealed class QuestStep
    {
        #region Inspector Fields

        [Tooltip("Filled in by the editor, unique in its quest. Saves point at it.")]
        [SerializeField] private string id;

        [Tooltip("What the step is about, as players see it.")]
        [TextArea]
        [SerializeField] private string text;

        [Tooltip("Hide the step from players until it starts.")]
        [SerializeField] private bool hiddenUntilReached = true;

        [Tooltip("When the step starts. Empty: when the step before it is done (the first step starts with the quest).")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> unlockWhen;

        [Tooltip("Whether every required objective, or any one, completes the step.")]
        [SerializeField] private StepCompletion completion;

        [Tooltip("Completing this step completes the quest. Without any such step, the last step completes it.")]
        [SerializeField] private bool endsQuest;

        [Tooltip("When the step fails, which fails the quest. Empty: it can't fail.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> failWhen;

        [Tooltip("What the step asks for.")]
        [SerializeField] private List<QuestObjective> objectives = new List<QuestObjective>();

        #endregion

        #region Properties

        /** <summary>The step's id, unique in its quest.</summary> */
        public string Id => id;

        /** <summary>What the step is about.</summary> */
        public string Text => text;

        /** <summary>True when players don't see the step until it starts.</summary> */
        public bool HiddenUntilReached => hiddenUntilReached;

        /** <summary>When the step starts; null means after the step before it.</summary> */
        public ICondition<IFacts> UnlockWhen => unlockWhen;

        /** <summary>How its objectives complete it.</summary> */
        public StepCompletion Completion => completion;

        /** <summary>True when completing it completes the quest.</summary> */
        public bool EndsQuest => endsQuest;

        /** <summary>When it fails; null means never.</summary> */
        public ICondition<IFacts> FailWhen => failWhen;

        /** <summary>What it asks for.</summary> */
        public IReadOnlyList<QuestObjective> Objectives => objectives;

        #endregion

        #region Public API

        /** <summary>Makes a step in code, as tests and tools do.</summary> */
        public QuestStep(string id, IEnumerable<QuestObjective> objectives, StepSettings settings = default)
        {
            this.id = id;
            this.objectives = new List<QuestObjective>(objectives ?? Array.Empty<QuestObjective>());
            unlockWhen = settings.UnlockWhen;
            failWhen = settings.FailWhen;
            completion = settings.Completion;
            endsQuest = settings.EndsQuest;
            hiddenUntilReached = settings.HiddenUntilReached;
            text = settings.Text;
        }

        /** <summary>Gives the step a new id, for the editor when it is missing or repeated.</summary> */
        internal void AssignId(string newId) => id = newId;

        #endregion
    }

    /** <summary>A step's settings, for making one in code.</summary> */
    public struct StepSettings
    {
        /** <summary>When the step starts; null means after the step before it.</summary> */
        public ICondition<IFacts> UnlockWhen;

        /** <summary>When it fails; null means never.</summary> */
        public ICondition<IFacts> FailWhen;

        /** <summary>How its objectives complete it.</summary> */
        public StepCompletion Completion;

        /** <summary>True when completing it completes the quest.</summary> */
        public bool EndsQuest;

        /** <summary>True when players don't see it until it starts.</summary> */
        public bool HiddenUntilReached;

        /** <summary>What the step is about.</summary> */
        public string Text;
    }
}
