using System;
using System.Collections.Generic;

namespace QuestSystem
{
    /** <summary>A quest's runtime state: its state, completions, the order it was given and ended, and its steps.</summary> */
    internal sealed class QuestRecord
    {
        #region Properties

        /** <summary>The quest.</summary> */
        public QuestDefinition Definition { get; }

        /** <summary>Its state.</summary> */
        public QuestState State { get; set; }

        /** <summary>How many times it has been completed.</summary> */
        public int Completions { get; set; }

        /** <summary>When it was last given, as a count of quest events this game; 0 when never.</summary> */
        public long GivenOrder { get; set; }

        /** <summary>When it last ended, as a count of quest events this game; 0 when never.</summary> */
        public long EndedOrder { get; set; }

        /** <summary>Its steps, in order.</summary> */
        public IReadOnlyList<StepRecord> Steps { get; }

        /** <summary>Every objective of every step.</summary> */
        public IReadOnlyList<ObjectiveRecord> Objectives { get; }

        /** <summary>True when a step is marked to end the quest; otherwise every step nothing follows does.</summary> */
        private bool HasMarkedEnd { get; }

        #endregion

        #region Public API

        /** <summary>Builds the record for a quest, every step locked.</summary> */
        public QuestRecord(QuestDefinition definition)
        {
            Definition = definition;
            var steps = new List<StepRecord>();
            var objectives = new List<ObjectiveRecord>();
            foreach (var step in definition.Steps)
            {
                if (step == null) continue;
                var record = new StepRecord(step, this);
                steps.Add(record);
                objectives.AddRange(record.Objectives);
                HasMarkedEnd |= step.EndsQuest;
            }
            Steps = steps;
            Objectives = objectives;
            LinkSteps(steps);
        }

        /** <summary>Makes the quest active with every step locked again; completions are kept.</summary> */
        public void Start(long order)
        {
            State = QuestState.Active;
            GivenOrder = order;
            foreach (var step in Steps)
                step.Reset();
        }

        /** <summary>Back to never given.</summary> */
        public void Reset()
        {
            State = QuestState.NotGiven;
            Completions = 0;
            GivenOrder = 0;
            EndedOrder = 0;
            foreach (var step in Steps)
                step.Reset();
        }

        /** <summary>A step by id; null when there is none.</summary> */
        public StepRecord Find(string stepId)
        {
            foreach (var step in Steps)
                if (string.Equals(step.Definition.Id, stepId, StringComparison.Ordinal)) return step;
            return null;
        }

        /** <summary>An objective by id; null when there is none.</summary> */
        public ObjectiveRecord FindObjective(string objectiveId)
        {
            foreach (var objective in Objectives)
                if (string.Equals(objective.Definition.Id, objectiveId, StringComparison.Ordinal)) return objective;
            return null;
        }

        /** <summary>True when completing this step completes the quest: a step marked to, or else a step nothing follows.</summary> */
        public bool EndsWith(StepRecord step) => HasMarkedEnd ? step.Definition.EndsQuest : step.Dependants.Count == 0;

        /** <summary>True when a step that ends the quest is done.</summary> */
        public bool HasEnded()
        {
            foreach (var step in Steps)
                if (step.State == StepState.Done && EndsWith(step)) return true;
            return false;
        }


        /** <summary>The quest as saved data.</summary> */
        public QuestEntry ToEntry()
        {
            var entry = new QuestEntry
            {
                Quest = Definition.AssetId.Value, State = State, Completions = Completions, GivenOrder = GivenOrder, EndedOrder = EndedOrder,
            };
            foreach (var step in Steps)
                entry.Steps.Add(new StepEntry { Step = step.Definition.Id, State = step.State });
            foreach (var objective in Objectives)
                if (objective.Progress > 0) entry.Objectives.Add(new ObjectiveEntry { Objective = objective.Definition.Id, Progress = objective.Progress });
            return entry;
        }

        /** <summary>Takes the saved state. Unknown steps and objectives are ignored; progress is kept within the target.</summary> */
        public void Restore(QuestEntry entry)
        {
            State = Enum.IsDefined(typeof(QuestState), entry.State) ? entry.State : QuestState.NotGiven;
            Completions = Math.Max(0, entry.Completions);
            GivenOrder = Math.Max(0, entry.GivenOrder);
            EndedOrder = Math.Max(0, entry.EndedOrder);
            foreach (var saved in entry.Steps ?? new List<StepEntry>())
            {
                var step = saved == null ? null : Find(saved.Step);
                if (step != null && Enum.IsDefined(typeof(StepState), saved.State)) step.State = saved.State;
            }
            foreach (var saved in entry.Objectives ?? new List<ObjectiveEntry>())
            {
                var objective = saved == null ? null : FindObjective(saved.Objective);
                if (objective != null) objective.Progress = Math.Max(0, Math.Min(objective.Definition.TargetCount, saved.Progress));
            }
        }

        #endregion

        #region Helpers

        /** <summary>Links each step to the steps it follows: the step above, its linked steps, or none. Unknown ids and self-links are skipped.</summary> */
        private void LinkSteps(List<StepRecord> steps)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                switch (step.Definition.Start)
                {
                    case StepStart.AfterStepAbove:
                        if (i > 0) step.Follow(steps[i - 1]);
                        break;
                    case StepStart.AfterLinkedSteps:
                        foreach (var id in step.Definition.After)
                        {
                            var before = Find(id);
                            if (before != null && before != step) step.Follow(before);
                        }
                        break;
                }
            }
        }

        #endregion
    }

    /** <summary>A step's runtime state.</summary> */
    internal sealed class StepRecord
    {
        #region Private Fields

        private readonly List<StepRecord> _follows = new List<StepRecord>();
        private readonly List<StepRecord> _dependants = new List<StepRecord>();

        #endregion

        #region Properties

        /** <summary>The step.</summary> */
        public QuestStep Definition { get; }

        /** <summary>The quest it belongs to.</summary> */
        public QuestRecord Quest { get; }

        /** <summary>Its state.</summary> */
        public StepState State { get; set; }

        /** <summary>Its objectives.</summary> */
        public IReadOnlyList<ObjectiveRecord> Objectives { get; }

        /** <summary>The steps it comes after.</summary> */
        public IReadOnlyList<StepRecord> Follows => _follows;

        /** <summary>The steps that come after it.</summary> */
        public IReadOnlyList<StepRecord> Dependants => _dependants;

        /**
         * <summary>True when the steps it follows let it start: the step above is done (or its unlock condition
         * replaces waiting for it), its links are done by their rule, or it starts with the quest.</summary>
         */
        public bool IsStartMet => Definition.Start switch
        {
            StepStart.AfterStepAbove => Definition.UnlockWhen != null || AllFollowedDone(),
            StepStart.AfterLinkedSteps => Definition.AfterRule == StepJoin.Any && _follows.Count > 0 ? AnyFollowedDone() : AllFollowedDone(),
            _ => true,
        };

        /** <summary>True when its objectives complete it, by its completion rule.</summary> */
        public bool IsComplete
        {
            get
            {
                if (Definition.Completion == StepCompletion.AnyOne)
                {
                    if (Objectives.Count == 0) return true;
                    foreach (var objective in Objectives)
                        if (objective.IsDone) return true;
                    return false;
                }
                foreach (var objective in Objectives)
                    if (!objective.Definition.Optional && !objective.IsDone) return false;
                return true;
            }
        }

        #endregion

        #region Public API

        /** <summary>Builds the record for a step, locked.</summary> */
        public StepRecord(QuestStep definition, QuestRecord quest)
        {
            Definition = definition;
            Quest = quest;
            var objectives = new List<ObjectiveRecord>();
            foreach (var objective in definition.Objectives)
                if (objective != null) objectives.Add(new ObjectiveRecord(objective, this));
            Objectives = objectives;
        }

        /** <summary>Locked, with no progress.</summary> */
        public void Reset()
        {
            State = StepState.Locked;
            foreach (var objective in Objectives)
                objective.Progress = 0;
        }

        /** <summary>Makes this step come after another.</summary> */
        public void Follow(StepRecord before)
        {
            if (_follows.Contains(before)) return;
            _follows.Add(before);
            before._dependants.Add(this);
        }

        #endregion

        #region Helpers

        private bool AllFollowedDone()
        {
            foreach (var step in _follows)
                if (step.State != StepState.Done) return false;
            return true;
        }

        private bool AnyFollowedDone()
        {
            foreach (var step in _follows)
                if (step.State == StepState.Done) return true;
            return false;
        }

        #endregion
    }

    /** <summary>An objective's runtime progress, and what its tracker can do.</summary> */
    internal sealed class ObjectiveRecord
    {
        #region Properties

        /** <summary>The objective.</summary> */
        public QuestObjective Definition { get; }

        /** <summary>The step it belongs to.</summary> */
        public StepRecord Step { get; }

        /** <summary>Its progress, within its target.</summary> */
        public int Progress { get; set; }

        /** <summary>True when its progress reaches its target.</summary> */
        public bool IsDone => Progress >= Definition.TargetCount;

        /** <summary>Its tracker as a signal counter; null when it doesn't count signals.</summary> */
        public ISignalCounter Counter { get; }

        /** <summary>Its tracker as state progress; null when it doesn't read state.</summary> */
        public IStateProgress State { get; }

        #endregion

        #region Public API

        /** <summary>Builds the record for an objective.</summary> */
        public ObjectiveRecord(QuestObjective definition, StepRecord step)
        {
            Definition = definition;
            Step = step;
            Counter = Trackers.SignalCounterOf(definition.Tracker);
            State = Trackers.StateProgressOf(definition.Tracker);
        }

        #endregion
    }
}
