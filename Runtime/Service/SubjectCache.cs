using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Extensions.Logic;

namespace QuestSystem
{
    /** <summary>Remembers what each condition and tracker reads, so a change checks only what it can affect.</summary> */
    internal sealed class SubjectCache
    {
        #region Private Fields

        /** <summary>Keyed by condition, by reference, resolving to what it reads.</summary> */
        private readonly Dictionary<ICondition<IFacts>, ReadSet> _conditions = new Dictionary<ICondition<IFacts>, ReadSet>(new ByReference<ICondition<IFacts>>());

        /** <summary>Keyed by tracker, by reference, resolving to what it reads.</summary> */
        private readonly Dictionary<ObjectiveTracker, ReadSet> _trackers = new Dictionary<ObjectiveTracker, ReadSet>(new ByReference<ObjectiveTracker>());

        #endregion

        #region Public API

        /** <summary>True when the condition reads the subject, or can't say what it reads.</summary> */
        public bool Reads(ICondition<IFacts> condition, IConditionSubject subject) =>
            Lookup(_conditions, condition, subjects => ConditionSubjects.TryCollect(condition, subjects)).Includes(subject);

        /** <summary>True when the tracker reads the subject, or can't say what it reads.</summary> */
        public bool Reads(ObjectiveTracker tracker, IConditionSubject subject) =>
            Lookup(_trackers, tracker, tracker.TryCollectSubjects).Includes(subject);

        #endregion

        #region Helpers

        private static ReadSet Lookup<T>(Dictionary<T, ReadSet> cache, T reader, Func<ICollection<IConditionSubject>, bool> collect)
        {
            if (cache.TryGetValue(reader, out var reads)) return reads;
            var subjects = new HashSet<IConditionSubject>();
            reads = new ReadSet(collect(subjects), subjects);
            cache.Add(reader, reads);
            return reads;
        }

        /** <summary>What one condition or tracker reads; everything, when it can't say.</summary> */
        private readonly struct ReadSet
        {
            private readonly bool _known;
            private readonly HashSet<IConditionSubject> _subjects;

            public ReadSet(bool known, HashSet<IConditionSubject> subjects)
            {
                _known = known;
                _subjects = subjects;
            }

            public bool Includes(IConditionSubject subject) => !_known || _subjects.Contains(subject);
        }

        /** <summary>Compares by reference, whatever a type's own equality says.</summary> */
        private sealed class ByReference<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        #endregion
    }
}
