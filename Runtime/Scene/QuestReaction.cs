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

    /** <summary>Makes the scene follow quest state: while its condition holds, its effects are on; otherwise off.</summary> */
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

        private readonly HashSet<IConditionSubject> _reads = new HashSet<IConditionSubject>();
        private QuestService _service;
        private bool? _applied;
        private bool _readsKnown;

        #endregion

        #region Properties

        /** <summary>True while the effects are on.</summary> */
        public bool IsOn => _applied == true;

        #endregion

        #region MonoBehaviour Callbacks

        private void OnEnable()
        {
            _reads.Clear();
            _readsKnown = ConditionSubjects.TryCollect(when, _reads);
            if (runtime != null) runtime.OnServiceChanged += HandleServiceChanged;
            Attach(runtime != null ? runtime.Service : null);
        }

        private void OnDisable()
        {
            if (runtime != null) runtime.OnServiceChanged -= HandleServiceChanged;
            Attach(null);
        }

        #endregion

        #region Public API

        /** <summary>Sets the reaction up in code, as tests and tools do. Call before it is enabled.</summary> */
        public void Configure(QuestRuntime questRuntime, ICondition<IFacts> condition, IEnumerable<IQuestReactionEffect> reactionEffects)
        {
            runtime = questRuntime;
            when = condition;
            effects = new List<IQuestReactionEffect>(reactionEffects ?? Array.Empty<IQuestReactionEffect>());
        }

        /** <summary>Checks the condition now and applies or undoes the effects if it changed.</summary> */
        public void Refresh()
        {
            if (_service == null || when == null) return;
            var on = when.Evaluate(_service.Facts);
            if (_applied == on) return;
            _applied = on;
            foreach (var effect in effects)
                effect?.Apply(on);
        }

        #endregion

        #region Event Handlers

        private void HandleServiceChanged(QuestService service) => Attach(service);

        private void HandleSubjectChanged(IConditionSubject subject)
        {
            if (!_readsKnown || _reads.Contains(subject)) Refresh();
        }

        private void HandleReset()
        {
            _applied = null;
            Refresh();
        }

        #endregion

        #region Helpers

        private void Attach(QuestService service)
        {
            if (_service != null)
            {
                _service.Facts.OnSubjectChanged -= HandleSubjectChanged;
                _service.OnReset -= HandleReset;
            }
            _service = service;
            _applied = null;
            if (_service == null) return;
            _service.Facts.OnSubjectChanged += HandleSubjectChanged;
            _service.OnReset += HandleReset;
            Refresh();
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
