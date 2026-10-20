using System.Collections.Generic;
using Extensions.Logic;

namespace QuestSystem.EditorTools
{
    /** <summary>What the quest graph needs to know about one quest: the quests and flags it reads, and its problems.</summary> */
    public sealed class QuestSummary
    {
        #region Properties

        /** <summary>The quest.</summary> */
        public QuestDefinition Quest { get; }

        /** <summary>Every quest it reads, itself included, with where and how.</summary> */
        public IReadOnlyList<QuestLink> Reads { get; }

        /** <summary>Every story flag its conditions and trackers read.</summary> */
        public IReadOnlyList<StoryFlag> Flags { get; }

        /** <summary>How many condition parts can't say what they read, so some links may be missing.</summary> */
        public int Unreadable { get; }

        /** <summary>Its validation problems.</summary> */
        public IReadOnlyList<QuestIssue> Issues { get; }

        /** <summary>How many of its problems are errors.</summary> */
        public int Errors { get; }

        /** <summary>How many of its problems are warnings.</summary> */
        public int Warnings { get; }

        #endregion

        #region Public API

        /** <summary>Reads a quest.</summary> */
        public static QuestSummary Of(QuestDefinition quest) => new QuestSummary(quest);

        #endregion

        #region Helpers

        private QuestSummary(QuestDefinition quest)
        {
            Quest = quest;
            var links = new List<QuestLink>();
            var flags = new List<StoryFlag>();
            var unreadable = 0;
            var found = new List<FoundReference>();
            var subjects = new HashSet<IConditionSubject>();
            var held = new List<ICondition<IFacts>>();

            void Scan(ICondition<IFacts> condition, LinkSlot slot, string stepId)
            {
                if (condition == null) return;
                found.Clear();
                unreadable += QuestReferences.Collect(condition, found);
                var named = new HashSet<QuestDefinition>();
                foreach (var reference in found)
                {
                    links.Add(new QuestLink(quest, stepId, slot, reference.Reference.Quest, reference.Reference.StepId, reference.Reference.StateName, reference.Use));
                    named.Add(reference.Reference.Quest);
                }
                subjects.Clear();
                ConditionSubjects.TryCollect(condition, subjects);
                foreach (var subject in subjects)
                {
                    var flag = QuestReferences.FlagOf(subject);
                    if (flag != null && !flags.Contains(flag)) flags.Add(flag);
                    var read = QuestReferences.QuestOf(subject);
                    if (read != null && !named.Contains(read)) links.Add(new QuestLink(quest, stepId, slot, read, null, "read", new LeafUse(true, false)));
                }
            }

            Scan(quest.AvailableWhen, LinkSlot.Available, null);
            Scan(quest.FailWhen, LinkSlot.Fails, null);
            foreach (var step in quest.Steps)
            {
                if (step == null) continue;
                Scan(step.UnlockWhen, LinkSlot.StepUnlock, step.Id);
                Scan(step.FailWhen, LinkSlot.StepFails, step.Id);
                foreach (var objective in step.Objectives)
                {
                    if (objective?.Tracker == null) continue;
                    held.Clear();
                    objective.Tracker.CollectConditions(held);
                    foreach (var condition in held)
                        Scan(condition, LinkSlot.Objective, step.Id);
                    subjects.Clear();
                    objective.Tracker.TryCollectSubjects(subjects);
                    foreach (var subject in subjects)
                    {
                        var flag = QuestReferences.FlagOf(subject);
                        if (flag != null && !flags.Contains(flag)) flags.Add(flag);
                    }
                }
            }

            Reads = links;
            Flags = flags;
            Unreadable = unreadable;
            var issues = QuestValidator.Validate(quest);
            Issues = issues;
            foreach (var issue in issues)
                if (issue.Level == QuestIssueLevel.Error) Errors++;
                else Warnings++;
        }

        #endregion
    }
}
