using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>Copied steps waiting to be pasted, as deep copies held in memory so references to unsaved objects
     * survive. The system clipboard holds only a token naming them. A domain reload empties it.</summary>
     */
    internal sealed class StepClipboard : ScriptableObject
    {
        #region Inspector Fields

        [Tooltip("The quest the steps came from.")]
        [SerializeField] private QuestDefinition source;

        [Tooltip("The copied steps.")]
        [SerializeField] private List<QuestStep> steps = new List<QuestStep>();

        #endregion

        #region Private Fields

        private static StepClipboard _held;
        private static string _token;

        #endregion

        #region Properties

        /** <summary>The quest the steps came from.</summary> */
        public QuestDefinition Source
        {
            get => source;
            set => source = value;
        }

        /** <summary>The steps.</summary> */
        public List<QuestStep> Steps => steps;

        #endregion

        #region Public API

        /** <summary>What the system clipboard text starts with while steps are held.</summary> */
        public const string Prefix = "QuestSteps:";

        /** <summary>Holds copies and returns the token for the system clipboard; the previous copies are let go.</summary> */
        public static string Hold(StepClipboard copies)
        {
            Clear();
            copies.hideFlags = HideFlags.HideAndDontSave;
            _held = copies;
            _token = Prefix + Guid.NewGuid().ToString("N");
            return _token;
        }

        /** <summary>Fresh copies of the held steps for a paste, when the text is the current token; null otherwise.</summary> */
        public static StepClipboard Take(string text) => _held != null && text != null && text == _token ? Instantiate(_held) : null;

        /** <summary>True when the text is the current token.</summary> */
        public static bool Holds(string text) => _held != null && text != null && text == _token;

        /** <summary>True when the text looks like a token, current or from before a reload, so a failed paste can say why.</summary> */
        public static bool LooksLikeToken(string text) => text != null && text.StartsWith(Prefix, StringComparison.Ordinal);

        /** <summary>Lets the held steps go.</summary> */
        public static void Clear()
        {
            if (_held != null) DestroyImmediate(_held);
            _held = null;
            _token = null;
        }

        #endregion

        #region Helpers

        /** <summary>Destroys copies left from before a domain reload, which nothing refers to any more.</summary> */
        [InitializeOnLoadMethod]
        private static void DestroyOrphans()
        {
            foreach (var orphan in Resources.FindObjectsOfTypeAll<StepClipboard>())
                if (orphan != _held) Object.DestroyImmediate(orphan);
        }

        #endregion
    }
}
