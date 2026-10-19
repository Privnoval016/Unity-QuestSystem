using System;
using System.Collections.Generic;
using Extensions.Inspector;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /**
     * <summary>What counts towards an objective. A tracker counts signals, reads state, or both; override what applies.
     * A custom objective is a subclass.</summary>
     */
    [Serializable]
    public abstract class ObjectiveTracker : ISubjectReader
    {
        /** <summary>Progress to add when a signal is raised while the objective is active; 0 when it doesn't count.</summary> */
        public virtual int ProgressFrom(QuestSignal signal, int amount) => 0;

        /** <summary>The progress the current facts give, for trackers that read state; null for trackers that only count signals.</summary> */
        public virtual int? ProgressNow(IFacts facts) => null;

        /** <summary>Adds what the tracker reads. Signal-only trackers read nothing.</summary> <returns>False when it can't say.</returns> */
        public virtual bool TryCollectSubjects(ICollection<IConditionSubject> subjects) => true;
    }

    /** <summary>Counts a signal: each time it is raised adds its amount.</summary> */
    [Serializable]
    public sealed class SignalTracker : ObjectiveTracker
    {
        [Tooltip("The signal that counts.")]
        [SerializeField] private QuestSignal signal;

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public SignalTracker() { }

        /** <summary>A tracker counting a signal, as code builds one.</summary> */
        public SignalTracker(QuestSignal signal) => this.signal = signal;

        /** <inheritdoc /> */
        public override int ProgressFrom(QuestSignal raised, int amount) => raised != null && raised == signal ? Math.Max(0, amount) : 0;
    }

    /** <summary>Reads a story flag: its value is the progress, so a counter flag counts and an on/off flag completes it.</summary> */
    [Serializable]
    public sealed class FlagTracker : ObjectiveTracker
    {
        [Tooltip("The flag whose value is the progress.")]
        [SerializeField] private StoryFlag flag;

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public FlagTracker() { }

        /** <summary>A tracker reading a flag, as code builds one.</summary> */
        public FlagTracker(StoryFlag flag) => this.flag = flag;

        /** <inheritdoc /> */
        public override int? ProgressNow(IFacts facts) =>
            flag != null && facts != null && facts.TryGet<IFlagFacts>(out var flags) ? Math.Max(0, flags.ValueOf(flag)) : 0;

        /** <inheritdoc /> */
        public override bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            if (flag != null) subjects.Add(flag);
            return true;
        }
    }

    /** <summary>Completes the objective while a condition holds: another quest done, a flag set, anything the game can check.</summary> */
    [Serializable]
    public sealed class ConditionTracker : ObjectiveTracker
    {
        [Tooltip("The condition that completes the objective.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> condition;

        /** <summary>An empty tracker, for the Inspector.</summary> */
        public ConditionTracker() { }

        /** <summary>A tracker on a condition, as code builds one.</summary> */
        public ConditionTracker(ICondition<IFacts> condition) => this.condition = condition;

        /** <inheritdoc /> */
        public override int? ProgressNow(IFacts facts) => condition != null && condition.Evaluate(facts) ? int.MaxValue : 0;

        /** <inheritdoc /> */
        public override bool TryCollectSubjects(ICollection<IConditionSubject> subjects) => ConditionSubjects.TryCollect(condition, subjects);
    }
}
