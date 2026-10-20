using System;
using UnityEditor;
using Object = UnityEngine.Object;

namespace QuestSystem.EditorTools
{
    /** <summary>Runs a quest graph change as one named Undo step over every asset it touches.</summary> */
    internal static class EditScope
    {
        #region Public API

        /** <summary>Records the assets, runs the change, marks them changed, and folds it all into one Undo step.</summary> */
        public static void Run(string name, Object[] targets, Action change)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RecordObjects(targets, name);
            change();
            foreach (var target in targets)
                EditorUtility.SetDirty(target);
            Undo.CollapseUndoOperations(group);
        }

        #endregion
    }
}
