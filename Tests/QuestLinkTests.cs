using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Steps linked to other steps: start rules, ALL and ANY, links with conditions, endings, and their validation.</summary> */
    public sealed class QuestLinkTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static StepSettings After(params string[] ids) => new StepSettings { Start = StepStart.AfterLinkedSteps, After = ids };

        private static StepSettings AfterAny(params string[] ids) => new StepSettings { Start = StepStart.AfterLinkedSteps, After = ids, AfterRule = StepJoin.Any };

        private static string[] Problems(QuestDefinition quest, QuestIssueLevel level) =>
            QuestValidator.Validate(quest).Where(issue => issue.Level == level).Select(issue => issue.Problem).ToArray();

        [Test]
        public void StepsWithTheQuest_StartTogether()
        {
            var quest = _kit.Quest("Parallel", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", _kit.Signal("B"))),
                Step("c", new StepSettings { Start = StepStart.WithQuest, EndsQuest = true }, Count("z", _kit.Signal("C"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(new[] { "a", "b", "c" }.Select(id => service.StateOf(quest, id)), Is.All.EqualTo(StepState.Active));
        }

        [Test]
        public void AllLinks_WaitForEveryLinkedStep()
        {
            var a = _kit.Signal("A");
            var b = _kit.Signal("B");
            var quest = _kit.Quest("Join", new[]
            {
                Step("a", Count("x", a)),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", b)),
                Step("c", After("a", "b"), Count("z", _kit.Signal("C"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Locked));
            service.Raise(b);
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void AnyLink_StartsOnTheFirstLinkedStep_AndTheOtherBranchCarriesOn()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Merge", new[]
            {
                Step("a", new StepSettings { Start = StepStart.WithQuest }, Count("x", a)),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", _kit.Signal("B"))),
                Step("c", AfterAny("a", "b"), Count("z", _kit.Signal("C"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Active));
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void AnyLink_KeepsItsProgress_WhenAnotherLinkedStepFinishesLater()
        {
            var a = _kit.Signal("A");
            var b = _kit.Signal("B");
            var c = _kit.Signal("C");
            var quest = _kit.Quest("Merge", new[]
            {
                Step("a", new StepSettings { Start = StepStart.WithQuest }, Count("x", a)),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", b)),
                Step("c", AfterAny("a", "b"), Count("z", c, 2)),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            service.Raise(c);
            service.Raise(b);
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Active));
            Assert.That(service.ProgressOf(quest, "z"), Is.EqualTo(1), "the second linked step finishing doesn't start it over");
        }

        [Test]
        public void AnyLink_WhoseLinkedStepsFinishTogether_StartsOnce()
        {
            var both = _kit.Signal("Both");
            var quest = _kit.Quest("Merge", new[]
            {
                Step("a", new StepSettings { Start = StepStart.WithQuest }, Count("x", both)),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", both)),
                Step("c", AfterAny("a", "b"), Count("z", both, 2)),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(both);
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Active));
            Assert.That(service.ProgressOf(quest, "z"), Is.EqualTo(0), "the signal that started it isn't counted by it");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ALinkAndACondition_BothMustHold_InEitherOrder(bool linkFirst)
        {
            var a = _kit.Signal("A");
            var flag = _kit.Flag("Ready");
            var quest = _kit.Quest("Both", new[]
            {
                Step("a", Count("x", a)),
                Step("b", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "a" }, UnlockWhen = new FlagCondition(flag) }, Count("y", _kit.Signal("B"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            if (linkFirst) service.Raise(a);
            else service.SetFlag(flag, 1);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Locked));
            if (linkFirst) service.SetFlag(flag, 1);
            else service.Raise(a);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void WithTheQuestAndACondition_StartsWhenTheConditionHolds()
        {
            var flag = _kit.Flag("Ready");
            var quest = _kit.Quest("Gated", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { Start = StepStart.WithQuest, UnlockWhen = new FlagCondition(flag) }, Count("y", _kit.Signal("B"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Locked));
            service.SetFlag(flag, 1);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void EveryStepNothingFollows_EndsTheQuest_Once()
        {
            var start = _kit.Signal("Start");
            var left = _kit.Signal("Left");
            var quest = _kit.Quest("Fork", new[]
            {
                Step("start", Count("s", start)),
                Step("left", After("start"), Count("l", left)),
                Step("right", After("start"), Count("r", _kit.Signal("Right"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(start);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active));
            service.Raise(left);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(1));
        }

        [Test]
        public void AMarkedEnd_OverridesStepsNothingFollows()
        {
            var a = _kit.Signal("A");
            var b = _kit.Signal("B");
            var quest = _kit.Quest("Marked", new[]
            {
                Step("a", new StepSettings { Start = StepStart.WithQuest }, Count("x", a)),
                Step("b", new StepSettings { Start = StepStart.WithQuest, EndsQuest = true }, Count("y", b)),
            });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active));
            service.Raise(b);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void AStepWithAnUnlockCondition_StillFollowsTheStepAbove_ForEnding()
        {
            var a = _kit.Signal("A");
            var flag = _kit.Flag("Branch");
            var quest = _kit.Quest("Old", new[] { Step("a", Count("x", a)), Step("b", new StepSettings { UnlockWhen = new FlagCondition(flag) }, Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active), "as before links: only the last step ends an unmarked quest");
            service.SetFlag(flag, 1);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void LinksToMissingSteps_AreIgnored_SoTheStepStartsWithTheQuest()
        {
            var quest = _kit.Quest("Dangling", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", After("gone", "b"), Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void StepsWaitingOnEachOther_NeverStart_AndNothingHangs()
        {
            var quest = _kit.Quest("Loop", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", After("c"), Count("y", _kit.Signal("B"))),
                Step("c", After("b"), Count("z", _kit.Signal("C"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Locked));
            Assert.That(service.StateOf(quest, "c"), Is.EqualTo(StepState.Locked));
        }

        [Test]
        public void ARewiredQuest_CatchesUpWhenASaveLoads()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Rewired", new[] { Step("a", Count("x", a)), Step("b", new StepSettings { Start = StepStart.WithQuest, UnlockWhen = new FlagCondition(_kit.Flag("Never")), EndsQuest = true }, Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active));
            var saved = service.Capture();

            quest.Configure(new[] { Step("a", Count("x", a)), Step("b", After("a"), Count("y", _kit.Signal("B"))) });
            _kit.Start(newGame: false).Restore(saved);
            Assert.That(_kit.Service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void TheValidator_ReportsBrokenLinks()
        {
            var quest = _kit.Quest("Links", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", After("a", "a", "b", "gone"), Count("y", _kit.Signal("B"))),
                Step("c", After(), Count("z", _kit.Signal("C"))),
                Step("d", AfterAny("a"), Count("w", _kit.Signal("D"))),
            }, new QuestSettings { Title = "Links" });
            var errors = Problems(quest, QuestIssueLevel.Error);
            Assert.That(errors, Has.Some.Contains("step 2 is linked to itself"));
            Assert.That(errors, Has.Some.Contains("step 2 is linked to the same step twice"));
            Assert.That(errors, Has.Some.Contains("doesn't exist (gone)"));
            Assert.That(errors, Has.Some.Contains("step 3 starts after linked steps but has no links"));
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Has.Some.Contains("step 4 starts after any of one linked step"));
        }

        [Test]
        public void TheValidator_ReportsSteps_ThatWaitOnEachOther_OrCanNeverStart()
        {
            var quest = _kit.Quest("Loop", new[]
            {
                Step("a", new StepSettings { EndsQuest = true }, Count("x", _kit.Signal("A"))),
                Step("b", After("c"), Count("y", _kit.Signal("B"))),
                Step("c", After("b"), Count("z", _kit.Signal("C"))),
                Step("d", After("c"), Count("w", _kit.Signal("D"))),
            }, new QuestSettings { Title = "Loop" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.Some.Contains("wait on each other"));
            var warnings = Problems(quest, QuestIssueLevel.Warning);
            Assert.That(warnings, Has.Some.Contains("step 4 can never start"));
            Assert.That(warnings, Has.None.Contains("step 2 can never start"), "a step in a loop is reported once, as the loop");
        }

        [Test]
        public void TheValidator_WarnsWhenSeveralUnmarkedStepsEndTheQuest_ButNotWhenOneIsMarked()
        {
            var fork = new[] { Step("start", Count("s", _kit.Signal("S"))), Step("left", After("start"), Count("l", _kit.Signal("L"))), Step("right", After("start"), Count("r", _kit.Signal("R"))) };
            var quest = _kit.Quest("Fork", fork, new QuestSettings { Title = "Fork" });
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Has.Some.Contains("step 2, step 3 each end the quest"));

            quest.Configure(new[] { Step("start", Count("s", _kit.Signal("S"))), Step("left", After("start"), Count("l", _kit.Signal("L"))), Step("right", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "start" }, EndsQuest = true }, Count("r", _kit.Signal("R"))) },
                new QuestSettings { Title = "Fork" });
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Has.None.Contains("each end the quest"));
        }

        [Test]
        public void TheValidator_SaysWhichStepEachProblemIsOn()
        {
            var quest = _kit.Quest("Links", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", After("gone"), Count("y", _kit.Signal("B"))) });
            var issues = QuestValidator.Validate(quest);
            Assert.That(issues.Single(issue => issue.Problem.Contains("doesn't exist")).StepId, Is.EqualTo("b"));
            Assert.That(issues.Single(issue => issue.Problem == "has no kind").StepId, Is.Null);
        }

        [Test]
        public void AnAnyLinkWithAStartableBranch_IsNotALoop()
        {
            var quest = _kit.Quest("Retry", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", AfterAny("a", "c"), Count("y", _kit.Signal("B"))),
                Step("c", After("b"), Count("z", _kit.Signal("C"))),
            }, new QuestSettings { Title = "Retry" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Is.Empty);
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Has.None.Contains("never start"));
        }

        [Test]
        public void ALinkRule_OnAStepThatFollowsTheStepAbove_IsIgnored()
        {
            var quest = _kit.Quest("Plain", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { AfterRule = StepJoin.Any }, Count("y", _kit.Signal("B"))),
            }, new QuestSettings { Title = "Plain" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Is.Empty);
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Is.EqualTo(new[] { "has no kind" }));
        }

        [Test]
        public void ConditionsReadingALockedOrNegatedStep_AreNotLoops_ButADoneStepIs()
        {
            var quest = _kit.Quest("Reads", new[] { Step("tmp") });
            quest.Configure(new[]
            {
                Step("a", new StepSettings { UnlockWhen = new StepStateCondition(quest, "b", StepState.Locked) }, Count("w", _kit.Signal("A"))),
                Step("b", After("a"), Count("x", _kit.Signal("B"))),
                Step("c", new StepSettings { Start = StepStart.WithQuest, UnlockWhen = new NotCondition<IFacts> { child = new StepStateCondition(quest, "d") } }, Count("y", _kit.Signal("C"))),
                Step("d", After("c"), Count("z", _kit.Signal("D"))),
            }, new QuestSettings { Title = "Reads" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.None.Contains("wait on each other"));

            quest.Configure(new[]
            {
                Step("e", new StepSettings { Start = StepStart.WithQuest, UnlockWhen = new StepStateCondition(quest, "f") }, Count("y", _kit.Signal("E"))),
                Step("f", After("e"), Count("z", _kit.Signal("F"))),
            }, new QuestSettings { Title = "Reads" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.Some.Contains("wait on each other"));
        }

        [Test]
        public void AWellLinkedQuest_HasNoLinkProblems()
        {
            var quest = _kit.Quest("Clean", new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", new StepSettings { Start = StepStart.WithQuest }, Count("y", _kit.Signal("B"))),
                Step("c", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "a", "b" }, EndsQuest = true }, Count("z", _kit.Signal("C"))),
            }, new QuestSettings { Title = "Clean" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Is.Empty);
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Is.EqualTo(new[] { "has no kind" }));
        }
    }
}
