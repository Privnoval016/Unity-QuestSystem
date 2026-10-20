using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace QuestSystem.EditorTools
{
    /** <summary>Gives a quest graph element its stylesheet and the editor skin's colours.</summary> */
    public static class QuestGraphStyles
    {
        #region Private Fields

        /** <summary>The stylesheet's file name, matched exactly so a similarly named sheet is never picked.</summary> */
        private const string SheetName = "QuestGraph";

        private static StyleSheet _sheet;

        #endregion

        #region Public API

        /** <summary>Adds the stylesheet and the skin class to a root element.</summary> */
        public static void Apply(VisualElement root)
        {
            if (_sheet == null)
            {
                var path = AssetDatabase.FindAssets($"{SheetName} t:StyleSheet").Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(candidate => Path.GetFileNameWithoutExtension(candidate) == SheetName);
                if (path != null) _sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            if (_sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
            root.EnableInClassList("quest-graph--dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("quest-graph--light", !EditorGUIUtility.isProSkin);
        }

        #endregion
    }
}
