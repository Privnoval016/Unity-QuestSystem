using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.EventBus;
using Extensions.Ids;
using Extensions.Logic;
using UnityEngine;
using FactSets = Extensions.Logic.Facts;

namespace QuestSystem
{
    /**
     * <summary>Runs quests: their states, progress and flags. It registers the quest and flag facts conditions read,
     * checks only what a change can affect, grants rewards on completion and raises events on the event bus.</summary>
     * <remarks>
     * Main thread only. Create one per game, call <see cref="NewGame" /> or <see cref="Restore" />, and
     * <see cref="Dispose" /> it when the game ends; a disposed service ignores further changes.
     * </remarks>
     */
    public sealed class QuestService : IQuestFacts, IFlagFacts, IDisposable
    {
        #region Private Systems

        private readonly FactRegistry _facts;
        private readonly IAssetResolver _assets;
        private readonly SubjectCache _subjects = new SubjectCache();

        #endregion

        #region Private Fields

        /** <summary>The most changes one settle handles before it gives up, so conditions that keep changing each other can't hang the game.</summary> */
        private const int SettleLimit = 10000;

        private readonly List<QuestRecord> _records = new List<QuestRecord>();

        /** <summary>Keyed by quest, resolving to its runtime record.</summary> */
        private readonly Dictionary<QuestDefinition, QuestRecord> _byQuest = new Dictionary<QuestDefinition, QuestRecord>();

        /** <summary>Keyed by quest asset id, resolving to its runtime record, for restoring.</summary> */
        private readonly Dictionary<AssetId, QuestRecord> _byId = new Dictionary<AssetId, QuestRecord>();

        /** <summary>Keyed by subject, resolving to the quests with a condition or tracker that reads it.</summary> */
        private readonly Dictionary<IConditionSubject, List<QuestRecord>> _readers = new Dictionary<IConditionSubject, List<QuestRecord>>();

        /** <summary>Quests with a condition or tracker that can't say what it reads: checked after every change.</summary> */
        private readonly HashSet<QuestRecord> _readAll = new HashSet<QuestRecord>();

        /** <summary>Keyed by signal, resolving to the objectives that count it.</summary> */
        private readonly Dictionary<QuestSignal, List<ObjectiveRecord>> _counters = new Dictionary<QuestSignal, List<ObjectiveRecord>>();

        /** <summary>Objectives whose counter can't say which signals it counts: offered every signal.</summary> */
        private readonly List<ObjectiveRecord> _countAll = new List<ObjectiveRecord>();

        /** <summary>Keyed by flag, resolving to its value, for flags set this game.</summary> */
        private readonly Dictionary<StoryFlag, int> _flags = new Dictionary<StoryFlag, int>();

        private readonly List<QuestEntry> _unknownQuests = new List<QuestEntry>();
        private readonly List<FlagEntry> _unknownFlags = new List<FlagEntry>();
        private readonly Queue<IConditionSubject> _changes = new Queue<IConditionSubject>();
        private bool _settling;
        private bool _disposed;
        private long _order;

        #endregion

        #region Properties

        /** <summary>Every quest the service runs.</summary> */
        public IReadOnlyList<QuestDefinition> Quests { get; }

        /** <summary>The quest the player follows; null when none.</summary> */
        public QuestDefinition ActiveQuest { get; private set; }

        /** <summary>The facts conditions read, the service's own included.</summary> */
        public IFacts Facts => _facts;

        /** <summary>Raised after a new game or a restore, when every state may have changed at once.</summary> */
        public event Action OnReset = delegate { };

        #endregion

        #region Public API

        /**
         * <summary>Makes a service and registers its facts.</summary>
         * <param name="facts">Where conditions read facts. The service registers its own there and listens for changes.</param>
         * <param name="quests">Every quest in the game.</param>
         * <param name="assets">Finds flags by id when a save is restored.</param>
         * <exception cref="InvalidOperationException">Quest or flag facts are already registered in the registry.</exception>
         */
        public QuestService(FactRegistry facts, IEnumerable<QuestDefinition> quests, IAssetResolver assets)
        {
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            Quests = (quests ?? Enumerable.Empty<QuestDefinition>()).Where(quest => quest != null).Distinct().ToList();
            foreach (var quest in Quests)
                AddRecord(quest);

            _facts.Register<IQuestFacts>(this);
            try
            {
                _facts.Register<IFlagFacts>(this);
            }
            catch
            {
                _facts.Unregister<IQuestFacts>(this);
                throw;
            }
            _facts.OnSubjectChanged += HandleSubjectChanged;
        }

        /** <summary>Unregisters the service's facts and stops listening; later changes are ignored.</summary> */
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _facts.OnSubjectChanged -= HandleSubjectChanged;
            _facts.Unregister<IQuestFacts>(this);
            _facts.Unregister<IFlagFacts>(this);
        }

        /** <inheritdoc /> */
        public QuestState StateOf(QuestDefinition quest) => Find(quest)?.State ?? QuestState.NotGiven;

        /** <inheritdoc /> */
        public StepState StateOf(QuestDefinition quest, string stepId) => Find(quest)?.Find(stepId)?.State ?? StepState.Locked;

        /** <inheritdoc /> */
        public int CompletionsOf(QuestDefinition quest) => Find(quest)?.Completions ?? 0;

        /** <inheritdoc /> */
        public int ProgressOf(QuestDefinition quest, string objectiveId) => Find(quest)?.FindObjective(objectiveId)?.Progress ?? 0;

        /** <inheritdoc /> */
        public int ValueOf(StoryFlag flag) => flag == null ? 0 : _flags.TryGetValue(flag, out var value) ? value : flag.InitialValue;

        /** <summary>When a quest was last given, as a count of quest events this game: higher is more recent; 0 when never.</summary> */
        public long GivenOrderOf(QuestDefinition quest) => Find(quest)?.GivenOrder ?? 0;

        /** <summary>When a quest last ended, as a count of quest events this game: higher is more recent; 0 when never.</summary> */
        public long EndedOrderOf(QuestDefinition quest) => Find(quest)?.EndedOrder ?? 0;

        /** <summary>True when a quest can be given now: never given, or repeatable and ended, and available.</summary> */
        public bool IsAvailable(QuestDefinition quest)
        {
            var record = Find(quest);
            if (record == null) return false;
            var givable = record.State == QuestState.NotGiven || (quest.Repeatable && record.State != QuestState.Active);
            return givable && (quest.AvailableWhen == null || quest.AvailableWhen.Evaluate(_facts));
        }

        /** <summary>True when players may see the quest: it was given, or it isn't hidden until then.</summary> */
        public bool IsVisible(QuestDefinition quest) => quest != null && (!quest.HiddenUntilGiven || StateOf(quest) != QuestState.NotGiven);

        /** <summary>True when players may see the step: it started, or it isn't hidden until then.</summary> */
        public bool IsVisible(QuestDefinition quest, QuestStep step) => step != null && (!step.HiddenUntilReached || StateOf(quest, step.Id) != StepState.Locked);

        /** <summary>Gives a quest, if it is available.</summary> <returns>True when it was given.</returns> */
        public bool Give(QuestDefinition quest)
        {
            if (_disposed || !IsAvailable(quest)) return false;
            var record = _byQuest[quest];
            WhileSettling(() =>
            {
                record.Start(++_order);
                EventBus<QuestGivenEvent>.Raise(new QuestGivenEvent(this, quest));
                CheckSafely(record, null);
                Changed(quest);
            });
            return true;
        }

        /** <summary>Sets the quest the player follows: an active quest, or null for none.</summary> <returns>False when the quest isn't active.</returns> */
        public bool SetActive(QuestDefinition quest)
        {
            if (_disposed || (quest != null && StateOf(quest) != QuestState.Active)) return false;
            if (ActiveQuest == quest) return true;
            ActiveQuest = quest;
            EventBus<ActiveQuestChangedEvent>.Raise(new ActiveQuestChangedEvent(this, quest));
            return true;
        }

        /**
         * <summary>Counts a signal towards every active objective that counts it.</summary>
         * <param name="signal">What happened.</param>
         * <param name="amount">How much of it.</param>
         * <param name="payload">What the signal says about the moment, such as which foe fell (<c>Facts.Of(foe)</c>). A
         * signal tracker's condition reads it before the world's facts.</param>
         */
        public void Raise(QuestSignal signal, int amount = 1, IFacts payload = null)
        {
            if (_disposed || signal == null || amount <= 0) return;
            var context = FactSets.Layered(payload, _facts);
            WhileSettling(() =>
            {
                foreach (var objective in CountersOf(signal))
                {
                    var record = objective.Step.Quest;
                    if (record.State != QuestState.Active || objective.Step.State != StepState.Active) continue;
                    var added = objective.Counter.ProgressFrom(signal, amount, context);
                    if (added <= 0) continue;
                    SetProgress(record, objective, (long)objective.Progress + added);
                    TryComplete(record, objective.Step);
                }
            });
        }

        /** <summary>Sets a story flag's value.</summary> */
        public void SetFlag(StoryFlag flag, int value)
        {
            if (_disposed || flag == null) return;
            var previous = ValueOf(flag);
            _flags[flag] = value;
            if (previous == value) return;
            EventBus<FlagChangedEvent>.Raise(new FlagChangedEvent(this, flag, value));
            Changed(flag);
        }

        /** <summary>Adds to a story flag's value, stopping at the largest and smallest whole numbers.</summary> */
        public void AddToFlag(StoryFlag flag, int amount) => SetFlag(flag, (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)ValueOf(flag) + amount)));

        /** <summary>Clears every quest and flag, then gives the quests that start by themselves.</summary> */
        public void NewGame()
        {
            if (_disposed) return;
            Clear();
            CheckAll();
            OnReset();
        }

        /** <summary>Captures every quest and flag as plain data.</summary> */
        public QuestSaveData Capture()
        {
            var data = new QuestSaveData { ActiveQuest = ActiveQuest != null && !ActiveQuest.AssetId.IsEmpty ? ActiveQuest.AssetId.Value : null };
            foreach (var record in _records)
                if (!record.Definition.AssetId.IsEmpty && (record.State != QuestState.NotGiven || record.Completions > 0)) data.Quests.Add(record.ToEntry());
            data.Quests.AddRange(_unknownQuests);
            foreach (var pair in _flags)
                if (!pair.Key.AssetId.IsEmpty) data.Flags.Add(new FlagEntry { Flag = pair.Key.AssetId.Value, Value = pair.Value });
            data.Flags.AddRange(_unknownFlags);
            return data;
        }

        /**
         * <summary>Restores quests and flags from saved data. Quests and flags this game no longer has are kept and saved
         * again. Afterwards, anything the save didn't know about (a new step, a newly available quest) catches up.</summary>
         */
        public void Restore(QuestSaveData data)
        {
            if (_disposed) return;
            Clear();
            if (data != null)
            {
                foreach (var entry in data.Flags ?? new List<FlagEntry>())
                    RestoreFlag(entry);
                foreach (var entry in data.Quests ?? new List<QuestEntry>())
                    RestoreQuest(entry);
                ActiveQuest = AssetId.TryParse(data.ActiveQuest, out var activeId) && _byId.TryGetValue(activeId, out var active)
                    && active.State == QuestState.Active ? active.Definition : null;
            }
            CheckAll();
            OnReset();
        }

        #endregion

        #region Event Handlers

        private void HandleSubjectChanged(IConditionSubject subject)
        {
            _changes.Enqueue(subject);
            Settle();
        }

        #endregion

        #region Helpers

        private QuestRecord Find(QuestDefinition quest) => quest != null && _byQuest.TryGetValue(quest, out var record) ? record : null;

        private void Changed(IConditionSubject subject) => _facts.NotifyChanged(subject);

        private void AddRecord(QuestDefinition quest)
        {
            var record = new QuestRecord(quest);
            _records.Add(record);
            _byQuest.Add(quest, record);
            if (!quest.AssetId.IsEmpty && !_byId.ContainsKey(quest.AssetId)) _byId.Add(quest.AssetId, record);
            IndexReads(record);
            IndexCounters(record);
        }

        private void IndexReads(QuestRecord record)
        {
            var quest = record.Definition;
            var subjects = new HashSet<IConditionSubject>();
            var known = ConditionSubjects.TryCollect(quest.AvailableWhen, subjects) & ConditionSubjects.TryCollect(quest.FailWhen, subjects);
            foreach (var step in record.Steps)
                known &= ConditionSubjects.TryCollect(step.Definition.UnlockWhen, subjects) & ConditionSubjects.TryCollect(step.Definition.FailWhen, subjects);
            foreach (var objective in record.Objectives)
                if (objective.State != null) known &= objective.Definition.Tracker.TryCollectSubjects(subjects);
            if (!known) _readAll.Add(record);
            foreach (var subject in subjects)
            {
                if (!_readers.TryGetValue(subject, out var readers)) _readers[subject] = readers = new List<QuestRecord>();
                readers.Add(record);
            }
        }

        private void IndexCounters(QuestRecord record)
        {
            foreach (var objective in record.Objectives)
            {
                if (objective.Counter == null) continue;
                var signals = new HashSet<QuestSignal>();
                if (!objective.Counter.TryCollectSignals(signals))
                {
                    _countAll.Add(objective);
                    continue;
                }
                foreach (var signal in signals)
                {
                    if (!_counters.TryGetValue(signal, out var counters)) _counters[signal] = counters = new List<ObjectiveRecord>();
                    counters.Add(objective);
                }
            }
        }

        /** <summary>The objectives counting now, chosen before any progress lands, so a step a signal unlocks doesn't count that same signal.</summary> */
        private List<ObjectiveRecord> CountersOf(QuestSignal signal)
        {
            var counters = new List<ObjectiveRecord>();
            AddCounting(_countAll, counters);
            if (_counters.TryGetValue(signal, out var counting)) AddCounting(counting, counters);
            return counters;
        }

        private static void AddCounting(List<ObjectiveRecord> objectives, List<ObjectiveRecord> counting)
        {
            foreach (var objective in objectives)
                if (objective.Step.Quest.State == QuestState.Active && objective.Step.State == StepState.Active) counting.Add(objective);
        }

        private List<QuestRecord> ReadersOf(IConditionSubject subject)
        {
            var readers = new List<QuestRecord>(_readAll);
            if (_readers.TryGetValue(subject, out var reading))
                foreach (var record in reading)
                    if (!_readAll.Contains(record)) readers.Add(record);
            return readers;
        }

        private void Settle()
        {
            if (_settling || _disposed) return;
            _settling = true;
            try
            {
                var handled = 0;
                while (_changes.Count > 0)
                {
                    if (++handled > SettleLimit)
                    {
                        Debug.LogError("Quests: changes keep causing changes; stopped settling.");
                        _changes.Clear();
                        break;
                    }
                    var subject = _changes.Dequeue();
                    foreach (var record in ReadersOf(subject))
                        CheckSafely(record, subject);
                }
            }
            finally
            {
                _settling = false;
            }
        }

        private void CheckAll() => WhileSettling(() =>
        {
            foreach (var record in _records)
                CheckSafely(record, null);
        });

        /** <summary>Runs a change with settling held, so what it causes is queued, then settles once it is done.</summary> */
        private void WhileSettling(Action change)
        {
            var wasSettling = _settling;
            _settling = true;
            try
            {
                change();
            }
            finally
            {
                _settling = wasSettling;
            }
            Settle();
        }

        private void CheckSafely(QuestRecord record, IConditionSubject subject)
        {
            try
            {
                Check(record, subject);
            }
            catch (Exception e)
            {
                Debug.LogError($"Quests: checking {record.Definition.name} failed: {e.Message}");
            }
        }

        private bool Reads(ICondition<IFacts> condition, IConditionSubject subject) => condition != null && (subject == null || _subjects.Reads(condition, subject));

        private bool Reads(ObjectiveTracker tracker, IConditionSubject subject) => tracker != null && (subject == null || _subjects.Reads(tracker, subject));

        private void Check(QuestRecord record, IConditionSubject subject)
        {
            var quest = record.Definition;
            if (record.State == QuestState.NotGiven)
            {
                if (quest.GivenAutomatically && (quest.AvailableWhen == null ? subject == null : Reads(quest.AvailableWhen, subject))) Give(quest);
                return;
            }
            if (record.State != QuestState.Active) return;

            AdvanceSteps(record, subject);
            if (record.State != QuestState.Active) return;
            if (subject == null && (record.Steps.Count == 0 || record.HasEnded()))
            {
                Complete(record);
                return;
            }
            CheckFailure(record, subject);
        }

        private void AdvanceSteps(QuestRecord record, IConditionSubject subject)
        {
            foreach (var step in record.Steps)
            {
                if (record.State != QuestState.Active) return;
                if (step.State == StepState.Locked && ShouldUnlock(record, step, subject)) Unlock(record, step);
                if (step.State != StepState.Active) continue;
                foreach (var objective in step.Objectives)
                    if (objective.State != null && Reads(objective.Definition.Tracker, subject)) SetProgress(record, objective, objective.State.ProgressNow(_facts));
                TryComplete(record, step);
            }
        }

        private void CheckFailure(QuestRecord record, IConditionSubject subject)
        {
            var quest = record.Definition;
            if (Reads(quest.FailWhen, subject) && quest.FailWhen.Evaluate(_facts))
            {
                Fail(record);
                return;
            }
            foreach (var step in record.Steps)
            {
                if (step.State != StepState.Active || !Reads(step.Definition.FailWhen, subject) || !step.Definition.FailWhen.Evaluate(_facts)) continue;
                step.State = StepState.Failed;
                EventBus<StepChangedEvent>.Raise(new StepChangedEvent(this, quest, step.Definition, StepState.Failed));
                Fail(record);
                return;
            }
        }

        private bool ShouldUnlock(QuestRecord record, StepRecord step, IConditionSubject subject)
        {
            var unlockWhen = step.Definition.UnlockWhen;
            if (unlockWhen != null) return Reads(unlockWhen, subject) && unlockWhen.Evaluate(_facts);
            if (subject != null) return false;
            var before = record.Before(step);
            return before == null || before.State == StepState.Done;
        }

        private void Unlock(QuestRecord record, StepRecord step)
        {
            step.State = StepState.Active;
            EventBus<StepChangedEvent>.Raise(new StepChangedEvent(this, record.Definition, step.Definition, StepState.Active));
            foreach (var objective in step.Objectives)
                if (objective.State != null) SetProgress(record, objective, objective.State.ProgressNow(_facts));
            Changed(record.Definition);
            TryComplete(record, step);
        }

        private void SetProgress(QuestRecord record, ObjectiveRecord objective, long value)
        {
            var clamped = (int)Math.Max(0, Math.Min(objective.Definition.TargetCount, value));
            if (clamped == objective.Progress) return;
            objective.Progress = clamped;
            EventBus<ObjectiveProgressedEvent>.Raise(new ObjectiveProgressedEvent(this, record.Definition, objective.Definition, clamped));
            Changed(record.Definition);
        }

        private void TryComplete(QuestRecord record, StepRecord step)
        {
            if (record.State != QuestState.Active || step.State != StepState.Active || !step.IsComplete) return;
            step.State = StepState.Done;
            EventBus<StepChangedEvent>.Raise(new StepChangedEvent(this, record.Definition, step.Definition, StepState.Done));
            Changed(record.Definition);

            if (record.EndsWith(step))
            {
                Complete(record);
                return;
            }
            var next = record.After(step);
            if (next != null && next.State == StepState.Locked && next.Definition.UnlockWhen == null) Unlock(record, next);
        }

        private void Complete(QuestRecord record)
        {
            if (record.State != QuestState.Active) return;
            record.State = QuestState.Done;
            record.Completions++;
            record.EndedOrder = ++_order;
            foreach (var reward in record.Definition.Rewards)
            {
                try
                {
                    reward?.Grant(_facts);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Quests: a reward of {record.Definition.name} failed: {e.Message}");
                }
            }
            EventBus<QuestEndedEvent>.Raise(new QuestEndedEvent(this, record.Definition, QuestState.Done, record.Completions));
            Ended(record);
        }

        private void Fail(QuestRecord record)
        {
            if (record.State != QuestState.Active) return;
            record.State = QuestState.Failed;
            record.EndedOrder = ++_order;
            EventBus<QuestEndedEvent>.Raise(new QuestEndedEvent(this, record.Definition, QuestState.Failed, record.Completions));
            Ended(record);
        }

        private void Ended(QuestRecord record)
        {
            if (ActiveQuest == record.Definition) SetActive(null);
            Changed(record.Definition);
        }

        private void Clear()
        {
            foreach (var record in _records)
                record.Reset();
            _flags.Clear();
            _unknownQuests.Clear();
            _unknownFlags.Clear();
            _changes.Clear();
            _order = 0;
            ActiveQuest = null;
        }

        private void RestoreFlag(FlagEntry entry)
        {
            if (entry == null) return;
            if (AssetId.TryParse(entry.Flag, out var id) && _assets.TryFind(id, out var asset) && asset is StoryFlag flag)
                _flags[flag] = entry.Value;
            else
                _unknownFlags.Add(entry);
        }

        private void RestoreQuest(QuestEntry entry)
        {
            if (entry == null) return;
            if (!AssetId.TryParse(entry.Quest, out var id) || !_byId.TryGetValue(id, out var record))
            {
                _unknownQuests.Add(entry);
                return;
            }
            record.Restore(entry);
            _order = Math.Max(_order, Math.Max(record.GivenOrder, record.EndedOrder));
        }

        #endregion
    }
}
