using System.Collections;
using System.Linq;
using Extensions.Logic;
using NUnit.Framework;
using QuestSystem.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>The quest screen: what each step node shows, laying steps out, and the window opening on a saved quest.</summary> */
    public sealed class QuestGraphScreenTests
    {
        private const string Folder = "Assets/__QuestGraphSmoke";
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown()
        {
            _kit.Dispose();
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        }

        private static StepSettings After(params string[] ids) => new StepSettings { Start = StepStart.AfterLinkedSteps, After = ids };

        [Test]
        public void Cards_ShowPills_Objectives_Rules_AndProblems()
        {
            var flag = _kit.Flag("Sided");
            var quest = _kit.Quest("Vote", new[]
            {
                Step("hear", new StepSettings { Text = "Hear the proposal" }, Count("talk", _kit.Signal("Talked"), 1)),
                Step("guild", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "hear" }, UnlockWhen = new FlagCondition(flag) }, Count("sign", _kit.Signal("Signed"), 3)),
                Step("rebels", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "hear" }, FailWhen = new FlagCondition(flag, 2) }, new QuestObjective("x", null)),
                Step("report", new StepSettings { Start = StepStart.AfterLinkedSteps, After = new[] { "guild", "rebels" }, AfterRule = StepJoin.Any, Completion = StepCompletion.AnyOne, HiddenUntilReached = false },
                    Count("seal", _kit.Signal("Seal")), new QuestObjective("bonus", new FlagTracker(flag), 1, true)),
            });
            var cards = StepCard.Of(quest, QuestValidator.Validate(quest)).ToDictionary(card => card.Id);

            Assert.That(cards["hear"].Title, Is.EqualTo("Hear the proposal"));
            Assert.That(cards["guild"].Title, Is.EqualTo("guild"), "a step without text shows its id");
            Assert.That(cards["guild"].Objectives[0].Tracker, Is.EqualTo("Signed × 3"));
            Assert.That(cards["guild"].Rules.Single().Label, Is.EqualTo("And if"));
            Assert.That(cards["rebels"].Rules.Single().Label, Is.EqualTo("Fails quest if"));
            Assert.That(cards["rebels"].Objectives[0].Broken && cards["rebels"].HasError);
            Assert.That(cards["report"].Tags, Is.EqualTo(new[] { StepTag.AfterAny, StepTag.AnyOne, StepTag.EndsByDefault, StepTag.ShownEarly }));
            Assert.That(cards["report"].Objectives[1].Optional);
            Assert.That(cards["hear"].Tags, Is.EqualTo(new[] { StepTag.ShownEarly }), "steps made in code are shown early unless told otherwise");
            Assert.That(cards["guild"].Problems, Is.Empty);
        }

        [Test]
        public void TheLayout_PutsStepsInColumnsByWhatTheyFollow_AndSurvivesLoops()
        {
            var quest = _kit.Quest("Fork", new[]
            {
                Step("a"), Step("b", After("a")), Step("c", After("a")), Step("d", After("b", "c")),
                Step("x", After("y")), Step("y", After("x")),
            });
            var positions = StepLayout.Arrange(quest, _ => 80f);
            Assert.That(positions["a"].x, Is.EqualTo(StepLayout.Origin.x));
            Assert.That(positions["b"].x, Is.EqualTo(positions["c"].x));
            Assert.That(positions["b"].y, Is.Not.EqualTo(positions["c"].y));
            Assert.That(positions["d"].x, Is.EqualTo(StepLayout.Origin.x + 2 * StepLayout.ColumnWidth));
            Assert.That(positions.Keys, Is.EquivalentTo(new[] { "a", "b", "c", "d", "x", "y" }));
            Assert.That(StepLayout.NeedsLayout(quest));
            quest.Steps[0].SetGraphPosition(new Vector2(10, 10));
            Assert.That(StepLayout.NeedsLayout(quest), Is.False);
        }

        [UnityTest]
        public IEnumerator TheWindow_OpensOnASavedQuest_AndDrawsItsStepsAndLinks()
        {
            if (Application.isBatchMode) Assert.Ignore("Editor windows need a graphics device, which a headless run doesn't have.");
            AssetDatabase.CreateFolder("Assets", "__QuestGraphSmoke");
            var quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.Configure(new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", After("a")), Step("c", After("a")), Step("d", After("b", "c")) }, new QuestSettings { Title = "Smoke" });
            AssetDatabase.CreateAsset(quest, Folder + "/Smoke.asset");
            AssetDatabase.SaveAssets();

            var window = EditorWindow.GetWindow<QuestGraphWindow>();
            try
            {
                QuestGraphWindow.Open(quest);
                for (var i = 0; i < 5; i++) yield return null;
                var root = window.rootVisualElement;
                var screen = root.Q<QuestScreen>();
                Assert.That(screen.Query(className: "quest-graph__node").ToList(), Has.Count.EqualTo(4));
                Assert.That(screen.Q<WireLayer>().Wires, Has.Count.EqualTo(4), "a>b, a>c, b>d, c>d");
                Assert.That(root.Q<QuestList>().Query(className: "quest-graph__list-item--current").ToList(), Has.Count.EqualTo(1));
                Assert.That(EditorUtility.IsDirty(quest), Is.False, "opening a quest never changes it");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
