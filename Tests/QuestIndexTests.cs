using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEditor;
using UnityEngine;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>The quest index: what each quest reads, who reads whom, and updating one quest at a time.</summary> */
    public sealed class QuestIndexTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        [System.Serializable]
        private sealed class ReadsAQuest : ICondition<IFacts>, ISubjectReader
        {
            public QuestDefinition Quest;

            public bool Evaluate(IFacts context) => false;

            public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
            {
                subjects.Add(Quest);
                return true;
            }
        }

        [Test]
        public void ASummary_ReadsEveryKindOfLink_WithItsLabel()
        {
            var a = _kit.Quest("A", new[] { Step("s", Count("x", _kit.Signal("S"))) });
            var flag = _kit.Flag("Elders");
            var b = _kit.Quest("B", new[]
            {
                Step("one", new StepSettings { UnlockWhen = new StepStateCondition(a, "s"), FailWhen = new NotCondition<IFacts> { child = new QuestStateCondition(a) } },
                    new QuestObjective("o", new ConditionTracker(new QuestStateCondition(a, QuestState.Failed))), new QuestObjective("f", new FlagTracker(flag))),
            }, new QuestSettings { AvailableWhen = new AndCondition<IFacts> { children = { new QuestStateCondition(a), new FlagCondition(_kit.Flag("Met")) } }, FailWhen = new ReadsAQuest { Quest = a } });

            var summary = QuestSummary.Of(b);
            Assert.That(summary.Reads.Select(link => link.Label), Is.EquivalentTo(new[] { "needs done", "fails if read", "needs step done", "fails if not done", "tracks failed" }));
            Assert.That(summary.Reads.All(link => link.Read == a && link.Reader == b));
            Assert.That(summary.Reads.Single(link => link.Slot == LinkSlot.StepUnlock).ReaderStepId, Is.EqualTo("one"));
            Assert.That(summary.Flags.Select(f => f.name), Is.EquivalentTo(new[] { "Met", "Elders" }));
            Assert.That(summary.Unreadable, Is.Zero);
        }

        [Test]
        public void ConditionsThatCantSayWhatTheyRead_AreCounted()
        {
            var quest = _kit.Quest("Q", new[] { Step("s") }, new QuestSettings { AvailableWhen = new LeafCondition<IFacts>(_ => true) });
            Assert.That(QuestSummary.Of(quest).Unreadable, Is.EqualTo(1));
        }

        [Test]
        public void TheIndex_KnowsWhoReadsWhom_AndUpdatesOneQuestAtATime()
        {
            var a = _kit.Quest("A", new[] { Step("s", Count("x", _kit.Signal("S"))) });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            var c = _kit.Quest("C", new[] { Step("s") }, new QuestSettings { AvailableWhen = new StepStateCondition(a, "s") });
            var index = new QuestIndex();
            var changed = new List<QuestDefinition>();
            index.OnChanged += changed.Add;
            foreach (var quest in new[] { a, b, c }) index.Refresh(quest);

            Assert.That(index.ReadersOf(a).Select(link => link.Reader), Is.EquivalentTo(new[] { b, c }));
            Assert.That(index.Reads, Is.EqualTo(3));

            b.Configure(new[] { Step("s") });
            index.Refresh(b);
            Assert.That(index.ReadersOf(a).Select(link => link.Reader), Is.EqualTo(new[] { c }), "b's old link is gone");
            Assert.That(index.Reads, Is.EqualTo(4), "only b was read again");

            index.Remove(c);
            Assert.That(index.ReadersOf(a), Is.Empty);
            Assert.That(index.Quests, Is.EquivalentTo(new[] { a, b }));
            Assert.That(changed, Is.EqualTo(new[] { a, b, c, b, c }));
        }

        [Test]
        public void DestroyedQuests_AreForgotten()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            var index = new QuestIndex();
            index.Refresh(a);
            index.Refresh(b);
            Object.DestroyImmediate(b);
            index.RemoveDestroyed();
            Assert.That(index.Quests, Is.EqualTo(new[] { a }));
            Assert.That(index.ReadersOf(a), Is.Empty);
            Assert.That(index.SummaryOf(null), Is.Null);
        }

        [Test]
        public void Problems_AreKeptPerQuest()
        {
            var broken = _kit.Quest("Broken", new[] { Step("s", new QuestObjective("x", null)) });
            var summary = QuestSummary.Of(broken);
            Assert.That(summary.Errors, Is.EqualTo(1));
            Assert.That(summary.Warnings, Is.EqualTo(2), "no kind, no title");
            Assert.That(summary.Issues, Has.Count.EqualTo(3));
        }

        [Test]
        public void ReadingAQuest_NeverMarksItChanged()
        {
            var quest = _kit.Quest("Q", new[] { Step("s", Count("x", _kit.Signal("S"))) }, new QuestSettings { AvailableWhen = new QuestStateCondition(null) });
            var before = EditorUtility.GetDirtyCount(quest);
            QuestSummary.Of(quest);
            new QuestIndex().Refresh(quest);
            Assert.That(EditorUtility.GetDirtyCount(quest), Is.EqualTo(before));
        }

        [Test]
        public void TheSharedIndex_ScansOncePerSession()
        {
            var first = SharedQuestIndex.Get();
            var scans = SharedQuestIndex.Scans;
            Assert.That(SharedQuestIndex.Get(), Is.SameAs(first));
            Assert.That(SharedQuestIndex.Scans, Is.EqualTo(scans));
        }
    }
}
