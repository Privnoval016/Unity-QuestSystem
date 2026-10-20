using System.Collections.Generic;
using System.Linq;
using Extensions.Menus;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace QuestSystem.EditorTools
{
    /** <summary>Runs quest validation from the menu, and before every build.</summary> */
    public sealed class QuestValidationMenu : IPreprocessBuildWithReport
    {
        #region Properties

        /** <inheritdoc /> */
        public int callbackOrder => 2;

        #endregion

        #region Public API

        /** <summary>Every quest asset in the project.</summary> */
        public static List<QuestDefinition> FindQuests() => AssetDatabase.FindAssets("t:" + nameof(QuestDefinition))
            .Select(guid => AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(quest => quest != null)
            .ToList();

        /** <inheritdoc /> */
        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = QuestValidator.ValidateAll(FindQuests()).Where(issue => issue.Level == QuestIssueLevel.Error).ToList();
            if (errors.Count > 0) throw new BuildFailedException("Quests: " + string.Join("; ", errors));
        }

        #endregion

        #region Helpers

        [MenuItem(QuestMenus.Tools + "Validate Quests", priority = ToolMenus.Commands)]
        private static void ValidateFromMenu()
        {
            var issues = QuestValidator.ValidateAll(FindQuests());
            if (issues.Count == 0) Debug.Log("Quests: every quest is valid.");
            foreach (var issue in issues)
            {
                if (issue.Level == QuestIssueLevel.Error) Debug.LogError($"Quests: {issue}", issue.Quest);
                else Debug.LogWarning($"Quests: {issue}", issue.Quest);
            }
        }

        #endregion
    }
}
