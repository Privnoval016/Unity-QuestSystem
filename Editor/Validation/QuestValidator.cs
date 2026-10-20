using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>How bad a quest problem is.</summary> */
    public enum QuestIssueLevel
    {
        /** <summary>Worth a look, but the quest can still be played.</summary> */
        Warning,
        /** <summary>Part of the quest can never progress. Fails a build.</summary> */
        Error,
    }

    /** <summary>One problem with a quest.</summary> */
    public readonly struct QuestIssue
    {
        #region Properties

        /** <summary>How bad it is.</summary> */
        public QuestIssueLevel Level { get; }

        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>What is wrong, naming the step or objective.</summary> */
        public string Problem { get; }

        /** <summary>The step it is about; null when it is about the whole quest.</summary> */
        public string StepId { get; }

        #endregion

        #region Public API

        /** <summary>Makes an issue, about one step when given its id.</summary> */
        public QuestIssue(QuestIssueLevel level, QuestDefinition quest, string problem, string stepId = null)
        {
            Level = level;
            Quest = quest;
            Problem = problem;
            StepId = stepId;
        }

        /** <inheritdoc /> */
        public override string ToString() => $"{Level}: {(Quest == null ? "?" : Quest.name)}: {Problem}";

        #endregion
    }

    /** <summary>Checks quests for setup that can never work: objectives with nothing to count, steps waiting on themselves.</summary> */
    public static class QuestValidator
    {
        #region Public API

        /** <summary>Every problem with a quest.</summary> */
        public static List<QuestIssue> Validate(QuestDefinition quest)
        {
            var issues = new List<QuestIssue>();
            if (quest == null) return issues;
            if (quest.AssetId.IsEmpty) issues.Add(Error(quest, "has no asset id, so it can't be saved"));
            if (quest.Steps.Count == 0) issues.Add(Error(quest, "has no steps, so it completes the moment it is given"));
            if (quest.Kind == null) issues.Add(Warning(quest, "has no kind"));
            if (string.IsNullOrWhiteSpace(quest.Title)) issues.Add(Warning(quest, "has no title"));

            for (var i = 0; i < quest.Steps.Count; i++)
            {
                var step = quest.Steps[i];
                var label = $"step {i + 1}";
                if (step == null)
                {
                    issues.Add(Error(quest, $"{label} is empty"));
                    continue;
                }
                if (step.Objectives.Count == 0) issues.Add(Warning(quest, $"{label} has no objectives, so it completes the moment it starts", step.Id));
                ValidateLinks(quest, step, label, issues);
                for (var j = 0; j < step.Objectives.Count; j++)
                {
                    var objective = step.Objectives[j];
                    var objectiveLabel = $"{label}, objective {j + 1}";
                    if (objective == null) issues.Add(Error(quest, $"{objectiveLabel} is empty", step.Id));
                    else if (objective.Tracker == null) issues.Add(Error(quest, $"{objectiveLabel} has no tracker, so it never progresses", step.Id));
                    else if (objective.Tracker.MissingSetup != null) issues.Add(Error(quest, $"{objectiveLabel}: {objective.Tracker.MissingSetup}", step.Id));
                }
            }
            ValidateGraph(quest, issues);
            return issues;
        }

        /** <summary>Every problem with every quest given.</summary> */
        public static List<QuestIssue> ValidateAll(IEnumerable<QuestDefinition> quests) => quests.SelectMany(Validate).ToList();

        #endregion

        #region Helpers

        private static void ValidateLinks(QuestDefinition quest, QuestStep step, string label, List<QuestIssue> issues)
        {
            if (step.Start != StepStart.AfterLinkedSteps) return;
            if (step.After.Count == 0) issues.Add(Error(quest, $"{label} starts after linked steps but has no links", step.Id));
            var seen = new HashSet<string>();
            foreach (var id in step.After)
            {
                if (id == step.Id) issues.Add(Error(quest, $"{label} is linked to itself", step.Id));
                else if (quest.FindStep(id) == null) issues.Add(Error(quest, $"{label} is linked to a step that doesn't exist ({id})", step.Id));
                if (!seen.Add(id ?? string.Empty)) issues.Add(Error(quest, $"{label} is linked to the same step twice", step.Id));
            }
            if (step.AfterRule == StepJoin.Any && step.After.Count == 1) issues.Add(Warning(quest, $"{label} starts after any of one linked step", step.Id));
        }

        /** <summary>Steps waiting on each other, steps nothing can start, and endings nobody marked.</summary> */
        private static void ValidateGraph(QuestDefinition quest, List<QuestIssue> issues)
        {
            var record = new QuestRecord(quest);
            var labels = new Dictionary<StepRecord, string>();
            for (int i = 0, n = 0; i < quest.Steps.Count; i++)
                if (quest.Steps[i] != null) labels[record.Steps[n++]] = $"step {i + 1}";

            var waits = record.Steps.ToDictionary(step => step, step => new StepWaits(record, step));
            var startable = Startable(record, waits);
            var stuck = new HashSet<StepRecord>(record.Steps.Where(step => !startable.Contains(step)));
            var waiting = new HashSet<StepRecord>();
            foreach (var cycle in Cycles(stuck, waits))
            {
                waiting.UnionWith(cycle);
                var message = cycle.Count == 1
                    ? $"{labels[cycle[0]]} waits on itself, so it never starts"
                    : $"{string.Join(", ", cycle.Select(step => labels[step]))} wait on each other, so none starts";
                foreach (var step in cycle)
                    issues.Add(Error(quest, message, step.Definition.Id));
            }
            foreach (var step in stuck.Where(step => !waiting.Contains(step)))
                issues.Add(Warning(quest, $"{labels[step]} can never start", step.Definition.Id));

            if (!quest.Steps.Any(step => step != null && step.EndsQuest))
            {
                var ends = record.Steps.Where(step => step.Dependants.Count == 0).ToList();
                if (ends.Count > 1) issues.Add(Warning(quest, $"{string.Join(", ", ends.Select(step => labels[step]))} each end the quest, since nothing follows them; mark the ending steps to be sure"));
            }
        }

        /** <summary>Every step that can ever start: started over and over until nothing more can, as the quest service would.</summary> */
        private static HashSet<StepRecord> Startable(QuestRecord record, Dictionary<StepRecord, StepWaits> waits)
        {
            var started = new HashSet<StepRecord>();
            for (var changed = true; changed;)
            {
                changed = false;
                foreach (var step in record.Steps)
                    if (!started.Contains(step) && waits[step].CanStart(started)) changed |= started.Add(step);
            }
            return started;
        }

        /** <summary>Each set of steps among those that can't start that wait on each other, found by a depth-first walk.</summary> */
        private static List<List<StepRecord>> Cycles(HashSet<StepRecord> stuck, Dictionary<StepRecord, StepWaits> waits)
        {
            var cycles = new List<List<StepRecord>>();
            var state = new Dictionary<StepRecord, int>();
            var path = new List<StepRecord>();
            void Visit(StepRecord step)
            {
                state[step] = 1;
                path.Add(step);
                foreach (var before in waits[step].All.Where(stuck.Contains))
                {
                    if (!state.TryGetValue(before, out var seen)) Visit(before);
                    else if (seen == 1) cycles.Add(path.GetRange(path.IndexOf(before), path.Count - path.IndexOf(before)));
                }
                path.RemoveAt(path.Count - 1);
                state[step] = 2;
            }
            foreach (var step in stuck)
                if (!state.ContainsKey(step)) Visit(step);
            return cycles;
        }

        /**
         * <summary>What a step waits for before it can start: the steps it follows, all of them or any one (none when an
         * unlock condition replaces waiting for the step above), and the steps of its own quest its unlock condition
         * requires to have happened.</summary>
         */
        private sealed class StepWaits
        {
            private readonly List<StepRecord> _links = new List<StepRecord>();
            private readonly bool _anyLink;
            private readonly List<StepRecord> _conditions = new List<StepRecord>();

            /** <summary>Every step it waits for, links and conditions alike.</summary> */
            public IEnumerable<StepRecord> All => _links.Concat(_conditions);

            public StepWaits(QuestRecord record, StepRecord step)
            {
                var definition = step.Definition;
                var conditionReplacesWait = definition.Start == StepStart.AfterStepAbove && definition.UnlockWhen != null;
                if (!conditionReplacesWait) _links.AddRange(step.Follows);
                _anyLink = definition.Start == StepStart.AfterLinkedSteps && definition.AfterRule == StepJoin.Any;

                var found = new List<FoundReference>();
                QuestReferences.Collect(definition.UnlockWhen, found);
                foreach (var reference in found)
                {
                    var read = reference.Reference;
                    if (!reference.Use.AsItself || !reference.Use.Required || read.Quest != record.Definition || read.StepId == null) continue;
                    if (read.StateName == QuestStates.NameOf(StepState.Locked)) continue;
                    var target = record.Find(read.StepId);
                    if (target != null && !_conditions.Contains(target)) _conditions.Add(target);
                }
            }

            /** <summary>True when the steps already started let this one start.</summary> */
            public bool CanStart(HashSet<StepRecord> started)
            {
                var linksMet = _links.Count == 0 || (_anyLink ? _links.Any(started.Contains) : _links.All(started.Contains));
                return linksMet && _conditions.All(started.Contains);
            }
        }

        private static QuestIssue Error(QuestDefinition quest, string problem, string stepId = null) => new QuestIssue(QuestIssueLevel.Error, quest, problem, stepId);

        private static QuestIssue Warning(QuestDefinition quest, string problem, string stepId = null) => new QuestIssue(QuestIssueLevel.Warning, quest, problem, stepId);

        #endregion
    }
}
