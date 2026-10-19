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
                if (step.UnlockWhen is StepStateCondition waits && waits.Quest == quest && waits.StepId == step.Id)
                    issues.Add(Error(quest, $"{label} unlocks when it is itself done, so it never starts"));
                for (var j = 0; j < step.Objectives.Count; j++)
                {
                    var objective = step.Objectives[j];
                    var objectiveLabel = $"{label}, objective {j + 1}";
                    if (objective == null) issues.Add(Error(quest, $"{objectiveLabel} is empty"));
                    else if (objective.Tracker == null) issues.Add(Error(quest, $"{objectiveLabel} has no tracker, so it never progresses"));
                    else if (objective.Tracker.MissingSetup != null) issues.Add(Error(quest, $"{objectiveLabel}: {objective.Tracker.MissingSetup}"));
                }
            }
            return issues;
        }

        /** <summary>Every problem with every quest given.</summary> */
        public static List<QuestIssue> ValidateAll(IEnumerable<QuestDefinition> quests) => quests.SelectMany(Validate).ToList();

        #endregion

        #region Helpers

        private static QuestIssue Error(QuestDefinition quest, string problem) => new QuestIssue(QuestIssueLevel.Error, quest, problem);

        private static QuestIssue Warning(QuestDefinition quest, string problem) => new QuestIssue(QuestIssueLevel.Warning, quest, problem);

        #endregion
    }
}
