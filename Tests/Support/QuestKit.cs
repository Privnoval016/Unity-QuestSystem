using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.EditorTools;
using Extensions.EventBus;
using Extensions.Ids;
using Extensions.Logic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem.Tests
{
    /** <summary>Builds quests, flags and signals in memory for a test, runs a service over them, records events, and cleans up.</summary> */
    public sealed class QuestKit : IDisposable
    {
        private readonly List<Object> _made = new List<Object>();
        private readonly List<Action> _undo = new List<Action>();

        public FactRegistry Facts { get; } = new FactRegistry();
        public FakeResolver Assets { get; } = new FakeResolver();
        public List<string> Events { get; } = new List<string>();
        public List<QuestDefinition> Quests { get; } = new List<QuestDefinition>();
        public QuestService Service { get; private set; }

        public QuestKit()
        {
            Listen<QuestGivenEvent>(e => $"given {e.Quest.name}");
            Listen<StepChangedEvent>(e => $"step {e.Step.Id} {e.State}");
            Listen<ObjectiveProgressedEvent>(e => $"progress {e.Objective.Id} {e.Progress}");
            Listen<QuestEndedEvent>(e => $"{e.State} {e.Quest.name} x{e.Completions}");
            Listen<FlagChangedEvent>(e => $"flag {e.Flag.name} {e.Value}");
            Listen<ActiveQuestChangedEvent>(e => $"active {(e.Quest == null ? "none" : e.Quest.name)}");
        }

        public StoryFlag Flag(string name, int initial = 0)
        {
            var flag = Make<StoryFlag>(name);
            var serialized = new SerializedObject(flag);
            serialized.FindProperty("initialValue").intValue = initial;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return flag;
        }

        public QuestSignal Signal(string name) => Make<QuestSignal>(name);

        public QuestDefinition Quest(string name, IEnumerable<QuestStep> steps, QuestSettings settings = default)
        {
            var quest = Make<QuestDefinition>(name);
            quest.Configure(steps, settings);
            Quests.Add(quest);
            return quest;
        }

        public QuestService Start(bool newGame = true)
        {
            Service?.Dispose();
            Service = new QuestService(Facts, Quests, Assets);
            if (newGame) Service.NewGame();
            return Service;
        }

        public void Dispose()
        {
            Service?.Dispose();
            foreach (var undo in _undo) undo();
            foreach (var made in _made)
                if (made != null) Object.DestroyImmediate(made);
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var made = ScriptableObject.CreateInstance<T>();
            made.name = name;
            _made.Add(made);
            if (made is IIdentifiedAsset)
            {
                IdentifiedAssetDatabase.WriteId(made, AssetId.New());
                Assets.Add(made);
            }
            return made;
        }

        private void Listen<T>(Func<T, string> describe) where T : IEvent
        {
            var binding = new EventBinding<T>(e => Events.Add(describe(e)));
            EventBus<T>.Register(binding);
            _undo.Add(() => EventBus<T>.Deregister(binding));
        }
    }

    /** <summary>Finds test assets by id.</summary> */
    public sealed class FakeResolver : IAssetResolver
    {
        private readonly Dictionary<AssetId, Object> _assets = new Dictionary<AssetId, Object>();

        public void Add(Object asset) => _assets[AssetIds.IdOf(asset)] = asset;

        public void Remove(Object asset) => _assets.Remove(AssetIds.IdOf(asset));

        public bool TryFind(AssetId id, out Object asset) => _assets.TryGetValue(id, out asset);
    }

    /** <summary>A reward that records what it was given.</summary> */
    [Serializable]
    public sealed class RecordingReward : IQuestReward
    {
        public string Name;
        public List<string> Log;
        public bool Throws;

        public string Describe() => Name;

        public void Grant(IFacts facts)
        {
            if (Throws) throw new InvalidOperationException("reward broke");
            Log.Add(Name);
        }
    }

    /** <summary>A condition that counts how often it is checked, reading one flag.</summary> */
    [Serializable]
    public sealed class CountingCondition : ICondition<IFacts>, ISubjectReader
    {
        public StoryFlag Flag;
        public bool SaysWhatItReads = true;
        public int Checks;

        public bool Evaluate(IFacts context)
        {
            Checks++;
            return context.TryGet<IFlagFacts>(out var flags) && flags.ValueOf(Flag) > 0;
        }

        public bool TryCollectSubjects(ICollection<IConditionSubject> subjects)
        {
            subjects.Add(Flag);
            return SaysWhatItReads;
        }
    }

    /** <summary>Shorthands for building steps and objectives.</summary> */
    public static class Build
    {
        public static QuestObjective Count(string id, QuestSignal signal, int target = 1, bool optional = false) =>
            new QuestObjective(id, new SignalTracker(signal), target, optional);

        public static QuestStep Step(string id, params QuestObjective[] objectives) => new QuestStep(id, objectives);

        public static QuestStep Step(string id, StepSettings settings, params QuestObjective[] objectives) => new QuestStep(id, objectives, settings);
    }
}
