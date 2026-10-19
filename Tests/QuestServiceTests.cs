using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Extensions.Logic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Giving quests, steps unlocking and completing, objectives counting, failure, repeats, rewards and events.</summary> */
    public sealed class QuestServiceTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        [Test]
        public void Steps_RunInOrder_AndTheLastCompletesTheQuest()
        {
            var wolf = _kit.Signal("Wolf");
            var quest = _kit.Quest("Hunt", new[] { Step("a", Count("wolves", wolf, 2)), Step("b", Count("more", wolf)) });
            var service = _kit.Start();

            Assert.That(service.Give(quest));
            Assert.That(service.StateOf(quest, "a"), Is.EqualTo(StepState.Active));
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Locked));
            service.Raise(wolf);
            service.Raise(wolf);
            Assert.That(service.StateOf(quest, "a"), Is.EqualTo(StepState.Done));
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
            Assert.That(service.ProgressOf(quest, "more"), Is.Zero);
            service.Raise(wolf);

            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
            Assert.That(_kit.Events, Is.EqualTo(new[]
            {
                "given Hunt", "step a Active", "progress wolves 1", "progress wolves 2", "step a Done", "step b Active", "progress more 1",
                "step b Done", "Done Hunt x1",
            }));
        }

        [Test]
        public void Signals_BeforeAStepStarts_DontCount_AndCountsAreCapped()
        {
            var wolf = _kit.Signal("Wolf");
            var quest = _kit.Quest("Hunt", new[] { Step("a", Count("one", wolf)), Step("b", Count("three", wolf, 3)) });
            var service = _kit.Start();
            service.Raise(wolf, 5);
            Assert.That(service.ProgressOf(quest, "one"), Is.Zero);

            service.Give(quest);
            service.Raise(wolf, 50);
            Assert.That(service.ProgressOf(quest, "one"), Is.EqualTo(1));
            Assert.That(service.ProgressOf(quest, "three"), Is.Zero);
            service.Raise(wolf, int.MaxValue);
            Assert.That(service.ProgressOf(quest, "three"), Is.EqualTo(3));
        }

        [Test]
        public void ZeroOrNegativeAmounts_AndOtherSignals_DontCount()
        {
            var wolf = _kit.Signal("Wolf");
            var quest = _kit.Quest("Hunt", new[] { Step("a", Count("wolves", wolf, 2)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(wolf, 0);
            service.Raise(wolf, -3);
            service.Raise(_kit.Signal("Bear"));
            service.Raise(null);
            Assert.That(service.ProgressOf(quest, "wolves"), Is.Zero);
        }

        [Test]
        public void AnyOne_CompletesOnTheFirstObjective()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Choice", new[] { Step("s", new StepSettings { Completion = StepCompletion.AnyOne }, Count("x", a), Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void OptionalObjectives_DontHoldAStepBack()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Bonus", new[] { Step("s", Count("main", a), Count("bonus", _kit.Signal("B"), optional: true)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void StepsWithNothingRequired_AndQuestsWithNoSteps_CompleteAtOnce()
        {
            var onlyBonus = _kit.Quest("OnlyBonus", new[] { Step("s", Count("bonus", _kit.Signal("B"), optional: true)) });
            var empty = _kit.Quest("Empty", new QuestStep[0]);
            var service = _kit.Start();
            service.Give(onlyBonus);
            service.Give(empty);
            Assert.That(service.StateOf(onlyBonus), Is.EqualTo(QuestState.Done));
            Assert.That(service.StateOf(empty), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void FlagTrackers_FollowTheFlag_UpAndDownWhileTheStepRuns()
        {
            var herbs = _kit.Flag("Herbs");
            var quest = _kit.Quest("Gather", new[] { Step("s", new QuestObjective("herbs", new FlagTracker(herbs), 3)) });
            var service = _kit.Start();
            service.SetFlag(herbs, 1);
            service.Give(quest);
            Assert.That(service.ProgressOf(quest, "herbs"), Is.EqualTo(1));
            service.AddToFlag(herbs, 1);
            Assert.That(service.ProgressOf(quest, "herbs"), Is.EqualTo(2));
            service.SetFlag(herbs, 0);
            Assert.That(service.ProgressOf(quest, "herbs"), Is.Zero);
            service.SetFlag(herbs, 9);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void AQuestWaitingOnAnotherQuest_UpdatesWhenThatQuestCompletes()
        {
            var a = _kit.Signal("A");
            var first = _kit.Quest("First", new[] { Step("s", Count("x", a)) });
            var second = _kit.Quest("Second", new[] { Step("s", new QuestObjective("after", new ConditionTracker(new QuestStateCondition(first)))) });
            var service = _kit.Start();
            service.Give(first);
            service.Give(second);
            Assert.That(service.StateOf(second), Is.EqualTo(QuestState.Active));
            service.Raise(a);
            Assert.That(service.StateOf(second), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void ABranch_UnlocksByCondition_AndAStepMarkedToEndsTheQuest()
        {
            var sided = _kit.Flag("SidedWithGuild");
            var talk = _kit.Signal("Talk");
            var guildWork = _kit.Signal("GuildWork");
            var quest = _kit.Quest("Fork", new[]
            {
                Step("start", Count("talk", talk)),
                Step("guild", new StepSettings { UnlockWhen = new FlagCondition(sided), EndsQuest = true }, Count("g", guildWork)),
                Step("rebels", new StepSettings { UnlockWhen = new FlagCondition(sided, 0, NumberComparison.Equal), EndsQuest = true }, Count("r", _kit.Signal("R"))),
            });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(service.StateOf(quest, "rebels"), Is.EqualTo(StepState.Active));
            Assert.That(service.StateOf(quest, "guild"), Is.EqualTo(StepState.Locked));
            service.Raise(talk);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active), "start isn't marked to end the quest");
            service.SetFlag(sided, 1);
            Assert.That(service.StateOf(quest, "guild"), Is.EqualTo(StepState.Active));
            service.Raise(guildWork);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(1));
        }

        [Test]
        public void TwoEndingBranches_DoneByOneSignal_CompleteTheQuestOnce()
        {
            var sided = _kit.Flag("Sided");
            var shared = _kit.Signal("Shared");
            var log = new List<string>();
            var quest = _kit.Quest("Fork", new[]
            {
                Step("guild", new StepSettings { UnlockWhen = new FlagCondition(sided), EndsQuest = true }, Count("g", shared)),
                Step("rebels", new StepSettings { UnlockWhen = new FlagCondition(sided, 0, NumberComparison.Equal), EndsQuest = true }, Count("r", shared)),
            }, new QuestSettings { Rewards = new IQuestReward[] { new RecordingReward { Name = "gold", Log = log } } });
            var service = _kit.Start();
            service.Give(quest);
            service.SetFlag(sided, 1);
            Assert.That(service.StateOf(quest, "guild"), Is.EqualTo(StepState.Active));
            Assert.That(service.StateOf(quest, "rebels"), Is.EqualTo(StepState.Active));

            service.Raise(shared);
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(1));
            Assert.That(log, Is.EqualTo(new[] { "gold" }));
            Assert.That(_kit.Events.Count(e => e.StartsWith("Done Fork")), Is.EqualTo(1));
        }

        [Test]
        public void AllRequired_WaitsForEveryObjective()
        {
            var a = _kit.Signal("A");
            var b = _kit.Signal("B");
            var quest = _kit.Quest("Both", new[] { Step("s", Count("x", a), Count("y", b, 2)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            service.Raise(b);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active));
            service.Raise(b);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void ASignalThatEndsOneQuest_AndWouldFailAnother_EndsEachOnce()
        {
            var a = _kit.Signal("A");
            var first = _kit.Quest("First", new[] { Step("s", Count("x", a)) });
            var second = _kit.Quest("Second", new[] { Step("s", Count("y", a)) }, new QuestSettings { FailWhen = new QuestStateCondition(first) });
            var service = _kit.Start();
            service.Give(first);
            service.Give(second);
            service.Raise(a);

            Assert.That(service.StateOf(first), Is.EqualTo(QuestState.Done));
            Assert.That(service.StateOf(second), Is.EqualTo(QuestState.Done), "the signal's own progress lands before what it causes");
            Assert.That(_kit.Events.Where(e => e.Contains(" Second x")), Is.EqualTo(new[] { "Done Second x1" }));
        }

        [Test]
        public void SignalPayloads_FilterWhatCounts()
        {
            var defeated = _kit.Signal("Defeated");
            var quest = _kit.Quest("Wolves", new[] { Step("s", CountWhen("wolves", defeated, new FoeIs { Kind = "Wolf" }, 3)) });
            var service = _kit.Start();
            service.Give(quest);

            service.Raise(defeated, 1, Facts.Of<IDefeatedFoe>(new DefeatedFoe("Bear")));
            service.Raise(defeated);
            Assert.That(service.ProgressOf(quest, "wolves"), Is.Zero);
            service.Raise(defeated, 2, Facts.Of<IDefeatedFoe>(new DefeatedFoe("Wolf")));
            Assert.That(service.ProgressOf(quest, "wolves"), Is.EqualTo(2));
        }

        [Test]
        public void SignalConditions_ReadTheWorldsFactsUnderThePayload()
        {
            var night = _kit.Flag("Night");
            var defeated = _kit.Signal("Defeated");
            var onlyWhen = new AndCondition<IFacts> { children = { new FoeIs { Kind = "Wolf" }, new FlagCondition(night) } };
            var quest = _kit.Quest("NightHunt", new[] { Step("s", CountWhen("wolves", defeated, onlyWhen, 2)) });
            var service = _kit.Start();
            service.Give(quest);
            var wolf = Facts.Of<IDefeatedFoe>(new DefeatedFoe("Wolf"));

            service.Raise(defeated, 1, wolf);
            Assert.That(service.ProgressOf(quest, "wolves"), Is.Zero);
            service.SetFlag(night, 1);
            service.Raise(defeated, 1, wolf);
            Assert.That(service.ProgressOf(quest, "wolves"), Is.EqualTo(1));
        }

        [Test]
        public void AQuestFailCondition_FailsIt_AndItStopsCounting()
        {
            var late = _kit.Flag("TooLate");
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Timed", new[] { Step("s", Count("x", a, 2)) }, new QuestSettings { FailWhen = new FlagCondition(late) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            service.SetFlag(late, 1);
            service.Raise(a);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Failed));
            Assert.That(service.ProgressOf(quest, "x"), Is.EqualTo(1));
            Assert.That(service.Give(quest), Is.False);
        }

        [Test]
        public void AStepFailCondition_FailsTheStepAndTheQuest()
        {
            var lost = _kit.Flag("Lost");
            var quest = _kit.Quest("Escort", new[] { Step("s", new StepSettings { FailWhen = new FlagCondition(lost) }, Count("x", _kit.Signal("A"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.SetFlag(lost, 1);
            Assert.That(service.StateOf(quest, "s"), Is.EqualTo(StepState.Failed));
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Failed));
        }

        [Test]
        public void DoneAndFailedAtOnce_CountsAsDone()
        {
            var flag = _kit.Flag("Both");
            var quest = _kit.Quest("Race", new[] { Step("s", new QuestObjective("x", new FlagTracker(flag))) }, new QuestSettings { FailWhen = new FlagCondition(flag) });
            var service = _kit.Start();
            service.Give(quest);
            service.SetFlag(flag, 1);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void RepeatableQuests_StartOver_CountCompletions_AndRewardEachTime()
        {
            var a = _kit.Signal("A");
            var log = new List<string>();
            var quest = _kit.Quest("Bounty", new[] { Step("s", Count("x", a, 2)) },
                new QuestSettings { Repeatable = true, Rewards = new IQuestReward[] { new RecordingReward { Name = "coins", Log = log } } });
            var service = _kit.Start();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(service.Give(quest), Is.True);
                Assert.That(service.ProgressOf(quest, "x"), Is.Zero);
                service.Raise(a, 2);
            }
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(3));
            Assert.That(log, Is.EqualTo(new[] { "coins", "coins", "coins" }));
        }

        [Test]
        public void AQuestThatIsntRepeatable_IsGivenOnce()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Once", new[] { Step("s", Count("x", a)) });
            var service = _kit.Start();
            Assert.That(service.Give(quest));
            Assert.That(service.Give(quest), Is.False);
            service.Raise(a);
            Assert.That(service.Give(quest), Is.False);
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(1));
        }

        [Test]
        public void AFailedRepeatableQuest_CanBeGivenAgain()
        {
            var late = _kit.Flag("Late");
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Retry", new[] { Step("s", Count("x", a)) }, new QuestSettings { Repeatable = true, FailWhen = new FlagCondition(late) });
            var service = _kit.Start();
            service.Give(quest);
            service.SetFlag(late, 1);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Failed));
            service.SetFlag(late, 0);
            Assert.That(service.Give(quest));
            service.Raise(a);
            Assert.That(service.CompletionsOf(quest), Is.EqualTo(1));
        }

        [Test]
        public void GivenAndEndedOrder_RiseWithEachQuestEvent()
        {
            var a = _kit.Signal("A");
            var first = _kit.Quest("First", new[] { Step("s", Count("x", a)) });
            var second = _kit.Quest("Second", new[] { Step("s", Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            Assert.That(service.GivenOrderOf(first), Is.Zero);
            service.Give(first);
            service.Give(second);
            service.Raise(a);
            Assert.That(service.GivenOrderOf(first), Is.LessThan(service.GivenOrderOf(second)));
            Assert.That(service.EndedOrderOf(first), Is.GreaterThan(service.GivenOrderOf(second)));
            Assert.That(service.EndedOrderOf(second), Is.Zero);
        }

        [Test]
        public void Rewards_AreGrantedInOrder_AndOneThatThrowsDoesntStopTheRest()
        {
            var a = _kit.Signal("A");
            var log = new List<string>();
            var quest = _kit.Quest("Paid", new[] { Step("s", Count("x", a)) }, new QuestSettings
            {
                Rewards = new IQuestReward[]
                {
                    new RecordingReward { Name = "first", Log = log }, new RecordingReward { Name = "broken", Log = log, Throws = true },
                    new RecordingReward { Name = "last", Log = log },
                },
            });
            var service = _kit.Start();
            service.Give(quest);
            LogAssert.Expect(LogType.Error, new Regex("reward"));
            service.Raise(a);
            Assert.That(log, Is.EqualTo(new[] { "first", "last" }));
        }

        [Test]
        public void Availability_GatesGiving_AndAutomaticQuestsStartWhenAvailable()
        {
            var met = _kit.Flag("MetTheSmith");
            var quest = _kit.Quest("Smith", new[] { Step("s", Count("x", _kit.Signal("A"))) },
                new QuestSettings { AvailableWhen = new FlagCondition(met), GivenAutomatically = true, HiddenUntilGiven = true });
            var manual = _kit.Quest("Manual", new[] { Step("s", Count("x", _kit.Signal("B"))) }, new QuestSettings { AvailableWhen = new FlagCondition(met) });
            var service = _kit.Start();
            Assert.That(service.IsAvailable(quest), Is.False);
            Assert.That(service.IsVisible(quest), Is.False);
            Assert.That(service.Give(manual), Is.False);

            service.SetFlag(met, 1);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.Active));
            Assert.That(service.IsVisible(quest));
            Assert.That(service.StateOf(manual), Is.EqualTo(QuestState.NotGiven));
            Assert.That(service.Give(manual));
        }

        [Test]
        public void AutomaticQuestsWithNoCondition_StartOnNewGame()
        {
            var quest = _kit.Quest("Opening", new[] { Step("s", Count("x", _kit.Signal("A"))) }, new QuestSettings { GivenAutomatically = true });
            Assert.That(_kit.Start().StateOf(quest), Is.EqualTo(QuestState.Active));
        }

        [Test]
        public void HiddenSteps_ShowOnceReached()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Q", new[] { Step("a", Count("x", a)), Step("b", new StepSettings { HiddenUntilReached = true }, Count("y", a)) });
            var service = _kit.Start();
            service.Give(quest);
            Assert.That(service.IsVisible(quest, quest.FindStep("b")), Is.False);
            service.Raise(a);
            Assert.That(service.IsVisible(quest, quest.FindStep("b")));
        }

        [Test]
        public void TheActiveQuest_MustBeActive_AndClearsWhenItEnds()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Main", new[] { Step("s", Count("x", a)) });
            var service = _kit.Start();
            Assert.That(service.SetActive(quest), Is.False);
            service.Give(quest);
            Assert.That(service.SetActive(quest));
            Assert.That(service.ActiveQuest, Is.EqualTo(quest));
            service.Raise(a);
            Assert.That(service.ActiveQuest, Is.Null);
            Assert.That(_kit.Events, Does.Contain("active Main"));
            Assert.That(_kit.Events, Does.Contain("active none"));
        }

        [Test]
        public void SettingAFlagToItsValue_RaisesNothing()
        {
            var flag = _kit.Flag("F", 2);
            var service = _kit.Start();
            service.SetFlag(flag, 2);
            Assert.That(_kit.Events, Is.Empty);
            service.SetFlag(flag, 3);
            Assert.That(_kit.Events, Is.EqualTo(new[] { "flag F 3" }));
            Assert.That(service.ValueOf(null), Is.Zero);
        }

        [Test]
        public void AChange_ChecksOnlyConditionsThatReadIt()
        {
            var watched = _kit.Flag("Watched");
            var other = _kit.Flag("Other");
            var counting = new CountingCondition { Flag = watched };
            var quest = _kit.Quest("Q", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", new StepSettings { UnlockWhen = counting }, Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            var before = counting.Checks;
            for (var i = 1; i <= 10; i++) service.SetFlag(other, i);
            Assert.That(counting.Checks, Is.EqualTo(before));
            service.SetFlag(watched, 1);
            Assert.That(counting.Checks, Is.GreaterThan(before));
            Assert.That(service.StateOf(quest, "b"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void AConditionThatCantSay_IsCheckedAfterAnyChange()
        {
            var counting = new CountingCondition { Flag = _kit.Flag("Watched"), SaysWhatItReads = false };
            var quest = _kit.Quest("Q", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", new StepSettings { UnlockWhen = counting }, Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            var before = counting.Checks;
            service.SetFlag(_kit.Flag("Other"), 1);
            Assert.That(counting.Checks, Is.GreaterThan(before));
        }

        [Test]
        public void ConditionsReadTheGamesOwnFacts_AndTheServiceRegistersItsOwn()
        {
            var service = _kit.Start();
            Assert.That(_kit.Facts.TryGet<IQuestFacts>(out var quests) && quests == service);
            Assert.That(_kit.Facts.TryGet<IFlagFacts>(out var flags) && flags == service);
            service.Dispose();
            Assert.That(_kit.Facts.TryGet<IQuestFacts>(out _), Is.False);
        }

        [Test]
        public void ADisposedService_IgnoresChanges()
        {
            var flag = _kit.Flag("F");
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", a)) });
            var service = _kit.Start();
            service.Dispose();
            _kit.Events.Clear();

            Assert.That(service.Give(quest), Is.False);
            service.SetFlag(flag, 1);
            service.Raise(a);
            service.NewGame();
            Assert.That(_kit.Events, Is.Empty);
            Assert.That(service.ValueOf(flag), Is.Zero);
            Assert.DoesNotThrow(service.Dispose);
        }

        [Test]
        public void AServiceThatCantRegister_LeavesNothingRegistered()
        {
            var other = new OtherFlags();
            _kit.Facts.Register<IFlagFacts>(other);
            Assert.Throws<InvalidOperationException>(() => new QuestService(_kit.Facts, _kit.Quests, _kit.Assets));
            Assert.That(_kit.Facts.TryGet<IQuestFacts>(out _), Is.False);
            Assert.That(_kit.Facts.TryGet<IFlagFacts>(out var flags) && flags == other);
            _kit.Facts.Unregister<IFlagFacts>(other);
        }

        private sealed class OtherFlags : IFlagFacts
        {
            public int ValueOf(StoryFlag flag) => 0;
        }

        [Test]
        public void AConditionThatThrows_IsLogged_AndOtherQuestsStillRun()
        {
            var flag = _kit.Flag("F");
            var broken = _kit.Quest("Broken", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", new StepSettings { UnlockWhen = new ThrowingCondition { Flag = flag } }) });
            var fine = _kit.Quest("Fine", new[] { Step("s", new QuestObjective("f", new FlagTracker(flag))) });
            var service = _kit.Start();
            LogAssert.Expect(LogType.Error, new Regex("Broken"));
            service.Give(broken);
            service.Give(fine);
            LogAssert.Expect(LogType.Error, new Regex("Broken"));
            service.SetFlag(flag, 1);
            Assert.That(service.StateOf(fine), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void ChangesThatNeverStop_AreCutOff()
        {
            var flag = _kit.Flag("Runaway");
            var runaway = new RunawayCondition { Flag = flag };
            var quest = _kit.Quest("Loop", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", new StepSettings { UnlockWhen = runaway }) });
            var service = _kit.Start();
            service.Give(quest);
            runaway.Service = service;
            LogAssert.Expect(LogType.Error, new Regex("stopped settling"));
            service.SetFlag(flag, 1);
            Assert.That(service.ValueOf(flag), Is.GreaterThan(1000));
            runaway.Service = null;
        }

        [Test]
        public void UnknownQuests_ReadAsNotGiven()
        {
            var service = _kit.Start();
            Assert.That(service.StateOf((QuestDefinition)null), Is.EqualTo(QuestState.NotGiven));
            Assert.That(service.StateOf(null, "x"), Is.EqualTo(StepState.Locked));
            Assert.That(service.Give(null), Is.False);
            Assert.That(service.IsVisible(null), Is.False);
        }

        [Test]
        public void V1Bug_ADependencyIsNotUsedUp_WhenSomethingReactsToIt()
        {
            var a = _kit.Signal("A");
            var first = _kit.Quest("First", new[] { Step("s", Count("x", a)) });
            var dependent = new QuestStateCondition(first);
            var second = _kit.Quest("Second", new[] { Step("s", new QuestObjective("y", new ConditionTracker(dependent))) });
            var third = _kit.Quest("Third", new[] { Step("s", new QuestObjective("z", new ConditionTracker(dependent))) });
            var service = _kit.Start();
            service.Give(first);
            service.Raise(a);
            service.Give(second);
            service.Give(third);
            Assert.That(service.StateOf(second), Is.EqualTo(QuestState.Done));
            Assert.That(service.StateOf(third), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void V1Bug_AFalseCheckDoesntWipeProgress()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", a, 3)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a, 2);
            service.SetFlag(_kit.Flag("Unrelated"), 1);
            service.Raise(_kit.Signal("Other"));
            Assert.That(service.ProgressOf(quest, "x"), Is.EqualTo(2));
        }
    }
}
