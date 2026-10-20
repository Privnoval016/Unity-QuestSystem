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
    /** <summary>The quest graph's edits: adding, linking, deleting, copying and pasting steps, and editing quests, each through Undo.</summary> */
    public sealed class QuestGraphEditTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp()
        {
            _kit = new QuestKit();
            Undo.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            _kit.Dispose();
        }

        private static StepSettings After(params string[] ids) => new StepSettings { Start = StepStart.AfterLinkedSteps, After = ids };

        private static string Json(Object asset) => EditorJsonUtility.ToJson(asset);

        private static void AssertUndoRestores(Object asset, System.Action change)
        {
            var before = Json(asset);
            change();
            Assert.That(Json(asset), Is.Not.EqualTo(before), "the change changed something");
            Undo.PerformUndo();
            Assert.That(Json(asset), Is.EqualTo(before), "Undo restores the asset exactly");
        }

        [Test]
        public void AddingAStep_AfterAnother_LinksIt_AndSnapsItsPosition()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            var step = GraphEdits.AddStep(quest, new Vector2(13.7f, 46f), "a");
            Assert.That(step.Start, Is.EqualTo(StepStart.AfterLinkedSteps));
            Assert.That(step.After, Is.EqualTo(new[] { "a" }));
            Assert.That(step.GraphPosition, Is.EqualTo(new Vector2(10, 50)));
            Assert.That(quest.Steps.Last(), Is.SameAs(step));
            Assert.That(GraphEdits.AddStep(quest, Vector2.zero).Start, Is.EqualTo(StepStart.AfterStepAbove));
        }

        [Test]
        public void Linking_KeepsTheStepAboveAsAnExplicitLink_AndRefusesLoops()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c", new StepSettings { Start = StepStart.WithQuest }) });
            Assert.That(GraphEdits.Link(quest, "c", "b"));
            Assert.That(quest.FindStep("b").After, Is.EqualTo(new[] { "a", "c" }));
            Assert.That(GraphEdits.Link(quest, "b", "c"), Is.False, "c already leads to b");
            Assert.That(GraphEdits.Link(quest, "a", "a"), Is.False);
            Assert.That(GraphEdits.Link(quest, "a", "gone"), Is.False);
            Assert.That(GraphEdits.WouldLoop(quest, "b", "a"), Is.True);
        }

        [Test]
        public void Linking_IsOneUndoStep()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", new StepSettings { Start = StepStart.WithQuest }) });
            AssertUndoRestores(quest, () => GraphEdits.Link(quest, "a", "b"));
        }

        [Test]
        public void Unlinking_TheLastLink_StartsTheStepWithTheQuest()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c", After("a", "b")) });
            GraphEdits.Unlink(quest, "a", "c");
            Assert.That(quest.FindStep("c").After, Is.EqualTo(new[] { "b" }));
            GraphEdits.Unlink(quest, "b", "c");
            Assert.That(quest.FindStep("c").Start, Is.EqualTo(StepStart.WithQuest));
            GraphEdits.Unlink(quest, "a", "b");
            Assert.That(quest.FindStep("b").Start, Is.EqualTo(StepStart.WithQuest), "the implicit link to the step above can be removed too");
            GraphEdits.SetJoin(quest, "c", StepJoin.Any);
            Assert.That(quest.FindStep("c").AfterRule, Is.EqualTo(StepJoin.Any));
        }

        [Test]
        public void DeletingASteps_ItsDependantsComeAfterWhatItCameAfter()
        {
            var quest = _kit.Quest("Q", new[]
            {
                Step("a"), Step("b", After("a")), Step("c", After("b")),
                Step("d", new StepSettings { Start = StepStart.WithQuest }), Step("e"),
            });
            var result = GraphEdits.DeleteSteps(quest, new[] { "b", "d" });
            Assert.That(quest.Steps.Select(s => s.Id), Is.EqualTo(new[] { "a", "c", "e" }));
            Assert.That(quest.FindStep("c").After, Is.EqualTo(new[] { "a" }));
            Assert.That(quest.FindStep("e").Start, Is.EqualTo(StepStart.WithQuest), "e came after d, which started with the quest");
            Assert.That(result.Rewired.Select(s => s.Id), Is.EquivalentTo(new[] { "c", "e" }));
        }

        [Test]
        public void DeletingAStep_InAPlainList_ChangesNothingElse()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c") });
            var result = GraphEdits.DeleteSteps(quest, new[] { "b" });
            Assert.That(quest.FindStep("c").Start, Is.EqualTo(StepStart.AfterStepAbove));
            Assert.That(result.Rewired, Is.Empty);
        }

        [Test]
        public void DeletingAStep_ListsConditionsThatReadIt_AndUndoRestoresIt()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b") });
            var other = _kit.Quest("Other", new[] { Step("s") }, new QuestSettings { AvailableWhen = new StepStateCondition(quest, "b") });
            var index = new QuestIndex();
            index.Refresh(quest);
            index.Refresh(other);
            DeleteResult result = default;
            AssertUndoRestores(quest, () => result = GraphEdits.DeleteSteps(quest, new[] { "b" }, index));
            Assert.That(result.Readers.Select(link => link.Reader), Is.EqualTo(new[] { other }));
        }

        [Test]
        public void Pasting_IntoAnotherQuest_GivesFreshIds_KeepsInnerLinks_AndDropsOuterOnes()
        {
            var source = _kit.Quest("Source", new[] { Step("a") });
            source.Configure(new[]
            {
                Step("a", Count("x", _kit.Signal("A"))),
                Step("b", After("a"), Count("y", _kit.Signal("B"))),
                Step("c", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "b" }, UnlockWhen = new StepStateCondition(source, "b") }, Count("z", _kit.Signal("C"))),
            });
            source.FindStep("b").SetGraphPosition(new Vector2(100, 40));
            source.FindStep("c").SetGraphPosition(new Vector2(300, 80));
            var target = _kit.Quest("Target", new[] { Step("t") });

            var text = GraphEdits.Copy(source, new[] { "b", "c" });
            Assert.That(GraphEdits.CanPaste(text));
            var result = GraphEdits.Paste(target, text, new Vector2(500, 500));

            Assert.That(result.StepIds, Has.Count.EqualTo(2));
            Assert.That(result.DroppedLinks, Is.EqualTo(1), "b's link to a, which wasn't copied");
            var b = target.FindStep(result.StepIds[0]);
            var c = target.FindStep(result.StepIds[1]);
            Assert.That(b.Id, Is.Not.EqualTo("b"));
            Assert.That(b.Start, Is.EqualTo(StepStart.WithQuest));
            Assert.That(c.After, Is.EqualTo(new[] { b.Id }));
            var condition = (StepStateCondition)c.UnlockWhen;
            Assert.That(condition.Quest, Is.SameAs(target));
            Assert.That(condition.StepId, Is.EqualTo(b.Id));
            Assert.That(b.GraphPosition, Is.EqualTo(new Vector2(500, 500)));
            Assert.That(c.GraphPosition, Is.EqualTo(new Vector2(700, 540)));
            Assert.That(b.Objectives[0].Id, Is.Not.EqualTo("y"));
            Assert.That(((StepStateCondition)source.FindStep("c").UnlockWhen).Quest, Is.SameAs(source), "the original is untouched");
            Assert.That(QuestValidator.Validate(target).Where(i => i.Level == QuestIssueLevel.Error), Is.Empty);
        }

        [Test]
        public void Copying_TurnsTheStepAboveIntoAnExplicitLink()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c") });
            var result = GraphEdits.Paste(quest, GraphEdits.Copy(quest, new[] { "b", "c" }), Vector2.zero);
            var b = quest.FindStep(result.StepIds[0]);
            var c = quest.FindStep(result.StepIds[1]);
            Assert.That(quest.Steps, Has.Count.EqualTo(5));
            Assert.That(c.After, Is.EqualTo(new[] { b.Id }));
            Assert.That(result.DroppedLinks, Is.EqualTo(1), "b came after a, which wasn't copied");
            Assert.That(quest.Steps.Select(s => s.Id).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void PastingSomethingElse_DoesNothing()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            Assert.That(GraphEdits.CanPaste("hello"), Is.False);
            Assert.That(GraphEdits.Paste(quest, "hello", Vector2.zero).StepIds, Is.Empty);
            Assert.That(quest.Steps, Has.Count.EqualTo(1));
        }

        [Test]
        public void ALinkInTheWeb_JoinsTheExistingConditionWithAnd()
        {
            var smith = _kit.Quest("Smith", new[] { Step("s") }, new QuestSettings { Title = "Smith" });
            var map = _kit.Quest("Map", new[] { Step("s") }, new QuestSettings { Title = "Map" });
            var empty = _kit.Quest("Empty", new[] { Step("s") });
            var flagged = _kit.Quest("Flagged", new[] { Step("s") }, new QuestSettings { AvailableWhen = new FlagCondition(_kit.Flag("Met")) });
            var anded = _kit.Quest("Anded", new[] { Step("s") }, new QuestSettings { AvailableWhen = new AndCondition<IFacts> { children = { new FlagCondition(_kit.Flag("Met")) } } });

            Assert.That(GraphEdits.PreviewAvailability(flagged, smith), Is.EqualTo("(Met and Smith done)"));
            Assert.That(flagged.AvailableWhen, Is.InstanceOf<FlagCondition>(), "a preview changes nothing");
            GraphEdits.AddAvailability(empty, smith);
            GraphEdits.AddAvailability(flagged, smith);
            GraphEdits.AddAvailability(anded, smith);
            GraphEdits.AddAvailability(anded, map);
            Assert.That(ConditionTree.Summarize(empty.AvailableWhen), Is.EqualTo("Smith done"));
            Assert.That(ConditionTree.Summarize(flagged.AvailableWhen), Is.EqualTo("(Met and Smith done)"));
            Assert.That(ConditionTree.Summarize(anded.AvailableWhen), Is.EqualTo("(Met and Smith done and Map done)"));
            AssertUndoRestores(empty, () => GraphEdits.AddAvailability(empty, map));
        }

        [Test]
        public void QuestsThatWouldNeedEachOther_AreRefused()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            var c = _kit.Quest("C", new[] { Step("s") }, new QuestSettings { AvailableWhen = new OrCondition<IFacts> { children = { new QuestStateCondition(b), new FlagCondition(_kit.Flag("F")) } } });
            var index = new QuestIndex();
            foreach (var quest in new[] { a, b, c }) index.Refresh(quest);
            Assert.That(GraphEdits.WouldLoop(index, a, b), Is.True);
            Assert.That(GraphEdits.WouldLoop(index, a, a), Is.True);
            Assert.That(GraphEdits.WouldLoop(index, b, c), Is.False, "c only needs b through an Or");
        }

        [Test]
        public void MovingQuests_PinsThem_AndBulkEdits_AreOneUndoStep()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") });
            GraphEdits.MoveQuests(new Dictionary<QuestDefinition, Vector2> { [a] = new Vector2(104, 96) });
            Assert.That(a.WebPinned && a.WebPosition == new Vector2(100, 100));
            GraphEdits.Unpin(new[] { a });
            Assert.That(a.WebPinned, Is.False);

            var kind = ScriptableObject.CreateInstance<QuestKind>();
            try
            {
                var before = (Json(a), Json(b));
                GraphEdits.SetKind(new[] { a, b }, kind);
                Assert.That(a.Kind == kind && b.Kind == kind);
                Undo.PerformUndo();
                Assert.That((Json(a), Json(b)), Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(kind);
            }
        }

        [Test]
        public void MovingSteps_IsOneUndoStep_AndSkipsStepsThatDidntMove()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b") });
            AssertUndoRestores(quest, () => GraphEdits.MoveSteps(quest, new Dictionary<string, Vector2> { ["a"] = new Vector2(20, 20), ["b"] = new Vector2(300, 20), ["gone"] = Vector2.one }));
            var before = Json(quest);
            GraphEdits.MoveSteps(quest, new Dictionary<string, Vector2> { ["a"] = Vector2.zero });
            Assert.That(Json(quest), Is.EqualTo(before));
        }
    }
}
