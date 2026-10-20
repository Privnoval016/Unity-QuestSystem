using System;
using System.Collections.Generic;
using Extensions.Inspector;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /**
     * <summary>What counts towards an objective. A tracker counts signals (<see cref="ISignalCounter" />), reads state
     * (<see cref="IStateProgress" />), or both. A custom objective is a subclass implementing either.</summary>
     */
    [Serializable]
    public abstract class ObjectiveTracker : ISubjectReader
    {
        #region Properties

        /** <summary>What is missing for the tracker to ever make progress, for validation; null when nothing is.</summary> */
        public virtual string MissingSetup => null;

        #endregion

        #region Public API

        /**
         * <summary>Adds what the tracker reads. By default a signal counter reads nothing, and a state tracker that doesn't
         * override this can't say, so it is checked after every change.</summary>
         * <returns>False when it can't say.</returns>
         */
        public virtual bool TryCollectSubjects(ICollection<IConditionSubject> subjects) => Trackers.StateProgressOf(this) == null;

        /** <summary>Adds the conditions it holds, so tools can find what they read; none by default.</summary> */
        public virtual void CollectConditions(ICollection<ICondition<IFacts>> conditions) { }

        #endregion
    }

    /** <summary>A tracker that counts signals.</summary> */
    public interface ISignalCounter
    {
        /**
         * <summary>Progress to add when a signal is raised while the objective is active; 0 when it doesn't count it.</summary>
         * <param name="signal">The signal.</param>
         * <param name="amount">How much of it, at least 1.</param>
         * <param name="context">What the signal says about the moment (its payload), layered over the world's facts.</param>
         */
        int ProgressFrom(QuestSignal signal, int amount, IFacts context);

        /** <summary>Adds the signals it counts, so a signal reaches only its counters.</summary> <returns>False when it can't say: it then hears every signal.</returns> */
        bool TryCollectSignals(ICollection<QuestSignal> signals);
    }

    /** <summary>A tracker whose progress follows state, such as a flag or a condition.</summary> */
    public interface IStateProgress
    {
        /** <summary>The progress the current facts give; <see cref="Trackers.Complete" /> for done, whatever the target.</summary> */
        int ProgressNow(IFacts facts);
    }

    /** <summary>The one place that asks a tracker what it can do.</summary> */
    public static class Trackers
    {
        /** <summary>Progress that completes any objective.</summary> */
        public const int Complete = int.MaxValue;

        /** <summary>The tracker as a signal counter; null when it doesn't count signals.</summary> */
        public static ISignalCounter SignalCounterOf(ObjectiveTracker tracker) => tracker as ISignalCounter;

        /** <summary>The tracker as state progress; null when it doesn't read state.</summary> */
        public static IStateProgress StateProgressOf(ObjectiveTracker tracker) => tracker as IStateProgress;
    }

    /** <summary>Counts a signal: each time it is raised adds its amount.</summary> */
    [Serializable]
    public sealed class SignalTracker : ObjectiveTracker, ISignalCounter
    {
        #region Inspector Fields

        [Tooltip("The signal that counts.")]
        [SerializeField] private QuestSignal signal;

        [Tooltip("Count it only when this holds, read from what the signal says (its payload) and then the world: a foe's level, the weather, a flag. Empty: always.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> onlyWhen;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public override string MissingSetup => signal == null ? "no signal" : null;

        #endregion

        #region Public API

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public SignalTracker() { }

        /** <summary>A tracker counting a signal, as code builds one.</summary> */
        public SignalTracker(QuestSignal signal, ICondition<IFacts> onlyWhen = null)
        {
            this.signal = signal;
            this.onlyWhen = onlyWhen;
        }

        /** <inheritdoc /> */
        public int ProgressFrom(QuestSignal raised, int amount, IFacts context)
        {
            if (raised == null || raised != signal) return 0;
            return onlyWhen == null || onlyWhen.Evaluate(context) ? Math.Max(0, amount) : 0;
        }

        /** <inheritdoc /> */
        public bool TryCollectSignals(ICollection<QuestSignal> signals)
        {
            if (signal != null) signals.Add(signal);
            return true;
        }

        /** <inheritdoc /> */
        public override void CollectConditions(ICollection<ICondition<IFacts>> conditions)
        {
            if (onlyWhen != null) conditions.Add(onlyWhen);
        }

        /** <summary>The signal, and when it counts: "Wolf Defeated if foe is Wolf".</summary> */
        public override string ToString() => (signal == null ? "no signal" : signal.name) + (onlyWhen == null ? "" : " if " + ConditionTree.Summarize(onlyWhen));

        #endregion
    }

    /** <summary>Reads a story flag: its value is the progress, so a counter flag counts and an on/off flag completes it.</summary> */
    [Serializable]
    public sealed class FlagTracker : ObjectiveTracker, IStateProgress
    {
        #region Inspector Fields

        [Tooltip("The flag whose value is the progress.")]
        [SerializeField] private StoryFlag flag;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public override string MissingSetup => flag == null ? "no flag" : null;

        #endregion

        #region Public API

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public FlagTracker() { }

        /** <summary>A tracker reading a flag, as code builds one.</summary> */
        public FlagTracker(StoryFlag flag) => this.flag = flag;

        /** <inheritdoc /> */
        public int ProgressNow(IFacts facts) =>
            flag != null && facts != null && facts.TryGet<IFlagFacts>(out var flags) ? Math.Max(0, flags.ValueOf(flag)) : 0;

        /** <inheritdoc /> */
        public override bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (flag != null) subjects.Add(flag);
            return true;
        }

        /** <summary>The flag: "flag Elders".</summary> */
        public override string ToString() => "flag " + (flag == null ? "none" : flag.name);

        #endregion
    }

    /** <summary>Completes the objective while a condition holds: another quest done, a flag set, anything the game can check.</summary> */
    [Serializable]
    public sealed class ConditionTracker : ObjectiveTracker, IStateProgress
    {
        #region Inspector Fields

        [Tooltip("The condition that completes the objective.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> condition;

        #endregion

        #region Properties

        /** <inheritdoc /> */
        public override string MissingSetup => condition == null ? "no condition" : null;

        #endregion

        #region Public API

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public ConditionTracker() { }

        /** <summary>A tracker on a condition, as code builds one.</summary> */
        public ConditionTracker(ICondition<IFacts> condition) => this.condition = condition;

        /** <inheritdoc /> */
        public int ProgressNow(IFacts facts) => condition != null && condition.Evaluate(facts) ? Trackers.Complete : 0;

        /** <inheritdoc /> */
        public override bool TryCollectSubjects(ICollection<IConditionSubject> subjects) => ConditionSubjects.TryCollect(condition, subjects);

        /** <inheritdoc /> */
        public override void CollectConditions(ICollection<ICondition<IFacts>> conditions)
        {
            if (condition != null) conditions.Add(condition);
        }

        /** <summary>The condition: "while The Smith's Request done".</summary> */
        public override string ToString() => "while " + ConditionTree.Summarize(condition);

        #endregion
    }
}
