using System;
using System.Collections.Generic;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Every quest the editor knows, each with its summary, and who reads whom. Kept current one quest at a
     * time: a change reads only the quest that changed.</summary>
     */
    public sealed class QuestIndex
    {
        #region Private Fields

        /** <summary>Quests changed during a batch, announced when it ends.</summary> */
        private readonly HashSet<QuestDefinition> _batched = new HashSet<QuestDefinition>();
        private int _batchDepth;

        /** <summary>Keyed by quest, resolving to its summary.</summary> */
        private readonly Dictionary<QuestDefinition, QuestSummary> _summaries = new Dictionary<QuestDefinition, QuestSummary>();

        /** <summary>Keyed by quest, resolving to the links of other quests (and itself) that read it.</summary> */
        private readonly Dictionary<QuestDefinition, List<QuestLink>> _readers = new Dictionary<QuestDefinition, List<QuestLink>>();

        #endregion

        #region Properties

        /** <summary>Every quest known.</summary> */
        public IReadOnlyCollection<QuestDefinition> Quests => _summaries.Keys;

        /** <summary>How many times a quest has been read, for tests and measuring.</summary> */
        public int Reads { get; private set; }

        /** <summary>Raised after a quest is added, changed or removed; once per quest at the end of a batch.</summary> */
        public event Action<QuestDefinition> OnChanged = delegate { };

        #endregion

        #region Public API

        /** <summary>Holds change events until the returned scope ends, then raises one per changed quest.</summary> */
        public IDisposable Batch()
        {
            _batchDepth++;
            return new BatchScope(this);
        }

        /** <summary>A quest's summary; null when it isn't known.</summary> */
        public QuestSummary SummaryOf(QuestDefinition quest) => quest != null && _summaries.TryGetValue(quest, out var summary) ? summary : null;

        /** <summary>The links that read a quest: other quests' conditions, and its own.</summary> */
        public IReadOnlyList<QuestLink> ReadersOf(QuestDefinition quest) =>
            quest != null && _readers.TryGetValue(quest, out var readers) ? readers : (IReadOnlyList<QuestLink>)Array.Empty<QuestLink>();

        /** <summary>Reads a quest again, adding it if new; a destroyed quest is removed.</summary> */
        public void Refresh(QuestDefinition quest)
        {
            if (ReferenceEquals(quest, null)) return;
            if (quest == null)
            {
                Remove(quest);
                return;
            }
            Forget(quest);
            var summary = QuestSummary.Of(quest);
            Reads++;
            _summaries[quest] = summary;
            foreach (var link in summary.Reads)
            {
                if (!_readers.TryGetValue(link.Read, out var readers)) _readers[link.Read] = readers = new List<QuestLink>();
                readers.Add(link);
            }
            Announce(quest);
        }

        /** <summary>Forgets a quest. Other quests' links to it are kept, so they are back if the quest returns through Undo.</summary> */
        public void Remove(QuestDefinition quest)
        {
            if (ReferenceEquals(quest, null) || !_summaries.ContainsKey(quest)) return;
            Forget(quest);
            _summaries.Remove(quest);
            Announce(quest);
        }

        /** <summary>Forgets every quest whose asset is gone.</summary> */
        public void RemoveDestroyed()
        {
            var gone = new List<QuestDefinition>();
            foreach (var quest in _summaries.Keys)
                if (quest == null) gone.Add(quest);
            foreach (var quest in gone)
                Remove(quest);
        }

        #endregion

        #region Helpers

        private void Announce(QuestDefinition quest)
        {
            if (_batchDepth > 0) _batched.Add(quest);
            else OnChanged(quest);
        }

        private void EndBatch()
        {
            if (--_batchDepth > 0) return;
            var changed = new List<QuestDefinition>(_batched);
            _batched.Clear();
            foreach (var quest in changed)
                OnChanged(quest);
        }

        /** <summary>Ends a batch when disposed.</summary> */
        private sealed class BatchScope : IDisposable
        {
            private QuestIndex _index;

            public BatchScope(QuestIndex index) => _index = index;

            public void Dispose()
            {
                _index?.EndBatch();
                _index = null;
            }
        }

        /** <summary>Takes a quest's links out of the readers of the quests it read.</summary> */
        private void Forget(QuestDefinition quest)
        {
            if (!_summaries.TryGetValue(quest, out var old)) return;
            foreach (var link in old.Reads)
                if (_readers.TryGetValue(link.Read, out var readers)) readers.Remove(link);
        }

        #endregion
    }
}
