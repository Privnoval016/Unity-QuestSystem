using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEngine;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>The quest web: which quests link to which, and laying them out in bands by kind.</summary> */
    public sealed class QuestWebTests
    {
        private QuestKit _kit;
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made) Object.DestroyImmediate(made);
            _made.Clear();
            _kit.Dispose();
        }

        private QuestKind Kind(string name, int order)
        {
            var kind = ScriptableObject.CreateInstance<QuestKind>();
            kind.name = name;
            var serialized = new UnityEditor.SerializedObject(kind);
            serialized.FindProperty("order").intValue = order;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _made.Add(kind);
            return kind;
        }

        private QuestIndex Index(params QuestDefinition[] quests)
        {
            var index = new QuestIndex();
            foreach (var quest in quests) index.Refresh(quest);
            return index;
        }

        [Test]
        public void Links_AreOnePerPair_JoinTheirLabels_AndLeaveOutSelfReads()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s", new StepSettings { UnlockWhen = new StepStateCondition(a, "s") }) },
                new QuestSettings { AvailableWhen = new QuestStateCondition(a), FailWhen = new QuestStateCondition(a, QuestState.Failed) });
            var self = _kit.Quest("Self", new[] { Step("s") });
            self.Configure(new[] { Step("s") }, new QuestSettings { AvailableWhen = new NotCondition<IFacts> { child = new QuestStateCondition(self) } });
            var index = Index(a, b, self);

            var links = WebLinks.Of(index, new[] { a, b, self });
            Assert.That(links, Has.Count.EqualTo(1));
            Assert.That(links[0].From, Is.SameAs(a));
            Assert.That(links[0].To, Is.SameAs(b));
            Assert.That(links[0].Fails, Is.True);
            Assert.That(links[0].Label, Does.StartWith("needs done · "));
            Assert.That(WebLinks.ReadsItself(index, self));
            Assert.That(WebLinks.ReadsItself(index, a), Is.False);
        }

        [Test]
        public void Links_ToQuestsNotShown_AreLeftOut()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            Assert.That(WebLinks.Of(Index(a, b), new[] { b }), Is.Empty);
        }

        [Test]
        public void TheLayout_PutsKindsInBands_InOrder_AndQuestsInColumnsByWhatLeadsToThem()
        {
            var main = Kind("Main", 0);
            var side = Kind("Side", 1);
            var a = _kit.Quest("A", new[] { Step("s") }, new QuestSettings { Kind = main });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { Kind = main, AvailableWhen = new QuestStateCondition(a) });
            var c = _kit.Quest("C", new[] { Step("s") }, new QuestSettings { Kind = side, AvailableWhen = new QuestStateCondition(b) });
            var loose = _kit.Quest("Loose", new[] { Step("s") });
            var quests = new[] { c, loose, b, a };
            var (positions, bands) = WebLayout.Arrange(quests, WebLinks.Of(Index(quests), quests));

            Assert.That(bands.Select(band => band.Kind), Is.EqualTo(new[] { main, side, null }), "kinds in order, quests without one last");
            Assert.That(positions[a].x, Is.EqualTo(WebLayout.Left));
            Assert.That(positions[b].x, Is.EqualTo(WebLayout.Left + WebLayout.ColumnWidth));
            Assert.That(positions[c].x, Is.EqualTo(WebLayout.Left + 2 * WebLayout.ColumnWidth));
            Assert.That(positions[c].y, Is.GreaterThanOrEqualTo(bands[1].Top));
            Assert.That(positions[loose].y, Is.GreaterThanOrEqualTo(bands[2].Top));
        }

        [Test]
        public void TheLayout_KeepsPinnedQuests_SurvivesLoops_AndIsTheSameEveryTime()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            a.Configure(new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(b) });
            var pinned = _kit.Quest("Pinned", new[] { Step("s") });
            pinned.SetWebPlacement(new Vector2(900, 500), true);
            var quests = new[] { a, b, pinned };
            var links = WebLinks.Of(Index(quests), quests);

            var first = WebLayout.Arrange(quests, links);
            var second = WebLayout.Arrange(quests, links);
            Assert.That(first.positions[pinned], Is.EqualTo(new Vector2(900, 500)));
            Assert.That(first.positions.Values, Is.EqualTo(second.positions.Values));
            Assert.That(first.bands.Last().Height, Is.GreaterThanOrEqualTo(500 + WebLayout.RowHeight), "a band grows to hold a quest pinned low in it");
        }

        [Test]
        public void FiveHundredQuests_LayOutQuickly()
        {
            var kinds = new[] { Kind("K0", 0), Kind("K1", 1), Kind("K2", 2) };
            var quests = new List<QuestDefinition>();
            for (var i = 0; i < 500; i++)
            {
                var needs = i >= 3 ? quests[i - 3] : null;
                quests.Add(_kit.Quest("Q" + i, new[] { Step("s", Count("x", _kit.Signal("S" + i))) },
                    new QuestSettings { Kind = kinds[i % 3], AvailableWhen = needs == null ? null : new QuestStateCondition(needs) }));
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var index = Index(quests.ToArray());
            var indexed = watch.ElapsedMilliseconds;
            var links = WebLinks.Of(index, quests);
            var (positions, _) = WebLayout.Arrange(quests, links);
            watch.Stop();
            Debug.Log($"Quest web: indexed 500 quests in {indexed} ms; links and layout in {watch.ElapsedMilliseconds - indexed} ms");
            Assert.That(positions, Has.Count.EqualTo(500));
            Assert.That(links, Has.Count.EqualTo(497));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(3000));
        }
    }
}
