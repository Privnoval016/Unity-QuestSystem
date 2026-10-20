using System.Collections.Generic;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>Copied steps on their way to a paste: the steps, and the quest they came from.</summary> */
    internal sealed class StepClipboard : ScriptableObject
    {
        #region Inspector Fields

        [SerializeField] private QuestDefinition source;
        [SerializeField] private List<QuestStep> steps = new List<QuestStep>();

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
    }
}
