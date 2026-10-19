using System;
using System.Collections.Generic;
using System.Linq;

namespace QuestSystem
{
    /** <summary>A quest's runtime state: its state, completions and steps.</summary> */
    internal sealed class QuestRecord
    {
        #region Properties

        public QuestDefinition Definition { get; }
        public QuestState State { get; set; }
        public int Completions { get; set; }
        public IReadOnlyList<StepRecord> Steps { get; }

        #endregion

        #region Public API

        public QuestRecord(QuestDefinition definition)
        {
            Definition = definition;
            Steps = definition.Steps.Where(step => step != null).Select(step => new StepRecord(step)).ToList();
        }

        /** <summary>Makes the quest active with every step locked again; completions are kept.</summary> */
        public void Start()
        {
            State = QuestState.Active;
            foreach (var step in Steps)
                step.Reset();
        }

        /** <summary>Back to never given.</summary> */
        public void Reset()
        {
            State = QuestState.NotGiven;
            Completions = 0;
            foreach (var step in Steps)
                step.Reset();
        }

        public StepRecord Find(string stepId) => Steps.FirstOrDefault(step => string.Equals(step.Definition.Id, stepId, StringComparison.Ordinal));

        public ObjectiveRecord FindObjective(string objectiveId) =>
            Steps.SelectMany(step => step.Objectives).FirstOrDefault(objective => string.Equals(objective.Definition.Id, objectiveId, StringComparison.Ordinal));

        /** <summary>True when completing this step completes the quest: a step marked to, or else the last step.</summary> */
        public bool EndsWith(StepRecord step) =>
            Steps.Any(candidate => candidate.Definition.EndsQuest) ? step.Definition.EndsQuest : Steps.Count > 0 && Steps[Steps.Count - 1] == step;

        /** <summary>The step before this one; null for the first.</summary> */
        public StepRecord Before(StepRecord step)
        {
            for (var i = 1; i < Steps.Count; i++)
                if (Steps[i] == step) return Steps[i - 1];
            return null;
        }

        /** <summary>The step after this one; null for the last.</summary> */
        public StepRecord After(StepRecord step)
        {
            for (var i = 0; i < Steps.Count - 1; i++)
                if (Steps[i] == step) return Steps[i + 1];
            return null;
        }

        public QuestEntry ToEntry() => new QuestEntry
        {
            Quest = Definition.AssetId.Value,
            State = State,
            Completions = Completions,
            Steps = Steps.Select(step => step.ToEntry()).ToList(),
        };

        public void Restore(QuestEntry entry)
        {
            State = entry.State;
            Completions = Math.Max(0, entry.Completions);
            foreach (var saved in entry.Steps ?? new List<StepEntry>())
                Find(saved?.Step)?.Restore(saved);
        }

        #endregion
    }

    /** <summary>A step's runtime state.</summary> */
    internal sealed class StepRecord
    {
        #region Properties

        public QuestStep Definition { get; }
        public StepState State { get; set; }
        public IReadOnlyList<ObjectiveRecord> Objectives { get; }

        /** <summary>True when its objectives complete it, by its completion rule.</summary> */
        public bool IsComplete
        {
            get
            {
                if (Definition.Completion == StepCompletion.AnyOne) return Objectives.Count == 0 || Objectives.Any(objective => objective.IsDone);
                return Objectives.Where(objective => !objective.Definition.Optional).All(objective => objective.IsDone);
            }
        }

        #endregion

        #region Public API

        public StepRecord(QuestStep definition)
        {
            Definition = definition;
            Objectives = definition.Objectives.Where(objective => objective != null).Select(objective => new ObjectiveRecord(objective)).ToList();
        }

        public void Reset()
        {
            State = StepState.Locked;
            foreach (var objective in Objectives)
                objective.Progress = 0;
        }

        public StepEntry ToEntry() => new StepEntry
        {
            Step = Definition.Id,
            State = State,
            Objectives = Objectives.Select(objective => new ObjectiveEntry { Objective = objective.Definition.Id, Progress = objective.Progress }).ToList(),
        };

        public void Restore(StepEntry entry)
        {
            State = entry.State;
            foreach (var saved in entry.Objectives ?? new List<ObjectiveEntry>())
            {
                var objective = Objectives.FirstOrDefault(candidate => string.Equals(candidate.Definition.Id, saved?.Objective, StringComparison.Ordinal));
                if (objective != null) objective.Progress = Math.Max(0, Math.Min(objective.Definition.TargetCount, saved.Progress));
            }
        }

        #endregion
    }

    /** <summary>An objective's runtime progress.</summary> */
    internal sealed class ObjectiveRecord
    {
        public QuestObjective Definition { get; }
        public int Progress { get; set; }
        public bool IsDone => Progress >= Definition.TargetCount;

        public ObjectiveRecord(QuestObjective definition) => Definition = definition;
    }
}
