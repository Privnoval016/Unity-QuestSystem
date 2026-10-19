using System.Linq;
using Extensions.EditorTools;
using Extensions.Ids;
using NUnit.Framework;
using QuestSystem.EditorTools;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Quest validation: setup that can never work is an error; doubtful setup a warning.</summary> */
    public sealed class QuestValidatorTests
    {
        private QuestKit _kit;

        [SetUp]
        public void SetUp() => _kit = new QuestKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static string[] Problems(QuestDefinition quest, QuestIssueLevel level) =>
            QuestValidator.Validate(quest).Where(issue => issue.Level == level).Select(issue => issue.Problem).ToArray();

        [Test]
        public void AWellMadeQuest_HasNoErrors()
        {
            var quest = _kit.Quest("Good", new[] { Step("s", Count("x", _kit.Signal("A"))) }, new QuestSettings { Title = "Good" });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Is.Empty);
            Assert.That(Problems(quest, QuestIssueLevel.Warning), Is.EqualTo(new[] { "has no kind" }));
        }

        [Test]
        public void ObjectivesThatCanNeverProgress_AreErrors()
        {
            var quest = _kit.Quest("Broken", new[]
            {
                Step("s", new QuestObjective("none", null), new QuestObjective("nosignal", new SignalTracker(null)),
                    new QuestObjective("noflag", new FlagTracker(null)), new QuestObjective("nocondition", new ConditionTracker(null))),
            });
            var errors = Problems(quest, QuestIssueLevel.Error);
            Assert.That(errors, Has.Length.EqualTo(4));
            Assert.That(errors, Has.Some.Contains("no tracker"));
            Assert.That(errors, Has.Some.Contains("no signal"));
            Assert.That(errors, Has.Some.Contains("no flag"));
            Assert.That(errors, Has.Some.Contains("no condition"));
        }

        [Test]
        public void AStepWaitingOnItself_AndAQuestWithNoSteps_AreErrors()
        {
            var quest = _kit.Quest("Loop", new QuestStep[0]);
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.Some.Contains("no steps"));

            quest.Configure(new[] { Step("a", Count("x", _kit.Signal("A"))), Step("b", new StepSettings { UnlockWhen = new StepStateCondition(quest, "b") }, Count("y", _kit.Signal("B"))) });
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.Some.Contains("never starts"));
        }

        [Test]
        public void AQuestWithNoAssetId_IsAnError()
        {
            var quest = _kit.Quest("NoId", new[] { Step("s", Count("x", _kit.Signal("A"))) }, new QuestSettings { Title = "NoId" });
            IdentifiedAssetDatabase.WriteId(quest, default(AssetId));
            Assert.That(Problems(quest, QuestIssueLevel.Error), Has.Some.Contains("no asset id"));
        }

        [Test]
        public void EmptyStepsAndMissingTitles_AreWarnings()
        {
            var quest = _kit.Quest("Thin", new[] { Step("s") });
            var warnings = Problems(quest, QuestIssueLevel.Warning);
            Assert.That(warnings, Has.Some.Contains("no objectives"));
            Assert.That(warnings, Has.Some.Contains("no title"));
            Assert.That(QuestValidator.Validate(null), Is.Empty);
        }

        [Test]
        public void MissingAndRepeatedIds_AreFixed()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Ids", new[] { Step("", Count("same", a)), Step("dup", Count("same", a)), Step("dup", Count("", a)) });
            Assert.That(quest.AssignMissingIds());
            var ids = quest.Steps.Select(step => step.Id).Concat(quest.Steps.SelectMany(step => step.Objectives).Select(objective => objective.Id)).ToList();
            Assert.That(ids.All(id => !string.IsNullOrEmpty(id)));
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
            Assert.That(quest.Steps[1].Id, Is.EqualTo("dup"));
            Assert.That(quest.AssignMissingIds(), Is.False);
        }
    }
}
