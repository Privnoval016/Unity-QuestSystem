using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>A pill on a step node.</summary> */
    public enum StepTag
    {
        /** <summary>Starts after any one of its links.</summary> */
        AfterAny,
        /** <summary>Any one objective completes it.</summary> */
        AnyOne,
        /** <summary>Marked to end the quest.</summary> */
        Ends,
        /** <summary>Ends the quest because nothing follows it.</summary> */
        EndsByDefault,
        /** <summary>Shown to players before it starts.</summary> */
        ShownEarly,
        /** <summary>Starts with the quest.</summary> */
        WithQuest,
    }

    /** <summary>How a step pill reads: its text, its look, and what it means.</summary> */
    public readonly struct StepTagLook
    {
        /** <summary>The pill's text.</summary> */
        public string Text { get; }

        /** <summary>The pill's style class.</summary> */
        public string ClassName { get; }

        /** <summary>What the pill means, for its tooltip.</summary> */
        public string Meaning { get; }

        /** <summary>Makes a look.</summary> */
        public StepTagLook(string text, string className, string meaning)
        {
            Text = text;
            ClassName = className;
            Meaning = meaning;
        }

        /** <summary>How a tag reads on a step with this many objectives.</summary> */
        public static StepTagLook Of(StepTag tag, int objectives) => tag switch
        {
            StepTag.AfterAny => new StepTagLook("After any", "quest-graph__pill--any", "Starts once any one linked step is done"),
            StepTag.AnyOne => new StepTagLook($"Any 1 of {objectives}", "quest-graph__pill--any", "Any one objective completes it"),
            StepTag.Ends => new StepTagLook("Ends", "quest-graph__pill--end", "Completing it completes the quest"),
            StepTag.EndsByDefault => new StepTagLook("Ends", "quest-graph__pill--end-implicit", "Nothing follows it, so completing it completes the quest"),
            StepTag.ShownEarly => new StepTagLook("Shown early", "quest-graph__pill--plain", "Players see it before it starts"),
            _ => new StepTagLook("With quest", "quest-graph__pill--plain", "Starts as soon as the quest is given"),
        };
    }

    /** <summary>One objective as a node shows it.</summary> */
    public readonly struct ObjectiveLine
    {
        /** <summary>Its text, or its id without one.</summary> */
        public string Text { get; }

        /** <summary>What counts, with the target: "Wolf Defeated × 3".</summary> */
        public string Tracker { get; }

        /** <summary>True for an optional objective.</summary> */
        public bool Optional { get; }

        /** <summary>True when it can never progress.</summary> */
        public bool Broken { get; }

        /** <summary>Makes a line.</summary> */
        public ObjectiveLine(string text, string tracker, bool optional, bool broken)
        {
            Text = text;
            Tracker = tracker;
            Optional = optional;
            Broken = broken;
        }
    }

    /** <summary>A rule as a node shows it: "And if", "Unlock if", "Fails quest", with its condition in brief.</summary> */
    public readonly struct RuleLine
    {
        /** <summary>The rule.</summary> */
        public string Label { get; }

        /** <summary>The condition in brief.</summary> */
        public string Text { get; }

        /** <summary>Makes a line.</summary> */
        public RuleLine(string label, string text)
        {
            Label = label;
            Text = text;
        }
    }

    /** <summary>Everything a step node shows, worked out from the quest without any UI, so it can be tested.</summary> */
    public sealed class StepCard
    {
        #region Properties

        /** <summary>The step's id.</summary> */
        public string Id { get; }

        /** <summary>Its title: its text, or its id without one.</summary> */
        public string Title { get; }

        /** <summary>Its pills, in the order shown.</summary> */
        public IReadOnlyList<StepTag> Tags { get; }

        /** <summary>Its objectives.</summary> */
        public IReadOnlyList<ObjectiveLine> Objectives { get; }

        /** <summary>Its start and fail rules.</summary> */
        public IReadOnlyList<RuleLine> Rules { get; }

        /** <summary>Its problems, worst first.</summary> */
        public IReadOnlyList<QuestIssue> Problems { get; }

        /** <summary>True when any problem is an error.</summary> */
        public bool HasError => Problems.Any(issue => issue.Level == QuestIssueLevel.Error);

        #endregion

        #region Public API

        /** <summary>The cards for every step of a quest, in list order.</summary> */
        public static List<StepCard> Of(QuestDefinition quest, IReadOnlyList<QuestIssue> issues)
        {
            var record = new QuestRecord(quest);
            var marked = quest.Steps.Any(step => step != null && step.EndsQuest);
            return record.Steps.Select(step => new StepCard(step, marked, issues)).ToList();
        }

        /** <summary>The label of a step's fail rule.</summary> */
        public const string FailsLabel = "Fails quest if";

        /** <summary>An objective's tracker in brief, with its target when more than one.</summary> */
        public static string TrackerText(QuestObjective objective)
        {
            if (objective.Tracker == null) return "no tracker";
            var text = objective.Tracker.ToString();
            return objective.TargetCount > 1 ? $"{text} × {objective.TargetCount}" : text;
        }

        #endregion

        #region Helpers

        private StepCard(StepRecord record, bool marked, IReadOnlyList<QuestIssue> issues)
        {
            var step = record.Definition;
            Id = step.Id;
            Title = string.IsNullOrWhiteSpace(step.Text) ? step.Id : step.Text;

            var tags = new List<StepTag>();
            if (step.Start == StepStart.WithQuest) tags.Add(StepTag.WithQuest);
            if (step.Start == StepStart.AfterLinkedSteps && step.AfterRule == StepJoin.Any) tags.Add(StepTag.AfterAny);
            if (step.Completion == StepCompletion.AnyOne) tags.Add(StepTag.AnyOne);
            if (step.EndsQuest) tags.Add(StepTag.Ends);
            else if (!marked && record.Dependants.Count == 0) tags.Add(StepTag.EndsByDefault);
            if (!step.HiddenUntilReached) tags.Add(StepTag.ShownEarly);
            Tags = tags;

            Objectives = step.Objectives.Where(objective => objective != null)
                .Select(objective => new ObjectiveLine(string.IsNullOrWhiteSpace(objective.Text) ? objective.Id : objective.Text, TrackerText(objective), objective.Optional,
                    objective.Tracker == null || objective.Tracker.MissingSetup != null))
                .ToList();

            var rules = new List<RuleLine>();
            if (step.UnlockWhen != null) rules.Add(new RuleLine(step.Start == StepStart.AfterStepAbove ? "Unlock if" : "And if", ConditionTree.Summarize(step.UnlockWhen)));
            if (step.FailWhen != null) rules.Add(new RuleLine(FailsLabel, ConditionTree.Summarize(step.FailWhen)));
            Rules = rules;

            Problems = issues.Where(issue => issue.StepId == step.Id).OrderByDescending(issue => issue.Level).ToList();
        }

        #endregion
    }
}
