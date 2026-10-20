using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEditor;
using UnityEngine;
using static QuestSystem.Tests.Build;
using Object = UnityEngine.Object;

namespace QuestSystem.Tests
{
    /**
     * <summary>The quest graph's edits: adding, linking, deleting, copying and pasting steps, and editing quests, each
     * one Undo step. The editor's own Undo history is left alone: each edit starts a group of its own.</summary>
     */
    public sealed class QuestGraphEditTests
    {
        private const string Folder = "Assets/__QuestGraphSmoke";
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown()
        {
            StepClipboard.Clear();
            _kit.Dispose();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        private static StepSettings After(params string[] ids) => new StepSettings { Start = StepStart.AfterLinkedSteps, After = ids };

        private static string Json(Object asset) => EditorJsonUtility.ToJson(asset);

        private static void AssertUndoRestores(Object asset, Action change)
        {
            var before = Json(asset);
            change();
            Assert.That(Json(asset), Is.Not.EqualTo(before), "the change changed something");
            Undo.PerformUndo();
            Assert.That(Json(asset), Is.EqualTo(before), "Undo restores the asset exactly");
        }

        private static void AssertUnchanged(Object asset, Action change)
        {
            var before = Json(asset);
            change();
            Assert.That(Json(asset), Is.EqualTo(before));
        }

        [Test]
        public void Snapping_RoundsToTheGrid_AndHalvesAwayFromZero()
        {
            Assert.That(GraphMetrics.Snap(new Vector2(14.9f, 15f)), Is.EqualTo(new Vector2(10, 20)));
            Assert.That(GraphMetrics.Snap(new Vector2(-5f, 5f)), Is.EqualTo(new Vector2(-10, 10)));
            Assert.That(GraphMetrics.Snap(new Vector2(25f, -25f)), Is.EqualTo(new Vector2(30, -30)), "never banker's rounding");
        }

        [Test]
        public void AddingAStep_AfterAnother_LinksIt_AndSnapsItsPosition()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            var step = StepEdits.AddStep(quest, new Vector2(13.7f, 46f), "a");
            Assert.That(step.Start, Is.EqualTo(StepStart.AfterLinkedSteps));
            Assert.That(step.After, Is.EqualTo(new[] { "a" }));
            Assert.That(step.GraphPosition, Is.EqualTo(new Vector2(10, 50)));
            Assert.That(quest.Steps.Last(), Is.SameAs(step));
            Assert.That(StepEdits.AddStep(quest, Vector2.zero).Start, Is.EqualTo(StepStart.AfterStepAbove));
        }

        [Test]
        public void TheFirstEdit_ToAQuestNeverArranged_SavesWhereEveryStepWasDrawn()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b") });
            Assert.That(quest.GraphArranged, Is.False);
            var drawn = new Dictionary<string, Vector2> { ["a"] = new Vector2(40, 110), ["b"] = new Vector2(320, 110) };
            StepEdits.AddStep(quest, new Vector2(600, 110), "b", drawn);
            Assert.That(quest.GraphArranged);
            Assert.That(quest.FindStep("a").GraphPosition, Is.EqualTo(new Vector2(40, 110)));
            Assert.That(quest.FindStep("b").GraphPosition, Is.EqualTo(new Vector2(320, 110)));
            StepEdits.AddStep(quest, Vector2.zero, null, new Dictionary<string, Vector2> { ["a"] = new Vector2(900, 900) });
            Assert.That(quest.FindStep("a").GraphPosition, Is.EqualTo(new Vector2(40, 110)), "once arranged, positions only change by moving");
        }

        [Test]
        public void Linking_KeepsTheStepAboveAsAnExplicitLink_AndRefusesLoops()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c", new StepSettings { Start = StepStart.WithQuest }) });
            Assert.That(StepEdits.Link(quest, "c", "b"));
            Assert.That(quest.FindStep("b").After, Is.EqualTo(new[] { "a", "c" }));
            Assert.That(StepEdits.Link(quest, "b", "c"), Is.False, "c already leads to b");
            Assert.That(StepEdits.Link(quest, "a", "a"), Is.False);
            Assert.That(StepEdits.Link(quest, "a", "gone"), Is.False);
            Assert.That(StepEdits.Link(quest, "gone", "a"), Is.False);
            Assert.That(StepEdits.WouldLoop(quest, "b", "a"), Is.True);
        }

        [Test]
        public void Linking_WhatIsAlreadyLinked_ChangesNothing()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", After("a")) });
            AssertUnchanged(quest, () => Assert.That(StepEdits.Link(quest, "a", "b")));
        }

        [Test]
        public void Linking_AStepWhoseConditionReplacesTheWait_AddsOnlyTheNewLink()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", new StepSettings { UnlockWhen = new FlagCondition(_kit.Flag("F")) }), Step("c", new StepSettings { Start = StepStart.WithQuest }) });
            Assert.That(StepEdits.WouldLoop(quest, "b", "a"), Is.False, "b only follows a in the list; it never waits for it");
            Assert.That(StepEdits.Link(quest, "c", "b"));
            Assert.That(quest.FindStep("b").After, Is.EqualTo(new[] { "c" }), "b never waited for a, so a isn't kept");
        }

        [Test]
        public void Linking_IsOneUndoStep()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", new StepSettings { Start = StepStart.WithQuest }) });
            AssertUndoRestores(quest, () => StepEdits.Link(quest, "a", "b"));
        }

        [Test]
        public void Unlinking_TheLastLink_StartsTheStepWithTheQuest()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c", After("a", "b")) });
            StepEdits.Unlink(quest, "a", "c");
            Assert.That(quest.FindStep("c").After, Is.EqualTo(new[] { "b" }));
            StepEdits.Unlink(quest, "b", "c");
            Assert.That(quest.FindStep("c").Start, Is.EqualTo(StepStart.WithQuest));
            StepEdits.Unlink(quest, "a", "b");
            Assert.That(quest.FindStep("b").Start, Is.EqualTo(StepStart.WithQuest), "the implicit link to the step above can be removed too");
            StepEdits.SetJoin(quest, "c", StepJoin.Any);
            Assert.That(quest.FindStep("c").AfterRule, Is.EqualTo(StepJoin.Any));
        }

        [Test]
        public void Unlinking_ALinkThatIsntThere_ChangesNothing()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c", After("a")) });
            AssertUnchanged(quest, () =>
            {
                StepEdits.Unlink(quest, "b", "c");
                StepEdits.Unlink(quest, "c", "b");
                StepEdits.Unlink(quest, "a", "gone");
                StepEdits.SetJoin(quest, "c", StepJoin.All);
                StepEdits.SetJoin(quest, "gone", StepJoin.Any);
            });
        }

        [Test]
        public void DeletingSteps_TheirDependantsComeAfterWhatTheyCameAfter()
        {
            var quest = _kit.Quest("Q", new[]
            {
                Step("a"), Step("b", After("a")), Step("c", After("b")),
                Step("d", new StepSettings { Start = StepStart.WithQuest }), Step("e"),
            });
            var result = StepEdits.DeleteSteps(quest, new[] { "b", "d" });
            Assert.That(quest.Steps.Select(s => s.Id), Is.EqualTo(new[] { "a", "c", "e" }));
            Assert.That(quest.FindStep("c").After, Is.EqualTo(new[] { "a" }));
            Assert.That(quest.FindStep("e").Start, Is.EqualTo(StepStart.WithQuest), "e came after d, which started with the quest");
            Assert.That(result.Rewired.Select(s => s.Id), Is.EquivalentTo(new[] { "c", "e" }));
        }

        [Test]
        public void DeletingAStep_KeepsTheOrderOfTheLinksItsDependantHad()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("c", new StepSettings { Start = StepStart.WithQuest }), Step("b", After("a")), Step("d", After("b", "c")) });
            StepEdits.DeleteSteps(quest, new[] { "b" });
            Assert.That(quest.FindStep("d").After, Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void DeletingBothSidesOfADiamond_LeavesOneLinkToItsTop()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", After("a")), Step("c", After("a")), Step("d", After("b", "c")) });
            StepEdits.DeleteSteps(quest, new[] { "b", "c" });
            Assert.That(quest.FindStep("d").After, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void DeletingAStep_InAPlainList_ChangesNothingElse()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c") });
            var result = StepEdits.DeleteSteps(quest, new[] { "b" });
            Assert.That(quest.FindStep("c").Start, Is.EqualTo(StepStart.AfterStepAbove));
            Assert.That(result.Rewired, Is.Empty);
            StepEdits.DeleteSteps(quest, new[] { "a" });
            Assert.That(quest.FindStep("c").Start, Is.EqualTo(StepStart.AfterStepAbove), "the first step starts with the quest either way");
        }

        [Test]
        public void DeletingAStep_LeavesAStepWhoseConditionReplacesTheWait()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b", new StepSettings { UnlockWhen = new FlagCondition(_kit.Flag("F")) }) });
            var result = StepEdits.DeleteSteps(quest, new[] { "a" });
            Assert.That(result.Rewired, Is.Empty);
            Assert.That(quest.FindStep("b").Start, Is.EqualTo(StepStart.AfterStepAbove));
        }

        [Test]
        public void DeletingStepsThatArentThere_ChangesNothing()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            AssertUnchanged(quest, () => Assert.That(StepEdits.DeleteSteps(quest, new[] { "gone" }).Rewired, Is.Empty));
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
            AssertUndoRestores(quest, () => result = StepEdits.DeleteSteps(quest, new[] { "b" }, index));
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

            var text = StepEdits.Copy(source, new[] { "b", "c" });
            Assert.That(StepEdits.CanPaste(text));
            var result = StepEdits.Paste(target, text, new Vector2(500, 500));

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
        public void Pasting_RetargetsStepConditionsInsideObjectives()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            quest.Configure(new[] { Step("a"), Step("b", new QuestObjective("wait", new ConditionTracker(new StepStateCondition(quest, "a")))) });
            var result = StepEdits.Paste(quest, StepEdits.Copy(quest, new[] { "a", "b" }), new Vector2(0, 300));
            var held = new List<ICondition<IFacts>>();
            Trackers.ConditionHolderOf(quest.FindStep(result.StepIds[1]).Objectives[0].Tracker).CollectConditions(held);
            var reference = (StepStateCondition)held.Single();
            Assert.That(reference.StepId, Is.EqualTo(result.StepIds[0]), "the copy of b waits for the copy of a");
            var original = new List<ICondition<IFacts>>();
            Trackers.ConditionHolderOf(quest.FindStep("b").Objectives[0].Tracker).CollectConditions(original);
            Assert.That(((StepStateCondition)original.Single()).StepId, Is.EqualTo("a"));
        }

        [Test]
        public void Copying_TurnsTheStepAboveIntoAnExplicitLink()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b"), Step("c") });
            var result = StepEdits.Paste(quest, StepEdits.Copy(quest, new[] { "b", "c" }), Vector2.zero);
            var b = quest.FindStep(result.StepIds[0]);
            var c = quest.FindStep(result.StepIds[1]);
            Assert.That(quest.Steps, Has.Count.EqualTo(5));
            Assert.That(c.After, Is.EqualTo(new[] { b.Id }));
            Assert.That(result.DroppedLinks, Is.EqualTo(1), "b came after a, which wasn't copied");
            Assert.That(quest.Steps.Select(s => s.Id).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void Pasting_IsOneUndoStep_AndWorksTwice()
        {
            var quest = _kit.Quest("Q", new[] { Step("a"), Step("b") });
            var text = StepEdits.Copy(quest, new[] { "a" });
            AssertUndoRestores(quest, () => StepEdits.Paste(quest, text, Vector2.zero));
            Assert.That(StepEdits.Paste(quest, text, Vector2.zero).StepIds, Has.Count.EqualTo(1), "the copy is still there after an Undo");
        }

        [Test]
        public void PastingSomethingElse_DoesNothing()
        {
            var quest = _kit.Quest("Q", new[] { Step("a") });
            Assert.That(StepEdits.CanPaste("hello"), Is.False);
            Assert.That(StepEdits.CanPaste(null), Is.False);
            AssertUnchanged(quest, () => Assert.That(StepEdits.Paste(quest, "hello", Vector2.zero).StepIds, Is.Empty));
            var stale = StepEdits.Copy(quest, new[] { "a" });
            StepClipboard.Clear();
            Assert.That(StepEdits.CanPaste(stale), Is.False, "a copy that is gone can't be pasted");
            Assert.That(StepEdits.Paste(quest, stale, Vector2.zero).StepIds, Is.Empty);
        }

        [Test]
        public void ALinkInTheWeb_JoinsTheExistingConditionWithAnd()
        {
            var smith = _kit.Quest("Smith", new[] { Step("s") }, new QuestSettings { Title = "Smith" });
            var map = _kit.Quest("Map", new[] { Step("s") }, new QuestSettings { Title = "Map" });
            var empty = _kit.Quest("Empty", new[] { Step("s") });
            var flagged = _kit.Quest("Flagged", new[] { Step("s") }, new QuestSettings { AvailableWhen = new FlagCondition(_kit.Flag("Met")) });
            var anded = _kit.Quest("Anded", new[] { Step("s") }, new QuestSettings { AvailableWhen = new AndCondition<IFacts> { children = { new FlagCondition(_kit.Flag("Met")) } } });

            Assert.That(QuestEdits.PreviewAvailability(flagged, smith), Is.EqualTo("(Met and Smith done)"));
            Assert.That(flagged.AvailableWhen, Is.InstanceOf<FlagCondition>(), "a preview changes nothing");
            Assert.That(QuestEdits.AddAvailability(empty, smith));
            Assert.That(QuestEdits.AddAvailability(flagged, smith));
            Assert.That(QuestEdits.AddAvailability(anded, smith));
            Assert.That(QuestEdits.AddAvailability(anded, map));
            Assert.That(ConditionTree.Summarize(empty.AvailableWhen), Is.EqualTo("Smith done"));
            Assert.That(ConditionTree.Summarize(flagged.AvailableWhen), Is.EqualTo("(Met and Smith done)"));
            Assert.That(ConditionTree.Summarize(anded.AvailableWhen), Is.EqualTo("(Met and Smith done and Map done)"));
            AssertUndoRestores(empty, () => QuestEdits.AddAvailability(empty, map));
        }

        [Test]
        public void ALinkInTheWeb_ThatIsAlreadyThere_ChangesNothing()
        {
            var smith = _kit.Quest("Smith", new[] { Step("s") }, new QuestSettings { Title = "Smith" });
            var quest = _kit.Quest("Q", new[] { Step("s") });
            Assert.That(QuestEdits.AddAvailability(quest, smith));
            AssertUnchanged(quest, () => Assert.That(QuestEdits.AddAvailability(quest, smith), Is.False));
            Assert.That(QuestEdits.PreviewAvailability(quest, smith), Is.EqualTo("Smith done"));
            Assert.That(QuestEdits.AddAvailability(quest, null), Is.False);
            Assert.That(QuestEdits.AddAvailability(null, smith), Is.False);
        }

        [Test]
        public void QuestsThatWouldNeedEachOther_AreRefused()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") }, new QuestSettings { AvailableWhen = new QuestStateCondition(a) });
            var c = _kit.Quest("C", new[] { Step("s") }, new QuestSettings { AvailableWhen = new OrCondition<IFacts> { children = { new QuestStateCondition(b), new FlagCondition(_kit.Flag("F")) } } });
            var index = new QuestIndex();
            foreach (var quest in new[] { a, b, c }) index.Refresh(quest);
            Assert.That(QuestEdits.WouldLoop(index, a, b), Is.True);
            Assert.That(QuestEdits.WouldLoop(index, a, a), Is.True);
            Assert.That(QuestEdits.WouldLoop(index, b, c), Is.False, "c only needs b through an Or");
        }

        [Test]
        public void MovingQuests_PinsThem_AndBulkEdits_AreOneUndoStep()
        {
            var a = _kit.Quest("A", new[] { Step("s") });
            var b = _kit.Quest("B", new[] { Step("s") });
            QuestEdits.MoveQuests(new Dictionary<QuestDefinition, Vector2> { [a] = new Vector2(104, 96) });
            Assert.That(a.WebPinned && a.WebPosition == new Vector2(100, 100));
            QuestEdits.Unpin(new[] { a });
            Assert.That(a.WebPinned, Is.False);

            var kind = ScriptableObject.CreateInstance<QuestKind>();
            try
            {
                var before = (Json(a), Json(b));
                QuestEdits.SetKind(new[] { a, b }, kind);
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
        public void AGiverWithoutADisplayName_IsRefused()
        {
            var quest = _kit.Quest("Q", new[] { Step("s") });
            var texture = new Texture2D(1, 1);
            try
            {
                AssertUnchanged(quest, () => Assert.That(QuestEdits.SetGiver(new[] { quest }, texture), Is.False));
                Assert.That(QuestEdits.SetPlace(new[] { quest }, null), "clearing is always allowed");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void CreatingAQuest_MakesAPinnedAssetOfTheKind()
        {
            AssetDatabase.CreateFolder("Assets", "__QuestGraphSmoke");
            var kind = ScriptableObject.CreateInstance<QuestKind>();
            try
            {
                var quest = QuestEdits.CreateQuest(Folder, "New: Errand?", kind, new Vector2(123, 47));
                Assert.That(AssetDatabase.GetAssetPath(quest), Is.EqualTo(Folder + "/New Errand.asset"));
                Assert.That(QuestStates.TitleOf(quest), Is.EqualTo("New: Errand?"));
                Assert.That(quest.Kind, Is.SameAs(kind));
                Assert.That(quest.WebPinned && quest.WebPosition == new Vector2(120, 50));
                Assert.That(AssetDatabase.GetAssetPath(QuestEdits.CreateQuest(Folder, "New: Errand?", kind, Vector2.zero)), Is.Not.EqualTo(Folder + "/New Errand.asset"), "a second gets a name of its own");
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
            AssertUndoRestores(quest, () => StepEdits.MoveSteps(quest, new Dictionary<string, Vector2> { ["a"] = new Vector2(20, 20), ["b"] = new Vector2(300, 20), ["gone"] = Vector2.one }));
            StepEdits.MoveSteps(quest, new Dictionary<string, Vector2> { ["a"] = new Vector2(20, 20), ["b"] = new Vector2(300, 20) });
            Assert.That(quest.GraphArranged);
            AssertUnchanged(quest, () => StepEdits.MoveSteps(quest, new Dictionary<string, Vector2> { ["a"] = new Vector2(21, 19) }));
        }
    }
}
