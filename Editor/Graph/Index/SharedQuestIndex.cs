using UnityEditor;

namespace QuestSystem.EditorTools
{
    /**
     * <summary>The editor's one quest index: every quest asset, found once per session and kept current by asset
     * imports, deletions and in-editor changes (Undo included).</summary>
     */
    public static class SharedQuestIndex
    {
        #region Private Fields

        private static QuestIndex _index;

        #endregion

        #region Properties

        /** <summary>How many full project scans have run this session; one, unless the domain reloads.</summary> */
        public static int Scans { get; private set; }

        #endregion

        #region Public API

        /** <summary>The index, scanning the project the first time it is asked for.</summary> */
        public static QuestIndex Get()
        {
            if (_index != null) return _index;
            _index = new QuestIndex();
            Scans++;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(QuestDefinition)))
                _index.Refresh(AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            ObjectChangeEvents.changesPublished += HandleChanges;
            return _index;
        }

        #endregion

        #region Event Handlers

        private static void HandleChanges(ref ObjectChangeEventStream stream)
        {
            for (var i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var changed);
                        Refresh(EditorUtility.EntityIdToObject(changed.entityId) as QuestDefinition);
                        break;
                    case ObjectChangeKind.CreateAssetObject:
                        stream.GetCreateAssetObjectEvent(i, out var created);
                        Refresh(EditorUtility.EntityIdToObject(created.entityId) as QuestDefinition);
                        break;
                    case ObjectChangeKind.DestroyAssetObject:
                        _index?.RemoveDestroyed();
                        break;
                }
            }
        }

        #endregion

        #region Helpers

        private static void Refresh(QuestDefinition quest)
        {
            if (quest != null) _index.Refresh(quest);
        }

        /** <summary>Keeps the index current when assets change on disk: a pull, a reimport, a file moved or deleted.</summary> */
        private sealed class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (_index == null) return;
                foreach (var path in imported)
                    if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(QuestDefinition)) Refresh(AssetDatabase.LoadAssetAtPath<QuestDefinition>(path));
                if (deleted.Length > 0) _index.RemoveDestroyed();
            }
        }

        #endregion
    }
}
