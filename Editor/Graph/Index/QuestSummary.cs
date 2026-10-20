using System.Collections.Generic;
using System.Linq;
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

        /** <summary>Reads a quest: every condition and tracker, then its problems.</summary> */
        private QuestSummary(QuestDefinition quest)
        {
            Quest = quest;
            var scan = new Scan(quest);
            scan.Condition(quest.AvailableWhen, LinkSlot.Available, null);
            scan.Condition(quest.FailWhen, LinkSlot.Fails, null);
            foreach (var step in quest.Steps)
            {
                if (step == null) continue;
                scan.Condition(step.UnlockWhen, LinkSlot.StepUnlock, step.Id);
                scan.Condition(step.FailWhen, LinkSlot.StepFails, step.Id);
                foreach (var objective in step.Objectives)
                    if (objective?.Tracker != null) scan.Tracker(objective.Tracker, step.Id);
            }
            Reads = scan.Links;
            Flags = scan.Flags.ToList();
            Unreadable = scan.Unreadable;
            var issues = QuestValidator.Validate(quest);
            Issues = issues;
            Errors = issues.Count(issue => issue.Level == QuestIssueLevel.Error);
            Warnings = issues.Count - Errors;
        }

        #endregion

        #region Helpers

        /** <summary>Collects a quest's links, flags and unreadable conditions, one condition or tracker at a time.</summary> */
        private sealed class Scan
        {
            private readonly QuestDefinition _quest;
            private readonly List<FoundReference> _found = new List<FoundReference>();
            private readonly HashSet<IConditionSubject> _subjects = new HashSet<IConditionSubject>();
            private readonly List<ICondition<IFacts>> _held = new List<ICondition<IFacts>>();

            public List<QuestLink> Links { get; } = new List<QuestLink>();
            public HashSet<StoryFlag> Flags { get; } = new HashSet<StoryFlag>();
            public int Unreadable { get; private set; }

            public Scan(QuestDefinition quest) => _quest = quest;

            /** <summary>Reads one condition: the quests it names with how it uses them, quests it reads without saying how, and its flags.</summary> */
            public void Condition(ICondition<IFacts> condition, LinkSlot slot, string stepId)
            {
                if (condition == null) return;
                _found.Clear();
                Unreadable += QuestReferences.Collect(condition, _found);
                var named = new HashSet<QuestDefinition>();
                foreach (var reference in _found)
                {
                    Links.Add(new QuestLink(_quest, stepId, slot, reference.Reference.Quest, reference.Reference.StepId, reference.Reference.StateName, reference.Use));
                    named.Add(reference.Reference.Quest);
                }
                _subjects.Clear();
                ConditionSubjects.TryCollect(condition, _subjects);
                AddFlags();
                foreach (var subject in _subjects)
                {
                    var read = QuestReferences.QuestOf(subject);
                    if (read != null && !named.Contains(read)) Links.Add(new QuestLink(_quest, stepId, slot, read, null, QuestLink.UnknownState, new LeafUse(true, false)));
                }
            }

            /** <summary>Reads one tracker: the conditions it holds, and the flags it reads.</summary> */
            public void Tracker(ObjectiveTracker tracker, string stepId)
            {
                _held.Clear();
                Trackers.ConditionHolderOf(tracker)?.CollectConditions(_held);
                foreach (var condition in _held.ToList())
                    Condition(condition, LinkSlot.Objective, stepId);
                _subjects.Clear();
                tracker.TryCollectSubjects(_subjects);
                AddFlags();
            }

            private void AddFlags()
            {
                foreach (var subject in _subjects)
                {
                    var flag = QuestReferences.FlagOf(subject);
                    if (flag != null) Flags.Add(flag);
                }
            }
        }

        #endregion
    }
}
