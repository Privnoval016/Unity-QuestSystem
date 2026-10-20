using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>A short message at the bottom of the canvas, never a dialog: what just happened, with jump buttons and Undo.</summary> */
    public sealed class Notice : VisualElement
    {
        #region Private Fields

        private const long ShowMilliseconds = 7000;
        private IVisualElementScheduledItem _hide;

        #endregion

        #region Public API

        /** <summary>Makes a hidden notice.</summary> */
        public Notice()
        {
            AddToClassList("quest-graph__notice");
            pickingMode = PickingMode.Position;
            style.display = DisplayStyle.None;
        }

        /** <summary>Shows a message, with optional buttons, and an Undo button when asked.</summary> */
        public void Show(string message, bool offerUndo, params (string label, Action onClick)[] buttons)
        {
            Clear();
            Add(new Label(message));
            foreach (var (label, onClick) in buttons)
            {
                var button = new Button(onClick) { text = label };
                button.AddToClassList("quest-graph__ref");
                Add(button);
            }
            if (offerUndo)
            {
                var undo = new Button(() =>
                {
                    Undo.PerformUndo();
                    Hide();
                }) { text = "Undo" };
                Add(undo);
            }
            style.display = DisplayStyle.Flex;
            _hide?.Pause();
            _hide = schedule.Execute(Hide).StartingIn(ShowMilliseconds);
        }

        /** <summary>Hides the notice.</summary> */
        public void Hide() => style.display = DisplayStyle.None;

        #endregion
    }
}
