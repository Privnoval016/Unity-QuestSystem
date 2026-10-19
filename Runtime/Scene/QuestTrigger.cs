using System;
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

    /** <summary>A trigger volume that raises quest signals when something enters or leaves it: reaching a place, leaving an area.</summary> */
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

        #region MonoBehaviour Callbacks

        private void OnTriggerEnter(Collider other) => RaiseFor(other, onEnter);

        private void OnTriggerExit(Collider other) => RaiseFor(other, onExit);

        #endregion

        #region Public API

        /** <summary>Sets the trigger up in code, as tests and tools do.</summary> */
        public void Configure(QuestRuntime questRuntime, QuestSignal enter, QuestSignal exit = null, IQuestTriggerFilter triggerFilter = null)
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

        private void RaiseFor(Collider other, QuestSignal signal)
        {
            if (signal == null || !Counts(other)) return;
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
