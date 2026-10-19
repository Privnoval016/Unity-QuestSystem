using System;
using System.Collections.Generic;
using System.Linq;
using Extensions.EventBus;
using Extensions.Ids;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /**
     * <summary>Runs quests: their states, progress and flags. It registers the quest and flag facts conditions read,
     * checks only what a change can affect, grants rewards on completion and raises events on the event bus.</summary>
     * <remarks>
     * Main thread only. Create one per game, call <see cref="NewGame" /> or <see cref="Restore" />, and
     * <see cref="Dispose" /> it when the game ends.
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

        private const int MaxChangesPerSettle = 10000;

        private readonly List<QuestRecord> _records = new List<QuestRecord>();

        /** <summary>Keyed by quest, resolving to its runtime record.</summary> */
        private readonly Dictionary<QuestDefinition, QuestRecord> _byQuest = new Dictionary<QuestDefinition, QuestRecord>();

        /** <summary>Keyed by flag, resolving to its value, for flags set this game.</summary> */
        private readonly Dictionary<StoryFlag, int> _flags = new Dictionary<StoryFlag, int>();

        private readonly List<QuestEntry> _unknownQuests = new List<QuestEntry>();
        private readonly List<FlagEntry> _unknownFlags = new List<FlagEntry>();
        private readonly Queue<IConditionSubject> _changes = new Queue<IConditionSubject>();

        /** <summary>Keyed by subject, resolving to the quests with a condition or tracker that reads it.</summary> */
        private readonly Dictionary<IConditionSubject, List<QuestRecord>> _readers = new Dictionary<IConditionSubject, List<QuestRecord>>();

        /** <summary>Quests with a condition or tracker that can't say what it reads: checked after every change.</summary> */
        private readonly List<QuestRecord> _readAll = new List<QuestRecord>();
        private bool _settling;

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
         */
        public QuestService(FactRegistry facts, IEnumerable<QuestDefinition> quests, IAssetResolver assets)
        {
            _facts = facts ?? throw new ArgumentNullException(nameof(facts));
            _assets = assets;
            Quests = (quests ?? Enumerable.Empty<QuestDefinition>()).Where(quest => quest != null).Distinct().ToList();
            foreach (var quest in Quests)
            {
                var record = new QuestRecord(quest);
                _records.Add(record);
                _byQuest.Add(quest, record);
            }
            foreach (var record in _records)
                IndexReads(record);
            _facts.Register<IQuestFacts>(this);
            _facts.Register<IFlagFacts>(this);
            _facts.OnSubjectChanged += HandleSubjectChanged;
        }

        /** <summary>Unregisters the service's facts and stops listening.</summary> */
        public void Dispose()
        {
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
        public int ValueOf(StoryFlag flag) => flag == null ? 0 : _flags.TryGetValue(flag, out var value) ? value : flag.InitialValue;

        /** <summary>An objective's progress; 0 when unknown.</summary> */
        public int ProgressOf(QuestDefinition quest, string objectiveId) => Find(quest)?.FindObjective(objectiveId)?.Progress ?? 0;

        /** <summary>True when a quest can be given now.</summary> */
        public bool IsAvailable(QuestDefinition quest)
        {
            var record = Find(quest);
            if (record == null) return false;
            var givable = record.State == QuestState.NotGiven || (record.State == QuestState.Done && quest.Repeatable);
            return givable && (quest.AvailableWhen == null || quest.AvailableWhen.Evaluate(_facts));
        }

        /** <summary>True when players may see the quest: it was given, or it isn't hidden until then.</summary> */
        public bool IsVisible(QuestDefinition quest) => quest != null && (!quest.HiddenUntilGiven || StateOf(quest) != QuestState.NotGiven);

        /** <summary>True when players may see the step: it started, or it isn't hidden until then.</summary> */
        public bool IsVisible(QuestDefinition quest, QuestStep step) => step != null && (!step.HiddenUntilReached || StateOf(quest, step.Id) != StepState.Locked);

        /** <summary>Gives a quest, if it is available.</summary> <returns>True when it was given.</returns> */
        public bool Give(QuestDefinition quest)
        {
            if (!IsAvailable(quest)) return false;
            var record = _byQuest[quest];
            record.Start();
            EventBus<QuestGivenEvent>.Raise(new QuestGivenEvent(quest));
            if (record.Steps.Count == 0) Complete(record);
            else Check(record, null);
            Changed(quest);
            return true;
        }

        /** <summary>Sets the quest the player follows: an active quest, or null for none.</summary> <returns>False when the quest isn't active.</returns> */
        public bool SetActive(QuestDefinition quest)
        {
            if (quest != null && StateOf(quest) != QuestState.Active) return false;
            if (ActiveQuest == quest) return true;
            ActiveQuest = quest;
            EventBus<ActiveQuestChangedEvent>.Raise(new ActiveQuestChangedEvent(quest));
            return true;
        }

        /** <summary>Counts a signal towards every active objective that counts it.</summary> */
        public void Raise(QuestSignal signal, int amount = 1)
        {
            if (signal == null || amount <= 0) return;
            foreach (var record in _records.Where(record => record.State == QuestState.Active).ToList())
            {
                foreach (var step in record.Steps.Where(step => step.State == StepState.Active).ToList())
                {
                    foreach (var objective in step.Objectives)
                    {
                        var added = objective.Definition.Tracker?.ProgressFrom(signal, amount) ?? 0;
                        if (added > 0) SetProgress(record, objective, (long)objective.Progress + added);
                    }
                    TryComplete(record, step);
                }
            }
            Settle();
        }

        /** <summary>Sets a story flag's value.</summary> */
        public void SetFlag(StoryFlag flag, int value)
        {
            if (flag == null) return;
            var previous = ValueOf(flag);
            _flags[flag] = value;
            if (previous == value) return;
            EventBus<FlagChangedEvent>.Raise(new FlagChangedEvent(flag, value));
            Changed(flag);
        }

        /** <summary>Adds to a story flag's value.</summary> */
        public void AddToFlag(StoryFlag flag, int amount) => SetFlag(flag, ValueOf(flag) + amount);

        /** <summary>Clears every quest and flag, then gives the quests that start by themselves.</summary> */
        public void NewGame()
        {
            Clear();
            CheckAll();
            OnReset();
        }

        /** <summary>Captures every quest and flag as plain data.</summary> */
        public QuestSaveData Capture()
        {
            var data = new QuestSaveData { ActiveQuest = IdOf(ActiveQuest) };
            foreach (var record in _records)
                if (record.State != QuestState.NotGiven || record.Completions > 0) data.Quests.Add(record.ToEntry());
            data.Quests.AddRange(_unknownQuests);
            foreach (var pair in _flags)
                if (TryIdOf(pair.Key, out var id)) data.Flags.Add(new FlagEntry { Flag = id, Value = pair.Value });
            data.Flags.AddRange(_unknownFlags);
            return data;
        }

        /**
         * <summary>Restores quests and flags from saved data. Quests and flags this game no longer has are kept and saved
         * again. Afterwards, anything the save didn't know about (a new step, a newly available quest) catches up.</summary>
         */
        public void Restore(QuestSaveData data)
        {
            Clear();
            if (data != null)
            {
                foreach (var entry in data.Flags ?? new List<FlagEntry>())
                    RestoreFlag(entry);
                foreach (var entry in data.Quests ?? new List<QuestEntry>())
                    RestoreQuest(entry);
                var active = _records.FirstOrDefault(record => record.State == QuestState.Active && IdOf(record.Definition) == data.ActiveQuest);
                ActiveQuest = active?.Definition;
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

        private void Settle()
        {
            if (_settling) return;
            _settling = true;
            try
            {
                var handled = 0;
                while (_changes.Count > 0)
                {
                    if (++handled > MaxChangesPerSettle)
                    {
                        Debug.LogError("Quests: changes keep causing changes; stopped settling.");
                        _changes.Clear();
                        break;
                    }
                    var subject = _changes.Dequeue();
                    foreach (var record in ReadersOf(subject))
                        Check(record, subject);
                }
            }
            finally
            {
                _settling = false;
            }
        }

        private void CheckAll()
        {
            foreach (var record in _records.ToList())
                Check(record, null);
            Settle();
        }

        private void IndexReads(QuestRecord record)
        {
            var quest = record.Definition;
            var subjects = new HashSet<IConditionSubject>();
            var known = ConditionSubjects.TryCollect(quest.AvailableWhen, subjects) & ConditionSubjects.TryCollect(quest.FailWhen, subjects);
            foreach (var step in record.Steps)
            {
                known &= ConditionSubjects.TryCollect(step.Definition.UnlockWhen, subjects) & ConditionSubjects.TryCollect(step.Definition.FailWhen, subjects);
                foreach (var objective in step.Objectives)
                    known &= objective.Definition.Tracker?.TryCollectSubjects(subjects) ?? true;
            }
            if (!known) _readAll.Add(record);
            foreach (var subject in subjects)
            {
                if (!_readers.TryGetValue(subject, out var readers)) _readers[subject] = readers = new List<QuestRecord>();
                readers.Add(record);
            }
        }

        private List<QuestRecord> ReadersOf(IConditionSubject subject)
        {
            var readers = new List<QuestRecord>(_readAll);
            if (_readers.TryGetValue(subject, out var reading))
                foreach (var record in reading)
                    if (!_readAll.Contains(record)) readers.Add(record);
            return readers;
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

            foreach (var step in record.Steps)
            {
                if (record.State != QuestState.Active) return;
                if (step.State == StepState.Locked && ShouldUnlock(record, step, subject)) Unlock(record, step);
                if (step.State != StepState.Active) continue;
                foreach (var objective in step.Objectives)
                {
                    var tracker = objective.Definition.Tracker;
                    if (!Reads(tracker, subject)) continue;
                    var now = tracker.ProgressNow(_facts);
                    if (now.HasValue) SetProgress(record, objective, now.Value);
                }
                TryComplete(record, step);
            }

            if (record.State != QuestState.Active) return;
            if (Reads(quest.FailWhen, subject) && quest.FailWhen.Evaluate(_facts))
            {
                Fail(record);
                return;
            }
            foreach (var step in record.Steps)
            {
                if (step.State == StepState.Active && Reads(step.Definition.FailWhen, subject) && step.Definition.FailWhen.Evaluate(_facts))
                {
                    step.State = StepState.Failed;
                    EventBus<StepChangedEvent>.Raise(new StepChangedEvent(quest, step.Definition, StepState.Failed));
                    Fail(record);
                    return;
                }
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
            EventBus<StepChangedEvent>.Raise(new StepChangedEvent(record.Definition, step.Definition, StepState.Active));
            foreach (var objective in step.Objectives)
            {
                var now = objective.Definition.Tracker?.ProgressNow(_facts);
                if (now.HasValue) SetProgress(record, objective, now.Value);
            }
            Changed(record.Definition);
            TryComplete(record, step);
        }

        private void SetProgress(QuestRecord record, ObjectiveRecord objective, long value)
        {
            var clamped = (int)Math.Max(0, Math.Min(objective.Definition.TargetCount, value));
            if (clamped == objective.Progress) return;
            objective.Progress = clamped;
            EventBus<ObjectiveProgressedEvent>.Raise(new ObjectiveProgressedEvent(record.Definition, objective.Definition, clamped));
        }

        private void TryComplete(QuestRecord record, StepRecord step)
        {
            if (step.State != StepState.Active || !step.IsComplete) return;
            step.State = StepState.Done;
            EventBus<StepChangedEvent>.Raise(new StepChangedEvent(record.Definition, step.Definition, StepState.Done));
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
            record.State = QuestState.Done;
            record.Completions++;
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
            EventBus<QuestEndedEvent>.Raise(new QuestEndedEvent(record.Definition, QuestState.Done, record.Completions));
            Ended(record);
        }

        private void Fail(QuestRecord record)
        {
            record.State = QuestState.Failed;
            EventBus<QuestEndedEvent>.Raise(new QuestEndedEvent(record.Definition, QuestState.Failed, record.Completions));
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
            ActiveQuest = null;
        }

        private void RestoreFlag(FlagEntry entry)
        {
            if (entry == null) return;
            if (_assets != null && AssetId.TryParse(entry.Flag, out var id) && _assets.TryFind(id, out var asset) && asset is StoryFlag flag)
                _flags[flag] = entry.Value;
            else
                _unknownFlags.Add(entry);
        }

        private void RestoreQuest(QuestEntry entry)
        {
            if (entry == null) return;
            var record = _records.FirstOrDefault(candidate => IdOf(candidate.Definition) == entry.Quest);
            if (record == null) _unknownQuests.Add(entry);
            else record.Restore(entry);
        }

        private static string IdOf(QuestDefinition quest) => quest != null && !quest.AssetId.IsEmpty ? quest.AssetId.Value : null;

        private static bool TryIdOf(StoryFlag flag, out string id)
        {
            id = flag != null && !flag.AssetId.IsEmpty ? flag.AssetId.Value : null;
            return id != null;
        }

        #endregion
    }
}
