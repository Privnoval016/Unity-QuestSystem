using UnityEditor;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
#endif

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The quest's Inspector: the whole quest, or the one step the quest graph has selected. Drawn by Odin
     * where it is installed, and by Unity's own fields where it isn't.</summary>
     */
    [CustomEditor(typeof(QuestDefinition))]
    public sealed class QuestDefinitionEditor : Editor
    {
        #region Private Fields

        /** <summary>The serialized name of the quest's step list.</summary> */
        private const string StepsField = "steps";

#if ODIN_INSPECTOR
        private PropertyTree _tree;
#endif

        #endregion

        #region MonoBehaviour Callbacks

        private void OnDisable()
        {
#if ODIN_INSPECTOR
            _tree?.Dispose();
            _tree = null;
#endif
        }

        #endregion

        #region Public API

        /** <inheritdoc /> */
        public override void OnInspectorGUI()
        {
            var quest = (QuestDefinition)target;
            var path = StepPath(quest, QuestGraphFocus.StepOf(quest));
            if (path != null) DrawStepHeader(quest);
#if ODIN_INSPECTOR
            _tree ??= PropertyTree.Create(serializedObject);
            _tree.UpdateTree();
            if (path == null) _tree.Draw(false);
            else
            {
                _tree.BeginDraw(false);
                try
                {
                    _tree.GetPropertyAtUnityPath(path)?.Draw();
                }
                finally
                {
                    _tree.EndDraw();
                }
            }
#else
            serializedObject.Update();
            if (path == null) DrawDefaultInspector();
            else EditorGUILayout.PropertyField(serializedObject.FindProperty(path), true);
            serializedObject.ApplyModifiedProperties();
#endif
        }

        #endregion

        #region Helpers

        /** <summary>The serialized path of a step, such as "steps.Array.data[2]"; null when there is no such step.</summary> */
        private static string StepPath(QuestDefinition quest, string stepId)
        {
            if (stepId == null) return null;
            for (var i = 0; i < quest.Steps.Count; i++)
                if (quest.Steps[i] != null && quest.Steps[i].Id == stepId) return $"{StepsField}.Array.data[{i}]";
            return null;
        }

        private static void DrawStepHeader(QuestDefinition quest)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var step = quest.FindStep(QuestGraphFocus.StepOf(quest));
                EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(step.Text) ? step.Id : step.Text, EditorStyles.boldLabel);
                if (GUILayout.Button("Whole quest", EditorStyles.miniButton, GUILayout.Width(90f))) QuestGraphFocus.Focus(quest, null);
            }
            EditorGUILayout.Space(2f);
        }

        #endregion
    }
}
