using System.Collections.Generic;
using Extensions.Inspector;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>Decides which colliders a <see cref="QuestTrigger" /> counts; a game writes its own (the player only, say).</summary> */
    public interface IQuestTriggerFilter
    {
        /** <summary>True when the collider counts.</summary> */
        bool Accepts(Collider other);
    }

    /**
     * <summary>A trigger volume that raises quest signals when something enters or leaves it: reaching a place, leaving an area.</summary>
     * <remarks>
     * It counts bodies, not colliders: a body is its rigidbody, or the collider's own object without one. A body with
     * several colliders enters once, when the first is inside, and leaves once, when the last is out. A collider
     * disabled or destroyed inside is never reported as leaving, as with any Unity trigger.
     * </remarks>
     */
    [DisallowMultipleComponent]
    public sealed class QuestTrigger : MonoBehaviour
    {
        #region Inspector Fields

        [Tooltip("The quest runtime the game binds its quest service to.")]
        [SerializeField] private QuestRuntime runtime;

        [Tooltip("Raised when something counted enters. Optional.")]
        [SerializeField] private QuestSignal onEnter;

        [Tooltip("Raised when something counted leaves. Optional.")]
        [SerializeField] private QuestSignal onExit;

        [Tooltip("Only colliders on these layers count.")]
        [SerializeField] private LayerMask layers = ~0;

        [Tooltip("A further check on what counts. Empty: everything on the layers.")]
        [SerializeReference, SubclassPicker] private IQuestTriggerFilter filter;

        #endregion

        #region Private Fields

        /** <summary>Keyed by body, resolving to how many of its counted colliders are inside.</summary> */
        private readonly Dictionary<GameObject, int> _inside = new Dictionary<GameObject, int>();

        #endregion

        #region MonoBehaviour Callbacks

        private void OnDisable() => _inside.Clear();

        private void OnTriggerEnter(Collider other)
        {
            if (!Counts(other)) return;
            var body = BodyOf(other);
            _inside.TryGetValue(body, out var count);
            _inside[body] = count + 1;
            if (count == 0) Raise(onEnter);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!Counts(other)) return;
            var body = BodyOf(other);
            if (!_inside.TryGetValue(body, out var count)) return;
            if (count > 1)
            {
                _inside[body] = count - 1;
                return;
            }
            _inside.Remove(body);
            Raise(onExit);
        }

        #endregion

        #region Public API

        /** <summary>Sets the trigger up in code, as tests do.</summary> */
        internal void Configure(QuestRuntime questRuntime, QuestSignal enter, QuestSignal exit = null, IQuestTriggerFilter triggerFilter = null)
        {
            runtime = questRuntime;
            onEnter = enter;
            onExit = exit;
            filter = triggerFilter;
        }

        /** <summary>True when the collider counts: on the layers, and accepted by the filter.</summary> */
        public bool Counts(Collider other) =>
            other != null && (layers.value & (1 << other.gameObject.layer)) != 0 && (filter == null || filter.Accepts(other));

        #endregion

        #region Helpers

        private static GameObject BodyOf(Collider other) => other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;

        private void Raise(QuestSignal signal)
        {
            if (signal == null) return;
            if (runtime == null || runtime.Service == null)
            {
                Debug.LogWarning($"Quests: {name} has no running quest service.", this);
                return;
            }
            runtime.Service.Raise(signal);
        }

        #endregion
    }
}
