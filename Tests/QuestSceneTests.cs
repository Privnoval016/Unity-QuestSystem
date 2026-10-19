using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static QuestSystem.Tests.Build;
using Object = UnityEngine.Object;

namespace QuestSystem.Tests
{
    /** <summary>Scene triggers raising signals, and reactions following quest state.</summary> */
    public sealed class QuestSceneTests
    {
        private QuestKit _kit;
        private QuestRuntime _runtime;
        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _kit = new QuestKit();
            _runtime = ScriptableObject.CreateInstance<QuestRuntime>();
            _made.Add(_runtime);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
            _made.Clear();
            _kit.Dispose();
        }

        private T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _made.Add(go);
            return go.AddComponent<T>();
        }

        private static void Call(Component component, string message) =>
            component.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

        private static void Call(Component component, string message, Collider collider) =>
            component.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, new object[] { collider });

        private sealed class OnlyNamed : IQuestTriggerFilter
        {
            public bool Accepts(Collider other) => other.name == "Player";
        }

        [Test]
        public void ATrigger_RaisesItsSignals_ForWhatCounts()
        {
            var reached = _kit.Signal("Reached");
            var left = _kit.Signal("Left");
            var quest = _kit.Quest("Q", new[] { Step("s", Count("in", reached, 2), Count("out", left)) });
            var service = _kit.Start();
            _runtime.Bind(service);
            service.Give(quest);
            var trigger = Make<QuestTrigger>("Gate");
            trigger.Configure(_runtime, reached, left, new OnlyNamed());
            var player = Make<BoxCollider>("Player");
            var dog = Make<BoxCollider>("Dog");

            Call(trigger, "OnTriggerEnter", dog);
            Call(trigger, "OnTriggerEnter", player);
            Call(trigger, "OnTriggerExit", player);
            Assert.That(service.ProgressOf(quest, "in"), Is.EqualTo(1));
            Assert.That(service.ProgressOf(quest, "out"), Is.EqualTo(1));
            Assert.That(trigger.Counts(null), Is.False);
        }

        [Test]
        public void ATriggerWithNoService_WarnsAndDoesNothing()
        {
            var trigger = Make<QuestTrigger>("Gate");
            trigger.Configure(_runtime, _kit.Signal("Reached"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no running quest service"));
            Call(trigger, "OnTriggerEnter", Make<BoxCollider>("Player"));
        }

        [Test]
        public void AReaction_FollowsQuestState_AndUndoesItself()
        {
            var a = _kit.Signal("A");
            var quest = _kit.Quest("Smith", new[] { Step("s", Count("x", a)) });
            var gate = new GameObject("Gate");
            _made.Add(gate);
            var reaction = Make<QuestReaction>("Reaction");
            reaction.Configure(_runtime, new QuestStateCondition(quest, QuestState.Active), new IQuestReactionEffect[] { new SetActiveEffect(gate, activeWhenOn: false) });
            var service = _kit.Start();
            _runtime.Bind(service);
            Call(reaction, "OnEnable");
            Assert.That(gate.activeSelf);

            service.Give(quest);
            Assert.That(reaction.IsOn);
            Assert.That(gate.activeSelf, Is.False);
            service.Raise(a);
            Assert.That(reaction.IsOn, Is.False);
            Assert.That(gate.activeSelf);
        }

        [Test]
        public void AReaction_IsReappliedWhenASaveLoads_AndWhenTheServiceIsBoundLater()
        {
            var flag = _kit.Flag("Opened");
            var light = Make<Light>("Lamp");
            var reaction = Make<QuestReaction>("Reaction");
            reaction.Configure(_runtime, new FlagCondition(flag), new IQuestReactionEffect[] { new EnableBehaviourEffect(light) });
            Call(reaction, "OnEnable");
            Assert.That(reaction.IsOn, Is.False);

            var service = _kit.Start();
            service.SetFlag(flag, 1);
            var saved = service.Capture();
            service.NewGame();
            _runtime.Bind(service);
            Assert.That(light.enabled, Is.False);

            service.Restore(saved);
            Assert.That(reaction.IsOn);
            Assert.That(light.enabled);
            Call(reaction, "OnDisable");
            service.SetFlag(flag, 0);
            Assert.That(light.enabled, "a disabled reaction stops listening");
        }

        [Test]
        public void TheRuntime_ForgetsItsServiceBetweenSessions_AndUnbindsOnlyItsOwn()
        {
            var service = _kit.Start();
            _runtime.Bind(service);
            _runtime.Unbind(null);
            Assert.That(_runtime.Service, Is.EqualTo(service));
            _runtime.Unbind(service);
            Assert.That(_runtime.Service, Is.Null);
            _runtime.Bind(service);
            typeof(QuestRuntime).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_runtime, null);
            Assert.That(_runtime.Service, Is.Null);
        }
    }
}
