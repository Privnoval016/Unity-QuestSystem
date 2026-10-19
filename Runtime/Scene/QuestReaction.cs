using System;
using System.Collections.Generic;
using Extensions.Inspector;
using Extensions.Logic;
using UnityEngine;

namespace QuestSystem
{
    /** <summary>Something a <see cref="QuestReaction" /> does to the scene, and undoes.</summary> */
    public interface IQuestReactionEffect
    {
        /** <summary>Applies the effect (on) or undoes it (off).</summary> */
        void Apply(bool on);
    }

    /**
     * <summary>Makes the scene follow quest state: while its condition holds, its effects are on; otherwise off.</summary>
     * <remarks>
     * It listens from Awake to OnDestroy, not while enabled, so its effects may turn off its own object and still turn
     * it back on. Awake never runs on an object that starts inactive, so put the reaction on an object that starts
     * active and point its effects at what should change.
     * </remarks>
     */
    [DisallowMultipleComponent]
    public sealed class QuestReaction : MonoBehaviour
    {
        #region Inspector Fields

        [Tooltip("The quest runtime the game binds its quest service to.")]
        [SerializeField] private QuestRuntime runtime;

        [Tooltip("While this holds, the effects are on: a quest done, a step active, a flag set.")]
        [SerializeReference, SubclassPicker] private ICondition<IFacts> when;

        [Tooltip("What changes in the scene.")]
        [SerializeReference, SubclassPicker] private List<IQuestReactionEffect> effects = new List<IQuestReactionEffect>();

        #endregion

        #region Private Fields

        private QuestService _service;
        private ConditionWatcher _watcher;

        #endregion

        #region Properties

        /** <summary>True while the effects are on.</summary> */
        public bool IsOn => _watcher?.Holds == true;

        #endregion

        #region MonoBehaviour Callbacks

        private void Awake() => Hook();

        private void OnDestroy() => Unhook();

        #endregion

        #region Public API

        /** <summary>Checks the condition now and applies or undoes the effects if it changed.</summary> */
        public void Refresh() => _watcher?.Refresh();

        /** <summary>Sets the reaction up in code, as tests do, and checks it at once.</summary> */
        internal void Configure(QuestRuntime questRuntime, ICondition<IFacts> condition, IEnumerable<IQuestReactionEffect> reactionEffects)
        {
            Unhook();
            runtime = questRuntime;
            when = condition;
            effects = new List<IQuestReactionEffect>(reactionEffects ?? Array.Empty<IQuestReactionEffect>());
            Hook();
        }

        #endregion

        #region Event Handlers

        private void HandleServiceChanged(QuestService service) => Attach(service);

        private void HandleReset() => _watcher?.Reapply();

        private void HandleHoldsChanged(bool on)
        {
            foreach (var effect in effects)
                effect?.Apply(on);
        }

        #endregion

        #region Helpers

        private void Hook()
        {
            if (runtime == null) return;
            runtime.OnServiceChanged += HandleServiceChanged;
            Attach(runtime.Service);
        }

        private void Unhook()
        {
            if (runtime != null) runtime.OnServiceChanged -= HandleServiceChanged;
            Attach(null);
        }

        private void Attach(QuestService service)
        {
            if (_service != null) _service.OnReset -= HandleReset;
            _watcher?.Dispose();
            _watcher = null;
            _service = service;
            if (_service == null || when == null) return;
            _service.OnReset += HandleReset;
            _watcher = new ConditionWatcher(when, _service.Facts, HandleHoldsChanged);
        }

        #endregion
    }

    /** <summary>Turns a GameObject on or off.</summary> */
    [Serializable]
    public sealed class SetActiveEffect : IQuestReactionEffect
    {
        [Tooltip("The object to turn on or off.")]
        [SerializeField] private GameObject target;

        [Tooltip("On: the object is active while the condition holds. Off: inactive while it holds.")]
        [SerializeField] private bool activeWhenOn = true;

        /** <summary>An empty effect, for the Inspector.</summary> */
        public SetActiveEffect() { }

        /** <summary>An effect on a GameObject, as code builds one.</summary> */
        public SetActiveEffect(GameObject target, bool activeWhenOn = true)
        {
            this.target = target;
            this.activeWhenOn = activeWhenOn;
        }

        /** <inheritdoc /> */
        public void Apply(bool on)
        {
            if (target != null) target.SetActive(on == activeWhenOn);
        }
    }

    /** <summary>Enables or disables a component.</summary> */
    [Serializable]
    public sealed class EnableBehaviourEffect : IQuestReactionEffect
    {
        [Tooltip("The component to enable or disable.")]
        [SerializeField] private Behaviour target;

        [Tooltip("On: enabled while the condition holds. Off: disabled while it holds.")]
        [SerializeField] private bool enabledWhenOn = true;

        /** <summary>An empty effect, for the Inspector.</summary> */
        public EnableBehaviourEffect() { }

        /** <summary>An effect on a component, as code builds one.</summary> */
        public EnableBehaviourEffect(Behaviour target, bool enabledWhenOn = true)
        {
            this.target = target;
            this.enabledWhenOn = enabledWhenOn;
        }

        /** <inheritdoc /> */
        public void Apply(bool on)
        {
            if (target != null) target.enabled = on == enabledWhenOn;
        }
    }
}
