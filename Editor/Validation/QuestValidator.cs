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

        #endregion

        #region Public API

        /** <summary>Makes an issue.</summary> */
        public QuestIssue(QuestIssueLevel level, QuestDefinition quest, string problem)
        {
            Level = level;
            Quest = quest;
            Problem = problem;
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
                if (step.Objectives.Count == 0) issues.Add(Warning(quest, $"{label} has no objectives, so it completes the moment it starts"));
                ValidateLinks(quest, step, label, issues);
                for (var j = 0; j < step.Objectives.Count; j++)
                {
                    var objective = step.Objectives[j];
                    var objectiveLabel = $"{label}, objective {j + 1}";
                    if (objective == null) issues.Add(Error(quest, $"{objectiveLabel} is empty"));
                    else if (objective.Tracker == null) issues.Add(Error(quest, $"{objectiveLabel} has no tracker, so it never progresses"));
                    else if (objective.Tracker.MissingSetup != null) issues.Add(Error(quest, $"{objectiveLabel}: {objective.Tracker.MissingSetup}"));
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
            if (step.After.Count == 0) issues.Add(Error(quest, $"{label} starts after linked steps but has no links"));
            var seen = new HashSet<string>();
            foreach (var id in step.After)
            {
                if (id == step.Id) issues.Add(Error(quest, $"{label} is linked to itself"));
                else if (quest.FindStep(id) == null) issues.Add(Error(quest, $"{label} is linked to a step that doesn't exist ({id})"));
                if (!seen.Add(id ?? string.Empty)) issues.Add(Error(quest, $"{label} is linked to the same step twice"));
            }
            if (step.AfterRule == StepJoin.Any && step.After.Count == 1) issues.Add(Warning(quest, $"{label} starts after any of one linked step"));
        }

        /** <summary>Steps waiting on each other, steps nothing can start, and endings nobody marked.</summary> */
        private static void ValidateGraph(QuestDefinition quest, List<QuestIssue> issues)
        {
            var record = new QuestRecord(quest);
            var labels = new Dictionary<StepRecord, string>();
            for (int i = 0, n = 0; i < quest.Steps.Count; i++)
                if (quest.Steps[i] != null) labels[record.Steps[n++]] = $"step {i + 1}";

            var waiting = new HashSet<StepRecord>();
            foreach (var cycle in Cycles(record))
            {
                waiting.UnionWith(cycle);
                issues.Add(Error(quest, cycle.Count == 1
                    ? $"{labels[cycle[0]]} waits on itself, so it never starts"
                    : $"{string.Join(", ", cycle.Select(step => labels[step]))} wait on each other, so none starts"));
            }

            var reached = new HashSet<StepRecord>();
            var open = new Stack<StepRecord>(record.Steps.Where(step => step.Follows.Count == 0 || StartsWithoutFollowed(step)));
            while (open.Count > 0)
            {
                var step = open.Pop();
                if (!reached.Add(step)) continue;
                foreach (var dependant in step.Dependants)
                    if (dependant.Definition.AfterRule == StepJoin.Any || dependant.Follows.All(reached.Contains) || StartsWithoutFollowed(dependant)) open.Push(dependant);
            }
            foreach (var step in record.Steps.Where(step => !reached.Contains(step) && !waiting.Contains(step)))
                issues.Add(Warning(quest, $"{labels[step]} can never start"));

            if (!quest.Steps.Any(step => step != null && step.EndsQuest))
            {
                var ends = record.Steps.Where(step => step.Dependants.Count == 0).ToList();
                if (ends.Count > 1) issues.Add(Warning(quest, $"{string.Join(", ", ends.Select(step => labels[step]))} each end the quest, since nothing follows them; mark the ending steps to be sure"));
            }
        }

        /** <summary>True when a step's start doesn't wait on the steps it follows: an unlock condition replaces waiting for the step above.</summary> */
        private static bool StartsWithoutFollowed(StepRecord step) => step.Definition.Start == StepStart.AfterStepAbove && step.Definition.UnlockWhen != null;

        /** <summary>The steps a step waits for: those it follows, unless its condition replaces that, and steps of this quest its condition requires.</summary> */
        private static List<StepRecord> WaitsFor(QuestRecord record, StepRecord step)
        {
            var waits = StartsWithoutFollowed(step) ? new List<StepRecord>() : new List<StepRecord>(step.Follows);
            var found = new List<FoundReference>();
            QuestReferences.Collect(step.Definition.UnlockWhen, found);
            foreach (var reference in found)
            {
                if (!reference.Use.AsItself || !reference.Use.Required || reference.Reference.Quest != record.Definition || reference.Reference.StepId == null) continue;
                var target = record.Find(reference.Reference.StepId);
                if (target != null && !waits.Contains(target)) waits.Add(target);
            }
            return waits;
        }

        /** <summary>Each set of steps that wait on each other, found by a depth-first walk over what each step waits for.</summary> */
        private static List<List<StepRecord>> Cycles(QuestRecord record)
        {
            var steps = record.Steps;
            var cycles = new List<List<StepRecord>>();
            var state = new Dictionary<StepRecord, int>();
            var path = new List<StepRecord>();
            void Visit(StepRecord step)
            {
                state[step] = 1;
                path.Add(step);
                foreach (var before in WaitsFor(record, step))
                {
                    if (!state.TryGetValue(before, out var seen)) Visit(before);
                    else if (seen == 1) cycles.Add(path.GetRange(path.IndexOf(before), path.Count - path.IndexOf(before)));
                }
                path.RemoveAt(path.Count - 1);
                state[step] = 2;
            }
            foreach (var step in steps)
                if (!state.ContainsKey(step)) Visit(step);
            return cycles;
        }

        private static QuestIssue Error(QuestDefinition quest, string problem) => new QuestIssue(QuestIssueLevel.Error, quest, problem);

        private static QuestIssue Warning(QuestDefinition quest, string problem) => new QuestIssue(QuestIssueLevel.Warning, quest, problem);

        #endregion
    }
}
