using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SaveSystem;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Saving and restoring quests and flags through the save system.</summary> */
    public sealed class QuestSaveTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static SaveDocument ThroughBytes(SaveDocument document)
        {
            var serializer = new SaveSerializer();
            return SaveDocument.FromJson(serializer.Parse(serializer.ToBytes(document.ToJson(), true)));
        }

        private SaveDocument Save()
        {
            var saves = new SaveService();
            saves.Register(new QuestSaveSection(_kit.Service));
            return ThroughBytes(saves.Capture());
        }

        private LoadResult Load(SaveDocument document)
        {
            _kit.Start(newGame: false);
            var saves = new SaveService();
            saves.Register(new QuestSaveSection(_kit.Service));
            return saves.Restore(document);
        }

        [Test]
        public void EveryState_RoundTrips()
        {
            var a = _kit.Signal("A");
            var flag = _kit.Flag("Flag");
            var done = _kit.Quest("Done", new[] { Step("s", Count("x", a)) }, new QuestSettings { Repeatable = true });
            var active = _kit.Quest("Active", new[] { Step("one", Count("y", _kit.Signal("B"), 5)), Step("two", Count("z", a)) });
            var doom = _kit.Flag("Doom");
            var failed = _kit.Quest("Failed", new[] { Step("s", Count("w", _kit.Signal("C"))) }, new QuestSettings { FailWhen = new FlagCondition(doom) });
            var service = _kit.Start();
            service.Give(done);
            service.Raise(a);
            service.Give(done);
            service.Raise(a);
            service.Give(active);
            service.SetActive(active);
            service.SetFlag(flag, 7);
            service.Give(failed);
            service.SetFlag(doom, 1);

            var document = Save();
            var result = Load(document);
            var restored = _kit.Service;

            Assert.That(result.Succeeded, result.ToString());
            Assert.That(restored.StateOf(done), Is.EqualTo(QuestState.Done));
            Assert.That(restored.CompletionsOf(done), Is.EqualTo(2));
            Assert.That(restored.StateOf(active), Is.EqualTo(QuestState.Active));
            Assert.That(restored.StateOf(active, "one"), Is.EqualTo(StepState.Active));
            Assert.That(restored.StateOf(failed), Is.EqualTo(QuestState.Failed));
            Assert.That(restored.ValueOf(flag), Is.EqualTo(7));
            Assert.That(restored.ActiveQuest, Is.EqualTo(active));
        }

        [Test]
        public void Progress_RoundTrips_AndCountingContinues()
        {
            var wolf = _kit.Signal("Wolf");
            var quest = _kit.Quest("Hunt", new[] { Step("s", Count("wolves", wolf, 5)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(wolf, 3);

            Load(Save());
            Assert.That(_kit.Service.ProgressOf(quest, "wolves"), Is.EqualTo(3));
            _kit.Service.Raise(wolf, 2);
            Assert.That(_kit.Service.StateOf(quest), Is.EqualTo(QuestState.Done));
        }

        [Test]
        public void Restoring_GrantsNothing_AndRaisesNothingForWhatTheSaveAlreadyHad()
        {
            var a = _kit.Signal("A");
            var log = new List<string>();
            var quest = _kit.Quest("Paid", new[] { Step("s", Count("x", a)) }, new QuestSettings { Rewards = new IQuestReward[] { new RecordingReward { Name = "gold", Log = log } } });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            var document = Save();
            _kit.Events.Clear();

            Load(document);
            Assert.That(log, Is.EqualTo(new[] { "gold" }));
            Assert.That(_kit.Events, Is.Empty);
        }

        [Test]
        public void QuestsAndFlagsMissingFromTheGame_AreKept()
        {
            var removedQuest = _kit.Quest("Removed", new[] { Step("s", Count("x", _kit.Signal("A"))) });
            var removedFlag = _kit.Flag("RemovedFlag");
            var service = _kit.Start();
            service.Give(removedQuest);
            service.SetFlag(removedFlag, 4);
            var document = Save();

            _kit.Quests.Remove(removedQuest);
            _kit.Assets.Remove(removedFlag);
            Load(document);
            var resaved = Save();
            var data = resaved.Sections[QuestSaveSection.DefaultKey].Data;

            Assert.That(data["Quests"].Count(), Is.EqualTo(1));
            Assert.That(data["Quests"][0]["Quest"].ToString(), Is.EqualTo(removedQuest.AssetId.Value));
            Assert.That(data["Flags"][0]["Value"].ToObject<int>(), Is.EqualTo(4));
        }

        [Test]
        public void AStepAddedAfterTheSave_StartsByItsRule()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Growing", new[] { Step("one", Count("x", a)), Step("two", Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            var document = Save();

            var json = document.Sections[QuestSaveSection.DefaultKey].Data;
            ((JArray)json["Quests"][0]["Steps"]).RemoveAt(1);
            Load(document);

            Assert.That(_kit.Service.StateOf(quest, "two"), Is.EqualTo(StepState.Active));
        }

        [Test]
        public void ANewGame_ClearsQuestsAndFlags()
        {
            var flag = _kit.Flag("F");
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", _kit.Signal("A"))) });
            var service = _kit.Start();
            service.Give(quest);
            service.SetFlag(flag, 2);
            var saves = new SaveService();
            saves.Register(new QuestSaveSection(service));

            Assert.That(saves.NewGame().Succeeded);
            Assert.That(service.StateOf(quest), Is.EqualTo(QuestState.NotGiven));
            Assert.That(service.ValueOf(flag), Is.Zero);
            Assert.That(service.Capture().Quests, Is.Empty);
        }

        [Test]
        public void Restoring_OntoAServiceInUse_ReplacesEverything()
        {
            var a = _kit.Signal("A");
            var flag = _kit.Flag("F");
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", a, 3)) });
            var later = _kit.Quest("Later", new[] { Step("s", Count("y", a, 9)) });
            var service = _kit.Start();
            service.Give(quest);
            service.Raise(a);
            var saved = service.Capture();

            service.Raise(a);
            service.SetFlag(flag, 5);
            service.Give(later);
            service.SetActive(later);
            service.Restore(saved);

            Assert.That(service.ProgressOf(quest, "x"), Is.EqualTo(1));
            Assert.That(service.ValueOf(flag), Is.Zero);
            Assert.That(service.StateOf(later), Is.EqualTo(QuestState.NotGiven));
            Assert.That(service.ProgressOf(later, "y"), Is.Zero);
            Assert.That(service.ActiveQuest, Is.Null);
        }

        [Test]
        public void DamagedValues_AreKeptInRange()
        {
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", _kit.Signal("A"), 3)) });
            _kit.Start(newGame: false);
            _kit.Service.Restore(new QuestSaveData
            {
                Quests =
                {
                    new QuestEntry
                    {
                        Quest = quest.AssetId.Value, State = (QuestState)99, Completions = -4, GivenOrder = -1,
                        Steps = { new StepEntry { Step = "s", State = (StepState)42 }, null },
                        Objectives = { new ObjectiveEntry { Objective = "x", Progress = 999 }, new ObjectiveEntry { Objective = "missing", Progress = 1 }, null },
                    },
                },
            });

            Assert.That(_kit.Service.StateOf(quest), Is.EqualTo(QuestState.NotGiven));
            Assert.That(_kit.Service.CompletionsOf(quest), Is.Zero);
            Assert.That(_kit.Service.GivenOrderOf(quest), Is.Zero);
            Assert.That(_kit.Service.StateOf(quest, "s"), Is.EqualTo(StepState.Locked));
            Assert.That(_kit.Service.ProgressOf(quest, "x"), Is.EqualTo(3));
        }

        [Test]
        public void AnActiveQuestWhoseEndingStepIsDone_CompletesOnLoad()
        {
            var quest = _kit.Quest("Q", new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", Count("y", _kit.Signal("B"))) });
            _kit.Start(newGame: false);
            _kit.Service.Restore(new QuestSaveData
            {
                Quests = { new QuestEntry { Quest = quest.AssetId.Value, State = QuestState.Active, Steps = { new StepEntry { Step = "a", State = StepState.Done }, new StepEntry { Step = "b", State = StepState.Done } } } },
            });
            Assert.That(_kit.Service.StateOf(quest), Is.EqualTo(QuestState.Done));
            Assert.That(_kit.Service.CompletionsOf(quest), Is.EqualTo(1));
        }

        [Test]
        public void QuestOrder_RoundTrips_AndKeepsRising()
        {
            var a = _kit.Signal("A");
            var first = _kit.Quest("First", new[] { Step("s", Count("x", a)) });
            var second = _kit.Quest("Second", new[] { Step("s", Count("y", _kit.Signal("B"))) });
            var service = _kit.Start();
            service.Give(first);
            service.Raise(a);
            var ended = service.EndedOrderOf(first);

            Load(Save());
            Assert.That(_kit.Service.EndedOrderOf(first), Is.EqualTo(ended));
            _kit.Service.Give(second);
            Assert.That(_kit.Service.GivenOrderOf(second), Is.GreaterThan(ended));
        }

        [Test]
        public void AnEmptyOrDamagedSection_LoadsAsANewGame()
        {
            var quest = _kit.Quest("Opening", new[] { Step("s", Count("x", _kit.Signal("A"))) }, new QuestSettings { GivenAutomatically = true });
            _kit.Start(newGame: false);
            _kit.Service.Restore(null);
            Assert.That(_kit.Service.StateOf(quest), Is.EqualTo(QuestState.Active));
            _kit.Service.Restore(new QuestSaveData { Quests = { null, new QuestEntry { Quest = "nonsense" } }, Flags = { null } });
            Assert.That(_kit.Service.StateOf(quest), Is.EqualTo(QuestState.Active));
        }
    }
}
