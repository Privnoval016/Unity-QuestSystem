using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>Draws a <see cref="StepIdPickerAttribute" /> field as a menu of its quest's steps.</summary> */
    [CustomPropertyDrawer(typeof(StepIdPickerAttribute))]
    public sealed class StepIdPickerDrawer : PropertyDrawer
    {
        #region Public API

        /** <inheritdoc /> */
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var questField = ((StepIdPickerAttribute)attribute).QuestField;
            var path = property.propertyPath;
            var siblingPath = path.Substring(0, path.Length - property.name.Length) + questField;
            var quest = property.serializedObject.FindProperty(siblingPath)?.objectReferenceValue as QuestDefinition;
            if (quest == null || property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var ids = new List<string>();
            var labels = new List<GUIContent>();
            for (var i = 0; i < quest.Steps.Count; i++)
            {
                var step = quest.Steps[i];
                if (step == null) continue;
                ids.Add(step.Id);
                labels.Add(new GUIContent($"{i + 1}. {(string.IsNullOrWhiteSpace(step.Text) ? step.Id : step.Text)}"));
            }
            var current = ids.IndexOf(property.stringValue);
            if (current < 0)
            {
                ids.Insert(0, property.stringValue);
                labels.Insert(0, new GUIContent(string.IsNullOrEmpty(property.stringValue) ? "(choose a step)" : $"(missing: {property.stringValue})"));
                current = 0;
            }
            label = EditorGUI.BeginProperty(position, label, property);
            var chosen = EditorGUI.Popup(position, label, current, labels.ToArray());
            if (chosen != current) property.stringValue = ids[chosen];
            EditorGUI.EndProperty();
        }

        #endregion
    }
}
