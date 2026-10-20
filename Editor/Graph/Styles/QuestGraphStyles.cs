using UnityEditor;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>Gives a quest graph element its stylesheet and the editor skin's colours.</summary> */
    public static class QuestGraphStyles
    {
        #region Private Fields

        private static StyleSheet _sheet;

        #endregion

        #region Public API

        /** <summary>Adds the stylesheet and the skin class to a root element.</summary> */
        public static void Apply(VisualElement root)
        {
            if (_sheet == null)
            {
                var guids = AssetDatabase.FindAssets("QuestGraph t:StyleSheet");
                if (guids.Length > 0) _sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
            root.EnableInClassList("quest-graph--dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("quest-graph--light", !EditorGUIUtility.isProSkin);
        }

        #endregion
    }
}
