using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Finding the quests and steps conditions read, summaries for tools, and loops through step conditions.</summary> */
    public sealed class QuestReferenceTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        [Serializable]
        private sealed class GameQuestCondition : ICondition<IFacts>, IQuestReference
        {
            public QuestDefinition Target;

            public QuestDefinition Quest => Target;

            public string StepId => null;

            public string StateName => "talked about";

            public bool Evaluate(IFacts context) => false;
        }

        [Test]
        public void References_AreFoundAnywhereInATree_WithHowTheyAreUsed()
        {
            var a = _kit.Quest("A", new QuestStep[0]);
            var b = _kit.Quest("B", new[] { Step("s") });
            var tree = new AndCondition<IFacts>
            {
                children =
                {
                    new QuestStateCondition(a),
                    new NotCondition<IFacts> { child = new StepStateCondition(b, "s") },
                    new OrCondition<IFacts> { children = { new GameQuestCondition { Target = b }, new FlagCondition(_kit.Flag("F")) } },
                    new QuestStateCondition(null),
                },
            };
            var found = new List<FoundReference>();
            Assert.That(QuestReferences.Collect(tree, found), Is.Zero);
            Assert.That(found.Select(f => f.Reference.Quest), Is.EqualTo(new[] { a, b, b }), "a reference with no quest is skipped");
            Assert.That(found[0].Use.AsItself && found[0].Use.Required);
            Assert.That(!found[1].Use.AsItself && found[1].Reference.StepId == "s");
            Assert.That(!found[2].Use.Required && found[2].Reference.StateName == "talked about");
        }

        [Test]
        public void PartsThatCantSayWhatTheyRead_AreCounted()
        {
            var tree = new OrCondition<IFacts> { children = { new LeafCondition<IFacts>(_ => true), new FlagCondition(_kit.Flag("F")), null } };
            Assert.That(QuestReferences.Collect(tree, new List<FoundReference>()), Is.EqualTo(1));
            Assert.That(QuestReferences.Collect(null, new List<FoundReference>()), Is.Zero);
        }

        [Test]
        public void ConditionsAndTrackers_SummarizeThemselves()
        {
            var flag = _kit.Flag("Elders");
            var quest = _kit.Quest("Vote", new[] { Step("rebels", new StepSettings { Text = "Side with the rebels" }) }, new QuestSettings { Title = "Guild Vote" });
            Assert.That(new FlagCondition(flag).ToString(), Is.EqualTo("Elders"));
            Assert.That(new FlagCondition(flag, 3).ToString(), Is.EqualTo("Elders ≥ 3"));
            Assert.That(new FlagCondition(flag, 0, NumberComparison.Equal).ToString(), Is.EqualTo("Elders = 0"));
            Assert.That(new FlagCondition(flag, 2, NumberComparison.Below).ToString(), Is.EqualTo("Elders < 2"));
            Assert.That(new QuestStateCondition(quest).ToString(), Is.EqualTo("Guild Vote done"));
            Assert.That(new QuestStateCondition(quest, QuestState.Done, 3).ToString(), Is.EqualTo("Guild Vote done 3 times"));
            Assert.That(new QuestStateCondition(quest, QuestState.Failed).ToString(), Is.EqualTo("Guild Vote failed"));
            Assert.That(new StepStateCondition(quest, "rebels").ToString(), Is.EqualTo("Guild Vote: Side with the rebels done"));
            Assert.That(new StepStateCondition(quest, "gone", StepState.Active).ToString(), Is.EqualTo("Guild Vote: missing step gone active"));
            Assert.That(new FlagCondition().ToString(), Is.EqualTo("no flag"));

            var signal = _kit.Signal("Wolf Defeated");
            Assert.That(new SignalTracker(signal).ToString(), Is.EqualTo("Wolf Defeated"));
            Assert.That(new SignalTracker(signal, new FlagCondition(flag)).ToString(), Is.EqualTo("Wolf Defeated if Elders"));
            Assert.That(new FlagTracker(flag).ToString(), Is.EqualTo("flag Elders"));
            Assert.That(new ConditionTracker(new QuestStateCondition(quest)).ToString(), Is.EqualTo("while Guild Vote done"));
            Assert.That(new ConditionTracker(null).ToString(), Is.EqualTo("while none"));
        }

        [Test]
        public void Trackers_HandOverTheConditionsTheyHold()
        {
            var condition = new FlagCondition(_kit.Flag("F"));
            var held = new List<ICondition<IFacts>>();
            new ConditionTracker(condition).CollectConditions(held);
            new SignalTracker(_kit.Signal("S"), condition).CollectConditions(held);
            new SignalTracker(_kit.Signal("S")).CollectConditions(held);
            Assert.That(Trackers.ConditionHolderOf(new FlagTracker(_kit.Flag("G"))), Is.Null, "a tracker that holds no conditions isn't a holder");
            Assert.That(held, Is.EqualTo(new[] { condition, condition }));
        }

        [Test]
        public void TheValidator_FindsLoopsThroughStepConditions_ButNotThroughAlternatives()
        {
            var quest = _kit.Quest("Loop", new[] { Step("a", Count("x", _kit.Signal("A"))) });
            quest.Configure(new[]
            {
                Step("a", new StepSettings { UnlockWhen = new StepStateCondition(quest, "b") }, Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "a" }, EndsQuest = true }, Count("y", _kit.Signal("B"))),
            }, new QuestSettings { Title = "Loop" });
            Assert.That(QuestValidator.Validate(quest).Select(i => i.Problem), Has.Some.Contains("wait on each other"));

            quest.Configure(new[]
            {
                Step("a", new StepSettings { UnlockWhen = new OrCondition<IFacts> { children = { new StepStateCondition(quest, "b"), new FlagCondition(_kit.Flag("Skip")) } } }, Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "a" }, EndsQuest = true }, Count("y", _kit.Signal("B"))),
            }, new QuestSettings { Title = "Loop" });
            Assert.That(QuestValidator.Validate(quest).Where(i => i.Level == QuestIssueLevel.Error), Is.Empty);
        }
    }
}
