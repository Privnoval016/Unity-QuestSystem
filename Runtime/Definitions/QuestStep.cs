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

    /** <summary>When a step starts, before its unlock condition (if any) is also asked.</summary> */
    public enum StepStart
    {
        /** <summary>After the step above it is done; the first step starts with the quest. With an unlock condition, when that holds instead. Either way it follows the step above.</summary> */
        AfterStepAbove,
        /** <summary>After its linked steps are done, all of them or any one; and when its unlock condition holds, if it has one.</summary> */
        AfterLinkedSteps,
        /** <summary>With the quest; and when its unlock condition holds, if it has one.</summary> */
        WithQuest,
    }

    /** <summary>How a step's links start it.</summary> */
    public enum StepJoin
    {
        /** <summary>Once every linked step is done.</summary> */
        All,
        /** <summary>Once any one linked step is done.</summary> */
        Any,
    }

    /** <summary>One stage of a quest: a set of objectives that starts after other steps or by a condition.</summary> */
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

        [Tooltip("When the step starts: after the step above, after its linked steps, or with the quest.")]
        [SerializeField] private StepStart start;

        [Tooltip("The steps this one comes after, when it starts after linked steps.")]
        [StepIdPicker]
        [SerializeField] private List<string> after = new List<string>();

        [Tooltip("Whether every linked step, or any one, must be done.")]
        [SerializeField] private StepJoin afterRule;

        [Tooltip("A further condition for the step to start. After the step above, it replaces waiting for that step; otherwise both must hold. Empty: none.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> unlockWhen;

        [Tooltip("Whether every required objective, or any one, completes the step.")]
        [SerializeField] private StepCompletion completion;

        [Tooltip("Completing this step completes the quest. Without any such step, every step nothing follows completes it.")]
        [SerializeField] private bool endsQuest;

        [Tooltip("When the step fails, which fails the quest. Empty: it can't fail.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> failWhen;

        [Tooltip("What the step asks for.")]
        [SerializeField] private List<QuestObjective> objectives = new List<QuestObjective>();

        [HideInInspector]
        [SerializeField] private Vector2 graphPosition;

        #endregion

        #region Properties

        /** <summary>The step's id, unique in its quest.</summary> */
        public string Id => id;

        /** <summary>What the step is about.</summary> */
        public string Text => text;

        /** <summary>True when players don't see the step until it starts.</summary> */
        public bool HiddenUntilReached => hiddenUntilReached;

        /** <summary>When the step starts, before its unlock condition is asked.</summary> */
        public StepStart Start => start;

        /** <summary>The ids of the steps it comes after, used when it starts after linked steps.</summary> */
        public IReadOnlyList<string> After => after;

        /** <summary>Whether every linked step, or any one, must be done.</summary> */
        public StepJoin AfterRule => afterRule;

        /** <summary>A further condition for it to start; null means none.</summary> */
        public ICondition<IFacts> UnlockWhen => unlockWhen;

        /** <summary>How its objectives complete it.</summary> */
        public StepCompletion Completion => completion;

        /** <summary>True when completing it completes the quest.</summary> */
        public bool EndsQuest => endsQuest;

        /** <summary>When it fails; null means never.</summary> */
        public ICondition<IFacts> FailWhen => failWhen;

        /** <summary>What it asks for.</summary> */
        public IReadOnlyList<QuestObjective> Objectives => objectives;

        /** <summary>Where the quest graph draws it; never read by the game.</summary> */
        public Vector2 GraphPosition => graphPosition;

        #endregion

        #region Public API

        /** <summary>Makes a step in code, as tests and tools do.</summary> */
        public QuestStep(string id, IEnumerable<QuestObjective> objectives, StepSettings settings = default)
        {
            this.id = id;
            this.objectives = new List<QuestObjective>(objectives ?? Array.Empty<QuestObjective>());
            unlockWhen = settings.UnlockWhen;
            start = settings.Start;
            after = new List<string>(settings.After ?? Array.Empty<string>());
            afterRule = settings.AfterRule;
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
        /** <summary>When the step starts, before its unlock condition is asked.</summary> */
        public StepStart Start;

        /** <summary>The ids of the steps it comes after.</summary> */
        public IEnumerable<string> After;

        /** <summary>Whether every linked step, or any one, must be done.</summary> */
        public StepJoin AfterRule;

        /** <summary>A further condition for it to start; null means none.</summary> */
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
