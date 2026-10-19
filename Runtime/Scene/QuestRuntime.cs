using System;
using UnityEngine;

namespace QuestSystem
{
    /**
     * <summary>The asset scene objects reach the running quest service through. The game binds its service to it when
     * the game starts; triggers and reactions in any scene reference this asset.</summary>
     */
    [CreateAssetMenu(menuName = "Quests/Quest Runtime", fileName = "QuestRuntime")]
    public sealed class QuestRuntime : ScriptableObject
    {
        #region Properties

        /** <summary>The running service; null when none is bound.</summary> */
        public QuestService Service { get; private set; }

        /** <summary>Raised when a service is bound or unbound.</summary> */
        public event Action<QuestService> OnServiceChanged = delegate { };

        #endregion

        #region MonoBehaviour Callbacks

        private void OnEnable() => Service = null;

        private void OnDisable() => Service = null;

        #endregion

        #region Public API

        /** <summary>Binds the running service; null unbinds it.</summary> */
        public void Bind(QuestService service)
        {
            if (Service == service) return;
            Service = service;
            OnServiceChanged(service);
        }

        /** <summary>Unbinds the service, if it is the one bound.</summary> */
        public void Unbind(QuestService service)
        {
            if (Service == service) Bind(null);
        }

        #endregion
    }
}
