using System;
using SaveSystem;

namespace QuestSystem
{
    /**
     * <summary>Saves the quest service with Unity-SaveSystem. Register it with the game's save service; a new game
     * restores an empty save, which clears every quest and flag.</summary>
     * <remarks>This is the only part of the quest system that needs the save system: delete this folder if a project doesn't save quests.</remarks>
     */
    public sealed class QuestSaveSection : SaveSection<QuestSaveData>
    {
        #region Private Fields

        private readonly QuestService _quests;

        #endregion

        #region Properties

        /** <summary>The key quests save under unless given another.</summary> */
        public const string DefaultKey = "quests";

        /** <inheritdoc /> */
        public override string Key { get; }

        #endregion

        #region Public API

        /** <summary>A section for the quest service, under a key that must never change once saves exist.</summary> */
        public QuestSaveSection(QuestService quests, string key = DefaultKey)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            Key = key;
        }

        #endregion

        #region Helpers

        /** <inheritdoc /> */
        protected override QuestSaveData Capture() => _quests.Capture();

        /** <inheritdoc /> */
        protected override void Restore(QuestSaveData data) => _quests.Restore(data);

        #endregion
    }
}
