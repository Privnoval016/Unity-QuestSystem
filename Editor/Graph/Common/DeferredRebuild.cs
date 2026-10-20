using System;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Redraws an element once on the next update however often it is asked, and not at all while it is hidden
     * (showing it redraws it anyway). Many index changes at once cost one redraw.</summary>
     */
    public sealed class DeferredRebuild
    {
        #region Private Fields

        private readonly VisualElement _owner;
        private readonly Action _rebuild;
        private bool _scheduled;

        #endregion

        #region Public API

        /** <summary>Makes a deferred redraw of an element.</summary> */
        public DeferredRebuild(VisualElement owner, Action rebuild)
        {
            _owner = owner;
            _rebuild = rebuild;
        }

        /** <summary>Asks for a redraw on the next update, unless one is already asked for or the element is hidden.</summary> */
        public void Request()
        {
            if (_scheduled || _owner.style.display == DisplayStyle.None) return;
            _scheduled = true;
            _owner.schedule.Execute(Run);
        }

        /** <summary>Drops a redraw asked for, because the element is redrawing now.</summary> */
        public void Cancel() => _scheduled = false;

        #endregion

        #region Helpers

        private void Run()
        {
            if (!_scheduled) return;
            _scheduled = false;
            _rebuild();
        }

        #endregion
    }
}
